using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Effects;

public static class EffectCategories
{
    public const string Blurs = "Blurs";
    public const string Photo = "Photo";
    public const string Noise = "Noise";
    public const string Distort = "Distort";
    public const string Stylize = "Stylize";
    public const string Render = "Render";
}

// ---------------------------------------------------------------- Blurs

public sealed class GaussianBlurEffect : Effect
{
    public override string Name => "Gaussian Blur";
    public override string Category => EffectCategories.Blurs;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Radius", 0, 200, 2)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct) =>
        Blur(ctx, region, dst, (int)v[0], ct);

    /// <summary>Separable Gaussian (sigma = radius / 3) on premultiplied colors, in horizontal strips.</summary>
    public static void Blur(EffectContext ctx, RectangleI region, byte[] dst, int radius, CancellationToken ct)
    {
        if (radius <= 0)
        {
            PixelRegion.Extract(ctx.Source, ctx.Width, region).CopyTo(dst, 0);
            return;
        }
        var kernel = Kernel(radius);
        const int strip = 128;
        var w = region.Width;
        for (var y0 = 0; y0 < region.Height; y0 += strip)
        {
            var rows = Math.Min(strip, region.Height - y0);
            var top = region.Y + y0 - radius;
            var tmpRows = rows + 2 * radius;
            var tmp = new double[tmpRows * w * 4];

            for (var ty = 0; ty < tmpRows; ty++)
            {
                ct.ThrowIfCancellationRequested();
                var sy = top + ty;
                for (var x = 0; x < w; x++)
                {
                    double b = 0, g = 0, r = 0, a = 0;
                    for (var k = -radius; k <= radius; k++)
                    {
                        var i = ctx.Index(region.X + x + k, sy);
                        var wa = kernel[k + radius] * ctx.Source[i + 3];
                        b += wa * ctx.Source[i];
                        g += wa * ctx.Source[i + 1];
                        r += wa * ctx.Source[i + 2];
                        a += wa;
                    }
                    var t = (ty * w + x) * 4;
                    (tmp[t], tmp[t + 1], tmp[t + 2], tmp[t + 3]) = (b, g, r, a);
                }
            }

            for (var y = 0; y < rows; y++)
            {
                ct.ThrowIfCancellationRequested();
                for (var x = 0; x < w; x++)
                {
                    double b = 0, g = 0, r = 0, a = 0;
                    for (var k = 0; k <= 2 * radius; k++)
                    {
                        var t = ((y + k) * w + x) * 4;
                        b += kernel[k] * tmp[t];
                        g += kernel[k] * tmp[t + 1];
                        r += kernel[k] * tmp[t + 2];
                        a += kernel[k] * tmp[t + 3];
                    }
                    var o = ((y0 + y) * w + x) * 4;
                    if (a <= 0)
                    {
                        dst.AsSpan(o, 4).Clear();
                        continue;
                    }
                    (dst[o], dst[o + 1], dst[o + 2], dst[o + 3]) =
                        (Sampling.ToByte(b / a), Sampling.ToByte(g / a), Sampling.ToByte(r / a), Sampling.ToByte(a));
                }
            }
        }
    }

    private static double[] Kernel(int radius)
    {
        var sigma = Math.Max(0.5, radius / 3.0);
        var k = new double[2 * radius + 1];
        double sum = 0;
        for (var i = -radius; i <= radius; i++)
            sum += k[i + radius] = Math.Exp(-(i * i) / (2 * sigma * sigma));
        for (var i = 0; i < k.Length; i++)
            k[i] /= sum;
        return k;
    }
}

public sealed class MotionBlurEffect : Effect
{
    public override string Name => "Motion Blur";
    public override string Category => EffectCategories.Blurs;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Angle", -180, 180, 25), new("Distance", 1, 200, 10)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var angle = v[0] * Math.PI / 180;
        var distance = v[1];
        var samples = Math.Max(2, (int)distance + 1);
        var (dx, dy) = (Math.Cos(angle) * distance, -Math.Sin(angle) * distance);
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var avg = new Sampling.Average();
            for (var i = 0; i < samples; i++)
            {
                var t = (double)i / (samples - 1) - 0.5;
                avg.Add(ctx.Source.AsSpan(ctx.Index((int)Math.Round(x + dx * t), (int)Math.Round(y + dy * t)), 4));
            }
            avg.WriteTo(px);
        });
    }
}

