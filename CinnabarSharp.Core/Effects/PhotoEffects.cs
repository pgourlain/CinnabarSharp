using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Effects;

/// <summary>
/// The iPhone Photos "Adjust" sliders in one effect. Every slider is 0 when neutral; tone and color work on
/// 0–1 values, local-contrast sliders (brilliance, sharpness, definition, noise reduction) use blurred luminance.
/// Pure C#, deterministic, alpha kept.
/// </summary>
public sealed class PhotoAdjustEffect : Effect
{
    public const int Exposure = 0, Brilliance = 1, Highlights = 2, Shadows = 3, Contrast = 4, Brightness = 5,
        BlackPoint = 6, Saturation = 7, Vibrance = 8, Warmth = 9, Tint = 10, Sharpness = 11, Definition = 12,
        NoiseReduction = 13, Vignette = 14;

    /// <summary>Red/blue gain per unit of warmth, green gain per unit of tint (positive tint = magenta).</summary>
    public const double WarmthGain = 0.25, TintGain = 0.2;

    public override string Name => "Adjust Photo";
    public override string Category => EffectCategories.Photo;

    public override IReadOnlyList<EffectParameter> Parameters =>
    [
        new("Exposure", -100, 100, 0),
        new("Brilliance", -100, 100, 0),
        new("Highlights", -100, 100, 0),
        new("Shadows", -100, 100, 0),
        new("Contrast", -100, 100, 0),
        new("Brightness", -100, 100, 0),
        new("Black Point", -100, 100, 0),
        new("Saturation", -100, 100, 0),
        new("Vibrance", -100, 100, 0),
        new("Warmth", -100, 100, 0),
        new("Tint", -100, 100, 0),
        new("Sharpness", 0, 100, 0),
        new("Definition", 0, 100, 0),
        new("Noise Reduction", 0, 100, 0),
        new("Vignette", -100, 100, 0),
    ];

