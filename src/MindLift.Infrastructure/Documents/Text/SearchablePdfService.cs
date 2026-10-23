using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using PdfPig = UglyToad.PdfPig;

namespace MindLift.Infrastructure.Documents.Text;

/// <summary>
/// Makes a searchable copy of a scanned document: the same pages, each turned upright and straightened, with the recognised text hidden behind the picture
/// so the PDF can be searched and its text copied. The original file is never changed.
/// Each page is read the same way as for extraction (several attempts, the best kept), so the hidden text is as good as the app's best reading.
/// Text the standard PDF font cannot hold, such as Arabic, is written by the engine's own PDF writer instead, from one reading of each page.
/// A PDF that already has text on every page is already searchable, so no copy is made.
/// </summary>
public sealed class SearchablePdfService(IOcrEngine ocr, IOptions<OcrOptions> options, ILogger<SearchablePdfService> logger, OcrGate? gate = null, IEnumerable<IOcrEngine>? otherEngines = null)
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".webp"];

    /// <summary>
    /// Writes the searchable copy and returns its path (the caller deletes it), or null when the source is a PDF that is already searchable.
    /// Throws <see cref="NotSupportedException"/> for a file that is neither a PDF nor a picture, <see cref="OcrBusyException"/> when the reader is busy,
    /// <see cref="TimeoutException"/> when it takes longer than allowed, and <see cref="InvalidDataException"/> for a file that cannot be turned into pages.
    /// </summary>
    public async Task<string?> CreateAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension != ".pdf" && !ImageExtensions.Contains(extension))
        {
            throw new NotSupportedException("A searchable copy can only be made from a PDF or a picture.");
        }

        using var place = gate is null ? null : await gate.EnterAsync(cancellationToken);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.MaxDocumentSeconds)));
        var temp = new List<string>();
        try
        {
            var pageImages = extension == ".pdf" ? RenderPdf(sourcePath, temp, budget.Token) : [(sourcePath, options.Value.OcrDpi)];
            if (pageImages is null)
            {
                return null;
            }

            var cleaned = new List<string>();
            var pages = new List<TextLayerPage>();
            var pipeline = new OcrPagePipeline(ocr, (otherEngines ?? []).ToList(), options.Value, logger);
            foreach (var (image, dpi) in pageImages)
            {
                budget.Token.ThrowIfCancellationRequested();
                var turns = await pipeline.DetermineQuarterTurnsAsync(image, budget.Token);
                using var page = await PreparedPage.CreateAsync(image, null, turns, logger, budget.Token)
                    ?? throw new InvalidDataException("A page of the file is not a picture that can be read.");
                var path = Path.Combine(Path.GetTempPath(), $"searchable_page_{Guid.NewGuid():N}.png");
                temp.Add(path);
                var picture = page.StraightGray();
                ImageEnhancer.SavePng(picture, path);
                cleaned.Add(path);

                // The upright page is read as extraction reads a page, so its word boxes match this picture.
                var reading = await pipeline.ReadAsync(path, picture.Width, picture.Height, budget.Token);
                pages.Add(new TextLayerPage(picture, dpi, reading.Words));
            }

            var output = Path.Combine(Path.GetTempPath(), $"searchable_{Guid.NewGuid():N}.pdf");
            try
            {
                if (TextLayerPdfWriter.CanWrite(pages))
                {
                    TextLayerPdfWriter.Write(pages, output);
                }
                else if (ocr is ISearchablePdfWriter writer)
                {
                    await writer.CreateSearchablePdfAsync(cleaned, output, options.Value.Languages, budget.Token);
                }
                else
                {
                    throw new NotSupportedException("This document's text needs the engine's own PDF writer, and the OCR engine in use has none.");
                }
            }
            catch
            {
                TryDelete(output);
                throw;
            }

            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Making the searchable copy took longer than {options.Value.MaxDocumentSeconds} seconds.");
        }
        finally
        {
            foreach (var path in temp)
            {
                TryDelete(path);
            }
        }
    }

    /// <summary>Renders each page to a picture. Returns null when every page already has a text layer.</summary>
    private List<(string Path, int Dpi)>? RenderPdf(string pdfPath, List<string> temp, CancellationToken cancellationToken)
    {
        using var pdf = PdfPig.PdfDocument.Open(pdfPath);
        var limit = Math.Max(1, options.Value.MaxPages);
        if (pdf.NumberOfPages > limit)
        {
            // A copy with missing pages would look complete and not be, so it is refused.
            throw new InvalidDataException($"The PDF has {pdf.NumberOfPages} pages; a searchable copy can be made for up to {limit}.");
        }

        var scanned = new List<(int Number, double Width, double Height)>();
        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pdf.GetPage(number);
            var text = DocumentPageReader.JoinLines(page.GetWords().Select(w => (w.Text, w.BoundingBox.Bottom, w.BoundingBox.Height, w.BoundingBox.Left)));
            if (text.Trim().Length < DocumentPageReader.MinTextLayerChars)
            {
                scanned.Add((number, page.Width, page.Height));
            }
        }

        if (scanned.Count == 0)
        {
            return null;
        }

        // At least one page is a scan, so every page is rendered: the copy has to be a whole document.
        var images = new List<(string Path, int Dpi)>();
        for (var number = 1; number <= pdf.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = pdf.GetPage(number);
            var dpi = DocumentPageReader.RenderDpiFor(options.Value, page.Width, page.Height);
            if (dpi == 0)
            {
                throw new InvalidDataException($"Page {number} is too large to make a searchable copy of.");
            }

            var image = DocumentPageReader.RenderPage(pdfPath, number, dpi);
            temp.Add(image);
            images.Add((image, dpi));
        }

        return images;
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