/// <summary>Averages samples toward (zoom) or around (radial) the image center.</summary>
public sealed class ZoomBlurEffect : Effect
{
    public override string Name => "Zoom Blur";
    public override string Category => EffectCategories.Blurs;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Amount", 0, 100, 10)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var amount = v[0] / 100;
        var (cx, cy) = (ctx.Width / 2.0, ctx.Height / 2.0);
        const int samples = 24;
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var avg = new Sampling.Average();
            for (var i = 0; i < samples; i++)
            {
                var s = 1 - amount * i / samples;
                avg.Add(ctx.Source.AsSpan(ctx.Index((int)(cx + (x + 0.5 - cx) * s), (int)(cy + (y + 0.5 - cy) * s)), 4));
            }
            avg.WriteTo(px);
        });
    }
}

public sealed class RadialBlurEffect : Effect
{
    public override string Name => "Radial Blur";
    public override string Category => EffectCategories.Blurs;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Angle", 0, 90, 4)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var angle = v[0] * Math.PI / 180;
        var (cx, cy) = (ctx.Width / 2.0, ctx.Height / 2.0);
        const int samples = 24;
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var avg = new Sampling.Average();
            var (rx, ry) = (x + 0.5 - cx, y + 0.5 - cy);
            for (var i = 0; i < samples; i++)
            {
                var a = angle * ((double)i / (samples - 1) - 0.5);
                var (c, s) = (Math.Cos(a), Math.Sin(a));
                avg.Add(ctx.Source.AsSpan(ctx.Index((int)(cx + rx * c - ry * s), (int)(cy + rx * s + ry * c)), 4));
            }
            avg.WriteTo(px);
        });
    }
}

// ---------------------------------------------------------------- Photo

/// <summary>Unsharp mask: original + amount × (original − blurred).</summary>
public sealed class SharpenEffect : Effect
{
    public override string Name => "Sharpen";
    public override string Category => EffectCategories.Photo;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Amount", 1, 20, 10)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var amount = v[0] / 10;
        var blurred = new byte[dst.Length];
        GaussianBlurEffect.Blur(ctx, region, blurred, 2, ct);
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var s = ctx.Index(x, y);
            var o = ((y - region.Y) * region.Width + (x - region.X)) * 4;
            for (var c = 0; c < 3; c++)
                px[c] = Sampling.ToByte(ctx.Source[s + c] + amount * (ctx.Source[s + c] - blurred[o + c]));
            px[3] = ctx.Source[s + 3];
        });
    }
}

/// <summary>Screen-blends a brightened blur over the image.</summary>
public sealed class GlowEffect : Effect
{
    public override string Name => "Glow";
    public override string Category => EffectCategories.Photo;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Radius", 1, 50, 6), new("Brightness", -100, 100, 10)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var blurred = new byte[dst.Length];
        GaussianBlurEffect.Blur(ctx, region, blurred, (int)v[0], ct);
        var brightness = v[1] * 2.55;
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var s = ctx.Index(x, y);
            var o = ((y - region.Y) * region.Width + (x - region.X)) * 4;
            for (var c = 0; c < 3; c++)
            {
                var glow = Math.Clamp(blurred[o + c] + brightness, 0, 255);
                px[c] = Sampling.ToByte(255 - (255 - ctx.Source[s + c]) * (255 - glow) / 255);
            }
            px[3] = ctx.Source[s + 3];
        });
    }
}

public sealed class VignetteEffect : Effect
{
    public override string Name => "Vignette";
    public override string Category => EffectCategories.Photo;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Radius", 10, 200, 100), new("Strength", 0, 100, 60)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var (cx, cy) = (ctx.Width / 2.0, ctx.Height / 2.0);
        var radius = Math.Sqrt(cx * cx + cy * cy) * v[0] / 100;
        var strength = v[1] / 100;
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var d = Math.Sqrt(Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2)) / radius;
            var t = Math.Clamp(d, 0, 1);
            var factor = 1 - strength * t * t * (3 - 2 * t);
            var s = ctx.Index(x, y);
            for (var c = 0; c < 3; c++)
                px[c] = Sampling.ToByte(ctx.Source[s + c] * factor);
            px[3] = ctx.Source[s + 3];
        });
    }
}

// ---------------------------------------------------------------- Noise

