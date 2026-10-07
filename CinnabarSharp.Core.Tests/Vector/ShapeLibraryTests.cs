using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class ShapeLibraryTests : VectorToolTestBase
{
    private static double Area(VectorPath path)
    {
        var total = 0.0;
        foreach (var polyline in Flattener.Flatten(path, Matrix2D.Identity, 0.001))
        {
            var p = polyline.Points;
            for (var i = 0; i < p.Count; i++)
                total += p[i].X * p[(i + 1) % p.Count].Y - p[(i + 1) % p.Count].X * p[i].Y;
        }
        return Math.Abs(total / 2);
    }

    [Fact]
    public void The_library_has_plenty_of_shapes_in_every_category_with_unique_ids()
    {
        Assert.True(ShapeLibrary.All.Count >= 100, $"only {ShapeLibrary.All.Count} shapes");
        Assert.All(ShapeLibrary.Categories, c => Assert.True(ShapeLibrary.In(c).Count() >= 8, c));
        Assert.Equal(ShapeLibrary.All.Count, ShapeLibrary.All.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(ShapeLibrary.All, s => Assert.Contains(s.Category, ShapeLibrary.Categories));
        Assert.Equal("basic/star", ShapeLibrary.Default.Id);
        Assert.Same(ShapeLibrary.Default, ShapeLibrary.Find("basic/star"));
        Assert.Null(ShapeLibrary.Find("nothing/here"));
    }

    [Fact]
    public void Every_shape_builds_to_a_filled_outline_in_the_unit_square()
    {
        foreach (var shape in ShapeLibrary.All)
        {
            var bounds = shape.UnitPath.Bounds;
            Assert.True(Math.Abs(bounds.X) < 1e-6 && Math.Abs(bounds.Y) < 1e-6 && Math.Abs(bounds.Width - 1) < 1e-6 && Math.Abs(bounds.Height - 1) < 1e-6,
                $"{shape.Id}: {bounds}");
            Assert.True(Area(shape.UnitPath) > 0.05, $"{shape.Id} fills almost nothing");
            Assert.Contains("M", shape.PreviewData);
            Assert.DoesNotContain("NaN", shape.PreviewData);
        }
    }

    [Fact]
    public void A_shape_is_stretched_to_the_box()
    {
        var path = ShapeLibrary.Find("basic/triangle")!.Place(new VRect(10, 20, 80, 40));
        var b = path.Bounds;
        Assert.Equal((10, 20, 80, 40), (Math.Round(b.X, 6), Math.Round(b.Y, 6), Math.Round(b.Width, 6), Math.Round(b.Height, 6)));
    }

    [Fact]
    public void The_shape_tool_draws_the_chosen_shape_in_one_step()
    {
        var doc = Open("");
        Settings.LibraryShape = "arrows/right-arrow";
        var steps = Steps(doc);
        Drag(new VectorLibraryShapeTool(Settings), doc, 20, 30, 120, 80);

        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Shape", doc.History.Items[^1].Text);
        var path = Assert.IsType<SvgPath>(Assert.Single(doc.Root.Descendants().OfType<SvgPath>()));
        var b = path.CreatePath().Bounds;
        Assert.Equal((20, 30, 100, 50), (Math.Round(b.X, 2), Math.Round(b.Y, 2), Math.Round(b.Width, 2), Math.Round(b.Height, 2)));
        Assert.Contains(path, doc.Selection.Nodes);
        doc.History.Undo();
        Assert.Empty(doc.Root.Descendants().OfType<SvgPath>());
    }

    [Fact]
    public void Shift_keeps_the_proportions_and_the_grid_marker_applies()
    {
        var doc = Open("");
        Settings.LibraryShape = "basic/heart";
        Drag(new VectorLibraryShapeTool(Settings), doc, 20, 20, 120, 50, ToolModifiers.Shift);
        var b = doc.Root.Descendants().OfType<SvgPath>().Single().CreatePath().Bounds;
        Assert.Equal(b.Width, b.Height, 0.01);
        Assert.IsAssignableFrom<IGridSnappingTool>(new VectorLibraryShapeTool(Settings));
    }
}
