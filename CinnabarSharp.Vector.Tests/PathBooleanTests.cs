namespace CinnabarSharp.Vector.Tests;

public class PathBooleanTests
{
    private static VectorPath Square(double x, double y, double size) => VectorPath.FromRect(x, y, size, size);

    // Signed shoelace area of the flattened path: outer outlines count positive, holes negative.
    private static double Area(VectorPath path)
    {
        var total = 0.0;
        foreach (var polyline in Flattener.Flatten(path, Matrix2D.Identity, 0.01))
        {
            var p = polyline.Points;
            for (var i = 0; i < p.Count; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % p.Count];
                total += a.X * b.Y - b.X * a.Y;
            }
        }
        return Math.Abs(total / 2);
    }

    private static VectorPath Op(VectorPath a, VectorPath b, BooleanOperation op) =>
        PathBoolean.Combine(a, FillRule.NonZero, b, FillRule.NonZero, op);

    private static HashSet<VPoint> Corners(VectorPath path) =>
        path.Segments.Where(s => s.Kind is SegmentKind.MoveTo or SegmentKind.LineTo).Select(s => s.End).ToHashSet();

    [Fact]
    public void Two_overlapping_squares()
    {
        var a = Square(0, 0, 10);
        var b = Square(5, 5, 10);
        Assert.Equal(175, Area(Op(a, b, BooleanOperation.Union)), 0.01);
        Assert.Equal(25, Area(Op(a, b, BooleanOperation.Intersection)), 0.01);
        Assert.Equal(75, Area(Op(a, b, BooleanOperation.Difference)), 0.01);
        Assert.Equal(150, Area(Op(a, b, BooleanOperation.Exclusion)), 0.01);
    }

    [Fact]
    public void Union_of_two_squares_is_one_outline_of_eight_corners()
    {
        var union = Op(Square(0, 0, 10), Square(5, 5, 10), BooleanOperation.Union);
        Assert.Single(PathBoolean.BreakApart(union));
        Assert.Equal(
            new HashSet<VPoint> { new(0, 0), new(10, 0), new(10, 5), new(15, 5), new(15, 15), new(5, 15), new(5, 10), new(0, 10) },
            Corners(union));
    }

    [Fact]
    public void Identical_shapes_union_to_themselves_and_difference_is_empty()
    {
        var a = Square(0, 0, 10);
        var union = Op(a, a, BooleanOperation.Union);
        Assert.Equal(100, Area(union), 0.01);
        Assert.Equal(4, Corners(union).Count);
        Assert.True(Op(a, a, BooleanOperation.Difference).IsEmpty);
        Assert.True(Op(a, a, BooleanOperation.Exclusion).IsEmpty);
    }

    [Fact]
    public void Squares_sharing_an_edge_merge_into_a_rectangle()
    {
        var union = Op(Square(0, 0, 10), Square(10, 0, 10), BooleanOperation.Union);
        Assert.Equal(200, Area(union), 0.01);
        Assert.Equal(new HashSet<VPoint> { new(0, 0), new(20, 0), new(20, 10), new(0, 10) }, Corners(union));
        Assert.True(Op(Square(0, 0, 10), Square(10, 0, 10), BooleanOperation.Intersection).IsEmpty);
    }

    [Fact]
    public void Disjoint_shapes_stay_apart()
    {
        var union = Op(Square(0, 0, 10), Square(30, 30, 10), BooleanOperation.Union);
        Assert.Equal(2, PathBoolean.BreakApart(union).Count);
        Assert.Equal(200, Area(union), 0.01);
        Assert.True(Op(Square(0, 0, 10), Square(30, 30, 10), BooleanOperation.Intersection).IsEmpty);
    }

    [Fact]
    public void A_circle_minus_a_square_keeps_its_curves()
    {
        var circle = VectorPath.FromEllipse(0, 0, 10, 10);
        var result = Op(circle, Square(0, -20, 40), BooleanOperation.Difference);   // the left half
        Assert.InRange(Area(result), Math.PI * 100 / 2 - 2, Math.PI * 100 / 2);   // the flattening is 0.05 inside the circle
        Assert.Contains(result.Segments, s => s.Kind == SegmentKind.CubicTo);
        Assert.True(result.Segments.Count < 14, "the arc is fitted back, not a polyline");
    }

    [Fact]
    public void Holes_are_respected()
    {
        var frame = new VectorPath().Append(Square(0, 0, 20)).Append(PathOperations.Reverse(Square(5, 5, 10)));
        Assert.Equal(300, Area(Op(frame, new VectorPath(), BooleanOperation.Union)), 0.01);
        Assert.Equal(225, Area(Op(frame, Square(10, 10, 15), BooleanOperation.Difference)), 0.01);
        Assert.Equal(75, Area(Op(frame, Square(10, 10, 15), BooleanOperation.Intersection)), 0.01);
        Assert.Equal(2, PathBoolean.BreakApart(Op(frame, new VectorPath(), BooleanOperation.Union)).Count);
    }

    [Fact]
    public void A_self_intersecting_star_follows_its_fill_rule()
    {
        var points = Enumerable.Range(0, 5)
            .Select(i => new VPoint(50 + 40 * Math.Sin(i * 4 * Math.PI / 5), 50 - 40 * Math.Cos(i * 4 * Math.PI / 5))).ToList();
        var star = VectorPath.FromPolyline(points, true);
        var far = Square(200, 200, 1);
        var nonZero = PathBoolean.Combine(star, FillRule.NonZero, far, FillRule.NonZero, BooleanOperation.Union);
        var evenOdd = PathBoolean.Combine(star, FillRule.EvenOdd, far, FillRule.NonZero, BooleanOperation.Union);
        // The two rules differ by the inner pentagon (5/2 r² sin 72°, r = 40 cos 72° / cos 36°).
        var r = 40 * Math.Cos(2 * Math.PI / 5) / Math.Cos(Math.PI / 5);
        var inner = 2.5 * r * r * Math.Sin(2 * Math.PI / 5);
        Assert.Equal(inner, Area(nonZero) - Area(evenOdd), 0.5);
    }

    [Fact]
    public void Many_operands_union_difference_and_exclusion()
    {
        var operands = new[]
        {
            (Square(0, 0, 10), FillRule.NonZero), (Square(5, 0, 10), FillRule.NonZero), (Square(10, 0, 10), FillRule.NonZero),
        };
        Assert.Equal(200, Area(PathBoolean.Combine(operands, BooleanOperation.Union)), 0.01);
        Assert.Equal(0, Area(PathBoolean.Combine(operands, BooleanOperation.Intersection)), 0.01);
        Assert.Equal(50, Area(PathBoolean.Combine(operands, BooleanOperation.Difference)), 0.01);
        Assert.Equal(100, Area(PathBoolean.Combine(operands, BooleanOperation.Exclusion)), 0.01);
    }

    [Fact]
    public void Division_cuts_the_first_shape_along_the_second()
    {
        var pieces = PathBoolean.Divide(Square(0, 0, 10), FillRule.NonZero, Square(5, 5, 10), FillRule.NonZero);
        Assert.Equal(2, pieces.Count);
        Assert.Equal([25.0, 75.0], pieces.Select(p => Math.Round(Area(p), 2)).Order());
    }

    [Fact]
    public void Touching_corners_and_crossing_edges_do_not_break_the_outline()
    {
        var square = Square(0, 0, 10);
        var crossing = VectorPath.FromPolyline([new(-5, 4), new(15, 4), new(15, 6), new(-5, 6)], true);
        var cut = Op(square, crossing, BooleanOperation.Difference);
        Assert.Equal(2, PathBoolean.BreakApart(cut).Count);
        Assert.Equal(80, Area(cut), 0.01);
        // A diamond whose corners touch the middle of the square's sides.
        var diamond = VectorPath.FromPolyline([new(5, 0), new(10, 5), new(5, 10), new(0, 5)], true);
        Assert.Equal(100, Area(Op(diamond, square, BooleanOperation.Union)), 0.01);
        Assert.Equal(50, Area(Op(diamond, square, BooleanOperation.Intersection)), 0.01);
        Assert.Equal(50, Area(Op(square, diamond, BooleanOperation.Difference)), 0.01);
        Assert.Equal(4, PathBoolean.BreakApart(Op(square, diamond, BooleanOperation.Difference)).Count);
    }

    [Fact]
    public void The_result_is_deterministic()
    {
        var a = VectorPath.FromEllipse(50, 50, 40, 30);
        var b = VectorPath.FromRect(20, 30, 70, 50, 10, 10);
        var first = PathDataWriter.Write(Op(a, b, BooleanOperation.Exclusion));
        for (var i = 0; i < 3; i++)
            Assert.Equal(first, PathDataWriter.Write(Op(a, b, BooleanOperation.Exclusion)));
        Assert.True(Area(Op(a, b, BooleanOperation.Exclusion)) > 0);
    }

    [Fact]
    public void Reverse_flips_the_direction_but_not_the_shape()
    {
        var path = new VectorPath().MoveTo(0, 0).LineTo(10, 0).CubicTo(new(14, 0), new(14, 10), new(10, 10)).LineTo(0, 10).Close();
        var reversed = PathOperations.Reverse(path);
        Assert.Equal(Area(path), Area(reversed), 0.01);
        Assert.Equal(PathDataWriter.Write(path), PathDataWriter.Write(PathOperations.Reverse(reversed)));
        Assert.NotEqual(PathDataWriter.Write(path), PathDataWriter.Write(reversed));
    }

    [Fact]
    public void Simplify_reduces_nodes_within_tolerance()
    {
        var points = Enumerable.Range(0, 101).Select(i => new VPoint(i, Math.Sin(i / 15.0) * 10)).ToList();
        var path = VectorPath.FromPolyline(points, false);
        var simple = PathOperations.Simplify(path, 0.3);
        Assert.True(simple.Segments.Count < path.Segments.Count / 3);
        Assert.Equal(points[^1].X, simple.Segments[^1].End.X, 0.001);
    }
}
