namespace CinnabarSharp.Vector.Tests;

public class CurveFitterTests
{
    private static List<VPoint> Circle(int count, double radius, double sweepDegrees = 360)
    {
        var points = new List<VPoint>();
        for (var i = 0; i < count; i++)
        {
            var a = sweepDegrees * Math.PI / 180 * i / (count - 1);
            points.Add(new VPoint(100 + radius * Math.Cos(a), 100 + radius * Math.Sin(a)));
        }
        return points;
    }

    private static double MaxDeviation(VectorPath path, IReadOnlyList<VPoint> original)
    {
        var polyline = Flattener.Flatten(path, Matrix2D.Identity, 0.05).SelectMany(p => p.Points).ToList();
        var worst = 0.0;
        foreach (var p in original)
        {
            var best = double.MaxValue;
            for (var i = 0; i + 1 < polyline.Count; i++)
            {
                var ab = polyline[i + 1] - polyline[i];
                var len = ab.Dot(ab);
                var t = len == 0 ? 0 : Math.Clamp(ab.Dot(p - polyline[i]) / len, 0, 1);
                best = Math.Min(best, p.DistanceTo(polyline[i] + ab * t));
            }
            worst = Math.Max(worst, best);
        }
        return worst;
    }

    [Fact]
    public void A_dense_circle_arc_becomes_a_few_curves_within_the_error()
    {
        var points = Circle(200, 50, 270);
        var path = CurveFitter.Fit(points, 1);
        var segments = path.Segments.Count(s => s.Kind == SegmentKind.CubicTo);
        Assert.InRange(segments, 2, 8);
        Assert.True(MaxDeviation(path, points) < 1.2, $"deviation {MaxDeviation(path, points)}");
        Assert.Equal(points[0], path.Segments[0].End);
        Assert.Equal(points[^1], path.Segments[^1].End);
    }

    [Fact]
    public void A_smaller_error_gives_more_curves()
    {
        var points = Circle(300, 80, 340).Select((p, i) => new VPoint(p.X + Math.Sin(i * 1.7) * 0.3, p.Y)).ToList();
        var coarse = CurveFitter.Fit(points, 4);
        var fine = CurveFitter.Fit(points, 0.4);
        Assert.True(fine.Segments.Count >= coarse.Segments.Count);
        Assert.True(MaxDeviation(fine, points) < 0.6);
    }

    [Fact]
    public void Straight_input_is_one_curve_or_a_line()
    {
        var line = Enumerable.Range(0, 20).Select(i => new VPoint(i * 5, i * 2)).ToList();
        var path = CurveFitter.Fit(line, 1);
        Assert.True(path.Segments.Count <= 2);
        Assert.True(MaxDeviation(path, line) < 0.5);
    }

    [Fact]
    public void Degenerate_inputs()
    {
        Assert.True(CurveFitter.Fit([], 1).IsEmpty);
        Assert.Single(CurveFitter.Fit([new VPoint(1, 1)], 1).Segments);
        Assert.Equal(2, CurveFitter.Fit([new VPoint(1, 1), new VPoint(1, 1), new VPoint(5, 5)], 1).Segments.Count);
        Assert.Equal(SegmentKind.LineTo, CurveFitter.Fit([new VPoint(0, 0), new VPoint(3, 4)], 1).Segments[1].Kind);
    }

    [Fact]
    public void Corners_are_kept_by_splitting()
    {
        var points = new List<VPoint>();
        for (var i = 0; i <= 20; i++) points.Add(new VPoint(i * 5, 0));
        for (var i = 1; i <= 20; i++) points.Add(new VPoint(100, i * 5));
        var path = CurveFitter.Fit(points, 1);
        Assert.True(MaxDeviation(path, points) < 1.5);
        Assert.Contains(path.Segments, s => s.End.DistanceTo(new VPoint(100, 0)) < 8);
    }

    [Fact]
    public void Simplify_drops_points_on_straight_runs()
    {
        var points = Enumerable.Range(0, 50).Select(i => new VPoint(i, 0)).Concat(Enumerable.Range(1, 50).Select(i => new VPoint(49, i))).ToList();
        var simple = CurveFitter.Simplify(points, 0.5);
        Assert.Equal(3, simple.Count);
        Assert.Equal(points[0], simple[0]);
        Assert.Equal(points[^1], simple[^1]);
        Assert.Equal(2, CurveFitter.Simplify([new VPoint(0, 0), new VPoint(1, 1)], 1).Count);
    }
}
