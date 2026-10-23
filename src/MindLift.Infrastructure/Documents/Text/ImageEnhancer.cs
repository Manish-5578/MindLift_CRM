using SkiaSharp;

namespace MindLift.Infrastructure.Documents.Text;

/// <summary>A picture reduced to one byte per pixel, 0 black to 255 white.</summary>
public sealed class GrayImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;
}

/// <summary>
/// Where a word found on a cleaned-up copy of a page sits on the page as the person sees it.
/// The copy may be turned by whole quarter turns, straightened by a small angle (the canvas grows to hold the turned page) and enlarged.
/// Enlarging changes nothing here because positions are fractions of the picture.
/// </summary>
public sealed record CoordinateMap(int OriginalWidth, int OriginalHeight, int QuarterTurns, double DeskewDegrees, int StraightWidth, int StraightHeight)
{
    public static CoordinateMap Identity(int width, int height) => new(width, height, 0, 0, width, height);

    private int OrientedWidth => QuarterTurns % 2 == 0 ? OriginalWidth : OriginalHeight;
    private int OrientedHeight => QuarterTurns % 2 == 0 ? OriginalHeight : OriginalWidth;

    /// <summary>Maps a box given as fractions of the cleaned copy to fractions of the original picture.</summary>
    public (double X, double Y, double Width, double Height) MapBox(double x, double y, double width, double height)
    {
        if (QuarterTurns == 0 && DeskewDegrees == 0)
        {
            return (x, y, width, height);
        }

        var corners = new[] { (x, y), (x + width, y), (x, y + height), (x + width, y + height) };
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (fx, fy) in corners)
        {
            var (ox, oy) = ToOriginalPixel(fx * StraightWidth, fy * StraightHeight);
            minX = Math.Min(minX, ox);
            minY = Math.Min(minY, oy);
            maxX = Math.Max(maxX, ox);
            maxY = Math.Max(maxY, oy);
        }

        var left = Math.Clamp(minX / OriginalWidth, 0, 1);
        var top = Math.Clamp(minY / OriginalHeight, 0, 1);
        var right = Math.Clamp(maxX / OriginalWidth, 0, 1);
        var bottom = Math.Clamp(maxY / OriginalHeight, 0, 1);
        return (left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private (double X, double Y) ToOriginalPixel(double px, double py)
    {
        // Undo the straightening: the page was turned by DeskewDegrees about its centre, onto a canvas of the straight size.
        if (DeskewDegrees != 0)
        {
            var radians = -DeskewDegrees * Math.PI / 180.0;
            var (sin, cos) = (Math.Sin(radians), Math.Cos(radians));
            var dx = px - StraightWidth / 2.0;
            var dy = py - StraightHeight / 2.0;
            px = dx * cos - dy * sin + OrientedWidth / 2.0;
            py = dx * sin + dy * cos + OrientedHeight / 2.0;
        }

        // Undo the quarter turns, which were clockwise.
        return QuarterTurns switch
        {
            1 => (py, OrientedWidth - px),
            2 => (OriginalWidth - px, OriginalHeight - py),
            3 => (OrientedHeight - py, px),
            _ => (px, py),
        };
    }
}

/// <summary>
/// Cleans a page picture so it reads better: puts it the right way up, straightens it, enlarges small pictures, raises the contrast,
/// and (when asked) turns it into pure black and white with a threshold that follows the lighting of each part of the page.
/// All of it is plain image arithmetic on SkiaSharp bitmaps, so it needs no other program.
/// </summary>
public static class ImageEnhancer
{
    /// <summary>The longest side below which a picture is enlarged, and the side it is enlarged to.</summary>
    private const int SmallSide = 1600;
    private const int TargetSide = 2400;
    private const double MaxUpscale = 3.0;

    /// <summary>The steepest tilt looked for, in degrees. Pages are rarely scanned more crooked than this.</summary>
    private const double MaxSkew = 10.0;

