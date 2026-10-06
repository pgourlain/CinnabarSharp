namespace CinnabarSharp.Vector;

public enum SegmentKind
{
    MoveTo,
    LineTo,
    CubicTo,
    QuadTo,
    ArcTo,
    Close,
}

/// <summary>
/// One command of a path, in absolute coordinates. <see cref="End"/> is the end point (for <see cref="SegmentKind.Close"/>:
/// the start of the figure it closes). Control points and arc parameters are only meaningful for their kind.
/// </summary>
public readonly record struct PathSegment(SegmentKind Kind, VPoint End, VPoint C1 = default, VPoint C2 = default,
    double Rx = 0, double Ry = 0, double XAxisRotation = 0, bool LargeArc = false, bool Sweep = false)
{
    public static PathSegment MoveTo(VPoint p) => new(SegmentKind.MoveTo, p);

    public static PathSegment LineTo(VPoint p) => new(SegmentKind.LineTo, p);

    public static PathSegment CubicTo(VPoint c1, VPoint c2, VPoint end) => new(SegmentKind.CubicTo, end, c1, c2);

    /// <summary>Quadratic Bézier; the control point is <see cref="C1"/>.</summary>
    public static PathSegment QuadTo(VPoint c, VPoint end) => new(SegmentKind.QuadTo, end, c);

    public static PathSegment ArcTo(double rx, double ry, double xAxisRotation, bool largeArc, bool sweep, VPoint end) =>
        new(SegmentKind.ArcTo, end, Rx: rx, Ry: ry, XAxisRotation: xAxisRotation, LargeArc: largeArc, Sweep: sweep);

    public static PathSegment Close(VPoint start) => new(SegmentKind.Close, start);

    /// <summary>The segment with every point (and arc radius) mapped by <paramref name="m"/>; arcs must have been converted first.</summary>
    internal PathSegment Map(Matrix2D m) => this with { End = m.Transform(End), C1 = m.Transform(C1), C2 = m.Transform(C2) };
}

/// <summary>A sub-path: a start point and the segments that follow it.</summary>
public sealed record PathFigure(VPoint Start, IReadOnlyList<PathSegment> Segments, bool IsClosed);

/// <summary>
/// A list of path segments. Every figure starts with a <see cref="SegmentKind.MoveTo"/>; a closed figure ends with
/// <see cref="SegmentKind.Close"/>. Coordinates are absolute.
/// </summary>
public sealed class VectorPath
{
    private readonly List<PathSegment> _segments = [];

    public VectorPath()
    {
    }

    public VectorPath(IEnumerable<PathSegment> segments) => _segments.AddRange(segments);

    public IReadOnlyList<PathSegment> Segments => _segments;

    public bool IsEmpty => _segments.Count == 0;

    private VPoint _start;
    private VPoint _current;

    /// <summary>Current point (end of the last segment).</summary>
    public VPoint Current => _segments.Count == 0 ? default : _current;

    public VectorPath MoveTo(double x, double y) => MoveTo(new VPoint(x, y));

    public VectorPath MoveTo(VPoint p)
    {
        // Two MoveTo in a row: the first one draws nothing.
        if (_segments.Count > 0 && _segments[^1].Kind == SegmentKind.MoveTo)
            _segments.RemoveAt(_segments.Count - 1);
        _segments.Add(PathSegment.MoveTo(p));
        _start = _current = p;
        return this;
    }

    public VectorPath LineTo(double x, double y) => LineTo(new VPoint(x, y));

    public VectorPath LineTo(VPoint p)
    {
        EnsureFigure();
        _segments.Add(PathSegment.LineTo(p));
        _current = p;
        return this;
    }

    public VectorPath CubicTo(VPoint c1, VPoint c2, VPoint end)
    {
        EnsureFigure();
        _segments.Add(PathSegment.CubicTo(c1, c2, end));
        _current = end;
        return this;
    }

    public VectorPath QuadTo(VPoint c, VPoint end)
    {
        EnsureFigure();
        _segments.Add(PathSegment.QuadTo(c, end));
        _current = end;
        return this;
    }

    public VectorPath ArcTo(double rx, double ry, double xAxisRotation, bool largeArc, bool sweep, VPoint end)
    {
        EnsureFigure();
        _segments.Add(PathSegment.ArcTo(rx, ry, xAxisRotation, largeArc, sweep, end));
        _current = end;
        return this;
    }

    public VectorPath Close()
    {
        if (_segments.Count == 0 || _segments[^1].Kind == SegmentKind.Close)
            return this;
        EnsureFigure();
        _segments.Add(PathSegment.Close(_start));
        _current = _start;
        return this;
    }

    public void Add(PathSegment segment)
    {
        switch (segment.Kind)
        {
            case SegmentKind.MoveTo: MoveTo(segment.End); break;
            case SegmentKind.Close: Close(); break;
            default:
                EnsureFigure();
                _segments.Add(segment);
                _current = segment.End;
                break;
        }
    }

