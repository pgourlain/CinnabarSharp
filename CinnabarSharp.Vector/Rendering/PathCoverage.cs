namespace CinnabarSharp.Vector;

/// <summary>Per-pixel coverage (0 to 255) of a rectangle of the device.</summary>
public sealed class CoverageMask(VRectI area, byte[] data)
{
    public VRectI Area { get; } = area;

    /// <summary>Row-major, <see cref="Area"/>.Width × Height bytes.</summary>
    public byte[] Data { get; } = data;

    public static CoverageMask Empty { get; } = new(VRectI.Empty, []);

    public bool IsEmpty => Area.IsEmpty;

    public byte At(int x, int y)
    {
        if (x < Area.X || y < Area.Y || x >= Area.Right || y >= Area.Bottom)
            return 0;
        return Data[(y - Area.Y) * Area.Width + (x - Area.X)];
    }
}

/// <summary>
/// Antialiased polygon filling. Method: 16 sub-scanlines per pixel row, and for each the exact horizontal extent of every
/// span (fractional coverage at the span ends), summed and rounded to 8 bits. So horizontal and vertical edges are exact to
/// 1/16 of a pixel vertically and exact horizontally. Nonzero and even-odd rules. Deterministic: plain double arithmetic.
/// </summary>
public static class PathCoverage
{
    public const int SubScanlines = 16;

    private struct Edge
    {
        public double X0, Y0, Y1, Slope;
        public int Direction;
    }

    /// <summary>Coverage of the polygons (closed implicitly) inside <paramref name="clip"/>; only the part that can be touched is allocated.</summary>
    public static CoverageMask Fill(IReadOnlyList<Polyline> polylines, FillRule rule, VRectI clip)
    {
        if (clip.IsEmpty)
            return CoverageMask.Empty;

        var edges = new List<Edge>();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var polyline in polylines)
        {
            var points = polyline.Points;
            var count = points.Count;
            if (count < 2)
                continue;
            for (var i = 0; i < count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % count];
                if (!double.IsFinite(a.X) || !double.IsFinite(a.Y) || !double.IsFinite(b.X) || !double.IsFinite(b.Y))
                    continue;
                minX = Math.Min(minX, a.X);
                maxX = Math.Max(maxX, a.X);
                minY = Math.Min(minY, a.Y);
                maxY = Math.Max(maxY, a.Y);
                if (a.Y == b.Y)
                    continue;
                var down = a.Y < b.Y;
                var (top, bottom) = down ? (a, b) : (b, a);
                edges.Add(new Edge { X0 = top.X, Y0 = top.Y, Y1 = bottom.Y, Slope = (bottom.X - top.X) / (bottom.Y - top.Y), Direction = down ? 1 : -1 });
            }
        }
        if (edges.Count == 0)
            return CoverageMask.Empty;

        var area = new VRectI((int)Math.Floor(Math.Max(minX, clip.X)), (int)Math.Floor(Math.Max(minY, clip.Y)), 0, 0);
        var right = (int)Math.Min(clip.Right, Math.Ceiling(Math.Min(maxX, clip.Right)));
        var bottomPx = (int)Math.Min(clip.Bottom, Math.Ceiling(Math.Min(maxY, clip.Bottom)));
        area = new VRectI(area.X, area.Y, right - area.X, bottomPx - area.Y);
        if (area.IsEmpty)
            return CoverageMask.Empty;

        edges.Sort((p, q) => p.Y0.CompareTo(q.Y0));
        var data = new byte[area.Width * area.Height];
        var width = area.Width;
        var row = new double[width + 2];
        var delta = new double[width + 2];
        var active = new List<int>();
        var crossings = new (double X, int Dir)[16];
        var next = 0;
        var weight = 1.0 / SubScanlines;

        for (var py = 0; py < area.Height; py++)
        {
            Array.Clear(row);
            Array.Clear(delta);
            var any = false;
            for (var s = 0; s < SubScanlines; s++)
            {
                var sy = area.Y + py + (s + 0.5) / SubScanlines;
                while (next < edges.Count && edges[next].Y0 <= sy)
                    active.Add(next++);
                var n = 0;
                for (var k = active.Count - 1; k >= 0; k--)
                {
                    var e = edges[active[k]];
                    if (e.Y1 <= sy)
                    {
                        active.RemoveAt(k);
                        continue;
                    }
                    if (n == crossings.Length)
                        Array.Resize(ref crossings, n * 2);
                    crossings[n++] = (e.X0 + (sy - e.Y0) * e.Slope, e.Direction);
                }
                if (n < 2)
                    continue;
                SortCrossings(crossings, n);
                var winding = 0;
                var inside = false;
                var spanStart = 0.0;
                for (var i = 0; i < n; i++)
                {
                    winding += crossings[i].Dir;
                    var nowInside = rule == FillRule.NonZero ? winding != 0 : ((i + 1) & 1) == 1;
                    if (nowInside && !inside)
                        spanStart = crossings[i].X;
                    else if (!nowInside && inside)
                        any |= AddSpan(row, delta, spanStart - area.X, crossings[i].X - area.X, width, weight);
                    inside = nowInside;
                }
            }
            if (!any)
                continue;
            var running = 0.0;
            var offset = py * width;
            for (var px = 0; px < width; px++)
            {
                running += delta[px];
                var value = (row[px] + running) * 255;
                data[offset + px] = value >= 254.5 ? (byte)255 : value <= 0 ? (byte)0 : (byte)(value + 0.5);
            }
        }
        return new CoverageMask(area, data);
    }

    private static bool AddSpan(double[] row, double[] delta, double xa, double xb, int width, double weight)
    {
        if (xa < 0)
            xa = 0;
        if (xb > width)
            xb = width;
        if (xb <= xa)
            return false;
        var ia = (int)xa;
        var ib = (int)xb;
        if (ia == ib)
        {
            row[ia] += (xb - xa) * weight;
            return true;
        }
        row[ia] += (ia + 1 - xa) * weight;
        delta[ia + 1] += weight;
        delta[ib] -= weight;
        if (ib < width)
            row[ib] += (xb - ib) * weight;
        return true;
    }

    private static void SortCrossings((double X, int Dir)[] items, int count)
    {
        if (count > 32)
        {
            Array.Sort(items, 0, count, Comparer<(double X, int Dir)>.Create((p, q) => p.X.CompareTo(q.X)));
            return;
        }
        for (var i = 1; i < count; i++)
        {
            var item = items[i];
            var j = i - 1;
            while (j >= 0 && items[j].X > item.X)
            {
                items[j + 1] = items[j];
                j--;
            }
            items[j + 1] = item;
        }
    }
}
