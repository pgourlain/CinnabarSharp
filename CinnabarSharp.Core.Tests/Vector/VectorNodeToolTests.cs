using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class VectorNodeToolTests : VectorToolTestBase
{
    private const string Body =
        "<path id='p' d='M20 100C20 40 100 40 100 100L160 100' fill='none' stroke='#000' stroke-width='2'/>" +
        "<rect id='r' x='20' y='140' width='60' height='40' fill='#f00'/>" +
        "<ellipse id='e' cx='140' cy='160' rx='30' ry='20' fill='#0f0'/>";

    private (SvgDocument Doc, VectorNodeTool Tool) Setup(string body = Body)
    {
        var doc = Open(body);
        var tool = new VectorNodeTool(Settings);
        doc.Selection.Set(El(doc, "p"));
        return (doc, tool);
    }

    private static string Data(SvgDocument doc, string id = "p") => El(doc, id).GetAttribute("d")!;

    [Fact]
    public void The_overlay_shows_the_nodes_of_the_selected_path()
    {
        var (doc, tool) = Setup();
        var overlay = Assert.IsType<ToolOverlay>(tool.GetOverlay(doc));
        Assert.Equal(3, overlay.Handles.Count);                         // the nodes; no handles until a node is selected
        Click(tool, doc, 20, 100);
        overlay = tool.GetOverlay(doc)!;
        Assert.Single(tool.SelectedNodes);
        Assert.Equal(4, overlay.Handles.Count);                         // three nodes and the handle of the selected one
        Assert.Single(overlay.Highlights);
        Assert.Single(overlay.Lines);                                    // the first node has one handle
    }

    [Fact]
    public void Dragging_a_node_moves_it_with_its_handles_in_one_step()
    {
        var (doc, tool) = Setup();
        var steps = Steps(doc);
        Drag(tool, doc, 100, 100, 120, 80);
        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Edit Path", doc.History.Items[^1].Text);
        var nodes = EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures[0].Nodes;
        Assert.Equal(new VPoint(120, 80), nodes[1].Point);
        Assert.Equal(new VPoint(120, 20), nodes[1].In);                  // moved with the node (it was at 100,40)
        doc.History.Undo();
        Assert.Equal("M20 100C20 40 100 40 100 100L160 100", Data(doc));      // exactly the original text
        doc.History.Redo();
        Assert.Contains("120 80", Data(doc));
    }

    [Fact]
    public void The_edit_is_shown_while_dragging_but_not_recorded_until_release()
    {
        var (doc, tool) = Setup();
        var steps = Steps(doc);
        tool.OnPointerDown(doc, At(100, 100));
        tool.OnPointerMove(doc, At(110, 90));
        tool.OnPointerMove(doc, At(120, 80));
        Assert.Contains("120 80", Data(doc));
        Assert.Equal(steps, Steps(doc));
        tool.OnPointerUp(doc, At(120, 80));
        Assert.Equal(steps + 1, Steps(doc));
    }

    [Fact]
    public void A_click_selects_nodes_shift_toggles_and_a_rubber_band_selects_several()
    {
        var (doc, tool) = Setup();
        Click(tool, doc, 20, 100);
        Click(tool, doc, 160, 100, ToolModifiers.Shift);
        Assert.Equal(2, tool.SelectedNodes.Count);
        Click(tool, doc, 20, 100, ToolModifiers.Shift);
        Assert.Equal([new NodeRef(0, 2)], tool.SelectedNodes);
        Drag(tool, doc, 0, 0, 200, 120);                                    // empty space inside: all three
        Assert.Equal(3, tool.SelectedNodes.Count);
        Click(tool, doc, 190, 30);
        Assert.Empty(tool.SelectedNodes);
    }

    [Fact]
    public void Dragging_a_handle_changes_the_curve_and_smooth_nodes_keep_their_handles_aligned()
    {
        var (doc, tool) = Setup();
        Click(tool, doc, 100, 100);                                         // select the middle node: its handles show
        tool.SetNodeType(doc, NodeType.Smooth);
        var steps = Steps(doc);
        Drag(tool, doc, 100, 40, 100, 20);                                  // the incoming handle up
        Assert.Equal(steps + 1, Steps(doc));
        var nodes = EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures[0].Nodes;
        Assert.Equal(new VPoint(100, 20), nodes[1].In);
        Assert.NotNull(nodes[1].Out);                                       // smooth: the outgoing handle follows
        var direction = nodes[1].Point - nodes[1].In!.Value;
        var other = nodes[1].Out!.Value - nodes[1].Point;
        Assert.Equal(0, direction.Normalized().Cross(other.Normalized()), 6);
    }

    [Fact]
    public void Alt_moves_a_handle_alone()
    {
        var (doc, tool) = Setup("<path id='p' d='M20 100C20 40 100 40 100 100C100 160 160 160 160 100' fill='none' stroke='#000'/>");
        Click(tool, doc, 100, 100);
        tool.SetNodeType(doc, NodeType.Symmetric);
        Assert.Contains(tool.SelectedNodes, n => n.Index == 1);
        var before = EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures[0].Nodes[1];
        Drag(tool, doc, 100, 40, 90, 20, ToolModifiers.Alt);
        var after = EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures[0].Nodes[1];
        Assert.Equal(new VPoint(90, 20), after.In);
        Assert.Equal(before.Out, after.Out);
    }

    [Fact]
    public void Double_click_on_a_segment_adds_a_node_and_double_click_on_a_node_toggles_its_type()
    {
        var (doc, tool) = Setup();
        var steps = Steps(doc);
        tool.OnPointerDown(doc, At(130, 100, clicks: 2));                   // on the straight segment
        tool.OnPointerUp(doc, At(130, 100));
        Assert.Equal(steps + 1, Steps(doc));
        var nodes = EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures[0].Nodes;
        Assert.Equal(4, nodes.Count);
        Assert.Equal(130, nodes[2].Point.X, 3);
        Assert.Equal([new NodeRef(0, 2)], tool.SelectedNodes);

        steps = Steps(doc);
        tool.OnPointerDown(doc, At(100, 100, clicks: 2));
        tool.OnPointerUp(doc, At(100, 100));
        Assert.Equal(steps + 1, Steps(doc));                                  // corner to smooth: one step
    }

    [Fact]
    public void Delete_removes_selected_nodes_keeping_the_shape_and_keys_nudge()
    {
        var (doc, tool) = Setup("<path id='p' d='M0 100C0 60 20 40 40 40C60 40 80 60 80 100' fill='none' stroke='#000'/>");
        Click(tool, doc, 40, 40);
        Assert.True(tool.OnKeyDown(doc, ToolKey.Right, ToolModifiers.None));
        Assert.True(tool.OnKeyDown(doc, ToolKey.Down, ToolModifiers.Shift));
        var middle = EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures[0].Nodes[1];
        Assert.Equal(new VPoint(41, 50), middle.Point);

        var steps = Steps(doc);
        Assert.True(tool.OnKeyDown(doc, ToolKey.Delete, ToolModifiers.None));
        Assert.Equal(steps + 1, Steps(doc));
        var nodes = EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures[0].Nodes;
        Assert.Equal(2, nodes.Count);
        Assert.NotNull(nodes[0].Out);                                        // still a curve, not a line
        Assert.False(tool.OnKeyDown(doc, ToolKey.Delete, ToolModifiers.None));
    }

    [Fact]
    public void Deleting_every_node_deletes_the_path()
    {
        var (doc, tool) = Setup("<path id='p' d='M0 0L50 50' stroke='#000'/>");
        tool.SelectAllNodes(doc);
        tool.DeleteNodes(doc);
        Assert.Null(doc.Root.FindById("p"));
    }

    [Fact]
    public void Node_type_segment_break_and_join_commands()
    {
        var (doc, tool) = Setup("<path id='p' d='M10 100L60 100L110 100L160 100' fill='none' stroke='#000'/>");
        Click(tool, doc, 60, 100);
        Click(tool, doc, 110, 100, ToolModifiers.Shift);
        tool.SetSegments(doc, line: false);
        Assert.Contains("C", Data(doc));
        var after = Data(doc);
        tool.SetSegments(doc, line: true);
        Assert.DoesNotContain("C", Data(doc));
        doc.History.Undo();
        Assert.Equal(after, Data(doc));

        // Break at the second node: two figures.
        Click(tool, doc, 190, 30);                                           // deselect
        Click(tool, doc, 60, 100);
        tool.BreakNodes(doc);
        Assert.Equal(2, EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures.Count);
        // Join the two end nodes again: both lie on the same spot, so a rubber band takes both.
        Drag(tool, doc, 50, 90, 70, 110);
        Assert.Equal(2, tool.SelectedNodes.Count);
        Assert.True(tool.JoinNodes(doc));
        Assert.Single(EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures);
    }

    [Fact]
    public void Node_types_make_handles_for_corners_and_stay_in_the_tool()
    {
        var (doc, tool) = Setup("<path id='p' d='M10 100L60 50L110 100' fill='none' stroke='#000'/>");
        Click(tool, doc, 60, 50);
        tool.SetNodeType(doc, NodeType.Smooth);
        var node = EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures[0].Nodes[1];
        Assert.NotNull(node.In);
        Assert.NotNull(node.Out);
        Assert.Equal(NodeType.Smooth, tool.Editable!.Figures[0].Nodes[1].Type);
        tool.SetNodeType(doc, NodeType.Corner);
        Assert.Equal(NodeType.Corner, tool.Editable!.Figures[0].Nodes[1].Type);
    }

    [Fact]
    public void Clicking_another_object_selects_it_and_a_rect_shows_its_own_handles()
    {
        var (doc, tool) = Setup();
        Click(tool, doc, 50, 160);                                            // inside the rect
        Assert.Equal(["r"], doc.Selection.Nodes.Select(n => n.Id));
        var overlay = Assert.IsType<ToolOverlay>(tool.GetOverlay(doc));
        Assert.Equal(2, overlay.Handles.Count);                               // rx and ry
        Assert.True(tool.CanConvertToPath(doc));

        var steps = Steps(doc);
        Drag(tool, doc, 66, 140, 65, 140);                                    // the corner radius handle, 14 inside the top right corner
        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Round Corners", doc.History.Items[^1].Text);
        Assert.Equal(15, ((SvgRect)El(doc, "r")).Rx, 3);
        doc.History.Undo();
        Assert.False(El(doc, "r").HasAttribute("rx"));
    }

    [Fact]
    public void Ellipse_handles_resize_the_radii()
    {
        var (doc, tool) = Setup();
        doc.Selection.Set(El(doc, "e"));
        Drag(tool, doc, 170, 160, 180, 160);
        Assert.Equal(40, ((SvgEllipse)El(doc, "e")).Rx, 3);
        Drag(tool, doc, 140, 180, 140, 190);
        Assert.Equal(30, ((SvgEllipse)El(doc, "e")).Ry, 3);
    }

    [Fact]
    public void Convert_to_path_makes_the_shape_editable()
    {
        var (doc, tool) = Setup();
        doc.Selection.Set(El(doc, "r"));
        tool.ConvertToPath(doc);
        Assert.IsType<SvgPath>(doc.Selection.Primary);
        Assert.Equal(4, tool.Editable!.Figures[0].Nodes.Count);
        Assert.False(tool.CanConvertToPath(doc));
    }

    [Fact]
    public void Paths_inside_transformed_groups_are_edited_in_their_own_coordinates()
    {
        var (doc, tool) = Setup("<g transform='translate(50 0) scale(2)'><path id='p' d='M10 50L40 50L40 80' fill='none' stroke='#000'/></g>");
        // The node at (10, 50) is at (70, 100) on the page.
        Drag(tool, doc, 70, 100, 90, 120);
        var nodes = EditablePath.From(((SvgPath)El(doc, "p")).CreatePath()).Figures[0].Nodes;
        Assert.Equal(new VPoint(20, 60), nodes[0].Point);                      // 20 units on the page are 10 in the group
    }

    [Fact]
    public void Undo_while_the_tool_has_a_path_loaded_reloads_it()
    {
        var (doc, tool) = Setup();
        Drag(tool, doc, 100, 100, 120, 80);
        doc.History.Undo();
        var overlay = tool.GetOverlay(doc)!;
        Assert.Equal(100, (int)overlay.Handles[1].X);
    }

    [Fact]
    public void Pressing_a_corner_of_a_rectangle_makes_it_a_path_and_drags_that_node()
    {
        var (doc, tool) = Setup();
        doc.Selection.Set(El(doc, "r"));
        var steps = Steps(doc);
        // The nodes of the outline are shown before anything is converted.
        Assert.Equal(4, tool.GetOverlay(doc)!.Highlights.Count);

        Drag(tool, doc, 20, 140, 10, 120);                        // the top-left corner of the rectangle

        var path = Assert.IsType<SvgPath>(doc.Selection.Primary);
        Assert.Equal(steps + 2, Steps(doc));                       // Object to Path, then the edit
        Assert.Equal(new VRect(10, 120, 70, 60), SvgBounds.InDocument(path));
        doc.History.Undo();
        doc.History.Undo();
        Assert.IsType<SvgRect>(El(doc, "r"));
    }

    [Fact]
    public void Every_kind_of_shape_can_be_edited_point_by_point()
    {
        var doc = Open("<polygon id='pg' points='20,20 80,20 50,70' fill='#f00'/>" +
            "<line id='l' x1='100' y1='20' x2='180' y2='60' stroke='#000'/>" +
            "<ellipse id='e' cx='60' cy='140' rx='40' ry='20' fill='#0f0'/>" +
            "<circle id='c' cx='150' cy='140' r='25' fill='#00f'/>");
        var tool = new VectorNodeTool(Settings);

        doc.Selection.Set(El(doc, "pg"));
        Drag(tool, doc, 50, 70, 50, 90);
        Assert.IsType<SvgPath>(doc.Selection.Primary);
        Assert.Equal(90, SvgBounds.InDocument((SvgElement)doc.Selection.Primary!)!.Value.Bottom, 0.01);

        doc.Selection.Set(El(doc, "l"));
        Drag(tool, doc, 180, 60, 190, 90);
        Assert.IsType<SvgPath>(doc.Selection.Primary);

        doc.Selection.Set(El(doc, "e"));
        Drag(tool, doc, 20, 140, 5, 140);                         // the left node of the ellipse (the right one is a radius handle)
        Assert.IsType<SvgPath>(doc.Selection.Primary);
        Assert.Equal(5, SvgBounds.InDocument((SvgElement)doc.Selection.Primary!)!.Value.Left, 0.5);

        doc.Selection.Set(El(doc, "c"));
        Drag(tool, doc, 150, 115, 150, 100);                      // the top node of the circle
        Assert.IsType<SvgPath>(doc.Selection.Primary);
    }

    [Fact]
    public void Pressing_inside_a_shape_or_on_its_radius_handle_does_not_convert_it()
    {
        var (doc, tool) = Setup();
        doc.Selection.Set(El(doc, "r"));
        Click(tool, doc, 50, 160);                                // inside the rectangle, away from its outline
        Assert.IsType<SvgRect>(El(doc, "r"));
        Assert.Empty(doc.History.Items.Skip(1));
    }
}
