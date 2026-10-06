namespace CinnabarSharp.Vector.Tests;

public class FlattenerTests
{
    [Fact]
    public void Lines_stay_lines()
    {
        var path = new VectorPath().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Close();
        var polylines = Flattener.Flatten(path, Matrix2D.Identity, 0.1);
        var p = Assert.Single(polylines);
        Assert.True(p.Closed);
        Assert.Equal(3, p.Points.Count);
    }

    [Fact]
    public void Cubics_stay_within_the_tolerance()
    {
        var p0 = new VPoint(0, 0);
        var p1 = new VPoint(0, 100);
        var p2 = new VPoint(100, 100);
        var p3 = new VPoint(100, 0);
        var path = new VectorPath().MoveTo(p0).CubicTo(p1, p2, p3);
        foreach (var tolerance in new[] { 1.0, 0.1, 0.01 })
        {
            var points = Flattener.Flatten(path, Matrix2D.Identity, tolerance).Single().Points;
            var worst = 0.0;
            for (var i = 0; i <= 400; i++)
            {
                var t = i / 400.0;
                var mt = 1 - t;
                var x = mt * mt * mt * p0.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * p3.X;
                var y = mt * mt * mt * p0.Y + 3 * mt * mt * t * p1.Y + 3 * mt * t * t * p2.Y + t * t * t * p3.Y;
                worst = Math.Max(worst, DistanceToPolyline(points, new VPoint(x, y)));
            }
            Assert.True(worst <= tolerance, $"tolerance {tolerance}: deviation {worst}");
        }
    }

    private static double DistanceToPolyline(List<VPoint> points, VPoint p)
    {
        var best = double.MaxValue;
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var ab = b - a;
            var t = ab.Dot(p - a) / Math.Max(ab.Dot(ab), 1e-12);
            t = Math.Clamp(t, 0, 1);
            best = Math.Min(best, p.DistanceTo(a + ab * t));
        }
        return best;
    }

    [Fact]
    public void Arcs_flatten_through_cubics_and_the_circle_is_round()
    {
        var points = Flattener.Flatten(VectorPath.FromEllipse(50, 50, 40, 40), Matrix2D.Identity, 0.05).Single().Points;
        foreach (var p in points)
            Assert.InRange(p.DistanceTo(new VPoint(50, 50)), 40 - 0.1, 40 + 0.1);
    }

    [Fact]
    public void The_matrix_is_applied_before_flattening()
    {
        var path = new VectorPath().MoveTo(1, 1).LineTo(2, 1);
        var points = Flattener.Flatten(path, Matrix2D.Scale(10), 0.1).Single().Points;
        Assert.Equal(new VPoint(10, 10), points[0]);
        Assert.Equal(new VPoint(20, 10), points[1]);
    }

    [Fact]
    public void Tiny_tolerances_do_not_explode()
    {
        var path = new VectorPath().MoveTo(0, 0).CubicTo(new VPoint(0, 1e6), new VPoint(1e6, 1e6), new VPoint(1e6, 0));
        Assert.InRange(Flattener.Flatten(path, Matrix2D.Identity, 1e-9).Single().Points.Count, 2, 1002);
    }
}

public class PathCoverageTests
{
    private static List<Polyline> Poly(params (double X, double Y)[] points) =>
        [new Polyline(points.Select(p => new VPoint(p.X, p.Y)).ToList(), true)];

    private static readonly VRectI Clip = new(0, 0, 20, 20);

    [Fact]
    public void An_aligned_square_is_exact()
    {
        var mask = PathCoverage.Fill(Poly((2, 3), (7, 3), (7, 8), (2, 8)), FillRule.NonZero, Clip);
        Assert.Equal(new VRectI(2, 3, 5, 5), mask.Area);
        Assert.All(mask.Data, v => Assert.Equal(255, v));
        Assert.Equal(0, mask.At(1, 3));
        Assert.Equal(255, mask.At(2, 3));
        Assert.Equal(0, mask.At(7, 3));
    }

