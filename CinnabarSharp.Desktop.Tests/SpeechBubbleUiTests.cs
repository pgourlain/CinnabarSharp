using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class SpeechBubbleUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private void UseBubbles()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(560, 300), ColorBgra.FromBgra(120, 170, 60, 255)));
        Vm.SelectedTool = Vm.Tools.First(t => t.Name == "Speech Bubble");
        Vm.PrimaryColor = Color.FromRgb(20, 30, 140);
        Vm.SecondaryColor = Color.FromRgb(255, 250, 220);
        Vm.FontSize = 20;
        Dispatcher.UIThread.RunJobs();
    }

    private void Drag(params (double X, double Y)[] points)
    {
        Dispatcher.UIThread.RunJobs();
        _h.Window.MouseDown(_h.CanvasToWindow(points[0].X, points[0].Y), MouseButton.Left);
        foreach (var p in points.Skip(1))
            _h.Window.MouseMove(_h.CanvasToWindow(p.X, p.Y), RawInputModifiers.LeftMouseButton);
        _h.Window.MouseUp(_h.CanvasToWindow(points[^1].X, points[^1].Y), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Finishes the bubble being edited (a style change would restyle it).</summary>
    private void Escape()
    {
        _h.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

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

    [AvaloniaFact]
    public void Several_numbered_bubbles_are_placed_in_a_row_on_their_own_layer()
    {
        UseBubbles();
        Vm.BubbleNumbered = true;
        Vm.BubbleStyle = BubbleStyle.Rounded;

        Drag((60, 260), (120, 200), (150, 70));
        Type("Big tree");
        Escape();
        Vm.BubbleStyle = BubbleStyle.Square;
        Drag((300, 250), (300, 150));
        Type("Bench");
        Escape();
        Vm.BubbleStyle = BubbleStyle.Oval;
        Drag((520, 280), (440, 80));
        Type("Exit");

        var frame = _h.Capture("90-speech-bubbles");
        Assert.Equal("Speech Bubble", Vm.SelectedTool.Name); // letters were typed, not shortcuts
        Assert.Equal(4, Vm.BubbleNextNumber);
        Assert.Equal(["New Image", "Add New Layer", "Speech Bubble", "Speech Bubble", "Speech Bubble"], Vm.History.Select(h => h.Text));
        Assert.Equal(SpeechBubbleTool.LayerName, Vm.Layers[0].Name);
        Assert.Equal((255, 250, 220), TestHarness.PixelAt(frame, _h.CanvasToWindow(300, 135)));
        var text = 0; // the first bubble's text, in the primary color
        for (var y = 60; y < 80; y++)
            for (var x = 110; x < 190; x++)
                if (TestHarness.PixelAt(frame, _h.CanvasToWindow(x, y)) == (20, 30, 140))
                    text++;
        Assert.True(text > 50, $"{text} text pixels");
        Assert.NotNull(Vm.Overlay?.Frame);
    }

    [AvaloniaFact]
    public void Every_bubble_style_on_screen()
    {
        UseBubbles();
        Vm.BubbleOwnLayer = false;
        var x = 70;
        foreach (var style in Enum.GetValues<BubbleStyle>())
        {
            Vm.BubbleStyle = style;
            Drag((x - 30, 280), (x + 10, 120));
            Type(style.ToString());
            Escape();
            x += 135;
        }

        _h.Capture("91-speech-bubble-styles");
        Assert.Single(Vm.Layers);
        Assert.False(Vm.IsTyping);
    }

    [AvaloniaFact]
    public void Paste_goes_into_the_bubble_text()
    {
        UseBubbles();
        Drag((60, 260), (200, 100));
        _h.Clipboard.Text = "Pasted";

        Vm.PasteCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Pasted", ((SpeechBubbleTool)Vm.SelectedTool.Tool!).Engine.ToString());
        Assert.Equal(["New Image", "Add New Layer", "Speech Bubble"], Vm.History.Select(h => h.Text));
    }
}