public sealed class AddNoiseEffect : Effect
{
    public override string Name => "Add Noise";
    public override string Category => EffectCategories.Noise;
    public override IReadOnlyList<EffectParameter> Parameters =>
    [
        new("Intensity", 0, 100, 64),
        new("Color saturation", 0, 400, 100),
        new("Coverage", 0, 100, 100),
    ];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var intensity = v[0] * 1.28;
        var saturation = v[1] / 100;
        var coverage = v[2] / 100;
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var s = ctx.Index(x, y);
            ctx.Source.AsSpan(s, 4).CopyTo(px);
            if (Sampling.Hash(x, y, 1) >= coverage)
                return;
            var luma = Sampling.Noise(x, y, 2) * intensity;
            for (var c = 0; c < 3; c++)
                px[c] = Sampling.ToByte(px[c] + luma + Sampling.Noise(x, y, 10 + c) * intensity * saturation * 0.5);
        });
    }
}

/// <summary>Per-channel percentile (50 = median) over a square window, with sliding histograms.</summary>
public sealed class MedianEffect : Effect
{
    public override string Name => "Median";
    public override string Category => EffectCategories.Noise;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Radius", 1, 20, 3), new("Percentile", 0, 100, 50)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var r = (int)v[0];
        var count = (2 * r + 1) * (2 * r + 1);
        var rank = Math.Clamp((int)Math.Round(v[1] / 100 * (count - 1)), 0, count - 1);
        var hist = new int[4, 256];
        for (var y = 0; y < region.Height; y++)
        {
            ct.ThrowIfCancellationRequested();
            var sy = region.Y + y;
            Array.Clear(hist);
            for (var wy = -r; wy <= r; wy++)
                for (var wx = -r; wx <= r; wx++)
                    AddPixel(hist, ctx, region.X + wx, sy + wy, 1);

            for (var x = 0; x < region.Width; x++)
            {
                var o = (y * region.Width + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    int seen = 0, value = 0;
                    for (; value < 255; value++)
                        if ((seen += hist[c, value]) > rank)
                            break;
                    dst[o + c] = (byte)value;
                }
                var sx = region.X + x;
                for (var wy = -r; wy <= r; wy++)
                {
                    AddPixel(hist, ctx, sx - r, sy + wy, -1);
                    AddPixel(hist, ctx, sx + r + 1, sy + wy, 1);
                }
            }
        }
    }

    private static void AddPixel(int[,] hist, EffectContext ctx, int x, int y, int delta)
    {
        var i = ctx.Index(x, y);
        for (var c = 0; c < 4; c++)
            hist[c, ctx.Source[i + c]] += delta;
    }
}

// ---------------------------------------------------------------- Distort

public sealed class PixelateEffect : Effect
{
    public override string Name => "Pixelate";
    public override string Category => EffectCategories.Distort;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Cell size", 1, 100, 8)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var size = (int)v[0];
        var cells = new Dictionary<(int, int), byte[]>();
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var key = (x / size, y / size);
            if (!cells.TryGetValue(key, out var color))
            {
                var avg = new Sampling.Average();
                for (var cy = key.Item2 * size; cy < Math.Min(ctx.Height, (key.Item2 + 1) * size); cy++)
                    for (var cx = key.Item1 * size; cx < Math.Min(ctx.Width, (key.Item1 + 1) * size); cx++)
                        avg.Add(ctx.Source.AsSpan(ctx.Index(cx, cy), 4));
                color = new byte[4];
                avg.WriteTo(color);
                cells[key] = color;
            }
            color.CopyTo(px);
        });
    }
}

/// <summary>Base for distortions that map each output pixel to a source position around the image center.</summary>
public abstract class CenterDistortEffect : Effect
{
    public override string Category => EffectCategories.Distort;

    /// <summary>Source position for (dx, dy) relative to the center, with t = distance / radius (0..1 inside).</summary>
    protected abstract (double X, double Y) Map(double dx, double dy, double t, IReadOnlyList<double> v);

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var (cx, cy) = (ctx.Width / 2.0, ctx.Height / 2.0);
        var radius = Math.Min(cx, cy);
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var (dx, dy) = (x + 0.5 - cx, y + 0.5 - cy);
            var t = Math.Sqrt(dx * dx + dy * dy) / radius;
            if (t >= 1)
            {
                ctx.Source.AsSpan(ctx.Index(x, y), 4).CopyTo(px);
                return;
            }
            var (sx, sy) = Map(dx, dy, t, v);
            Sampling.Bilinear(ctx, cx + sx, cy + sy, px);
        });
    }
}

