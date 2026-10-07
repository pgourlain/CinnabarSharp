using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;
using CommunityToolkit.Mvvm.Input;

namespace CinnabarSharp.Desktop.ViewModels;

// Pictures in SVG drawings: import, clipping, Edit Bitmap.
public partial class MainViewModel
{
    private async Task ImportPicturesAsync(bool linked)
    {
        if (Dialogs is null || ActiveSvg is not { } drawing)
            return;
        foreach (var path in await Dialogs.PickFilesToOpenAsync(_formats.Formats))
        {
            try
            {
                drawing.Actions.ImportImage(new FileInfo(path), linked);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                await Dialogs.ShowErrorAsync($"Could not import \"{Path.GetFileName(path)}\"", Describe(e));
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasSvg))]
    private Task ImportPicture() => ImportPicturesAsync(linked: false);

    [RelayCommand(CanExecute = nameof(HasSvg))]
    private Task ImportLinkedPicture() => ImportPicturesAsync(linked: true);

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private Task SetClip() => PathEditAsync(d => d.Actions.SetClip());

    [RelayCommand(CanExecute = nameof(HasObjectSelection))]
    private void ReleaseClip() => ActiveSvg?.Actions.ReleaseClip();

    public bool HasImageObjectSelected => ActiveSvg?.Selection.Primary is SvgImage;

    /// <summary>Opens the pixels of the selected image in a new raster tab; saving that tab (or Update Drawing) sends them back.</summary>
    [RelayCommand(CanExecute = nameof(HasImageObjectSelected))]
    private async Task EditBitmap()
    {
        if (ActiveSvg is not { Selection.Primary: SvgImage image } drawing)
            return;
        try
        {
            SvgBitmapEditing.Open(_workspace, drawing, image);
        }
        catch (InvalidOperationException e)
        {
            await (Dialogs?.ShowErrorAsync("Could not edit the picture", e.Message) ?? Task.CompletedTask);
        }
    }

    public bool CanUpdateDrawing => ActiveImageTab?.Image is { BitmapEdit: not null } doc && SvgBitmapEditing.CanUpdate(doc);

    [RelayCommand(CanExecute = nameof(CanUpdateDrawing))]
    private void UpdateDrawing()
    {
        if (ActiveImageTab?.Image is { } doc)
            SvgBitmapEditing.Update(doc);
    }

    /// <summary>After saving a picture that edits a drawing's image: the drawing gets the new pixels too.</summary>
    private static void PushBitmapToDrawing(CinnabarSharp.Core.Models.ImageDocument doc)
    {
        if (doc.BitmapEdit is not null)
            SvgBitmapEditing.Update(doc);
    }
}
