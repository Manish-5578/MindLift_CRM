using Microsoft.Extensions.Logging;
using MindLift.Infrastructure.Cpq;
using SkiaSharp;

namespace MindLift.Infrastructure.Documents.Text;

/// <summary>One page, put the right way up and straightened, ready to be read in several ways. Dispose it to free the pictures.</summary>
public sealed class PreparedPage : IDisposable
{
    private readonly SKBitmap _oriented;
    private readonly SKBitmap _straight;
    private GrayImage? _enhanced;
    private GrayImage? _binarized;
    private GrayImage? _restored;
    private GrayImage? _denoised;

    private PreparedPage(SKBitmap oriented, SKBitmap straight, int originalWidth, int originalHeight, int quarterTurns, double skewDegrees, bool blank, bool orientationKnown, int? orientationHint)
    {
        OrientationKnown = orientationKnown;
        OrientationHint = orientationHint;
        _oriented = oriented;
        _straight = straight;
        OriginalWidth = originalWidth;
        OriginalHeight = originalHeight;
        QuarterTurns = quarterTurns;
        SkewDegrees = skewDegrees;
        Blank = blank;
    }

    /// <summary>The size of the picture as a person sees it (camera rotation applied).</summary>
    public int OriginalWidth { get; }
    public int OriginalHeight { get; }
    public int QuarterTurns { get; }
    public double SkewDegrees { get; }

    /// <summary>Nothing on the page is darker than the paper, so there is nothing to read.</summary>
    public bool Blank { get; }

    /// <summary>The engine was firm about which way up the page is (or the turn was chosen by the caller), so turning it again would not help.</summary>
    public bool OrientationKnown { get; }

    /// <summary>A rotation the engine suggested without being sure. It is not applied; it is the first one tried if the page then reads badly.</summary>
    public int? OrientationHint { get; }

    /// <summary>The engine is firm at or above this orientation confidence.</summary>
    public const double FirmOrientation = 2.0;

    /// <summary>Position map for the page turned the right way up but not straightened.</summary>
    public CoordinateMap OrientedMap => new(OriginalWidth, OriginalHeight, QuarterTurns, 0, _oriented.Width, _oriented.Height);

    /// <summary>Position map for the straightened page (the enhanced and black-and-white copies).</summary>
    public CoordinateMap StraightMap => new(OriginalWidth, OriginalHeight, QuarterTurns, SkewDegrees, _straight.Width, _straight.Height);

    /// <summary>The straightened page in grays with the contrast raised, enlarged when small.</summary>
    public GrayImage Enhanced => _enhanced ??= BuildEnhanced(enlarge: true);

    /// <summary>The enhanced page with specks removed, for pages that are dusty or grainy.</summary>
    public GrayImage Denoised => _denoised ??= ImageEnhancer.Denoise(Enhanced);

    /// <summary>The same, as pure black and white.</summary>
    public GrayImage Binarized => _binarized ??= ImageEnhancer.Binarize(Enhanced);

    /// <summary>The enhanced page sharpened, made black and white and cleared of specks, for pages that are both blurred and dirty.</summary>
    public GrayImage Restored => _restored ??= ImageEnhancer.Restore(Enhanced);

    /// <summary>The straightened page in grays with the contrast raised, at its own size. Used for the picture inside a searchable PDF.</summary>
    public GrayImage StraightGray() => BuildEnhanced(enlarge: false);

    public void SaveOriented(string path) => ImageEnhancer.SavePng(_oriented, path);

    private GrayImage BuildEnhanced(bool enlarge)
    {
        var factor = enlarge ? ImageEnhancer.UpscaleFactor(_straight.Width, _straight.Height) : 1;
        if (factor <= 1.0001)
        {
            return ImageEnhancer.StretchContrast(ImageEnhancer.ToGray(_straight));
        }

        using var bigger = ImageEnhancer.Enlarge(_straight, factor);
        return ImageEnhancer.StretchContrast(ImageEnhancer.ToGray(bigger));
    }