    public override IReadOnlyList<double>? SuggestValues(EffectContext context) => AutoEnhanceEffect.Analyze(context);

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> values,
        CancellationToken ct)
    {
        var v = values.Select(x => x / 100).ToArray();
        var fine = v[Sharpness] != 0 || v[NoiseReduction] != 0 ? PhotoMath.BlurredLuma(ctx, region, 1, ct) : null;
        var localRadius = Math.Max(3, Math.Min(ctx.Width, ctx.Height) / 60);
        var coarse = v[Definition] != 0 || v[Brilliance] != 0 ? PhotoMath.BlurredLuma(ctx, region, localRadius, ct) : null;
        var (cx, cy) = (ctx.Width / 2.0, ctx.Height / 2.0);
        var corner = Math.Sqrt(cx * cx + cy * cy);

        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var s = ctx.Index(x, y);
            double b = ctx.Source[s] / 255.0, g = ctx.Source[s + 1] / 255.0, r = ctx.Source[s + 2] / 255.0;
            var i = (y - region.Y) * region.Width + (x - region.X);
            var l = PhotoMath.Luma(r, g, b);
            var delta = 0.0;

            if (fine is not null)
            {
                var smooth = fine[i];
                // Edge-preserving: only smooth small differences (noise), sharpen the rest.
                var similar = Math.Exp(-Math.Pow((l - smooth) / 0.06, 2));
                delta += v[NoiseReduction] * similar * (smooth - l);
                delta += v[Sharpness] * 1.5 * (l - smooth);
            }
            if (coarse is not null)
            {
                var local = coarse[i];
                delta += v[Definition] * 0.8 * (l - local) * 4 * l * (1 - l);
                // Brilliance: brighten areas darker than their surroundings, tame brighter ones, lift midtones.
                delta += v[Brilliance] * (0.3 * (0.5 - local) * (1 - Math.Abs(2 * l - 1)) + 0.4 * l * (1 - l));
            }
            (r, g, b) = (r + delta, g + delta, b + delta);

            // White balance as channel gains (keeps ratios, so it survives exposure and contrast).
            r *= 1 + WarmthGain * v[Warmth] + TintGain / 2 * v[Tint];
            g *= 1 - TintGain * v[Tint];
            b *= 1 - WarmthGain * v[Warmth] + TintGain / 2 * v[Tint];

            var exposure = Math.Pow(2, v[Exposure] * 1.5);
            (r, g, b) = (r * exposure, g * exposure, b * exposure);

            l = Math.Clamp(PhotoMath.Luma(r, g, b), 0, 1);
            var tone = v[Highlights] * 0.35 * PhotoMath.Smoothstep(0.45, 1, l)
                       + v[Shadows] * 0.35 * (1 - PhotoMath.Smoothstep(0, 0.55, l));
            (r, g, b) = (r + tone, g + tone, b + tone);

            var contrast = 1 + v[Contrast] * 0.6;
            (r, g, b) = ((r - 0.5) * contrast + 0.5, (g - 0.5) * contrast + 0.5, (b - 0.5) * contrast + 0.5);

            var gamma = 1 / (1 + v[Brightness] * 0.5);
            (r, g, b) = (PhotoMath.Gamma(r, gamma), PhotoMath.Gamma(g, gamma), PhotoMath.Gamma(b, gamma));

            var black = v[BlackPoint] * 0.15;
            if (black > 0)
                (r, g, b) = ((r - black) / (1 - black), (g - black) / (1 - black), (b - black) / (1 - black));
            else if (black < 0)
                (r, g, b) = (r * (1 + black) - black, g * (1 + black) - black, b * (1 + black) - black);

            l = PhotoMath.Luma(r, g, b);
            var chroma = Math.Clamp(Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)), 0, 1);
            var saturation = (1 + v[Saturation]) * (1 + v[Vibrance] * (1 - chroma));
            (r, g, b) = (l + (r - l) * saturation, l + (g - l) * saturation, l + (b - l) * saturation);

            if (v[Vignette] != 0)
            {
                var d = Math.Sqrt(Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2)) / corner;
                var factor = 1 - v[Vignette] * 0.7 * PhotoMath.Smoothstep(0.35, 1, d);
                (r, g, b) = v[Vignette] > 0 ? (r * factor, g * factor, b * factor)
                    : (1 - (1 - r) * (2 - factor), 1 - (1 - g) * (2 - factor), 1 - (1 - b) * (2 - factor));
            }

            px[0] = Sampling.ToByte(b * 255);
            px[1] = Sampling.ToByte(g * 255);
            px[2] = Sampling.ToByte(r * 255);
            px[3] = ctx.Source[s + 3];
        });
    }
}

