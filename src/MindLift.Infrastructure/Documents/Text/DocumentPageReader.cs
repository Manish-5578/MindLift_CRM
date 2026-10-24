using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using SkiaSharp;
using PdfPig = UglyToad.PdfPig;

namespace MindLift.Infrastructure.Documents.Text;

/// <summary>
/// Text of one page, with where each word sits (normalised 0–1 of the page) and where it came from.
/// <c>Confidence</c> (0–100) says how sure the reading is; <c>LowConfidence</c> asks a person to check the page.
/// </summary>
public sealed record PageText(int PageNumber, string Text, string Source, IReadOnlyList<PageWord> Words, double? Confidence = null, bool LowConfidence = false);

/// <summary>A word with its box as fractions of the page width and height, so the review screen can draw it at any size.</summary>
public sealed record PageWord(string Text, double Confidence, double X, double Y, double Width, double Height);

/// <summary>
/// Reads the text of any supported file (plan 19.5). A PDF page with a text layer is read directly, with no OCR.
/// A scanned PDF page, or a photo, is rendered to an image and read with Tesseract.
/// Reading is bounded: only so many documents are read at once, one document may take only so long, a page or picture may have only so many pixels,
/// a PDF is read only up to its page limit, and a text file only up to a character limit (all in <see cref="OcrOptions"/>).
/// </summary>
public sealed class DocumentPageReader(IOcrEngine ocr, IOptions<OcrOptions> options, ILogger<DocumentPageReader> logger, OcrGate? gate = null, IEnumerable<IOcrEngine>? otherEngines = null)
{
    private readonly OcrPagePipeline _pipeline = new(ocr, (otherEngines ?? []).ToList(), options.Value, logger);

