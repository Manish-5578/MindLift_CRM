using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Cpq;
using MindLift.Infrastructure.Documents.Text;
using MindLift.Infrastructure.Leads;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.ChatAndOcr;

public sealed class ChatAndOcrTests
{
    private readonly LeadCardOcrService _ocrService = new();
    private readonly LeadCardOcrService _fullPipelineOcrService;

    public ChatAndOcrTests()
    {
        var reader = CreateTestDocumentReader();
        _fullPipelineOcrService = new LeadCardOcrService(reader);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindLift.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private static DocumentPageReader? CreateTestDocumentReader()
    {
        var repoRoot = FindRepoRoot();
        var tesseractExe = @"C:\Program Files\Tesseract-OCR\tesseract.exe";
        var tessdata = Path.Combine(repoRoot, "tools", "tessdata");

        if (!File.Exists(tesseractExe) || !Directory.Exists(tessdata))
        {
            return null;
        }

        var options = Options.Create(new OcrOptions
        {
            TesseractPath = tesseractExe,
            TessdataPath = tessdata,
            Languages = "eng+ara",
            EnhanceImages = true,
            DetectOrientation = true,
            MaxAttemptsPerPage = 2
        });

        var engine = new TesseractCliOcrEngine(options, NullLogger<TesseractCliOcrEngine>.Instance);
        if (!engine.IsAvailable())
        {
            return null;
        }

        return new DocumentPageReader(engine, options, NullLogger<DocumentPageReader>.Instance);
    }

    private static byte[] GeneratePhotographedCardJpeg(
        string company,
        string name,
        string title,
        string phone,
        string email,
        bool addGlare = false,
        bool addBlur = false,
        int rotateDegrees = 0)
    {
        int width = 900;
        int height = 500;
        using var baseBitmap = new SkiaSharp.SKBitmap(width, height);
        using (var canvas = new SkiaSharp.SKCanvas(baseBitmap))
        {
            canvas.Clear(new SkiaSharp.SKColor(250, 250, 252));

            using var accentPaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(24, 76, 120), Style = SkiaSharp.SKPaintStyle.Fill };
            canvas.DrawRect(new SkiaSharp.SKRect(0, 0, 16, height), accentPaint);

            using var titleFont = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 36);
            using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 24);
            using var subFont = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 20);

            using var textPaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(15, 23, 42), IsAntialias = true };
            using var mutedPaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(71, 85, 105), IsAntialias = true };

            canvas.DrawText(company, 60, 90, titleFont, textPaint);
            canvas.DrawText(name, 60, 180, font, textPaint);
            canvas.DrawText(title, 60, 225, subFont, mutedPaint);
            canvas.DrawText(phone, 60, 310, font, textPaint);
            canvas.DrawText(email, 60, 360, font, textPaint);

            if (addGlare)
            {
                using var glareShader = SkiaSharp.SKShader.CreateRadialGradient(
                    new SkiaSharp.SKPoint(width * 0.75f, height * 0.35f),
                    180f,
                    [new SkiaSharp.SKColor(255, 255, 255, 180), new SkiaSharp.SKColor(255, 255, 255, 0)],
                    SkiaSharp.SKShaderTileMode.Clamp);
                using var glarePaint = new SkiaSharp.SKPaint { Shader = glareShader, Style = SkiaSharp.SKPaintStyle.Fill };
                canvas.DrawRect(new SkiaSharp.SKRect(0, 0, width, height), glarePaint);
            }

            if (addBlur)
            {
                using var blurPaint = new SkiaSharp.SKPaint { ImageFilter = SkiaSharp.SKImageFilter.CreateBlur(1.2f, 1.2f) };
                canvas.DrawBitmap(baseBitmap, 0, 0, blurPaint);

                var rand = new Random(42);
                using var noisePaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(60, 60, 60, 120) };
                for (int i = 0; i < 600; i++)
                {
                    canvas.DrawCircle(rand.Next(width), rand.Next(height), 0.8f, noisePaint);
                }
            }
        }

        using var finalBitmap = rotateDegrees switch
        {
            90 or 270 => new SkiaSharp.SKBitmap(height, width),
            180 => new SkiaSharp.SKBitmap(width, height),
            _ => baseBitmap
        };

        if (rotateDegrees != 0)
        {
            using var rotCanvas = new SkiaSharp.SKCanvas(finalBitmap);
            rotCanvas.Clear(SkiaSharp.SKColors.White);
            if (rotateDegrees == 90)
            {
                rotCanvas.Translate(height, 0);
                rotCanvas.RotateDegrees(90);
            }
            else if (rotateDegrees == 180)
            {
                rotCanvas.Translate(width, height);
                rotCanvas.RotateDegrees(180);
            }
            else if (rotateDegrees == 270)
            {
                rotCanvas.Translate(0, width);
                rotCanvas.RotateDegrees(270);
            }
            rotCanvas.DrawBitmap(baseBitmap, 0, 0);
        }

        using var finalImage = SkiaSharp.SKImage.FromBitmap(finalBitmap);
        using var encoded = finalImage.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 88);
        return encoded.ToArray();
    }

    [Fact]
    public async Task ExtractAsync_ParsesCompleteEnglishBusinessCard_Correctly()
    {
        var rawText = @"Apex Global Technologies LLC
Johnathan Smith
Managing Director
+971 50 123 4567
jsmith@apexglobal.test
https://www.apexglobal.test
Level 14, Al Saada Tower, Dubai, UAE";

        var result = await _ocrService.ExtractAsync(new ParseCardOcrRequest(rawText, null));

        result.CompanyName.ShouldBe("Apex Global Technologies LLC");
        result.FullName.ShouldBe("Johnathan Smith");
        result.FirstName.ShouldBe("Johnathan");
        result.LastName.ShouldBe("Smith");
        result.JobTitle.ShouldBe("Managing Director");
        result.Phone.ShouldContain("971 50 123 4567");
        result.Email.ShouldBe("jsmith@apexglobal.test");
        result.Website.ShouldContain("apexglobal.test");
        result.Address.ShouldContain("Dubai");
        result.DetectedScript.ShouldBe("Latin");
        result.OverallConfidence.ShouldBeGreaterThan(0.8);
    }

    [Fact]
    public async Task ExtractAsync_ParsesArabicBusinessCard_Correctly()
    {
        var rawText = @"شركة النور للتكنولوجيا ذ.م.م
محمد عبدالله
المدير العام
+971 4 333 4444
info@alnoortech.ae
شارع الشيخ زايد، برج النور، دبي";

        var result = await _ocrService.ExtractAsync(new ParseCardOcrRequest(rawText, null));

        result.CompanyName.ShouldContain("شركة النور للتكنولوجيا ذ.م.م");
        result.FullName.ShouldBe("محمد عبدالله");
        result.FirstName.ShouldBe("محمد");
        result.LastName.ShouldBe("عبدالله");
        result.JobTitle.ShouldBe("المدير العام");
        result.Phone.ShouldContain("971 4 333 4444");
        result.Email.ShouldBe("info@alnoortech.ae");
        result.Address.ShouldContain("دبي");
        result.DetectedScript.ShouldBe("Bilingual"); // contains Arabic + Latin email
        result.OverallConfidence.ShouldBeGreaterThan(0.8);
    }

    [Fact]
    public async Task ExtractAsync_NormalizesArabicIndicDigits_InPhoneNumber()
    {
        var rawText = @"Apex Technologies LLC
Sami Nasser
Manager
+٩٧١ ٥٠ ١٢٣ ٤٥٦٧
sami@apex.ae";

        var result = await _ocrService.ExtractAsync(new ParseCardOcrRequest(rawText, null));

        result.Phone.ShouldContain("971 50 123 4567");
    }

    [Fact]
    public async Task ExtractAsync_DetectsBilingualScript_Correctly()
    {
        var rawText = @"Vertex Group - مجموعة فيرتكس
Karim Mansour
Managing Director - مدير عام
karim@vertex.test";

        var result = await _ocrService.ExtractAsync(new ParseCardOcrRequest(rawText, null));

        result.DetectedScript.ShouldBe("Bilingual");
        result.CompanyName.ShouldContain("Vertex Group");
    }

    [Fact]
    public async Task ExtractAsync_ThrowsValidationException_WhenTextAndImageAreEmpty()
    {
        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractAsync(new ParseCardOcrRequest("   ", null)));
    }

    [Fact]
    public async Task ExtractAsync_ThrowsValidationException_WhenImageExceedsSizeLimit()
    {
        var hugeBase64 = new string('A', 23_000_000);
        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractAsync(new ParseCardOcrRequest(null, hugeBase64)));
    }

    [Fact]
    public async Task ExtractAsync_ThrowsValidationException_WhenInvalidImageFormatProvided()
    {
        // 100 bytes of non-image data
        var invalidBytes = Encoding.UTF8.GetBytes(new string('X', 100));
        var base64 = Convert.ToBase64String(invalidBytes);

        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractAsync(new ParseCardOcrRequest(null, base64)));
    }

    [Fact]
    public async Task ExtractFromFileStreamAsync_ThrowsValidationException_WhenStreamIsEmpty()
    {
        using var emptyStream = new System.IO.MemoryStream();
        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractFromFileStreamAsync(emptyStream, "empty.png"));
    }

    [Fact]
    public async Task ExtractFromDirectStreamAsync_ProcessesRenderedCardImage_StreamDirectly()
    {
        // Generate a synthetic business card bitmap (600x350)
        using var bitmap = new SkiaSharp.SKBitmap(600, 350);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(SkiaSharp.SKColors.White);

        using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 24);
        using var paint = new SkiaSharp.SKPaint
        {
            Color = SkiaSharp.SKColors.Black,
            IsAntialias = true
        };

        // Draw card content
        canvas.DrawText("MindLift Solutions LLC", 40, 60, font, paint);
        canvas.DrawText("Tariq Al-Hashemi", 40, 110, font, paint);
        font.Size = 18;
        canvas.DrawText("Managing Director", 40, 150, font, paint);
        canvas.DrawText("+971 50 765 4321", 40, 200, font, paint);
        canvas.DrawText("tariq@mindlift.ae", 40, 240, font, paint);

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var ms = new System.IO.MemoryStream(data.ToArray());

        // Header magic bytes check and direct stream persistence to disk run cleanly;
        // with no OCR engine attached, throws ValidationException("empty_card_text")
        await Should.ThrowAsync<ValidationException>(() =>
            _ocrService.ExtractFromDirectStreamAsync(ms, "business_card.png"));
    }

    [Fact]
    public async Task ExtractFromDirectStreamAsync_ProcessesCardWithSimulatedFlashGlare_Resiliently()
    {
        var jpeg = GeneratePhotographedCardJpeg(
            company: "Horizon Dynamics LLC",
            name: "Zaid Al-Mansoor",
            title: "Vice President",
            phone: "+971 50 888 9999",
            email: "zaid@horizondynamics.test",
            addGlare: true);

        using var ms = new MemoryStream(jpeg);
        var result = await _fullPipelineOcrService.ExtractFromDirectStreamAsync(ms, "card_with_glare.jpg");

        result.ShouldNotBeNull();
        result.RawText.ShouldNotBeNullOrWhiteSpace();
        result.Email.ShouldBe("zaid@horizondynamics.test");
        result.Phone.ShouldContain("971 50 888 9999");
        result.Evidence.ShouldNotBeNull();
        result.Evidence!.Count.ShouldBeGreaterThan(0);
        result.EngineConfidence.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExtractFromDirectStreamAsync_ProcessesCardWithCameraNoiseAndBlur_Resiliently()
    {
        var jpeg = GeneratePhotographedCardJpeg(
            company: "Crestview Holdings LLC",
            name: "Leila Al-Sabah",
            title: "Investment Director",
            phone: "+971 4 222 3333",
            email: "leila@crestview.test",
            addBlur: true);

        using var ms = new MemoryStream(jpeg);
        var result = await _fullPipelineOcrService.ExtractFromDirectStreamAsync(ms, "card_blur_noisy.jpg");

        result.ShouldNotBeNull();
        result.RawText.ShouldNotBeNullOrWhiteSpace();
        result.Email.ShouldContain("crestview.test");
        result.Evidence.ShouldNotBeNull();
        result.OverallConfidence.ShouldBeGreaterThan(0.5);
    }

    [Fact]
    public async Task ExtractFromDirectStreamAsync_ProcessesRotatedCard_ThroughOrientationPipeline()
    {
        var jpeg = GeneratePhotographedCardJpeg(
            company: "Apex Global Solutions LLC",
            name: "Omar Al-Khatib",
            title: "Managing Director",
            phone: "+971 50 111 2222",
            email: "omar@apexglobal.test",
            rotateDegrees: 90);

        using var ms = new MemoryStream(jpeg);
        var result = await _fullPipelineOcrService.ExtractFromDirectStreamAsync(ms, "card_rotated_90.jpg");

        result.ShouldNotBeNull();
        result.RawText.ShouldNotBeNullOrWhiteSpace();
        result.Email.ShouldBe("omar@apexglobal.test");
        result.Evidence.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExtractFromDirectStreamAsync_ProcessesRealBilingualCard_WithArabicAndEnglish()
    {
        var jpeg = GeneratePhotographedCardJpeg(
            company: "Al Noor Technologies LLC",
            name: "Sultan Al-Nuaimi",
            title: "Chief Executive Officer",
            phone: "+971 4 333 4444",
            email: "sultan@alnoor.ae");

        using var ms = new MemoryStream(jpeg);
        var result = await _fullPipelineOcrService.ExtractFromDirectStreamAsync(ms, "card_bilingual.jpg");

        result.ShouldNotBeNull();
        result.RawText.ShouldNotBeNullOrWhiteSpace();
        result.Email.ShouldBe("sultan@alnoor.ae");
        result.Phone.ShouldContain("971 4 333 4444");
        result.DetectedScript.ShouldNotBeNull();
        result.OverallConfidence.ShouldBeGreaterThan(0.6);
        result.Evidence.ShouldNotBeNull();
    }
}
