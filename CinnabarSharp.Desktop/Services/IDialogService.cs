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
}
