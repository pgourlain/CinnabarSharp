using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;

namespace CinnabarSharp.Desktop.ViewModels;

// Photo › Extract Walls and Layers › Trace Layer to SVG: the lines of a picture (the walls of a floor plan) as a new layer
// or as a new SVG drawing. The dialog is the effect dialog (live preview: the plan faded, the lines in red); OK does not
// apply the preview but computes the result and adds the layer or opens the drawing.
public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(HasImage))]
    private async Task ExtractWalls()
    {
        if (ActiveImageTab is not { } d || IsBusy || Dialogs is null)
            return;
        var effect = new ExtractWallsEffect();
        var dialog = new EffectDialogViewModel(new EffectSession(d.Image, effect, ToolSettings.PrimaryColor, ToolSettings.SecondaryColor));
        dialog.RequestPreview();
        var ok = await Dialogs.ShowEffectAsync(dialog);
        var values = dialog.Values;
        dialog.Cancel(); // puts the layer back: the preview was only an overlay
        if (!ok)
            return;
        await ExtractLinesAsync(effect.Name, ExtractWallsEffect.Settings(values), fromLayerAlpha: false,
            ExtractWallsEffect.WallColor(values, ToolSettings.PrimaryColor));
    }

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task TraceLayerToSvg() =>
        ExtractLinesAsync("Trace Layer to SVG", WallExtractionSettings.Default with { Output = WallsOutput.Svg }, fromLayerAlpha: true, ToolSettings.PrimaryColor);

    private async Task ExtractLinesAsync(string name, WallExtractionSettings settings, bool fromLayerAlpha, ColorBgra color)
    {
        if (ActiveImageTab is not { } d || IsBusy)
            return;
        var document = d.Image;
        var history = document.Workspace.History;
        var (pointer, count) = (history.Pointer, history.Items.Count);
        var data = await RunBusyAsync(name, _ => Task.Run(() => WallExtraction.Compute(document, settings, fromLayerAlpha)));
        // The macOS menu stays usable: if something changed the image meanwhile (e.g. Undo), drop the result.
        if (history.Pointer != pointer || history.Items.Count != count)
            return;
        WallExtraction.Apply(document, _workspace, data, settings, color);
        TrackUsage(TelemetryClient.Name("effect", name));
    }
}
