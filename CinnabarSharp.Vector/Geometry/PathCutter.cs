namespace CinnabarSharp.Vector;

/// <summary>
/// One sub-path of a path cut at the points where it crosses other outlines. <see cref="Pieces"/> are the runs between two
/// consecutive cuts, in path order (empty when nothing crosses the sub-path, or when a closed one is touched only once, which
/// leaves no piece to take out); a closed sub-path's last piece runs through its start point.
/// </summary>
public sealed class CutFigure(int index, VectorPath original, bool isClosed, IReadOnlyList<VectorPath> pieces)
{
    public int Index { get; } = index;

    /// <summary>The sub-path as it was (its arcs and its Close included).</summary>
    public VectorPath Original { get; } = original;

    public bool IsClosed { get; } = isClosed;

    public IReadOnlyList<VectorPath> Pieces { get; } = pieces;

    /// <summary>
    /// What is left of the sub-path when one piece is taken out: for a closed one a single open path (the other pieces, which
    /// follow each other all the way round), for an open one the part before and the part after (either may be missing).
    /// </summary>
    public IReadOnlyList<VectorPath> Without(int piece)
    {
        if (piece < 0 || piece >= Pieces.Count)
            throw new ArgumentOutOfRangeException(nameof(piece));
        var result = new List<VectorPath>();
        if (IsClosed)
        {
            var rest = new List<VectorPath>();
            for (var i = 1; i < Pieces.Count; i++)
                rest.Add(Pieces[(piece + i) % Pieces.Count]);
            if (rest.Count > 0)
                result.Add(PathCutter.Join(rest));
            return result;
        }
        if (piece > 0)
            result.Add(PathCutter.Join(Pieces.Take(piece)));
        if (piece < Pieces.Count - 1)
            result.Add(PathCutter.Join(Pieces.Skip(piece + 1)));
        return result;
    }
}

/// <summary>
/// Cuts a path where it crosses other outlines, keeping the curves: the Bézier segments are split at the crossings, not
/// flattened. This is what the Scissors tool uses to take out the run between two crossings.
/// </summary>
public static class PathCutter
{
    private const double Epsilon = 1e-7;

    private readonly record struct Curve(SegmentKind Kind, VPoint P0, VPoint C1, VPoint C2, VPoint P3);

    /// <summary>
    /// The sub-paths of <paramref name="path"/> cut at their crossings with <paramref name="cutters"/> (outlines in the same space;
    /// open sub-paths of a cutter count as outlines too). Sub-paths with no segment are left out.
    /// </summary>
    public static IReadOnlyList<CutFigure> Cut(VectorPath path, IReadOnlyList<VectorPath> cutters, double tolerance = 0.05)
    {
        var originals = path.Figures.ToList();
        var cubics = path.ToCubics().Figures.ToList();
        var edges = new List<(VPoint A, VPoint B)>();
        var ends = new List<VPoint>();
        foreach (var cutter in cutters)
            foreach (var line in Flattener.Flatten(cutter, Matrix2D.Identity, tolerance))
            {
                var points = line.Points;
                if (!line.Closed && points.Count > 1)
                {
                    ends.Add(points[0]);
                    ends.Add(points[^1]);
                }
                for (var i = 0; i + 1 < points.Count; i++)
                    edges.Add((points[i], points[i + 1]));
                if (line.Closed && points.Count > 2)
                    edges.Add((points[^1], points[0]));
            }

        var result = new List<CutFigure>();
        for (var f = 0; f < cubics.Count && f < originals.Count; f++)
        {
            var curves = Curves(cubics[f]);
            if (curves.Count == 0)
                continue;
            var cuts = Crossings(curves, edges, ends, cubics[f].IsClosed, tolerance);
            result.Add(new CutFigure(f, FigurePath(originals[f]), cubics[f].IsClosed, Pieces(curves, cuts, cubics[f].IsClosed)));
        }
        return result;
    }

    /// <summary>The paths one after the other as a single path: each one starts where the previous one ended.</summary>
    internal static VectorPath Join(IEnumerable<VectorPath> pieces)
    {
        var joined = new VectorPath();
        var first = true;
        foreach (var piece in pieces)
        {
            foreach (var segment in piece.Segments)
            {
                if (segment.Kind == SegmentKind.MoveTo && !first)
                    continue;
                joined.Add(segment);
            }
            first = false;
        }
        return joined;
    }

    private static VectorPath FigurePath(PathFigure figure)
    {
        var path = new VectorPath().MoveTo(figure.Start);
        foreach (var segment in figure.Segments)
            path.Add(segment);
        if (figure.IsClosed)
            path.Close();
        return path;
    }

    // ---- Curves ----