    /// <summary>Reads a picture the way a person sees it: the camera's rotation applied, and transparent parts turned white.</summary>
    public static SKBitmap? Load(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
        {
            return null;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = SKBitmap.Decode(codec, info);
        if (decoded is null)
        {
            return null;
        }

        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? decoded.Height : decoded.Width;
        var height = swap ? decoded.Width : decoded.Height;
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(OriginMatrix(codec.EncodedOrigin, decoded.Width, decoded.Height));
        canvas.DrawBitmap(decoded, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return result;
    }

    private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    /// <summary>Turns the picture clockwise by whole quarter turns.</summary>
    public static SKBitmap RotateQuarterTurns(SKBitmap source, int quarterTurns)
    {
        quarterTurns = ((quarterTurns % 4) + 4) % 4;
        if (quarterTurns == 0)
        {
            return source.Copy();
        }

        var w = source.Width;
        var h = source.Height;
        var swap = quarterTurns % 2 == 1;
        var result = new SKBitmap(new SKImageInfo(swap ? h : w, swap ? w : h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(quarterTurns switch
        {
            1 => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            2 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            _ => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        });
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return result;
    }

    /// <summary>Turns the picture clockwise by a small angle about its centre, on a canvas just big enough to hold the turned page, with white behind it.</summary>
    public static SKBitmap Rotate(SKBitmap source, double degrees)
    {
        if (degrees == 0)
        {
            return source.Copy();
        }

        var radians = degrees * Math.PI / 180.0;
        var (sin, cos) = (Math.Abs(Math.Sin(radians)), Math.Abs(Math.Cos(radians)));
        var width = (int)Math.Ceiling(source.Width * cos + source.Height * sin);
        var height = (int)Math.Ceiling(source.Width * sin + source.Height * cos);
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees((float)degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return result;
    }

    /// <summary>How much to enlarge a picture so small print becomes big enough to read. 1 means leave it.</summary>
    public static double UpscaleFactor(int width, int height)
    {
        var longest = Math.Max(width, height);
        if (longest <= 0 || longest >= SmallSide)
        {
            return 1;
        }

        return Math.Min(MaxUpscale, (double)TargetSide / longest);
    }

    public static SKBitmap Enlarge(SKBitmap source, double factor)
    {
        if (factor <= 1.0001)
        {
            return source.Copy();
        }

        var info = new SKImageInfo((int)Math.Round(source.Width * factor), (int)Math.Round(source.Height * factor), SKColorType.Rgba8888, SKAlphaType.Opaque);
        var result = new SKBitmap(info);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, new SKRect(0, 0, info.Width, info.Height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return result;
    }

    public static GrayImage ToGray(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixels = new byte[(long)width * height];
        var source = bitmap.GetPixelSpan();
        var stride = bitmap.RowBytes;
        var rgba = bitmap.ColorType == SKColorType.Rgba8888;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            var target = y * width;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                var (r, b) = rgba ? (source[i], source[i + 2]) : (source[i + 2], source[i]);
                var g = source[i + 1];
                pixels[target + x] = (byte)((299 * r + 587 * g + 114 * b) / 1000);
            }
        }

        return new GrayImage(width, height, pixels);
    }

    /// <summary>Spreads the used range of grays across the whole range, ignoring the extreme 1% at each end (dust, glare). A picture that already uses little range is left alone.</summary>
    public static GrayImage StretchContrast(GrayImage image)
    {
        var histogram = new long[256];
        foreach (var value in image.Pixels)
        {
            histogram[value]++;
        }

        var total = image.Pixels.LongLength;
        var (low, high) = (0, 255);
        long running = 0;
        for (var i = 0; i < 256; i++)
        {
            running += histogram[i];
            if (running >= total * 0.01)
            {
                low = i;
                break;
            }
        }

        running = 0;
        for (var i = 255; i >= 0; i--)
        {
            running += histogram[i];
            if (running >= total * 0.01)
            {
                high = i;
                break;
            }
        }

        if (high - low < 20 || (low <= 5 && high >= 250))
        {
            return image;
        }

        var map = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            map[i] = (byte)Math.Clamp((i - low) * 255.0 / (high - low), 0, 255);
        }

        var result = new byte[image.Pixels.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = map[image.Pixels[i]];
        }

        return new GrayImage(image.Width, image.Height, result);
    }

    /// <summary>The threshold that best separates dark from light (Otsu's method).</summary>
    public static int OtsuThreshold(GrayImage image)
    {
        var histogram = new long[256];
        foreach (var value in image.Pixels)
        {
            histogram[value]++;
        }

        double total = image.Pixels.LongLength;
        double sumAll = 0;
        for (var i = 0; i < 256; i++)
        {
            sumAll += i * (double)histogram[i];
        }

        double sumBackground = 0, weightBackground = 0, bestVariance = -1;
        var best = 127;
        for (var t = 0; t < 256; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0)
            {
                continue;
            }

            var weightForeground = total - weightBackground;
            if (weightForeground == 0)
            {
                break;
            }

            sumBackground += t * (double)histogram[t];
            var meanBackground = sumBackground / weightBackground;
            var meanForeground = (sumAll - sumBackground) / weightForeground;
            var variance = weightBackground * weightForeground * Math.Pow(meanBackground - meanForeground, 2);
            if (variance > bestVariance)
            {
                bestVariance = variance;
                best = t;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether the picture has anything on it to read: it is blank when almost no pixel is clearly darker than the paper.
    /// A page is not blank just because it is mostly white, so the test counts pixels, not their share.
    /// </summary>
    public static bool IsBlank(GrayImage image)
    {
        var paper = Percentile(image, 0.5);
        var ink = 0L;
        var cutoff = paper - 60;
        foreach (var value in image.Pixels)
        {
            if (value < cutoff)
            {
                ink++;
            }
        }

        return ink < Math.Max(50, image.Pixels.LongLength / 100_000);
    }

    private static int Percentile(GrayImage image, double fraction)
    {
        var histogram = new long[256];
        foreach (var value in image.Pixels)
        {
            histogram[value]++;
        }

        var target = image.Pixels.LongLength * fraction;
        long running = 0;
        for (var i = 0; i < 256; i++)
        {
            running += histogram[i];
            if (running >= target)
            {
                return i;
            }
        }

        return 255;
    }

    /// <summary>
    /// How many degrees to turn the picture clockwise to make its lines of text horizontal (within ±10°), or 0 when it is already straight or there is too little to tell.
    /// The ink pixels are turned by each trial angle and counted by row; straight text piles its ink into a few rows, which makes that count the most uneven.
    /// </summary>
    public static double EstimateSkew(GrayImage image)
    {
        var step = Math.Max(1, (int)Math.Ceiling(Math.Max(image.Width, image.Height) / 1000.0));
        var small = new GrayImage((image.Width + step - 1) / step, (image.Height + step - 1) / step, new byte[((image.Width + step - 1) / step) * ((image.Height + step - 1) / step)]);
        for (var y = 0; y < small.Height; y++)
        {
            for (var x = 0; x < small.Width; x++)
            {
                small.Pixels[y * small.Width + x] = image.Pixels[(long)(y * step) * image.Width + x * step];
            }
        }

        var threshold = OtsuThreshold(small);
        var dark = small.Pixels.LongCount(p => p <= threshold);
        var inkIsDark = dark <= small.Pixels.LongLength / 2;
        var points = new List<(float X, float Y)>();
        for (var y = 0; y < small.Height; y++)
        {
            for (var x = 0; x < small.Width; x++)
            {
                if ((small.Pixels[y * small.Width + x] <= threshold) == inkIsDark)
                {
                    points.Add((x, y));
                }
            }
        }

        if (points.Count < 400)
        {
            return 0;
        }

        // At most about 120,000 points are used, spread evenly.
        var every = Math.Max(1, points.Count / 120_000);
        if (every > 1)
        {
            points = points.Where((_, index) => index % every == 0).ToList();
        }

        double ScoreAt(double degrees)
        {
            var radians = degrees * Math.PI / 180.0;
            var (sin, cos) = ((float)Math.Sin(radians), (float)Math.Cos(radians));
            // A turn one way sends the far side of the picture above row 0, so the rows are shifted down by that much to keep every index positive.
            var offset = sin < 0 ? -small.Width * sin + 2 : 2;
            var bins = new int[(int)(small.Height * Math.Abs(cos) + small.Width * Math.Abs(sin) + 8)];
            foreach (var (x, y) in points)
            {
                bins[(int)(x * sin + y * cos + offset)]++;
            }

            double score = 0;
            foreach (var count in bins)
            {
                score += (double)count * count;
            }

            return score;
        }

        var baseline = ScoreAt(0);
        var (best, bestScore) = (0.0, baseline);
        for (var degrees = -MaxSkew; degrees <= MaxSkew + 1e-9; degrees += 0.5)
        {
            var score = ScoreAt(degrees);
            if (score > bestScore)
            {
                (best, bestScore) = (degrees, score);
            }
        }

        var centre = best;
        for (var degrees = centre - 0.5; degrees <= centre + 0.5 + 1e-9; degrees += 0.05)
        {
            var score = ScoreAt(degrees);
            if (score > bestScore)
            {
                (best, bestScore) = (degrees, score);
            }
        }

        // A tiny tilt is not worth the blur of turning the picture, and an unclear winner is noise.
        return Math.Abs(best) >= 0.3 && bestScore > baseline * 1.02 ? Math.Round(best, 2) : 0;
    }

    /// <summary>
    /// Pure black and white, using a threshold that follows the lighting of each part of the page (Sauvola's method over blocks), so a shadow or a bright patch does not wash out the text.
    /// </summary>
    public static GrayImage Binarize(GrayImage image)
    {
        const int block = 32;
        const double k = 0.3;
        const double range = 128.0;
        var columns = (image.Width + block - 1) / block;
        var rows = (image.Height + block - 1) / block;
        var sum = new double[columns * rows];
        var squares = new double[columns * rows];
        var count = new double[columns * rows];
        for (var y = 0; y < image.Height; y++)
        {
            var rowIndex = (y / block) * columns;
            var line = (long)y * image.Width;
            for (var x = 0; x < image.Width; x++)
            {
                double value = image.Pixels[line + x];
                var i = rowIndex + x / block;
                sum[i] += value;
                squares[i] += value * value;
                count[i]++;
            }
        }

        // Each block's statistics are pooled with its neighbours, so the window is about three blocks wide, larger than a letter.
        var threshold = new double[columns * rows];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                double s = 0, q = 0, n = 0;
                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        var rr = r + dr;
                        var cc = c + dc;
                        if (rr < 0 || cc < 0 || rr >= rows || cc >= columns)
                        {
                            continue;
                        }

                        var i = rr * columns + cc;
                        s += sum[i];
                        q += squares[i];
                        n += count[i];
                    }
                }

                var mean = s / n;
                var deviation = Math.Sqrt(Math.Max(0, q / n - mean * mean));
                threshold[r * columns + c] = mean * (1 + k * (deviation / range - 1));
            }
        }

        var result = new byte[image.Pixels.Length];
        for (var y = 0; y < image.Height; y++)
        {
            var rowIndex = (y / block) * columns;
            var line = (long)y * image.Width;
            for (var x = 0; x < image.Width; x++)
            {
                result[line + x] = image.Pixels[line + x] > threshold[rowIndex + x / block] ? (byte)255 : (byte)0;
            }
        }

        return new GrayImage(image.Width, image.Height, result);
    }

    /// <summary>
    /// Removes specks: each pixel becomes the median of itself and its eight neighbours. A dot a pixel or two wide, which is what dust and sensor noise leave, disappears,
    /// while strokes of text, which are wider, stay.
    /// </summary>
    public static GrayImage Denoise(GrayImage image)
    {
        var (width, height, source) = (image.Width, image.Height, image.Pixels);
        var result = new byte[source.Length];
        Span<byte> window = stackalloc byte[9];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var n = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var yy = Math.Clamp(y + dy, 0, height - 1);
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        window[n++] = source[(long)yy * width + Math.Clamp(x + dx, 0, width - 1)];
                    }
                }

