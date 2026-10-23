using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RapidOcrNet;
using SkiaSharp;

namespace MindLift.Infrastructure.Cpq;

/// <summary>An engine whose models read only Latin letters, digits and signs. It is not asked to read a page on which Arabic or another script was found.</summary>
public interface ILatinOnlyOcrEngine;

/// <summary>
/// A second opinion for pages Tesseract reads poorly: PaddleOCR's PP-OCRv5 models (text finder, line turner and Latin reader) run in-process on ONNX Runtime.
/// The models ship inside the RapidOcrNet package and are copied next to the app when it is built, so nothing is downloaded at run time.
/// Switched on with <see cref="OcrOptions.SecondEngine"/>. It reads one page at a time; the models are loaded on first use and kept.
/// </summary>
public sealed class RapidOcrEngine(IOptions<OcrOptions> options, ILogger<RapidOcrEngine> logger) : IOcrEngine, ILatinOnlyOcrEngine, IDisposable
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private RapidOcr? _ocr;
    private bool? _available;

    private static string ModelFolder => Path.Combine(AppContext.BaseDirectory, "models", "v5");

    public bool IsAvailable()
    {
        if (!options.Value.SecondEngine)
        {
            return false;
        }

        return _available ??= File.Exists(Path.Combine(ModelFolder, "ch_PP-OCRv5_mobile_det.onnx"));
    }

    public async Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest request, CancellationToken ct)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("The second OCR engine is switched off or its models are missing.");
        }

        using var bitmap = SKBitmap.Decode(imagePath) ?? throw new IOException($"{Path.GetFileName(imagePath)} is not a picture that can be read.");
        await _one.WaitAsync(ct);
        try
        {
            _ocr ??= Load();

            // The default caps the longer side at 1024 pixels, which makes a full-page scan's print too small to read; the page is read at its own size up to a bound.
            var settings = RapidOcrOptions.Default with { ReturnWordBox = true, ImgResize = Math.Min(Math.Max(bitmap.Width, bitmap.Height), MaxSide) };
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.PageTimeoutSeconds)));
            OcrResult result;
            try
            {
                result = await _ocr.DetectAsync(bitmap, settings, null, budget.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"The second OCR engine took longer than {options.Value.PageTimeoutSeconds} seconds on a page.");
            }

            return new OcrPageResult(ToWords(result), bitmap.Width, bitmap.Height);
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>The longest side the text finder is given, in pixels. Bigger pages are scaled down to this.</summary>
    private const int MaxSide = 3000;

    private RapidOcr Load()
    {
        var started = DateTime.UtcNow;
        // Absolute paths: the library otherwise looks for the models relative to the working directory, which is not the app folder.
        var ocr = new RapidOcr();
        ocr.InitModels(
            Path.Combine(ModelFolder, "ch_PP-OCRv5_mobile_det.onnx"),
            Path.Combine(ModelFolder, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
            Path.Combine(ModelFolder, "latin_PP-OCRv5_rec_mobile_infer.onnx"),
            Path.Combine(ModelFolder, "ppocrv5_latin_dict.txt"));
        logger.LogInformation("Loaded the second OCR engine's models in {Ms} ms.", (DateTime.UtcNow - started).TotalMilliseconds);
        return ocr;
    }

    /// <summary>Each word as Tesseract would report it: text, confidence 0–100, and an upright box. Each detected line counts as its own line and block.</summary>
    private static List<OcrWord> ToWords(OcrResult result)
    {
        var words = new List<OcrWord>();
        var line = 0;
        foreach (var block in result.TextBlocks)
        {
            line++;
            foreach (var word in block.WordResults ?? [])
            {
                var text = word.Text?.Trim();
                if (string.IsNullOrEmpty(text) || word.BoxPoints is not { Length: > 0 } points)
                {
                    continue;
                }

                var left = (int)points.Min(p => p.X);
                var top = (int)points.Min(p => p.Y);
                var right = (int)points.Max(p => p.X);
                var bottom = (int)points.Max(p => p.Y);
                words.Add(new OcrWord(text, Math.Clamp(word.Score * 100, 0, 100), left, top, Math.Max(1, right - left), Math.Max(1, bottom - top), line, 1, 1));
            }
        }

        return words;
    }

    public void Dispose()
    {
        _ocr?.Dispose();
        _one.Dispose();
    }
}
