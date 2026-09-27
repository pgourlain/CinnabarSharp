using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Adjustments;

/// <summary>
/// A per-pixel color adjustment (Paint.NET's Adjustments menu): an <see cref="Effect"/> whose result for a pixel
/// depends only on that pixel. <see cref="Create"/> receives the parameter values and the layer's original pixels
/// (for adjustments that need statistics, like Auto-Level) and returns the pixel function.
/// </summary>
public abstract class ColorAdjustment : Effect
{
    public abstract PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels);

    public override void Render(EffectContext context, RectangleI region, byte[] destination,
        IReadOnlyList<double> values, CancellationToken cancellation)
    {
        var function = Create(values, context.Source);
        ForEachPixel(region, destination, cancellation, (x, y, px) =>
        {
            context.Source.AsSpan(context.Index(x, y), 4).CopyTo(px);
            function(px);
        });
    }

    /// <summary>Transforms a pixel in place (straight-alpha BGRA; alpha is kept by every adjustment).</summary>
    public delegate void PixelFunction(Span<byte> pixel);

    protected static PixelFunction FromLookup(byte[] b, byte[] g, byte[] r) => pixel =>
    {
        pixel[0] = b[pixel[0]];
        pixel[1] = g[pixel[1]];
        pixel[2] = r[pixel[2]];
    };

    protected static PixelFunction FromLookup(byte[] lut) => FromLookup(lut, lut, lut);

    protected static byte[] Lookup(Func<int, double> f)
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
            lut[i] = (byte)Math.Clamp(Math.Round(f(i)), 0, 255);
        return lut;
    }

    /// <summary>Paint.NET's intensity: (7471 B + 38470 G + 19595 R) / 65536.</summary>
    protected static byte Intensity(ReadOnlySpan<byte> px) => (byte)((7471 * px[0] + 38470 * px[1] + 19595 * px[2]) >> 16);
}

public sealed class InvertColors : ColorAdjustment
{
    public override string Name => "Invert Colors";

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels) =>
        FromLookup(Lookup(i => 255 - i));
}

public sealed class BlackAndWhite : ColorAdjustment
{
    public override string Name => "Black and White";

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels) => pixel =>
    {
        var i = Intensity(pixel);
        pixel[0] = pixel[1] = pixel[2] = i;
    };
}

/// <summary>Desaturate, then warm the tones (red gamma 1.2, blue gamma 0.8), as Paint.NET does.</summary>
public sealed class Sepia : ColorAdjustment
{
    private static readonly byte[] Red = Lookup(i => 255 * Math.Pow(i / 255.0, 1 / 1.2));
    private static readonly byte[] Blue = Lookup(i => 255 * Math.Pow(i / 255.0, 1 / 0.8));

    public override string Name => "Sepia";

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels) => pixel =>
    {
        var i = Intensity(pixel);
        (pixel[0], pixel[1], pixel[2]) = (Blue[i], i, Red[i]);
    };
}

public sealed class BrightnessContrast : ColorAdjustment
{
    public override string Name => "Brightness / Contrast";

    public override IReadOnlyList<EffectParameter> Parameters =>
    [
        new("Brightness", -100, 100, 0),
        new("Contrast", -100, 100, 0),
    ];

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels)
    {
        var brightness = values[0] * 2.55;
        var c = values[1] * 2.55;
        var factor = 259 * (c + 255) / (255 * (259 - c));
        return FromLookup(Lookup(i => factor * (i - 128) + 128 + brightness));
    }
}

public sealed class Posterize : ColorAdjustment
{
    public override string Name => "Posterize";

    public override IReadOnlyList<EffectParameter> Parameters => [new("Levels", 2, 64, 16)];

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels)
    {
        var steps = (int)values[0] - 1;
        return FromLookup(Lookup(i => Math.Round(Math.Round(i * steps / 255.0) * 255.0 / steps)));
    }
}

