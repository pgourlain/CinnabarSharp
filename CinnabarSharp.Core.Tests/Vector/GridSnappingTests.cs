using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class GridSnappingTests : VectorToolTestBase
{
    [Theory]
    [InlineData(43, 10, 0, 40)]
    [InlineData(45, 10, 0, 50)]
    [InlineData(-4, 10, 0, 0)]
    [InlineData(-6, 10, 0, -10)]
    [InlineData(43, 10, 3, 43)]       // the grid starts at 3: 3, 13, 23, 33, 43
    [InlineData(47, 25, 0, 50)]
    public void Values_go_to_the_nearest_line(double value, double size, double origin, double expected) =>
        Assert.Equal(expected, GridSnapping.Snap(value, size, origin), 1e-9);

    [Fact]
    public void A_box_edge_within_reach_goes_to_a_line_and_none_beyond()
    {
        Assert.Equal(-1, GridSnapping.ShiftToGrid([12.0, 31.0], 10, 0, reach: 3), 1e-9);   // 31 → 30 is the smaller shift
        Assert.Equal(0, GridSnapping.ShiftToGrid([15.0], 10, 0, reach: 3));                 // 5 away: out of reach
    }

    [Fact]
    public void Rectangles_are_drawn_on_the_grid_by_the_marker_through_the_pointer_snapping()
    {
        Assert.IsAssignableFrom<IGridSnappingTool>(new VectorRectangleTool(Settings));
        Assert.IsAssignableFrom<IGridSnappingTool>(new VectorPenTool(Settings));
        Assert.IsAssignableFrom<IGridSnappingTool>(new RectangleSelectTool(Settings));
        Assert.False(new VectorPencilTool(Settings) is IGridSnappingTool);   // freehand stays free
    }

    [Fact]
    public void Moving_with_the_select_tool_puts_the_edges_of_the_box_on_the_grid()
    {
        var doc = Open("<rect id='r' x='23' y='34' width='40' height='30' fill='#f00'/>");
        Settings.SnapToObjects = false;
        Settings.SnapToGrid = true;
        Settings.GridSize = 10;
        doc.Selection.Set(El(doc, "r"));
        var select = new VectorSelectTool(Settings);
        Drag(select, doc, 40, 50, 46, 54);                    // a move by (6, 4): the box would sit at 29, 38
        var box = Box(doc, "r");
        Assert.Equal((30, 40), (box.X, box.Y));               // its left and top edges moved to the nearest lines

        Settings.SnapToGrid = false;
        doc.History.Undo();
        Drag(select, doc, 40, 50, 46, 54);
        Assert.Equal((29, 38), (Box(doc, "r").X, Box(doc, "r").Y));   // without the grid the move is exact
    }

    [Fact]
    public void Resizing_with_the_select_tool_snaps_the_dragged_handle()
    {
        var doc = Open("<rect id='r' x='20' y='20' width='40' height='40' fill='#f00'/>");
        Settings.SnapToObjects = false;
        Settings.SnapToGrid = true;
        Settings.GridSize = 10;
        doc.Selection.Set(El(doc, "r"));
        var select = new VectorSelectTool(Settings);
        Drag(select, doc, 60, 60, 83, 77);                    // the bottom-right handle to (83, 77) → (80, 80)
        var box = Box(doc, "r");
        Assert.Equal((20, 20, 60, 60), (box.X, box.Y, box.Width, box.Height));
    }
}