    /// <summary>
    /// Loads a picture and prepares it. When <paramref name="detector"/> is given, a page the engine is firm is lying on its side or upside down is turned upright.
    /// With <paramref name="forcedTurns"/> the page is turned by exactly that many clockwise quarter turns and the engine is not asked.
    /// Returns null when the file is not a picture that can be decoded.
    /// </summary>
    public static async Task<PreparedPage?> CreateAsync(string imagePath, IOrientationDetector? detector, int? forcedTurns, ILogger logger, CancellationToken ct)
    {
        var original = ImageEnhancer.Load(imagePath);
        if (original is null)
        {
            return null;
        }

        var gray = ImageEnhancer.ToGray(original);
        if (ImageEnhancer.IsBlank(gray))
        {
            return new PreparedPage(original, original.Copy(), original.Width, original.Height, 0, 0, blank: true, orientationKnown: true, orientationHint: null);
        }

        var turns = 0;
        var oriented = original;
        var known = forcedTurns is not null;
        int? hint = null;
        if (forcedTurns is { } forced)
        {
            turns = ((forced % 4) + 4) % 4;
            if (turns != 0)
            {
                oriented = ImageEnhancer.RotateQuarterTurns(original, turns);
                gray = ImageEnhancer.ToGray(oriented);
            }
        }
        else if (detector is not null)
        {
            var probe = Path.Combine(Path.GetTempPath(), $"ocrprobe_{Guid.NewGuid():N}.png");
            try
            {
                ImageEnhancer.SavePng(ImageEnhancer.StretchContrast(gray), probe);
                var guess = await detector.DetectRotationAsync(probe, ct);
                if (guess is { Degrees: > 0 } turn && turn.Degrees % 90 == 0)
                {
                    if (turn.Confidence >= FirmOrientation)
                    {
                        turns = turn.Degrees / 90;
                        known = true;
                        oriented = ImageEnhancer.RotateQuarterTurns(original, turns);
                        gray = ImageEnhancer.ToGray(oriented);
                        logger.LogInformation("The page was turned {Degrees} degrees clockwise to put it upright.", turn.Degrees);
                    }
                    else
                    {
                        hint = turn.Degrees;
                    }
                }
                else if (guess is { Degrees: 0 } && guess.Confidence >= FirmOrientation)
                {
                    known = true;
                }
            }
            finally
            {
                try { File.Delete(probe); } catch (IOException) { }
            }
        }

        var skew = ImageEnhancer.EstimateSkew(gray);
        var straight = skew == 0 ? oriented.Copy() : ImageEnhancer.Rotate(oriented, skew);
        if (skew != 0)
        {
            logger.LogInformation("The page was straightened by {Degrees} degrees.", skew);
        }

        var page = new PreparedPage(oriented, straight, original.Width, original.Height, turns, skew, blank: false, known, hint);
        if (!ReferenceEquals(oriented, original))
        {
            original.Dispose();
        }

        return page;
    }

    public void Dispose()
    {
        _oriented.Dispose();
        _straight.Dispose();
    }
}

/// <summary>The result of reading one page: its text and word boxes, and how sure the reading is.</summary>
public sealed record OcrPageReading(IReadOnlyList<PageWord> Words, string Text, double Confidence, bool LowConfidence, int Attempts);

/// <summary>
/// Reads one page picture as well as it can. The page is turned upright and straightened, its contrast is raised and a small picture is enlarged.
/// It is then read; when the reading scores low it is read again a different way (the original picture, a black-and-white copy, another layout setting),
/// and finally by any other engine that is set up. The best reading is kept. Word boxes are always reported against the original picture.
/// </summary>
public sealed class OcrPagePipeline(IOcrEngine primary, IReadOnlyList<IOcrEngine> fallbacks, OcrOptions options, ILogger logger)
{
    private enum Variant { Enhanced, Original, Denoised, Restored, Binarized }

    /// <summary><c>Vertical</c>: the words found are taller than wide, so the text runs up or down the picture and the page is lying on its side.</summary>
    private sealed record Attempt(PageQuality Quality, string Text, List<PageWord> Words, int QuarterTurns, bool Vertical);