    [Fact]
    public void Half_covered_pixels_get_half_coverage()
    {
        var mask = PathCoverage.Fill(Poly((2.5, 2), (6, 2), (6, 6), (2.5, 6)), FillRule.NonZero, Clip);
        Assert.InRange((int)mask.At(2, 3), 127, 128);
        Assert.Equal(255, mask.At(3, 3));
        var vertical = PathCoverage.Fill(Poly((2, 2.25), (6, 2.25), (6, 6), (2, 6)), FillRule.NonZero, Clip);
        Assert.InRange((int)vertical.At(3, 2), 190, 193); // 0.75 of 255, in steps of 1/16
    }

    [Fact]
    public void Area_of_a_circle_is_accurate()
    {
        var polylines = Flattener.Flatten(VectorPath.FromEllipse(10, 10, 8, 8), Matrix2D.Identity, 0.05);
        var mask = PathCoverage.Fill(polylines, FillRule.NonZero, Clip);
        var area = mask.Data.Sum(v => v) / 255.0;
        Assert.InRange(area, Math.PI * 64 * 0.99, Math.PI * 64 * 1.005);
    }

    [Fact]
    public void Fill_rules_differ_for_nested_same_direction_squares()
    {
        var both = new List<Polyline>
        {
            new([new VPoint(2, 2), new VPoint(18, 2), new VPoint(18, 18), new VPoint(2, 18)], true),
            new([new VPoint(6, 6), new VPoint(14, 6), new VPoint(14, 14), new VPoint(6, 14)], true),
        };
        Assert.Equal(255, PathCoverage.Fill(both, FillRule.NonZero, Clip).At(10, 10));
        Assert.Equal(0, PathCoverage.Fill(both, FillRule.EvenOdd, Clip).At(10, 10));
        Assert.Equal(255, PathCoverage.Fill(both, FillRule.EvenOdd, Clip).At(4, 10));
    }

    [Fact]
    public void Opposite_direction_hole_is_empty_for_nonzero()
    {
        var shape = new List<Polyline>
        {
            new([new VPoint(2, 2), new VPoint(18, 2), new VPoint(18, 18), new VPoint(2, 18)], true),
            new([new VPoint(6, 6), new VPoint(6, 14), new VPoint(14, 14), new VPoint(14, 6)], true),
        };
        Assert.Equal(0, PathCoverage.Fill(shape, FillRule.NonZero, Clip).At(10, 10));
        Assert.Equal(255, PathCoverage.Fill(shape, FillRule.NonZero, Clip).At(4, 4));
    }

    [Fact]
    public void Shapes_are_clipped_to_the_region()
    {
        var mask = PathCoverage.Fill(Poly((-10, -10), (100, -10), (100, 100), (-10, 100)), FillRule.NonZero, new VRectI(5, 6, 4, 3));
        Assert.Equal(new VRectI(5, 6, 4, 3), mask.Area);
        Assert.All(mask.Data, v => Assert.Equal(255, v));
    }

    [Fact]
    public void Shapes_outside_the_region_give_an_empty_mask()
    {
        Assert.True(PathCoverage.Fill(Poly((30, 30), (40, 30), (40, 40)), FillRule.NonZero, Clip).IsEmpty);
        Assert.True(PathCoverage.Fill([], FillRule.NonZero, Clip).IsEmpty);
        Assert.True(PathCoverage.Fill(Poly((1, 1), (5, 1), (5, 5)), FillRule.NonZero, VRectI.Empty).IsEmpty);
    }

    [Fact]
    public void Non_finite_points_are_ignored()
    {
        var mask = PathCoverage.Fill(Poly((double.NaN, 0), (5, 1), (5, 5), (1, 5)), FillRule.NonZero, Clip);
        Assert.NotNull(mask);
    }

    [Fact]
    public void A_triangle_has_the_right_area()
    {
        var mask = PathCoverage.Fill(Poly((0, 0), (16, 0), (0, 16)), FillRule.NonZero, Clip);
        Assert.InRange(mask.Data.Sum(v => v) / 255.0, 127, 129);
    }
}

public class StrokerTests
{
    private static double StrokeArea(VectorPath path, StrokeStyle style, double tolerance = 0.02)
    {
        var polygons = Stroker.Stroke(Flattener.Flatten(path, Matrix2D.Identity, tolerance), style, tolerance);
        return PathCoverage.Fill(polygons, FillRule.NonZero, new VRectI(-50, -50, 200, 200)).Data.Sum(v => v) / 255.0;
    }

