using System.Collections.Generic;
using System.Threading.Tasks;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Services;

public enum SaveChangesChoice
{
    Save,
    DontSave,
    Cancel,
}

public interface IDialogService
{
    Task<NewImageOptions?> ShowNewImageAsync(ImageSize suggested);
    Task ShowAboutAsync();

    /// <summary>Returns local paths of the chosen files; empty when cancelled.</summary>
    Task<IReadOnlyList<string>> PickFilesToOpenAsync(IReadOnlyList<ImageFormat> formats);

    /// <summary>Returns the chosen local path, or null when cancelled.</summary>
    Task<string?> PickFileToSaveAsync(string suggestedName, ImageFormat suggestedFormat, IReadOnlyList<ImageFormat> formats);

    Task<SaveChangesChoice> AskSaveChangesAsync(string documentName);

    /// <summary>Returns true to continue.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel);

    Task ShowErrorAsync(string title, string message);

    /// <summary>The dialog edits the layer live; returns true for OK, false for Cancel.</summary>
    Task<bool> ShowLayerPropertiesAsync(LayerPropertiesViewModel properties);

    /// <summary>The dialog previews live; returns true for OK.</summary>
    Task<bool> ShowEffectAsync(EffectDialogViewModel adjustment);

    Task<bool> ShowCurvesAsync(CurvesDialogViewModel curves);

    Task<bool> ShowLevelsAsync(LevelsDialogViewModel levels);

    Task<bool> ShowPhotoFilterAsync(PhotoFilterDialogViewModel filters);

    /// <summary>Returns true for OK; the choices stay in the view model.</summary>
    Task<bool> ShowPrepareForTvAsync(PrepareForTvViewModel options);

    /// <summary>Returns the JPEG quality (1–100), or null when cancelled.</summary>
    Task<int?> AskJpegQualityAsync(int current);

    /// <summary>Returns the chosen folder's local path, or null when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    Task ShowMessageAsync(string title, string message);

    Task<ResizeImageOptions?> ShowResizeImageAsync(ImageSize current);
    Task<CanvasSizeOptions?> ShowCanvasSizeAsync(ImageSize current);

    /// <summary>Page de BD: photos, page format and layout. Returns true for OK.</summary>
    Task<bool> ShowComicPageAsync(ComicPageViewModel comic);

    /// <summary>How to connect an AI agent to this window (MCP attached mode).</summary>
    Task ShowAgentConnectionAsync(AgentConnectionViewModel connection);

    /// <summary>Returns the chosen color, or null when cancelled.</summary>
    Task<Avalonia.Media.Color?> PickColorAsync(string title, Avalonia.Media.Color initial);
}