/// <summary>
/// One click: analyzes the photo (exposure, contrast range, clipped shadows/highlights, color cast, saturation)
/// and applies balanced <see cref="PhotoAdjustEffect"/> corrections.
/// </summary>
public sealed class AutoEnhanceEffect : Effect
{
    public override string Name => "Auto-Enhance";
    public override string Category => EffectCategories.Photo;

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> values,
        CancellationToken ct) =>
        new PhotoAdjustEffect().Render(ctx, region, dst, Analyze(ctx), ct);

    /// <summary><see cref="PhotoAdjustEffect"/> values that correct the photo; all zero for a balanced one.</summary>
    public static double[] Analyze(EffectContext ctx)
    {
        var v = new double[15];
        var histogram = new long[256];
        double sumR = 0, sumG = 0, sumB = 0, sumChroma = 0;
        long count = 0;
        // Sample at most ~250 000 pixels: enough for statistics, fast on 50-megapixel photos.
        var step = Math.Max(1, (int)Math.Sqrt((double)ctx.Width * ctx.Height / 250_000));
        for (var y = 0; y < ctx.Height; y += step)
        {
            for (var x = 0; x < ctx.Width; x += step)
            {
                var i = (y * ctx.Width + x) * 4;
                if (ctx.Source[i + 3] < 128)
                    continue;
                double b = ctx.Source[i], g = ctx.Source[i + 1], r = ctx.Source[i + 2];
                histogram[(int)Math.Round(PhotoMath.Luma(r, g, b))]++;
                (sumR, sumG, sumB) = (sumR + r, sumG + g, sumB + b);
                sumChroma += Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
                count++;
            }
        }
        if (count == 0)
            return v;

        double Percentile(double p)
        {
            long seen = 0;
            for (var i = 0; i < 256; i++)
                if ((seen += histogram[i]) >= p * count)
                    return i / 255.0;
            return 1;
        }
        var (p1, p5, median, p95, p99) = (Percentile(0.01), Percentile(0.05), Percentile(0.5), Percentile(0.95), Percentile(0.99));

        // Exposure: bring the median towards 0.45, at most ±0.6 stop.
        var stops = Math.Clamp(Math.Log2(0.45 / Math.Max(median, 0.02)), -0.6, 0.6);
        v[PhotoAdjustEffect.Exposure] = Math.Abs(stops) < 0.1 ? 0 : stops / 1.5 * 100;
        // Recover highlights that are clipped, open shadows that are blocked.
        if (p99 > 0.97)
            v[PhotoAdjustEffect.Highlights] = -Math.Min(40, (p99 - 0.97) * 1300);
        if (p5 < 0.06)
            v[PhotoAdjustEffect.Shadows] = Math.Min(35, (0.06 - p5) * 600);
        // Stretch a flat tonal range; set the black point when there are no real blacks.
        var range = p95 - p5;
        if (range < 0.65)
            v[PhotoAdjustEffect.Contrast] = Math.Min(30, (0.65 - range) * 80);
        if (p1 > 0.04)
            v[PhotoAdjustEffect.BlackPoint] = Math.Min(30, p1 * 250);
        // Gray-world white balance at half strength (square root of the correcting gain ratio).
        var (mr, mg, mb) = (sumR / count, sumG / count, sumB / count);
        if (Math.Min(mr, Math.Min(mg, mb)) > 5)
        {
            // Solve (1 + a·w) / (1 − a·w) = k for the warmth w that multiplies red/blue by k.
            var k = Math.Sqrt(mb / mr);
            v[PhotoAdjustEffect.Warmth] = Math.Clamp((k - 1) / (k + 1) / PhotoAdjustEffect.WarmthGain * 100, -50, 50);
            var kg = Math.Sqrt((mr + mb) / 2 / mg);
            v[PhotoAdjustEffect.Tint] = Math.Clamp(-(kg - 1) / (kg + 1) / (PhotoAdjustEffect.TintGain * 0.75) * 100, -40, 40);
        }
        // Dull photos get some vibrance.
        var chroma = sumChroma / count / 255;
        if (chroma < 0.25)
            v[PhotoAdjustEffect.Vibrance] = Math.Min(30, (0.25 - chroma) * 150);
        for (var i = 0; i < v.Length; i++)
            v[i] = Math.Round(v[i]);
        return v;
    }
}

/// <summary>iPhone-like filter presets, blended with the original by an intensity slider.</summary>
public sealed class PhotoFilterEffect : Effect
{
    public static IReadOnlyList<(string Name, double[] Adjust, bool Mono)> Presets { get; } =
    [
        ("Original", Values(), false),
        ("Vivid", Values(contrast: 15, saturation: 25, vibrance: 20), false),
        ("Vivid Warm", Values(contrast: 15, saturation: 20, vibrance: 20, warmth: 30), false),
        ("Vivid Cool", Values(contrast: 15, saturation: 20, vibrance: 20, warmth: -30), false),
        ("Dramatic", Values(contrast: 35, highlights: -30, shadows: -15, saturation: -20, definition: 30), false),
        ("Dramatic Warm", Values(contrast: 35, highlights: -30, shadows: -15, saturation: -10, warmth: 30, definition: 30), false),
        ("Dramatic Cool", Values(contrast: 35, highlights: -30, shadows: -15, saturation: -10, warmth: -30, definition: 30), false),
        ("Mono", Values(), true),
        ("Silvertone", Values(contrast: 20, brightness: 15, highlights: 15, warmth: -10), true),
        ("Noir", Values(contrast: 60, blackPoint: 35, highlights: -10), true),
    ];

