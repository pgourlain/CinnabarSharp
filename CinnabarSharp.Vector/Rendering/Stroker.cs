namespace CinnabarSharp.Vector;

/// <summary>What the stroker needs to know about a stroke.</summary>
public sealed record StrokeStyle(double Width, LineCap Cap = LineCap.Butt, LineJoin Join = LineJoin.Miter,
    double MiterLimit = 4, double[]? Dashes = null, double DashOffset = 0);

/// <summary>
/// Converts a path and a stroke style into polygons to fill with the nonzero rule: one piece per segment, per join and per
/// cap, all with the same orientation, so overlaps never cancel. Works in the path's own space; flatten there (a
/// non-uniform transform is applied to the polygons afterwards, so the stroke is as the SVG says: scaled with the
/// transform). Widths below a pixel are drawn at their true (thin) width.
/// </summary>
public static class Stroker
{
    /// <summary>Stroke outline of the flattened sub-paths. <paramref name="tolerance"/> bounds the error of round joins and caps.</summary>
    public static List<Polyline> Stroke(IReadOnlyList<Polyline> subpaths, StrokeStyle style, double tolerance)
    {
        var result = new List<Polyline>();
        var half = style.Width / 2;
        if (!(half > 0) || !double.IsFinite(half))
            return result;

        IEnumerable<Polyline> source = subpaths;
        if (style.Dashes is { Length: > 0 } dashes)
            source = subpaths.SelectMany(p => Dash(p, dashes, style.DashOffset));

        foreach (var polyline in source)
            StrokeOne(result, polyline, style, half, tolerance);
        return result;
    }

    private static void StrokeOne(List<Polyline> output, Polyline polyline, StrokeStyle style, double half, double tolerance)
    {
        // Drop repeated points: they have no direction.
        var points = new List<VPoint>(polyline.Points.Count);
        foreach (var p in polyline.Points)
            if (points.Count == 0 || p != points[^1])
                points.Add(p);
        var closed = polyline.Closed;
        if (closed && points.Count > 1 && points[0] == points[^1])
            points.RemoveAt(points.Count - 1);

        if (points.Count == 1)
        {
            // A zero-length sub-path: round cap draws a dot, square cap a square, butt nothing.
            var c = points[0];
            if (style.Cap == LineCap.Round)
                AddDisk(output, c, half, tolerance);
            else if (style.Cap == LineCap.Square)
                AddQuad(output, new VPoint(c.X - half, c.Y - half), new VPoint(c.X + half, c.Y - half),
                    new VPoint(c.X + half, c.Y + half), new VPoint(c.X - half, c.Y + half));
            return;
        }
        if (points.Count < 2)
            return;

        var segments = closed ? points.Count : points.Count - 1;
        var directions = new VVector[segments];
        for (var i = 0; i < segments; i++)
            directions[i] = (points[(i + 1) % points.Count] - points[i]).Normalized();

        // Segment bodies.
        for (var i = 0; i < segments; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var n = new VVector(-directions[i].Y, directions[i].X) * half;
            AddQuad(output, a + n, b + n, b - n, a - n);
        }

        // Joins.
        var firstJoin = closed ? 0 : 1;
        var lastJoin = segments - 1;
        for (var i = firstJoin; i <= lastJoin; i++)
        {
            var incoming = directions[(i + segments - 1) % segments];
            var outgoing = directions[i % segments];
            AddJoin(output, points[i % points.Count], incoming, outgoing, style, half, tolerance);
        }

        // Caps.
        if (!closed)
        {
            AddCap(output, points[0], -directions[0], style.Cap, half, tolerance);
            AddCap(output, points[^1], directions[^1], style.Cap, half, tolerance);
        }
    }

    private static void AddJoin(List<Polyline> output, VPoint v, VVector d0, VVector d1, StrokeStyle style, double half, double tolerance)
    {
        var cross = d0.Cross(d1);
        var dot = d0.Dot(d1);
        // Straight on: the two bodies already meet.
        if (Math.Abs(cross) < 1e-9 && dot > 0)
            return;
        if (style.Join == LineJoin.Round)
        {
            AddDisk(output, v, half, tolerance);
            return;
        }
        var n0 = new VVector(-d0.Y, d0.X);
        var n1 = new VVector(-d1.Y, d1.X);
        var side = cross > 0 ? -1.0 : 1.0;
        var a = v + n0 * (side * half);
        var b = v + n1 * (side * half);
        if (style.Join == LineJoin.Miter)
        {
            var cosTurn = n0.Dot(n1);
            // miter length / width = 1 / sin(theta / 2) = sqrt(2 / (1 + cos(turn)))
            if (1 + cosTurn > 1e-9 && Math.Sqrt(2 / (1 + cosTurn)) <= style.MiterLimit)
            {
                var tip = v + (n0 + n1) * (side * half / (1 + cosTurn));
                AddQuad(output, v, a, tip, b);
                return;
            }
        }
        AddTriangle(output, v, a, b);
    }

