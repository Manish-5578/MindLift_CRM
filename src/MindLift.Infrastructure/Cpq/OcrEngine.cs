using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Documents.Text;

namespace MindLift.Infrastructure.Cpq;

public sealed class OcrOptions
{
    public string TesseractPath { get; set; } = @"C:\Program Files\Tesseract-OCR\tesseract.exe";
    public string? TessdataPath { get; set; }
    public string Languages { get; set; } = "eng+ara";
    public int OcrDpi { get; set; } = 300;
    public int ViewDpi { get; set; } = 150;
    public int ThumbWidth { get; set; } = 200;
    public int PageTimeoutSeconds { get; set; } = 60;
    public int MaxFileSizeMb { get; set; } = 25;

    /// <summary>Pages read from one PDF. Later pages are not read.</summary>
    public int MaxPages { get; set; } = 50;

    /// <summary>The longest one document may take to read, in seconds. Longer and the read stops and the document is marked Failed.</summary>
    public int MaxDocumentSeconds { get; set; } = 300;

    /// <summary>How many documents are read at the same time across the app.</summary>
    public int MaxConcurrentDocuments { get; set; } = 2;

    /// <summary>How long a document waits for a place when that many are already being read, in seconds, before it is told to try again.</summary>
    public int QueueSeconds { get; set; } = 30;

    /// <summary>The most pixels a rendered PDF page may have. A bigger page is rendered at a lower resolution, down to <see cref="MinRenderDpi"/>.</summary>
    public int MaxPagePixels { get; set; } = 40_000_000;

    /// <summary>The lowest resolution a large PDF page is rendered at. A page that would need less is skipped.</summary>
    public int MinRenderDpi { get; set; } = 100;

    /// <summary>The most pixels an uploaded picture may have. A bigger one is refused before it is opened for reading.</summary>
    public int MaxImagePixels { get; set; } = 60_000_000;

    /// <summary>The most characters read from a plain text file.</summary>
    public int MaxTextCharacters { get; set; } = 2_000_000;

    /// <summary>The most output one Tesseract run may produce, in megabytes. A run that produces more is stopped.</summary>
    public int MaxOutputMegabytes { get; set; } = 20;

    /// <summary>Clean a picture before it is read: straighten it, raise its contrast, enlarge small ones. Turn off to hand the original to the engine unchanged.</summary>
    public bool EnhanceImages { get; set; } = true;

    /// <summary>Turn a page that was scanned or photographed sideways or upside down the right way up before reading it.</summary>
    public bool DetectOrientation { get; set; } = true;

    /// <summary>Pictures with more pixels than this are read as they are, without cleaning, to keep memory use bounded.</summary>
    public int EnhanceMaxPixels { get; set; } = 25_000_000;

    /// <summary>A page whose reading scores at least this (0–100) is accepted at once, without trying other ways of reading it.</summary>
    public double TargetPageConfidence { get; set; } = 85;

    /// <summary>A page that still scores below this (0–100) after every attempt is flagged for a person to check.</summary>
    public double MinPageConfidence { get; set; } = 70;

    /// <summary>The most times one page is read, each with a different clean-up or layout setting, while the score stays low.</summary>
    public int MaxAttemptsPerPage { get; set; } = 6;

    /// <summary>
    /// Ask a second engine (PaddleOCR's models, run in the app) to read a page that is still weak after every attempt; its reading is kept only when it scores better.
    /// Its models read Latin script only, so a page where Arabic was found is not given to it.
    /// </summary>
    public bool SecondEngine { get; set; } = true;
}

public sealed record OcrRequest(string Languages, int PageSegMode = 3, string? CharWhitelist = null, int? Dpi = null);
public sealed record OcrWord(string Text, double Confidence, int Left, int Top, int Width, int Height, int Block, int Paragraph, int Line);
public sealed record OcrPageResult(IReadOnlyList<OcrWord> Words, int ImageWidth, int ImageHeight);

public interface IOcrEngine
{
    bool IsAvailable();
    Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest request, CancellationToken ct);
}

/// <summary>The engine's view of which way up a page is: the degrees to turn it clockwise to make it upright (0, 90, 180 or 270), and how sure it is (about 2 and over is firm).</summary>
public sealed record OrientationGuess(int Degrees, double Confidence);

/// <summary>An engine that can tell which way up a page is.</summary>
public interface IOrientationDetector
{
    /// <summary>The engine's guess, or null when the page has too little text for it to say anything.</summary>
    Task<OrientationGuess?> DetectRotationAsync(string imagePath, CancellationToken ct);
}

/// <summary>An engine that can write the pages back out as a PDF with the recognised text hidden behind the picture, so the file can be searched and copied from.</summary>
public interface ISearchablePdfWriter
{
    Task CreateSearchablePdfAsync(IReadOnlyList<string> imagePaths, string outputPdfPath, string languages, CancellationToken ct);
}

