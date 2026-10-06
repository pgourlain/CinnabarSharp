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
            _h.Window.MouseWheel(mouse, new Avalonia.Vector(0, 1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("200%", _h.Vm.ZoomText);
        var after = _h.WindowToImage(mouse);
        Assert.InRange(after.X, before.X - 1, before.X + 1);
        Assert.InRange(after.Y, before.Y - 1, before.Y + 1);
        _h.Capture("12-wheel-zoom-around-mouse");
    }

    // Regression: at 25 % a 1023-pixel-wide image is 255.75 pixels wide; the zoom used to be derived from the
    // truncated width (24.93 %), so Zoom In and Ctrl+wheel up went back to 25 % and never got past it.
    [AvaloniaFact]
    public void Zoom_in_works_from_every_zoom_level_with_odd_image_sizes()
    {
        NewImage(1023, 767);
        _h.Vm.ActualSizeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        for (var i = 0; i < 6; i++) // 75, 66.67, 50, 40, 30, 25
            _h.Vm.ZoomOutCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("25%", _h.Vm.ZoomText);

        _h.Vm.ZoomInCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("30%", _h.Vm.ZoomText);

        _h.Window.MouseWheel(ViewportCenter(), new Avalonia.Vector(0, 1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("40%", _h.Vm.ZoomText);

        for (var percent = 1.0; percent < 3200; percent = MainViewModel.NextZoomIn(percent))
        {
            _h.Vm.ActiveDocument!.Document.Workspace.Scale = percent / 100;
            Assert.True(MainViewModel.NextZoomIn(_h.Vm.CurrentZoomPercent) > percent, $"stuck at {percent}%");
        }
    }

    // Zoom scales the bitmap the canvas already has: re-flattening every layer at each wheel step made zoom slow
    // on big images.
    [AvaloniaFact]
    public void Zoom_redraws_without_recompositing()
    {
        _h.Vm.CreateImage(new NewImageOptions(new ImageSize(400, 300), ColorBgra.FromBgra(30, 90, 200, 255)));
        Dispatcher.UIThread.RunJobs();
        _h.Vm.ActualSizeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var renderVersion = _h.Vm.RenderVersion;

        _h.Vm.ZoomInCommand.Execute(null);
        _h.Window.MouseWheel(ViewportCenter(), new Avalonia.Vector(0, 1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(renderVersion, _h.Vm.RenderVersion);
        var scale = _h.Vm.ActiveDocument!.Document.Workspace.Scale;
        Assert.True(scale > 1);
        Assert.Equal(400 * scale, _h.Canvas.Bounds.Width, 0.5);
        var frame = _h.Capture("12-zoom-without-recompositing");
        Assert.Equal(((byte)200, (byte)90, (byte)30), TestHarness.PixelAt(frame, ViewportCenter()));
    }

    // The Cinnabar theme (tasks.md, Phase 12) has a dark variant: the workspace and the chrome follow it.
    [AvaloniaFact]
    public void Dark_theme_uses_the_dark_workspace_and_chrome()
    {
        NewImage(400, 300);
        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        try
        {
            var frame = _h.Capture("13-dark-theme");
            Assert.Equal(((byte)0x12, (byte)0x11, (byte)0x10), TestHarness.PixelAt(frame, _h.CanvasToWindow(-8, 150)));
            Assert.Equal(((byte)0x22, (byte)0x21, (byte)0x1F), TestHarness.PixelAt(frame, new Point(600, 20)));
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public void Wheel_without_modifier_scrolls_instead_of_zooming()
    {
        NewImage(3000, 3000);
        _h.Vm.ActualSizeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        _h.Window.MouseWheel(_h.CanvasToWindow(10, 10), new Avalonia.Vector(0, -1), RawInputModifiers.None);
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

        Assert.Equal(start + new Avalonia.Vector(100, 50), _h.Scroller.Offset);
    }

    [AvaloniaFact]
    public void Zoom_tool_left_click_zooms_in_and_right_click_zooms_out()
    {
        NewImage(400, 300);
        _h.Vm.SelectedTool = _h.Vm.Tools.First(t => t.Name == "Zoom");
        Dispatcher.UIThread.RunJobs();
        var p = _h.CanvasToWindow(100, 100);

        _h.Window.MouseDown(p, MouseButton.Left);
        _h.Window.MouseUp(p, MouseButton.Left);
        Assert.Equal("125%", _h.Vm.ZoomText);

        Dispatcher.UIThread.RunJobs();
        p = _h.CanvasToWindow(100, 100);
        _h.Window.MouseDown(p, MouseButton.Right);
        _h.Window.MouseUp(p, MouseButton.Right);
        Assert.Equal("100%", _h.Vm.ZoomText);
        Assert.Single(_h.Vm.History);
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

        Assert.Equal(start + new Avalonia.Vector(40, 30), _h.Scroller.Offset);
    }
}