    private static void AddCap(List<Polyline> output, VPoint end, VVector outward, LineCap cap, double half, double tolerance)
    {
        switch (cap)
        {
            case LineCap.Round:
                AddDisk(output, end, half, tolerance);
                break;
            case LineCap.Square:
            {
                var n = new VVector(-outward.Y, outward.X) * half;
                var extend = outward * half;
                AddQuad(output, end + n, end - n, end - n + extend, end + n + extend);
                break;
            }
        }
    }

    private static void AddDisk(List<Polyline> output, VPoint center, double radius, double tolerance)
    {
        var count = DiskSegments(radius, tolerance);
        var points = new List<VPoint>(count);
        for (var i = 0; i < count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            points.Add(new VPoint(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
        }
        output.Add(new Polyline(points, true));
    }

    private static int DiskSegments(double radius, double tolerance)
    {
        if (radius <= tolerance)
            return 8;
        var step = 2 * Math.Acos(1 - tolerance / radius);
        return Math.Clamp((int)Math.Ceiling(2 * Math.PI / Math.Max(step, 1e-3)), 8, 256);
    }

    private static void AddTriangle(List<Polyline> output, VPoint a, VPoint b, VPoint c) => AddOriented(output, [a, b, c]);

    private static void AddQuad(List<Polyline> output, VPoint a, VPoint b, VPoint c, VPoint d) => AddOriented(output, [a, b, c, d]);

    // Same orientation for every piece (positive area), so the nonzero rule unions them.
    private static void AddOriented(List<Polyline> output, List<VPoint> points)
    {
        double area = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var q = points[(i + 1) % points.Count];
            area += p.X * q.Y - q.X * p.Y;
        }
        if (area < 0)
            points.Reverse();
        output.Add(new Polyline(points, true));
    }

    // ---- Dashes ----

    /// <summary>Splits a sub-path into dashes (open polylines). An odd-length pattern is repeated, like SVG says.</summary>
    public static List<Polyline> Dash(Polyline polyline, double[] pattern, double offset)
    {
        var lengths = pattern.Length % 2 == 1 ? pattern.Concat(pattern).ToArray() : pattern;
        var total = lengths.Sum();
        var result = new List<Polyline>();
        if (!(total > 0))
        {
            result.Add(polyline);
            return result;
        }

        var points = polyline.Points;
        if (polyline.Closed && points.Count > 0)
            points = [.. points, points[0]];

        // Position in the pattern at the start of the path.
        var phase = ((offset % total) + total) % total;
        var index = 0;
        while (phase >= lengths[index] && lengths[index] > 0)
        {
            phase -= lengths[index];
            index = (index + 1) % lengths.Length;
        }
        var remaining = lengths[index] - phase;
        var on = index % 2 == 0;

        List<VPoint>? dash = on && points.Count > 0 ? [points[0]] : null;
        var startedOn = on;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var length = a.DistanceTo(b);
            var position = 0.0;
            while (length - position > remaining)
            {
                position += remaining;
                var p = a.Lerp(b, position / length);
                if (on)
                {
                    dash!.Add(p);
                    result.Add(new Polyline(dash, false));
                    dash = null;
                }
                else
                {
                    dash = [p];
                }
                on = !on;
                index = (index + 1) % lengths.Length;
                remaining = lengths[index];
                if (remaining <= 0 && length - position <= 0)
                    break;
            }
            remaining -= length - position;
            if (on)
                dash!.Add(b);
        }
        if (on && dash is { Count: > 0 })
        {
            // A closed path whose last dash runs into the first one is one dash across the start.
            if (polyline.Closed && startedOn && result.Count > 0)
            {
                var first = result[0].Points;
                dash.AddRange(first.Skip(1));
                result[0] = new Polyline(dash, false);
            }
            else
            {
                result.Add(new Polyline(dash, false));
            }
        }
        return result;
    }
}