    public async Task<OcrPageReading> ReadAsync(string imagePath, int width, int height, CancellationToken ct)
    {
        var request = new OcrRequest(options.Languages, 3, null, options.OcrDpi);
        if (!options.EnhanceImages || (long)width * height > options.EnhanceMaxPixels)
        {
            return await ReadAsIsAsync(imagePath, width, height, request, blankIsKnown: false, ct);
        }

        using var page = await PreparedPage.CreateAsync(imagePath, options.DetectOrientation ? primary as IOrientationDetector : null, null, logger, ct);
        if (page is null)
        {
            return await ReadAsIsAsync(imagePath, width, height, request, blankIsKnown: false, ct);
        }

        if (page.Blank)
        {
            // Nothing is darker than the paper, so one look is enough and no cleaning is done.
            return await ReadAsIsAsync(imagePath, width, height, request, blankIsKnown: true, ct);
        }

        var temp = new List<string>();
        try
        {
            string PathFor(Variant variant)
            {
                var path = Path.Combine(Path.GetTempPath(), $"ocrpage_{Guid.NewGuid():N}.png");
                temp.Add(path);
                switch (variant)
                {
                    case Variant.Enhanced: ImageEnhancer.SavePng(page.Enhanced, path); break;
                    case Variant.Denoised: ImageEnhancer.SavePng(page.Denoised, path); break;
                    case Variant.Binarized: ImageEnhancer.SavePng(page.Binarized, path); break;
                    case Variant.Restored: ImageEnhancer.SavePng(page.Restored, path); break;
                    default: page.SaveOriented(path); break;
                }

                return path;
            }

            // Different looks at the page, cheapest and most likely first. Each is tried only while the score is still low.
            (Variant Variant, int Layout)[] plan = [(Variant.Enhanced, 3), (Variant.Original, 3), (Variant.Denoised, 3), (Variant.Restored, 3), (Variant.Binarized, 3), (Variant.Enhanced, 6), (Variant.Binarized, 11)];
            var attempts = new List<Attempt>();
            var paths = new Dictionary<Variant, string>();
            foreach (var (variant, layout) in plan.Take(Math.Max(1, options.MaxAttemptsPerPage)))
            {
                ct.ThrowIfCancellationRequested();
                if (!paths.TryGetValue(variant, out var path))
                {
                    paths[variant] = path = PathFor(variant);
                }

                var (map, picture) = variant switch
                {
                    Variant.Enhanced => (page.StraightMap, page.Enhanced),
                    Variant.Denoised => (page.StraightMap, page.Denoised),
                    Variant.Binarized => (page.StraightMap, page.Binarized),
                    Variant.Restored => (page.StraightMap, page.Restored),
                    _ => (page.OrientedMap, null),
                };
                var (pictureWidth, pictureHeight) = picture is null ? (map.StraightWidth, map.StraightHeight) : (picture.Width, picture.Height);

                var result = await primary.RecognizeAsync(path, request with { PageSegMode = layout }, ct);
                var attempt = ToAttempt(result.Words, pictureWidth, pictureHeight, map);
                attempts.Add(attempt);
                logger.LogDebug("Read {Variant} with layout {Layout}: {Quality}", variant, layout, OcrQuality.Describe(attempt.Quality));
                if (attempt.Quality.Score >= options.TargetPageConfidence)
                {
                    break;
                }
            }

            var best = attempts[OcrQuality.Best(attempts.Select(a => a.Quality).ToList())];

            // A page that reads like noise may simply be lying on its side or upside down with the engine unable to tell. The turns are tried, the engine's hint first.
            if (best.Quality.Score < RescueBelow && options.DetectOrientation && !page.OrientationKnown)
            {
                attempts.AddRange(await TryOtherTurnsAsync(imagePath, page, request, TurnOrder(page, 2, 1, 3), ct));
                best = attempts[OcrQuality.Best(attempts.Select(a => a.Quality).ToList())];
            }

            if (best.Quality.Score < options.MinPageConfidence)
            {
                var extra = await TryOtherEnginesAsync(paths.TryGetValue(Variant.Enhanced, out var enhancedPath) ? enhancedPath : PathFor(Variant.Enhanced), page, request, HasNonLatinLetters(best.Text), ct);
                attempts.AddRange(extra);
                best = attempts[OcrQuality.Best(attempts.Select(a => a.Quality).ToList())];
            }

            return new OcrPageReading(best.Words, best.Text, best.Quality.Score, best.Quality.Score < options.MinPageConfidence, attempts.Count);
        }
        finally
        {
            foreach (var path in temp)
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }
    }