    private static VectorPath Line(double x0, double y0, double x1, double y1) => new VectorPath().MoveTo(x0, y0).LineTo(x1, y1);

    [Fact]
    public void A_butt_line_covers_length_times_width()
    {
        Assert.InRange(StrokeArea(Line(10, 10, 30, 10), new StrokeStyle(4)), 79.5, 80.5);
        Assert.InRange(StrokeArea(Line(10, 10, 30, 30), new StrokeStyle(4)), 20 * Math.Sqrt(2) * 4 - 1, 20 * Math.Sqrt(2) * 4 + 1);
    }

    [Fact]
    public void Square_caps_add_half_a_width_at_each_end()
    {
        Assert.InRange(StrokeArea(Line(10, 10, 30, 10), new StrokeStyle(4, LineCap.Square)), 95.5, 96.5); // (20 + 4) x 4
    }

    [Fact]
    public void Round_caps_add_a_disc()
    {
        var area = StrokeArea(Line(10, 10, 30, 10), new StrokeStyle(4, LineCap.Round));
        Assert.InRange(area, 80 + Math.PI * 4 - 0.5, 80 + Math.PI * 4 + 0.5);
    }

    [Fact]
    public void Zero_length_subpaths_follow_the_cap()
    {
        Assert.Equal(0, StrokeArea(Line(10, 10, 10, 10), new StrokeStyle(6, LineCap.Butt)), 6);
        Assert.InRange(StrokeArea(Line(10, 10, 10, 10), new StrokeStyle(6, LineCap.Round)), Math.PI * 9 - 0.3, Math.PI * 9 + 0.3);
        Assert.InRange(StrokeArea(Line(10, 10, 10, 10), new StrokeStyle(6, LineCap.Square)), 35.5, 36.5);
    }

    [Theory]
    [InlineData(LineJoin.Miter, 100 + 8 + 1.5)]   // 10 wide corner: miter fills the outer corner
    [InlineData(LineJoin.Bevel, 100 + 8 - 1.5)]
    public void Joins_of_a_right_angle(LineJoin join, double approx)
    {
        var path = new VectorPath().MoveTo(10, 10).LineTo(30, 10).LineTo(30, 30);
        var area = StrokeArea(path, new StrokeStyle(4, LineCap.Butt, join));
        // Two 20x4 bodies overlap in a 2x2 inner square; the join adds the outer corner (4 for a miter, 2 for a bevel).
        var expected = join == LineJoin.Miter ? 80 + 80 - 4 + 4 : 80 + 80 - 4 + 2;
        Assert.InRange(area, expected - 0.6, expected + 0.6);
        Assert.True(approx > 0);
    }

    [Fact]
    public void Round_join_adds_a_quarter_disc()
    {
        var path = new VectorPath().MoveTo(10, 10).LineTo(30, 10).LineTo(30, 30);
        var area = StrokeArea(path, new StrokeStyle(4, LineCap.Butt, LineJoin.Round));
        Assert.InRange(area, 80 + 80 - 4 + Math.PI - 0.5, 80 + 80 - 4 + Math.PI + 0.5);
    }

    [Fact]
    public void Miter_limit_turns_sharp_corners_into_bevels()
    {
        var sharp = new VectorPath().MoveTo(10, 10).LineTo(40, 14).LineTo(10, 18);
        var withMiter = StrokeArea(sharp, new StrokeStyle(2, LineCap.Butt, LineJoin.Miter, 100));
        var limited = StrokeArea(sharp, new StrokeStyle(2, LineCap.Butt, LineJoin.Miter, 2));
        Assert.True(withMiter > limited + 3, $"{withMiter} vs {limited}");
        var bevel = StrokeArea(sharp, new StrokeStyle(2, LineCap.Butt, LineJoin.Bevel));
        Assert.Equal(bevel, limited, 0);
    }

