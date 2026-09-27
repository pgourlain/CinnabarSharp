namespace CinnabarSharp.Core.Effects;

/// <summary>
/// Pixel reading helpers. Averaging happens on premultiplied colors so transparent pixels don't darken edges.
/// </summary>
public static class Sampling
{
    /// <summary>Accumulates premultiplied samples and writes their straight-alpha average.</summary>
    public struct Average
    {
        private double _b, _g, _r, _a, _weight;

        public void Add(ReadOnlySpan<byte> px, double weight = 1)
        {
            var a = px[3] * weight;
            _b += px[0] * a;
            _g += px[1] * a;
            _r += px[2] * a;
            _a += a;
            _weight += weight;
        }

        public readonly void WriteTo(Span<byte> dst)
        {
            if (_weight <= 0 || _a <= 0)
            {
                dst.Clear();
                return;
            }
            dst[0] = ToByte(_b / _a);
            dst[1] = ToByte(_g / _a);
            dst[2] = ToByte(_r / _a);
            dst[3] = ToByte(_a / _weight);
        }
    }

    public static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>Bilinear sample at a fractional position (pixel centers at x + 0.5), clamped to the edges.</summary>
    public static void Bilinear(EffectContext ctx, double x, double y, Span<byte> dst)
    {
        x -= 0.5;
        y -= 0.5;
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var avg = new Average();
        var src = ctx.Source;
        avg.Add(src.AsSpan(ctx.Index(x0, y0), 4), (1 - fx) * (1 - fy));
        avg.Add(src.AsSpan(ctx.Index(x0 + 1, y0), 4), fx * (1 - fy));
        avg.Add(src.AsSpan(ctx.Index(x0, y0 + 1), 4), (1 - fx) * fy);
        avg.Add(src.AsSpan(ctx.Index(x0 + 1, y0 + 1), 4), fx * fy);
        avg.WriteTo(dst);
    }

    /// <summary>Deterministic pseudo-random value in [0, 1) for a pixel and seed (same on every OS).</summary>
    public static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>Gaussian-ish noise in about [-1, 1] (sum of two uniforms).</summary>
    public static double Noise(int x, int y, int seed) => Hash(x, y, seed) + Hash(x, y, seed + 7919) - 1;
}
