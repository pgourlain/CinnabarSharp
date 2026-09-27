namespace CinnabarSharp.Core.Models;

/// <summary>How a new shape combines with the existing selection (Paint.NET's selection modes).</summary>
public enum SelectionMode
{
    Replace,
    Union,
    Exclude,
    Xor,
    Intersect,
}

/// <summary>
/// Image-sized selection: one byte per pixel, 255 = selected, 0 = not selected. Immutable; operations return new masks.
/// </summary>
public sealed class SelectionMask
{
    private readonly byte[] _data;

    private SelectionMask(int width, int height, byte[] data)
    {
        Width = width;
        Height = height;
        _data = data;
        Bounds = ComputeBounds();
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Smallest rectangle containing every selected pixel; empty when nothing is selected.</summary>
    public RectangleI Bounds { get; }

    public bool IsEmpty => Bounds.IsEmpty;

    public bool Contains(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height && _data[y * Width + x] != 0;

    public byte this[int x, int y] => _data[y * Width + x];

    public ReadOnlySpan<byte> Data => _data;

    public static SelectionMask Empty(int width, int height) => new(width, height, new byte[width * height]);

    public static SelectionMask All(int width, int height)
    {
        var data = new byte[width * height];
        Array.Fill(data, (byte)255);
        return new SelectionMask(width, height, data);
    }

    /// <summary>Pixels whose centers are inside the rectangle between two corners (any order).</summary>
    public static SelectionMask Rectangle(int width, int height, PointD a, PointD b)
    {
        var data = new byte[width * height];
        var x0 = Math.Clamp((int)Math.Round(Math.Min(a.X, b.X)), 0, width);
        var x1 = Math.Clamp((int)Math.Round(Math.Max(a.X, b.X)), 0, width);
        var y0 = Math.Clamp((int)Math.Round(Math.Min(a.Y, b.Y)), 0, height);
        var y1 = Math.Clamp((int)Math.Round(Math.Max(a.Y, b.Y)), 0, height);
        for (var y = y0; y < y1; y++)
            data.AsSpan(y * width + x0, x1 - x0).Fill(255);
        return new SelectionMask(width, height, data);
    }

    /// <summary>Pixels whose centers are inside the ellipse inscribed in the rectangle between two corners.</summary>
    public static SelectionMask Ellipse(int width, int height, PointD a, PointD b)
    {
        var data = new byte[width * height];
        var left = Math.Round(Math.Min(a.X, b.X));
        var right = Math.Round(Math.Max(a.X, b.X));
        var top = Math.Round(Math.Min(a.Y, b.Y));
        var bottom = Math.Round(Math.Max(a.Y, b.Y));
        var rx = (right - left) / 2;
        var ry = (bottom - top) / 2;
        if (rx <= 0 || ry <= 0)
            return new SelectionMask(width, height, data);
        var cx = left + rx;
        var cy = top + ry;

        for (var y = Math.Max(0, (int)top); y < Math.Min(height, (int)bottom); y++)
        {
            var dy = (y + 0.5 - cy) / ry;
            var half = 1 - dy * dy;
            if (half <= 0)
                continue;
            var dx = rx * Math.Sqrt(half);
            var x0 = Math.Clamp((int)Math.Ceiling(cx - dx - 0.5), 0, width);
            var x1 = Math.Clamp((int)Math.Floor(cx + dx - 0.5) + 1, 0, width);
            if (x1 > x0)
                data.AsSpan(y * width + x0, x1 - x0).Fill(255);
        }
        return new SelectionMask(width, height, data);
    }

    /// <summary>Pixels whose centers are inside the closed polygon (even-odd rule).</summary>
    public static SelectionMask Polygon(int width, int height, IReadOnlyList<PointD> points)
    {
        var data = new byte[width * height];
        if (points.Count < 3)
            return new SelectionMask(width, height, data);

        var crossings = new List<double>();
        for (var y = 0; y < height; y++)
        {
            var sy = y + 0.5;
            crossings.Clear();
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var q = points[(i + 1) % points.Count];
                if ((p.Y <= sy && q.Y > sy) || (q.Y <= sy && p.Y > sy))
                    crossings.Add(p.X + (sy - p.Y) / (q.Y - p.Y) * (q.X - p.X));
            }
            crossings.Sort();
            for (var i = 0; i + 1 < crossings.Count; i += 2)
            {
                var x0 = Math.Clamp((int)Math.Ceiling(crossings[i] - 0.5), 0, width);
                // Half-open [left, right): a pixel whose center is exactly on the right edge is outside.
                var x1 = Math.Clamp((int)Math.Ceiling(crossings[i + 1] - 0.5), 0, width);
                if (x1 > x0)
                    data.AsSpan(y * width + x0, x1 - x0).Fill(255);
            }
        }
        return new SelectionMask(width, height, data);
    }

    /// <summary>
    /// Magic wand on straight-alpha BGRA pixels: selects pixels whose color is within <paramref name="tolerance"/>
    /// (0–100 %) of the pixel at <paramref name="start"/>, either connected to it or anywhere in the image.
    /// </summary>
    public static SelectionMask MagicWand(ReadOnlySpan<byte> bgra, int width, int height, PointI start,
        int tolerance, bool global)
    {
        var data = new byte[width * height];
        if (start.X < 0 || start.Y < 0 || start.X >= width || start.Y >= height)
            return new SelectionMask(width, height, data);

        var s = (start.Y * width + start.X) * 4;
        var seed = (bgra[s], bgra[s + 1], bgra[s + 2], bgra[s + 3]);
        // Compare squared distance (sum over 4 channels) against the tolerance scaled to that range.
        var limit = (long)Math.Round(Math.Pow(Math.Clamp(tolerance, 0, 100) / 100.0 * 255, 2) * 4);

        bool Matches(ReadOnlySpan<byte> px, int index)
        {
            var i = index * 4;
            long db = px[i] - seed.Item1, dg = px[i + 1] - seed.Item2, dr = px[i + 2] - seed.Item3, da = px[i + 3] - seed.Item4;
            return db * db + dg * dg + dr * dr + da * da <= limit;
        }

        if (global)
        {
            for (var i = 0; i < width * height; i++)
                if (Matches(bgra, i))
                    data[i] = 255;
            return new SelectionMask(width, height, data);
        }

        var visited = new bool[width * height];
        var stack = new Stack<int>();
        stack.Push(start.Y * width + start.X);
        while (stack.Count > 0)
        {
            var index = stack.Pop();
            if (visited[index])
                continue;
            visited[index] = true;
            if (!Matches(bgra, index))
                continue;
            data[index] = 255;
            int x = index % width, y = index / width;
            if (x > 0) stack.Push(index - 1);
            if (x < width - 1) stack.Push(index + 1);
            if (y > 0) stack.Push(index - width);
            if (y < height - 1) stack.Push(index + width);
        }
        return new SelectionMask(width, height, data);
    }

    public SelectionMask Combine(SelectionMask shape, SelectionMode mode)
    {
        if (shape.Width != Width || shape.Height != Height)
            throw new ArgumentException("Selection sizes differ.", nameof(shape));
        if (mode == SelectionMode.Replace)
            return shape;

        var result = new byte[_data.Length];
        for (var i = 0; i < result.Length; i++)
        {
            bool a = _data[i] != 0, b = shape._data[i] != 0;
            var selected = mode switch
            {
                SelectionMode.Union => a || b,
                SelectionMode.Exclude => a && !b,
                SelectionMode.Xor => a ^ b,
                SelectionMode.Intersect => a && b,
                _ => b,
            };
            result[i] = selected ? (byte)255 : (byte)0;
        }
        return new SelectionMask(Width, Height, result);
    }

    public SelectionMask Invert()
    {
        var result = new byte[_data.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = (byte)(255 - _data[i]);
        return new SelectionMask(Width, Height, result);
    }

    /// <summary>Shifted by (dx, dy); pixels moved outside the image are dropped.</summary>
    public SelectionMask Offset(int dx, int dy)
    {
        var result = new byte[_data.Length];
        for (var y = 0; y < Height; y++)
        {
            var ty = y + dy;
            if (ty < 0 || ty >= Height)
                continue;
            for (var x = 0; x < Width; x++)
            {
                var tx = x + dx;
                if (tx >= 0 && tx < Width)
                    result[ty * Width + tx] = _data[y * Width + x];
            }
        }
        return new SelectionMask(Width, Height, result);
    }

    /// <summary>The part of the mask inside <paramref name="rect"/>, as a mask of that size.</summary>
    public SelectionMask Crop(RectangleI rect)
    {
        var result = new byte[rect.Width * rect.Height];
        for (var y = 0; y < rect.Height; y++)
            _data.AsSpan((rect.Y + y) * Width + rect.X, rect.Width).CopyTo(result.AsSpan(y * rect.Width, rect.Width));
        return new SelectionMask(rect.Width, rect.Height, result);
    }

    /// <summary>
    /// Outline as axis-aligned segments on pixel edges (x1, y1, x2, y2), merged into runs; used to draw marching ants.
    /// </summary>
    public List<(int X1, int Y1, int X2, int Y2)> GetOutline()
    {
        var segments = new List<(int, int, int, int)>();
        if (IsEmpty)
            return segments;

        // Horizontal edges: between row y-1 and row y.
        for (var y = Bounds.Top; y <= Bounds.Bottom + 1; y++)
        {
            var runStart = -1;
            for (var x = Bounds.Left; x <= Bounds.Right + 1; x++)
            {
                var edge = x <= Bounds.Right && Contains(x, y) != Contains(x, y - 1);
                if (edge && runStart < 0)
                    runStart = x;
                else if (!edge && runStart >= 0)
                {
                    segments.Add((runStart, y, x, y));
                    runStart = -1;
                }
            }
        }

        // Vertical edges: between column x-1 and column x.
        for (var x = Bounds.Left; x <= Bounds.Right + 1; x++)
        {
            var runStart = -1;
            for (var y = Bounds.Top; y <= Bounds.Bottom + 1; y++)
            {
                var edge = y <= Bounds.Bottom && Contains(x, y) != Contains(x - 1, y);
                if (edge && runStart < 0)
                    runStart = y;
                else if (!edge && runStart >= 0)
                {
                    segments.Add((x, runStart, x, y));
                    runStart = -1;
                }
            }
        }
        return segments;
    }

    private RectangleI ComputeBounds()
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (var y = 0; y < Height; y++)
        {
            var row = _data.AsSpan(y * Width, Width);
            var first = row.IndexOfAnyExcept((byte)0);
            if (first < 0)
                continue;
            var last = row.LastIndexOfAnyExcept((byte)0);
            minX = Math.Min(minX, first);
            maxX = Math.Max(maxX, last);
            minY = Math.Min(minY, y);
            maxY = y;
        }
        return maxX < 0 ? RectangleI.Zero : new RectangleI(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
