namespace CinnabarSharp.Core.Models;

/// <summary>
/// Layer compositing on straight-alpha BGRA byte buffers, with Paint.NET's blend functions.
/// </summary>
public static class BlendOps
{
    /// <summary>Blend function on one color channel: bottom <paramref name="b"/>, top <paramref name="t"/>.</summary>
    public static byte Blend(BlendMode mode, byte b, byte t) => mode switch
    {
        BlendMode.Normal => t,
        BlendMode.Multiply => Div255(b * t),
        BlendMode.Additive => (byte)Math.Min(255, b + t),
        BlendMode.ColorBurn => t == 0 ? (byte)0 : (byte)Math.Max(0, 255 - (255 - b) * 255 / t),
        BlendMode.ColorDodge => t == 255 ? (byte)255 : (byte)Math.Min(255, b * 255 / (255 - t)),
        BlendMode.Reflect => t == 255 ? (byte)255 : (byte)Math.Min(255, b * b / (255 - t)),
        BlendMode.Glow => b == 255 ? (byte)255 : (byte)Math.Min(255, t * t / (255 - b)),
        BlendMode.Overlay => b < 128 ? Div255(2 * b * t) : (byte)(255 - Div255(2 * (255 - b) * (255 - t))),
        BlendMode.Difference => (byte)Math.Abs(b - t),
        BlendMode.Negation => (byte)(255 - Math.Abs(255 - b - t)),
        BlendMode.Lighten => Math.Max(b, t),
        BlendMode.Darken => Math.Min(b, t),
        BlendMode.Screen => (byte)(b + t - Div255(b * t)),
        BlendMode.Xor => (byte)(b ^ t),
        BlendMode.HardLight => t < 128 ? Div255(2 * b * t) : (byte)(255 - Div255(2 * (255 - b) * (255 - t))),
        BlendMode.SoftLight => SoftLight(b, t),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>
    /// Composites <paramref name="top"/> onto <paramref name="bottom"/> in place (both BGRA, same size).
    /// Where both pixels are opaque the result is the blend function; elsewhere it falls back to
    /// "source over", as in Paint.NET and the W3C compositing model.
    /// </summary>
    public static void Composite(Span<byte> bottom, ReadOnlySpan<byte> top, BlendMode mode, double opacity)
    {
        if (bottom.Length != top.Length || bottom.Length % 4 != 0)
            throw new ArgumentException("Buffers must be BGRA and the same size.");

        var op = (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        if (op == 0)
            return;

        for (var i = 0; i < bottom.Length; i += 4)
        {
            int aT = (top[i + 3] * op + 127) / 255;
            if (aT == 0)
                continue;

            int aB = bottom[i + 3];
            if (mode == BlendMode.Normal && aT == 255)
            {
                top.Slice(i, 4).CopyTo(bottom.Slice(i, 4));
                continue;
            }

            int both = aB * aT;
            int onlyBottom = aB * (255 - aT);
            int onlyTop = aT * (255 - aB);
            int total = both + onlyBottom + onlyTop;

            for (var c = 0; c < 3; c++)
            {
                int cb = bottom[i + c];
                int ct = top[i + c];
                int f = Blend(mode, (byte)cb, (byte)ct);
                bottom[i + c] = (byte)((onlyBottom * cb + onlyTop * ct + both * f + total / 2) / total);
            }
            bottom[i + 3] = (byte)((total + 127) / 255);
        }
    }

    private static byte Div255(int x) => (byte)((x + 127) / 255);

    // Pegtop soft light: smooth, no discontinuity at mid-grey.
    private static byte SoftLight(byte b, byte t)
    {
        var multiply = b * t / 255.0;
        var screen = 255 - (255 - b) * (255 - t) / 255.0;
        return (byte)Math.Round(((255 - b) * multiply + b * screen) / 255.0);
    }
}
