using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

public sealed class PaintingUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private void NewImage(int w = 300, int h = 200) =>
        Vm.CreateImage(new NewImageOptions(new ImageSize(w, h), ColorBgra.White));

    private void UseTool(string name)
    {
        Vm.SelectedTool = Vm.Tools.First(t => t.Name == name);
        Dispatcher.UIThread.RunJobs();
    }

    private void Drag(MouseButton button, params (double X, double Y)[] points)
    {
        Dispatcher.UIThread.RunJobs();
        var held = button == MouseButton.Right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton;
        _h.Window.MouseDown(_h.CanvasToWindow(points[0].X, points[0].Y), button);
        foreach (var p in points.Skip(1))
            _h.Window.MouseMove(_h.CanvasToWindow(p.X, p.Y), held);
        _h.Window.MouseUp(_h.CanvasToWindow(points[^1].X, points[^1].Y), button);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Brush_strokes_paint_primary_and_secondary_colors()
    {
        NewImage();
        UseTool("Paintbrush");
        Vm.BrushWidth = 10;
        Vm.PrimaryColor = Colors.Red;
        Vm.SecondaryColor = Colors.Blue;

        Drag(MouseButton.Left, (20, 50), (150, 50), (280, 60));
        Drag(MouseButton.Right, (20, 150), (280, 150));

        var frame = _h.Capture("50-brush-strokes");
        Assert.Equal((255, 0, 0), TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 50)));
        Assert.Equal((0, 0, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 150)));
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 100)));
        Assert.Equal(["New Image", "Paintbrush", "Paintbrush"], Vm.History.Select(h => h.Text));
    }

    [AvaloniaFact]
    public void Painted_scene_with_shapes_gradient_and_bucket()
    {
        NewImage(400, 260);
        Vm.PrimaryColor = Color.FromRgb(227, 66, 52);
        Vm.SecondaryColor = Color.FromRgb(250, 200, 60);

        UseTool("Gradient");
        Vm.GradientKind = GradientKind.Linear;
        Drag(MouseButton.Left, (0, 0), (400, 260));

        UseTool("Shapes");
        Vm.ShapeKind = ShapeKind.Ellipse;
        Vm.ShapeStyle = ShapeStyle.OutlineAndFill;
        Vm.BrushWidth = 6;
        Drag(MouseButton.Left, (40, 40), (200, 200));

        UseTool("Line / Curve");
        Vm.PrimaryColor = Colors.Black;
        Drag(MouseButton.Left, (220, 30), (380, 230));

        UseTool("Paint Bucket");
        var frame = _h.Capture("51-painted-scene");

        Assert.Equal(["New Image", "Gradient", "Shapes", "Line / Curve"], Vm.History.Select(h => h.Text));
        Assert.Equal((250, 200, 60), TestHarness.PixelAt(frame, _h.CanvasToWindow(120, 120)));
    }

    [AvaloniaFact]
    public void Color_picker_updates_the_palette()
    {
        NewImage();
        UseTool("Paintbrush");
        Vm.PrimaryColor = Colors.Lime;
        Vm.BrushWidth = 20;
        Drag(MouseButton.Left, (100, 100), (120, 100));
        Vm.PrimaryColor = Colors.Black;

        UseTool("Color Picker");
        Drag(MouseButton.Left, (110, 100));

        Assert.Equal(Colors.Lime, Vm.PrimaryColor);
        Assert.Equal(Colors.Lime, ((ISolidColorBrush)Vm.PrimaryBrush).Color);
    }

    [AvaloniaFact]
    public void Color_panel_shows_hex_and_rgb_and_updates_with_the_colors()
    {
        Vm.PrimaryColor = Color.FromRgb(0x1A, 0x2B, 0x3C);
        Vm.SecondaryColor = Color.FromArgb(0x80, 0xFF, 0x00, 0x80);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("#1A2B3C", Vm.PrimaryColorHex);
        Assert.Equal("RGB 26, 43, 60", Vm.PrimaryColorRgbText);
        Assert.Contains("Hex #1A2B3C", Vm.PrimaryColorDetails);
        Assert.Contains("RGB 26, 43, 60", Vm.PrimaryColorDetails);
        Assert.Contains("HSV", Vm.PrimaryColorDetails);
        Assert.DoesNotContain("Alpha", Vm.PrimaryColorDetails); // fully opaque: no alpha line

        // Not fully opaque: the hex code and the tooltip both carry the alpha.
        Assert.Equal("#FF008080", Vm.SecondaryColorHex);
        Assert.Equal("RGB 255, 0, 128", Vm.SecondaryColorRgbText);
        Assert.Contains("Alpha 128", Vm.SecondaryColorDetails);

        var texts = _h.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("#1A2B3C", texts);
        Assert.Contains("#FF008080", texts);

        Vm.SwapColorsCommand.Execute(null);
        Assert.Equal("#FF008080", Vm.PrimaryColorHex);
        Assert.Equal("#1A2B3C", Vm.SecondaryColorHex);

        Vm.ResetColorsCommand.Execute(null);
        Assert.Equal("#000000", Vm.PrimaryColorHex);
        Assert.Equal("#FFFFFF", Vm.SecondaryColorHex);
        _h.Capture("20-color-panel-hex");
    }

    [AvaloniaFact]
    public async Task Clicking_a_swatch_opens_the_color_dialog()
    {
        _h.Dialogs.ColorAnswers.Enqueue(Colors.Orange);
        _h.Dialogs.ColorAnswers.Enqueue(null);

        await Vm.PickPrimaryColorCommand.ExecuteAsync(null);
        await Vm.PickSecondaryColorCommand.ExecuteAsync(null);

        Assert.Equal(Colors.Orange, Vm.PrimaryColor);
        Assert.Equal(Colors.White, Vm.SecondaryColor);

        Vm.ResetColorsCommand.Execute(null);
        Assert.Equal(Colors.Black, Vm.PrimaryColor);
    }

    [AvaloniaFact]
    public void Color_dialog_renders()
    {
        var dialog = new ColorPickerWindow("Primary Color", Colors.Orange);
        dialog.Show();
        var frame = TestHarness.CaptureWindow(dialog, "52-color-dialog");
        Assert.True(dialog.Bounds.Width > 200);

        // The three tabs show their icons: dark pixels where each icon is, on the light tab bar.
        var icons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<Avalonia.Controls.PathIcon>().ToList();
        Assert.Equal(3, icons.Count);
        foreach (var icon in icons)
        {
            var origin = icon.TranslatePoint(default, dialog)!.Value;
            var dark = 0;
            for (var y = 0; y < icon.Bounds.Height; y += 2)
                for (var x = 0; x < icon.Bounds.Width; x += 2)
                {
                    var (r, g, b) = TestHarness.PixelAt(frame, new Avalonia.Point(origin.X + x, origin.Y + y));
                    if (r + g + b < 3 * 150)
                        dark++;
                }
            Assert.True(dark > 5, "a tab icon of the color dialog is blank");
        }
        dialog.Close();
    }

    [AvaloniaFact]
    public void Letter_shortcuts_select_tools_and_s_cycles_select_tools()
    {
        NewImage();

        _h.Window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, "b");
        Assert.Equal("Paintbrush", Vm.SelectedTool.Name);
        Assert.True(Vm.ShowBrushOptions);

        _h.Window.KeyPress(Key.S, RawInputModifiers.None, PhysicalKey.S, "s");
        Assert.Equal("Rectangle Select", Vm.SelectedTool.Name);
        _h.Window.KeyPress(Key.S, RawInputModifiers.None, PhysicalKey.S, "s");
        Assert.Equal("Ellipse Select", Vm.SelectedTool.Name);

        _h.Window.KeyPress(Key.O, RawInputModifiers.None, PhysicalKey.O, "o");
        _h.Window.KeyPress(Key.O, RawInputModifiers.None, PhysicalKey.O, "o");
        Assert.Equal("Shapes", Vm.SelectedTool.Name);
        Assert.True(Vm.ShowShapeOptions);
        Assert.False(Vm.ShowSelectionOptions);
    }

    [AvaloniaFact]
    public void Eraser_and_undo()
    {
        NewImage();
        UseTool("Eraser");
        Vm.BrushWidth = 30;

        Drag(MouseButton.Left, (50, 100), (250, 100));
        var frame = _h.Capture("53-eraser");
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 50)));
        // Erased pixels show the transparency checkerboard: one of two squares 8 px apart is grey.
        Assert.Contains(((byte)204, (byte)204, (byte)204), new[]
        {
            TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 100)),
            TestHarness.PixelAt(frame, _h.CanvasToWindow(158, 100)),
        });

        Vm.UndoCommand.Execute(null);
        frame = _h.Capture("54-eraser-undone");
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 100)));
    }
}