    public override string Name => "Photo Filter";
    public override string Category => EffectCategories.Photo;
    public override bool HasCustomDialog => true;

    public override IReadOnlyList<EffectParameter> Parameters =>
    [
        new("Filter", 0, Presets.Count - 1, 1, Choices: Presets.Select(p => p.Name).ToList()),
        new("Intensity", 0, 100, 100),
    ];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> values,
        CancellationToken ct)
    {
        var (_, adjust, mono) = Presets[Math.Clamp((int)values[0], 0, Presets.Count - 1)];
        var intensity = Math.Clamp(values[1] / 100, 0, 1);
        new PhotoAdjustEffect().Render(ctx, region, dst, adjust, ct);
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            if (mono)
            {
                var gray = Sampling.ToByte(PhotoMath.Luma(px[2], px[1], px[0]));
                px[0] = px[1] = px[2] = gray;
            }
            var s = ctx.Index(x, y);
            for (var c = 0; c < 3; c++)
                px[c] = Sampling.ToByte(ctx.Source[s + c] + (px[c] - ctx.Source[s + c]) * intensity);
        });
    }

    private static double[] Values(double contrast = 0, double highlights = 0, double shadows = 0, double brightness = 0,
        double blackPoint = 0, double saturation = 0, double vibrance = 0, double warmth = 0, double definition = 0)
    {
        var v = new double[15];
        v[PhotoAdjustEffect.Contrast] = contrast;
        v[PhotoAdjustEffect.Highlights] = highlights;
        v[PhotoAdjustEffect.Shadows] = shadows;
        v[PhotoAdjustEffect.Brightness] = brightness;
        v[PhotoAdjustEffect.BlackPoint] = blackPoint;
        v[PhotoAdjustEffect.Saturation] = saturation;
        v[PhotoAdjustEffect.Vibrance] = vibrance;
        v[PhotoAdjustEffect.Warmth] = warmth;
        v[PhotoAdjustEffect.Definition] = definition;
        return v;
    }
}

/// <summary>
/// Rotates by a small angle and zooms just enough that the rotated photo still fills the whole frame
/// (automatic crop, as the iPhone's Straighten).
/// </summary>
public sealed class StraightenEffect : Effect
{
    public override string Name => "Straighten";
    public override string Category => EffectCategories.Photo;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Angle", -45, 45, 0, 0.1)];

    /// <summary>Zoom factor so that a w×h image rotated by <paramref name="degrees"/> covers the w×h frame.</summary>
    public static double CoverScale(double degrees, int width, int height)
    {
        var a = Math.Abs(degrees) * Math.PI / 180;
        var ratio = Math.Max((double)width / height, (double)height / width);
        return Math.Cos(a) + ratio * Math.Sin(a);
    }

    /// <summary>The angle that levels the photo's dominant horizontal/vertical lines (Auto button).</summary>
    public override IReadOnlyList<double>? SuggestValues(EffectContext context) => [DetectAngle(context)];

    /// <summary>Longer side of the downscaled copy the detection works on.</summary>
    private const int DetectSize = 512;

