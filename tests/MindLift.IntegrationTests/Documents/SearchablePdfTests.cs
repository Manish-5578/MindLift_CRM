using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using MindLift.Infrastructure.Documents.Text;
using Shouldly;
using SkiaSharp;
using PdfPig = UglyToad.PdfPig;

namespace MindLift.IntegrationTests.Documents;

/// <summary>
/// A searchable copy of a scan: the pages come back as a PDF whose text can be found and copied. The test files are drawn here, and the result is checked by reading its text layer back.
/// </summary>
public sealed class SearchablePdfTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mindlift-searchable-{Guid.NewGuid():N}");

    public SearchablePdfTests() => Directory.CreateDirectory(_folder);

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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindLift.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static SearchablePdfService Service(Action<OcrOptions>? change = null)
    {
        var raw = new OcrOptions
        {
            TesseractPath = @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            TessdataPath = Path.Combine(FindRepoRoot(), "tools", "tessdata"),
            Languages = "eng",
        };
        change?.Invoke(raw);
        var options = Options.Create(raw);
        return new SearchablePdfService(new TesseractCliOcrEngine(options, NullLogger<TesseractCliOcrEngine>.Instance), options, NullLogger<SearchablePdfService>.Instance);
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

    private string SavePicture(SKBitmap bitmap)
    {
        var path = Path.Combine(_folder, $"scan-{Guid.NewGuid():N}.png");
        ImageEnhancer.SavePng(bitmap, path);
        return path;
    }

    /// <summary>A PDF whose pages are only pictures, as a scanner makes (an A4 page at 300 dpi per picture).</summary>
    private string ScannedPdf(int pages)
    {
        var path = Path.Combine(_folder, $"scanned-{Guid.NewGuid():N}.pdf");
        using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream);
        for (var i = 0; i < pages; i++)
        {
            using var picture = TextBitmap(2480, 3508, 50);
            using var canvas = document.BeginPage(595, 842);
            canvas.DrawBitmap(picture, new SKRect(0, 0, 595, 842));
            document.EndPage();
        }

        document.Close();
        return path;
    }

    private string DigitalPdf()
    {
        var path = Path.Combine(_folder, $"digital-{Guid.NewGuid():N}.pdf");
        using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream);
        using var canvas = document.BeginPage(900, 400);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, 30);
        canvas.DrawText("TRADE LICENSE", 40, 70, font, paint);
        canvas.DrawText("License No: CN-1234567", 40, 150, font, paint);
        canvas.DrawText("Expiry Date: 31/12/2027", 40, 230, font, paint);
        document.EndPage();
        document.Close();
        return path;
    }

    private static List<string> TextOfEachPage(string pdfPath)
    {
        using var pdf = PdfPig.PdfDocument.Open(pdfPath);
        return pdf.GetPages().Select(p => string.Join(' ', p.GetWords().Select(w => w.Text))).ToList();
    }

    [Fact]
    public async Task A_scanned_pdf_becomes_a_pdf_with_findable_text_on_every_page()
    {
        var output = await Service().CreateAsync(ScannedPdf(2), CancellationToken.None);

        output.ShouldNotBeNull();
        try
        {
            var pages = TextOfEachPage(output);
            pages.Count.ShouldBe(2);
            pages.ShouldAllBe(text => text.Contains("CN-1234567") && text.Contains("31/12/2027"));
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task A_photo_becomes_a_one_page_searchable_pdf()
    {
        using var bitmap = TextBitmap();
        var output = await Service().CreateAsync(SavePicture(bitmap), CancellationToken.None);

        output.ShouldNotBeNull();
        try
        {
            TextOfEachPage(output).Single().ShouldContain("CN-1234567");
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task A_page_lying_on_its_side_is_made_upright_in_the_copy()
    {
        using var bitmap = TextBitmap();
        using var sideways = ImageEnhancer.RotateQuarterTurns(bitmap, 1);
        var output = await Service().CreateAsync(SavePicture(sideways), CancellationToken.None);

        output.ShouldNotBeNull();
        try
        {
            TextOfEachPage(output).Single().ShouldContain("CN-1234567");
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task A_pdf_that_already_has_text_needs_no_copy()
    {
        (await Service().CreateAsync(DigitalPdf(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_pdf_with_more_pages_than_the_limit_is_refused_not_cut_short()
    {
        await Should.ThrowAsync<InvalidDataException>(() => Service(o => o.MaxPages = 1).CreateAsync(ScannedPdf(2), CancellationToken.None));
    }

    [Fact]
    public async Task A_file_that_is_not_a_pdf_or_picture_is_refused()
    {
        var path = Path.Combine(_folder, "notes.txt");
        await File.WriteAllTextAsync(path, "just text");

        await Should.ThrowAsync<NotSupportedException>(() => Service().CreateAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task Hidden_words_sit_over_their_place_on_the_page_and_the_page_keeps_its_printed_size()
    {
        var output = await Service().CreateAsync(ScannedPdf(1), CancellationToken.None);

        output.ShouldNotBeNull();
        try
        {
            using var pdf = PdfPig.PdfDocument.Open(output);
            var page = pdf.GetPage(1);

            // An A4 scan comes back as an A4 page, give or take the margin added when the page was straightened.
            page.Width.ShouldBe(595, 15);
            page.Height.ShouldBe(842, 15);

            // The licence number is the second line, drawn about 205 of 3508 pixels down (6% of the page); PDF measures from the bottom.
            var number = page.GetWords().First(w => w.Text.Contains("CN-1234567"));
            (1 - number.BoundingBox.Bottom / page.Height).ShouldBe(0.06, 0.03);
            number.BoundingBox.Left.ShouldBeLessThan(page.Width / 4);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public async Task A_crooked_faint_scan_gets_the_cleaned_reading_in_its_text_layer()
    {
        using var bitmap = TextBitmap();
        using var crooked = ImageEnhancer.Rotate(bitmap, -6);
        using (var canvas = new SKCanvas(crooked))
        using (var veil = new SKPaint { Color = new SKColor(255, 255, 255, 150) })
        {
            canvas.DrawRect(0, 0, crooked.Width, crooked.Height, veil);
        }

        var output = await Service().CreateAsync(SavePicture(crooked), CancellationToken.None);

        output.ShouldNotBeNull();
        try
        {
            var text = TextOfEachPage(output).Single();
            text.ShouldContain("CN-1234567");
            text.ShouldContain("31/12/2027");
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void Words_outside_the_standard_font_are_left_to_the_engines_own_writer()
    {
        var picture = new GrayImage(10, 10, new byte[100]);
        TextLayerPdfWriter.CanWrite([new TextLayerPage(picture, 300, [new PageWord("Invoice €12", 90, 0, 0, 5, 5)])]).ShouldBeTrue();
        TextLayerPdfWriter.CanWrite([new TextLayerPage(picture, 300, [new PageWord("فاتورة", 90, 0, 0, 5, 5)])]).ShouldBeFalse();
    }

}