    public VectorPath Append(VectorPath other)
    {
        foreach (var segment in other._segments)
            Add(segment);
        return this;
    }

    // After a Close, a drawing command starts a new figure at the figure's start point.
    private void EnsureFigure()
    {
        if (_segments.Count == 0)
            MoveTo(0, 0);
        else if (_segments[^1].Kind == SegmentKind.Close)
            _segments.Add(PathSegment.MoveTo(_start));
    }

    public VectorPath Clone() => new(_segments) { _start = _start, _current = _current };

    /// <summary>The sub-paths in order. A figure that was only moved to (no drawing) is returned with no segments.</summary>
    public IEnumerable<PathFigure> Figures
    {
        get
        {
            VPoint start = default;
            List<PathSegment>? current = null;
            foreach (var segment in _segments)
            {
                switch (segment.Kind)
                {
                    case SegmentKind.MoveTo:
                        if (current is not null)
                            yield return new PathFigure(start, current, false);
                        start = segment.End;
                        current = [];
                        break;
                    case SegmentKind.Close:
                        if (current is not null)
                            yield return new PathFigure(start, current, true);
                        current = null;
                        break;
                    default:
                        current ??= [];
                        current.Add(segment);
                        break;
                }
            }
            if (current is not null)
                yield return new PathFigure(start, current, false);
        }
    }

    /// <summary>Tight bounding box of the curves (extrema included); empty for an empty path.</summary>
    public VRect Bounds
    {
        get
        {
            var box = new BoundsBuilder();
            var current = default(VPoint);
            var start = default(VPoint);
            foreach (var segment in _segments)
            {
                switch (segment.Kind)
                {
                    case SegmentKind.MoveTo:
                        current = start = segment.End;
                        box.Add(current);
                        break;
                    case SegmentKind.LineTo:
                        box.Add(segment.End);
                        current = segment.End;
                        break;
                    case SegmentKind.CubicTo:
                        box.AddCubic(current, segment.C1, segment.C2, segment.End);
                        current = segment.End;
                        break;
                    case SegmentKind.QuadTo:
                        box.AddQuad(current, segment.C1, segment.End);
                        current = segment.End;
                        break;
                    case SegmentKind.ArcTo:
                        foreach (var cubic in ArcConverter.ToCubics(current, segment))
                            box.AddCubic(cubic.Start, cubic.C1, cubic.C2, cubic.End);
                        if (segment.Rx == 0 || segment.Ry == 0)
                            box.Add(segment.End);
                        current = segment.End;
                        break;
                    case SegmentKind.Close:
                        current = start;
                        break;
                }
            }
            return box.Result;
        }
    }

