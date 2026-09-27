using Avalonia.Headless.XUnit;
using Avalonia.Media;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Desktop.Services;

namespace CinnabarSharp.Desktop.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cinnabarsharp-settings-");

    public void Dispose() => _dir.Delete(recursive: true);

    [AvaloniaFact]
    public void Tool_options_colors_and_window_size_survive_a_restart()
    {
        var store = new SettingsStore(Path.Combine(_dir.FullName, "settings.json"));

        using (var first = new TestHarness())
        {
            first.Window.RestoreSettings(store);
            first.Vm.SelectedTool = first.Vm.Tools.First(t => t.Name == "Gradient");
            first.Vm.BrushWidth = 17;
            first.Vm.GradientKind = GradientKind.Conical;
            first.Vm.PrimaryColor = Colors.Teal;
            first.Vm.Hardness = 40;
            first.Vm.FontSize = 36;
            first.Vm.Italic = true;
            first.Vm.TextAlignment = CinnabarSharp.Core.Models.TextAlignment.Center;
            first.Window.Width = 1100;
            first.Window.Height = 700;
        }

        using var second = new TestHarness();
        second.Window.RestoreSettings(store);

        Assert.Equal("Gradient", second.Vm.SelectedTool.Name);
        Assert.Equal(17, second.Vm.BrushWidth);
        Assert.Equal(GradientKind.Conical, second.Vm.GradientKind);
        Assert.Equal(Colors.Teal, second.Vm.PrimaryColor);
        Assert.Equal(40, second.Vm.Hardness);
        Assert.Equal(36, second.Vm.FontSize);
        Assert.True(second.Vm.Italic);
        Assert.Equal(CinnabarSharp.Core.Models.TextAlignment.Center, second.Vm.TextAlignment);
        Assert.Equal(1100, second.Window.Width);
        Assert.Equal(700, second.Window.Height);
    }

    [AvaloniaFact]
    public void Missing_or_corrupt_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(_dir.FullName, "bad.json");
        File.WriteAllText(path, "{ not json");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(2, settings.BrushWidth);
        Assert.Null(settings.SelectedTool);
    }
}
