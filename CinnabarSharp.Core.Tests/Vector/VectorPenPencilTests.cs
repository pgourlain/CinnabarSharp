using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class VectorPenPencilTests : VectorToolTestBase
{
    private SvgDocument Blank() => Open("<g id='layer1' xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape' inkscape:groupmode='layer'/>");

    private static IEnumerable<SvgElement> Shapes(SvgDocument doc) => ((SvgContainer)El(doc, "layer1")).Elements;

    private static SvgPath OnlyPath(SvgDocument doc) => Assert.IsType<SvgPath>(Assert.Single(Shapes(doc)));

    [Fact]
    public void Clicks_make_corner_nodes_and_enter_finishes_in_one_step()
    {
        var doc = Blank();
        var pen = new VectorPenTool(Settings);
        var steps = Steps(doc);
        Click(pen, doc, 10, 10);
        Click(pen, doc, 60, 10);
        Click(pen, doc, 60, 50);
        Assert.True(pen.IsEditing(doc));
        Assert.Equal(steps, Steps(doc));                       // still being drawn, nothing recorded
        Assert.Single(Shapes(doc));                           // but shown

        Assert.True(pen.OnKeyDown(doc, ToolKey.Enter, ToolModifiers.None));

        Assert.False(pen.IsEditing(doc));
        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Pen", doc.History.Items[^1].Text);
        Assert.Equal("M10 10H60V50", OnlyPath(doc).GetAttribute("d"));
        Assert.Equal("none", OnlyPath(doc).GetAttribute("fill"));
        Assert.NotNull(OnlyPath(doc).GetAttribute("stroke"));
        doc.History.Undo();
        Assert.Empty(Shapes(doc));
    }

    [Fact]
    public void Click_and_drag_makes_a_smooth_node_with_mirrored_handles()
    {
        var doc = Blank();
        var pen = new VectorPenTool(Settings);
        Click(pen, doc, 10, 50);
        Drag(pen, doc, 60, 50, 80, 30);                         // second node: drag out its handle to (80, 30)
        pen.OnKeyDown(doc, ToolKey.Enter, ToolModifiers.None);
        var data = OnlyPath(doc).GetAttribute("d")!;
        Assert.Contains("C", data);
        var nodes = EditablePath.From(OnlyPath(doc).CreatePath()).Figures[0].Nodes;
        // The handle dragged out is the end's outgoing one, which a path that stops there cannot keep; the incoming one is its mirror.
        Assert.Equal(new VPoint(40, 70), nodes[1].In);
    }

    [Fact]
    public void Clicking_the_first_node_closes_the_path()
    {
        var doc = Blank();
        var pen = new VectorPenTool(Settings);
        var steps = Steps(doc);
        Click(pen, doc, 10, 10);
        Click(pen, doc, 60, 10);
        Click(pen, doc, 60, 50);
        Click(pen, doc, 12, 12);                                // near the first node
        Assert.False(pen.IsEditing(doc));
        Assert.Equal(steps + 1, Steps(doc));
        Assert.EndsWith("Z", OnlyPath(doc).GetAttribute("d")!);
        Assert.Equal(3, EditablePath.From(OnlyPath(doc).CreatePath()).Figures[0].Nodes.Count);
    }

    [Fact]
    public void Double_click_finishes_open_and_escape_cancels_and_backspace_removes_the_last_node()
    {
        var doc = Blank();
        var pen = new VectorPenTool(Settings);
        Click(pen, doc, 10, 10);
        Click(pen, doc, 60, 10);
        pen.OnPointerDown(doc, At(60, 50));
        pen.OnPointerUp(doc, At(60, 50));
        Assert.True(pen.OnKeyDown(doc, ToolKey.Backspace, ToolModifiers.None));
        pen.OnPointerDown(doc, At(80, 80, clicks: 1));
        pen.OnPointerUp(doc, At(80, 80));
        pen.OnPointerDown(doc, At(80, 80, clicks: 2));          // double click
        pen.OnPointerUp(doc, At(80, 80));
        Assert.False(pen.IsEditing(doc));
        Assert.Equal("M10 10H60L80 80", OnlyPath(doc).GetAttribute("d"));

        var steps = Steps(doc);
        Click(pen, doc, 100, 100);
        Click(pen, doc, 150, 100);
        Assert.Equal(2, Shapes(doc).Count());
        Assert.True(pen.OnKeyDown(doc, ToolKey.Escape, ToolModifiers.None));
        Assert.Single(Shapes(doc));
        Assert.Equal(steps, Steps(doc));

        Click(pen, doc, 100, 100);
        Assert.True(pen.OnKeyDown(doc, ToolKey.Backspace, ToolModifiers.None));
        Assert.False(pen.IsEditing(doc));                       // nothing left
        Assert.False(pen.OnKeyDown(doc, ToolKey.Enter, ToolModifiers.None));
    }

    [Fact]
    public void A_single_node_is_not_a_path()
    {
        var doc = Blank();
        var pen = new VectorPenTool(Settings);
        var steps = Steps(doc);
        Click(pen, doc, 10, 10);
        pen.Finish(doc);
        Assert.Empty(Shapes(doc));
        Assert.Equal(steps, Steps(doc));
    }

    [Fact]
    public void Shift_snaps_the_segment_direction_to_15_degrees()
    {
        var doc = Blank();
        var pen = new VectorPenTool(Settings);
        Click(pen, doc, 10, 10);
        Click(pen, doc, 60, 18, ToolModifiers.Shift);           // about 9 degrees: snaps to 15
        pen.Finish(doc);
        var nodes = EditablePath.From(OnlyPath(doc).CreatePath()).Figures[0].Nodes;
        var d = nodes[1].Point - nodes[0].Point;
        Assert.Equal(15, Math.Atan2(d.Y, d.X) * 180 / Math.PI, 3);
    }

    [Fact]
    public void Another_history_change_ends_the_drawing_and_drops_the_live_path()
    {
        var doc = Blank();
        var pen = new VectorPenTool(Settings);
        Click(pen, doc, 10, 10);
        Click(pen, doc, 60, 10);
        doc.Actions.AddNode(new SvgRect { Id = "other" }.With(r => { r.Width = 5; r.Height = 5; }));     // something else is done meanwhile
        Assert.False(pen.IsEditing(doc));
        Assert.Equal(["other"], Shapes(doc).Select(s => s.Id));
    }

    [Fact]
    public void Overlay_shows_nodes_and_handles_and_style_options_apply()
    {
        var doc = Blank();
        Settings.PrimaryColor = ColorBgra.FromBgra(0, 0, 255, 255);
        Settings.BrushWidth = 4;
        var pen = new VectorPenTool(Settings);
        Assert.Null(pen.GetOverlay(doc));
        Click(pen, doc, 10, 10);
        Drag(pen, doc, 60, 10, 80, 30);
        var overlay = Assert.IsType<ToolOverlay>(pen.GetOverlay(doc));
        Assert.Equal(4, overlay.Handles.Count);                  // two nodes and the two handles of the dragged one
        Assert.Equal(2, overlay.Lines.Count);
        pen.Finish(doc);
        Assert.Equal("red", OnlyPath(doc).GetAttribute("stroke"));
        Assert.Equal("4", OnlyPath(doc).GetAttribute("stroke-width"));
    }

    [Fact]
    public void Pencil_fits_curves_through_a_freehand_stroke_in_one_step()
    {
        var doc = Blank();
        var pencil = new VectorPencilTool(Settings);
        var steps = Steps(doc);
        pencil.OnPointerDown(doc, At(20, 100));
        for (var i = 1; i <= 60; i++)
        {
            var x = 20 + i * 2.5;
            pencil.OnPointerMove(doc, At(x, 100 - 40 * Math.Sin(i * Math.PI / 30)));
        }
        Assert.Single(Shapes(doc));
        Assert.Equal(steps, Steps(doc));
        pencil.OnPointerUp(doc, At(170, 100));

        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Pencil", doc.History.Items[^1].Text);
        var path = OnlyPath(doc).CreatePath();
        var curves = path.Segments.Count(s => s.Kind == SegmentKind.CubicTo);
        Assert.InRange(curves, 2, 20);                            // far fewer than the 60 points drawn
        var bounds = path.Bounds;
        Assert.InRange(bounds.Height, 60, 90);
        Assert.Equal("none", OnlyPath(doc).GetAttribute("fill"));
        doc.History.Undo();
        Assert.Empty(Shapes(doc));
    }

    [Fact]
    public void Pencil_smoothing_changes_how_many_curves_are_needed()
    {
        int Curves(int smoothing)
        {
            var doc = Blank();
            Settings.PencilSmoothing = smoothing;
            var pencil = new VectorPencilTool(Settings);
            pencil.OnPointerDown(doc, At(20, 100));
            var rng = new Random(5);
            for (var i = 1; i <= 80; i++)
                pencil.OnPointerMove(doc, At(20 + i * 2, 100 + 20 * Math.Sin(i * 0.4) + (rng.NextDouble() - 0.5) * 3));
            pencil.OnPointerUp(doc, At(180, 100));
            return OnlyPath(doc).CreatePath().Segments.Count;
        }
        Assert.True(Curves(0) >= Curves(100));
    }

    [Fact]
    public void A_click_with_the_pencil_draws_nothing_and_a_closed_loop_is_closed()
    {
        var doc = Blank();
        var pencil = new VectorPencilTool(Settings);
        var steps = Steps(doc);
        Click(pencil, doc, 50, 50);
        Assert.Equal(steps, Steps(doc));
        pencil.OnPointerDown(doc, At(100, 50));
        for (var i = 1; i <= 36; i++)
            pencil.OnPointerMove(doc, At(100 + 30 * Math.Cos(i * Math.PI / 18), 50 + 30 * Math.Sin(i * Math.PI / 18)));
        pencil.OnPointerUp(doc, At(100.5, 50.2));
        Assert.EndsWith("Z", OnlyPath(doc).GetAttribute("d")!);
    }
}