/// <summary>Maps [input black, input white] to [output black, output white] with a gamma curve.</summary>
/// <summary>
/// Paint.NET's Levels: maps [input black, input white] to [output black, output white] with a gamma. Values are
/// either 5 numbers (the same levels for every channel) or 15 (red, green, then blue: see <see cref="PerChannel"/>).
/// </summary>
public sealed class Levels : ColorAdjustment
{
    public override string Name => "Levels";

    public override bool HasCustomDialog => true;

    public override IReadOnlyList<EffectParameter> Parameters =>
    [
        new("Input black", 0, 254, 0),
        new("Input white", 1, 255, 255),
        new("Gamma", 0.1, 10, 1, 0.01),
        new("Output black", 0, 255, 0),
        new("Output white", 0, 255, 255),
    ];

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels)
    {
        if (values.Count < 15)
            return FromLookup(Curve(values[0], values[1], values[2], values[3], values[4]));
        var r = LevelsChannel.From(values, 0).Lookup();
        var g = LevelsChannel.From(values, 5).Lookup();
        var b = LevelsChannel.From(values, 10).Lookup();
        return FromLookup(b, g, r);
    }

    public static IReadOnlyList<double> PerChannel(LevelsChannel red, LevelsChannel green, LevelsChannel blue) =>
        [.. red.Values, .. green.Values, .. blue.Values];

    public static byte[] Curve(double inBlack, double inWhite, double gamma, double outBlack, double outWhite)
    {
        inWhite = Math.Max(inWhite, inBlack + 1);
        return Lookup(i =>
        {
            var t = Math.Clamp((i - inBlack) / (inWhite - inBlack), 0, 1);
            return outBlack + Math.Pow(t, 1 / gamma) * (outWhite - outBlack);
        });
    }
}

/// <summary>Levels of one channel.</summary>
public readonly record struct LevelsChannel(double InBlack, double InWhite, double Gamma, double OutBlack, double OutWhite)
{
    public static LevelsChannel Identity => new(0, 255, 1, 0, 255);

    public IReadOnlyList<double> Values => [InBlack, InWhite, Gamma, OutBlack, OutWhite];

    public byte[] Lookup() => Levels.Curve(InBlack, InWhite, Gamma, OutBlack, OutWhite);

    public static LevelsChannel From(IReadOnlyList<double> values, int start) =>
        new(values[start], values[start + 1], values[start + 2], values[start + 3], values[start + 4]);

    /// <summary>Input range that clips the darkest and brightest 0.5 % of a histogram (Auto-Level).</summary>
    public static LevelsChannel Auto(IReadOnlyList<long> histogram)
    {
        var count = histogram.Sum();
        if (count == 0)
            return Identity;
        var clip = count * 0.005;
        int low = 0, high = 255;
        for (long seen = 0; low < 255 && (seen += histogram[low]) <= clip; low++) { }
        for (long seen = 0; high > 0 && (seen += histogram[high]) <= clip; high--) { }
        return high <= low ? Identity : Identity with { InBlack = low, InWhite = high };
    }
}

public enum CurvesMode
{
    /// <summary>One curve applied to the intensity; colors keep their hue.</summary>
    Luminosity,

    /// <summary>One curve per channel.</summary>
    Rgb,
}

/// <summary>Control points (0–255 on both axes, sorted by x) of Paint.NET's Curves adjustment.</summary>
public sealed record CurvesSettings(
    CurvesMode Mode,
    IReadOnlyList<PointI> Luminosity,
    IReadOnlyList<PointI> Red,
    IReadOnlyList<PointI> Green,
    IReadOnlyList<PointI> Blue)
{
    public static IReadOnlyList<PointI> Diagonal { get; } = [new(0, 0), new(255, 255)];

    public static CurvesSettings Identity { get; } = new(CurvesMode.Luminosity, Diagonal, Diagonal, Diagonal, Diagonal);

    /// <summary>Encoded as: mode, then for each curve its point count followed by x, y pairs.</summary>
    public IReadOnlyList<double> ToValues()
    {
        var values = new List<double> { (int)Mode };
        foreach (var curve in new[] { Luminosity, Red, Green, Blue })
        {
            values.Add(curve.Count);
            foreach (var p in curve)
                values.AddRange([p.X, p.Y]);
        }
        return values;
    }

    public static CurvesSettings FromValues(IReadOnlyList<double> values)
    {
        var index = 1;
        IReadOnlyList<PointI> Next()
        {
            var count = (int)values[index++];
            var points = new List<PointI>(count);
            for (var i = 0; i < count; i++, index += 2)
                points.Add(new PointI((int)values[index], (int)values[index + 1]));
            return points;
        }
        var mode = (CurvesMode)(int)values[0];
        return new CurvesSettings(mode, Next(), Next(), Next(), Next());
    }
}