public sealed class BulgeEffect : CenterDistortEffect
{
    public override string Name => "Bulge";
    public override IReadOnlyList<EffectParameter> Parameters => [new("Amount", -100, 100, 45)];

    protected override (double X, double Y) Map(double dx, double dy, double t, IReadOnlyList<double> v)
    {
        var scale = 1 - v[0] / 100 * (1 - t) * (1 - t);
        return (dx * scale, dy * scale);
    }
}

public sealed class TwistEffect : CenterDistortEffect
{
    public override string Name => "Twist";
    public override IReadOnlyList<EffectParameter> Parameters => [new("Amount", -100, 100, 30)];

    protected override (double X, double Y) Map(double dx, double dy, double t, IReadOnlyList<double> v)
    {
        var a = v[0] / 100 * Math.PI * 2 * (1 - t) * (1 - t);
        var (c, s) = (Math.Cos(a), Math.Sin(a));
        return (dx * c - dy * s, dx * s + dy * c);
    }
}

public sealed class FrostedGlassEffect : Effect
{
    public override string Name => "Frosted Glass";
    public override string Category => EffectCategories.Distort;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Amount", 1, 50, 5)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var amount = v[0];
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var sx = x + (int)Math.Round(Sampling.Noise(x, y, 31) * amount);
            var sy = y + (int)Math.Round(Sampling.Noise(x, y, 37) * amount);
            ctx.Source.AsSpan(ctx.Index(sx, sy), 4).CopyTo(px);
        });
    }
}

// ---------------------------------------------------------------- Stylize

/// <summary>Directional 3×3 derivative (light from <c>angle</c>) — the base of Emboss, Relief and Edge Detect.</summary>
public abstract class DirectionalEffect : Effect
{
    public override string Category => EffectCategories.Stylize;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Angle", 0, 360, 45)];

    protected static double[] DerivativeKernel(double angleDegrees)
    {
        var a = angleDegrees * Math.PI / 180;
        var (lx, ly) = (Math.Cos(a), -Math.Sin(a));
        var k = new double[9];
        for (var j = -1; j <= 1; j++)
            for (var i = -1; i <= 1; i++)
                k[(j + 1) * 3 + i + 1] = -(i * lx + j * ly);
        return k;
    }

    protected static double Convolve(EffectContext ctx, int x, int y, double[] kernel, int channel)
    {
        double sum = 0;
        for (var j = -1; j <= 1; j++)
            for (var i = -1; i <= 1; i++)
                sum += kernel[(j + 1) * 3 + i + 1] * ctx.Source[ctx.Index(x + i, y + j) + channel];
        return sum;
    }

    protected static double Gray(EffectContext ctx, int x, int y, double[] kernel) =>
        (0.114 * Convolve(ctx, x, y, kernel, 0) + 0.587 * Convolve(ctx, x, y, kernel, 1) + 0.299 * Convolve(ctx, x, y, kernel, 2));
}

public sealed class EmbossEffect : DirectionalEffect
{
    public override string Name => "Emboss";

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var k = DerivativeKernel(v[0]);
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var g = Sampling.ToByte(128 + Gray(ctx, x, y, k));
            (px[0], px[1], px[2], px[3]) = (g, g, g, ctx.Source[ctx.Index(x, y) + 3]);
        });
    }
}

public sealed class ReliefEffect : DirectionalEffect
{
    public override string Name => "Relief";

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var k = DerivativeKernel(v[0]);
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            var shade = Gray(ctx, x, y, k);
            var s = ctx.Index(x, y);
            for (var c = 0; c < 3; c++)
                px[c] = Sampling.ToByte(ctx.Source[s + c] + shade);
            px[3] = ctx.Source[s + 3];
        });
    }
}

public sealed class EdgeDetectEffect : DirectionalEffect
{
    public override string Name => "Edge Detect";

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var k = DerivativeKernel(v[0]);
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            for (var c = 0; c < 3; c++)
                px[c] = Sampling.ToByte(128 + Convolve(ctx, x, y, k, c));
            px[3] = ctx.Source[ctx.Index(x, y) + 3];
        });
    }
}

// ---------------------------------------------------------------- Render

