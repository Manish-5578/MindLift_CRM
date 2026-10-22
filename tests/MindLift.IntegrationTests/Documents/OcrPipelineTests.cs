using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using MindLift.Infrastructure.Documents.Text;
using Shouldly;
using SkiaSharp;

namespace MindLift.IntegrationTests.Documents;

/// <summary>
/// The page reading pipeline: scoring, turning and straightening a page, trying other ways of reading a weak page, and mapping word boxes back to the original picture.
/// The logic tests use stand-in engines. The reading tests use the real Tesseract on pictures drawn here, so they do not depend on files that someone may change.
/// </summary>
public sealed class OcrPipelineTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mindlift-ocr-pipeline-{Guid.NewGuid():N}");

    public OcrPipelineTests() => Directory.CreateDirectory(_folder);

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

    // ---------- Helpers ----------

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindLift.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static OcrOptions RealOptions(Action<OcrOptions>? change = null)
    {
        var options = new OcrOptions
        {
            TesseractPath = @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            TessdataPath = Path.Combine(FindRepoRoot(), "tools", "tessdata"),
            Languages = "eng",
        };
        change?.Invoke(options);
        return options;
    }

    private static DocumentPageReader RealReader(OcrOptions? options = null)
    {
        options ??= RealOptions();
        var wrapped = Options.Create(options);
        return new DocumentPageReader(new TesseractCliOcrEngine(wrapped, NullLogger<TesseractCliOcrEngine>.Instance), wrapped, NullLogger<DocumentPageReader>.Instance);
    }

    private static SKBitmap TextBitmap(int width = 1400, int height = 500, float size = 64)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, size);
        canvas.Clear(SKColors.White);
        canvas.DrawText("TRADE LICENSE", 40, size * 1.7f, font, paint);
        canvas.DrawText("License No: CN-1234567", 40, size * 4.1f, font, paint);
        canvas.DrawText("Expiry Date: 31/12/2027", 40, size * 6.4f, font, paint);
        return bitmap;
    }

    private string Save(SKBitmap bitmap)
    {
        var path = Path.Combine(_folder, $"page-{Guid.NewGuid():N}.png");
        ImageEnhancer.SavePng(bitmap, path);
        return path;
    }

    private string Picture(Func<SKBitmap, SKBitmap>? change = null, int width = 1400, int height = 500, float size = 64)
    {
        using var original = TextBitmap(width, height, size);
        if (change is null)
        {
            return Save(original);
        }

        using var changed = change(original);
        return Save(changed);
    }

    /// <summary>Turns a picture by an angle, as a crooked scan is, on a canvas that keeps all of it.</summary>
    private static SKBitmap Crooked(SKBitmap source, double degrees) => ImageEnhancer.Rotate(source, degrees);

    /// <summary>Stands in for an engine: answers from a script, one answer per call, and counts the calls.</summary>
    private sealed class ScriptedEngine(params double[] confidences) : IOcrEngine
    {
        public int Calls { get; private set; }
        public bool IsAvailable() => true;

        public Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest request, CancellationToken ct)
        {
            var confidence = confidences[Math.Min(Calls, confidences.Length - 1)];
            Calls++;
            var words = new[] { new OcrWord($"word{Calls}", confidence, 10, 10, 100, 20, 1, 1, 1), new OcrWord("second", confidence, 120, 10, 100, 20, 1, 1, 1) };
            return Task.FromResult(new OcrPageResult(words, 0, 0));
        }
    }

    /// <summary>A picture with something on it, so the page is not treated as blank.</summary>
    private string InkedPicture()
    {
        using var bitmap = new SKBitmap(1000, 500);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(new SKRect(100, 100, 600, 160), paint);
        }

        return Save(bitmap);
    }

    private static OcrPagePipeline Pipeline(IOcrEngine primary, OcrOptions options, params IOcrEngine[] others)
        => new(primary, others, options, NullLogger.Instance);

    // ---------- Scoring ----------

    [Fact]
    public void A_page_with_nothing_found_scores_zero()
    {
        OcrQuality.Evaluate([]).Score.ShouldBe(0);
    }

    [Fact]
    public void Long_words_count_more_than_short_ones_in_the_score()
    {
        var words = new[]
        {
            new OcrWord("registration", 90, 0, 0, 1, 1, 1, 1, 1), // 12 letters at 90
            new OcrWord("a", 10, 0, 0, 1, 1, 1, 1, 1),            // 1 letter at 10
        };

        // (12*90 + 1*10) / 13 = 83.8
        OcrQuality.Evaluate(words).Score.ShouldBe(83.8, 0.1);
    }

    [Fact]
    public void Marks_that_are_not_text_lower_the_score()
    {
        var clean = new[] { new OcrWord("invoice", 90, 0, 0, 1, 1, 1, 1, 1), new OcrWord("total", 90, 0, 0, 1, 1, 1, 1, 1) };
        var noisy = clean.Concat([new OcrWord("|", 90, 0, 0, 1, 1, 1, 1, 1), new OcrWord("~~", 90, 0, 0, 1, 1, 1, 1, 1)]).ToArray();

        OcrQuality.Evaluate(noisy).Score.ShouldBeLessThan(OcrQuality.Evaluate(clean).Score - 10);
    }

    [Fact]
    public void A_reading_that_found_little_does_not_win_just_by_being_sure()
    {
        var sureButThin = new PageQuality(95, 2, 12);
        var fullButLessSure = new PageQuality(85, 120, 800);

        OcrQuality.Best([sureButThin, fullButLessSure]).ShouldBe(1);
    }

    [Fact]
    public void The_higher_score_wins_when_both_found_about_the_same_text()
    {
        OcrQuality.Best([new PageQuality(70, 100, 700), new PageQuality(90, 98, 690)]).ShouldBe(1);
    }

    [Theory]
    [InlineData("Page number: 0\nOrientation in degrees: 270\nRotate: 90\nOrientation confidence: 11.20\nScript: Latin", 90, 11.2)]
    [InlineData("Rotate: 180\nOrientation confidence: 4.5", 180, 4.5)]
    [InlineData("Rotate: 0\nOrientation confidence: 20", 0, 20)]
    [InlineData("Rotate: 270\nOrientation confidence: 1.01", 270, 1.01)]   // weak, but still reported so it can be used as a hint
    public void The_orientation_report_is_read(string report, int degrees, double confidence)
    {
        var guess = TesseractCliOcrEngine.ParseOrientation(report);

        guess.ShouldNotBeNull();
        guess.Degrees.ShouldBe(degrees);
        guess.Confidence.ShouldBe(confidence, 0.001);
    }

    [Fact]
    public void A_page_with_too_little_text_gives_no_orientation()
    {
        TesseractCliOcrEngine.ParseOrientation("Too few characters. Skipping this page").ShouldBeNull();
    }

    // ---------- Picture arithmetic ----------

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(0, -6.5)]
    [InlineData(3, 7.5)]
    public void Boxes_found_on_a_turned_page_map_back_to_where_they_are_on_the_original(int quarterTurns, double skew)
    {
        // A dark block at a known place on a white page, then the page turned and straightened as the pipeline does.
        var (left, top, width, height) = (0.2, 0.1, 0.15, 0.05);
        using var page = new SKBitmap(1000, 600);
        using (var canvas = new SKCanvas(page))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(new SKRect((float)(left * 1000), (float)(top * 600), (float)((left + width) * 1000), (float)((top + height) * 600)), paint);
        }

        using var oriented = ImageEnhancer.RotateQuarterTurns(page, quarterTurns);
        using var straight = skew == 0 ? oriented.Copy() : ImageEnhancer.Rotate(oriented, skew);
        var gray = ImageEnhancer.ToGray(straight);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = 0; y < gray.Height; y++)
        {
            for (var x = 0; x < gray.Width; x++)
            {
                if (gray.Pixels[y * gray.Width + x] < 128)
                {
                    (minX, minY, maxX, maxY) = (Math.Min(minX, x), Math.Min(minY, y), Math.Max(maxX, x), Math.Max(maxY, y));
                }
            }
        }

        var map = new CoordinateMap(1000, 600, quarterTurns, skew, straight.Width, straight.Height);
        var found = map.MapBox((double)minX / gray.Width, (double)minY / gray.Height, (double)(maxX - minX + 1) / gray.Width, (double)(maxY - minY + 1) / gray.Height);

        if (skew == 0)
        {
            found.X.ShouldBe(left, 0.004);
            found.Y.ShouldBe(top, 0.004);
            found.Width.ShouldBe(width, 0.004);
            found.Height.ShouldBe(height, 0.004);
        }
        else
        {
            // A box around a turned box is a little larger, so it must contain the original, and not by much.
            found.X.ShouldBeLessThanOrEqualTo(left + 0.004);
            found.Y.ShouldBeLessThanOrEqualTo(top + 0.004);
            (found.X + found.Width).ShouldBeGreaterThanOrEqualTo(left + width - 0.004);
            (found.Y + found.Height).ShouldBeGreaterThanOrEqualTo(top + height - 0.004);
            found.Width.ShouldBeLessThan(width * 1.8);
            found.Height.ShouldBeLessThan(height * 2.5);
        }
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(-4.5)]
    [InlineData(7.0)]
    public void A_crooked_page_is_measured_and_the_turn_that_straightens_it_is_reported(double crookedBy)
    {
        using var straight = TextBitmap();
        using var crooked = Crooked(straight, crookedBy);

        var turn = ImageEnhancer.EstimateSkew(ImageEnhancer.ToGray(crooked));

        // The page was turned by +x, so the turn that undoes it is -x.
        turn.ShouldBe(-crookedBy, 0.4);
    }

    [Fact]
    public void A_straight_page_is_left_alone()
    {
        using var straight = TextBitmap();

        ImageEnhancer.EstimateSkew(ImageEnhancer.ToGray(straight)).ShouldBe(0);
    }

    [Fact]
    public void A_blank_page_is_recognised_and_a_page_with_text_is_not()
    {
        using var blank = new SKBitmap(800, 600);
        using (var canvas = new SKCanvas(blank))
        {
            canvas.Clear(SKColors.White);
        }

        using var text = TextBitmap();

        ImageEnhancer.IsBlank(ImageEnhancer.ToGray(blank)).ShouldBeTrue();
        ImageEnhancer.IsBlank(ImageEnhancer.ToGray(text)).ShouldBeFalse();
    }

    [Fact]
    public void Black_and_white_follows_a_shadow_across_the_page()
    {
        // Dark text on a page that fades from white (left) to mid-gray (right), as an unevenly lit photo does. A single threshold would lose the text on one side.
        using var bitmap = new SKBitmap(1200, 400);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var shadow = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1200, 0), [new SKColor(250, 250, 250), new SKColor(120, 120, 120)], SKShaderTileMode.Clamp) };
            canvas.DrawRect(new SKRect(0, 0, 1200, 400), shadow);
            using var ink = new SKPaint { Color = new SKColor(20, 20, 20), IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 60);
            canvas.DrawText("LEFT SIDE", 40, 150, font, ink);
            canvas.DrawText("RIGHT SIDE", 800, 150, font, ink);
        }

        var binary = ImageEnhancer.Binarize(ImageEnhancer.ToGray(bitmap));

        // The paper at the far right (gray 120) must come out white, and the ink on that side must come out black.
        binary.Pixels[300 * binary.Width + 1150].ShouldBe((byte)255);
        var inkOnRight = Enumerable.Range(800, 400).Count(x => Enumerable.Range(100, 60).Any(y => binary.Pixels[y * binary.Width + x] == 0));
        inkOnRight.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void Specks_are_removed_and_strokes_of_text_are_kept()
    {
        var width = 200;
        var height = 100;
        var pixels = Enumerable.Repeat((byte)255, width * height).ToArray();
        // A single-pixel speck, a two-pixel speck, and a vertical stroke 5 pixels wide.
        pixels[20 * width + 20] = 0;
        pixels[40 * width + 60] = 0;
        pixels[40 * width + 61] = 0;
        for (var y = 10; y < 90; y++)
        {
            for (var x = 120; x < 125; x++)
            {
                pixels[y * width + x] = 0;
            }
        }

        var clean = ImageEnhancer.Denoise(new GrayImage(width, height, pixels));

        clean.Pixels[20 * width + 20].ShouldBe((byte)255);
        clean.Pixels[40 * width + 60].ShouldBe((byte)255);
        clean.Pixels[40 * width + 61].ShouldBe((byte)255);
        clean.Pixels[50 * width + 122].ShouldBe((byte)0);
    }

    // ---------- Trying other ways of reading ----------

    [Fact]
    public async Task A_page_that_reads_well_is_read_once()
    {
        var engine = new ScriptedEngine(95);

        var reading = await Pipeline(engine, new OcrOptions()).ReadAsync(InkedPicture(), 1000, 500, CancellationToken.None);

        engine.Calls.ShouldBe(1);
        reading.Attempts.ShouldBe(1);
        reading.LowConfidence.ShouldBeFalse();
    }

    [Fact]
    public async Task A_weak_page_is_read_again_until_it_reads_well()
    {
        var engine = new ScriptedEngine(40, 55, 92);

        var reading = await Pipeline(engine, new OcrOptions()).ReadAsync(InkedPicture(), 1000, 500, CancellationToken.None);

        engine.Calls.ShouldBe(3);
        reading.Confidence.ShouldBe(92, 0.1);
        reading.Text.ShouldContain("word3");
        reading.LowConfidence.ShouldBeFalse();
    }

    [Fact]
    public async Task A_page_that_never_reads_well_stops_at_the_attempt_limit_and_is_flagged()
    {
        var engine = new ScriptedEngine(30);

        var reading = await Pipeline(engine, new OcrOptions { MaxAttemptsPerPage = 3, DetectOrientation = false }).ReadAsync(InkedPicture(), 1000, 500, CancellationToken.None);

        engine.Calls.ShouldBe(3);
        reading.LowConfidence.ShouldBeTrue();
        reading.Confidence.ShouldBe(30, 0.1);
    }

    [Fact]
    public async Task A_page_that_reads_like_noise_is_turned_and_read_again_and_the_better_turn_wins()
    {
        // Three weak readings of the page as it lies, then the first turn reads well.
        var engine = new ScriptedEngine(20, 20, 20, 90);

        var reading = await Pipeline(engine, new OcrOptions { MaxAttemptsPerPage = 3 }).ReadAsync(InkedPicture(), 1000, 500, CancellationToken.None);

        engine.Calls.ShouldBe(4);
        reading.Confidence.ShouldBe(90, 0.1);
        reading.LowConfidence.ShouldBeFalse();
    }

    [Fact]
    public async Task A_page_that_is_only_a_little_weak_is_not_turned()
    {
        var engine = new ScriptedEngine(62);

        var reading = await Pipeline(engine, new OcrOptions { MaxAttemptsPerPage = 3 }).ReadAsync(InkedPicture(), 1000, 500, CancellationToken.None);

        engine.Calls.ShouldBe(3);   // three ways of reading it as it lies, no turns
        reading.LowConfidence.ShouldBeTrue();
    }

    [Fact]
    public async Task Another_engine_is_asked_only_when_the_first_stays_weak_and_its_better_reading_wins()
    {
        var weak = new ScriptedEngine(30);
        var strong = new ScriptedEngine(90);

        var reading = await Pipeline(weak, new OcrOptions { MaxAttemptsPerPage = 2 }, strong).ReadAsync(InkedPicture(), 1000, 500, CancellationToken.None);

        strong.Calls.ShouldBe(1);
        reading.Confidence.ShouldBe(90, 0.1);
        reading.LowConfidence.ShouldBeFalse();
    }

    [Fact]
    public async Task Another_engine_is_not_asked_when_the_first_reads_well()
    {
        var good = new ScriptedEngine(95);
        var other = new ScriptedEngine(99);

        await Pipeline(good, new OcrOptions(), other).ReadAsync(InkedPicture(), 1000, 500, CancellationToken.None);

        other.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task With_cleaning_off_the_original_picture_is_read_once()
    {
        var engine = new ScriptedEngine(40);

        var reading = await Pipeline(engine, new OcrOptions { EnhanceImages = false }).ReadAsync(InkedPicture(), 1000, 500, CancellationToken.None);

        engine.Calls.ShouldBe(1);
        reading.LowConfidence.ShouldBeTrue();
    }

    [Fact]
    public async Task A_blank_page_is_read_once_and_is_not_flagged()
    {
        using var blank = new SKBitmap(800, 600);
        using (var canvas = new SKCanvas(blank))
        {
            canvas.Clear(SKColors.White);
        }

        var empty = new EmptyEngine();

        var reading = await Pipeline(empty, new OcrOptions()).ReadAsync(Save(blank), 800, 600, CancellationToken.None);

        empty.Calls.ShouldBe(1);
        reading.LowConfidence.ShouldBeFalse();
        reading.Words.ShouldBeEmpty();
    }

    private sealed class EmptyEngine : IOcrEngine
    {
        public int Calls { get; private set; }
        public bool IsAvailable() => true;

        public Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new OcrPageResult([], 0, 0));
        }
    }

    // ---------- Reading with the real Tesseract ----------

    private static void ShouldReadTheLicense(PageText page)
    {
        page.Source.ShouldBe("Ocr");
        page.Text.ShouldContain("CN-1234567");
        page.Text.ShouldContain("31/12/2027");
        page.Words.ShouldAllBe(w => w.X >= 0 && w.Y >= 0 && w.X + w.Width <= 1.0001 && w.Y + w.Height <= 1.0001);
    }

    [Fact]
    public async Task A_clean_page_is_read_with_high_confidence()
    {
        var page = (await RealReader().ReadAsync(Picture(), CancellationToken.None)).Single();

        ShouldReadTheLicense(page);
        page.Confidence!.Value.ShouldBeGreaterThan(85);
        page.LowConfidence.ShouldBeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task A_page_lying_on_its_side_or_upside_down_is_turned_upright_and_read(int quarterTurns)
    {
        var path = Picture(bitmap => ImageEnhancer.RotateQuarterTurns(bitmap, quarterTurns));

        var page = (await RealReader().ReadAsync(path, CancellationToken.None)).Single();

        ShouldReadTheLicense(page);
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(-6.0)]
    public async Task A_crooked_scan_is_straightened_and_read(double crookedBy)
    {
        var path = Picture(bitmap => Crooked(bitmap, crookedBy));

        var page = (await RealReader().ReadAsync(path, CancellationToken.None)).Single();

        ShouldReadTheLicense(page);
    }

    [Fact]
    public async Task A_small_low_resolution_picture_is_enlarged_and_read()
    {
        // Text about 14 pixels high on a 520-pixel-wide picture, as a thumbnail or a small phone crop gives.
        var path = Picture(width: 520, height: 190, size: 20);

        var page = (await RealReader().ReadAsync(path, CancellationToken.None)).Single();

        ShouldReadTheLicense(page);
    }

    [Fact]
    public async Task Word_boxes_of_a_turned_page_land_where_the_words_are_on_the_original()
    {
        // The same words read upright and read from a page turned a quarter turn must be boxed in the same place on the picture a person sees.
        using var upright = TextBitmap();
        var uprightPage = (await RealReader().ReadAsync(Save(upright), CancellationToken.None)).Single();
        using var turned = ImageEnhancer.RotateQuarterTurns(upright, 1);
        var turnedPage = (await RealReader().ReadAsync(Save(turned), CancellationToken.None)).Single();

        var expected = uprightPage.Words.First(w => w.Text.Contains("CN-1234567"));
        var found = turnedPage.Words.First(w => w.Text.Contains("CN-1234567"));

        // "Where it is on the picture a person sees" for the turned page is the turned picture, so the expected box is the upright one turned the same way:
        // a point (x, y) of the upright page is at (1 - y, x) after a clockwise quarter turn.
        var turnedExpected = (X: 1 - (expected.Y + expected.Height), Y: expected.X, Width: expected.Height, Height: expected.Width);
        // The pipeline reports against the picture it was given (the turned one), which is what the review screen shows.
        found.X.ShouldBe(turnedExpected.X, 0.04);
        found.Y.ShouldBe(turnedExpected.Y, 0.04);
        found.Width.ShouldBe(turnedExpected.Width, 0.04);
        found.Height.ShouldBe(turnedExpected.Height, 0.04);
    }

    [Fact]
    public async Task A_page_with_no_text_is_flagged_and_a_blank_page_is_not()
    {
        // Random speckle: there is something on the page, but nothing to read.
        var random = new Random(7);
        using var speckle = new SKBitmap(900, 600);
        using (var canvas = new SKCanvas(speckle))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            for (var i = 0; i < 400; i++)
            {
                canvas.DrawCircle(random.Next(900), random.Next(600), random.Next(1, 4), paint);
            }
        }

        using var blank = new SKBitmap(900, 600);
        using (var canvas = new SKCanvas(blank))
        {
            canvas.Clear(SKColors.White);
        }

        var reader = RealReader();
        var speckled = (await reader.ReadAsync(Save(speckle), CancellationToken.None)).Single();
        var empty = (await reader.ReadAsync(Save(blank), CancellationToken.None)).Single();

        empty.LowConfidence.ShouldBeFalse();
        empty.Text.ShouldBeEmpty();
        (speckled.LowConfidence || speckled.Text.Length < 10).ShouldBeTrue();
    }

    [Fact]
    public async Task A_page_with_light_gray_text_on_a_gray_background_is_read()
    {
        // Low contrast, as a faded receipt or a dim photo is.
        using var bitmap = new SKBitmap(1400, 500);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(190, 190, 190));
            using var paint = new SKPaint { Color = new SKColor(120, 120, 120), IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 64);
            canvas.DrawText("TRADE LICENSE", 40, 109, font, paint);
            canvas.DrawText("License No: CN-1234567", 40, 262, font, paint);
            canvas.DrawText("Expiry Date: 31/12/2027", 40, 410, font, paint);
        }

        var page = (await RealReader().ReadAsync(Save(bitmap), CancellationToken.None)).Single();

        ShouldReadTheLicense(page);
    }
}
