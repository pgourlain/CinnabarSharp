using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class VectorGradientEyedropperTests : VectorToolTestBase
{
    private const string Body = "<rect id='r' x='20' y='20' width='100' height='60' fill='#ff0000' stroke='#0000ff' stroke-width='4'/>" +
        "<circle id='c' cx='150' cy='150' r='30' fill='#00ff00' fill-opacity='0.5'/>";

    private SvgDocument Setup()
    {
        var doc = Open(Body);
        doc.Selection.Set(El(doc, "r"));
        return doc;
    }

    [Fact]
    public void Dragging_creates_a_linear_gradient_in_one_step_shown_live()
    {
        var doc = Setup();
        var tool = new VectorGradientTool(Settings);
        var steps = Steps(doc);
        tool.OnPointerDown(doc, At(20, 50));
        tool.OnPointerMove(doc, At(60, 50));
        Assert.StartsWith("url(#preview-", El(doc, "r").GetAttribute("fill"));
        Assert.Equal(steps, Steps(doc));
        tool.OnPointerMove(doc, At(120, 50));
        tool.OnPointerUp(doc, At(120, 50));

        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Gradient Fill", doc.History.Items[^1].Text);
        var gradient = Assert.IsType<SvgLinearGradient>(doc.Root.FindById(StyleResolver.ComputeFor(El(doc, "r")).Fill.Id));
        Assert.Equal(GradientUnits.UserSpaceOnUse, gradient.Units);
        Assert.Equal((20, 50, 120, 50), (gradient.X1, gradient.Y1, gradient.X2, gradient.Y2));
        Assert.Equal(2, gradient.ResolvedStops().Count);
        Assert.DoesNotContain(doc.Root.Descendants().OfType<SvgElement>(), e => e.Id?.StartsWith("preview-") == true);
        doc.History.Undo();
        Assert.Equal("#ff0000", El(doc, "r").GetAttribute("fill"));
        Assert.Empty(doc.Root.Descendants().OfType<SvgLinearGradient>());
    }

    [Fact]
    public void Options_choose_radial_stroke_and_the_stop_colors()
    {
        var doc = Setup();
        Settings.GradientKind = GradientKind.Radial;
        Settings.GradientOnStroke = true;
        Settings.GradientTransparency = false;
        Settings.PrimaryColor = ColorBgra.FromBgra(0, 0, 255, 255);
        Settings.SecondaryColor = ColorBgra.FromBgra(255, 0, 0, 255);
        Drag(new VectorGradientTool(Settings), doc, 70, 50, 120, 50);
        var radial = Assert.IsType<SvgRadialGradient>(doc.Root.FindById(StyleResolver.ComputeFor(El(doc, "r")).Stroke.Id));
        Assert.Equal((70, 50, 50), (radial.Cx, radial.Cy, radial.R));
        Assert.Equal([VColor.FromRgb(255, 0, 0), VColor.FromRgb(0, 0, 255)], radial.ResolvedStops().Select(s => s.Color));
        Assert.Equal("#ff0000", El(doc, "r").GetAttribute("fill"));       // the fill is untouched
    }

    [Fact]
    public void Gradients_follow_the_objects_own_coordinates()
    {
        var doc = Open("<g transform='translate(100 0)'><rect id='r' x='0' y='0' width='50' height='50' fill='red'/></g>");
        doc.Selection.Set(El(doc, "r"));
        Drag(new VectorGradientTool(Settings), doc, 100, 25, 150, 25);
        var gradient = (SvgLinearGradient)doc.Root.FindById(StyleResolver.ComputeFor(El(doc, "r")).Fill.Id)!;
        Assert.Equal((0, 50), (gradient.X1, gradient.X2));
    }

    [Fact]
    public void Handles_of_an_existing_gradient_are_dragged_in_one_step()
    {
        var doc = Setup();
        var tool = new VectorGradientTool(Settings);
        Drag(tool, doc, 20, 50, 120, 50);
        var overlay = Assert.IsType<ToolOverlay>(tool.GetOverlay(doc));
        Assert.Equal(4, overlay.Handles.Count);                           // start, end and two stops
        var steps = Steps(doc);
        Drag(tool, doc, 120, 50, 100, 70);
        Assert.Equal(steps + 1, Steps(doc));
        var gradient = (SvgLinearGradient)doc.Root.FindById(StyleResolver.ComputeFor(El(doc, "r")).Fill.Id)!;
        Assert.Equal((100, 70), (gradient.X2, gradient.Y2));
        doc.History.Undo();
        Assert.Equal(120, gradient.X2);
    }

    [Fact]
    public void Stops_are_dragged_added_and_removed_and_radial_has_a_focus_handle()
    {
        var doc = Setup();
        var tool = new VectorGradientTool(Settings);
        Drag(tool, doc, 20, 50, 120, 50);
        var gradient = (SvgLinearGradient)doc.Root.FindById(StyleResolver.ComputeFor(El(doc, "r")).Fill.Id)!;
        Drag(tool, doc, 120, 50, 120, 50);                                // a click on the end: no change
        // The end stop sits on the end handle: move the first stop (on the start handle) is a handle drag; add a middle stop first.
        tool.OnPointerDown(doc, At(70, 50, clicks: 2));
        tool.OnPointerUp(doc, At(70, 50));
        Assert.Equal(3, gradient.ResolvedStops().Count);
        Assert.Equal(0.5, gradient.ResolvedStops()[1].Offset, 3);
        // Drag that stop along the line.
        Drag(tool, doc, 70, 50, 95, 50);
        Assert.Equal(0.75, gradient.ResolvedStops()[1].Offset, 3);
        var steps = Steps(doc);
        Assert.True(tool.OnKeyDown(doc, ToolKey.Delete, ToolModifiers.None));
        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal(2, gradient.ResolvedStops().Count);

        Settings.GradientKind = GradientKind.Radial;
        doc.Selection.Set(El(doc, "c"));
        Drag(tool, doc, 150, 150, 180, 150);
        var overlay = tool.GetOverlay(doc)!;
        Assert.Equal(5, overlay.Handles.Count);                           // center, radius, focus and two stops
    }

    [Fact]
    public void Nothing_selected_means_nothing_to_draw_and_escape_cancels()
    {
        var doc = Open(Body);
        var tool = new VectorGradientTool(Settings);
        var steps = Steps(doc);
        Drag(tool, doc, 20, 50, 120, 50);
        Assert.Equal(steps, Steps(doc));
        Assert.Null(tool.GetOverlay(doc));

        doc.Selection.Set(El(doc, "r"));
        var xml = Xml(doc);
        tool.OnPointerDown(doc, At(20, 50));
        tool.OnPointerMove(doc, At(120, 50));
        Assert.True(tool.OnKeyDown(doc, ToolKey.Escape, ToolModifiers.None));
        tool.OnPointerUp(doc, At(120, 50));
        Assert.Equal(xml, Xml(doc));
    }

    [Fact]
    public void Eyedropper_takes_stroke_and_fill_or_the_rendered_pixel()
    {
        var doc = Open(Body);
        var tool = new VectorEyedropperTool(Settings);
        var steps = Steps(doc);
        Click(tool, doc, 70, 50);
        Assert.Equal((0, 0, 255), (Settings.PrimaryColor.R, Settings.PrimaryColor.G, Settings.PrimaryColor.B));      // stroke blue
        Assert.Equal((255, 0, 0), (Settings.SecondaryColor.R, Settings.SecondaryColor.G, Settings.SecondaryColor.B)); // fill red
        Click(tool, doc, 150, 150);
        Assert.Equal(128, Settings.SecondaryColor.A);                                                             // fill opacity kept
        Assert.Equal((0, 0, 255), (Settings.PrimaryColor.R, Settings.PrimaryColor.G, Settings.PrimaryColor.B));      // no stroke: unchanged
        Click(tool, doc, 70, 50, ToolModifiers.Shift);
        Assert.Equal((255, 0, 0), (Settings.PrimaryColor.R, Settings.PrimaryColor.G, Settings.PrimaryColor.B));
        Click(tool, doc, 190, 10, ToolModifiers.Shift);                                                          // empty: transparent
        Assert.Equal(0, Settings.PrimaryColor.A);
        Assert.Equal(steps, Steps(doc));
    }
}