/// <summary>Fractal value noise blended from the primary to the secondary color.</summary>
public sealed class CloudsEffect : Effect
{
    public override string Name => "Clouds";
    public override string Category => EffectCategories.Render;
    public override IReadOnlyList<EffectParameter> Parameters =>
    [
        new("Scale", 2, 1000, 250),
        new("Roughness", 0, 100, 50),
        new("Seed", 0, 1000, 0),
    ];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var scale = v[0];
        var roughness = v[1] / 100;
        var seed = (int)v[2];
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            double value = 0, amplitude = 1, total = 0, frequency = 1 / scale;
            for (var octave = 0; octave < 8; octave++)
            {
                value += amplitude * ValueNoise(x * frequency, y * frequency, seed + octave * 101);
                total += amplitude;
                amplitude *= roughness;
                frequency *= 2;
            }
            var c = ColorBgra.Lerp(ctx.Primary, ctx.Secondary, Math.Clamp(value / total, 0, 1));
            (px[0], px[1], px[2], px[3]) = (c.B, c.G, c.R, c.A);
        });
    }

    private static double ValueNoise(double x, double y, int seed)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = Smooth(x - x0);
        var fy = Smooth(y - y0);
        var top = Lerp(Sampling.Hash(x0, y0, seed), Sampling.Hash(x0 + 1, y0, seed), fx);
        var bottom = Lerp(Sampling.Hash(x0, y0 + 1, seed), Sampling.Hash(x0 + 1, y0 + 1, seed), fx);
        return Lerp(top, bottom, fy);
    }

    private static double Smooth(double t) => t * t * (3 - 2 * t);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

public sealed class MandelbrotEffect : Effect
{
    public override string Name => "Mandelbrot Fractal";
    public override string Category => EffectCategories.Render;
    public override IReadOnlyList<EffectParameter> Parameters => [new("Zoom", 1, 50, 1), new("Iterations", 16, 512, 128)];

    public override void Render(EffectContext ctx, RectangleI region, byte[] dst, IReadOnlyList<double> v, CancellationToken ct)
    {
        var zoom = v[0];
        var maxIterations = (int)v[1];
        var scale = 3.0 / (Math.Min(ctx.Width, ctx.Height) * zoom);
        var (cx, cy) = (ctx.Width / 2.0, ctx.Height / 2.0);
        ForEachPixel(region, dst, ct, (x, y, px) =>
        {
            double re0 = (x + 0.5 - cx) * scale - 0.5, im0 = (y + 0.5 - cy) * scale;
            double re = 0, im = 0;
            var n = 0;
            for (; n < maxIterations && re * re + im * im <= 4; n++)
                (re, im) = (re * re - im * im + re0, 2 * re * im + im0);
            if (n == maxIterations)
            {
                (px[0], px[1], px[2], px[3]) = (0, 0, 0, 255);
                return;
            }
            var t = (double)n / maxIterations;
            (px[0], px[1], px[2], px[3]) = (Sampling.ToByte(255 * Math.Sqrt(t)), Sampling.ToByte(255 * t), Sampling.ToByte(255 * t * t), 255);
        });
    }
}

public static class EffectCatalog
{
    /// <summary>The Effects menu, grouped by <see cref="Effect.Category"/>.</summary>
    public static IReadOnlyList<Effect> Effects { get; } =
    [
        new GaussianBlurEffect(), new MotionBlurEffect(), new RadialBlurEffect(), new ZoomBlurEffect(),
        new GlowEffect(), new SharpenEffect(), new VignetteEffect(),
        new AddNoiseEffect(), new MedianEffect(),
        new BulgeEffect(), new FrostedGlassEffect(), new PixelateEffect(), new TwistEffect(),
        new EdgeDetectEffect(), new EmbossEffect(), new ReliefEffect(),
        new CloudsEffect(), new MandelbrotEffect(),
    ];

    /// <summary>The Photo menu (iPhone-like photo editing).</summary>
    public static IReadOnlyList<Effect> PhotoTools { get; } =
    [
        new AutoEnhanceEffect(), new PhotoAdjustEffect(), new PhotoFilterEffect(), new StraightenEffect(),
    ];

    public static IReadOnlyList<Effect> All { get; } = [.. Effects, .. PhotoTools];

    /// <summary>The Adjustments menu (per-pixel color adjustments), in menu order.</summary>
    public static IReadOnlyList<Effect> Adjustments { get; } =
    [
        new Adjustments.AutoLevel(), new Adjustments.BlackAndWhite(), new Adjustments.BrightnessContrast(),
        new Adjustments.Curves(), new Adjustments.HueSaturation(), new Adjustments.InvertColors(),
        new Adjustments.Levels(), new Adjustments.Posterize(), new Adjustments.Sepia(),
    ];
}
