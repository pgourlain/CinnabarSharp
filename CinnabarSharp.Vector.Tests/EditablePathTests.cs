namespace CinnabarSharp.Vector.Tests;

public class EditablePathTests
{
    private static string Round(VectorPath p) => PathDataWriter.Write(p, 3);

    [Theory]
    [InlineData("M0 0L10 0 10 10 0 10Z")]
    [InlineData("M0 0C10 0 10 10 20 10C30 10 30 0 40 0")]
    [InlineData("M0 0C0 10 10 10 10 0C10-10 0-10 0 0Z")]
    public void Reading_then_writing_keeps_the_path(string data)
    {
        var path = PathDataParser.Parse(data);
        Assert.Equal(Round(path), Round(EditablePath.From(path).ToPath()));
    }

    [Fact]
    public void Quadratics_and_arcs_become_cubics_with_the_same_shape()
    {
        var path = PathDataParser.Parse("M0 0Q50 100 100 0A30 30 0 0 1 160 0");
        var editable = EditablePath.From(path);
        var figure = Assert.Single(editable.Figures);
        Assert.True(figure.Nodes.Count >= 4);
        var original = path.Bounds;
        var after = editable.ToPath().Bounds;
        Assert.Equal(original.X, after.X, 3);
        Assert.Equal(original.Height, after.Height, 2);
        Assert.Equal(original.Width, after.Width, 3);
        Assert.DoesNotContain(editable.ToPath().Segments, s => s.Kind is SegmentKind.QuadTo or SegmentKind.ArcTo);
    }

    [Fact]
    public void A_closing_node_on_top_of_the_first_is_merged()
    {
        var editable = EditablePath.From(PathDataParser.Parse("M0 0C0 10 10 10 10 0C10-10 0-10 0 0Z"));
        var figure = Assert.Single(editable.Figures);
        Assert.True(figure.Closed);
        Assert.Equal(2, figure.Nodes.Count);
        Assert.NotNull(figure.Nodes[0].In);          // the handle that arrived at the end belongs to the first node
    }

    [Fact]
    public void Node_types_are_found_from_the_handles()
    {
        var f = EditablePath.From(PathDataParser.Parse("M0 0C10 0 20 10 30 10C40 10 50 0 60 0C60 10 70 10 70 20")).Figures[0];
        Assert.Equal(NodeType.Corner, f.Nodes[0].Type);
        Assert.Equal(NodeType.Symmetric, f.Nodes[1].Type);    // handles (20,10) and (40,10) around (30,10): equal and collinear
        Assert.Equal(NodeType.Corner, f.Nodes[2].Type);       // handle (50,0) in, (60,10) out: a corner
    }

    [Fact]
    public void Symmetric_and_smooth_nodes_move_the_other_handle()
    {
        var node = new PathNode(new VPoint(10, 10), new VPoint(5, 10), new VPoint(15, 10), NodeType.Symmetric);
        node.SetOut(new VPoint(10, 20));
        Assert.Equal(new VPoint(10, 0), node.In);
        node.Type = NodeType.Smooth;
        node.SetOut(new VPoint(13, 14));                       // length 5 now
        var inHandle = node.In!.Value;
        Assert.Equal(10, (inHandle - node.Point).Length, 6);   // the length of the other side is kept
        Assert.Equal(0, (inHandle - node.Point).Normalized().Cross((node.Point - node.Out!.Value).Normalized()), 6);
        node.Type = NodeType.Corner;
        node.SetOut(new VPoint(30, 30));
        Assert.Equal(inHandle, node.In!.Value);
    }

    [Fact]
    public void Setting_the_type_aligns_the_handles()
    {
        var node = new PathNode(new VPoint(0, 0), new VPoint(-10, 5), new VPoint(20, 0));
        node.SetType(NodeType.Symmetric);
        Assert.Equal(-node.In!.Value.X, node.Out!.Value.X, 6);
        Assert.Equal(-node.In.Value.Y, node.Out.Value.Y, 6);
        node.SetType(NodeType.Corner);
        Assert.Equal(NodeType.Corner, node.Type);
    }

    [Fact]
    public void Inserting_a_node_keeps_the_curve()
    {
        var path = PathDataParser.Parse("M0 0C0 40 40 40 40 0");
        var editable = EditablePath.From(path);
        var figure = editable.Figures[0];
        var node = EditablePath.InsertNode(figure, 0, 0.5);
        Assert.Equal(3, figure.Nodes.Count);
        Assert.Equal(20, node.Point.X, 6);
        Assert.Equal(30, node.Point.Y, 6);                      // the curve's middle
        var before = Flattener.Flatten(path, Matrix2D.Identity, 0.01).Single().Points;
        var after = Flattener.Flatten(editable.ToPath(), Matrix2D.Identity, 0.01).Single().Points;
        // Same polyline length within a hair: nothing moved.
        Assert.Equal(Length(before), Length(after), 1);
    }

    private static double Length(List<VPoint> p) =>
        Enumerable.Range(0, p.Count - 1).Sum(i => p[i].DistanceTo(p[i + 1]));

