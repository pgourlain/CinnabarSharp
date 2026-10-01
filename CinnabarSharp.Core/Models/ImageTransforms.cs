using ImageMagick;
using CinnabarSharp.Core.Extensions;

namespace CinnabarSharp.Core.Models;

public enum ResamplingMode
{
    /// <summary>Best quality for photos (Lanczos).</summary>
    BestQuality,
    Bicubic,
    Bilinear,
    NearestNeighbor,
}

/// <summary>Whole-image pixel transforms on straight-alpha BGRA buffers (pure C# except resampling).</summary>
public static class ImageTransforms
{
    public static byte[] FlipHorizontal(ReadOnlySpan<byte> src, int w, int h)
    {
        var dst = new byte[src.Length];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                src.Slice((y * w + x) * 4, 4).CopyTo(dst.AsSpan((y * w + (w - 1 - x)) * 4, 4));
        return dst;
    }

    public static byte[] FlipVertical(ReadOnlySpan<byte> src, int w, int h)
    {
        var dst = new byte[src.Length];
        for (var y = 0; y < h; y++)
            src.Slice(y * w * 4, w * 4).CopyTo(dst.AsSpan((h - 1 - y) * w * 4, w * 4));
        return dst;
    }

    /// <summary>Rotates by 90° clockwise (<paramref name="clockwise"/>) or counter-clockwise; result is h × w.</summary>
    public static byte[] Rotate90(ReadOnlySpan<byte> src, int w, int h, bool clockwise)
    {
        var dst = new byte[src.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (nx, ny) = clockwise ? (h - 1 - y, x) : (y, w - 1 - x);
                src.Slice((y * w + x) * 4, 4).CopyTo(dst.AsSpan((ny * h + nx) * 4, 4));
            }
        }
        return dst;
    }

    public static byte[] Rotate180(ReadOnlySpan<byte> src, int w, int h) =>
        FlipVertical(FlipHorizontal(src, w, h), w, h);

    /// <summary>Top-left of the old image inside the new canvas for an anchor (negative when shrinking).</summary>
    public static PointI AnchorOffset(ImageSize from, ImageSize to, Anchor anchor)
    {
        var dx = to.Width - from.Width;
        var dy = to.Height - from.Height;
        return anchor switch
        {
            Anchor.NW => new PointI(0, 0),
            Anchor.N => new PointI(dx / 2, 0),
            Anchor.NE => new PointI(dx, 0),
            Anchor.W => new PointI(0, dy / 2),
            Anchor.Center => new PointI(dx / 2, dy / 2),
            Anchor.E => new PointI(dx, dy / 2),
            Anchor.SW => new PointI(0, dy),
            Anchor.S => new PointI(dx / 2, dy),
            Anchor.SE => new PointI(dx, dy),
            _ => new PointI(0, 0),
        };
    }

    /// <summary>New canvas of <paramref name="to"/> filled with <paramref name="background"/>, old pixels placed at the anchor.</summary>
    public static byte[] ResizeCanvas(ReadOnlySpan<byte> src, ImageSize from, ImageSize to, Anchor anchor, ColorBgra background) =>
        ResizeCanvas(src, from, to, AnchorOffset(from, to, anchor), background);

    /// <summary>New canvas of <paramref name="to"/> filled with <paramref name="background"/>, old pixels placed at <paramref name="at"/>.</summary>
    public static byte[] ResizeCanvas(ReadOnlySpan<byte> src, ImageSize from, ImageSize to, PointI at, ColorBgra background)
    {
        var dst = new byte[to.Width * to.Height * 4];
        if (background.A != 0)
            for (var i = 0; i < dst.Length; i += 4)
                (dst[i], dst[i + 1], dst[i + 2], dst[i + 3]) = (background.B, background.G, background.R, background.A);
        PixelRegion.Place(dst, to.Width, to.Height, src, from.Width, from.Height, at.X, at.Y, composite: false);
        return dst;
    }

    public static IImageBuf Resample(IImageBuf source, ImageSize size, ResamplingMode mode)
    {
        var result = source.Clone();
        result.FilterType = mode switch
        {
            ResamplingMode.Bicubic => FilterType.Catrom,
            ResamplingMode.Bilinear => FilterType.Triangle,
            ResamplingMode.NearestNeighbor => FilterType.Point,
            _ => FilterType.Lanczos,
        };
        result.Resize(new MagickGeometry((uint)size.Width, (uint)size.Height) { IgnoreAspectRatio = true });
        return result;
    }
}
