using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class ViewportTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private void NewImage(int w, int h)
    {
        _h.Vm.CreateImage(new NewImageOptions(new ImageSize(w, h), ColorBgra.White));
        Dispatcher.UIThread.RunJobs();
    }

    private Point ViewportCenter() => _h.Scroller.TranslatePoint(
        new Point(_h.Scroller.Viewport.Width / 2, _h.Scroller.Viewport.Height / 2), _h.Window)!.Value;

    [AvaloniaFact]
    public void Ctrl_wheel_zooms_around_the_mouse()
    {
        NewImage(1200, 900);
        _h.Vm.ActualSizeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var mouse = ViewportCenter() + new Point(-200, 120);
        var before = _h.WindowToImage(mouse);

        for (var i = 0; i < 3; i++)
            _h.Window.MouseWheel(mouse, new Vector(0, 1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("200%", _h.Vm.ZoomText);
        var after = _h.WindowToImage(mouse);
        Assert.InRange(after.X, before.X - 1, before.X + 1);
        Assert.InRange(after.Y, before.Y - 1, before.Y + 1);
        _h.Capture("12-wheel-zoom-around-mouse");
    }

    [AvaloniaFact]
    public void Wheel_without_modifier_scrolls_instead_of_zooming()
    {
        NewImage(3000, 3000);
        _h.Vm.ActualSizeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        _h.Window.MouseWheel(_h.CanvasToWindow(10, 10), new Vector(0, -1), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("100%", _h.Vm.ZoomText);
        Assert.True(_h.Scroller.Offset.Y > 0);
    }

    [AvaloniaFact]
    public void Zoom_in_command_keeps_view_center()
    {
        NewImage(400, 300);
        var center = ViewportCenter();
        var before = _h.WindowToImage(center);

        for (var i = 0; i < 6; i++)
            _h.Vm.ZoomInCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("500%", _h.Vm.ZoomText);
        var after = _h.WindowToImage(center);
        Assert.InRange(after.X, before.X - 1, before.X + 1);
        Assert.InRange(after.Y, before.Y - 1, before.Y + 1);
    }

    [AvaloniaFact]
    public void Middle_button_drag_pans()
    {
        NewImage(3000, 3000);
        _h.Vm.ActualSizeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var start = _h.Scroller.Offset;
        var p = ViewportCenter();

        _h.Window.MouseDown(p, MouseButton.Middle);
        _h.Window.MouseMove(p - new Point(100, 50), RawInputModifiers.MiddleMouseButton);
        _h.Window.MouseUp(p - new Point(100, 50), MouseButton.Middle);

        Assert.Equal(start + new Vector(100, 50), _h.Scroller.Offset);
    }

    [AvaloniaFact]
    public void Pan_tool_drag_pans_with_left_button()
    {
        NewImage(3000, 3000);
        _h.Vm.ActualSizeCommand.Execute(null);
        _h.Vm.SelectedTool = _h.Vm.Tools.First(t => t.Name == "Pan");
        Dispatcher.UIThread.RunJobs();
        var start = _h.Scroller.Offset;
        var p = ViewportCenter();

        _h.Window.MouseDown(p, MouseButton.Left);
        _h.Window.MouseMove(p - new Point(40, 30), RawInputModifiers.LeftMouseButton);
        _h.Window.MouseUp(p - new Point(40, 30), MouseButton.Left);

        Assert.Equal(start + new Vector(40, 30), _h.Scroller.Offset);
    }
}
