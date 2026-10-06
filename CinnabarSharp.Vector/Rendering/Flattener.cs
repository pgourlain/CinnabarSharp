namespace CinnabarSharp.Vector;

/// <summary>A flattened sub-path: straight segments through <see cref="Points"/>; a closed one also joins the last point to the first.</summary>
public sealed class Polyline(List<VPoint> points, bool closed)
{
    public List<VPoint> Points { get; } = points;

    public bool Closed { get; } = closed;
}

/// <summary>
/// Turns Bézier curves and arcs into line segments. The number of segments follows the curve's second difference, so the
/// polyline stays within <c>tolerance</c> of the curve. Only IEEE double arithmetic is used (no fused multiply-add,
/// no Math.Pow), so a given path flattens identically on every platform.
/// </summary>
public static class Flattener
{
    private const int MaxSegmentsPerCurve = 1000;

    /// <summary>Flattens <paramref name="path"/> after mapping it by <paramref name="matrix"/>; <paramref name="tolerance"/> is in the mapped space.</summary>
    public static List<Polyline> Flatten(VectorPath path, Matrix2D matrix, double tolerance)
    {
        var source = path.Segments.Any(s => s.Kind == SegmentKind.ArcTo) ? path.ToCubics() : path;
        var result = new List<Polyline>();
        List<VPoint>? points = null;
        var current = default(VPoint);
        var start = default(VPoint);

        void Finish(bool closed)
        {
            if (points is { Count: > 0 })
                result.Add(new Polyline(points, closed));
            points = null;
        }

        foreach (var segment in source.Segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.MoveTo:
                    Finish(false);
                    current = start = matrix.Transform(segment.End);
                    points = [current];
                    break;
                case SegmentKind.LineTo:
                    points ??= [current];
                    current = matrix.Transform(segment.End);
                    points.Add(current);
                    break;
                case SegmentKind.CubicTo:
                {
                    points ??= [current];
                    var c1 = matrix.Transform(segment.C1);
                    var c2 = matrix.Transform(segment.C2);
                    var end = matrix.Transform(segment.End);
                    FlattenCubic(points, current, c1, c2, end, tolerance);
                    current = end;
                    break;
                }
                case SegmentKind.QuadTo:
                {
                    points ??= [current];
                    var c = matrix.Transform(segment.C1);
                    var end = matrix.Transform(segment.End);
                    FlattenQuad(points, current, c, end, tolerance);
                    current = end;
                    break;
                }
                case SegmentKind.Close:
                    Finish(true);
                    current = start;
                    break;
            }
        }
        Finish(false);
        return result;
    }

    public static void FlattenCubic(List<VPoint> output, VPoint p0, VPoint p1, VPoint p2, VPoint p3, double tolerance)
    {
        var d1x = p0.X - 2 * p1.X + p2.X;
        var d1y = p0.Y - 2 * p1.Y + p2.Y;
        var d2x = p1.X - 2 * p2.X + p3.X;
        var d2y = p1.Y - 2 * p2.Y + p3.Y;
        var dd = Math.Max(Math.Sqrt(d1x * d1x + d1y * d1y), Math.Sqrt(d2x * d2x + d2y * d2y));
        var n = Segments(0.75 * dd / Math.Max(tolerance, 1e-6));
        for (var i = 1; i < n; i++)
        {
            var t = (double)i / n;
            var mt = 1 - t;
            var a = mt * mt * mt;
            var b = 3 * mt * mt * t;
            var c = 3 * mt * t * t;
            var d = t * t * t;
            output.Add(new VPoint(a * p0.X + b * p1.X + c * p2.X + d * p3.X, a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y));
        }
        output.Add(p3);
    }

    public static void FlattenQuad(List<VPoint> output, VPoint p0, VPoint p1, VPoint p2, double tolerance)
    {
        var dx = p0.X - 2 * p1.X + p2.X;
        var dy = p0.Y - 2 * p1.Y + p2.Y;
        var dd = Math.Sqrt(dx * dx + dy * dy);
        var n = Segments(0.25 * dd / Math.Max(tolerance, 1e-6));
        for (var i = 1; i < n; i++)
        {
            var t = (double)i / n;
            var mt = 1 - t;
            output.Add(new VPoint(mt * mt * p0.X + 2 * mt * t * p1.X + t * t * p2.X, mt * mt * p0.Y + 2 * mt * t * p1.Y + t * t * p2.Y));
        }
        output.Add(p2);
    }

    private static int Segments(double ratio) =>
        Math.Clamp((int)Math.Ceiling(Math.Sqrt(ratio)), 1, MaxSegmentsPerCurve);
}
