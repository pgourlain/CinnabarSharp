using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Services;

public class DialogService(Window owner) : IDialogService
{
    public Task<NewImageOptions?> ShowNewImageAsync(ImageSize suggested) =>
        new NewImageWindow { DataContext = new NewImageViewModel(suggested) }
            .ShowDialog<NewImageOptions?>(owner);

    public Task ShowAboutAsync() => new AboutWindow().ShowDialog(owner);

    public async Task<IReadOnlyList<string>> PickFilesToOpenAsync(IReadOnlyList<ImageFormat> formats)
    {
        var allImages = new FilePickerFileType("All images")
        {
            Patterns = formats.SelectMany(Patterns).ToList(),
        };
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open",
            AllowMultiple = true,
            FileTypeFilter = [allImages, .. formats.Select(ToFileType), FilePickerFileTypes.All],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickFileToSaveAsync(string suggestedName, ImageFormat suggestedFormat,
        IReadOnlyList<ImageFormat> formats)
    {
        var suggestedType = ToFileType(suggestedFormat);
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = Path.ChangeExtension(suggestedName, suggestedFormat.SupportedExtensions[0]),
            DefaultExtension = suggestedFormat.SupportedExtensions[0],
            FileTypeChoices = [suggestedType, .. formats.Where(f => f != suggestedFormat).Select(ToFileType)],
            SuggestedFileType = suggestedType,
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<SaveChangesChoice> AskSaveChangesAsync(string documentName)
    {
        var window = new MessageWindow("CinnabarSharp", $"Save changes to \"{documentName}\"?",
            "If you don't save, your changes will be lost.",
            ["Save", "Don't Save", "Cancel"], defaultIndex: 0, cancelIndex: 2);
        var result = await window.ShowDialog<int?>(owner) ?? window.CancelIndex;
        return (SaveChangesChoice)result;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        var window = new MessageWindow("CinnabarSharp", title, message, [confirmLabel, "Cancel"], defaultIndex: 0, cancelIndex: 1);
        return await window.ShowDialog<int?>(owner) == 0;
    }

    public Task ShowErrorAsync(string title, string message) =>
        new MessageWindow("CinnabarSharp", title, message, ["OK"], defaultIndex: 0, cancelIndex: 0).ShowDialog(owner);

    public async Task<bool> ShowLayerPropertiesAsync(LayerPropertiesViewModel properties) =>
        await new LayerPropertiesWindow { DataContext = properties }.ShowDialog<bool?>(owner) == true;

    private static IEnumerable<string> Patterns(ImageFormat format) =>
        format.SupportedExtensions.Select(e => "*." + e);

    private static FilePickerFileType ToFileType(ImageFormat format) =>
        new(format.DisplayName) { Patterns = Patterns(format).ToList() };
}
