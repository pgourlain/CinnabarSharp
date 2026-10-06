using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class VectorSelectToolTests : VectorToolTestBase
{
    private const string Shapes =
        "<rect id='a' x='10' y='10' width='40' height='30' fill='#f00'/>" +
        "<circle id='b' cx='100' cy='30' r='20' fill='#0f0'/>" +
        "<g id='g' transform='translate(0 100)'><rect id='g1' x='10' y='0' width='40' height='30' fill='#00f'/><rect id='g2' x='30' y='10' width='40' height='30' fill='#ff0'/></g>" +
        "<line id='l' x1='120' y1='120' x2='180' y2='180' stroke='#000' stroke-width='4'/>" +
        "<rect id='nofill' x='150' y='10' width='40' height='40' fill='none' stroke='#000' stroke-width='2'/>";

    private (SvgDocument Doc, VectorSelectTool Tool) Setup()
    {
        var doc = Open(Shapes);
        return (doc, new VectorSelectTool(Settings));
    }

    [Fact]
    public void Click_selects_the_top_most_object_and_empty_space_deselects()
    {
        var (doc, tool) = Setup();
        Click(tool, doc, 20, 20);
        Assert.Equal(["a"], doc.Selection.Nodes.Select(n => n.Id));
        Click(tool, doc, 100, 30);
        Assert.Equal(["b"], doc.Selection.Nodes.Select(n => n.Id));
        Click(tool, doc, 190, 190);
        Assert.True(doc.Selection.IsEmpty);
    }

    [Fact]
    public void Selection_clicks_are_undoable_steps_only_when_the_selection_changes()
    {
        var (doc, tool) = Setup();
        var steps = Steps(doc);
        Click(tool, doc, 20, 20);
        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Select", doc.History.Items[^1].Text);
        Click(tool, doc, 20, 20);                         // the same object again: nothing changes
        Assert.Equal(steps + 1, Steps(doc));
        doc.History.Undo();
        Assert.True(doc.Selection.IsEmpty);
    }

    [Fact]
    public void Shift_toggles_and_unfilled_shapes_are_picked_by_their_stroke_only()
    {
        var (doc, tool) = Setup();
        Click(tool, doc, 20, 20);
        Click(tool, doc, 100, 30, ToolModifiers.Shift);
        Assert.Equal(["a", "b"], doc.Selection.Nodes.Select(n => n.Id));
        Click(tool, doc, 20, 20, ToolModifiers.Shift);
        Assert.Equal(["b"], doc.Selection.Nodes.Select(n => n.Id));

        Click(tool, doc, 170, 30);                         // inside the unfilled rect: nothing
        Assert.True(doc.Selection.IsEmpty);
        Click(tool, doc, 150, 30);                         // on its stroke
        Assert.Equal(["nofill"], doc.Selection.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Groups_are_one_object_unless_ctrl_clicked_and_alt_click_goes_below()
    {
        var (doc, tool) = Setup();
        Click(tool, doc, 40, 120);                         // g2 over g1, inside the group
        Assert.Equal(["g"], doc.Selection.Nodes.Select(n => n.Id));
        Click(tool, doc, 40, 120, ToolModifiers.Command);
        Assert.Equal(["g2"], doc.Selection.Nodes.Select(n => n.Id));

        // g1 and g2 overlap at (40, 115)..: with Alt the object below the selected one is picked.
        Click(tool, doc, 40, 120, ToolModifiers.Command);
        Click(tool, doc, 40, 120, ToolModifiers.Command | ToolModifiers.Alt);
        Assert.Equal(["g1"], doc.Selection.Nodes.Select(n => n.Id));
        Click(tool, doc, 40, 120, ToolModifiers.Command | ToolModifiers.Alt);
        Assert.Equal(["g2"], doc.Selection.Nodes.Select(n => n.Id));          // back to the top after the last
    }

    [Fact]
    public void Locked_and_hidden_objects_cannot_be_picked()
    {
        var (doc, tool) = Setup();
        doc.Actions.SetLocked(El(doc, "a"), true);
        doc.Actions.SetVisible(El(doc, "b"), false);
        Click(tool, doc, 20, 20);
        Assert.True(doc.Selection.IsEmpty);
        Click(tool, doc, 100, 30);
        Assert.True(doc.Selection.IsEmpty);
    }

    [Fact]
    public void Rubber_band_selects_what_it_encloses_and_shift_adds()
    {
        var (doc, tool) = Setup();
        Drag(tool, doc, 0, 0, 130, 60);
        Assert.Equal(["a", "b"], doc.Selection.Nodes.Select(n => n.Id));
        Drag(tool, doc, 0, 95, 80, 145, ToolModifiers.Shift);
        Assert.Equal(["a", "b", "g"], doc.Selection.Nodes.Select(n => n.Id));
        Drag(tool, doc, 0, 0, 30, 30);                    // a is not entirely inside: nothing
        Assert.True(doc.Selection.IsEmpty);
        Assert.Null(tool.GetOverlay(doc));
    }

    [Fact]
    public void The_rubber_band_is_shown_while_dragging_and_one_step_selects()
    {
        var (doc, tool) = Setup();
        var steps = Steps(doc);
        tool.OnPointerDown(doc, At(0, 0));
        tool.OnPointerMove(doc, At(130, 60));
        var overlay = Assert.IsType<ToolOverlay>(tool.GetOverlay(doc));
        Assert.Equal((0, 0, 130, 60), ((int)overlay.Frame!.Value.X, (int)overlay.Frame.Value.Y, (int)overlay.Frame.Value.Width, (int)overlay.Frame.Value.Height));
        tool.OnPointerUp(doc, At(130, 60));
        Assert.Equal(steps + 1, Steps(doc));
    }

    [Fact]
    public void Dragging_an_object_moves_it_in_exactly_one_step_and_undo_restores_everything()
    {
        var (doc, tool) = Setup();
        Settings.SnapToObjects = false;
        var before = Xml(doc);
        var steps = Steps(doc);

        Drag(tool, doc, 20, 20, 45, 50);

        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Move", doc.History.Items[^1].Text);
        Assert.Equal(new VRect(35, 40, 40, 30), Box(doc, "a"));
        Assert.Equal(["a"], doc.Selection.Nodes.Select(n => n.Id));
        Assert.Equal("35", El(doc, "a").GetAttribute("x"));                       // natural form
        doc.History.Undo();
        Assert.Equal(before, Xml(doc));
        Assert.True(doc.Selection.IsEmpty);                                      // the click's selection is part of the step
        doc.History.Redo();
        Assert.Equal(new VRect(35, 40, 40, 30), Box(doc, "a"));
        Assert.Equal(["a"], doc.Selection.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void Move_is_shown_live_before_the_mouse_is_released_and_is_not_in_the_history_yet()
    {
        var (doc, tool) = Setup();
        Settings.SnapToObjects = false;
        var steps = Steps(doc);
        tool.OnPointerDown(doc, At(20, 20));
        tool.OnPointerMove(doc, At(30, 30));
        tool.OnPointerMove(doc, At(40, 40));
        Assert.Equal(new VRect(30, 30, 40, 30), Box(doc, "a"));
        Assert.Equal(steps, Steps(doc));
        tool.OnPointerUp(doc, At(40, 40));
        Assert.Equal(steps + 1, Steps(doc));
    }

    [Fact]
    public void Moves_snap_to_the_page_and_to_other_objects_unless_alt_is_held()
    {
        var (doc, tool) = Setup();
        Settings.SnapToObjects = true;
        // a's left edge is at 10: dragging it 7 units left gets within reach of the page edge (x = 0)... 10 - 10 = 0 with a delta of -10;
        // a delta of -7 is within 6 of... 3 away: snaps to -10.
        Drag(tool, doc, 20, 20, 13, 20);
        Assert.Equal(0, Box(doc, "a").X);
        doc.History.Undo();
        Drag(tool, doc, 20, 20, 13, 20, ToolModifiers.Alt);                        // Alt: no snapping
        Assert.Equal(3, Box(doc, "a").X);
    }

    [Fact]
    public void Resize_handles_scale_the_selection_and_shift_keeps_the_ratio()
    {
        var (doc, tool) = Setup();
        Settings.SnapToObjects = false;
        Click(tool, doc, 20, 20);
        var overlay = Assert.IsType<ToolOverlay>(tool.GetOverlay(doc));
        Assert.Equal(8, overlay.Handles.Count);
        Assert.True(overlay.SquareHandles);
        Assert.Equal((10, 10), ((int)overlay.Handles[0].X, (int)overlay.Handles[0].Y));      // top left
        Assert.Equal((50, 40), ((int)overlay.Handles[4].X, (int)overlay.Handles[4].Y));      // bottom right
        var steps = Steps(doc);

        Drag(tool, doc, 50, 40, 90, 70);                                                    // bottom right corner
        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Resize", doc.History.Items[^1].Text);
        Assert.Equal(new VRect(10, 10, 80, 60), Box(doc, "a"));

        Drag(tool, doc, 90, 70, 50, 70, ToolModifiers.Shift);                               // right-bottom: ratio kept
        var box = Box(doc, "a");
        Assert.Equal(80.0 / 60, box.Width / box.Height, 3);
        Assert.Equal((10, 10), (box.X, box.Y));

        doc.History.Undo();
        doc.History.Undo();
        Assert.Equal(new VRect(10, 10, 40, 30), Box(doc, "a"));
    }

    [Fact]
    public void Resize_from_the_center_with_alt_and_edge_handles()
    {
        var (doc, tool) = Setup();
        Settings.SnapToObjects = false;
        Click(tool, doc, 20, 20);
        Drag(tool, doc, 50, 25, 60, 25);                                                   // right edge middle
        Assert.Equal(new VRect(10, 10, 50, 30), Box(doc, "a"));
        Drag(tool, doc, 60, 40, 70, 50, ToolModifiers.Alt);                                // bottom right corner, from the center
        var box = Box(doc, "a");
        Assert.Equal(35, box.Center.X, 3);
        Assert.Equal(25, box.Center.Y, 3);
        Assert.True(box.Width > 50 && box.Height > 30);
    }

    [Fact]
    public void Clicking_the_selection_again_shows_rotate_handles_and_a_corner_drag_rotates()
    {
        var (doc, tool) = Setup();
        Settings.SnapToObjects = false;
        Click(tool, doc, 20, 20);
        Assert.False(tool.RotateMode);
        Click(tool, doc, 20, 20);                                                          // again: rotate/skew mode
        Assert.True(tool.RotateMode);
        var overlay = Assert.IsType<ToolOverlay>(tool.GetOverlay(doc));
        Assert.False(overlay.SquareHandles);
        Assert.NotEmpty(overlay.Lines);                                                    // the rotation center cross
        var steps = Steps(doc);

        // Rotate the corner at (50, 40) by 90 degrees about the center (30, 25): to (45, -... ) use a quarter turn.
        Drag(tool, doc, 50, 40, 15, 40);
        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Rotate", doc.History.Items[^1].Text);
        Assert.False(El(doc, "a").Transform.IsIdentity);
        Assert.Equal("10", El(doc, "a").GetAttribute("x"));                                // geometry untouched, transform edited
        Assert.True(Math.Abs(El(doc, "a").Transform.Decompose().Rotation) > 20);

        // Shift snaps to 15 degrees.
        doc.History.Undo();
        Drag(tool, doc, 50, 40, 52, 10, ToolModifiers.Shift);
        Assert.Equal(0, Math.Abs(El(doc, "a").Transform.Decompose().Rotation) % 15, 3);

        // A third click goes back to resize handles (not on the center marker, which moves the rotation center).
        Click(tool, doc, 38, 25);
        Assert.False(tool.RotateMode);
    }

    [Fact]
    public void The_rotation_center_can_be_moved_and_skew_uses_the_edge_handles()
    {
        var (doc, tool) = Setup();
        Settings.SnapToObjects = false;
        Click(tool, doc, 20, 20);
        Click(tool, doc, 20, 20);
        var steps = Steps(doc);
        Drag(tool, doc, 30, 25, 10, 10);                                                   // the center marker: only a UI state
        Assert.Equal(steps, Steps(doc));
        Drag(tool, doc, 50, 40, 10, 40);                                                   // rotate around (10, 10) now
        var t = El(doc, "a").Transform;
        // The point (10, 10) stays where it is.
        var p = t.Transform(new VPoint(10, 10));
        Assert.Equal(10, p.X, 3);
        Assert.Equal(10, p.Y, 3);

        doc.History.Undo();
        Drag(tool, doc, 30, 40, 40, 40);                                                   // bottom edge middle: skew along x
        var skew = El(doc, "a").Transform;
        Assert.NotEqual(0, skew.C, 6);
        Assert.Equal(0, skew.B, 6);
    }

    [Fact]
    public void Arrow_keys_move_and_delete_removes_and_escape_deselects()
    {
        var (doc, tool) = Setup();
        Click(tool, doc, 20, 20);
        var steps = Steps(doc);
        Assert.True(tool.OnKeyDown(doc, ToolKey.Right, ToolModifiers.None));
        Assert.True(tool.OnKeyDown(doc, ToolKey.Down, ToolModifiers.Shift));
        Assert.Equal(new VRect(11, 20, 40, 30), Box(doc, "a"));
        Assert.Equal(steps + 2, Steps(doc));
        Assert.True(tool.OnKeyDown(doc, ToolKey.Delete, ToolModifiers.None));
        Assert.Null(doc.Root.FindById("a"));
        Assert.False(tool.OnKeyDown(doc, ToolKey.Escape, ToolModifiers.None));              // nothing selected: not used
        Click(tool, doc, 100, 30);
        Assert.True(tool.OnKeyDown(doc, ToolKey.Escape, ToolModifiers.None));
        Assert.True(doc.Selection.IsEmpty);
    }

    [Fact]
    public void Escape_during_a_drag_cancels_it_without_a_step()
    {
        var (doc, tool) = Setup();
        Settings.SnapToObjects = false;
        Click(tool, doc, 20, 20);
        var steps = Steps(doc);
        var xml = Xml(doc);
        tool.OnPointerDown(doc, At(20, 20));
        tool.OnPointerMove(doc, At(60, 60));
        tool.OnPointerMove(doc, At(70, 70));
        Assert.True(tool.OnKeyDown(doc, ToolKey.Escape, ToolModifiers.None));
        tool.OnPointerUp(doc, At(70, 70));
        Assert.Equal(steps, Steps(doc));
        Assert.Equal(xml, Xml(doc));
    }

    [Fact]
    public void Cursors_follow_what_is_under_the_pointer()
    {
        var (doc, tool) = Setup();
        Click(tool, doc, 20, 20);
        Assert.Equal(ToolCursor.ResizeDiagonal, tool.CursorAt(doc, new PointD(10, 10)));
        Assert.Equal(ToolCursor.ResizeAntiDiagonal, tool.CursorAt(doc, new PointD(50, 10)));
        Assert.Equal(ToolCursor.ResizeVertical, tool.CursorAt(doc, new PointD(30, 10)));
        Assert.Equal(ToolCursor.ResizeHorizontal, tool.CursorAt(doc, new PointD(50, 25)));
        Assert.Equal(ToolCursor.Move, tool.CursorAt(doc, new PointD(25, 25)));
        Assert.Equal(ToolCursor.Default, tool.CursorAt(doc, new PointD(150, 150)));
    }

    [Fact]
    public void Zoom_scales_handle_reach_and_tolerance()
    {
        var (doc, tool) = Setup();
        Click(tool, doc, 20, 20);
        doc.Workspace.Scale = 0.1;                                                         // zoomed far out: handles are 10x bigger in user units
        Assert.Equal(ToolCursor.ResizeDiagonal, tool.CursorAt(doc, new PointD(10 + 40, 10 + 40)));  // 40 units from the corner, 4 px on screen
        doc.Workspace.Scale = 1;
        Assert.NotEqual(ToolCursor.ResizeDiagonal, tool.CursorAt(doc, new PointD(10 + 40, 10 + 40)));
    }
}
