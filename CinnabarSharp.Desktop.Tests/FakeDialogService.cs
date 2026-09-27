using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

/// <summary>Scripted answers for dialogs; records what was asked.</summary>
public class FakeDialogService : IDialogService
{
    public Queue<IReadOnlyList<string>> FilesToOpen { get; } = new();
    public Queue<string?> SavePaths { get; } = new();
    public Queue<SaveChangesChoice> SaveChangesAnswers { get; } = new();
    public Queue<bool> ConfirmAnswers { get; } = new();

    public List<string> Errors { get; } = [];
    public List<string> SaveChangesAsked { get; } = [];
    public List<string> Confirmations { get; } = [];
    public string? LastSuggestedSaveName { get; private set; }
    public ImageFormat? LastSuggestedSaveFormat { get; private set; }

    public Task<NewImageOptions?> ShowNewImageAsync(ImageSize suggested) =>
        Task.FromResult<NewImageOptions?>(new NewImageOptions(suggested, ColorBgra.White));

    public Task ShowAboutAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<string>> PickFilesToOpenAsync(IReadOnlyList<ImageFormat> formats) =>
        Task.FromResult(FilesToOpen.TryDequeue(out var files) ? files : (IReadOnlyList<string>)[]);

    public Task<string?> PickFileToSaveAsync(string suggestedName, ImageFormat suggestedFormat,
        IReadOnlyList<ImageFormat> formats)
    {
        LastSuggestedSaveName = suggestedName;
        LastSuggestedSaveFormat = suggestedFormat;
        return Task.FromResult(SavePaths.TryDequeue(out var path) ? path : null);
    }

    public Task<SaveChangesChoice> AskSaveChangesAsync(string documentName)
    {
        SaveChangesAsked.Add(documentName);
        return Task.FromResult(SaveChangesAnswers.TryDequeue(out var a) ? a : SaveChangesChoice.Cancel);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        Confirmations.Add(title);
        return Task.FromResult(ConfirmAnswers.TryDequeue(out var a) && a);
    }

    /// <summary>Edits applied to the properties dialog, then whether the user clicks OK.</summary>
    public Func<LayerPropertiesViewModel, bool> LayerPropertiesAnswer { get; set; } = _ => false;

    public Task<bool> ShowLayerPropertiesAsync(LayerPropertiesViewModel properties) =>
        Task.FromResult(LayerPropertiesAnswer(properties));

    /// <summary>Changes made in the adjustment dialog, then whether OK is clicked.</summary>
    public Func<EffectDialogViewModel, bool> EffectAnswer { get; set; } = _ => true;

    public List<string> EffectsShown { get; } = [];

    public Task<bool> ShowEffectAsync(EffectDialogViewModel adjustment)
    {
        EffectsShown.Add(adjustment.Title);
        return Task.FromResult(EffectAnswer(adjustment));
    }

    public Func<CurvesDialogViewModel, bool> CurvesAnswer { get; set; } = _ => true;

    public Task<bool> ShowCurvesAsync(CurvesDialogViewModel curves)
    {
        EffectsShown.Add(curves.Title);
        return Task.FromResult(CurvesAnswer(curves));
    }

    public Func<LevelsDialogViewModel, bool> LevelsAnswer { get; set; } = _ => true;

    public Task<bool> ShowLevelsAsync(LevelsDialogViewModel levels)
    {
        EffectsShown.Add(levels.Title);
        return Task.FromResult(LevelsAnswer(levels));
    }

    public Queue<ResizeImageOptions?> ResizeAnswers { get; } = new();
    public Queue<CanvasSizeOptions?> CanvasSizeAnswers { get; } = new();

    public Task<ResizeImageOptions?> ShowResizeImageAsync(ImageSize current) =>
        Task.FromResult(ResizeAnswers.TryDequeue(out var a) ? a : null);

    public Task<CanvasSizeOptions?> ShowCanvasSizeAsync(ImageSize current) =>
        Task.FromResult(CanvasSizeAnswers.TryDequeue(out var a) ? a : null);

    public Queue<Avalonia.Media.Color?> ColorAnswers { get; } = new();

    public Task<Avalonia.Media.Color?> PickColorAsync(string title, Avalonia.Media.Color initial) =>
        Task.FromResult(ColorAnswers.TryDequeue(out var c) ? c : null);

    public Task ShowErrorAsync(string title, string message)
    {
        Errors.Add(title);
        return Task.CompletedTask;
    }
}