    /// <summary>Below this score a page reads like noise rather than like a poor page, which is when turning it is worth the extra readings.</summary>
    private const double RescueBelow = 55;

    /// <summary>The extra quarter turns to try, the engine's hint first, then the rest of <paramref name="candidates"/>.</summary>
    private static List<int> TurnOrder(PreparedPage page, params int[] candidates)
    {
        var order = new List<int>();
        if (page.OrientationHint is { } hint && candidates.Contains(hint / 90))
        {
            order.Add(hint / 90);
        }

        order.AddRange(candidates.Where(t => !order.Contains(t)));
        return order;
    }

    private async Task<List<Attempt>> TryOtherTurnsAsync(string imagePath, PreparedPage page, OcrRequest request, IReadOnlyList<int> order, CancellationToken ct)
    {
        var attempts = new List<Attempt>();
        foreach (var extra in order)
        {
            ct.ThrowIfCancellationRequested();
            var turns = (page.QuarterTurns + extra) % 4;
            using var turned = await PreparedPage.CreateAsync(imagePath, null, turns, logger, ct);
            if (turned is null || turned.Blank)
            {
                continue;
            }

            var path = Path.Combine(Path.GetTempPath(), $"ocrturn_{Guid.NewGuid():N}.png");
            try
            {
                ImageEnhancer.SavePng(turned.Enhanced, path);
                var result = await primary.RecognizeAsync(path, request, ct);
                var attempt = ToAttempt(result.Words, turned.Enhanced.Width, turned.Enhanced.Height, turned.StraightMap);
                attempts.Add(attempt);
                logger.LogDebug("Read the page turned {Turns} quarter turn(s): {Quality}", turns, OcrQuality.Describe(attempt.Quality));
                if (attempt.Quality.Score >= options.TargetPageConfidence)
                {
                    break;
                }
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        return attempts;
    }

    /// <summary>
    /// Works out how many clockwise quarter turns put a page upright, for a caller that needs the page itself turned (a searchable copy) rather than read.
    /// A firm answer from the engine is taken as it is. Otherwise the page is read as it lies, and if that reads like noise the other turns are read too and the best wins.
    /// </summary>
    public async Task<int> DetermineQuarterTurnsAsync(string imagePath, CancellationToken ct)
    {
        using var page = await PreparedPage.CreateAsync(imagePath, options.DetectOrientation ? primary as IOrientationDetector : null, null, logger, ct);
        if (page is null)
        {
            return 0;
        }

        if (page.Blank || page.OrientationKnown || !options.DetectOrientation)
        {
            return page.QuarterTurns;
        }

        var request = new OcrRequest(options.Languages, 3, null, options.OcrDpi);
        var path = Path.Combine(Path.GetTempPath(), $"ocrturn_{Guid.NewGuid():N}.png");
        Attempt asItLies;
        try
        {
            ImageEnhancer.SavePng(page.Enhanced, path);
            var result = await primary.RecognizeAsync(path, request, ct);
            asItLies = ToAttempt(result.Words, page.Enhanced.Width, page.Enhanced.Height, page.StraightMap);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }

        // Reading well does not mean the page is upright: a page on its side can be read, with its words running up the picture.
        // The turns tried are only the ones that could help: a page that reads like noise may be turned any way, a page whose text runs vertically is on its side.
        List<int> order;
        if (asItLies.Vertical)
        {
            order = TurnOrder(page, 1, 3);
        }
        else if (asItLies.Quality.Score < RescueBelow)
        {
            order = TurnOrder(page, 2, 1, 3);
        }
        else
        {
            return page.QuarterTurns;
        }

        var attempts = new List<Attempt> { asItLies };
        attempts.AddRange(await TryOtherTurnsAsync(imagePath, page, request, order, ct));

        // A turn that leaves the text running vertically is not the answer, however well it read.
        var upright = attempts.Where(a => !a.Vertical).ToList();
        var pool = upright.Count > 0 ? upright : attempts;
        return pool[OcrQuality.Best(pool.Select(a => a.Quality).ToList())].QuarterTurns;
    }

    /// <summary>True when the text has letters from a script other than Latin, such as Arabic.</summary>
    private static bool HasNonLatinLetters(string text) => text.Any(c => char.IsLetter(c) && c > 'ɏ');

    private async Task<List<Attempt>> TryOtherEnginesAsync(string enhancedPath, PreparedPage page, OcrRequest request, bool nonLatin, CancellationToken ct)
    {
        var attempts = new List<Attempt>();
        foreach (var engine in fallbacks.Where(e => !ReferenceEquals(e, primary) && !(nonLatin && e is ILatinOnlyOcrEngine) && e.IsAvailable()))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await engine.RecognizeAsync(enhancedPath, request, ct);
                attempts.Add(ToAttempt(result.Words, page.Enhanced.Width, page.Enhanced.Height, page.StraightMap));
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or IOException)
            {
                // A second opinion that fails is not a reason to lose the first reading.
                logger.LogWarning(exception, "The engine {Engine} could not read the page.", engine.GetType().Name);
            }
        }

        return attempts;
    }

