using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Adjustments;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

public sealed class AdjustmentsUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;
    private ImageDocument Doc => Vm.ActiveDocument!.Image;

    private void NewImage() =>
        Vm.CreateImage(new NewImageOptions(new ImageSize(200, 100), ColorBgra.FromBgra(40, 90, 200, 255)));

    private byte[] Pixel(int x = 10, int y = 10) => Doc.Layers[0].Surface.ReadRegion(new RectangleI(x, y, 1, 1));

    [AvaloniaFact]
    public async Task Invert_applies_immediately_as_one_step()
    {
        NewImage();

        await Vm.InvertColorsCommand.ExecuteAsync(null);

        Assert.Equal(new byte[] { 215, 165, 55, 255 }, Pixel());
        Assert.Empty(_h.Dialogs.EffectsShown);
        Assert.Equal(["New Image", "Invert Colors"], Vm.History.Select(h => h.Text));
    }

    [AvaloniaFact]
    public async Task Dialog_ok_applies_the_chosen_values()
    {
        NewImage();
        _h.Dialogs.EffectAnswer = dialog =>
        {
            dialog.Parameters.Single(p => p.Name == "Saturation").Value = 0;
            return true;
        };

        await Vm.HueSaturationCommand.ExecuteAsync(null);

        var px = Pixel();
        Assert.Equal(["Hue / Saturation"], _h.Dialogs.EffectsShown);
        Assert.True(px[0] == px[1] && px[1] == px[2], string.Join(",", px));
        Assert.Equal("Hue / Saturation", Vm.History[^1].Text);
    }

    [AvaloniaFact]
    public async Task Dialog_cancel_leaves_no_trace()
    {
        NewImage();
        _h.Dialogs.EffectAnswer = dialog =>
        {
            dialog.Parameters[0].Value = 100;
            return false;
        };

        await Vm.BrightnessContrastCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new byte[] { 40, 90, 200, 255 }, Pixel());
        Assert.Single(Vm.History);
        Assert.False(Doc.IsDirty);
    }

    [AvaloniaFact]
    public async Task Live_preview_is_computed_in_background_and_shown_on_canvas()
    {
        NewImage();
        var dialog = new EffectDialogViewModel(new EffectSession(Doc, new BrightnessContrast()));

        dialog.Parameters[0].Value = 100;
        await dialog.PreviewTask;
        Dispatcher.UIThread.RunJobs();

        var frame = _h.Capture("70-brightness-preview");
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(100, 50)));
        Assert.Single(Vm.History);

        dialog.Cancel();
        Assert.Equal(new byte[] { 40, 90, 200, 255 }, Pixel());
    }

    [AvaloniaFact]
    public async Task Adjustment_is_limited_to_the_selection()
    {
        NewImage();
        Doc.SetSelection(SelectionMask.Rectangle(200, 100, new PointD(0, 0), new PointD(100, 100)));

        await Vm.BlackAndWhiteCommand.ExecuteAsync(null);

        var left = Pixel(10, 10);
        Assert.True(left[0] == left[1] && left[1] == left[2]);
        Assert.Equal(new byte[] { 40, 90, 200, 255 }, Pixel(150, 10));
    }

    [AvaloniaFact]
    public void Adjustment_dialog_renders_its_parameters()
    {
        NewImage();
        var dialog = new EffectWindow
        {
            DataContext = new EffectDialogViewModel(new EffectSession(Doc, new BrightnessContrast())),
        };
        dialog.Show();
        TestHarness.CaptureWindow(dialog, "71-adjustment-dialog");
        Assert.True(dialog.Bounds.Height > 150);
        dialog.Close();
    }
}
