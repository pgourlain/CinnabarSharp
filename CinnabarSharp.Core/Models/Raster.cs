namespace CinnabarSharp.Core.Models;

/// <summary>
/// Coverage mask for painting: one byte per pixel (0 = untouched, 255 = fully covered), addressed in image
/// coordinates like a full-image mask would be, but the backing store only ever covers the smallest rectangle
/// touched so far (performance-tasks.md P3) — nothing is allocated until the first shape is drawn, and it grows
/// (reallocating and copying, like a growable list) only as far as the strokes on it actually reach, instead of
/// always being <see cref="Width"/> × <see cref="Height"/>. Shapes are merged with "max" so overlapping dabs of
/// one stroke don't build up. Pure C# so strokes are identical on every OS. Every method returns the rectangle
/// it touched.
/// </summary>
public sealed class CoverageMask(int width, int height)
{
    // The covered sub-rectangle's own pixels, row-major; (_originX, _originY) is its top-left in image
    // coordinates. Empty/zero-sized until the first write.
    private byte[] _data = [];
    private int _dataWidth, _dataHeight, _originX, _originY;

    /// <summary>Extra margin added, on top of what's strictly needed, to whichever side(s) of the backing
    /// store actually grow (see <see cref="EnsureCovers"/>).</summary>
    private const int GrowthPadding = 64;

    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>Raw bytes of the covered sub-rectangle (see <see cref="Bounds"/>) — not the whole image, and
    /// empty until the first shape is drawn.</summary>
    public ReadOnlySpan<byte> Data => _data;

    /// <summary>Union of every area touched so far.</summary>
    public RectangleI Bounds { get; private set; } = RectangleI.Zero;

    public byte this[int x, int y]
    {
        get
        {
            var lx = x - _originX;
            var ly = y - _originY;
            if ((uint)lx >= (uint)_dataWidth || (uint)ly >= (uint)_dataHeight)
                return 0;
            return _data[ly * _dataWidth + lx];
        }
    }

    /// <summary>
    /// A round dab (antialiased: soft 1-pixel edge; aliased: pixel centers inside the circle).
    /// <paramref name="hardness"/> below 1 fades the outer part of the radius out smoothly.
    /// </summary>
    public RectangleI Disc(double cx, double cy, double radius, bool antialias, double hardness = 1) =>
        Paint(cx - radius - 1, cy - radius - 1, cx + radius + 1, cy + radius + 1, (x, y) =>
        {
            var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            return Brush(radius, d, antialias, hardness);
        });

    /// <summary>A thick line with round caps.</summary>
    public RectangleI Segment(PointD a, PointD b, double radius, bool antialias, double hardness = 1)
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
                return Brush(radius, Math.Sqrt(px * px + py * py), antialias, hardness);
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

