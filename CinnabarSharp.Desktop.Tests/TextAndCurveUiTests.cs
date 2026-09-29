using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class TextAndCurveUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private void NewImage(int w = 400, int h = 240) =>
        Vm.CreateImage(new NewImageOptions(new ImageSize(w, h), ColorBgra.White));

    private void UseTool(string name)
    {
        Vm.SelectedTool = Vm.Tools.First(t => t.Name == name);
        Dispatcher.UIThread.RunJobs();
    }

    private void Drag(RawInputModifiers modifiers, params (double X, double Y)[] points)
    {
        Dispatcher.UIThread.RunJobs();
        _h.Window.MouseDown(_h.CanvasToWindow(points[0].X, points[0].Y), MouseButton.Left, modifiers);
        foreach (var p in points.Skip(1))
            _h.Window.MouseMove(_h.CanvasToWindow(p.X, p.Y), RawInputModifiers.LeftMouseButton | modifiers);
        _h.Window.MouseUp(_h.CanvasToWindow(points[^1].X, points[^1].Y), MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private void Drag(params (double X, double Y)[] points) => Drag(RawInputModifiers.None, points);

    /// <summary>Types like a keyboard: key down/up (which may trigger shortcuts), then the text input.</summary>
    private void Type(string text)
    {
        foreach (var c in text)
        {
            if (Enum.TryParse<Key>(c.ToString(), ignoreCase: true, out var key) && char.IsLetter(c))
                _h.Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, c.ToString());
            _h.Window.KeyTextInput(c.ToString());
        }
        Dispatcher.UIThread.RunJobs();
    }

    private void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        _h.Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static int CountPixels(Avalonia.Media.Imaging.WriteableBitmap frame, Avalonia.Point from, Avalonia.Point to,
        (byte R, byte G, byte B) color)
    {
        var count = 0;
        for (var y = (int)from.Y; y < (int)to.Y; y++)
            for (var x = (int)from.X; x < (int)to.X; x++)
                if (TestHarness.PixelAt(frame, new Avalonia.Point(x, y)) == color)
                    count++;
        return count;
    }

    [AvaloniaFact]
    public void Text_tool_types_text_in_the_primary_color_and_shortcuts_stay_quiet()
    {
        NewImage();
        UseTool("Text");
        Vm.PrimaryColor = Color.FromRgb(200, 30, 60);
        Vm.FontSize = 48;
        Vm.Bold = true;

        Drag((40, 60));
        Type("Hello bxs");
        Press(Key.Back);
        Press(Key.Back);
        Press(Key.Back);
        Type("world");

        var frame = _h.Capture("60-text-tool");
        Assert.Equal("Text", Vm.SelectedTool.Name); // "b", "x" and "s" were typed, not shortcuts.
        Assert.Equal(Colors.White, Vm.SecondaryColor); // "x" did not swap the colors.
        Assert.Equal("Hello world", ((TextTool)Vm.SelectedTool.Tool!).Engine.ToString());
        Assert.Equal(["New Image", "Text"], Vm.History.Select(h => h.Text));
        Assert.True(CountPixels(frame, _h.CanvasToWindow(40, 60), _h.CanvasToWindow(390, 130), (200, 30, 60)) > 200);
        Assert.NotNull(Vm.Overlay?.Frame);
    }

    [AvaloniaFact]
    public void Rotate_handle_rotates_the_text_overlay_and_stays_editable()
    {
        NewImage();
        UseTool("Text");
        Vm.PrimaryColor = Color.FromRgb(200, 30, 60);
        Vm.FontSize = 100;
        Drag((60, 60));
        Type("Hi");

        var pivot = Vm.Overlay!.Rotation!.Value.Pivot;
        var handle = Vm.Overlay!.RotateHandle!.Value;
        // A quarter turn (90°, atan2's clockwise-in-image-coordinates sense) around the pivot.
        var quarterTurn = new PointD(pivot.X + (handle.Y - pivot.Y), pivot.Y - (handle.X - pivot.X));
        Drag((handle.X, handle.Y), (quarterTurn.X, quarterTurn.Y));

        var frame = _h.Capture("68-text-rotated");
        Assert.Equal(-Math.PI / 2, Vm.Overlay!.Rotation!.Value.Angle, 1e-6);
        Assert.Equal(["New Image", "Text"], Vm.History.Select(h => h.Text)); // still the one live step
        Assert.True(Vm.IsTyping); // rotating doesn't finish editing

        Type("!");
        Assert.Equal("Hi!", ((TextTool)Vm.SelectedTool.Tool!).Engine.ToString());
        Assert.Equal(["New Image", "Text"], Vm.History.Select(h => h.Text));
    }

    [AvaloniaFact]
    public void Escape_finishes_text_then_letters_are_shortcuts_again()
    {
        NewImage();
        UseTool("Text");
        Drag((40, 60));
        Type("A");
        Assert.True(Vm.IsTyping);

        Press(Key.Escape);

        Assert.False(Vm.IsTyping);
        Assert.Null(Vm.Overlay);
        _h.Window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, "b");
        Assert.Equal("Paintbrush", Vm.SelectedTool.Name);
    }

    [AvaloniaFact]
    public void Font_options_restyle_the_text_being_edited()
    {
        NewImage();
        UseTool("Text");
        Vm.FontSize = 20;
        Drag((20, 20));
        Type("Wide");
        var before = CountPixels(_h.Capture("61-text-small"), _h.CanvasToWindow(0, 0), _h.CanvasToWindow(400, 240), (0, 0, 0));

        Vm.FontSize = 60;
        Vm.Italic = true;
        Vm.Underline = true;
        var frame = _h.Capture("62-text-restyled");
        var after = CountPixels(frame, _h.CanvasToWindow(0, 0), _h.CanvasToWindow(400, 240), (0, 0, 0));

        Assert.True(after > before * 3, $"{before} → {after}");
        Assert.Equal(["New Image", "Text"], Vm.History.Select(h => h.Text));
    }

    [AvaloniaFact]
    public void Switching_tools_finishes_the_text_and_undo_removes_it()
    {
        NewImage();
        UseTool("Text");
        Drag((40, 60));
        Type("Hi");
        UseTool("Rectangle Select");

        Assert.Null(Vm.Overlay);
        Vm.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var frame = _h.Capture("63-text-undone");
        Assert.Equal(0, CountPixels(frame, _h.CanvasToWindow(40, 60), _h.CanvasToWindow(120, 110), (0, 0, 0)));
    }

    [AvaloniaFact]
    public async Task Copy_and_paste_work_on_the_text_while_editing()
    {
        NewImage();
        UseTool("Text");
        Drag((40, 60));
        Type("abc");

        Vm.SelectAllCommand.Execute(null);
        await Vm.CopyCommand.ExecuteAsync(null);
        await Vm.PasteCommand.ExecuteAsync(null);
        await Vm.PasteCommand.ExecuteAsync(null);

        Assert.Equal("abc", _h.Clipboard.Text);
        Assert.Equal("abcabc", ((TextTool)Vm.SelectedTool.Tool!).Engine.ToString());
        Assert.Null(Vm.ActiveDocument!.Document.Selection); // Select All selected the text, not the image.
    }

    [AvaloniaFact]
    public void Typing_in_a_number_box_does_not_trigger_tool_shortcuts()
    {
        NewImage();
        UseTool("Paintbrush");
        var box = _h.Window.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.IsEffectivelyVisible);
        box.Focus();
        Dispatcher.UIThread.RunJobs();

        _h.Window.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e");

        Assert.Equal("Paintbrush", Vm.SelectedTool.Name);
    }

    [AvaloniaFact]
    public void Line_can_be_bent_with_its_handles_until_enter()
    {
        NewImage();
        UseTool("Line / Curve");
        Vm.PrimaryColor = Colors.Blue;
        Vm.BrushWidth = 6;

        Drag((50, 200), (350, 200));
        var line = (LineTool)Vm.SelectedTool.Tool!;
        Assert.Equal(4, Vm.Overlay!.Handles.Count);
        var c1 = line.Points[1];
        var c2 = line.Points[2];
        Drag((c1.X, c1.Y), (c1.X, 20));
        Drag((c2.X, c2.Y), (c2.X, 20));

        var frame = _h.Capture("64-bezier-curve");
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(200, 200)));
        Assert.Equal((0, 0, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(200, 65)));
        Assert.Equal(["New Image", "Line / Curve"], Vm.History.Select(h => h.Text));

        Press(Key.Enter);
        Assert.Null(Vm.Overlay);
    }

    [AvaloniaFact]
    public void Clone_stamp_uses_command_click_as_source()
    {
        NewImage();
        UseTool("Paintbrush");
        Vm.PrimaryColor = Colors.Red;
        Vm.BrushWidth = 20;
        Drag((60, 60), (61, 60));

        UseTool("Clone Stamp");
        Drag(RawInputModifiers.Control, (60, 60));
        Drag((200, 150), (201, 150));

        var frame = _h.Capture("65-clone-stamp");
        Assert.Equal((255, 0, 0), TestHarness.PixelAt(frame, _h.CanvasToWindow(200, 150)));
        Assert.Equal(["New Image", "Paintbrush", "Clone Stamp"], Vm.History.Select(h => h.Text));
    }

    [AvaloniaFact]
    public void Brush_tools_show_a_size_outline_and_hardness()
    {
        NewImage();
        UseTool("Paintbrush");
        Vm.BrushWidth = 40;
        _h.Window.MouseMove(_h.CanvasToWindow(200, 120));

        Assert.Equal(40, _h.Canvas.BrushSize);
        Assert.True(Vm.ShowHardnessOptions);
        _h.Capture("66-brush-outline");

        UseTool("Pencil");
        Assert.Equal(0, _h.Canvas.BrushSize);
        Assert.False(Vm.ShowHardnessOptions);
    }

    [AvaloniaFact]
    public void Rounded_rectangle_shows_its_radius_option()
    {
        NewImage();
        UseTool("Shapes");
        Assert.False(Vm.ShowCornerRadiusOptions);

        Vm.ShapeKind = ShapeKind.RoundedRectangle;
        Vm.ShapeStyle = ShapeStyle.Fill;
        Vm.CornerRadius = 30;
        Vm.PrimaryColor = Colors.Green;
        Drag((50, 50), (250, 200));

        var frame = _h.Capture("67-rounded-rectangle");
        Assert.True(Vm.ShowCornerRadiusOptions);
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(52, 52)));
        Assert.Equal((0, 128, 0), TestHarness.PixelAt(frame, _h.CanvasToWindow(150, 52)));
    }
}