    /// <summary>
    /// Finds the tilt of the photo (horizon, buildings, door frames) and returns the Straighten angle that cancels it,
    /// rounded to 0.1°; 0 when there are no clear edges. Strong edges are split into horizontal-ish and vertical-ish
    /// ones by their gradient; for each candidate tilt they are projected across their direction, and the tilt whose
    /// projection is the most concentrated (edges falling on the same lines) wins: coarse 0.5° steps, then 0.05°.
    /// </summary>
    public static double DetectAngle(EffectContext context)
    {
        var (px, w, h) = PhotoMath.Downscale(context.Source, context.Width, context.Height, DetectSize);
        if (w < 8 || h < 8)
            return 0;

        var luma = new double[w * h];
        for (var i = 0; i < luma.Length; i++)
        {
            var a = px[i * 4 + 3] / 255.0;
            luma[i] = a * PhotoMath.Luma(px[i * 4 + 2], px[i * 4 + 1], px[i * 4]);
        }

        // Sobel gradients; keep the strongest edges (top 10%, and not just noise).
        var edges = new List<(int X, int Y, double Weight, bool Horizontal)>();
        var magnitudes = new double[w * h];
        var gradients = new (double Gx, double Gy)[w * h];
        for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                double L(int dx, int dy) => luma[(y + dy) * w + x + dx];
                var gx = L(1, -1) + 2 * L(1, 0) + L(1, 1) - L(-1, -1) - 2 * L(-1, 0) - L(-1, 1);
                var gy = L(-1, 1) + 2 * L(0, 1) + L(1, 1) - L(-1, -1) - 2 * L(0, -1) - L(1, -1);
                gradients[y * w + x] = (gx, gy);
                magnitudes[y * w + x] = Math.Sqrt(gx * gx + gy * gy);
            }
        var sorted = magnitudes.Where(m => m > 0).Order().ToArray();
        if (sorted.Length == 0)
            return 0;
        var threshold = Math.Max(40, sorted[(int)(sorted.Length * 0.9)]);
        for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                var m = magnitudes[y * w + x];
                if (m < threshold)
                    continue;
                var (gx, gy) = gradients[y * w + x];
                edges.Add((x, y, m, Math.Abs(gy) > Math.Abs(gx)));
            }
        if (edges.Count < 20)
            return 0;

        // Tilt t (image y down, positive = lines going down to the right) maps to Straighten angle -t.
        var diagonal = (int)Math.Ceiling(Math.Sqrt((double)w * w + h * h));
        var bins = new double[2 * (2 * diagonal + 1)];
        double Score(double degrees)
        {
            var t = degrees * Math.PI / 180;
            var (cos, sin) = (Math.Cos(t), Math.Sin(t));
            Array.Clear(bins);
            foreach (var (x, y, weight, horizontal) in edges)
            {
                // Distance across the tilted line direction: (cos t, sin t) for horizontal lines, (-sin t, cos t) for vertical.
                var across = horizontal ? y * cos - x * sin : x * cos + y * sin;
                bins[(horizontal ? 0 : 2 * diagonal + 1) + (int)Math.Round(across) + diagonal] += weight;
            }
            double score = 0;
            foreach (var b in bins)
                score += b * b;
            return score;
        }

        double Best(double from, double to, double step)
        {
            var (best, bestScore) = (0.0, double.MinValue);
            for (var i = 0; from + i * step <= to + 1e-9; i++)
            {
                var degrees = from + i * step;
                var score = Score(degrees);
                // Ties go to the smaller tilt: a flat profile means nothing to straighten.
                if (score > bestScore + 1e-9 || (Math.Abs(score - bestScore) <= 1e-9 && Math.Abs(degrees) < Math.Abs(best)))
                    (best, bestScore) = (degrees, score);
            }
            return best;
        }

        var coarse = Best(-45, 45, 0.5);
        var fine = Best(Math.Max(-45, coarse - 0.5), Math.Min(45, coarse + 0.5), 0.05);
        return Math.Clamp(Math.Round(-fine, 1), -45, 45) + 0.0;
    }

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> values,
        CancellationToken ct)
    {
        var angle = values[0] * Math.PI / 180;
        var scale = CoverScale(values[0], ctx.Width, ctx.Height);
        var (cos, sin) = (Math.Cos(angle) / scale, Math.Sin(angle) / scale);
        var (cx, cy) = (ctx.Width / 2.0, ctx.Height / 2.0);
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            // Inverse mapping of the destination pixel center to a source position (Bilinear takes centers too).
            var dx = x + 0.5 - cx;
            var dy = y + 0.5 - cy;
            Sampling.Bilinear(ctx, cx + dx * cos + dy * sin, cy - dx * sin + dy * cos, px);
        });
    }
}