    [Fact]
    public void Closed_paths_have_joins_and_no_caps()
    {
        var square = new VectorPath().MoveTo(10, 10).LineTo(30, 10).LineTo(30, 30).LineTo(10, 30).Close();
        var area = StrokeArea(square, new StrokeStyle(4, LineCap.Square, LineJoin.Miter));
        // Outer 24 x 24 minus inner 16 x 16 (mitered corners; square caps would add nothing for a closed figure).
        Assert.InRange(area, 24 * 24 - 16 * 16 - 0.8, 24 * 24 - 16 * 16 + 0.8);
    }

    [Fact]
    public void Curves_stroke_with_the_expected_area()
    {
        var circle = VectorPath.FromEllipse(50, 50, 20, 20);
        var area = StrokeArea(circle, new StrokeStyle(4, LineCap.Butt, LineJoin.Round));
        Assert.InRange(area, Math.PI * (22 * 22 - 18 * 18) * 0.99, Math.PI * (22 * 22 - 18 * 18) * 1.01);
    }

    [Fact]
    public void Dashes_cut_the_line_with_offset()
    {
        var path = Line(0, 10, 40, 10);
        // 10 on, 10 off over 40: two dashes.
        Assert.InRange(StrokeArea(path, new StrokeStyle(2, Dashes: [10, 10])), 39.5, 40.5);
        // An offset of 5 starts in the middle of a dash: 5 on, 10 off, 10 on, 10 off, 5 on = 20 long.
        Assert.InRange(StrokeArea(path, new StrokeStyle(2, Dashes: [10, 10], DashOffset: 5)), 39.5, 40.5);
        // Offset 15: starts in a gap.
        Assert.InRange(StrokeArea(path, new StrokeStyle(2, Dashes: [10, 10], DashOffset: 15)), 39.5, 40.5);
    }

    [Fact]
    public void Dash_segments_are_where_the_pattern_says()
    {
        var dashes = Stroker.Dash(new Polyline([new VPoint(0, 0), new VPoint(35, 0)], false), [10, 5], 0);
        Assert.Equal(3, dashes.Count);
        Assert.Equal([0.0, 10.0], dashes[0].Points.Select(p => p.X));
        Assert.Equal([15.0, 25.0], dashes[1].Points.Select(p => p.X));
        Assert.Equal([30.0, 35.0], dashes[2].Points.Select(p => p.X));
    }

    [Fact]
    public void Odd_patterns_repeat()
    {
        var dashes = Stroker.Dash(new Polyline([new VPoint(0, 0), new VPoint(30, 0)], false), [5], 0);
        // 5 on, 5 off, 5 on, 5 off, ...
        Assert.Equal(3, dashes.Count);
    }

    [Fact]
    public void Zero_length_dashes_make_dots_with_round_caps()
    {
        var path = Line(0, 10, 20, 10);
        var area = StrokeArea(path, new StrokeStyle(4, LineCap.Round, Dashes: [0, 5]));
        Assert.InRange(area, 4 * Math.PI * 3.5, 5 * Math.PI * 4.5); // four or five dots of radius 2
    }

    [Fact]
    public void A_closed_path_dash_runs_across_the_start()
    {
        var square = new Polyline([new VPoint(0, 0), new VPoint(10, 0), new VPoint(10, 10), new VPoint(0, 10)], true);
        // Perimeter 40, pattern 30 on / 5 off: the dash 35..40 runs on into the dash 0..30, so there is one dash.
        var dashes = Stroker.Dash(square, [30, 5], 0);
        var dash = Assert.Single(dashes);
        Assert.Equal(new VPoint(0, 5), dash.Points[0]);
        Assert.Equal(new VPoint(0, 0), dash.Points[1]);
    }

    [Fact]
    public void Hairlines_are_drawn_at_their_true_thin_width()
    {
        Assert.InRange(StrokeArea(Line(10, 10.5, 30, 10.5), new StrokeStyle(0.25)), 4.5, 5.5);
    }

    [Fact]
    public void Degenerate_widths_draw_nothing()
    {
        Assert.Empty(Stroker.Stroke(Flattener.Flatten(Line(0, 0, 10, 0), Matrix2D.Identity, 0.1), new StrokeStyle(0), 0.1));
        Assert.Empty(Stroker.Stroke(Flattener.Flatten(Line(0, 0, 10, 0), Matrix2D.Identity, 0.1), new StrokeStyle(double.NaN), 0.1));
    }
}
