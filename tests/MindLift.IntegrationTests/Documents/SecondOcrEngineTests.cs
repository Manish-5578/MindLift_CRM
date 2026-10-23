using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindLift.Infrastructure.Cpq;
using MindLift.Infrastructure.Documents.Text;
using Shouldly;
using SkiaSharp;

namespace MindLift.IntegrationTests.Documents;

/// <summary>The second engine (PaddleOCR's models through RapidOcrNet) reads a page on its own, and reports words the way the pipeline expects. No database is needed.</summary>
public sealed class SecondOcrEngineTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mindlift-second-ocr-{Guid.NewGuid():N}");

    public SecondOcrEngineTests() => Directory.CreateDirectory(_folder);

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

    private static RapidOcrEngine Engine(bool on = true) => new(Options.Create(new OcrOptions { SecondEngine = on }), NullLogger<RapidOcrEngine>.Instance);

    private string Page()
    {
        using var bitmap = new SKBitmap(1600, 600);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        using (var font = new SKFont(SKTypeface.Default, 60))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText("TRADE LICENSE", 60, 120, font, paint);
            canvas.DrawText("License No: CN-1234567", 60, 300, font, paint);
            canvas.DrawText("Expiry Date: 31/12/2027", 60, 480, font, paint);
        }

        var path = Path.Combine(_folder, "page.png");
        ImageEnhancer.SavePng(bitmap, path);
        return path;
    }

    [Fact]
    public async Task It_reads_a_page_and_places_each_word_where_it_is_drawn()
    {
        using var engine = Engine();
        engine.IsAvailable().ShouldBeTrue("the models should be copied next to the app");

        var result = await engine.RecognizeAsync(Page(), new OcrRequest("eng"), CancellationToken.None);

        var text = string.Join(' ', result.Words.Select(w => w.Text));
        text.ShouldContain("CN-1234567");
        text.ShouldContain("31/12/2027");
        result.ImageWidth.ShouldBe(1600);

        // The licence number is on the middle line, drawn with its baseline at 300 pixels.
        var number = result.Words.First(w => w.Text.Contains("CN-1234567"));
        number.Top.ShouldBeInRange(220, 300);
        (number.Top + number.Height).ShouldBeInRange(280, 330);
        number.Confidence.ShouldBeGreaterThan(80);
    }

    [Fact]
    public void It_is_not_used_when_switched_off()
    {
        using var engine = Engine(on: false);

        engine.IsAvailable().ShouldBeFalse();
    }
}