/// <summary>
/// Paint.NET's Curves: smooth curves through control points remap intensity (luminosity mode) or each RGB channel.
/// </summary>
public sealed class Curves : ColorAdjustment
{
    public override string Name => "Curves";

    public override bool HasCustomDialog => true;

    public override IReadOnlyList<double> Defaults => CurvesSettings.Identity.ToValues();

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels)
    {
        var settings = CurvesSettings.FromValues(values);
        if (settings.Mode == CurvesMode.Rgb)
            return FromLookup(Spline(settings.Blue), Spline(settings.Green), Spline(settings.Red));

        // Paint.NET's luminosity curve: shift the three channels by the change of intensity.
        var lut = Spline(settings.Luminosity);
        return pixel =>
        {
            var intensity = Intensity(pixel);
            var delta = lut[intensity] - intensity;
            pixel[0] = (byte)Math.Clamp(pixel[0] + delta, 0, 255);
            pixel[1] = (byte)Math.Clamp(pixel[1] + delta, 0, 255);
            pixel[2] = (byte)Math.Clamp(pixel[2] + delta, 0, 255);
        };
    }

    /// <summary>
    /// 256-entry lookup through the points with a monotone cubic spline (Fritsch–Carlson: no overshoot between
    /// points); flat before the first point and after the last. Points with the same x keep the last one.
    /// </summary>
    public static byte[] Spline(IReadOnlyList<PointI> points)
    {
        var sorted = points.GroupBy(p => Math.Clamp(p.X, 0, 255)).Select(g => new PointI(g.Key, Math.Clamp(g.Last().Y, 0, 255)))
            .OrderBy(p => p.X).ToArray();
        if (sorted.Length == 0)
            return Lookup(i => i);
        if (sorted.Length == 1)
            return Lookup(_ => sorted[0].Y);

        var n = sorted.Length;
        var slopes = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
            slopes[i] = (double)(sorted[i + 1].Y - sorted[i].Y) / (sorted[i + 1].X - sorted[i].X);
        var tangents = new double[n];
        tangents[0] = slopes[0];
        tangents[n - 1] = slopes[n - 2];
        for (var i = 1; i < n - 1; i++)
            tangents[i] = slopes[i - 1] * slopes[i] <= 0 ? 0 : (slopes[i - 1] + slopes[i]) / 2;
        for (var i = 0; i < n - 1; i++)
        {
            if (slopes[i] == 0)
            {
                tangents[i] = tangents[i + 1] = 0;
                continue;
            }
            var a = tangents[i] / slopes[i];
            var b = tangents[i + 1] / slopes[i];
            var h = a * a + b * b;
            if (h > 9)
            {
                var t = 3 / Math.Sqrt(h);
                tangents[i] = t * a * slopes[i];
                tangents[i + 1] = t * b * slopes[i];
            }
        }

        return Lookup(x =>
        {
            if (x <= sorted[0].X)
                return sorted[0].Y;
            if (x >= sorted[^1].X)
                return sorted[^1].Y;
            var k = 0;
            while (sorted[k + 1].X < x)
                k++;
            double x0 = sorted[k].X, x1 = sorted[k + 1].X, y0 = sorted[k].Y, y1 = sorted[k + 1].Y;
            var dx = x1 - x0;
            var t = (x - x0) / dx;
            var t2 = t * t;
            var t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * y0 + (t3 - 2 * t2 + t) * dx * tangents[k]
                   + (-2 * t3 + 3 * t2) * y1 + (t3 - t2) * dx * tangents[k + 1];
        });
    }
}

