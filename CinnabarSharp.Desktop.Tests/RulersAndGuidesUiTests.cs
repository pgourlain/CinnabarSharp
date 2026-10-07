using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.Controls;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Desktop.Tests;

public sealed class RulersAndGuidesUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private SvgDocument Svg => Assert.IsType<SvgDocument>(Vm.ActiveDocument!.Document);

    private RulerView RulerTop => _h.Window.FindControl<RulerView>("RulerTop")!;

    private RulerView RulerLeft => _h.Window.FindControl<RulerView>("RulerLeft")!;

    private void NewDrawing()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(200, 150), ColorBgra.Transparent, new SvgDrawingOptions(200, 150, SvgUnit.Px)));
        Dispatcher.UIThread.RunJobs();
    }

    private static ToolPointer P(double x, double y) => new(new PointD(x, y), ToolButton.Left, ToolModifiers.None);

    [AvaloniaFact]
    public void The_ruler_labels_get_further_apart_as_the_zoom_grows_but_stay_readable()
    {
        Assert.Equal(100, RulerView.StepFor(1));          // 100 px per label at 100 %
        Assert.Equal(20, RulerView.StepFor(4));
        Assert.Equal(1, RulerView.StepFor(100));
        Assert.Equal(500, RulerView.StepFor(0.125));
        Assert.All(new[] { 0.01, 0.1, 0.5, 1, 3, 16, 64 }, z => Assert.True(RulerView.StepFor(z) * z >= 60 || RulerView.StepFor(z) >= 100000));
    }

    [AvaloniaFact]
    public void The_rulers_are_hidden_until_asked_for_and_guides_show_only_with_them()
    {
        NewDrawing();
        Assert.False(RulerTop.IsVisible);
        Svg.Workspace.Guides.Add(GuideOrientation.Vertical, 50);
        Assert.Null(Vm.Guides);                              // not shown without the rulers
        Assert.True(Vm.ClearGuidesCommand.CanExecute(null));

        Vm.ToggleRulersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(RulerTop.IsVisible && RulerLeft.IsVisible);
        Assert.Equal([new GuideLine(GuideOrientation.Vertical, 50)], Vm.Guides);
        Assert.Equal(1, RulerTop.Scale);
        var frame = _h.Capture("svg-100-rulers");
        var canvasAt = _h.Canvas.TranslatePoint(new Point(50.5, 75), _h.Window)!.Value;   // on the guide at x = 50
        var (r, g, b) = TestHarness.PixelAt(frame, canvasAt);
        Assert.True(b > 200 && g > 120 && r < 90, $"the guide is not cyan: {r},{g},{b}");

        Vm.ClearGuidesCommand.Execute(null);
        Assert.Null(Vm.Guides);
        Assert.False(Vm.ClearGuidesCommand.CanExecute(null));
        Assert.True(Vm.CaptureSettings(new CinnabarSharp.Desktop.Services.AppSettings()).ShowRulers);
    }

    [AvaloniaFact]
    public void Dragging_from_a_ruler_makes_a_guide_and_dropping_it_back_on_the_ruler_deletes_it()
    {
        NewDrawing();
        Vm.ToggleRulersCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var canvasOrigin = _h.Canvas.TranslatePoint(default, _h.Window)!.Value;

        // Press on the top ruler, move onto the picture 40 pixels down, release: a horizontal guide at y = 40.
        var press = RulerTop.TranslatePoint(new Point(canvasOrigin.X - RulerTop.TranslatePoint(default, _h.Window)!.Value.X + 30, 10), _h.Window)!.Value;
        _h.Window.MouseDown(press, MouseButton.Left);
        _h.Window.MouseMove(new Point(canvasOrigin.X + 30, canvasOrigin.Y + 40));
        _h.Window.MouseUp(new Point(canvasOrigin.X + 30, canvasOrigin.Y + 40), MouseButton.Left);
        var guide = Assert.Single(Svg.Workspace.Guides.Items);
        Assert.Equal(GuideOrientation.Horizontal, guide.Orientation);
        Assert.Equal(40, guide.Position, 1);

        // The same from the left ruler gives a vertical one; letting go on the ruler again removes a guide.
        var pressLeft = RulerLeft.TranslatePoint(new Point(10, canvasOrigin.Y - RulerLeft.TranslatePoint(default, _h.Window)!.Value.Y + 20), _h.Window)!.Value;
        _h.Window.MouseDown(pressLeft, MouseButton.Left);
        _h.Window.MouseMove(new Point(canvasOrigin.X + 70, canvasOrigin.Y + 20));
        _h.Window.MouseMove(pressLeft);
        _h.Window.MouseUp(pressLeft, MouseButton.Left);
        Assert.Single(Svg.Workspace.Guides.Items);            // the second one was dropped back and deleted
    }

    [AvaloniaFact]
    public void Drawing_tools_snap_to_a_guide_and_the_grid_gives_way_on_that_axis()
    {
        NewDrawing();
        Svg.Workspace.Guides.Add(GuideOrientation.Vertical, 43);
        Vm.SelectedTool = Vm.VectorTools.First(t => t.Name == "Rectangle");
        Vm.ShapeStyle = ShapeStyle.Fill;

        Vm.ToolPointerDown(P(45, 27));                          // 2 px from the guide at x = 43
        Vm.ToolPointerMove(P(98, 64));
        Vm.ToolPointerUp(P(98, 64));
        var rect = Svg.Root.Descendants().OfType<SvgRect>().Single();
        Assert.Equal(43, rect.X, 1e-6);                         // on the guide
        Assert.Equal(27, rect.Y, 1e-6);                         // free elsewhere

        Vm.UndoCommand.Execute(null);
        Vm.GridSize = 10;
        Vm.SnapToGrid = true;
        Vm.ToolPointerDown(P(45, 27));
        Vm.ToolPointerMove(P(98, 64));
        Vm.ToolPointerUp(P(98, 64));
        rect = Svg.Root.Descendants().OfType<SvgRect>().Single();
        Assert.Equal((43, 30, 57, 30), (rect.X, rect.Y, rect.Width, rect.Height));   // x from the guide; y and the far corner (100, 60) on the grid

        Vm.SnapToGuides = false;
        Vm.UndoCommand.Execute(null);
        Vm.ToolPointerDown(P(45, 27));
        Vm.ToolPointerMove(P(98, 64));
        Vm.ToolPointerUp(P(98, 64));
        Assert.Equal(50, Svg.Root.Descendants().OfType<SvgRect>().Single().X);   // guides off: the grid alone
    }
}
