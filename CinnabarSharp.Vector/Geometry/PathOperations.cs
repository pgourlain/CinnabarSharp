namespace CinnabarSharp.Vector;

/// <summary>Small operations on whole paths: reversing and simplifying.</summary>
public static class PathOperations
{
    /// <summary>The same shape drawn in the opposite direction (each sub-path is reversed in place).</summary>
    public static VectorPath Reverse(VectorPath path)
    {
        var result = new VectorPath();
        foreach (var figure in path.Figures)
        {
            // The points the figure passes through, then the segments walked backwards.
            var points = new List<VPoint> { figure.Start };
            points.AddRange(figure.Segments.Select(s => s.End));
            var closedBack = figure.IsClosed && figure.Segments.Count > 0 && figure.Segments[^1].End == figure.Start;
            if (figure.Segments.Count == 0)
            {
                result.MoveTo(figure.Start);
                if (figure.IsClosed)
                    result.Close();
                continue;
            }
            result.MoveTo(points[^1]);
            for (var i = figure.Segments.Count - 1; i >= 0; i--)
            {
                var s = figure.Segments[i];
                var to = points[i];
                switch (s.Kind)
                {
                    case SegmentKind.LineTo:
                        if (!(figure.IsClosed && i == figure.Segments.Count - 1 && closedBack && false))
                            result.LineTo(to);
                        break;
                    case SegmentKind.CubicTo:
                        result.CubicTo(s.C2, s.C1, to);
                        break;
                    case SegmentKind.QuadTo:
                        result.QuadTo(s.C1, to);
                        break;
                    case SegmentKind.ArcTo:
                        result.Add(s with { End = to, Sweep = !s.Sweep });
                        break;
                }
            }
            if (figure.IsClosed)
                result.Close();
        }
        return result;
    }

    /// <summary>Fewer nodes: each open or closed sub-path is flattened and fitted again within <paramref name="tolerance"/>.</summary>
    public static VectorPath Simplify(VectorPath path, double tolerance)
    {
        var result = new VectorPath();
        foreach (var polyline in Flattener.Flatten(path, Matrix2D.Identity, Math.Min(tolerance / 4, 0.05)))
        {
            var points = polyline.Points.ToList();
            if (polyline.Closed && points.Count > 0)
                points.Add(points[0]);
            var fitted = CurveFitter.Fit(CurveFitter.Simplify(points, tolerance / 2), tolerance);
            result.Append(fitted);
            if (polyline.Closed)
                result.Close();
        }
        return result;
    }
}