    [Fact]
    public void Inserting_on_a_line_makes_a_corner_node_on_it()
    {
        var f = EditablePath.From(PathDataParser.Parse("M0 0L100 0")).Figures[0];
        var node = EditablePath.InsertNode(f, 0, 0.25);
        Assert.Equal(25, node.Point.X, 6);
        Assert.Null(node.In);
        Assert.Null(node.Out);
    }

    [Fact]
    public void Nearest_finds_the_segment_and_parameter()
    {
        var f = EditablePath.From(PathDataParser.Parse("M0 0L100 0L100 100")).Figures[0];
        var near = EditablePath.Nearest(f, new VPoint(100, 60))!.Value;
        Assert.Equal(1, near.Segment);
        Assert.Equal(0.6, near.T, 2);
        Assert.Equal(0, near.Distance, 3);
        Assert.Equal(7, EditablePath.Nearest(f, new VPoint(50, 7))!.Value.Distance, 3);
    }

    [Fact]
    public void Removing_a_node_keeps_the_shape_close()
    {
        var path = PathDataParser.Parse("M0 0C0 20 20 40 40 40C60 40 80 20 80 0");
        var editable = EditablePath.From(path);
        var figure = editable.Figures[0];
        Assert.Equal(3, figure.Nodes.Count);
        EditablePath.RemoveNode(figure, 1);
        Assert.Equal(2, figure.Nodes.Count);
        var after = editable.ToPath().Bounds;
        Assert.InRange(after.Height, 25, 45);                  // still an arch, not a straight line
        Assert.InRange(after.Width, 79, 81);
    }

    [Fact]
    public void Removing_ends_and_the_last_nodes()
    {
        var f = EditablePath.From(PathDataParser.Parse("M0 0L10 0L10 10")).Figures[0];
        EditablePath.RemoveNode(f, 0);
        Assert.Equal(2, f.Nodes.Count);
        EditablePath.RemoveNode(f, 1);
        EditablePath.RemoveNode(f, 0);
        Assert.Empty(f.Nodes);
    }

    [Fact]
    public void Segments_become_lines_or_curves()
    {
        var f = EditablePath.From(PathDataParser.Parse("M0 0C0 20 20 40 40 40")).Figures[0];
        EditablePath.SetSegmentKind(f, 0, line: true);
        Assert.True(f.IsLine(0));
        Assert.Equal("M0 0L40 40", Round(new EditablePath { Figures = { f } }.ToPath()));
        EditablePath.SetSegmentKind(f, 0, line: false);
        Assert.False(f.IsLine(0));
        Assert.Contains("C", Round(new EditablePath { Figures = { f } }.ToPath()));
    }

    [Fact]
    public void Break_opens_a_closed_figure_and_splits_an_open_one()
    {
        var editable = EditablePath.From(PathDataParser.Parse("M0 0L10 0L10 10L0 10Z"));
        var square = editable.Figures[0];
        editable.BreakAt(square, 2);
        Assert.False(square.Closed);
        Assert.Equal(5, square.Nodes.Count);
        Assert.Equal(square.Nodes[0].Point, square.Nodes[^1].Point);
        Assert.Equal(new VPoint(10, 10), square.Nodes[0].Point);

        var open = EditablePath.From(PathDataParser.Parse("M0 0L10 0L20 0L30 0"));
        var tail = open.BreakAt(open.Figures[0], 2);
        Assert.NotNull(tail);
        Assert.Equal(2, open.Figures.Count);
        Assert.Equal(3, open.Figures[0].Nodes.Count);
        Assert.Equal(2, open.Figures[1].Nodes.Count);
        Assert.Equal(new VPoint(20, 0), open.Figures[1].Nodes[0].Point);
    }

    [Fact]
    public void Join_closes_a_figure_or_merges_two_at_their_ends()
    {
        var one = EditablePath.From(PathDataParser.Parse("M0 0L10 0L10 10L0 10"));
        Assert.True(one.Join(one.Figures[0], true, one.Figures[0], false));
        Assert.True(one.Figures[0].Closed);
        Assert.Equal(3, one.Figures[0].Nodes.Count);              // the two ends became one node, half way

        var two = EditablePath.From(PathDataParser.Parse("M0 0L10 0M12 0L30 0"));
        Assert.True(two.Join(two.Figures[0], true, two.Figures[1], false));
        var joined = Assert.Single(two.Figures);
        Assert.Equal(3, joined.Nodes.Count);
        Assert.Equal(new VPoint(11, 0), joined.Nodes[1].Point);              // met half way
        Assert.Equal(new VPoint(30, 0), joined.Nodes[2].Point);

        var closed = EditablePath.From(PathDataParser.Parse("M0 0L10 0L10 10Z"));
        Assert.False(closed.Join(closed.Figures[0], true, closed.Figures[0], false));
    }

    [Fact]
    public void Join_reverses_figures_when_the_ends_are_the_other_ones()
    {
        var two = EditablePath.From(PathDataParser.Parse("M10 0L0 0M12 0L30 0"));
        Assert.True(two.Join(two.Figures[0], false, two.Figures[1], false));       // join the start of the first to the start of the second
        var joined = Assert.Single(two.Figures);
        Assert.Equal(3, joined.Nodes.Count);
        Assert.Equal(new VPoint(0, 0), joined.Nodes[0].Point);
        Assert.Equal(new VPoint(30, 0), joined.Nodes[2].Point);
    }
}
