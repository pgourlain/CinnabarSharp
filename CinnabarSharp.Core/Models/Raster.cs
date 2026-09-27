namespace CinnabarSharp.Core.Models;

/// <summary>
/// Coverage masks for painting: image-sized, one byte per pixel (0 = untouched, 255 = fully covered).
/// Shapes are merged with "max" so overlapping dabs of one stroke don't build up.
/// Pure C# so strokes are identical on every OS. Every method returns the rectangle it touched.
/// </summary>
public sealed class CoverageMask(int width, int height)
{
    private readonly byte[] _data = new byte[width * height];

    public int Width { get; } = width;
    public int Height { get; } = height;
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>Union of every area touched so far.</summary>
    public RectangleI Bounds { get; private set; } = RectangleI.Zero;

    public byte this[int x, int y] => _data[y * Width + x];

    /// <summary>A round dab (antialiased: soft 1-pixel edge; aliased: pixel centers inside the circle).</summary>
    public RectangleI Disc(double cx, double cy, double radius, bool antialias) =>
        Paint(cx - radius - 1, cy - radius - 1, cx + radius + 1, cy + radius + 1, (x, y) =>
        {
            var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return Edge(radius - d, antialias);
        });

    /// <summary>A thick line with round caps.</summary>
    public RectangleI Segment(PointD a, PointD b, double radius, bool antialias)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        return Paint(Math.Min(a.X, b.X) - radius - 1, Math.Min(a.Y, b.Y) - radius - 1,
            Math.Max(a.X, b.X) + radius + 1, Math.Max(a.Y, b.Y) + radius + 1, (x, y) =>
            {
                var t = len2 == 0 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / len2, 0, 1);
                var px = a.X + t * dx - x;
                var py = a.Y + t * dy - y;
                return Edge(radius - Math.Sqrt(px * px + py * py), antialias);
            });
    }

    /// <summary>Filled axis-aligned rectangle between two corners.</summary>
    public RectangleI FillRectangle(PointD a, PointD b, bool antialias)
    {
        var (l, t, r, btm) = (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        return Paint(l - 1, t - 1, r + 1, btm + 1, (x, y) =>
            Edge(Math.Min(Math.Min(x - l, r - x), Math.Min(y - t, btm - y)), antialias));
    }

    /// <summary>
    /// Rectangle outline of the given width. The path runs through the centers of the outermost pixels of the
    /// rectangle (so a 1-pixel outline is exactly 1 pixel wide and matches <see cref="FillRectangle"/>'s area).
    /// </summary>
    public RectangleI StrokeRectangle(PointD a, PointD b, double width, bool antialias)
    {
        var l = Math.Round(Math.Min(a.X, b.X)) + 0.5;
        var t = Math.Round(Math.Min(a.Y, b.Y)) + 0.5;
        var r = Math.Max(l, Math.Round(Math.Max(a.X, b.X)) - 0.5);
        var btm = Math.Max(t, Math.Round(Math.Max(a.Y, b.Y)) - 0.5);
        var half = width / 2;
        return Paint(l - half - 1, t - half - 1, r + half + 1, btm + half + 1, (x, y) =>
        {
            // Signed distance to the rectangle border (negative inside).
            var inside = Math.Min(Math.Min(x - l, r - x), Math.Min(y - t, btm - y));
            var ox = Math.Max(Math.Max(l - x, x - r), 0);
            var oy = Math.Max(Math.Max(t - y, y - btm), 0);
            var distance = inside > 0 ? inside : Math.Sqrt(ox * ox + oy * oy);
            return Edge(half - distance, antialias);
        });
    }

    public RectangleI FillEllipse(PointD a, PointD b, bool antialias)
    {
        var (cx, cy, rx, ry) = EllipseOf(a, b);
        if (rx <= 0 || ry <= 0)
            return RectangleI.Zero;
        return Paint(cx - rx - 1, cy - ry - 1, cx + rx + 1, cy + ry + 1, (x, y) =>
            Edge(-EllipseDistance(x - cx, y - cy, rx, ry), antialias));
    }

    public RectangleI StrokeEllipse(PointD a, PointD b, double width, bool antialias)
    {
        var (cx, cy, rx, ry) = EllipseOf(a, b);
        if (rx <= 0 || ry <= 0)
            return RectangleI.Zero;
        var half = width / 2;
        return Paint(cx - rx - half - 1, cy - ry - half - 1, cx + rx + half + 1, cy + ry + half + 1, (x, y) =>
            Edge(half - Math.Abs(EllipseDistance(x - cx, y - cy, rx, ry)), antialias));
    }

    /// <summary>Marks every pixel of <paramref name="mask"/> (e.g. a flood fill) as fully covered.</summary>
    public RectangleI Fill(SelectionMask mask)
    {
        var data = mask.Data;
        for (var i = 0; i < data.Length; i++)
            if (data[i] != 0)
                _data[i] = 255;
        Bounds = Union(Bounds, mask.Bounds);
        return mask.Bounds;
    }

    /// <summary>A 1-pixel aliased line (Bresenham) through pixel centers, as the Pencil draws.</summary>
    public RectangleI PixelLine(PointI a, PointI b)
    {
        int x = a.X, y = a.Y, dx = Math.Abs(b.X - a.X), dy = -Math.Abs(b.Y - a.Y);
        int sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1, err = dx + dy;
        while (true)
        {
            if (x >= 0 && y >= 0 && x < Width && y < Height)
                _data[y * Width + x] = 255;
            if (x == b.X && y == b.Y)
                break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
        var touched = Clip(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
        Bounds = Union(Bounds, touched);
        return touched;
    }

    // Pixel (x, y) is sampled at its center (x + 0.5, y + 0.5).
    private RectangleI Paint(double left, double top, double right, double bottom, Func<double, double, double> coverage)
    {
        var rect = Clip((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(right) + 1, (int)Math.Ceiling(bottom) + 1);
        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                var c = coverage(x + 0.5, y + 0.5);
                if (c <= 0)
                    continue;
                var value = (byte)Math.Round(Math.Min(1, c) * 255);
                ref var cell = ref _data[y * Width + x];
                if (value > cell)
                    cell = value;
            }
        }
        Bounds = Union(Bounds, rect);
        return rect;
    }

    private static double Edge(double signedDistance, bool antialias) =>
        antialias ? Math.Clamp(signedDistance + 0.5, 0, 1) : signedDistance >= 0 ? 1 : 0;

    private static (double Cx, double Cy, double Rx, double Ry) EllipseOf(PointD a, PointD b) =>
        ((a.X + b.X) / 2, (a.Y + b.Y) / 2, Math.Abs(b.X - a.X) / 2, Math.Abs(b.Y - a.Y) / 2);

    // First-order signed distance to an ellipse's outline (negative inside).
    private static double EllipseDistance(double x, double y, double rx, double ry)
    {
        var f = x * x / (rx * rx) + y * y / (ry * ry) - 1;
        var gx = 2 * x / (rx * rx);
        var gy = 2 * y / (ry * ry);
        var g = Math.Sqrt(gx * gx + gy * gy);
        return g == 0 ? -Math.Min(rx, ry) : f / g;
    }

    private RectangleI Clip(int x0, int y0, int x1, int y1)
    {
        x0 = Math.Clamp(x0, 0, Width);
        y0 = Math.Clamp(y0, 0, Height);
        x1 = Math.Clamp(x1, 0, Width);
        y1 = Math.Clamp(y1, 0, Height);
        return x1 > x0 && y1 > y0 ? new RectangleI(x0, y0, x1 - x0, y1 - y0) : RectangleI.Zero;
    }

    public static RectangleI Union(RectangleI a, RectangleI b)
    {
        if (a.IsEmpty) return b;
        if (b.IsEmpty) return a;
        var x0 = Math.Min(a.X, b.X);
        var y0 = Math.Min(a.Y, b.Y);
        var x1 = Math.Max(a.X + a.Width, b.X + b.Width);
        var y1 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new RectangleI(x0, y0, x1 - x0, y1 - y0);
    }
}
