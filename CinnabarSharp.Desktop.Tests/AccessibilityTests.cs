using Avalonia;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class AccessibilityTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    /// <summary>What a screen reader announces: the automation name, else text content.</summary>
    private static string? Announced(Control c) =>
        AutomationProperties.GetName(c) is { Length: > 0 } name ? name
        : c is ContentControl { Content: string { Length: > 0 } text } ? text
        : null;

    [AvaloniaFact]
    public void Every_visible_input_in_the_main_window_has_a_name()
    {
        _h.Vm.CreateImage(new NewImageOptions(new ImageSize(100, 80), ColorBgra.White));
        var unnamed = new List<string>();
        foreach (var tool in _h.Vm.Tools)
        {
            _h.Vm.SelectedTool = tool;
            Dispatcher.UIThread.RunJobs();
            unnamed.AddRange(_h.Window.GetVisualDescendants().OfType<Control>()
                .Where(c => c is Button or ToggleButton or ComboBox or NumericUpDown or Slider or CheckBox or ListBox)
                .Where(c => c.IsEffectivelyVisible && c.TemplatedParent is null && Announced(c) is null)
                .Select(c => $"{tool.Name}: {c.GetType().Name} {c.Name}"));
        }

        Assert.Empty(unnamed.Distinct());
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v) { var x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    private static Color Token(string name, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(name, variant, out var value), name);
        return ((ISolidColorBrush)value!).Color;
    }

    // WCAG AA: 4.5:1 for text, in both themes. Every pair of the design system where text sits on a surface.
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Theme_text_colors_have_enough_contrast(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var pairs = new (string Text, string Surface)[]
        {
            ("CinnabarTextBrush", "CinnabarPanelBrush"), ("CinnabarTextBrush", "CinnabarChromeBrush"),
            ("CinnabarTextBrush", "CinnabarSurfaceBrush"), ("CinnabarTextBrush", "CinnabarAccentTintBrush"),
            ("CinnabarTextSecondaryBrush", "CinnabarPanelBrush"), ("CinnabarTextSecondaryBrush", "CinnabarChromeBrush"),
            ("CinnabarTextSecondaryBrush", "CinnabarSurfaceBrush"),
            ("CinnabarOnAccentBrush", "CinnabarAccentBrush"), ("CinnabarOnAccentBrush", "CinnabarAccentHoverBrush"),
            ("CinnabarOnAccentBrush", "CinnabarAccentPressedBrush"),
            ("CinnabarWarningTextBrush", "CinnabarWarningBackgroundBrush"),
            ("CinnabarAccentBrush", "CinnabarPanelBrush"), // the focus ring and the unsaved dot: 3:1 is the minimum, 4.5 held
        };
        var low = pairs.Select(p => (p, Ratio: Contrast(Token(p.Text, variant), Token(p.Surface, variant))))
            .Where(x => x.Ratio < 4.5).Select(x => $"{x.p.Text} on {x.p.Surface}: {x.Ratio:0.0}").ToList();

        Assert.Empty(low);
    }

    [AvaloniaFact]
    public void Every_control_of_the_main_window_can_be_reached_with_the_keyboard()
    {
        _h.Vm.CreateImage(new NewImageOptions(new ImageSize(100, 80), ColorBgra.White));
        var unreachable = new List<string>();
        foreach (var tool in _h.Vm.Tools)
        {
            _h.Vm.SelectedTool = tool;
            Dispatcher.UIThread.RunJobs();
            unreachable.AddRange(_h.Window.GetVisualDescendants().OfType<InputElement>()
                .Where(c => c is Button or ToggleButton or ComboBox or Slider or CheckBox) // a NumericUpDown's own TextBox takes the focus
                .Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.TemplatedParent is null)
                .Where(c => !c.Focusable || !KeyboardNavigation.GetIsTabStop(c))
                .Select(c => $"{tool.Name}: {c.GetType().Name} {AutomationProperties.GetName(c)}"));
        }

        Assert.Empty(unreachable.Distinct());
    }

    [AvaloniaFact]
    public void Tab_moves_the_focus_through_the_toolbar_in_order()
    {
        _h.Vm.CreateImage(new NewImageOptions(new ImageSize(100, 80), ColorBgra.White));
        Dispatcher.UIThread.RunJobs();
        var toolbar = _h.Window.GetVisualDescendants().OfType<Button>()
            .First(b => AutomationProperties.GetName(b) == "New image");
        toolbar.Focus();

        var visited = new List<string?>();
        for (var i = 0; i < 4; i++)
        {
            _h.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            visited.Add(AutomationProperties.GetName((Control)_h.Window.FocusManager!.GetFocusedElement()!));
        }

        _h.Capture("15-keyboard-focus");
        Assert.Equal(["Open", "Save", "Zoom out", "Zoom in"], visited); // Undo and Redo are disabled: skipped
    }

    [AvaloniaFact]
    public void Tool_icons_are_announced_with_their_name_and_shortcut()
    {
        Dispatcher.UIThread.RunJobs();
        var names = _h.Window.GetVisualDescendants().OfType<Viewbox>()
            .Select(AutomationProperties.GetName).OfType<string>().ToList();

        Assert.Contains("Paintbrush (B)", names);
        Assert.Contains("Crop (C)", names);
    }
}