public sealed class TesseractCliOcrEngine : IOcrEngine, IOrientationDetector, ISearchablePdfWriter
{
    private readonly OcrOptions _options;
    private readonly ILogger<TesseractCliOcrEngine> _logger;
    private bool? _isAvailableCached;

    public TesseractCliOcrEngine(IOptions<OcrOptions> options, ILogger<TesseractCliOcrEngine> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAvailable()
    {
        if (_isAvailableCached.HasValue) return _isAvailableCached.Value;

        try
        {
            var path = _options.TesseractPath;
            if (File.Exists(path))
            {
                _isAvailableCached = true;
                return true;
            }

            // Check if on PATH
            using var proc = Process.Start(new ProcessStartInfo("tesseract", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (proc != null && proc.WaitForExit(3000) && proc.ExitCode == 0)
            {
                _isAvailableCached = true;
                return true;
            }
        }
        catch
        {
            // not available
        }

        _isAvailableCached = false;
        return false;
    }

    private string ExecutablePath => File.Exists(_options.TesseractPath) ? _options.TesseractPath : "tesseract";

    private void AddTessdata(ProcessStartInfo psi)
    {
        if (!string.IsNullOrEmpty(_options.TessdataPath))
        {
            psi.ArgumentList.Add("--tessdata-dir");
            psi.ArgumentList.Add(_options.TessdataPath);
        }
    }

    /// <summary>
    /// Runs Tesseract with a time limit and a ceiling on its output, and stops it when either is passed.
    /// <paramref name="describe"/> names what was being read, for the messages.
    /// </summary>
    private async Task<(string Stdout, string Stderr, int ExitCode)> RunAsync(Action<ProcessStartInfo> arguments, int timeoutSeconds, string describe, CancellationToken ct)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("Tesseract OCR engine is not available on this server.");
        }

        var psi = new ProcessStartInfo(ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        // One thread per run: Tesseract otherwise uses every processor for a single page, and several runs at once would starve the app.
        psi.Environment["OMP_THREAD_LIMIT"] = "1";
        arguments(psi);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Tesseract process.");
        try
        {
            proc.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            // Not allowed here: it runs at normal priority.
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)));

        // The output is read with a ceiling. A run that goes past it is stopped, so a page of noise cannot fill memory.
        var maxOutput = Math.Max(1, _options.MaxOutputMegabytes) * 1_000_000;
        var stdoutTask = Task.Run(async () =>
        {
            var read = await BoundedText.ReadAsync(proc.StandardOutput, maxOutput, drain: false, cts.Token);
            if (read.Truncated)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
            }

            return read;
        }, cts.Token);
        var stderrTask = BoundedText.ReadAsync(proc.StandardError, 64_000, drain: true, cts.Token);

        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"Tesseract OCR processing timed out on {describe}");
        }

        var (stdout, tooMuchOutput) = await stdoutTask;
        var (stderr, _) = await stderrTask;

        if (tooMuchOutput)
        {
            throw new InvalidOperationException($"Tesseract produced more than {_options.MaxOutputMegabytes} MB of output for {describe}, so it was stopped.");
        }

        return (stdout, stderr, proc.ExitCode);
    }

    public async Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest req, CancellationToken ct)
    {
        var (stdout, stderr, exitCode) = await RunAsync(psi =>
        {
            psi.ArgumentList.Add(imagePath);
            psi.ArgumentList.Add("stdout");
            psi.ArgumentList.Add("-l");
            psi.ArgumentList.Add(string.IsNullOrWhiteSpace(req.Languages) ? _options.Languages : req.Languages);
            psi.ArgumentList.Add("--oem");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("--psm");
            psi.ArgumentList.Add(req.PageSegMode.ToString(CultureInfo.InvariantCulture));
            if (req.Dpi is { } dpi and > 0)
            {
                // A picture file often has no resolution recorded, and Tesseract then guesses a low one that hurts small print.
                psi.ArgumentList.Add("--dpi");
                psi.ArgumentList.Add(dpi.ToString(CultureInfo.InvariantCulture));
            }

            AddTessdata(psi);
            if (req.CharWhitelist is not null)
            {
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add($"tessedit_char_whitelist={req.CharWhitelist}");
            }

            // TSV output is a config setting for Tesseract, not a file name, so it is passed with -c.
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("tessedit_create_tsv=1");
        }, _options.PageTimeoutSeconds, imagePath, ct);

        if (exitCode != 0)
        {
            _logger.LogError("Tesseract failed with exit code {Code}: {Error}", exitCode, stderr);
            throw new InvalidOperationException($"Tesseract failed with exit code {exitCode}: {stderr}");
        }

        return new OcrPageResult(ParseTsv(stdout), 0, 0);
    }

    public async Task<OrientationGuess?> DetectRotationAsync(string imagePath, CancellationToken ct)
    {
        try
        {
            var (stdout, _, exitCode) = await RunAsync(psi =>
            {
                psi.ArgumentList.Add(imagePath);
                psi.ArgumentList.Add("stdout");
                psi.ArgumentList.Add("--psm");
                psi.ArgumentList.Add("0");
                psi.ArgumentList.Add("-l");
                psi.ArgumentList.Add("osd");
                AddTessdata(psi);
            }, Math.Min(_options.PageTimeoutSeconds, 30), imagePath, ct);

            // A page with too little text ends with an error or no answer. That means "cannot tell", not a failure.
            return exitCode == 0 ? ParseOrientation(stdout) : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
        {
            _logger.LogDebug(exception, "Page orientation could not be checked for {Image}", imagePath);
            return null;
        }
    }

    /// <summary>Reads the rotation and its confidence from Tesseract's orientation report. A report with no rotation in it gives null.</summary>
    public static OrientationGuess? ParseOrientation(string report)
    {
        int? rotate = null;
        double confidence = 0;
        foreach (var line in report.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            if (key == "Rotate" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var degrees))
            {
                rotate = ((degrees % 360) + 360) % 360;
            }
            else if (key == "Orientation confidence" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var c))
            {
                confidence = c;
            }
        }

        return rotate is { } turn ? new OrientationGuess(turn, confidence) : null;
    }

    public async Task CreateSearchablePdfAsync(IReadOnlyList<string> imagePaths, string outputPdfPath, string languages, CancellationToken ct)
    {
        if (imagePaths.Count == 0)
        {
            throw new ArgumentException("At least one page is needed.", nameof(imagePaths));
        }

        var listFile = Path.Combine(Path.GetTempPath(), $"ocrpages_{Guid.NewGuid():N}.txt");
        // Tesseract appends .pdf to the base name it is given.
        var baseName = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPdfPath))!, Path.GetFileNameWithoutExtension(outputPdfPath));
        try
        {
            await File.WriteAllLinesAsync(listFile, imagePaths, new UTF8Encoding(false), ct);
            var (_, stderr, exitCode) = await RunAsync(psi =>
            {
                psi.ArgumentList.Add(listFile);
                psi.ArgumentList.Add(baseName);
                psi.ArgumentList.Add("-l");
                psi.ArgumentList.Add(string.IsNullOrWhiteSpace(languages) ? _options.Languages : languages);
                psi.ArgumentList.Add("--oem");
                psi.ArgumentList.Add("1");
                psi.ArgumentList.Add("--psm");
                psi.ArgumentList.Add("3");
                psi.ArgumentList.Add("--dpi");
                psi.ArgumentList.Add(Math.Max(1, _options.OcrDpi).ToString(CultureInfo.InvariantCulture));
                AddTessdata(psi);
                // The PDF output is a setting rather than the named "pdf" config file, so it works with a tessdata folder that holds only language data.
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add("tessedit_create_pdf=1");
            }, Math.Max(1, _options.PageTimeoutSeconds) * imagePaths.Count, $"{imagePaths.Count} pages", ct);

            if (exitCode != 0 || !File.Exists(outputPdfPath))
            {
                _logger.LogError("Tesseract could not write a searchable PDF (exit code {Code}): {Error}", exitCode, stderr);
                throw new InvalidOperationException($"Tesseract could not write a searchable PDF (exit code {exitCode}): {stderr}");
            }
        }
        finally
        {
            try { File.Delete(listFile); } catch (IOException) { }
        }
    }

    public static List<OcrWord> ParseTsv(string tsv)
    {
        var words = new List<OcrWord>();
        if (string.IsNullOrWhiteSpace(tsv)) return words;

        var lines = tsv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        // Skip header line: level page_num block_num par_num line_num word_num left top width height conf text
        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split('\t');
            if (cols.Length < 12) continue;

            if (int.TryParse(cols[0], out var level) && level == 5) // level 5 is word
            {
                if (double.TryParse(cols[10], NumberStyles.Any, CultureInfo.InvariantCulture, out var conf) && conf >= 0)
                {
                    var text = cols[11].Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        int.TryParse(cols[2], out var block);
                        int.TryParse(cols[3], out var par);
                        int.TryParse(cols[4], out var line);
                        int.TryParse(cols[6], out var left);
                        int.TryParse(cols[7], out var top);
                        int.TryParse(cols[8], out var width);
                        int.TryParse(cols[9], out var height);

                        words.Add(new OcrWord(text, conf, left, top, width, height, block, par, line));
                    }
                }
            }
        }

        return words;
    }
}