    /// <summary>A page with fewer characters than this in its text layer is treated as a scan.</summary>
    public const int MinTextLayerChars = 20;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".webp"];
    private static readonly string[] TextExtensions = [".txt", ".csv", ".md"];

    /// <summary>
    /// Reads every page. Throws <see cref="OcrBusyException"/> when no place frees up in time, <see cref="TimeoutException"/> when the document takes longer than allowed,
    /// and <see cref="InvalidDataException"/> for a picture that cannot be read or is too large.
    /// </summary>
    public async Task<IReadOnlyList<PageText>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        using var place = gate is null ? null : await gate.EnterAsync(cancellationToken);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.MaxDocumentSeconds)));
        try
        {
            return await ReadCoreAsync(path, budget.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Reading the document took longer than {options.Value.MaxDocumentSeconds} seconds.");
        }
    }

    private async Task<IReadOnlyList<PageText>> ReadCoreAsync(string path, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".pdf")
        {
            return await ReadPdfAsync(path, cancellationToken);
        }

        if (TextExtensions.Contains(extension))
        {
            // Plain text already has its characters, so there is nothing to recognise. Only the first part of a very long file is read.
            using var reader = new StreamReader(path);
            var (text, truncated) = await BoundedText.ReadAsync(reader, Math.Max(1, options.Value.MaxTextCharacters), drain: false, cancellationToken);
            if (truncated)
            {
                logger.LogWarning("The text file {File} is longer than {Limit} characters, so only the start was read.", Path.GetFileName(path), options.Value.MaxTextCharacters);
            }

            return [new PageText(1, text, "Digital", [], 100)];
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
        var limit = Math.Max(1, options.Value.MaxPages);
        var count = Math.Min(pdf.NumberOfPages, limit);
        if (pdf.NumberOfPages > limit)
        {
            logger.LogWarning("The PDF {File} has {Pages} pages; only the first {Limit} are read.", Path.GetFileName(path), pdf.NumberOfPages, limit);
        }

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
                pages.Add(new PageText(number, text, "Digital", boxes, 100));
            }
            else
            {
                // A scanned page: render it at OCR resolution (lower for a very large page), then read the image.
                var dpi = RenderDpiFor(page.Width, page.Height);
                if (dpi == 0)
                {
                    logger.LogWarning("Page {Page} of {File} is {Width} x {Height} points, too large to read, so it was skipped.", number, Path.GetFileName(path), page.Width, page.Height);
                    pages.Add(new PageText(number, string.Empty, "Skipped", []));
                    continue;
                }

                var image = RenderPage(path, number, dpi);
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

    /// <summary>
    /// The resolution to render a page of this size (in points, 72 to the inch) at: the OCR resolution, or lower so the picture stays within the pixel limit.
    /// Zero means the page is too big to read even at the lowest resolution allowed.
    /// </summary>
    public int RenderDpiFor(double widthPoints, double heightPoints) => RenderDpiFor(options.Value, widthPoints, heightPoints);

    /// <summary>The same, for any set of options.</summary>
    public static int RenderDpiFor(OcrOptions options, double widthPoints, double heightPoints)
    {
        var dpi = Math.Max(1, options.OcrDpi);
        if (!(widthPoints > 0) || !(heightPoints > 0) || double.IsInfinity(widthPoints) || double.IsInfinity(heightPoints))
        {
            return 0;
        }

        var pixels = widthPoints / 72.0 * dpi * (heightPoints / 72.0 * dpi);
        var limit = Math.Max(1, options.MaxPagePixels);
        if (pixels <= limit)
        {
            return dpi;
        }

        var scaled = (int)Math.Floor(dpi * Math.Sqrt(limit / pixels));
        return scaled >= Math.Max(1, options.MinRenderDpi) ? scaled : 0;
    }

    public static string RenderPage(string pdfPath, int pageNumber, int dpi)
    {
        var output = Path.Combine(Path.GetTempPath(), $"page_{Guid.NewGuid():N}.png");
        using var input = File.OpenRead(pdfPath);
        PDFtoImage.Conversion.SavePng(output, input, page: pageNumber - 1, options: new PDFtoImage.RenderOptions(Dpi: dpi));
        return output;
    }

    /// <summary>The picture's size, read from its header without decoding it. A file that is not a readable picture, or has too many pixels, is refused.</summary>
    private (int Width, int Height) ImageSize(string imagePath)
    {
        using var codec = SKCodec.Create(imagePath) ?? throw new InvalidDataException("The file is not a picture that can be read.");
        var (width, height) = (codec.Info.Width, codec.Info.Height);
        var limit = Math.Max(1, options.Value.MaxImagePixels);
        if (width <= 0 || height <= 0 || (long)width * height > limit)
        {
            throw new InvalidDataException($"The picture is {width} x {height} pixels, which is more than the {limit / 1_000_000} million allowed.");
        }

        return (width, height);
    }

    private async Task<PageText> OcrImageAsync(string imagePath, int pageNumber, CancellationToken cancellationToken)
    {
        var size = ImageSize(imagePath);
        var reading = await _pipeline.ReadAsync(imagePath, size.Width, size.Height, cancellationToken);
        logger.LogInformation("Read page {Page} by OCR: {Words} words, confidence {Confidence:0.#}{Low} ({Attempts} reading(s))", pageNumber, reading.Words.Count,
            reading.Confidence, reading.LowConfidence ? ", LOW" : string.Empty, reading.Attempts);
        return new PageText(pageNumber, reading.Text, "Ocr", reading.Words, reading.Confidence, reading.LowConfidence);
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

        // A line of Arabic (or Hebrew) is read from its right edge, so its words are put in that order. Latin lines run left to right as before.
        // Direction marks and Arabic-Indic digits are cleaned, so the text can be searched and its numbers read.
        return string.Join("\n", lines.Select(l =>
        {
            var rightToLeft = ArabicText.RunsRightToLeft(string.Join(" ", l.Select(w => w.Text)));
            var ordered = (rightToLeft ? l.OrderByDescending(w => w.Left) : l.OrderBy(w => w.Left)).Select(w => ArabicText.Clean(w.Text));
            var line = string.Join(" ", ordered);
            return rightToLeft ? ArabicText.FixLine(line) : line;
        }));
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
