using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
