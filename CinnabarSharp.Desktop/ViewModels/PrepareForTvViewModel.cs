using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>
/// "Prepare for TV" options: resolution, how the photo fills the 16:9 screen, the border background, and optionally
/// a second photo shown side by side. For the current photo they are in the options bar while a 16:9 frame on the
/// canvas chooses what is kept (<see cref="Crop"/>); for a folder (batch) they are in a dialog, with no size warning.
/// </summary>
public partial class PrepareForTvViewModel : ViewModelBase
{
    private readonly ImageSize? _photo;

    public PrepareForTvViewModel(TvOptions options, ImageSize? photo = null, RectangleI? crop = null,
        IReadOnlyList<DocumentViewModel>? otherPhotos = null, string? folder = null)
    {
        _photo = photo;
        Crop = crop;
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
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning), nameof(ShowBackground), nameof(ShowsFrame), nameof(Hint))]
    public partial TvFit Fit { get; set; }

    [ObservableProperty]
    public partial TvBackground Background { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning), nameof(ShowBackground), nameof(ShowsFrame), nameof(Hint))]
    public partial bool SideBySide { get; set; }

    /// <summary>The area kept by Crop to fill (the frame on the canvas), in image pixels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning))]
    public partial RectangleI? Crop { get; set; }

    /// <summary>What the canvas shows, for the options bar.</summary>
    public string Hint
    {
        get
        {
            if (SideBySide)
                return "Each photo fills half of the screen.";
            if (Fit == TvFit.FitWithBorders)
                return "The whole photo is scaled to fit the screen without distortion; the borders fill the rest.";
            if (Fit == TvFit.Stretch && _photo is { } p)
            {
                var stretch = 16 / 9.0 / ((double)p.Width / p.Height);
                return Math.Abs(stretch - 1) < 0.01
                    ? "The photo already has the screen's shape: it is not distorted."
                    : stretch > 1
                        ? $"The whole photo fills the screen, stretched {stretch - 1:P0} wider."
                        : $"The whole photo fills the screen, stretched {1 / stretch - 1:P0} taller.";
            }
            return "Drag the frame to choose what the TV shows; drag a corner to resize it.";
        }
    }

    /// <summary>Crop to fill of the current photo: the canvas shows the 16:9 frame to place.</summary>
    public bool ShowsFrame => !IsBatch && Fit == TvFit.CropToFill && !SideBySide;

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

    /// <summary>Shown when the photo has fewer pixels than the TV and will be enlarged.</summary>
    public string? Warning
    {
        get
        {
            if (_photo is not { } photo || SideBySide)
                return null;
            var factor = TvExport.UpscaleFactor(photo.Width, photo.Height, Options, Crop);
            return factor > 1.05
                ? $"Enlarged {factor:0.0}×: fewer pixels than a {TvExport.Suffix(Resolution).TrimStart('_')} TV, may look soft."
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
