using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using MindLift.Infrastructure.Documents.Text;
using Shouldly;
using SkiaSharp;
using Xunit.Abstractions;

namespace MindLift.IntegrationTests.Documents;

/// <summary>
/// Measures what the page cleaning is worth: the same document is damaged in the ways real scans and phone photos are,
/// read once with the original picture handed to Tesseract (how it was before) and once through the pipeline, and the characters are compared with the truth.
/// The pictures are drawn here, so the numbers do not depend on files that someone may change.
/// </summary>
public sealed class OcrAccuracyTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mindlift-ocr-accuracy-{Guid.NewGuid():N}");

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

    private static readonly string[] Lines =
    [
        "TAX INVOICE",
        "Vendor: Gulf Trading LLC",
        "Invoice No: INV-2026-00418",
        "Date: 14/03/2026",
        "Total Amount Due: AED 12,450.75",
        "Trade License: CN-1234567",
        "Expiry Date: 31/12/2027",
    ];

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindLift.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static DocumentPageReader Reader(bool enhance)
    {
        var options = Options.Create(new OcrOptions
        {
            TesseractPath = @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            TessdataPath = Path.Combine(FindRepoRoot(), "tools", "tessdata"),
            Languages = "eng",
            EnhanceImages = enhance,
            DetectOrientation = enhance,
            // The old way: one reading, whatever the score.
            MaxAttemptsPerPage = enhance ? new OcrOptions().MaxAttemptsPerPage : 1,
        });
        return new DocumentPageReader(new TesseractCliOcrEngine(options, NullLogger<TesseractCliOcrEngine>.Instance), options, NullLogger<DocumentPageReader>.Instance);
    }

    private static SKBitmap Document(int width = 1500, int height = 900, float size = 54)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var font = new SKFont(SKTypeface.Default, size);
        canvas.Clear(SKColors.White);
        for (var i = 0; i < Lines.Length; i++)
        {
            canvas.DrawText(Lines[i], 50, 90 + i * size * 1.9f, font, paint);
        }

        return bitmap;
    }

    private static SKBitmap Redraw(SKBitmap source, Action<SKCanvas, SKBitmap> draw, int? width = null, int? height = null)
    {
        var result = new SKBitmap(width ?? source.Width, height ?? source.Height);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        draw(canvas, source);
        return result;
    }

    private static SKBitmap Downscale(SKBitmap source, double factor)
        => Redraw(source, (canvas, bitmap) =>
        {
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, new SKRect(0, 0, (float)(bitmap.Width * factor), (float)(bitmap.Height * factor)), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }, (int)(source.Width * factor), (int)(source.Height * factor));

    private static SKBitmap AddShadow(SKBitmap source)
        => Redraw(source, (canvas, bitmap) =>
        {
            canvas.DrawBitmap(bitmap, 0, 0);
            using var shade = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(bitmap.Width, bitmap.Height * 0.4f), [new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, 150)], SKShaderTileMode.Clamp) };
            canvas.DrawRect(new SKRect(0, 0, bitmap.Width, bitmap.Height), shade);
        });

    private static SKBitmap NoisyAndBlurred(SKBitmap source)
    {
        var random = new Random(11);
        var result = Redraw(source, (canvas, bitmap) =>
        {
            using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(1.6f, 1.6f) };
            canvas.DrawBitmap(bitmap, 0, 0, blur);
            using var speck = new SKPaint { Color = new SKColor(40, 40, 40, 200) };
            for (var i = 0; i < 2500; i++)
            {
                canvas.DrawCircle(random.Next(bitmap.Width), random.Next(bitmap.Height), (float)(0.6 + random.NextDouble() * 1.4), speck);
            }
        });
        return result;
    }

    private static SKBitmap LowContrast(SKBitmap source)
        => Redraw(source, (canvas, bitmap) =>
        {
            canvas.Clear(new SKColor(200, 200, 200));
            using var faded = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix([0.4f, 0, 0, 0, 0.45f * 255 * 0, 0, 0.4f, 0, 0, 0, 0, 0, 0.4f, 0, 0, 0, 0, 0, 1, 0]) };
            canvas.DrawBitmap(bitmap, 0, 0, faded);
        });

    private static SKBitmap HeavyJpeg(SKBitmap source)
    {
        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 18);
        return SKBitmap.Decode(data);
    }

    private static string Normalize(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>1 minus the edit distance over the length of the truth: the share of characters read right.</summary>
    private static double Accuracy(string read, string truth)
    {
        var a = Normalize(read);
        var b = Normalize(truth);
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return Math.Max(0, 1.0 - (double)previous[b.Length] / b.Length);
    }

    private string Save(SKBitmap bitmap, bool jpeg = false)
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, $"case-{Guid.NewGuid():N}{(jpeg ? ".jpg" : ".png")}");
        if (jpeg)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            File.WriteAllBytes(path, data.ToArray());
        }
        else
        {
            ImageEnhancer.SavePng(bitmap, path);
        }

        return path;
    }

    [Fact]
    public async Task Cleaning_the_page_reads_damaged_pictures_better_than_handing_over_the_original()
    {
        var truth = string.Join('\n', Lines);
        using var clean = Document();
        var cases = new List<(string Name, SKBitmap Picture)>
        {
            ("clean", clean.Copy()),
            ("crooked 5 degrees", ImageEnhancer.Rotate(clean, 5)),
            ("crooked -8 degrees", ImageEnhancer.Rotate(clean, -8)),
            ("upside down", ImageEnhancer.RotateQuarterTurns(clean, 2)),
            ("on its side", ImageEnhancer.RotateQuarterTurns(clean, 1)),
            ("small (40% size)", Downscale(clean, 0.4)),
            ("uneven shadow", AddShadow(clean)),
            ("blurred with specks", NoisyAndBlurred(clean)),
            ("low contrast", LowContrast(clean)),
            ("heavy JPEG, 50% size", HeavyJpeg(Downscale(clean, 0.5))),
            ("crooked 4 degrees + shadow + small", AddShadow(Downscale(ImageEnhancer.Rotate(clean, 4), 0.55))),
        };

        double plainTotal = 0, cleanedTotal = 0;
        var worse = new List<string>();
        output.WriteLine($"{"case",-38} {"original",9} {"cleaned",9}");
        foreach (var (name, picture) in cases)
        {
            using (picture)
            {
                var path = Save(picture);
                var plain = (await Reader(enhance: false).ReadAsync(path, CancellationToken.None)).Single();
                var cleaned = (await Reader(enhance: true).ReadAsync(path, CancellationToken.None)).Single();
                var plainScore = Accuracy(plain.Text, truth);
                var cleanedScore = Accuracy(cleaned.Text, truth);
                plainTotal += plainScore;
                cleanedTotal += cleanedScore;
                output.WriteLine($"{name,-38} {plainScore,9:P1} {cleanedScore,9:P1}   (confidence {cleaned.Confidence:0}{(cleaned.LowConfidence ? ", flagged" : string.Empty)})");
                if (cleanedScore < plainScore - 0.05)
                {
                    worse.Add($"{name}: {plainScore:P1} -> {cleanedScore:P1}");
                }
            }
        }

        output.WriteLine($"{"average",-38} {plainTotal / cases.Count,9:P1} {cleanedTotal / cases.Count,9:P1}");

        cleanedTotal.ShouldBeGreaterThan(plainTotal, "cleaning should read better overall");
        worse.ShouldBeEmpty("cleaning should not make any case clearly worse");
        (cleanedTotal / cases.Count).ShouldBeGreaterThan(0.9);
    }

    [Theory]
    [InlineData("tools/sample-license.png")]
    [InlineData("tests/fixtures/documents/trade-license-en.png")]
    public async Task The_sample_documents_in_the_repository_read_at_least_as_well_when_cleaned(string relativePath)
    {
        var path = Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return;
        }

        var plain = (await Reader(enhance: false).ReadAsync(path, CancellationToken.None)).Single();
        var cleaned = (await Reader(enhance: true).ReadAsync(path, CancellationToken.None)).Single();

        output.WriteLine($"{relativePath}: original {plain.Confidence:0.#} ({plain.Words.Count} words) -> cleaned {cleaned.Confidence:0.#} ({cleaned.Words.Count} words)");
        output.WriteLine("--- original ---");
        output.WriteLine(plain.Text);
        output.WriteLine("--- cleaned ---");
        output.WriteLine(cleaned.Text);

        // Cleaning must not lose text the plain reading found, nor lower the confidence by more than a little.
        cleaned.Words.Count.ShouldBeGreaterThanOrEqualTo((int)(plain.Words.Count * 0.9));
        cleaned.Confidence!.Value.ShouldBeGreaterThanOrEqualTo(plain.Confidence!.Value - 3);
    }

    private static SKBitmap Blur(SKBitmap source, float sigma)
        => Redraw(source, (canvas, bitmap) =>
        {
            using var blur = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(sigma, sigma) };
            canvas.DrawBitmap(bitmap, 0, 0, blur);
        });

    [Fact]
    public async Task The_second_engine_reads_pages_that_stay_weak_after_cleaning_at_least_as_well()
    {
        var truth = string.Join('\n', Lines);
        using var clean = Document();
        var cases = new List<(string Name, SKBitmap Picture)>
        {
            ("very blurred", Blur(clean, 3.2f)),
            ("extremely blurred", Blur(clean, 5f)),
            ("blurred + specks + faint", LowContrast(NoisyAndBlurred(Blur(clean, 1.5f)))),
            ("tiny (25%) + heavy JPEG", HeavyJpeg(Downscale(clean, 0.25))),
            ("tiny (15%) + heavy JPEG", HeavyJpeg(Downscale(clean, 0.15))),
            ("blurred, small, JPEG", HeavyJpeg(Downscale(Blur(clean, 3.5f), 0.35))),
        };

        var tesseractOptions = Options.Create(new OcrOptions
        {
            TesseractPath = @"C:\Program Files\Tesseract-OCR\tesseract.exe",
            TessdataPath = Path.Combine(FindRepoRoot(), "tools", "tessdata"),
            Languages = "eng",
        });
        var tesseract = new TesseractCliOcrEngine(tesseractOptions, NullLogger<TesseractCliOcrEngine>.Instance);
        using var paddle = new RapidOcrEngine(tesseractOptions, NullLogger<RapidOcrEngine>.Instance);
        var alone = new DocumentPageReader(tesseract, tesseractOptions, NullLogger<DocumentPageReader>.Instance);
        var withSecond = new DocumentPageReader(tesseract, tesseractOptions, NullLogger<DocumentPageReader>.Instance, otherEngines: [tesseract, paddle]);

        double aloneTotal = 0, secondTotal = 0;
        output.WriteLine($"{"case",-30} {"tesseract",10} {"+ second",10} {"paddle only",12}");
        foreach (var (name, picture) in cases)
        {
            using (picture)
            {
                var path = Save(picture);
                var a = (await alone.ReadAsync(path, CancellationToken.None)).Single();
                var b = (await withSecond.ReadAsync(path, CancellationToken.None)).Single();
                var (aScore, bScore) = (Accuracy(a.Text, truth), Accuracy(b.Text, truth));
                var paddleOnly = await paddle.RecognizeAsync(path, new OcrRequest("eng"), CancellationToken.None);
                var pScore = Accuracy(string.Join(' ', paddleOnly.Words.Select(w => w.Text)), truth);
                aloneTotal += aScore;
                secondTotal += bScore;
                output.WriteLine($"{name,-30} {aScore,10:P1} {bScore,10:P1} {pScore,12:P1}   (confidence {a.Confidence:0} -> {b.Confidence:0}{(b.LowConfidence ? ", flagged" : string.Empty)})");
            }
        }

        output.WriteLine($"{"average",-30} {aloneTotal / cases.Count,10:P1} {secondTotal / cases.Count,10:P1}");
        secondTotal.ShouldBeGreaterThanOrEqualTo(aloneTotal - 0.02 * cases.Count, "the second engine should not make weak pages read worse");
    }
}