public static class PhotoMath
{
    public static double Luma(double r, double g, double b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

    public static double Smoothstep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static double Gamma(double v, double gamma) => v <= 0 ? v : Math.Pow(v, gamma);

    /// <summary>Box-filtered copy whose longer side is at most <paramref name="maxSize"/> (for thumbnails).</summary>
    public static (byte[] Pixels, int Width, int Height) Downscale(byte[] bgra, int width, int height, int maxSize)
    {
        var scale = Math.Min(1, (double)maxSize / Math.Max(width, height));
        var (w, h) = (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
        var result = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            int sy0 = y * height / h, sy1 = Math.Max(sy0 + 1, (y + 1) * height / h);
            for (var x = 0; x < w; x++)
            {
                int sx0 = x * width / w, sx1 = Math.Max(sx0 + 1, (x + 1) * width / w);
                long b = 0, g = 0, r = 0, a = 0, n = 0;
                for (var sy = sy0; sy < sy1; sy++)
                    for (var sx = sx0; sx < sx1; sx++)
                    {
                        var i = (sy * width + sx) * 4;
                        (b, g, r, a, n) = (b + bgra[i], g + bgra[i + 1], r + bgra[i + 2], a + bgra[i + 3], n + 1);
                    }
                var o = (y * w + x) * 4;
                (result[o], result[o + 1], result[o + 2], result[o + 3]) = ((byte)(b / n), (byte)(g / n), (byte)(r / n), (byte)(a / n));
            }
        }
        return (result, w, h);
    }

    /// <summary>
    /// Luminance (0–1) of <paramref name="region"/> blurred with three box passes of <paramref name="radius"/>
    /// (close to a Gaussian), reading the source around the region so tiles match the whole image.
    /// </summary>
    public static float[] BlurredLuma(EffectContext ctx, RectangleI region, int radius, CancellationToken ct)
    {
        var margin = 3 * radius;
        var x0 = Math.Max(0, region.X - margin);
        var y0 = Math.Max(0, region.Y - margin);
        var x1 = Math.Min(ctx.Width, region.X + region.Width + margin);
        var y1 = Math.Min(ctx.Height, region.Y + region.Height + margin);
        var (w, h) = (x1 - x0, y1 - y0);
        var plane = new float[w * h];
        for (var y = 0; y < h; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (var x = 0; x < w; x++)
            {
                var i = ((y + y0) * ctx.Width + x + x0) * 4;
                plane[y * w + x] = (float)(Luma(ctx.Source[i + 2], ctx.Source[i + 1], ctx.Source[i]) / 255);
            }
        }

        var line = new float[Math.Max(w, h)];
        for (var pass = 0; pass < 3; pass++)
        {
            for (var y = 0; y < h; y++)
                BoxLine(plane, y * w, 1, w, radius, line);
            ct.ThrowIfCancellationRequested();
            for (var x = 0; x < w; x++)
                BoxLine(plane, x, w, h, radius, line);
        }

        var result = new float[region.Width * region.Height];
        for (var y = 0; y < region.Height; y++)
            for (var x = 0; x < region.Width; x++)
                result[y * region.Width + x] = plane[(y + region.Y - y0) * w + x + region.X - x0];
        return result;
    }

    // Running-sum box blur of one row or column, edges clamped.
    private static void BoxLine(float[] data, int start, int stride, int length, int radius, float[] line)
    {
        for (var i = 0; i < length; i++)
            line[i] = data[start + i * stride];
        var size = 2 * radius + 1;
        double sum = 0;
        for (var k = -radius; k <= radius; k++)
            sum += line[Math.Clamp(k, 0, length - 1)];
        for (var i = 0; i < length; i++)
        {
            data[start + i * stride] = (float)(sum / size);
            sum += line[Math.Min(i + radius + 1, length - 1)] - line[Math.Max(i - radius, 0)];
        }
    }
}
