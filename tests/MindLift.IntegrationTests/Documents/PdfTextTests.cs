using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using MindLift.Infrastructure.Documents.Text;
using SkiaSharp;
using Shouldly;

namespace MindLift.IntegrationTests.Documents;

/// <summary>
/// PDF support (plan 19.5): a PDF page with a text layer is read directly, a scanned page is rendered and read by Tesseract.
/// The test files are made here with SkiaSharp, so the tests do not depend on files that someone may change.
/// </summary>
public sealed class PdfTextTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mindlift-pdf-{Guid.NewGuid():N}");

    public PdfTextTests() => Directory.CreateDirectory(_folder);

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

    private static DocumentPageReader NewReader()
    {
        var options = Options.Create(new OcrOptions
        {
            TesseractPath = @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            TessdataPath = Path.Combine(FindRepoRoot(), "tools", "tessdata"),
            Languages = "eng",
        });
        return new DocumentPageReader(new TesseractCliOcrEngine(options, NullLogger<TesseractCliOcrEngine>.Instance), options, NullLogger<DocumentPageReader>.Instance);
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

    /// <summary>A PDF whose page has real, selectable text.</summary>
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

    /// <summary>A picture of the text, saved as a PNG, as a phone photo or a scanner gives.</summary>
    private string Picture(int width = 1400, int height = 500, float size = 64)
    {
        var picture = Path.Combine(_folder, $"scan-{Guid.NewGuid():N}.png");
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, size);
        canvas.Clear(SKColors.White);
        canvas.DrawText("TRADE LICENSE", 40, size * 1.7f, font, paint);
        canvas.DrawText("License No: CN-1234567", 40, size * 4.1f, font, paint);
        canvas.DrawText("Expiry Date: 31/12/2027", 40, size * 6.4f, font, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(picture, data.ToArray());
        return picture;
    }

    /// <summary>A PDF whose only content is the picture, as a scanner makes.</summary>
    private string ScannedPdf()
    {
        // An A4 page scanned at 300 dpi: 2480 x 3508 pixels, with text about 12 points high, as in a real scan.
        var picture = Picture(2480, 3508, 50);
        var path = Path.Combine(_folder, $"scanned-{Guid.NewGuid():N}.pdf");
        using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream);
        using var pdfCanvas = document.BeginPage(595, 842);
        using var bitmap = SKBitmap.Decode(picture);
        pdfCanvas.DrawBitmap(bitmap, new SKRect(0, 0, 595, 842));
        document.EndPage();
        document.Close();
        return path;
    }

    [Fact]
    public async Task A_digital_pdf_is_read_from_its_text_layer_without_ocr()
    {
        var pages = await NewReader().ReadAsync(DigitalPdf(), CancellationToken.None);

        pages.Count.ShouldBe(1);
        pages[0].Source.ShouldBe("Digital");
        pages[0].Text.ShouldContain("CN-1234567");
        pages[0].Text.ShouldContain("31/12/2027");
        pages[0].Words.ShouldAllBe(w => w.X >= 0 && w.X <= 1 && w.Y >= 0 && w.Y <= 1);
    }

    [Fact]
    public async Task A_scanned_pdf_page_is_rendered_and_read_by_tesseract()
    {
        var pages = await NewReader().ReadAsync(ScannedPdf(), CancellationToken.None);

        pages.Count.ShouldBe(1);
        pages[0].Source.ShouldBe("Ocr");
        pages[0].Text.ShouldContain("CN-1234567");
        pages[0].Text.ShouldContain("31/12/2027");
    }

    [Fact]
    public async Task A_photo_is_read_by_tesseract_directly()
    {
        var pages = await NewReader().ReadAsync(Picture(), CancellationToken.None);

        pages[0].Source.ShouldBe("Ocr");
        pages[0].Text.ShouldContain("CN-1234567");
    }

    [Fact]
    public async Task A_file_type_that_is_not_supported_is_refused()
    {
        var path = Path.Combine(_folder, "notes.docx");
        await File.WriteAllTextAsync(path, "not read here");

        await Should.ThrowAsync<NotSupportedException>(() => NewReader().ReadAsync(path, CancellationToken.None));
    }
}