    /// <summary>Filled rectangle with rounded corners (radius clamped to half the shorter side).</summary>
    public RectangleI FillRoundedRectangle(PointD a, PointD b, double radius, bool antialias)
    {
        var (l, t, r, btm) = (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        return Paint(l - 1, t - 1, r + 1, btm + 1, (x, y) =>
            Edge(-RoundedBoxDistance(x, y, l, t, r, btm, radius), antialias));
    }

    /// <summary>Rounded rectangle outline; like <see cref="StrokeRectangle"/>, the path runs through pixel centers.</summary>
    public RectangleI StrokeRoundedRectangle(PointD a, PointD b, double radius, double width, bool antialias)
    {
        var l = Math.Round(Math.Min(a.X, b.X)) + 0.5;
        var t = Math.Round(Math.Min(a.Y, b.Y)) + 0.5;
        var r = Math.Max(l, Math.Round(Math.Max(a.X, b.X)) - 0.5);
        var btm = Math.Max(t, Math.Round(Math.Max(a.Y, b.Y)) - 0.5);
        var half = width / 2;
        return Paint(l - half - 1, t - half - 1, r + half + 1, btm + half + 1, (x, y) =>
            Edge(half - Math.Abs(RoundedBoxDistance(x, y, l, t, r, btm, radius)), antialias));
    }

    // Signed distance to a rounded box (negative inside).
    private static double RoundedBoxDistance(double x, double y, double l, double t, double r, double b, double radius)
    {
        var hx = (r - l) / 2;
        var hy = (b - t) / 2;
        radius = Math.Clamp(radius, 0, Math.Min(hx, hy));
        var qx = Math.Abs(x - (l + r) / 2) - (hx - radius);
        var qy = Math.Abs(y - (t + b) / 2) - (hy - radius);
        var ox = Math.Max(qx, 0);
        var oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - radius;
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
        var bounds = mask.Bounds;
        if (bounds.IsEmpty)
            return bounds;
        EnsureCovers(bounds);
        var data = mask.Data;
        for (var y = bounds.Y; y < bounds.Y + bounds.Height; y++)
        {
            for (var x = bounds.X; x < bounds.X + bounds.Width; x++)
            {
                if (data[y * Width + x] != 0)
                    _data[(y - _originY) * _dataWidth + (x - _originX)] = 255;
            }
        }
        Bounds = Union(Bounds, bounds);
        return bounds;
    }

    /// <summary>A 1-pixel aliased line (Bresenham) through pixel centers, as the Pencil draws.</summary>
    public RectangleI PixelLine(PointI a, PointI b)
    {
        var touched = Clip(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
        if (!touched.IsEmpty)
            EnsureCovers(touched);

        int x = a.X, y = a.Y, dx = Math.Abs(b.X - a.X), dy = -Math.Abs(b.Y - a.Y);
        int sx = a.X < b.X ? 1 : -1, sy = a.Y < b.Y ? 1 : -1, err = dx + dy;
        while (true)
        {
            if (x >= 0 && y >= 0 && x < Width && y < Height)
                _data[(y - _originY) * _dataWidth + (x - _originX)] = 255;
            if (x == b.X && y == b.Y)
                break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
        Bounds = Union(Bounds, touched);
        return touched;
    }

    // Pixel (x, y) is sampled at its center (x + 0.5, y + 0.5).
    private RectangleI Paint(double left, double top, double right, double bottom, Func<double, double, double> coverage)
    {
        var rect = Clip((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(right) + 1, (int)Math.Ceiling(bottom) + 1);
        if (rect.IsEmpty)
            return rect;
        EnsureCovers(rect);
        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                var c = coverage(x + 0.5, y + 0.5);
                if (c <= 0)
                    continue;
                var value = (byte)Math.Round(Math.Min(1, c) * 255);
                ref var cell = ref _data[(y - _originY) * _dataWidth + (x - _originX)];
                if (value > cell)
                    cell = value;
            }
        }
        Bounds = Union(Bounds, rect);
        return rect;
    }

    /// <summary>Grows the backing store, if needed, so it covers every pixel of <paramref name="rect"/> (which
    /// must already be clipped to the image), preserving whatever was already drawn.</summary>
    private void EnsureCovers(RectangleI rect)
    {
        if (_dataWidth == 0)
        {
            _originX = rect.X;
            _originY = rect.Y;
            _dataWidth = rect.Width;
            _dataHeight = rect.Height;
            _data = new byte[_dataWidth * _dataHeight];
            return;
        }
        if (rect.X >= _originX && rect.Y >= _originY &&
            rect.X + rect.Width <= _originX + _dataWidth && rect.Y + rect.Height <= _originY + _dataHeight)
            return; // already covered

        var x0 = Math.Min(_originX, rect.X);
        var y0 = Math.Min(_originY, rect.Y);
        var x1 = Math.Max(_originX + _dataWidth, rect.X + rect.Width);
        var y1 = Math.Max(_originY + _dataHeight, rect.Y + rect.Height);

        // Pad whichever side(s) actually grew, so the next several nearby dabs of the same stroke (a drag
        // rarely jumps far pixel to pixel) land inside the padded area instead of each triggering their own
        // reallocate-and-copy — without this, a long, gradually-wandering stroke regrows on almost every dab,
        // and the *cumulative* bytes copied across all those regrows can rival what a fixed full-image buffer
        // would have cost, even though the final buffer itself stays small.
        if (x0 < _originX) x0 = Math.Max(0, x0 - GrowthPadding);
        if (y0 < _originY) y0 = Math.Max(0, y0 - GrowthPadding);
        if (x1 > _originX + _dataWidth) x1 = Math.Min(Width, x1 + GrowthPadding);
        if (y1 > _originY + _dataHeight) y1 = Math.Min(Height, y1 + GrowthPadding);

        var newWidth = x1 - x0;
        var newHeight = y1 - y0;
        var newData = new byte[newWidth * newHeight];
        var dx = _originX - x0;
        var dy = _originY - y0;
        for (var y = 0; y < _dataHeight; y++)
            Array.Copy(_data, y * _dataWidth, newData, (y + dy) * newWidth + dx, _dataWidth);

        _data = newData;
        _originX = x0;
        _originY = y0;
        _dataWidth = newWidth;
        _dataHeight = newHeight;
    }

    // Coverage of a brush of the given radius at distance d from its center. Soft brushes fade from full coverage at
    // hardness × radius to zero at the radius (smoothstep).
    private static double Brush(double radius, double d, bool antialias, double hardness)
    {
        if (hardness >= 1)
            return Edge(radius - d, antialias);
        var fade = Math.Max(1, radius * (1 - Math.Max(0, hardness)));
        var c = Math.Clamp((radius - d) / fade, 0, 1);
        return c * c * (3 - 2 * c);
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
