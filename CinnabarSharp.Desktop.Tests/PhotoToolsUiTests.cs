using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

public sealed class PhotoToolsUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;
    private ImageDocument Doc => Vm.ActiveDocument!.Document;

    private byte[] Pixel(int x = 10, int y = 10) => Doc.Layers[0].Surface.ReadRegion(new RectangleI(x, y, 1, 1));

    private async Task OpenSample() => Assert.True(await Vm.OpenFileAsync(TestHarness.SampleImage));

    private static Effect Photo<T>() where T : Effect => EffectCatalog.PhotoTools.OfType<T>().Single();

    private static void WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Assert.True(condition());
    }

    private double BlueToRed()
    {
        var px = Doc.Layers[0].Surface.ToBgra();
        double b = 0, r = 0;
        for (var i = 0; i < px.Length; i += 4)
            (b, r) = (b + px[i], r + px[i + 2]);
        return b / r;
    }

    [AvaloniaFact]
    public async Task Auto_enhance_applies_at_once_as_one_step()
    {
        // A dark, bluish gradient.
        Vm.CreateImage(new NewImageOptions(new ImageSize(120, 80), ColorBgra.White));
        Vm.SelectedTool = Vm.Tools.First(t => t.Name == "Gradient");
        Vm.PrimaryColor = Avalonia.Media.Color.FromRgb(20, 25, 60);
        Vm.SecondaryColor = Avalonia.Media.Color.FromRgb(110, 120, 170);
        Vm.ToolPointerDown(new Core.Tools.ToolPointer(new PointD(0, 40), Core.Tools.ToolButton.Left, Core.Tools.ToolModifiers.None));
        Vm.ToolPointerUp(new Core.Tools.ToolPointer(new PointD(120, 40), Core.Tools.ToolButton.Left, Core.Tools.ToolModifiers.None));
        var before = BlueToRed();
        var brightness = Pixel(60, 40)[1];

        await Vm.ApplyEffectCommand.ExecuteAsync(Photo<AutoEnhanceEffect>());

        Assert.Empty(_h.Dialogs.EffectsShown);
        Assert.Equal(["New Image", "Gradient", "Auto-Enhance"], Vm.History.Select(h => h.Text));
        Assert.True(BlueToRed() < before * 0.9, $"{before} → {BlueToRed()}");
        Assert.True(Pixel(60, 40)[1] > brightness);
    }

    [AvaloniaFact]
    public async Task Adjust_dialog_auto_button_and_sliders()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(120, 80), ColorBgra.FromBgra(60, 60, 60, 255)));
        _h.Dialogs.EffectAnswer = dialog =>
        {
            Assert.True(dialog.CanAuto);
            dialog.AutoCommand.Execute(null);
            Assert.True(dialog.Parameters.Single(p => p.Name == "Exposure").Value > 0);
            dialog.ResetCommand.Execute(null);
            dialog.Parameters.Single(p => p.Name == "Warmth").Value = 60;
            return true;
        };

        await Vm.ApplyEffectCommand.ExecuteAsync(Photo<PhotoAdjustEffect>());

        var px = Pixel();
        Assert.True(px[2] > px[0], string.Join(",", px));
        Assert.Equal("Adjust Photo", Vm.History[^1].Text);
    }

    [AvaloniaFact]
    public async Task Hold_to_compare_shows_the_original_during_preview()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(60, 40), ColorBgra.FromBgra(60, 60, 60, 255)));
        var dialog = new EffectDialogViewModel(new EffectSession(Doc, Photo<PhotoAdjustEffect>()));
        dialog.Parameters.Single(p => p.Name == "Exposure").Value = 80;
        await dialog.PreviewTask;
        Dispatcher.UIThread.RunJobs();
        var preview = Pixel();
        Assert.True(preview[1] > 60);

        dialog.ShowOriginal = true;
        Assert.Equal(new byte[] { 60, 60, 60, 255 }, Pixel());
        dialog.ShowOriginal = false;
        Assert.Equal(preview, Pixel());
        dialog.Cancel();
    }

    [AvaloniaFact]
    public async Task Filters_dialog_applies_the_chosen_preset()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(60, 40), ColorBgra.FromBgra(40, 120, 220, 255)));
        _h.Dialogs.PhotoFilterAnswer = filters =>
        {
            Assert.Equal(PhotoFilterEffect.Presets.Count, filters.Presets.Count);
            filters.SelectedPreset = filters.Presets.Single(p => p.Name == "Mono");
            return true;
        };

        await Vm.ApplyEffectCommand.ExecuteAsync(Photo<PhotoFilterEffect>());

        var px = Pixel();
        Assert.True(px[0] == px[1] && px[1] == px[2], string.Join(",", px));
        Assert.Equal("Photo Filter", Vm.History[^1].Text);
    }

    [AvaloniaFact]
    public async Task Photo_dialogs_render()
    {
        await OpenSample();

        var filters = new PhotoFilterWindow { DataContext = new PhotoFilterDialogViewModel(new EffectSession(Doc, Photo<PhotoFilterEffect>())) };
        filters.Show();
        TestHarness.CaptureWindow(filters, "80-photo-filters");
        Assert.True(filters.Bounds.Width > 400);
        filters.Close();

        var adjust = new EffectWindow { DataContext = new EffectDialogViewModel(new EffectSession(Doc, Photo<PhotoAdjustEffect>())) };
        adjust.Show();
        TestHarness.CaptureWindow(adjust, "81-adjust-photo");
        Assert.True(adjust.Bounds.Height < 800);
        adjust.Close();
    }

    [AvaloniaFact]
    public async Task Straighten_rotates_and_fills_the_frame()
    {
        await OpenSample();
        _h.Dialogs.EffectAnswer = dialog =>
        {
            dialog.Parameters[0].Value = 6;
            return true;
        };

        await Vm.ApplyEffectCommand.ExecuteAsync(Photo<StraightenEffect>());

        Assert.Equal("Straighten", Vm.History[^1].Text);
        Assert.Equal(255, Pixel(0, 0)[3]);
        _h.Capture("82-straightened");
    }

    [AvaloniaFact]
    public void Photo_menu_lists_the_photo_tools()
    {
        var names = EffectCatalog.PhotoTools.Select(e => e.Name).ToList();
        Assert.Equal(["Auto-Enhance", "Adjust Photo", "Photo Filter", "Straighten"], names);
        Assert.DoesNotContain(EffectCatalog.Effects, e => names.Contains(e.Name));
    }
}
