using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>
/// "Prepare for TV": resolution, how the photo fills the 16:9 screen, the border background, and optionally a
/// second photo shown side by side. For a folder (batch) there is no current photo, so no size warning.
/// </summary>
public partial class PrepareForTvViewModel : ViewModelBase
{
    private readonly ImageSize? _photo;
    private readonly RectangleI? _crop;

    public PrepareForTvViewModel(TvOptions options, ImageSize? photo = null, RectangleI? crop = null,
        IReadOnlyList<DocumentViewModel>? otherPhotos = null, string? folder = null)
    {
        _photo = photo;
        _crop = crop;
        Folder = folder;
        OtherPhotos = otherPhotos ?? [];
        SecondPhoto = OtherPhotos.FirstOrDefault();
        Resolution = options.Resolution;
        Fit = options.Fit;
        Background = options.Background;
    }

    public sealed record Option<T>(T Value, string Label);

    public static IReadOnlyList<Option<TvResolution>> Resolutions { get; } =
    [
        new(TvResolution.FullHd, "Full HD / 2K (1920 × 1080)"),
        new(TvResolution.Uhd4K, "4K UHD (3840 × 2160)"),
        new(TvResolution.Uhd8K, "8K UHD (7680 × 4320)"),
    ];

    public static IReadOnlyList<Option<TvFit>> Fits { get; } =
    [
        new(TvFit.CropToFill, "Crop to fill the screen"),
        new(TvFit.FitWithBorders, "Fit with borders"),
        new(TvFit.Stretch, "Stretch"),
    ];

    public static IReadOnlyList<Option<TvBackground>> Backgrounds { get; } =
    [
        new(TvBackground.Black, "Black"),
        new(TvBackground.White, "White"),
        new(TvBackground.Blurred, "Blurred photo"),
    ];

    public Option<TvResolution> SelectedResolution
    {
        get => Resolutions.First(o => o.Value == Resolution);
        set => Resolution = value.Value;
    }

    public Option<TvFit> SelectedFit
    {
        get => Fits.First(o => o.Value == Fit);
        set => Fit = value.Value;
    }

    public Option<TvBackground> SelectedBackground
    {
        get => Backgrounds.First(o => o.Value == Background);
        set => Background = value.Value;
    }

    partial void OnResolutionChanged(TvResolution value) => OnPropertyChanged(nameof(SelectedResolution));
    partial void OnFitChanged(TvFit value) => OnPropertyChanged(nameof(SelectedFit));
    partial void OnBackgroundChanged(TvBackground value) => OnPropertyChanged(nameof(SelectedBackground));

    /// <summary>Set in batch mode: every photo of this folder is exported.</summary>
    public string? Folder { get; }
    public bool IsBatch => Folder is not null;
    public string Title => IsBatch ? "Prepare Folder for TV" : "Prepare for TV";

    public IReadOnlyList<DocumentViewModel> OtherPhotos { get; }
    public bool CanSideBySide => !IsBatch && OtherPhotos.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText), nameof(Warning), nameof(HasWarning))]
    public partial TvResolution Resolution { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning), nameof(ShowBackground), nameof(CropText))]
    public partial TvFit Fit { get; set; }

    [ObservableProperty]
    public partial TvBackground Background { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning), nameof(ShowBackground), nameof(CropText))]
    public partial bool SideBySide { get; set; }

    [ObservableProperty]
    public partial DocumentViewModel? SecondPhoto { get; set; }

    public TvOptions Options => new(Resolution, Fit, Background);

    public bool ShowBackground => Fit == TvFit.FitWithBorders && !SideBySide;

    public string ResultText
    {
        get
        {
            var size = TvExport.SizeOf(Resolution);
            return $"{size.Width} × {size.Height} JPEG, file name ending in {TvExport.Suffix(Resolution)}";
        }
    }

    public string? CropText => Fit == TvFit.CropToFill && !SideBySide && !IsBatch
        ? _crop is null ? "Keeps the center of the photo (draw a 16:9 frame with the Crop tool to choose)." : "Keeps the area of the crop frame or selection."
        : null;

    /// <summary>Shown when the photo has fewer pixels than the TV and will be enlarged.</summary>
    public string? Warning
    {
        get
        {
            if (_photo is not { } photo || SideBySide)
                return null;
            var factor = TvExport.UpscaleFactor(photo.Width, photo.Height, Options, _crop);
            return factor > 1.05
                ? $"The photo is enlarged {factor:0.0}×: it has fewer pixels than a {TvExport.Suffix(Resolution).TrimStart('_')} TV, so it may look soft."
                : null;
        }
    }

    public bool HasWarning => Warning is not null;
}

/// <summary>JPEG quality asked before saving a JPEG (1–100).</summary>
public partial class JpegQualityViewModel(int quality) : ViewModelBase
{
    [ObservableProperty]
    public partial double Quality { get; set; } = quality;
}
