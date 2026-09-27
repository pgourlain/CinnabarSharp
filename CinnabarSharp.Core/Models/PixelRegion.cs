namespace CinnabarSharp.Core.Models;

/// <summary>Rectangle copies between straight-alpha BGRA buffers.</summary>
public static class PixelRegion
{
    public static byte[] Extract(ReadOnlySpan<byte> source, int width, RectangleI rect)
    {
        var result = new byte[rect.Width * rect.Height * 4];
        for (var y = 0; y < rect.Height; y++)
            source.Slice(((rect.Y + y) * width + rect.X) * 4, rect.Width * 4)
                .CopyTo(result.AsSpan(y * rect.Width * 4, rect.Width * 4));
        return result;
    }

    /// <summary>
    /// Draws <paramref name="source"/> onto <paramref name="target"/> with its top-left at (x, y), clipped to the target.
    /// Returns the covered rectangle in target coordinates (empty if nothing overlaps).
    /// </summary>
    public static RectangleI Place(Span<byte> target, int width, int height,
        ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, int x, int y, bool composite)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(width, x + sourceWidth);
        var y1 = Math.Min(height, y + sourceHeight);
        if (x1 <= x0 || y1 <= y0)
            return RectangleI.Zero;

        var rowBytes = (x1 - x0) * 4;
        for (var ty = y0; ty < y1; ty++)
        {
            var src = source.Slice(((ty - y) * sourceWidth + (x0 - x)) * 4, rowBytes);
            var dst = target.Slice((ty * width + x0) * 4, rowBytes);
            if (composite)
                BlendOps.Composite(dst, src, BlendMode.Normal, 1);
            else
                src.CopyTo(dst);
        }
        return new RectangleI(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Makes pixels outside the mask fully transparent (mask and buffer have the same size).</summary>
    public static void ClearOutside(Span<byte> bgra, SelectionMask mask)
    {
        var data = mask.Data;
        for (var i = 0; i < data.Length; i++)
            if (data[i] == 0)
                bgra.Slice(i * 4, 4).Clear();
    }
}
