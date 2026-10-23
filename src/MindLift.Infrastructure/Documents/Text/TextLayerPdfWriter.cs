using SkiaSharp;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace MindLift.Infrastructure.Documents.Text;

/// <summary>One page of a searchable copy: the page picture, the resolution it was made at, and the words read from it (boxes as fractions of the page, as <see cref="PageWord"/> holds them).</summary>
public sealed record TextLayerPage(GrayImage Picture, int Dpi, IReadOnlyList<PageWord> Words);

/// <summary>
/// Writes a PDF whose pages are pictures with their words hidden behind them, so the text can be searched and copied.
/// Each word is placed over its box and sized to the box's width, so selecting text highlights the right part of the picture.
/// The hidden text uses a standard PDF font, which covers Western European letters only; <see cref="CanWrite"/> says whether a page's words fit it.
/// </summary>
public static class TextLayerPdfWriter
{
    private const int JpegQuality = 85;

    /// <summary>True when every word can be written in the standard font. Arabic, for example, cannot, and such a document is written another way.</summary>
    public static bool CanWrite(IEnumerable<TextLayerPage> pages) =>
        pages.SelectMany(p => p.Words).All(w => w.Text.All(Encodable));

    public static void Write(IReadOnlyList<TextLayerPage> pages, string outputPath)
    {
        if (pages.Count == 0)
        {
            throw new ArgumentException("At least one page is needed.", nameof(pages));
        }

        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var page in pages)
        {
            // Points per pixel: a picture made at 300 dpi becomes a page of its real printed size.
            var scale = 72.0 / page.Dpi;
            var width = page.Picture.Width * scale;
            var height = page.Picture.Height * scale;
            var pdfPage = builder.AddPage(width, height);
            pdfPage.AddJpeg(Jpeg(page.Picture), new PdfRectangle(0, 0, width, height));

            pdfPage.SetTextRenderingMode(TextRenderingMode.Neither);
            foreach (var word in page.Words)
            {
                var text = word.Text.Trim();
                if (text.Length == 0 || word.Width <= 0 || word.Height <= 0)
                {
                    continue;
                }

                var boxWidth = word.Width * width;
                var boxHeight = word.Height * height;

                // The font size is chosen so the word is as wide as its box, but never far taller than the box.
                var widthAtOne = Width(pdfPage, text, 1m, font);
                var size = widthAtOne > 0 ? boxWidth / widthAtOne : boxHeight;
                size = Math.Clamp(size, boxHeight * 0.3, boxHeight * 1.6);
                if (size <= 0.1)
                {
                    continue;
                }

                // PDF measures up from the bottom of the page; the box is measured down from the top. The text sits on the box's bottom edge, raised a little for letters that hang below it.
                var baseline = height - (word.Y + word.Height) * height + boxHeight * 0.15;
                pdfPage.AddText(text, (decimal)size, new PdfPoint(word.X * width, baseline), font);
            }

            pdfPage.SetTextRenderingMode(TextRenderingMode.Fill);
        }

        File.WriteAllBytes(outputPath, builder.Build());
    }

    private static double Width(PdfPageBuilder page, string text, decimal size, PdfDocumentBuilder.AddedFont font)
    {
        var letters = page.MeasureText(text, size, new PdfPoint(0, 0), font);
        return letters.Count == 0 ? 0 : letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left);
    }

    // The standard font's encoding: printable ASCII and the Latin-1 letters and signs above it, plus the few extra marks it adds.
    private static bool Encodable(char c) =>
        c is >= ' ' and <= '~' or >= ' ' and <= 'ÿ' or '€' or '‘' or '’' or '“' or '”' or '–' or '—' or '•' or '…';

    private static byte[] Jpeg(GrayImage picture)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(picture.Width, picture.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        var target = bitmap.GetPixelSpan();
        for (var y = 0; y < picture.Height; y++)
        {
            picture.Pixels.AsSpan(y * picture.Width, picture.Width).CopyTo(target.Slice(y * bitmap.RowBytes, picture.Width));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data.ToArray();
    }
}