    private static List<Curve> Curves(PathFigure figure)
    {
        var curves = new List<Curve>();
        var current = figure.Start;
        foreach (var segment in figure.Segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.LineTo:
                    curves.Add(new Curve(SegmentKind.LineTo, current, default, default, segment.End));
                    break;
                case SegmentKind.QuadTo:
                    curves.Add(new Curve(SegmentKind.QuadTo, current, segment.C1, default, segment.End));
                    break;
                case SegmentKind.CubicTo:
                    curves.Add(new Curve(SegmentKind.CubicTo, current, segment.C1, segment.C2, segment.End));
                    break;
            }
            current = segment.End;
        }
        if (figure.IsClosed && current.DistanceTo(figure.Start) > 1e-9)
            curves.Add(new Curve(SegmentKind.LineTo, current, default, default, figure.Start));
        return curves;
    }

    private static VPoint At(Curve c, double t)
    {
        var u = 1 - t;
        return c.Kind switch
        {
            SegmentKind.LineTo => c.P0.Lerp(c.P3, t),
            SegmentKind.QuadTo => new VPoint(u * u * c.P0.X + 2 * u * t * c.C1.X + t * t * c.P3.X,
                u * u * c.P0.Y + 2 * u * t * c.C1.Y + t * t * c.P3.Y),
            _ => new VPoint(u * u * u * c.P0.X + 3 * u * u * t * c.C1.X + 3 * u * t * t * c.C2.X + t * t * t * c.P3.X,
                u * u * u * c.P0.Y + 3 * u * u * t * c.C1.Y + 3 * u * t * t * c.C2.Y + t * t * t * c.P3.Y),
        };
    }

    /// <summary>The part of the curve between the parameters <paramref name="t0"/> and <paramref name="t1"/>.</summary>
    private static Curve Sub(Curve c, double t0, double t1)
    {
        if (t0 <= 0 && t1 >= 1)
            return c;
        if (c.Kind == SegmentKind.LineTo)
            return c with { P0 = c.P0.Lerp(c.P3, t0), P3 = c.P0.Lerp(c.P3, t1) };
        if (c.Kind == SegmentKind.QuadTo)
        {
            var (p0, p1, p2) = (c.P0, c.C1, c.P3);
            if (t1 < 1)
                (p1, p2) = (p0.Lerp(p1, t1), p0.Lerp(p1, t1).Lerp(p1.Lerp(p2, t1), t1));
            if (t0 > 0)
            {
                var s = t0 / t1;
                (p0, p1) = (p0.Lerp(p1, s).Lerp(p1.Lerp(p2, s), s), p1.Lerp(p2, s));
            }
            return new Curve(SegmentKind.QuadTo, p0, p1, default, p2);
        }
        var (a, b, d, e) = (c.P0, c.C1, c.C2, c.P3);
        if (t1 < 1)
        {
            var (ab, bd, de) = (a.Lerp(b, t1), b.Lerp(d, t1), d.Lerp(e, t1));
            var (abd, bde) = (ab.Lerp(bd, t1), bd.Lerp(de, t1));
            (b, d, e) = (ab, abd, abd.Lerp(bde, t1));
        }
        if (t0 > 0)
        {
            var s = t0 / t1;
            var (ab, bd, de) = (a.Lerp(b, s), b.Lerp(d, s), d.Lerp(e, s));
            var (abd, bde) = (ab.Lerp(bd, s), bd.Lerp(de, s));
            (a, b, d) = (abd.Lerp(bde, s), bde, de);
        }
        return new Curve(SegmentKind.CubicTo, a, b, d, e);
    }

    private static PathSegment Emit(Curve c) => c.Kind switch
    {
        SegmentKind.LineTo => PathSegment.LineTo(c.P3),
        SegmentKind.QuadTo => PathSegment.QuadTo(c.C1, c.P3),
        _ => PathSegment.CubicTo(c.C1, c.C2, c.P3),
    };

    // ---- Crossings ----

    /// <summary>
    /// Positions of the crossings along the sub-path, sorted, as a segment number plus a fraction (3.25 is a quarter of the way
    /// along the fourth segment). Ends of an open sub-path are not crossings.
    /// </summary>
    private static List<double> Crossings(List<Curve> curves, List<(VPoint A, VPoint B)> edges, List<VPoint> ends, bool closed, double tolerance)
    {
        var found = new List<double>();
        if (edges.Count == 0)
            return found;
        for (var i = 0; i < curves.Count; i++)
        {
            var curve = curves[i];
            var steps = Steps(curve, tolerance);
            var previous = curve.P0;
            for (var k = 1; k <= steps; k++)
            {
                var t = (double)k / steps;
                var next = k == steps ? curve.P3 : At(curve, t);
                var minX = Math.Min(previous.X, next.X) - Epsilon;
                var maxX = Math.Max(previous.X, next.X) + Epsilon;
                var minY = Math.Min(previous.Y, next.Y) - Epsilon;
                var maxY = Math.Max(previous.Y, next.Y) + Epsilon;
                foreach (var (a, b) in edges)
                {
                    if (Math.Max(a.X, b.X) < minX || Math.Min(a.X, b.X) > maxX || Math.Max(a.Y, b.Y) < minY || Math.Min(a.Y, b.Y) > maxY)
                        continue;
                    if (Intersect(previous, next, a, b) is { } along)
                        found.Add(i + (k - 1 + along) / steps);
                }
                previous = next;
            }
        }

        // An open outline that was itself cut earlier ends on this one only up to the error of its own cut: an end that comes
        // within a hair of the path counts as touching it, unless a real crossing already lies there.
        var snap = Math.Max(tolerance * 4, 1e-3);
        foreach (var end in ends)
        {
            var nearest = (Distance: double.MaxValue, Position: 0.0);
            for (var i = 0; i < curves.Count; i++)
            {
                var steps = Steps(curves[i], tolerance);
                var previous = curves[i].P0;
                for (var k = 1; k <= steps; k++)
                {
                    var next = k == steps ? curves[i].P3 : At(curves[i], (double)k / steps);
                    var ab = next - previous;
                    var lengthSquared = ab.Dot(ab);
                    var w = lengthSquared == 0 ? 0 : Math.Clamp(ab.Dot(end - previous) / lengthSquared, 0, 1);
                    var distance = end.DistanceTo(previous + ab * w);
                    if (distance < nearest.Distance)
                        nearest = (distance, i + (k - 1 + w) / steps);
                    previous = next;
                }
            }
            if (nearest.Distance > snap)
                continue;
            var covered = false;
            foreach (var raw in found)
            {
                var c = Math.Min((int)Math.Floor(raw), curves.Count - 1);
                if (At(curves[c], raw - c).DistanceTo(end) <= snap * 2)
                    covered = true;
            }
            if (!covered)
                found.Add(nearest.Position);
        }

        var count = curves.Count;
        var positions = new List<double>();
        foreach (var raw in found)
        {
            var s = raw;
            var rounded = Math.Round(s);
            if (Math.Abs(s - rounded) < Epsilon)
                s = rounded;
            if (closed)
            {
                if (s >= count - Epsilon)
                    s = 0;
            }
            else if (s <= Epsilon || s >= count - Epsilon)
            {
                continue;
            }
            positions.Add(s);
        }
        positions.Sort();
        var distinct = new List<double>();
        foreach (var s in positions)
            if (distinct.Count == 0 || s - distinct[^1] > Epsilon)
                distinct.Add(s);
        return distinct;
    }

    private static int Steps(Curve curve, double tolerance)
    {
        if (curve.Kind == SegmentKind.LineTo)
            return 1;
        var length = curve.P0.DistanceTo(curve.C1) + curve.C1.DistanceTo(curve.Kind == SegmentKind.QuadTo ? curve.P3 : curve.C2);
        if (curve.Kind == SegmentKind.CubicTo)
            length += curve.C2.DistanceTo(curve.P3);
        return Math.Clamp((int)Math.Ceiling(Math.Sqrt(length / Math.Max(tolerance, 1e-6))), 4, 400);
    }

    /// <summary>Where along the segment p→q (0 to 1) it crosses the segment a→b; null when they do not cross.</summary>
    private static double? Intersect(VPoint p, VPoint q, VPoint a, VPoint b)
    {
        var r = q - p;
        var s = b - a;
        var denominator = r.Cross(s);
        if (Math.Abs(denominator) < 1e-14)
            return null;
        var d = a - p;
        var t = d.Cross(s) / denominator;
        var u = d.Cross(r) / denominator;
        if (t < -Epsilon || t > 1 + Epsilon || u < -Epsilon || u > 1 + Epsilon)
            return null;
        return Math.Clamp(t, 0, 1);
    }

    // ---- Pieces ----

    private static List<VectorPath> Pieces(List<Curve> curves, List<double> cuts, bool closed)
    {
        var pieces = new List<VectorPath>();
        var count = curves.Count;
        if (closed)
        {
            // One crossing leaves one piece that is the whole outline: nothing can be taken out.
            if (cuts.Count < 2)
                return pieces;
            for (var j = 0; j < cuts.Count; j++)
            {
                if (j < cuts.Count - 1)
                {
                    pieces.Add(Extract(curves, cuts[j], cuts[j + 1]));
                    continue;
                }
                var wrapped = new List<VectorPath> { Extract(curves, cuts[j], count) };
                if (cuts[0] > Epsilon)
                    wrapped.Add(Extract(curves, 0, cuts[0]));
                pieces.Add(Join(wrapped));
            }
            return pieces;
        }
        if (cuts.Count == 0)
            return pieces;
        var bounds = new List<double> { 0 };
        bounds.AddRange(cuts);
        bounds.Add(count);
        for (var j = 0; j + 1 < bounds.Count; j++)
            pieces.Add(Extract(curves, bounds[j], bounds[j + 1]));
        return pieces;
    }

    /// <summary>The run of the sub-path from position <paramref name="from"/> to <paramref name="to"/> (from &lt; to).</summary>
    private static VectorPath Extract(List<Curve> curves, double from, double to)
    {
        var first = Math.Min((int)Math.Floor(from + Epsilon), curves.Count - 1);
        var path = new VectorPath().MoveTo(At(curves[first], Math.Clamp(from - first, 0, 1)));
        for (var i = first; i < curves.Count && i < to - Epsilon; i++)
        {
            var t0 = Math.Max(from - i, 0);
            var t1 = Math.Min(to - i, 1);
            if (t1 - t0 < Epsilon)
                continue;
            path.Add(Emit(Sub(curves[i], t0, t1)));
        }
        return path;
    }
}
