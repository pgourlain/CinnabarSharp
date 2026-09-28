using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using CinnabarSharp.Core.Effects;
using Effect = CinnabarSharp.Core.Effects.Effect;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using PointD = CinnabarSharp.Core.Models.PointD;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

public sealed class EffectsUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private void NewImage() => Vm.CreateImage(new NewImageOptions(new ImageSize(300, 200), ColorBgra.White));

    private ImageDocument Doc => Vm.ActiveDocument!.Document;

    /// <summary>A white image with a colored rectangle, so effects like Zoom Blur have something to distort.</summary>
    private void NewPatternedImage()
    {
        NewImage();
        Doc.SetSelection(SelectionMask.Rectangle(300, 200, new PointD(80, 60), new PointD(220, 140)));
        Doc.Actions.FillSelection(ColorBgra.FromBgra(0, 120, 220, 255));
        Doc.SetSelection(null);
    }

    private static Effect Find(string name) => EffectCatalog.All.Single(e => e.Name == name);

    [AvaloniaFact]
    public void Effects_menu_lists_categories()
    {
        string[] expected = ["Repeat Last Effect", "Blurs", "Photo", "Noise", "Distort", "Stylize", "Artistic", "Render"];
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
    public async Task Effect_dialog_shows_progress_while_previewing_and_the_status_bar_while_applying()
    {
        NewPatternedImage(); // Zoom Blur needs something non-uniform to actually change any pixel
        EffectDialogViewModel? captured = null;
        _h.Dialogs.EffectAnswer = d =>
        {
            // The preview for the dialog's default values was just requested and hasn't finished yet (no
            // dispatcher pump has happened): the same "Computing…" indicator Auto-Enhance uses in the status bar.
            Assert.True(d.Computing);
            captured = d;
            return true;
        };

        var task = Vm.ApplyEffectCommand.ExecuteAsync(Find("Zoom Blur"));
        // After the (fake) dialog "closes", CommitAsync's own await is the first one that doesn't complete
        // synchronously, so by now the status bar is already showing, exactly like clicking Auto-Enhance.
        Assert.True(Vm.IsBusy);
        Assert.Equal("Zoom Blur…", Vm.BusyText);
        Assert.False(_h.Canvas.IsEffectivelyEnabled);

        await task;

        Assert.False(Vm.IsBusy);
        Assert.NotNull(captured);
        Assert.False(captured!.Computing);
        Assert.Equal(["New Image", "Fill Selection", "Zoom Blur"], Vm.History.Select(h => h.Text));
    }

    [AvaloniaFact]
    public async Task Effect_window_shows_a_progress_indicator_and_disables_ok_while_computing()
    {
        NewImage();
        var session = new Core.Effects.EffectSession(Doc, Find("Gaussian Blur"));
        var dialog = new EffectDialogViewModel(session);
        var window = new EffectWindow { DataContext = dialog };
        window.Show();
        Dispatcher.UIThread.RunJobs(); // let the window realize its visual tree before checking visibility

        dialog.RequestPreview(); // just started: still computing, nothing has pumped the background task's result yet
        var row = window.FindControl<StackPanel>("ComputingRow")!;
        var ok = window.FindControl<Button>("OkButton")!;
        Assert.True(row.IsVisible);
        Assert.False(ok.IsEnabled);
        TestHarness.CaptureWindow(window, "83-effect-computing");

        // The preview's continuation resumes on this same (UI) thread, so pump the dispatcher while awaiting it
        // instead of blocking it with .Wait(), which would deadlock.
        var preview = dialog.PreviewTask;
        while (!preview.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();

        Assert.False(dialog.Computing);
        Assert.False(row.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Cartoon_turns_a_photo_into_flat_colors_with_outlines()
    {
        Assert.True(await Vm.OpenFileAsync(TestHarness.SampleImage));
        var before = Doc.Layers[0].Surface.ToBgra();
        _h.Dialogs.EffectAnswer = d =>
        {
            Assert.Equal(["Smoothness", "Colors", "Saturation", "Edge threshold", "Edge width", "Edge strength"],
                d.Parameters.Select(p => p.Name));
            return true;
        };

        await Vm.ApplyEffectCommand.ExecuteAsync(Find("Cartoon"));

        Assert.Equal("Cartoon", Vm.History[^1].Text);
        var after = Doc.Layers[0].Surface.ToBgra();
        // Fewer distinct colors than the photo: flat tones.
        int Colors(byte[] px) => Enumerable.Range(0, px.Length / 4).Select(i => (px[i * 4], px[i * 4 + 1], px[i * 4 + 2])).Distinct().Count();
        Assert.True(Colors(after) < Colors(before) / 2, $"{Colors(after)} colors vs {Colors(before)}");
        _h.Capture("100-cartoon");
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
