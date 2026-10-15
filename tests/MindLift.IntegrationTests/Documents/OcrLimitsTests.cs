using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using MindLift.Infrastructure.Documents.Text;
using Shouldly;
using SkiaSharp;

namespace MindLift.IntegrationTests.Documents;

/// <summary>
/// Reading a document is bounded: how many are read at once, how long one may take, how many pixels a page or picture may have, and how much text is read.
/// These tests use a stand-in for Tesseract, so they need neither the program nor a database.
/// </summary>
public sealed class OcrLimitsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mindlift-ocr-limits-{Guid.NewGuid():N}");

    public OcrLimitsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Left for the system to clean up.
        }
    }

    /// <summary>Stands in for Tesseract: records what it was asked to read and answers as told.</summary>
    private sealed class FakeEngine : IOcrEngine
    {
        public List<(int Width, int Height)> Pictures { get; } = [];
        public Func<CancellationToken, Task>? Wait { get; init; }
        public IReadOnlyList<OcrWord> Words { get; init; } = [];

        public bool IsAvailable() => true;

        public async Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest request, CancellationToken ct)
        {
            using (var codec = SKCodec.Create(imagePath))
            {
                lock (Pictures)
                {
                    Pictures.Add((codec!.Info.Width, codec.Info.Height));
                }
            }

            if (Wait is not null)
            {
                await Wait(ct);
            }

            return new OcrPageResult(Words, 0, 0);
        }
    }

    private static DocumentPageReader Reader(OcrOptions options, IOcrEngine engine, OcrGate? gate = null)
        => new(engine, Options.Create(options), NullLogger<DocumentPageReader>.Instance, gate);

    private string Png(int width, int height)
    {
        var path = Path.Combine(_folder, $"picture-{Guid.NewGuid():N}.png");
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    /// <summary>A PDF with no text on its pages, so every page counts as a scan.</summary>
    private string BlankPdf(float width, float height)
    {
        var path = Path.Combine(_folder, $"blank-{Guid.NewGuid():N}.pdf");
        using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream);
        using var canvas = document.BeginPage(width, height);
        using var paint = new SKPaint { Color = SKColors.LightGray };
        canvas.DrawRect(new SKRect(10, 10, 50, 50), paint);
        document.EndPage();
        document.Close();
        return path;
    }

    /// <summary>A PDF with real text on every page.</summary>
    private string DigitalPdf(int pages)
    {
        var path = Path.Combine(_folder, $"digital-{Guid.NewGuid():N}.pdf");
        using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream);
        for (var number = 1; number <= pages; number++)
        {
            using var canvas = document.BeginPage(900, 300);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 30);
            canvas.DrawText($"Page number {number} of the test file", 40, 70, font, paint);
            canvas.DrawText("Some more text to be sure it is read", 40, 150, font, paint);
            document.EndPage();
        }

        document.Close();
        return path;
    }

    // ---------- pictures ----------

    [Fact]
    public async Task A_picture_with_too_many_pixels_is_refused_before_it_is_read()
    {
        var engine = new FakeEngine();
        var reader = Reader(new OcrOptions { MaxImagePixels = 10_000 }, engine);

        await Should.ThrowAsync<InvalidDataException>(() => reader.ReadAsync(Png(200, 200), CancellationToken.None));

        engine.Pictures.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_that_is_not_a_picture_is_refused_before_it_is_read()
    {
        var path = Path.Combine(_folder, "not-a-picture.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var engine = new FakeEngine();

        await Should.ThrowAsync<InvalidDataException>(() => Reader(new OcrOptions(), engine).ReadAsync(path, CancellationToken.None));

        engine.Pictures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Word_positions_are_fractions_of_the_picture()
    {
        var engine = new FakeEngine { Words = [new OcrWord("Hello", 95, 100, 50, 200, 20, 1, 1, 1)] };

        var page = (await Reader(new OcrOptions(), engine).ReadAsync(Png(1000, 500), CancellationToken.None)).Single();

        var word = page.Words.Single();
        word.X.ShouldBe(0.1, 0.0001);
        word.Y.ShouldBe(0.1, 0.0001);
        word.Width.ShouldBe(0.2, 0.0001);
        word.Height.ShouldBe(0.04, 0.0001);
    }

    // ---------- PDF pages ----------

    [Theory]
    [InlineData(595, 842, 300)]     // A4 stays at the full OCR resolution.
    [InlineData(2384, 3370, 160)]   // A0 is rendered lower so the picture stays within 40 million pixels.
    [InlineData(14400, 14400, 0)]   // 200 inches square would need less than the lowest resolution allowed.
    [InlineData(0, 842, 0)]
    [InlineData(595, -1, 0)]
    public void The_render_resolution_keeps_a_page_within_the_pixel_limit(double width, double height, int expectedDpi)
    {
        var dpi = Reader(new OcrOptions(), new FakeEngine()).RenderDpiFor(width, height);

        if (expectedDpi == 160)
        {
            dpi.ShouldBeInRange(150, 170);
        }
        else
        {
            dpi.ShouldBe(expectedDpi);
        }
    }

    [Fact]
    public async Task A_huge_pdf_page_is_skipped_without_being_rendered()
    {
        var engine = new FakeEngine();

        var pages = await Reader(new OcrOptions(), engine).ReadAsync(BlankPdf(14400, 14400), CancellationToken.None);

        var page = pages.Single();
        page.Source.ShouldBe("Skipped");
        page.Text.ShouldBeEmpty();
        engine.Pictures.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_large_pdf_page_is_rendered_at_a_lower_resolution_to_stay_within_the_pixel_limit()
    {
        var engine = new FakeEngine();
        var options = new OcrOptions { OcrDpi = 300, MaxPagePixels = 2_000_000, MinRenderDpi = 50 };

        await Reader(options, engine).ReadAsync(BlankPdf(595, 842), CancellationToken.None);

        var (width, height) = engine.Pictures.Single();
        ((long)width * height).ShouldBeLessThanOrEqualTo(2_100_000);
        width.ShouldBeLessThan(2480); // Not the 300 dpi width of an A4 page.
    }

    [Fact]
    public async Task A_pdf_over_the_page_limit_is_read_only_up_to_the_limit()
    {
        var pages = await Reader(new OcrOptions { MaxPages = 2 }, new FakeEngine()).ReadAsync(DigitalPdf(5), CancellationToken.None);

        pages.Select(p => p.PageNumber).ShouldBe([1, 2]);
    }

    // ---------- text files ----------

    [Fact]
    public async Task A_text_file_longer_than_the_limit_is_cut_at_the_limit()
    {
        var path = Path.Combine(_folder, "long.txt");
        await File.WriteAllTextAsync(path, new string('a', 5000));

        var page = (await Reader(new OcrOptions { MaxTextCharacters = 1000 }, new FakeEngine()).ReadAsync(path, CancellationToken.None)).Single();

        page.Text.Length.ShouldBe(1000);
    }

    [Fact]
    public async Task A_short_text_file_is_read_whole()
    {
        var path = Path.Combine(_folder, "short.txt");
        await File.WriteAllTextAsync(path, "License No: CN-1234567");

        var page = (await Reader(new OcrOptions(), new FakeEngine()).ReadAsync(path, CancellationToken.None)).Single();

        page.Text.ShouldBe("License No: CN-1234567");
    }

    // ---------- time and numbers ----------

    [Fact]
    public async Task A_document_that_takes_longer_than_allowed_is_stopped()
    {
        var engine = new FakeEngine { Wait = ct => Task.Delay(TimeSpan.FromSeconds(30), ct) };
        var reader = Reader(new OcrOptions { MaxDocumentSeconds = 1 }, engine);

        var started = DateTime.UtcNow;
        var error = await Should.ThrowAsync<TimeoutException>(() => reader.ReadAsync(Png(100, 100), CancellationToken.None));

        error.Message.ShouldContain("1 seconds");
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task A_caller_who_cancels_is_not_told_the_document_timed_out()
    {
        var engine = new FakeEngine { Wait = ct => Task.Delay(TimeSpan.FromSeconds(30), ct) };
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() => Reader(new OcrOptions(), engine).ReadAsync(Png(100, 100), cancel.Token));
    }

    [Fact]
    public async Task Only_so_many_documents_are_read_at_once_and_a_place_is_given_back()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var slow = new FakeEngine { Wait = async _ => { started.TrySetResult(); await release.Task; } };
        var options = new OcrOptions { MaxConcurrentDocuments = 1, QueueSeconds = 0 };
        var gate = new OcrGate(Options.Create(options));
        var first = Reader(options, slow, gate).ReadAsync(Png(100, 100), CancellationToken.None);
        await started.Task;

        await Should.ThrowAsync<OcrBusyException>(() => Reader(options, new FakeEngine(), gate).ReadAsync(Png(100, 100), CancellationToken.None));

        release.SetResult();
        await first;
        (await Reader(options, new FakeEngine(), gate).ReadAsync(Png(100, 100), CancellationToken.None)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_place_is_given_back_when_reading_fails()
    {
        var options = new OcrOptions { MaxConcurrentDocuments = 1, QueueSeconds = 0, MaxImagePixels = 10 };
        var gate = new OcrGate(Options.Create(options));

        await Should.ThrowAsync<InvalidDataException>(() => Reader(options, new FakeEngine(), gate).ReadAsync(Png(100, 100), CancellationToken.None));

        // The failed read did not keep the only place.
        await Should.ThrowAsync<InvalidDataException>(() => Reader(options, new FakeEngine(), gate).ReadAsync(Png(100, 100), CancellationToken.None));
    }

    // ---------- bounded text ----------

    [Fact]
    public async Task Bounded_reading_stops_at_the_limit_and_says_there_was_more()
    {
        using var reader = new StringReader(new string('x', 100));

        var (text, truncated) = await BoundedText.ReadAsync(reader, 10, drain: false, CancellationToken.None);

        text.ShouldBe(new string('x', 10));
        truncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Bounded_reading_can_read_on_and_throw_the_rest_away()
    {
        using var reader = new StringReader(new string('x', 100_000));

        var (text, truncated) = await BoundedText.ReadAsync(reader, 10, drain: true, CancellationToken.None);

        text.Length.ShouldBe(10);
        truncated.ShouldBeTrue();
        reader.Peek().ShouldBe(-1); // All of it was read.
    }

    [Fact]
    public async Task Bounded_reading_returns_everything_that_fits()
    {
        using var reader = new StringReader("short");

        var (text, truncated) = await BoundedText.ReadAsync(reader, 100, drain: false, CancellationToken.None);

        text.ShouldBe("short");
        truncated.ShouldBeFalse();
    }
}
