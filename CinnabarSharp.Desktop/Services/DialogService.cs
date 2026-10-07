using System;
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

    public Task ShowErrorAsync(string title, string message)
    {
        AppLog.Error("dialog", $"{title}: {message}");
        return new MessageWindow("CinnabarSharp", title, message, ["OK"], defaultIndex: 0, cancelIndex: 0).ShowDialog(owner);
    }

    public async Task OpenFolderAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }

    public async Task OpenUrlAsync(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            await owner.Launcher.LaunchUriAsync(uri);
    }

    public async Task<bool> ShowLayerPropertiesAsync(LayerPropertiesViewModel properties) =>
        await new LayerPropertiesWindow { DataContext = properties }.ShowDialog<bool?>(owner) == true;

    public async Task<bool> ShowEffectAsync(EffectDialogViewModel adjustment) =>
        await new EffectWindow { DataContext = adjustment }.ShowDialog<bool?>(owner) == true;

    public async Task<bool> ShowCurvesAsync(CurvesDialogViewModel curves) =>
        await new CurvesWindow { DataContext = curves }.ShowDialog<bool?>(owner) == true;

    public async Task<bool> ShowLevelsAsync(LevelsDialogViewModel levels) =>
        await new LevelsWindow { DataContext = levels }.ShowDialog<bool?>(owner) == true;

    public async Task<bool> ShowPhotoFilterAsync(PhotoFilterDialogViewModel filters) =>
        await new PhotoFilterWindow { DataContext = filters }.ShowDialog<bool?>(owner) == true;

    public async Task<bool> ShowPrepareForTvAsync(PrepareForTvViewModel options) =>
        await new PrepareForTvWindow { DataContext = options }.ShowDialog<bool?>(owner) == true;

    public async Task<int?> AskJpegQualityAsync(int current)
    {
        var quality = new JpegQualityViewModel(current);
        return await new JpegQualityWindow { DataContext = quality }.ShowDialog<bool?>(owner) == true
            ? (int)System.Math.Round(quality.Quality)
            : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public Task ShowMessageAsync(string title, string message) =>
        new MessageWindow("CinnabarSharp", title, message, ["OK"], defaultIndex: 0, cancelIndex: 0).ShowDialog(owner);

    public Task<CinnabarSharp.Core.Vector.SvgExportOptions?> ShowSvgExportAsync(SvgExportViewModel options) =>
        new SvgExportWindow { DataContext = options }.ShowDialog<CinnabarSharp.Core.Vector.SvgExportOptions?>(owner);

    public Task<ResizeImageOptions?> ShowResizeImageAsync(ImageSize current) =>
        new ResizeImageWindow { DataContext = new ResizeImageViewModel(current) }.ShowDialog<ResizeImageOptions?>(owner);

    public Task<CanvasSizeOptions?> ShowCanvasSizeAsync(ImageSize current) =>
        new CanvasSizeWindow { DataContext = new CanvasSizeViewModel(current) }.ShowDialog<CanvasSizeOptions?>(owner);

    public Task<PasteBesideOptions?> ShowPasteBesideAsync(ImageSize current, ImageSize pasted) =>
        new PasteBesideWindow { DataContext = new PasteBesideViewModel(current, pasted) }.ShowDialog<PasteBesideOptions?>(owner);

    public Task<bool> ShowComicPageAsync(ComicPageViewModel comic) =>
        new ComicPageWindow { DataContext = comic }.ShowDialog<bool>(owner);

    public Task ShowAgentConnectionAsync(AgentConnectionViewModel connection) =>
        new AgentConnectionWindow { DataContext = connection }.ShowDialog(owner);

    public Task<Avalonia.Media.Color?> PickColorAsync(string title, Avalonia.Media.Color initial) =>
        new ColorPickerWindow(title, initial).ShowDialog<Avalonia.Media.Color?>(owner);

    private static IEnumerable<string> Patterns(ImageFormat format) =>
        format.SupportedExtensions.Select(e => "*." + e);

    private static FilePickerFileType ToFileType(ImageFormat format) =>
        new(format.DisplayName) { Patterns = Patterns(format).ToList() };
}
