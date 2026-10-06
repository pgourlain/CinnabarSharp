using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class SelectionUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;
    private ImageDocument Doc => Vm.ActiveDocument!.Document;

    private void NewImage(int w = 300, int h = 200) =>
        Vm.CreateImage(new NewImageOptions(new ImageSize(w, h), ColorBgra.White));

    private void UseTool(string name) => Vm.SelectedTool = Vm.Tools.First(t => t.Name == name);

    private void Drag(double x1, double y1, double x2, double y2, RawInputModifiers mods = RawInputModifiers.None)
    {
        Dispatcher.UIThread.RunJobs();
        var a = _h.CanvasToWindow(x1, y1);
        var b = _h.CanvasToWindow(x2, y2);
        _h.Window.MouseDown(a, MouseButton.Left, mods);
        _h.Window.MouseMove(new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), mods | RawInputModifiers.LeftMouseButton);
        _h.Window.MouseMove(b, mods | RawInputModifiers.LeftMouseButton);
        _h.Window.MouseUp(b, MouseButton.Left, mods);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Dragging_rectangle_select_on_canvas_selects_and_draws_marching_ants()
    {
        NewImage();

        Drag(50, 40, 150, 120);

        Assert.Equal(new RectangleI(50, 40, 100, 80), Doc.Selection!.Bounds);
        Assert.Equal("Selection 100 × 80", Vm.SelectionSizeText);
        Assert.Equal(["New Image", "Rectangle Select"], Vm.History.Select(h => h.Text));
        Assert.True(Vm.DeselectAllCommand.CanExecute(null));

        var frame = _h.Capture("40-rectangle-selection");
        // Away from the resize handles (corners and middle of each edge).
        var edge = TestHarness.PixelAt(frame, _h.CanvasToWindow(75, 40));
        Assert.NotEqual((255, 255, 255), edge == (255, 255, 255) ? TestHarness.PixelAt(frame, _h.CanvasToWindow(79, 40)) : edge);
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(100, 80)));
    }

    [AvaloniaFact]
    public void Command_drag_adds_to_selection_and_click_deselects()
    {
        NewImage();
        Drag(10, 10, 50, 50);

        Drag(40, 40, 90, 90, RawInputModifiers.Control);
        Assert.Equal(new RectangleI(10, 10, 80, 80), Doc.Selection!.Bounds);

        Drag(20, 20, 20, 20);
        Assert.Null(Doc.Selection);
        Assert.Equal("", Vm.SelectionSizeText);
    }

    [AvaloniaFact]
    public void Ellipse_and_lasso_tools_select_on_canvas()
    {
        NewImage();
        UseTool("Ellipse Select");
        Drag(0, 0, 100, 60);
        Assert.Equal(new RectangleI(0, 0, 100, 60), Doc.Selection!.Bounds);
        Assert.False(Doc.Selection.Contains(1, 1));

        UseTool("Lasso Select");
        Dispatcher.UIThread.RunJobs();
        _h.Window.MouseDown(_h.CanvasToWindow(20, 20), MouseButton.Left);
        foreach (var (x, y) in new[] { (120, 30), (150, 150), (40, 120) })
            _h.Window.MouseMove(_h.CanvasToWindow(x, y), RawInputModifiers.LeftMouseButton);
        _h.Window.MouseUp(_h.CanvasToWindow(40, 120), MouseButton.Left);

        Assert.Equal(new RectangleI(20, 20, 130, 130), Doc.Selection!.Bounds);
        Assert.True(Doc.Selection.Contains(80, 80));
        Assert.False(Doc.Selection.Contains(25, 110));
        _h.Capture("41-lasso");
    }

    [AvaloniaFact]
    public void Magic_wand_click_selects_similar_area_and_shows_options()
    {
        NewImage();
        Doc.Actions.SelectAll();
        Vm.SelectedTool = Vm.Tools.First(t => t.Name == "Magic Wand");
        Assert.True(Vm.ShowToleranceOptions);
        Doc.SetSelection(SelectionMask.Rectangle(300, 200, new PointD(0, 0), new PointD(100, 200)));
        Vm.FillSelectionCommand.Execute(null);
        Doc.SetSelection(null);
        Dispatcher.UIThread.RunJobs();

        var p = _h.CanvasToWindow(200, 100);
        _h.Window.MouseDown(p, MouseButton.Left);
        _h.Window.MouseUp(p, MouseButton.Left);

        Assert.Equal(new RectangleI(100, 0, 200, 200), Doc.Selection!.Bounds);
        _h.Capture("42-magic-wand");
    }

    [AvaloniaFact]
    public void Move_selected_pixels_drags_content()
    {
        NewImage(100, 50);
        Drag(0, 0, 20, 20);
        Vm.PrimaryColor = Avalonia.Media.Colors.Red;
        Vm.FillSelectionCommand.Execute(null);

        UseTool("Move Selected Pixels");
        Drag(10, 10, 60, 30);

        Assert.Equal(new RectangleI(50, 20, 20, 20), Doc.Selection!.Bounds);
        var frame = _h.Capture("43-moved-pixels");
        Assert.Equal((255, 0, 0), TestHarness.PixelAt(frame, _h.CanvasToWindow(55, 25)));
        Assert.NotEqual((255, 0, 0), TestHarness.PixelAt(frame, _h.CanvasToWindow(5, 5)));
        Assert.Equal("Move Selected Pixels", Vm.History[^1].Text);
    }

    [AvaloniaFact]
    public void Crop_to_selection_resizes_image()
    {
        NewImage();
        Drag(50, 40, 150, 120);

        Vm.CropToSelectionCommand.Execute(null);

        Assert.Equal("100 × 80", Vm.ImageSizeText);
        Assert.False(Vm.CropToSelectionCommand.CanExecute(null));
        Vm.UndoCommand.Execute(null);
        Assert.Equal("300 × 200", Vm.ImageSizeText);
    }

    [AvaloniaFact]
    public async Task Copy_then_paste_into_new_layer_and_new_image()
    {
        NewImage();
        Drag(0, 0, 30, 20);
        Vm.PrimaryColor = Avalonia.Media.Colors.Blue;
        Vm.FillSelectionCommand.Execute(null);

        await Vm.CopyCommand.ExecuteAsync(null);
        Assert.Equal((30, 20), (_h.Clipboard.Image!.Width, _h.Clipboard.Image.Height));

        await Vm.PasteIntoNewLayerCommand.ExecuteAsync(null);
        Assert.Equal(2, Vm.Layers.Count);
        Assert.Equal("Move Selected Pixels", Vm.SelectedTool.Name);

        await Vm.PasteIntoNewImageCommand.ExecuteAsync(null);
        Assert.Equal(2, Vm.Documents.Count);
        Assert.Equal("30 × 20", Vm.ImageSizeText);
    }

    [AvaloniaFact]
    public async Task Paste_beside_puts_the_clipboard_image_next_to_the_image_in_a_new_layer()
    {
        NewImage(300, 200);
        var blue = ColorBgra.FromBgra(255, 80, 0, 255);
        _h.Clipboard.Image = new ClipboardImage(
            Enumerable.Range(0, 120 * 260).SelectMany(_ => new[] { blue.B, blue.G, blue.R, blue.A }).ToArray(), 120, 260);
        _h.Dialogs.PasteBesideAnswers.Enqueue(new PasteBesideOptions(PasteSide.Left, EdgeAlignment.End));

        await Vm.PasteBesideCommand.ExecuteAsync(null);

        Assert.Equal("420 × 260", Vm.ImageSizeText);
        Assert.Equal(2, Vm.Layers.Count);
        Assert.Equal("Move Selected Pixels", Vm.SelectedTool.Name);
        Assert.Equal("Paste Beside", Vm.History[^1].Text);
        var doc = Vm.ActiveDocument!.Document;
        Assert.Equal(new RectangleI(0, 0, 120, 260), doc.Selection!.Bounds);
        _h.Capture("42-paste-beside");

        // Canceling the dialog changes nothing.
        await Vm.PasteBesideCommand.ExecuteAsync(null);
        Assert.Equal("420 × 260", Vm.ImageSizeText);

        Vm.UndoCommand.Execute(null);
        Assert.Equal("300 × 200", Vm.ImageSizeText);
        Assert.Single(Vm.Layers);
    }

    [AvaloniaFact]
    public async Task Paste_with_empty_clipboard_tells_the_user()
    {
        NewImage();

        await Vm.PasteCommand.ExecuteAsync(null);

        Assert.Equal(["Nothing to paste"], _h.Dialogs.Errors);
    }

    // Paint.NET's floating paste: dragging a fresh paste must not leave a hole where it was.
    [AvaloniaFact]
    public async Task Dragging_a_fresh_paste_puts_back_the_pixels_under_it()
    {
        NewImage(120, 80);
        var red = ColorBgra.FromBgra(40, 30, 220, 255);
        _h.Clipboard.Image = new ClipboardImage(Enumerable.Repeat(new[] { red.B, red.G, red.R, red.A }, 20 * 20).SelectMany(p => p).ToArray(), 20, 20);

        await Vm.PasteCommand.ExecuteAsync(null);
        var at = Doc.Selection!.Bounds;
        Drag(at.X + 5, at.Y + 5, at.X + 65, at.Y + 5);

        var frame = _h.Capture("41-floating-paste");
        Assert.Equal("Move Selected Pixels", Vm.History[^1].Text);
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(at.X + 10, at.Y + 10)));
        Assert.Equal((220, 30, 40), TestHarness.PixelAt(frame, _h.CanvasToWindow(at.X + 70, at.Y + 10)));
    }

    [AvaloniaFact]
    public async Task Cut_clears_pixels_and_fills_clipboard()
    {
        NewImage(40, 20);
        Drag(0, 0, 10, 10);

        await Vm.CutCommand.ExecuteAsync(null);

        Assert.NotNull(_h.Clipboard.Image);
        Assert.Equal(0, Doc.Layers[0].Surface.ToBgra()[3]);
        Assert.Equal("Cut", Vm.History[^1].Text);
    }

    [AvaloniaFact]
    public void Clipboard_bitmaps_in_other_formats_become_straight_bgra()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(1, 1), new Avalonia.Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
            Marshal.Copy(new byte[] { 100, 50, 0, 128 }, 0, fb.Address, 4); // premultiplied RGBA

        var image = AvaloniaClipboardService.ToClipboardImage(bitmap);

        Assert.Equal((1, 1), (image.Width, image.Height));
        Assert.InRange(image.Bgra[2], 197, 201); // R = 100 / 0.5
        Assert.InRange(image.Bgra[1], 98, 101);
        Assert.Equal(0, image.Bgra[0]);
        Assert.Equal(128, image.Bgra[3]);
    }

    private void PressEscape()
    {
        _h.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Escape_deselects_and_can_be_undone()
    {
        NewImage();
        Drag(50, 40, 150, 120);

        PressEscape();
        Assert.Null(Doc.Selection);
        Assert.Equal("Deselect All", Vm.History[^1].Text);

        Vm.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new RectangleI(50, 40, 100, 80), Doc.Selection!.Bounds);
    }

    [AvaloniaFact]
    public void Escape_first_removes_the_crop_frame_then_deselects()
    {
        NewImage();
        Drag(50, 40, 150, 120);
        UseTool("Crop");
        Drag(10, 10, 170, 100);
        Assert.NotNull(Vm.Overlay?.Shade);

        PressEscape();
        Assert.Null(Vm.Overlay);
        Assert.NotNull(Doc.Selection);

        PressEscape();
        Assert.Null(Doc.Selection);
    }

    [AvaloniaFact]
    public void Selection_handles_resize_the_rectangle()
    {
        NewImage();
        Drag(50, 40, 150, 120);

        var handles = Vm.Overlay!.Handles;
        Assert.True(Vm.Overlay.SquareHandles);
        Assert.Equal(8, handles.Count);
        Assert.Contains(new PointD(150, 80), handles); // middle of the right edge

        // Hovering a handle shows a resize cursor.
        _h.Window.MouseMove(_h.CanvasToWindow(150, 80));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Core.Tools.ToolCursor.ResizeHorizontal, Vm.HoverCursor);
        _h.Window.MouseMove(_h.CanvasToWindow(100, 80));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Core.Tools.ToolCursor.Default, Vm.HoverCursor);

        var frame = _h.Capture("41-selection-handles");
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 120)));

        Drag(150, 120, 200, 160); // bottom-right corner
        Assert.Equal(new RectangleI(50, 40, 150, 120), Doc.Selection!.Bounds);
        Drag(50, 100, 20, 100); // middle of the left edge
        Assert.Equal(new RectangleI(20, 40, 180, 120), Doc.Selection!.Bounds);
        Assert.Equal("Selection 180 × 120", Vm.SelectionSizeText);
        Assert.Equal(["New Image", "Rectangle Select", "Rectangle Select", "Rectangle Select"], Vm.History.Select(h => h.Text));

        // Dragging inside the selection (not on a handle) draws a new one.
        Drag(100, 100, 120, 110);
        Assert.Equal(new RectangleI(100, 100, 20, 10), Doc.Selection!.Bounds);
    }

    [AvaloniaFact]
    public void Ellipse_select_has_resize_handles_too()
    {
        NewImage();
        UseTool("Ellipse Select");
        Drag(50, 40, 150, 120);
        Assert.Equal(8, Vm.Overlay!.Handles.Count);

        Drag(100, 120, 100, 180); // bottom middle
        Assert.Equal(new RectangleI(50, 40, 100, 140), Doc.Selection!.Bounds);

        Vm.SelectAllCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Vm.Overlay);
    }
}