                // Insertion sort of nine values; the middle one is the median.
                for (var i = 1; i < 9; i++)
                {
                    var value = window[i];
                    var j = i - 1;
                    while (j >= 0 && window[j] > value)
                    {
                        window[j + 1] = window[j];
                        j--;
                    }

                    window[j + 1] = value;
                }

                result[(long)y * width + x] = window[4];
            }
        }

        return new GrayImage(width, height, result);
    }

    /// <summary>
    /// Makes blurred edges crisp again (an unsharp mask): the difference between each pixel and a blurred copy of the picture is added back, doubled.
    /// Letters softened by a shaky hand or an out-of-focus lens get firm edges, so the black-and-white step keeps their shapes.
    /// </summary>
    public static GrayImage Sharpen(GrayImage image, double amount = 2.0)
    {
        var blurred = BoxBlur(BoxBlur(image, 2), 2);
        var result = new byte[image.Pixels.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var value = image.Pixels[i] + amount * (image.Pixels[i] - blurred.Pixels[i]);
            result[i] = (byte)Math.Clamp(Math.Round(value), 0, 255);
        }

        return new GrayImage(image.Width, image.Height, result);
    }

    /// <summary>
    /// The page made crisp and black and white, then cleared of specks: any dark blot much smaller than a letter is painted white.
    /// Meant for a blurred page with dirt on it, where a plain median filter is too small to catch the specks and too weak to undo the blur.
    /// </summary>
    public static GrayImage Restore(GrayImage image) => RemoveSpecks(Binarize(Sharpen(image)));

    /// <summary>
    /// Paints white each dark blot (in a black-and-white picture) whose box is small next to the page's letters: no wider or taller than
    /// a sixth of the usual letter height. Letters are kept, and so are most full stops and the dots of i and j, which are larger than that.
    /// </summary>
    public static GrayImage RemoveSpecks(GrayImage binary)
    {
        var (width, height) = (binary.Width, binary.Height);
        var pixels = (byte[])binary.Pixels.Clone();
        var label = new int[pixels.Length];
        var blots = new List<(int Size, int Left, int Top, int Right, int Bottom)>();
        var stack = new Stack<int>();
        for (var start = 0; start < pixels.Length; start++)
        {
            if (pixels[start] != 0 || label[start] != 0)
            {
                continue;
            }

            var id = blots.Count + 1;
            int size = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            label[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                var (x, y) = (at % width, at / width);
                size++;
                (left, right, top, bottom) = (Math.Min(left, x), Math.Max(right, x), Math.Min(top, y), Math.Max(bottom, y));
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (nx, ny) = (x + dx, y + dy);
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        var next = ny * width + nx;
                        if (pixels[next] == 0 && label[next] == 0)
                        {
                            label[next] = id;
                            stack.Push(next);
                        }
                    }
                }
            }

            blots.Add((size, left, top, right, bottom));
        }

        // The usual letter height. Specks can far outnumber letters, so only blots at least a third as tall as the tallest line-sized blot count
        // (anything taller than a tenth of the page is a picture or a ruled box, not a letter).
        var heights = blots.Select(b => b.Bottom - b.Top + 1).Where(h => h <= height / 10).ToList();
        if (heights.Count == 0)
        {
            return binary;
        }

        var tallest = heights.Max();
        var letters = heights.Where(h => h * 3 >= tallest).Order().ToList();
        if (letters.Count < 5)
        {
            return binary;
        }

        var letter = letters[letters.Count / 2];
        var limit = Math.Max(2, letter / 6);
        var speck = blots.Select(b => b.Right - b.Left + 1 <= limit && b.Bottom - b.Top + 1 <= limit).ToArray();
        for (var i = 0; i < pixels.Length; i++)
        {
            if (label[i] != 0 && speck[label[i] - 1])
            {
                pixels[i] = 255;
            }
        }

        return new GrayImage(width, height, pixels);
    }

    /// <summary>Each pixel becomes the average of the square around it, <paramref name="radius"/> pixels each way (a running sum along rows, then columns).</summary>
    private static GrayImage BoxBlur(GrayImage image, int radius)
    {
        var (width, height) = (image.Width, image.Height);
        var across = new byte[image.Pixels.Length];
        for (var y = 0; y < height; y++)
        {
            var line = y * width;
            var sum = 0;
            for (var x = -radius; x <= radius; x++)
            {
                sum += image.Pixels[line + Math.Clamp(x, 0, width - 1)];
            }

            for (var x = 0; x < width; x++)
            {
                across[line + x] = (byte)(sum / (2 * radius + 1));
                sum += image.Pixels[line + Math.Min(x + radius + 1, width - 1)] - image.Pixels[line + Math.Max(x - radius, 0)];
            }
        }

        var result = new byte[across.Length];
        for (var x = 0; x < width; x++)
        {
            var sum = 0;
            for (var y = -radius; y <= radius; y++)
            {
                sum += across[Math.Clamp(y, 0, height - 1) * width + x];
            }

            for (var y = 0; y < height; y++)
            {
                result[y * width + x] = (byte)(sum / (2 * radius + 1));
                sum += across[Math.Min(y + radius + 1, height - 1) * width + x] - across[Math.Max(y - radius, 0) * width + x];
            }
        }

        return new GrayImage(width, height, result);
    }

    /// <summary>Writes a gray picture as a PNG file.</summary>
    public static void SavePng(GrayImage image, string path)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        var target = bitmap.GetPixelSpan();
        for (var y = 0; y < image.Height; y++)
        {
            image.Pixels.AsSpan(y * image.Width, image.Width).CopyTo(target.Slice(y * bitmap.RowBytes, image.Width));
        }

        using var picture = SKImage.FromBitmap(bitmap);
        using var data = picture.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Writes a colour bitmap as a PNG file.</summary>
    public static void SavePng(SKBitmap bitmap, string path)
    {
        using var picture = SKImage.FromBitmap(bitmap);
        using var data = picture.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