    /// <summary>A copy where every arc is replaced by cubic Béziers (a zero radius becomes a line).</summary>
    public VectorPath ToCubics()
    {
        var result = new VectorPath();
        var current = default(VPoint);
        var start = default(VPoint);
        foreach (var segment in _segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.ArcTo:
                    if (segment.Rx == 0 || segment.Ry == 0 || current == segment.End)
                        result.Add(PathSegment.LineTo(segment.End));
                    else
                        foreach (var cubic in ArcConverter.ToCubics(current, segment))
                            result.Add(PathSegment.CubicTo(cubic.C1, cubic.C2, cubic.End));
                    current = segment.End;
                    break;
                case SegmentKind.MoveTo:
                    current = start = segment.End;
                    result.Add(segment);
                    break;
                case SegmentKind.Close:
                    current = start;
                    result.Add(segment);
                    break;
                default:
                    current = segment.End;
                    result.Add(segment);
                    break;
            }
        }
        return result;
    }

    /// <summary>The path mapped by <paramref name="m"/>. Arcs become cubics unless the matrix is the identity.</summary>
    public VectorPath Transformed(Matrix2D m)
    {
        if (m.IsIdentity)
            return Clone();
        var source = _segments.Any(s => s.Kind == SegmentKind.ArcTo) ? ToCubics() : this;
        var result = new VectorPath();
        foreach (var segment in source._segments)
            result.Add(segment.Map(m));
        return result;
    }

    /// <summary>The path moved by (<paramref name="dx"/>, <paramref name="dy"/>); unlike <see cref="Transformed"/> it keeps arcs.</summary>
    public VectorPath Translated(double dx, double dy)
    {
        var result = new VectorPath();
        VPoint Move(VPoint p) => new(p.X + dx, p.Y + dy);
        foreach (var segment in _segments)
            result.Add(segment with { End = Move(segment.End), C1 = Move(segment.C1), C2 = Move(segment.C2) });
        return result;
    }

    // ---- Shapes ----

    public static VectorPath FromRect(double x, double y, double width, double height, double rx = 0, double ry = 0)
    {
        var path = new VectorPath();
        rx = Math.Clamp(rx, 0, width / 2);
        ry = Math.Clamp(ry, 0, height / 2);
        if (rx <= 0 || ry <= 0)
        {
            path.MoveTo(x, y).LineTo(x + width, y).LineTo(x + width, y + height).LineTo(x, y + height).Close();
            return path;
        }
        path.MoveTo(x + rx, y)
            .LineTo(x + width - rx, y)
            .ArcTo(rx, ry, 0, false, true, new VPoint(x + width, y + ry))
            .LineTo(x + width, y + height - ry)
            .ArcTo(rx, ry, 0, false, true, new VPoint(x + width - rx, y + height))
            .LineTo(x + rx, y + height)
            .ArcTo(rx, ry, 0, false, true, new VPoint(x, y + height - ry))
            .LineTo(x, y + ry)
            .ArcTo(rx, ry, 0, false, true, new VPoint(x + rx, y))
            .Close();
        return path;
    }

    public static VectorPath FromEllipse(double cx, double cy, double rx, double ry)
    {
        var path = new VectorPath();
        if (rx <= 0 || ry <= 0)
            return path;
        path.MoveTo(cx + rx, cy)
            .ArcTo(rx, ry, 0, false, true, new VPoint(cx, cy + ry))
            .ArcTo(rx, ry, 0, false, true, new VPoint(cx - rx, cy))
            .ArcTo(rx, ry, 0, false, true, new VPoint(cx, cy - ry))
            .ArcTo(rx, ry, 0, false, true, new VPoint(cx + rx, cy))
            .Close();
        return path;
    }

    public static VectorPath FromPolyline(IReadOnlyList<VPoint> points, bool close)
    {
        var path = new VectorPath();
        if (points.Count == 0)
            return path;
        path.MoveTo(points[0]);
        for (var i = 1; i < points.Count; i++)
            path.LineTo(points[i]);
        if (close)
            path.Close();
        return path;
    }

    private sealed class BoundsBuilder
    {
        private double _minX = double.PositiveInfinity, _minY = double.PositiveInfinity;
        private double _maxX = double.NegativeInfinity, _maxY = double.NegativeInfinity;

        public VRect Result => _minX > _maxX ? VRect.Empty : VRect.FromLTRB(_minX, _minY, _maxX, _maxY);

        public void Add(VPoint p)
        {
            if (p.X < _minX) _minX = p.X;
            if (p.X > _maxX) _maxX = p.X;
            if (p.Y < _minY) _minY = p.Y;
            if (p.Y > _maxY) _maxY = p.Y;
        }

        public void AddQuad(VPoint p0, VPoint c, VPoint p1)
        {
            Add(p0);
            Add(p1);
            foreach (var t in QuadExtrema(p0.X, c.X, p1.X).Concat(QuadExtrema(p0.Y, c.Y, p1.Y)))
            {
                var mt = 1 - t;
                Add(new VPoint(mt * mt * p0.X + 2 * mt * t * c.X + t * t * p1.X,
                    mt * mt * p0.Y + 2 * mt * t * c.Y + t * t * p1.Y));
            }
        }

        private static IEnumerable<double> QuadExtrema(double a, double b, double c)
        {
            var denominator = a - 2 * b + c;
            if (denominator == 0)
                yield break;
            var t = (a - b) / denominator;
            if (t > 0 && t < 1)
                yield return t;
        }

        public void AddCubic(VPoint p0, VPoint c1, VPoint c2, VPoint p1)
        {
            Add(p0);
            Add(p1);
            foreach (var t in CubicExtrema(p0.X, c1.X, c2.X, p1.X).Concat(CubicExtrema(p0.Y, c1.Y, c2.Y, p1.Y)))
            {
                var mt = 1 - t;
                var (a, b, c, d) = (mt * mt * mt, 3 * mt * mt * t, 3 * mt * t * t, t * t * t);
                Add(new VPoint(a * p0.X + b * c1.X + c * c2.X + d * p1.X, a * p0.Y + b * c1.Y + c * c2.Y + d * p1.Y));
            }
        }

        private static IEnumerable<double> CubicExtrema(double p0, double p1, double p2, double p3)
        {
            // Derivative: 3(-p0 + 3p1 - 3p2 + p3)t^2 + 6(p0 - 2p1 + p2)t + 3(p1 - p0)
            var a = -p0 + 3 * p1 - 3 * p2 + p3;
            var b = 2 * (p0 - 2 * p1 + p2);
            var c = p1 - p0;
            if (Math.Abs(a) < 1e-12)
            {
                if (Math.Abs(b) > 1e-12)
                {
                    var t = -c / b;
                    if (t > 0 && t < 1)
                        yield return t;
                }
                yield break;
            }
            var disc = b * b - 4 * a * c;
            if (disc < 0)
                yield break;
            var root = Math.Sqrt(disc);
            var t1 = (-b + root) / (2 * a);
            var t2 = (-b - root) / (2 * a);
            if (t1 > 0 && t1 < 1)
                yield return t1;
            if (t2 > 0 && t2 < 1)
                yield return t2;
        }
    }
}
