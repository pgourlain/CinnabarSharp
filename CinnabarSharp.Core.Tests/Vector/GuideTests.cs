using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class GuideTests : VectorToolTestBase
{
    [Fact]
    public void Guides_are_added_moved_removed_and_every_change_is_announced()
    {
        var doc = Open("");
        var guides = doc.Workspace.Guides;
        var changes = 0;
        guides.Changed += () => changes++;

        var vertical = guides.Add(GuideOrientation.Vertical, 50);
        var horizontal = guides.Add(GuideOrientation.Horizontal, 80);
        guides.Move(vertical, 60);
        guides.Move(vertical, 60);                                // same place: no change
        Assert.Equal(60, vertical.Position);
        Assert.Equal(3, changes);
        Assert.Equal(60, guides.Snap(GuideOrientation.Vertical, 63, 5));
        Assert.Null(guides.Snap(GuideOrientation.Vertical, 70, 5));
        Assert.Null(guides.Snap(GuideOrientation.Horizontal, 62, 5));      // a vertical guide does not catch a y
        Assert.Same(horizontal, guides.Nearest(GuideOrientation.Horizontal, 78, 3));

        guides.Remove(horizontal);
        guides.Clear();
        Assert.Equal(0, guides.Count);
        Assert.Equal(5, changes);
        guides.Clear();
        Assert.Equal(5, changes);                                  // nothing to clear: nothing announced
    }

    [Fact]
    public void Moving_with_the_select_tool_snaps_the_box_to_a_guide()
    {
        var doc = Open("<rect id='r' x='23' y='34' width='40' height='30' fill='#f00'/>");
        Settings.SnapToObjects = false;
        doc.Workspace.Guides.Add(GuideOrientation.Vertical, 100);
        doc.Workspace.Guides.Add(GuideOrientation.Horizontal, 150);
        doc.Selection.Set(El(doc, "r"));
        var select = new VectorSelectTool(Settings);
        Drag(select, doc, 40, 50, 117, 128);                      // the box would sit at x = 23 + 77 = 100: its left edge on the guide
        var box = Box(doc, "r");
        Assert.Equal(100, box.X, 1e-9);
        Assert.Equal(34 + 78, box.Y, 1e-9);                       // too far from the horizontal guide: not snapped (112 vs 150)

        doc.History.Undo();
        Drag(select, doc, 40, 50, 112, 168);                      // the bottom edge would be at 34+118+30 = 182; the top at 152, near 150
        Assert.Equal(150, Box(doc, "r").Y, 1e-9);
        Settings.SnapToGuides = false;
        doc.History.Undo();
        Drag(select, doc, 40, 50, 117, 128);
        Assert.Equal(100, Box(doc, "r").X, 1e-9);                 // same drag, no snapping needed here
        doc.History.Undo();
        Drag(select, doc, 40, 50, 115, 128);
        Assert.Equal(98, Box(doc, "r").X, 1e-9);                  // guides off: exact
    }
}
