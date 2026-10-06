namespace CinnabarSharp.Vector;

/// <summary>A straight-alpha BGRA picture being drawn into.</summary>
public sealed class RenderTarget(int width, int height, byte[]? pixels = null)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>Straight-alpha BGRA, row-major, <c>Width * Height * 4</c> bytes.</summary>
    public byte[] Pixels { get; } = pixels ?? new byte[width * height * 4];

    public void Clear() => Array.Clear(Pixels);
}

/// <summary>Where the colors of a fill come from (a flat color or a gradient), asked per device pixel.</summary>
internal abstract class PaintSource
{
    /// <summary>The color at the center of pixel (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public abstract VColor ColorAt(int x, int y);
}

internal sealed class SolidPaint(VColor color) : PaintSource
{
    public VColor Color { get; } = color;

    public override VColor ColorAt(int x, int y) => Color;
}

/// <summary>Integer source-over compositing in straight alpha: the same result everywhere.</summary>
internal static class Compositor
{
    /// <summary>(a × b) / 255 rounded, for a and b in 0..255.</summary>
    public static int Mul255(int a, int b)
    {
        var t = a * b + 128;
        return (t + (t >> 8)) >> 8;
    }

    /// <summary>Draws the paint through the coverage (and an overall opacity of 0 to 255) onto the target.</summary>
    public static void Fill(RenderTarget target, CoverageMask mask, PaintSource paint, int opacity = 255)
    {
        if (mask.IsEmpty || opacity <= 0)
            return;
        var area = mask.Area.Intersect(new VRectI(0, 0, target.Width, target.Height));
        if (area.IsEmpty)
            return;
        var solid = paint as SolidPaint;
        for (var y = area.Y; y < area.Bottom; y++)
        {
            var maskRow = (y - mask.Area.Y) * mask.Area.Width - mask.Area.X;
            var targetRow = y * target.Width;
            for (var x = area.X; x < area.Right; x++)
            {
                var coverage = mask.Data[maskRow + x];
                if (coverage == 0)
                    continue;
                var color = solid is not null ? solid.Color : paint.ColorAt(x, y);
                var alpha = Mul255(Mul255(coverage, color.A), opacity);
                Blend(target.Pixels, (targetRow + x) * 4, color.B, color.G, color.R, alpha);
            }
        }
    }

    /// <summary>One pixel, source over destination, both straight alpha.</summary>
    public static void Blend(byte[] pixels, int index, int b, int g, int r, int sourceAlpha)
    {
        if (sourceAlpha <= 0)
            return;
        var destinationAlpha = pixels[index + 3];
        if (sourceAlpha >= 255 || destinationAlpha == 0)
        {
            pixels[index] = (byte)b;
            pixels[index + 1] = (byte)g;
            pixels[index + 2] = (byte)r;
            pixels[index + 3] = (byte)sourceAlpha;
            return;
        }
        // Alphas scaled by 255 to stay in integers.
        var outAlpha = sourceAlpha * 255 + destinationAlpha * (255 - sourceAlpha);
        var sourceWeight = sourceAlpha * 255;
        var destinationWeight = destinationAlpha * (255 - sourceAlpha);
        var half = outAlpha / 2;
        pixels[index] = (byte)((b * sourceWeight + pixels[index] * destinationWeight + half) / outAlpha);
        pixels[index + 1] = (byte)((g * sourceWeight + pixels[index + 1] * destinationWeight + half) / outAlpha);
        pixels[index + 2] = (byte)((r * sourceWeight + pixels[index + 2] * destinationWeight + half) / outAlpha);
        pixels[index + 3] = (byte)((outAlpha + 127) / 255);
    }

    /// <summary>Draws <paramref name="layer"/> onto <paramref name="target"/>, scaled by opacity (0 to 255) and, when given, a per-pixel alpha mask.</summary>
    public static void DrawLayer(RenderTarget target, RenderTarget layer, int opacity, byte[]? mask)
    {
        var count = target.Width * target.Height;
        var src = layer.Pixels;
        for (var p = 0; p < count; p++)
        {
            var i = p * 4;
            var alpha = src[i + 3];
            if (alpha == 0)
                continue;
            var a = alpha;
            if (mask is not null)
                a = (byte)Mul255(a, mask[p]);
            if (opacity < 255)
                a = (byte)Mul255(a, opacity);
            Blend(target.Pixels, i, src[i], src[i + 1], src[i + 2], a);
        }
    }
}
