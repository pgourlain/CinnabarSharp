namespace CinnabarSharp.Vector.Tests;

public class PathCutterTests
{
    private static VectorPath Line(double x0, double y0, double x1, double y1) => new VectorPath().MoveTo(x0, y0).LineTo(x1, y1);

    private static VPoint End(VectorPath path) => path.Segments[^1].End;

    [Fact]
    public void A_line_crossed_twice_has_three_pieces_and_the_middle_one_leaves_two_parts()
    {
        var line = Line(0, 50, 100, 50);
        var cutters = new[] { Line(30, 0, 30, 100), Line(70, 0, 70, 100) };
        var figure = Assert.Single(PathCutter.Cut(line, cutters));
        Assert.Equal(3, figure.Pieces.Count);
        Assert.Equal(new VPoint(30, 50).X, End(figure.Pieces[0]).X, 1e-6);
        Assert.Equal(70, End(figure.Pieces[1]).X, 1e-6);
        var rest = figure.Without(1);
        Assert.Equal(2, rest.Count);
        Assert.Equal(30, End(rest[0]).X, 1e-6);
        Assert.Equal(70, rest[1].Segments[0].End.X, 1e-6);
        Assert.Equal(100, End(rest[1]).X, 1e-6);
        Assert.Single(figure.Without(0));
        Assert.Single(figure.Without(2));
    }

    [Fact]
    public void A_closed_outline_gives_one_open_path_when_a_piece_is_taken_out()
    {
        var square = VectorPath.FromRect(0, 0, 100, 100);
        var bar = VectorPath.FromRect(40, -20, 20, 140);      // crosses the top and the bottom edges
        var figure = Assert.Single(PathCutter.Cut(square, [bar]));
        Assert.True(figure.IsClosed);
        Assert.Equal(4, figure.Pieces.Count);
        var rest = Assert.Single(figure.Without(0));
        // Taking one piece out leaves one open outline that starts and ends at the cut.
        Assert.Equal(SegmentKind.MoveTo, rest.Segments[0].Kind);
        Assert.DoesNotContain(rest.Segments, s => s.Kind == SegmentKind.Close);
        var start = rest.Segments[0].End;
        var removed = figure.Pieces[0];
        Assert.Equal(End(removed).X, start.X, 1e-6);
        Assert.Equal(End(removed).Y, start.Y, 1e-6);
        Assert.Equal(removed.Segments[0].End.X, End(rest).X, 1e-6);
        Assert.Equal(removed.Segments[0].End.Y, End(rest).Y, 1e-6);
    }

    [Fact]
    public void Curves_are_split_not_flattened()
    {
        var arc = new VectorPath().MoveTo(0, 0).CubicTo(new VPoint(0, 60), new VPoint(100, 60), new VPoint(100, 0));
        var figure = Assert.Single(PathCutter.Cut(arc, [Line(50, -10, 50, 100)]));
        Assert.Equal(2, figure.Pieces.Count);
        Assert.All(figure.Pieces, p => Assert.Equal(SegmentKind.CubicTo, p.Segments[1].Kind));
        // The cubic through the middle of a symmetric arch is at x = 50, y = 45.
        Assert.Equal(50, End(figure.Pieces[0]).X, 0.1);
        Assert.Equal(45, End(figure.Pieces[0]).Y, 0.1);
        // The two halves have the same shape as the curve: the first half ends where the second starts.
        Assert.Equal(End(figure.Pieces[0]).X, figure.Pieces[1].Segments[0].End.X, 1e-9);
        Assert.Equal(End(figure.Pieces[0]).Y, figure.Pieces[1].Segments[0].End.Y, 1e-9);
    }

    [Fact]
    public void Nothing_to_take_out_when_there_is_no_crossing_or_one_touch_of_a_closed_outline()
    {
        var square = VectorPath.FromRect(0, 0, 100, 100);
        Assert.Empty(Assert.Single(PathCutter.Cut(square, [VectorPath.FromRect(200, 200, 10, 10)])).Pieces);
        // A line that only touches the outline at its corner.
        Assert.Empty(Assert.Single(PathCutter.Cut(square, [Line(100, 100, 150, 150)])).Pieces);
        // The end of an open path on the cutter is not a crossing.
        Assert.Empty(Assert.Single(PathCutter.Cut(Line(0, 0, 50, 0), [Line(50, -10, 50, 10)])).Pieces);
    }

    [Fact]
    public void A_crossing_at_a_node_is_found_once()
    {
        var polyline = VectorPath.FromPolyline([new VPoint(0, 0), new VPoint(50, 0), new VPoint(100, 50)], false);
        var figure = Assert.Single(PathCutter.Cut(polyline, [Line(50, -20, 50, 20)]));
        Assert.Equal(2, figure.Pieces.Count);
    }

    [Fact]
    public void Arcs_of_a_circle_are_cut_by_a_crossing_circle()
    {
        var circle = VectorPath.FromEllipse(50, 50, 30, 30);
        var other = VectorPath.FromEllipse(90, 50, 30, 30);
        var figure = Assert.Single(PathCutter.Cut(circle, [other]));
        Assert.Equal(2, figure.Pieces.Count);
        // The pieces lie on the circle.
        foreach (var piece in figure.Pieces)
            foreach (var segment in piece.Segments)
                Assert.Equal(30, segment.End.DistanceTo(new VPoint(50, 50)), 0.05);
    }

    [Fact]
    public void Each_sub_path_is_cut_on_its_own()
    {
        var path = new VectorPath().MoveTo(0, 10).LineTo(100, 10).MoveTo(0, 80).LineTo(100, 80);
        var figures = PathCutter.Cut(path, [Line(50, 0, 50, 40)]);
        Assert.Equal(2, figures.Count);
        Assert.Equal(2, figures[0].Pieces.Count);
        Assert.Empty(figures[1].Pieces);
    }

    [Fact]
    public void An_open_outline_that_ends_a_hair_short_of_the_path_still_cuts_it()
    {
        var circle = VectorPath.FromEllipse(100, 100, 50, 50);
        // Two arcs of another shape that end 0.04 units off the circle, one short of it and one beyond it.
        var both = Assert.Single(PathCutter.Cut(circle, [Line(100, 0, 100, 50.04), Line(100, 150, 100, 149.96)]));
        Assert.Equal(2, both.Pieces.Count);
    }

    [Fact]
    public void An_end_touching_where_a_real_crossing_is_does_not_make_a_second_cut()
    {
        var circle = VectorPath.FromEllipse(100, 100, 50, 50);
        var figure = Assert.Single(PathCutter.Cut(circle, [Line(100, 0, 100, 60), Line(100, 140, 100, 200)]));
        Assert.Equal(2, figure.Pieces.Count);
    }
}
