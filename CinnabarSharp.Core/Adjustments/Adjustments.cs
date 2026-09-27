using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Adjustments;

/// <summary>A numeric parameter shown as a slider in the adjustment dialog.</summary>
public sealed record AdjustmentParameter(string Name, double Minimum, double Maximum, double Default, double Step = 1);

/// <summary>
/// A per-pixel color adjustment (Paint.NET's Adjustments menu). <see cref="Create"/> receives the parameter values and
/// the layer's original pixels (for adjustments that need statistics, like Auto-Level) and returns the pixel function.
/// Pure C#, so results are identical on every OS.
/// </summary>
public abstract class ColorAdjustment
{
    public abstract string Name { get; }

    /// <summary>Empty for adjustments applied immediately, without a dialog.</summary>
    public virtual IReadOnlyList<AdjustmentParameter> Parameters => [];

    public abstract PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels);

    public IReadOnlyList<double> Defaults => Parameters.Select(p => p.Default).ToList();

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

    public override IReadOnlyList<AdjustmentParameter> Parameters =>
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

    public override IReadOnlyList<AdjustmentParameter> Parameters => [new("Levels", 2, 64, 16)];

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels)
    {
        var steps = (int)values[0] - 1;
        return FromLookup(Lookup(i => Math.Round(Math.Round(i * steps / 255.0) * 255.0 / steps)));
    }
}

/// <summary>Maps [input black, input white] to [output black, output white] with a gamma curve.</summary>
public sealed class Levels : ColorAdjustment
{
    public override string Name => "Levels";

    public override IReadOnlyList<AdjustmentParameter> Parameters =>
    [
        new("Input black", 0, 254, 0),
        new("Input white", 1, 255, 255),
        new("Gamma", 0.1, 10, 1, 0.01),
        new("Output black", 0, 255, 0),
        new("Output white", 0, 255, 255),
    ];

    public override PixelFunction Create(IReadOnlyList<double> values, ReadOnlySpan<byte> layerPixels) =>
        FromLookup(Curve(values[0], values[1], values[2], values[3], values[4]));

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

    public override IReadOnlyList<AdjustmentParameter> Parameters =>
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
