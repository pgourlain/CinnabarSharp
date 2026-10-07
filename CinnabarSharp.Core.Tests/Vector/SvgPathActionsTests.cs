using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class SvgPathActionsTests : VectorToolTestBase
{
    private const string Body =
        "<rect id='a' x='10' y='10' width='40' height='30' fill='#ff0000' stroke='#0000ff' stroke-width='6'/>" +
        "<rect id='b' x='30' y='20' width='50' height='40' fill='#00aa00'/>" +
        "<g id='g' transform='translate(100 100) rotate(20)'><rect id='c' x='0' y='0' width='40' height='40' fill='#0000ff'/></g>" +
        "<circle id='k' cx='60' cy='150' r='25' fill='#ffcc00'/>";

    private static byte[] Pixels(SvgDocument doc) => VectorRasterizer.RenderAll(doc.Root, 1, doc.RenderOptions).Bgra;

    private static double DifferentShare(byte[] a, byte[] b)
    {
        var different = 0;
        for (var i = 0; i < a.Length; i += 4)
            if (Math.Abs(a[i] - b[i]) > 60 || Math.Abs(a[i + 1] - b[i + 1]) > 60 || Math.Abs(a[i + 2] - b[i + 2]) > 60 || Math.Abs(a[i + 3] - b[i + 3]) > 60)
                different++;
        return different / (a.Length / 4.0);
    }

    [Fact]
    public void Align_to_the_page_edge_and_to_the_first_selected()
    {
        var doc = Open(Body);
        doc.Actions.Align([El(doc, "a"), El(doc, "b"), El(doc, "k")], AlignEdge.Left, AlignRelativeTo.Page);
        Assert.Equal(new[] { 0.0, 0.0, 0.0 }, new[] { Box(doc, "a").X, Box(doc, "b").X, Box(doc, "k").X });
        var steps = Steps(doc);
        doc.Actions.Align([El(doc, "k"), El(doc, "b")], AlignEdge.Bottom, AlignRelativeTo.FirstSelected);
        Assert.Equal(Steps(doc), steps + 1);
        Assert.Equal(Box(doc, "k").Bottom, Box(doc, "b").Bottom, 1e-6);
        Assert.Equal(175, Box(doc, "k").Bottom, 1e-6);   // the first selected did not move
    }

    [Fact]
    public void Distribute_centers_and_gaps()
    {
        var doc = Open("<rect id='a' x='0' y='0' width='10' height='10'/><rect id='b' x='15' y='0' width='30' height='10'/><rect id='c' x='100' y='0' width='20' height='10'/>");
        doc.Actions.Distribute([El(doc, "a"), El(doc, "b"), El(doc, "c")], DistributeMode.CenterHorizontal);
        Assert.Equal(5, Box(doc, "a").Center.X, 1e-6);
        Assert.Equal(57.5, Box(doc, "b").Center.X, 1e-6);
        Assert.Equal(110, Box(doc, "c").Center.X, 1e-6);
        doc.Actions.Distribute([El(doc, "a"), El(doc, "b"), El(doc, "c")], DistributeMode.GapHorizontal);
        var gaps = new[] { Box(doc, "b").Left - Box(doc, "a").Right, Box(doc, "c").Left - Box(doc, "b").Right };
        Assert.Equal(gaps[0], gaps[1], 1e-6);
        Assert.Equal(0, Box(doc, "a").Left, 1e-6);
        Assert.Equal(120, Box(doc, "c").Right, 1e-6);
    }

    [Fact]
    public void Rotate_90_turns_around_the_center()
    {
        var doc = Open(Body);
        doc.Actions.Rotate90([El(doc, "b")], clockwise: true);
        var box = Box(doc, "b");
        Assert.Equal((40.0, 50.0), (box.Width, box.Height));
        Assert.Equal(new VPoint(55, 40), box.Center);
        doc.Actions.Rotate90([El(doc, "b")], clockwise: false);
        Assert.Equal((50.0, 40.0), (Box(doc, "b").Width, Box(doc, "b").Height));
    }

    [Fact]
    public void Union_replaces_the_shapes_by_one_path_with_the_style_of_the_bottom_one_and_looks_the_same()
    {
        var doc = Open("<rect id='a' x='10' y='10' width='60' height='60' fill='#336699'/><circle id='k' cx='70' cy='70' r='30' fill='#336699'/>");
        var before = Pixels(doc);
        var steps = Steps(doc);
        var result = Assert.Single(doc.Actions.ApplyPathOperation(PathOperation.Union, [El(doc, "a"), El(doc, "k")]));

        Assert.Equal(steps + 1, Steps(doc));
        var path = Assert.IsType<SvgPath>(result);
        Assert.Equal("#336699", path.Style.Get("fill"));
        Assert.Equal(["Union"], doc.History.Items.Skip(steps).Select(i => i.Text));
        Assert.Equal([path], doc.Root.Descendants().OfType<SvgShape>());
        Assert.Equal([path], doc.Selection.Nodes);
        Assert.True(DifferentShare(before, Pixels(doc)) < 0.002);
        doc.History.Undo();
        Assert.Equal(["a", "k"], doc.Root.Descendants().OfType<SvgShape>().Select(s => s.Id));
    }

    [Fact]
    public void Difference_cuts_the_top_shapes_out_of_the_bottom_one()
    {
        var doc = Open("<rect id='a' x='20' y='20' width='100' height='100' fill='#aa0000'/><rect id='b' x='60' y='0' width='20' height='200' fill='#0000aa'/>");
        var path = Assert.IsType<SvgPath>(Assert.Single(doc.Actions.ApplyPathOperation(PathOperation.Difference, [El(doc, "b"), El(doc, "a")])));
        // The bottom shape (a) is cut by the top one (b): two pieces left and right of the cut, as one path.
        Assert.Equal("#aa0000", path.Style.Get("fill"));
        Assert.Equal(2, PathBoolean.BreakApart(path.CreatePath()).Count);
        var pixels = VectorRasterizer.RenderAll(doc.Root, 1, doc.RenderOptions);
        Assert.Equal(0, pixels.Bgra[(50 * pixels.Width + 70) * 4 + 3]);      // inside the cut
        Assert.Equal(255, pixels.Bgra[(50 * pixels.Width + 30) * 4 + 3]);    // left of it
    }

    [Fact]
    public void Operations_work_across_groups_with_transforms()
    {
        var doc = Open(Body);
        var before = Pixels(doc);
        var steps = Steps(doc);
        doc.Actions.ApplyPathOperation(PathOperation.Union, [El(doc, "b"), El(doc, "c")]);
        Assert.Equal(steps + 1, Steps(doc));
        Assert.DoesNotContain(doc.Root.Descendants().OfType<SvgRect>(), r => r.Id is "b" or "c");
        // Only the merged shape's colour changes (the group's rectangle takes the bottom shape's green).
        var after = Pixels(doc);
        Assert.True(DifferentShare(before, after) < 0.06);
    }

    [Fact]
    public void Intersection_with_nothing_in_common_removes_both_and_division_cuts_in_pieces()
    {
        var doc = Open("<rect id='a' x='0' y='0' width='20' height='20'/><rect id='b' x='100' y='100' width='20' height='20'/>");
        Assert.Empty(doc.Actions.ApplyPathOperation(PathOperation.Intersection, [El(doc, "a"), El(doc, "b")]));
        Assert.Empty(doc.Root.Descendants().OfType<SvgShape>());

        doc = Open("<rect id='a' x='0' y='0' width='20' height='20'/><rect id='b' x='10' y='10' width='20' height='20'/>");
        var pieces = doc.Actions.ApplyPathOperation(PathOperation.Division, [El(doc, "a"), El(doc, "b")]);
        Assert.Equal(2, pieces.Count);
        Assert.Equal(2, doc.Root.Descendants().OfType<SvgPath>().Count());
    }

    [Fact]
    public void Combine_keeps_the_outlines_and_break_apart_splits_them_again()
    {
        var doc = Open("<rect id='a' x='0' y='0' width='20' height='20' fill='#abcdef'/><circle id='k' cx='60' cy='10' r='10'/>");
        var combined = Assert.IsType<SvgPath>(Assert.Single(doc.Actions.ApplyPathOperation(PathOperation.Combine, [El(doc, "a"), El(doc, "k")])));
        Assert.Equal(2, PathBoolean.BreakApart(combined.CreatePath()).Count);
        var parts = doc.Actions.BreakApart([combined]);
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Equal("#abcdef", p.Style.Get("fill")));
        Assert.Equal(2, doc.Root.Descendants().OfType<SvgPath>().Count());
    }

    [Fact]
    public void Stroke_to_path_looks_the_same_and_keeps_the_fill_below()
    {
        var doc = Open("<rect id='a' x='30' y='30' width='100' height='60' rx='12' fill='#ffcc00' stroke='#cc2200' stroke-width='10'/>" +
            "<line id='l' x1='20' y1='150' x2='180' y2='170' stroke='#0000ff' stroke-width='12' stroke-linecap='round'/>");
        var before = Pixels(doc);
        var created = doc.Actions.StrokeToPath([El(doc, "a"), El(doc, "l")]);
        Assert.Equal(3, created.Count);                      // a (fill only), its outline, the line's outline
        Assert.Equal("none", El(doc, "a").Style.Get("stroke"));
        var outline = Assert.IsType<SvgPath>(created[1]);
        Assert.Equal("#cc2200", outline.Style.Get("fill"));
        Assert.Equal("none", outline.Style.Get("stroke"));
        Assert.True(DifferentShare(before, Pixels(doc)) < 0.002);
        doc.History.Undo();
        Assert.Equal("#cc2200", El(doc, "a").Style.Get("stroke"));
        Assert.Equal(["a", "l"], doc.Root.Descendants().OfType<SvgShape>().Select(s => s.Id));
    }

    [Fact]
    public void Reverse_and_simplify_edit_the_path_in_place()
    {
        var doc = Open("<path id='p' d='M0 0 L10 0 L10 10 L0 10 Z' fill='#000'/><rect id='r' x='50' y='50' width='10' height='10'/>");
        var original = ((SvgPath)El(doc, "p")).Data;
        doc.Actions.Reverse([El(doc, "p")]);
        Assert.NotEqual(original, ((SvgPath)El(doc, "p")).Data);
        doc.Actions.Reverse([El(doc, "p")]);
        Assert.Equal(PathDataWriter.Write(PathDataParser.Parse(original)), PathDataWriter.Write(PathDataParser.Parse(((SvgPath)El(doc, "p")).Data)));
        // A shape becomes a path.
        doc.Actions.Simplify([El(doc, "r")]);
        Assert.IsType<SvgPath>(doc.Root.Descendants().OfType<SvgShape>().Last());
    }

    [Fact]
    public void Text_and_groups_are_refused()
    {
        var doc = Open("<rect id='a' x='0' y='0' width='20' height='20'/><text id='t' x='10' y='100'>Hi</text>");
        Assert.Throws<InvalidOperationException>(() => doc.Actions.ApplyPathOperation(PathOperation.Union, [El(doc, "a"), El(doc, "t")]));
        Assert.Equal(["a"], doc.Root.Descendants().OfType<SvgShape>().Select(s => s.Id));
    }
}
