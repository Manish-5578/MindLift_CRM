using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using PdfPig = UglyToad.PdfPig;

namespace MindLift.Infrastructure.Documents.Text;

/// <summary>Text of one page, with where each word sits (normalised 0–1 of the page) and where it came from.</summary>
public sealed record PageText(int PageNumber, string Text, string Source, IReadOnlyList<PageWord> Words);

/// <summary>A word with its box as fractions of the page width and height, so the review screen can draw it at any size.</summary>
public sealed record PageWord(string Text, double Confidence, double X, double Y, double Width, double Height);

/// <summary>
/// Reads the text of any supported file (plan 19.5). A PDF page with a text layer is read directly, with no OCR.
/// A scanned PDF page, or a photo, is rendered to an image and read with Tesseract.
/// </summary>
public sealed class DocumentPageReader(IOcrEngine ocr, IOptions<OcrOptions> options, ILogger<DocumentPageReader> logger)
{
    /// <summary>A page with fewer characters than this in its text layer is treated as a scan.</summary>
    public const int MinTextLayerChars = 20;

    public const int MaxPages = 50;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".webp"];
    private static readonly string[] TextExtensions = [".txt", ".csv", ".md"];

    public async Task<IReadOnlyList<PageText>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".pdf")
        {
            return await ReadPdfAsync(path, cancellationToken);
        }

        if (TextExtensions.Contains(extension))
        {
            // Plain text already has its characters, so there is nothing to recognise.
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            return [new PageText(1, text, "Digital", [])];
        }

        if (ImageExtensions.Contains(extension))
        {
            var page = await OcrImageAsync(path, 1, cancellationToken);
            return [page];
        }

        throw new NotSupportedException($"The file type {extension} cannot be read.");
    }

    private async Task<IReadOnlyList<PageText>> ReadPdfAsync(string path, CancellationToken cancellationToken)
    {
        var pages = new List<PageText>();
        using var pdf = PdfPig.PdfDocument.Open(path);
        var count = Math.Min(pdf.NumberOfPages, MaxPages);

        for (var number = 1; number <= count; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pdf.GetPage(number);
            var words = page.GetWords().ToList();
            var text = JoinLines(words.Select(w => (w.Text, w.BoundingBox.Bottom, w.BoundingBox.Height, w.BoundingBox.Left)));
            if (text.Trim().Length >= MinTextLayerChars)
            {
                var width = page.Width;
                var height = page.Height;
                var boxes = words.Select(w => new PageWord(w.Text, 100, w.BoundingBox.Left / width, 1 - w.BoundingBox.Top / height,
                    w.BoundingBox.Width / width, w.BoundingBox.Height / height)).ToList();
                pages.Add(new PageText(number, text, "Digital", boxes));
            }
            else
            {
                // A scanned page: render it at OCR resolution, then read the image.
                var image = RenderPage(path, number);
                try
                {
                    pages.Add(await OcrImageAsync(image, number, cancellationToken));
                }
                finally
                {
                    TryDelete(image);
                }
            }
        }

        return pages;
    }

    private string RenderPage(string pdfPath, int pageNumber)
    {
        var output = Path.Combine(Path.GetTempPath(), $"page_{Guid.NewGuid():N}.png");
        using var input = File.OpenRead(pdfPath);
        PDFtoImage.Conversion.SavePng(output, input, page: pageNumber - 1, options: new PDFtoImage.RenderOptions(Dpi: options.Value.OcrDpi));
        return output;
    }

    private async Task<PageText> OcrImageAsync(string imagePath, int pageNumber, CancellationToken cancellationToken)
    {
        var result = await ocr.RecognizeAsync(imagePath, new OcrRequest(options.Value.Languages), cancellationToken);
        var width = Math.Max(result.ImageWidth, 1);
        var height = Math.Max(result.ImageHeight, 1);
        var words = result.Words.Select(w => new PageWord(w.Text, w.Confidence, (double)w.Left / width, (double)w.Top / height,
            (double)w.Width / width, (double)w.Height / height)).ToList();
        var text = JoinLines(result.Words.Select(w => (w.Text, (double)-w.Top, (double)w.Height, (double)w.Left)));
        logger.LogInformation("Read page {Page} by OCR: {Words} words", pageNumber, words.Count);
        return new PageText(pageNumber, text, "Ocr", words);
    }


    /// <summary>
    /// Builds the page text with one line per printed line, so a label and the value after it stay together.
    /// Words are grouped by their height on the page and ordered left to right within each line.
    /// </summary>
    public static string JoinLines(IEnumerable<(string Text, double Vertical, double Height, double Left)> words)
    {
        var list = words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        var lines = new List<List<(string Text, double Vertical, double Height, double Left)>>();
        foreach (var word in list.OrderByDescending(w => w.Vertical).ThenBy(w => w.Left))
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].Vertical - word.Vertical) <= Math.Max(l[0].Height, word.Height) * 0.5);
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        return string.Join("\n", lines.Select(l => string.Join(" ", l.OrderBy(w => w.Left).Select(w => w.Text))));
    }
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Temporary file, removed by the system later.
        }
    }
}
