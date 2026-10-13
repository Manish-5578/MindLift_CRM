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
    public int MaxPages { get; set; } = 50;
}

public sealed record OcrRequest(string Languages, int PageSegMode = 3, string? CharWhitelist = null);
public sealed record OcrWord(string Text, double Confidence, int Left, int Top, int Width, int Height, int Block, int Paragraph, int Line);
public sealed record OcrPageResult(IReadOnlyList<OcrWord> Words, int ImageWidth, int ImageHeight);

public interface IOcrEngine
{
    bool IsAvailable();
    Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest request, CancellationToken ct);
}

public sealed class TesseractCliOcrEngine : IOcrEngine
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

    public async Task<OcrPageResult> RecognizeAsync(string imagePath, OcrRequest req, CancellationToken ct)
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("Tesseract OCR engine is not available on this server.");
        }

        var tesseractPath = File.Exists(_options.TesseractPath) ? _options.TesseractPath : "tesseract";

        var psi = new ProcessStartInfo(tesseractPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };

        psi.ArgumentList.Add(imagePath);
        psi.ArgumentList.Add("stdout");
        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add(string.IsNullOrWhiteSpace(req.Languages) ? _options.Languages : req.Languages);
        psi.ArgumentList.Add("--oem");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("--psm");
        psi.ArgumentList.Add(req.PageSegMode.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrEmpty(_options.TessdataPath))
        {
            psi.ArgumentList.Add("--tessdata-dir");
            psi.ArgumentList.Add(_options.TessdataPath);
        }

        if (req.CharWhitelist is not null)
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"tessedit_char_whitelist={req.CharWhitelist}");
        }

        // TSV output is a config setting for Tesseract, not a file name, so it is passed with -c.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("tessedit_create_tsv=1");

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start Tesseract process.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.PageTimeoutSeconds));

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Tesseract OCR processing timed out on {imagePath}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0)
        {
            _logger.LogError("Tesseract failed with exit code {Code}: {Error}", proc.ExitCode, stderr);
            throw new InvalidOperationException($"Tesseract failed with exit code {proc.ExitCode}: {stderr}");
        }

        var words = ParseTsv(stdout);
        return new OcrPageResult(words, 0, 0);
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
