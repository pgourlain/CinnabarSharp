using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using CinnabarSharp.Core.Effects;
using Effect = CinnabarSharp.Core.Effects.Effect;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class EffectsUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private void NewImage() => Vm.CreateImage(new NewImageOptions(new ImageSize(300, 200), ColorBgra.White));

    private static Effect Find(string name) => EffectCatalog.All.Single(e => e.Name == name);

    [AvaloniaFact]
    public void Effects_menu_lists_categories()
    {
        string[] expected = ["Repeat Last Effect", "Blurs", "Photo", "Noise", "Distort", "Stylize", "Render"];
        if (OperatingSystem.IsMacOS())
        {
            var effects = NativeMenu.GetMenu(_h.Window)!.Items.OfType<NativeMenuItem>().First(i => i.Header == "Effects");
            Assert.Equal(expected, effects.Menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header));
        }
        else
        {
            var menu = (Menu)_h.Window.FindControl<ContentControl>("MenuHost")!.Content!;
            var effects = menu.ItemsSource!.Cast<MenuItem>().First(i => (string)i.Header! == "Effe_cts");
            Assert.Equal(expected, effects.ItemsSource!.OfType<MenuItem>().Select(i => ((string)i.Header!).Replace("_", "")));
        }
    }

    [AvaloniaFact]
    public async Task Rendering_clouds_then_twist_and_repeat()
    {
        NewImage();
        Vm.PrimaryColor = Color.FromRgb(20, 60, 160);
        Vm.SecondaryColor = Colors.White;
        Assert.False(Vm.RepeatEffectCommand.CanExecute(null));

        await Vm.ApplyEffectCommand.ExecuteAsync(Find("Clouds"));
        _h.Dialogs.EffectAnswer = d =>
        {
            d.Parameters[0].Value = 60;
            return true;
        };
        await Vm.ApplyEffectCommand.ExecuteAsync(Find("Twist"));
        Assert.Equal("Repeat Twist", Vm.RepeatEffectText);

        await Vm.RepeatEffectCommand.ExecuteAsync(null);

        Assert.Equal(["New Image", "Clouds", "Twist", "Twist"], Vm.History.Select(h => h.Text));
        Assert.Equal(["Clouds", "Twist"], _h.Dialogs.EffectsShown);
        _h.Capture("80-clouds-twist");
    }

    [AvaloniaFact]
    public async Task Adjustments_are_not_remembered_as_last_effect()
    {
        NewImage();

        await Vm.InvertColorsCommand.ExecuteAsync(null);
        await Vm.BrightnessContrastCommand.ExecuteAsync(null);

        Assert.False(Vm.RepeatEffectCommand.CanExecute(null));
        Assert.Equal("Repeat Last Effect", Vm.RepeatEffectText);
    }

    [AvaloniaFact]
    public async Task Cancelled_effect_leaves_no_trace()
    {
        NewImage();
        _h.Dialogs.EffectAnswer = _ => false;

        await Vm.ApplyEffectCommand.ExecuteAsync(Find("Gaussian Blur"));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(Vm.History);
        Assert.False(Vm.RepeatEffectCommand.CanExecute(null));
    }
}