/// <summary>Histograms of the red, green, blue and intensity values of the non-transparent pixels.</summary>
public sealed record Histogram(long[] Red, long[] Green, long[] Blue, long[] Luminosity)
{
    /// <summary>Counts pixels of straight-alpha BGRA data, only inside the selection when there is one.</summary>
    public static Histogram Compute(ReadOnlySpan<byte> bgra, SelectionMask? selection = null)
    {
        var (r, g, b, l) = (new long[256], new long[256], new long[256], new long[256]);
        for (var i = 0; i < bgra.Length / 4; i++)
        {
            if (bgra[i * 4 + 3] == 0 || (selection is not null && selection.Data[i] == 0))
                continue;
            var px = bgra.Slice(i * 4, 4);
            b[px[0]]++;
            g[px[1]]++;
            r[px[2]]++;
            l[(7471 * px[0] + 38470 * px[1] + 19595 * px[2]) >> 16]++;
        }
        return new Histogram(r, g, b, l);
    }

    /// <summary>The histogram after mapping each channel through lookup tables (the Levels output histogram).</summary>
    public Histogram Map(byte[] red, byte[] green, byte[] blue)
    {
        static long[] MapOne(long[] h, byte[] lut)
        {
            var result = new long[256];
            for (var i = 0; i < 256; i++)
                result[lut[i]] += h[i];
            return result;
        }
        return this with { Red = MapOne(Red, red), Green = MapOne(Green, green), Blue = MapOne(Blue, blue) };
    }
}

/// <summary>Stretches the intensity range so the darkest 0.5 % become black and the brightest 0.5 % white.</summary>
public sealed class AutoLevel : ColorAdjustment
{
    public override string Name => "Auto-Level";

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels)
    {
        var histogram = new long[256];
        long count = 0;
        for (var i = 0; i < layerPixels.Length; i += 4)
        {
            if (layerPixels[i + 3] == 0)
                continue;
            histogram[Intensity(layerPixels.Slice(i, 4))]++;
            count++;
        }
        if (count == 0)
            return _ => { };

        var clip = count * 0.005;
        int low = 0, high = 255;
        for (long seen = 0; low < 255 && (seen += histogram[low]) <= clip; low++) { }
        for (long seen = 0; high > 0 && (seen += histogram[high]) <= clip; high--) { }
        if (high <= low)
            return _ => { };
        return FromLookup(Levels.Curve(low, high, 1, 0, 255));
    }
}

/// <summary>Hue rotation (degrees), saturation (%, 100 = unchanged) and lightness (-100..100), in HSL.</summary>
public sealed class HueSaturation : ColorAdjustment
{
    public override string Name => "Hue / Saturation";

    public override IReadOnlyList<EffectParameter> Parameters =>
    [
        new("Hue", -180, 180, 0),
        new("Saturation", 0, 200, 100),
        new("Lightness", -100, 100, 0),
    ];

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels)
    {
        var hueShift = values[0];
        var saturation = values[1] / 100;
        var lightness = values[2] / 100;
        return pixel =>
        {
            var (h, s, l) = ToHsl(pixel[2], pixel[1], pixel[0]);
            h = (h + hueShift + 360) % 360;
            s = Math.Clamp(s * saturation, 0, 1);
            l = lightness >= 0 ? l + (1 - l) * lightness : l * (1 + lightness);
            var (r, g, b) = FromHsl(h, s, l);
            (pixel[0], pixel[1], pixel[2]) = (b, g, r);
        };
    }

    private static (double H, double S, double L) ToHsl(byte r8, byte g8, byte b8)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
            return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        if (s == 0)
        {
            var v = ToByte(l);
            return (v, v, v);
        }
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        var hk = h / 360;
        return (ToByte(Channel(p, q, hk + 1.0 / 3)), ToByte(Channel(p, q, hk)), ToByte(Channel(p, q, hk - 1.0 / 3)));
    }

    private static double Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}