    /// <summary>One look at the picture as it is, with no cleaning. Used when cleaning is off, the picture is too big to clean, or the page is blank.</summary>
    private async Task<OcrPageReading> ReadAsIsAsync(string imagePath, int width, int height, OcrRequest request, bool blankIsKnown, CancellationToken ct)
    {
        var result = await primary.RecognizeAsync(imagePath, request, ct);
        var pictureWidth = result.ImageWidth > 0 ? result.ImageWidth : width;
        var pictureHeight = result.ImageHeight > 0 ? result.ImageHeight : height;
        var attempt = ToAttempt(result.Words, pictureWidth, pictureHeight, CoordinateMap.Identity(Math.Max(pictureWidth, 1), Math.Max(pictureHeight, 1)));
        if (blankIsKnown && attempt.Words.Count == 0)
        {
            return new OcrPageReading([], string.Empty, 100, false, 1);
        }

        return new OcrPageReading(attempt.Words, attempt.Text, attempt.Quality.Score, attempt.Quality.Score < options.MinPageConfidence, 1);
    }

    /// <summary>
    /// Whether the text runs up or down the picture. Words of three or more characters are much wider than tall when a page is upright, in any script that is written in lines,
    /// and taller than wide when it lies on its side. At least three such words are needed to say.
    /// </summary>
    public static bool RunsVertically(IReadOnlyList<OcrWord> words)
    {
        var shapes = words.Where(w => w.Text.Length >= 3 && w.Height > 0).Select(w => (double)w.Width / w.Height).OrderBy(x => x).ToList();
        return shapes.Count >= 3 && shapes[shapes.Count / 2] < 0.8;
    }

    private static Attempt ToAttempt(IReadOnlyList<OcrWord> words, int pictureWidth, int pictureHeight, CoordinateMap map)
    {
        // Positions are fractions of the picture the engine read. The text is joined line by line on that picture, where the lines are level.
        var width = Math.Max(pictureWidth, 1);
        var height = Math.Max(pictureHeight, 1);
        var boxes = words.Select(w =>
        {
            var (x, y, boxWidth, boxHeight) = map.MapBox((double)w.Left / width, (double)w.Top / height, (double)w.Width / width, (double)w.Height / height);
            return new PageWord(w.Text, w.Confidence, x, y, boxWidth, boxHeight);
        }).ToList();
        var text = DocumentPageReader.JoinLines(words.Select(w => (w.Text, (double)-w.Top, (double)w.Height, (double)w.Left)));
        return new Attempt(OcrQuality.Evaluate(words), text, boxes, map.QuarterTurns, RunsVertically(words));
    }
}
