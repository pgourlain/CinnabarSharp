using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Desktop.ViewModels;

public record ResizeImageOptions(ImageSize Size, ResamplingMode Resampling);
public record CanvasSizeOptions(ImageSize Size, Anchor Anchor);
public record PasteBesideOptions(PasteSide Side, EdgeAlignment Alignment);

/// <summary>Size by percentage or absolute pixels, keeping the aspect ratio when asked (Paint.NET's dialogs).</summary>
public abstract partial class SizeDialogViewModel : ViewModelBase
{
    public const int MaxDimension = 65535;

    private bool _updating;

    protected SizeDialogViewModel(ImageSize current)
    {
        Current = current;
        Width = current.Width;
        Height = current.Height;
    }

    public ImageSize Current { get; }
    public string CurrentSizeText => $"{Current.Width} × {Current.Height} pixels";

    [ObservableProperty]
    public partial bool ByPercentage { get; set; }

    [ObservableProperty]
    public partial decimal? Percentage { get; set; } = 100;

    [ObservableProperty]
    public partial decimal? Width { get; set; }

    [ObservableProperty]
    public partial decimal? Height { get; set; }

    [ObservableProperty]
    public partial bool MaintainAspectRatio { get; set; } = true;

    public bool ByAbsoluteSize
    {
        get => !ByPercentage;
        set => ByPercentage = !value;
    }

    public ImageSize? NewSize
    {
        get
        {
            if (ByPercentage)
            {
                if (Percentage is not { } p || p <= 0)
                    return null;
                return Valid((int)Math.Round(Current.Width * p / 100), (int)Math.Round(Current.Height * p / 100));
            }
            return Width is { } w && Height is { } h ? Valid((int)w, (int)h) : null;
        }
    }

    private static ImageSize? Valid(int w, int h) =>
        w >= 1 && h >= 1 && w <= MaxDimension && h <= MaxDimension ? new ImageSize(w, h) : null;

    partial void OnByPercentageChanged(bool value) => OnPropertyChanged(nameof(ByAbsoluteSize));

    partial void OnPercentageChanged(decimal? value)
    {
        if (_updating || value is not { } p)
            return;
        Sync(() =>
        {
            Width = Math.Round(Current.Width * p / 100);
            Height = Math.Round(Current.Height * p / 100);
        });
    }

    partial void OnWidthChanged(decimal? value)
    {
        if (_updating || !MaintainAspectRatio || value is not { } w || Current.Width == 0)
            return;
        Sync(() => Height = Math.Max(1, Math.Round(w * Current.Height / Current.Width)));
    }

    partial void OnHeightChanged(decimal? value)
    {
        if (_updating || !MaintainAspectRatio || value is not { } h || Current.Height == 0)
            return;
        Sync(() => Width = Math.Max(1, Math.Round(h * Current.Width / Current.Height)));
    }

    private void Sync(Action update)
    {
        _updating = true;
        update();
        _updating = false;
    }
}

public partial class ResizeImageViewModel(ImageSize current) : SizeDialogViewModel(current)
{
    public static IReadOnlyList<ResamplingMode> ResamplingModes { get; } = Enum.GetValues<ResamplingMode>();

    [ObservableProperty]
    public partial ResamplingMode Resampling { get; set; } = ResamplingMode.BestQuality;

    public ResizeImageOptions? ToOptions() => NewSize is { } size ? new ResizeImageOptions(size, Resampling) : null;
}

public partial class CanvasSizeViewModel : SizeDialogViewModel
{
    public CanvasSizeViewModel(ImageSize current) : base(current)
    {
        MaintainAspectRatio = false;
    }

    [ObservableProperty]
    public partial Anchor Anchor { get; set; } = Anchor.Center;

    [RelayCommand]
    private void SetAnchor(Anchor anchor) => Anchor = anchor;

    public CanvasSizeOptions? ToOptions() => NewSize is { } size ? new CanvasSizeOptions(size, Anchor) : null;
}

/// <summary>Paste Beside: which side of the image the clipboard image goes to, and how the shorter one is aligned.</summary>
public partial class PasteBesideViewModel(ImageSize current, ImageSize pasted) : ViewModelBase
{
    public string CurrentSizeText => $"Image: {current.Width} × {current.Height} pixels";
    public string PastedSizeText => $"Pasted: {pasted.Width} × {pasted.Height} pixels";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSizeText), nameof(StartLabel), nameof(MiddleLabel), nameof(EndLabel))]
    public partial PasteSide Side { get; set; } = PasteSide.Right;

    [ObservableProperty]
    public partial EdgeAlignment Alignment { get; set; } = EdgeAlignment.Middle;

    private bool Vertical => Side is PasteSide.Left or PasteSide.Right;
    public string StartLabel => Vertical ? "Top" : "Left";
    public string MiddleLabel => Vertical ? "Middle" : "Center";
    public string EndLabel => Vertical ? "Bottom" : "Right";

    public string ResultSizeText
    {
        get
        {
            var size = PasteBesideLayout.For(current, pasted, Side, Alignment).Size;
            return $"New size: {size.Width} × {size.Height} pixels";
        }
    }

    [RelayCommand]
    private void SetSide(PasteSide side) => Side = side;

    [RelayCommand]
    private void SetAlignment(EdgeAlignment alignment) => Alignment = alignment;

    public PasteBesideOptions ToOptions() => new(Side, Alignment);
}

/// <summary>
/// Export of an SVG drawing as a picture (also Image › Rasterize): by percentage of its size or by pixels with the
/// ratio locked or not, on a transparent or a white background.
/// </summary>
public partial class SvgExportViewModel(ImageSize current, string title, string okText) : SizeDialogViewModel(current)
{
    public string Title { get; } = title;

    public string OkText { get; } = okText;

    [ObservableProperty]
    public partial bool TransparentBackground { get; set; } = true;

    public bool WhiteBackground
    {
        get => !TransparentBackground;
        set => TransparentBackground = !value;
    }

    partial void OnTransparentBackgroundChanged(bool value) => OnPropertyChanged(nameof(WhiteBackground));

    public CinnabarSharp.Core.Vector.SvgExportOptions? ToOptions() => NewSize is { } size
        ? new CinnabarSharp.Core.Vector.SvgExportOptions(Width: size.Width, Height: size.Height,
            Background: TransparentBackground ? null : CinnabarSharp.Vector.VColor.White)
        : null;
}
