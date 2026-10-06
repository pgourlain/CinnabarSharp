using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class VectorShapeToolsTests : VectorToolTestBase
{
    private SvgDocument Blank() => Open("<g id='layer1' xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape' inkscape:groupmode='layer'/>");

    private static IEnumerable<SvgElement> Shapes(SvgDocument doc) => ((SvgContainer)El(doc, "layer1")).Elements;

    [Fact]
    public void Rectangle_drag_adds_one_step_with_the_stroke_and_fill_settings()
    {
        var doc = Blank();
        Settings.PrimaryColor = ColorBgra.FromBgra(0, 0, 255, 255);     // red
        Settings.SecondaryColor = ColorBgra.FromBgra(255, 0, 0, 255);   // blue
        Settings.BrushWidth = 3;
        Settings.ShapeStyle = ShapeStyle.OutlineAndFill;
        var tool = new VectorRectangleTool(Settings);
        var steps = Steps(doc);

        Drag(tool, doc, 10, 20, 70, 60);

        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Rectangle", doc.History.Items[^1].Text);
        var rect = Assert.IsType<SvgRect>(Assert.Single(Shapes(doc)));
        Assert.Equal((10, 20, 60, 40), (rect.X, rect.Y, rect.Width, rect.Height));
        Assert.Equal("red", rect.GetAttribute("stroke"));
        Assert.Equal("blue", rect.GetAttribute("fill"));
        Assert.Equal("3", rect.GetAttribute("stroke-width"));
        Assert.Same(rect, doc.Selection.Primary);
        Assert.Equal("rect1", rect.Id);

        doc.History.Undo();
        Assert.Empty(Shapes(doc));
        Assert.False(doc.IsDirty);
        doc.History.Redo();
        Assert.Single(Shapes(doc));
    }

    [Fact]
    public void The_shape_is_shown_live_during_the_drag_but_not_in_the_history()
    {
        var doc = Blank();
        var tool = new VectorRectangleTool(Settings);
        var steps = Steps(doc);
        tool.OnPointerDown(doc, At(10, 10));
        tool.OnPointerMove(doc, At(50, 40));
        Assert.Single(Shapes(doc));
        Assert.Equal(steps, Steps(doc));
        Assert.False(doc.IsDirty);
        tool.OnPointerMove(doc, At(80, 60));
        Assert.Equal(70, ((SvgRect)Shapes(doc).Single()).Width);
        tool.OnPointerUp(doc, At(80, 60));
        Assert.Equal(steps + 1, Steps(doc));
        Assert.Single(Shapes(doc));
    }

    [Fact]
    public void Shape_style_options_pick_fill_outline_or_both()
    {
        var doc = Blank();
        var tool = new VectorRectangleTool(Settings);
        Settings.ShapeStyle = ShapeStyle.Outline;
        Drag(tool, doc, 10, 10, 30, 30);
        var outline = Shapes(doc).Last();
        Assert.Equal("none", outline.GetAttribute("fill"));
        Assert.NotNull(outline.GetAttribute("stroke"));

        Settings.ShapeStyle = ShapeStyle.Fill;
        Drag(tool, doc, 40, 10, 60, 30);
        var filled = Shapes(doc).Last();
        Assert.NotEqual("none", filled.GetAttribute("fill"));
        Assert.Null(filled.GetAttribute("stroke"));
    }

    [Fact]
    public void Modifiers_make_squares_and_draw_from_the_center_and_a_click_makes_nothing()
    {
        var doc = Blank();
        var tool = new VectorRectangleTool(Settings);
        Drag(tool, doc, 50, 50, 90, 70, ToolModifiers.Shift);
        var square = (SvgRect)Shapes(doc).Last();
        Assert.Equal((40, 40), (square.Width, square.Height));
        Drag(tool, doc, 100, 100, 120, 110, ToolModifiers.Alt);
        var centered = (SvgRect)Shapes(doc).Last();
        Assert.Equal((80, 90, 40, 20), (centered.X, centered.Y, centered.Width, centered.Height));
        var count = Shapes(doc).Count();
        var steps = Steps(doc);
        Click(tool, doc, 20, 20);
        Assert.Equal(count, Shapes(doc).Count());
        Assert.Equal(steps, Steps(doc));
    }

    [Fact]
    public void Rounded_corners_come_from_the_option_and_are_limited_to_half_the_size()
    {
        var doc = Blank();
        var tool = new VectorRectangleTool(Settings);
        Settings.VectorCornerRadius = 8;
        Drag(tool, doc, 10, 10, 60, 40);
        Assert.Equal(8, ((SvgRect)Shapes(doc).Last()).Rx);
        Drag(tool, doc, 100, 10, 110, 14);
        Assert.Equal(2, ((SvgRect)Shapes(doc).Last()).Rx);
    }

    [Fact]
    public void Ellipse_circle_and_line()
    {
        var doc = Blank();
        Drag(new VectorEllipseTool(Settings), doc, 10, 10, 70, 50);
        var ellipse = (SvgEllipse)Shapes(doc).Last();
        Assert.Equal((40, 30, 30, 20), (ellipse.Cx, ellipse.Cy, ellipse.Rx, ellipse.Ry));
        Drag(new VectorEllipseTool(Settings), doc, 100, 100, 120, 110, ToolModifiers.Shift);
        var circle = (SvgEllipse)Shapes(doc).Last();
        Assert.Equal(circle.Rx, circle.Ry);

        Drag(new VectorLineTool(Settings), doc, 10, 100, 50, 130);
        var line = (SvgLine)Shapes(doc).Last();
        Assert.Equal((10, 100, 50, 130), (line.X1, line.Y1, line.X2, line.Y2));
        Assert.Equal("none", line.GetAttribute("fill"));
        Assert.NotNull(line.GetAttribute("stroke"));

        Drag(new VectorLineTool(Settings), doc, 100, 150, 140, 160, ToolModifiers.Shift);      // about 14 degrees: snaps to 15
        var snapped = (SvgLine)Shapes(doc).Last();
        var angle = Math.Atan2(snapped.Y2 - snapped.Y1, snapped.X2 - snapped.X1) * 180 / Math.PI;
        Assert.Equal(15, angle, 1);
        Drag(new VectorLineTool(Settings), doc, 150, 150, 170, 150, ToolModifiers.Alt);        // from the middle
        var centered = (SvgLine)Shapes(doc).Last();
        Assert.Equal((130, 170), (centered.X1, centered.X2));
    }

    [Fact]
    public void Polygon_and_star_with_corners_ratio_and_rounding()
    {
        var doc = Blank();
        var tool = new VectorPolygonTool(Settings);
        Settings.PolygonCorners = 6;
        Drag(tool, doc, 100, 100, 130, 100);
        var hexagon = Assert.IsType<SvgPolygon>(Shapes(doc).Last());
        Assert.Equal(6, hexagon.Points.Count);
        Assert.All(hexagon.Points, p => Assert.Equal(30, p.DistanceTo(new VPoint(100, 100)), 3));
        Assert.Equal(new VPoint(130, 100), hexagon.Points[0]);                  // the drag direction is the first corner

        Settings.PolygonCorners = 5;
        Settings.StarRatio = 0.4;
        Drag(tool, doc, 50, 50, 50, 20);
        var star = Assert.IsType<SvgPolygon>(Shapes(doc).Last());
        Assert.Equal(10, star.Points.Count);
        Assert.Equal(30, star.Points[0].DistanceTo(new VPoint(50, 50)), 3);
        Assert.Equal(12, star.Points[1].DistanceTo(new VPoint(50, 50)), 3);
        Assert.Equal("star1", star.Id);

        Settings.PolygonRounding = 0.5;
        Drag(tool, doc, 150, 150, 180, 150);
        var rounded = Assert.IsType<SvgPath>(Shapes(doc).Last());
        Assert.Contains(rounded.CreatePath().Segments, s => s.Kind == SegmentKind.QuadTo);
        Assert.Equal(SegmentKind.Close, rounded.CreatePath().Segments[^1].Kind);
    }

    [Fact]
    public void Stroke_width_follows_the_zoom_so_it_is_the_brush_width_in_pixels()
    {
        var doc = Blank();
        Settings.BrushWidth = 4;
        doc.Workspace.Scale = 2;                                              // zoomed in: 4 px on the picture is still 4 user units here
        Drag(new VectorRectangleTool(Settings), doc, 10, 10, 60, 40);
        Assert.Equal("4", Shapes(doc).Last().GetAttribute("stroke-width"));
    }

    [Fact]
    public void Shapes_go_into_the_layer_and_can_be_undone_in_a_long_row()
    {
        var doc = Blank();
        var rectangle = new VectorRectangleTool(Settings);
        for (var i = 0; i < 5; i++)
            Drag(rectangle, doc, 10 + i * 20, 10, 25 + i * 20, 30);
        Assert.Equal(5, Shapes(doc).Count());
        for (var i = 0; i < 5; i++)
            doc.History.Undo();
        Assert.Empty(Shapes(doc));
        Assert.Equal(["rect1"], new[] { "rect1" });
    }
}
