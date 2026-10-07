using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class VectorScissorsTests : VectorToolTestBase
{
    // A line through a rectangle: the rectangle's sides cross it at x = 50 and x = 70.
    private const string LineThroughRect =
        "<line id='l' x1='0' y1='100' x2='200' y2='100' stroke='#000000' stroke-width='4'/>" +
        "<rect id='a' x='50' y='50' width='20' height='100' fill='none' stroke='#ff0000'/>";

    // A circle crossed by a tall rectangle: four crossings, so four arcs.
    private const string CircleAndBar =
        "<circle id='c' cx='100' cy='100' r='40' fill='none' stroke='#0000ff' stroke-width='2'/>" +
        "<rect id='r' x='80' y='20' width='40' height='160' fill='none' stroke='#ff0000'/>";

    private static List<SvgShape> Shapes(SvgDocument doc) => doc.Root.Descendants().OfType<SvgShape>().ToList();

    [Fact]
    public void Clicking_the_run_between_two_crossings_cuts_an_open_line_in_two()
    {
        var doc = Open(LineThroughRect);
        var steps = Steps(doc);
        var tool = new VectorScissorsTool();
        Click(tool, doc, 60, 100);

        Assert.Equal(steps + 1, Steps(doc));
        var paths = Shapes(doc).OfType<SvgPath>().ToList();
        Assert.Equal(2, paths.Count);
        var left = paths[0].CreatePath().Bounds;
        var right = paths[1].CreatePath().Bounds;
        Assert.Equal((0, 50), (left.Left, left.Right));
        Assert.Equal((70, 200), (right.Left, right.Right));
        Assert.All(paths, p => Assert.Equal("#000000", p.Style.Get("stroke") ?? p.GetAttribute("stroke")));
        Assert.DoesNotContain(Shapes(doc), s => s is SvgLine);
        Assert.Equal(2, paths.Select(p => p.Id).Distinct().Count());

        doc.History.Undo();
        Assert.IsType<SvgLine>(El(doc, "l"));
        Assert.Single(Shapes(doc).OfType<SvgLine>());
    }

    [Fact]
    public void Clicking_an_end_run_removes_only_that_end()
    {
        var doc = Open(LineThroughRect);
        Click(new VectorScissorsTool(), doc, 150, 100);
        var path = Assert.IsType<SvgPath>(Assert.Single(Shapes(doc), s => s is not SvgRect));
        var box = path.CreatePath().Bounds;
        Assert.Equal((0, 70), (box.Left, box.Right));
    }

    [Fact]
    public void A_closed_shape_becomes_one_open_path_without_the_arc()
    {
        var doc = Open(CircleAndBar);
        var steps = Steps(doc);
        Click(new VectorScissorsTool(), doc, 100, 60);       // the top arc, between x = 80 and x = 120

        Assert.Equal(steps + 1, Steps(doc));
        var path = Assert.IsType<SvgPath>(doc.Root.Descendants().OfType<SvgShape>().First(s => s.Id == "c" || s is SvgPath));
        var segments = path.CreatePath().Segments;
        Assert.DoesNotContain(segments, s => s.Kind == SegmentKind.Close);
        Assert.False(SvgHitTester.Hits(path, new VPoint(100, 60), 2));
        Assert.True(SvgHitTester.Hits(path, new VPoint(100, 140), 2));     // the bottom arc stays
        Assert.True(SvgHitTester.Hits(path, new VPoint(60, 100), 2));      // and the sides
        Assert.True(SvgHitTester.Hits(path, new VPoint(140, 100), 2));
        // The end of the open path is where the removed arc ended.
        var start = segments[0].End;
        Assert.Equal(40, start.DistanceTo(new VPoint(100, 100)), 0.1);
        Assert.Equal(2, Math.Round(Math.Abs(start.X - 100) / 20 * 2));       // x = 80 or 120
    }

    [Fact]
    public void A_shape_nothing_crosses_cannot_be_cut()
    {
        var doc = Open("<circle id='c' cx='100' cy='100' r='40' fill='none' stroke='#0000ff'/><rect id='r' x='0' y='0' width='20' height='20'/>");
        var steps = Steps(doc);
        Click(new VectorScissorsTool(), doc, 100, 60);
        Assert.Equal(steps, Steps(doc));
        Assert.Null(SvgScissors.Find(doc, new VPoint(100, 60), 3));
    }

    [Fact]
    public void A_click_away_from_any_outline_does_nothing()
    {
        var doc = Open(LineThroughRect);
        var steps = Steps(doc);
        Click(new VectorScissorsTool(), doc, 100, 20);
        Assert.Equal(steps, Steps(doc));
    }

    [Fact]
    public void Shapes_in_groups_and_with_transforms_cut_each_other()
    {
        var doc = Open("<g transform='translate(0 100)'><line id='l' x1='0' y1='0' x2='200' y2='0' stroke='#000000'/></g>" +
                       "<rect id='a' x='50' y='50' width='20' height='100' fill='none' stroke='#ff0000'/>");
        Click(new VectorScissorsTool(), doc, 60, 100);
        var paths = Shapes(doc).OfType<SvgPath>().ToList();
        Assert.Equal(2, paths.Count);
        // The paths stay in the group, in the group's space.
        Assert.All(paths, p => Assert.Equal("g", p.Parent!.ElementName));
        Assert.Equal((0, 50), (paths[0].CreatePath().Bounds.Left, paths[0].CreatePath().Bounds.Right));
    }

    [Fact]
    public void A_shape_cut_earlier_still_cuts_what_its_open_ends_touch()
    {
        var doc = Open("<ellipse id='e' cx='75' cy='130' rx='70' ry='120' fill='none' stroke='#1a3cff' stroke-width='6'/>" +
                       "<circle id='c' cx='130' cy='55' r='60' fill='none' stroke='#1a3cff' stroke-width='6'/>");
        var tool = new VectorScissorsTool();
        // Take out the arc of the circle that is inside the ellipse: its ends now lie on the ellipse.
        Click(tool, doc, 70, 55);
        Assert.IsType<SvgPath>(El(doc, "c"));
        var steps = Steps(doc);

        // The arc of the ellipse between the ends of the circle can be taken out too.
        Assert.NotNull(SvgScissors.Find(doc, new VPoint(131.9, 60), 3));
        Click(tool, doc, 131.9, 60);
        Assert.Equal(steps + 1, Steps(doc));
        Assert.IsType<SvgPath>(El(doc, "e"));
    }

    [Fact]
    public void Hovering_shows_the_run_that_a_click_would_remove()
    {
        var doc = Open(LineThroughRect);
        var tool = new VectorScissorsTool();
        Assert.Null(tool.GetOverlay(doc));
        Assert.True(tool.OnHover(doc, new PointD(60, 100)));
        var overlay = tool.GetOverlay(doc)!;
        Assert.NotEmpty(overlay.Lines);
        Assert.Equal(2, overlay.Handles.Count);
        Assert.Equal(50, overlay.Handles.Min(h => h.X), 1e-6);
        Assert.Equal(70, overlay.Handles.Max(h => h.X), 1e-6);
        Assert.False(tool.OnHover(doc, new PointD(61, 100)));        // same run
        Assert.True(tool.OnHover(doc, new PointD(100, 20)));         // nothing there
        Assert.Null(tool.GetOverlay(doc));
    }
}
