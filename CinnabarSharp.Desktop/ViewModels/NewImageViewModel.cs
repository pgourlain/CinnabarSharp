using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Vector;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>The size of a new vector drawing, in its unit.</summary>
public record SvgDrawingOptions(double Width, double Height, SvgUnit Unit);

/// <summary>What File › New asks for: a raster image (<see cref="Svg"/> null) or an SVG drawing.</summary>
public record NewImageOptions(ImageSize Size, ColorBgra Background, SvgDrawingOptions? Svg = null);

public partial class NewImageViewModel : ViewModelBase
{
    public const int MaxDimension = 16384;

    public NewImageViewModel(ImageSize suggested)
    {
        Width = suggested.Width;
        Height = suggested.Height;
    }

    [ObservableProperty]
    public partial decimal? Width { get; set; }

    [ObservableProperty]
    public partial decimal? Height { get; set; }

    [ObservableProperty]
    public partial bool TransparentBackground { get; set; }

    /// <summary>True for "SVG drawing": the size gets a unit and the background does not apply.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImage), nameof(Title), nameof(UnitText))]
    public partial bool IsSvg { get; set; }

    public bool IsImage => !IsSvg;

    public string Title => IsSvg ? "New SVG Drawing" : "New Image";

    public static IReadOnlyList<SvgUnit> Units { get; } = Enum.GetValues<SvgUnit>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitText))]
    public partial SvgUnit Unit { get; set; } = SvgUnit.Px;

    /// <summary>The unit shown next to the size fields.</summary>
    public string UnitText => !IsSvg ? "pixels" : Unit switch { SvgUnit.Mm => "mm", SvgUnit.In => "in", _ => "px" };

    public int Max => MaxDimension;

    public NewImageOptions? ToOptions()
    {
        if (Width is not { } w || Height is not { } h || w <= 0 || h <= 0)
            return null;
        if (IsSvg)
        {
            var scale = Unit switch { SvgUnit.Mm => 96 / 25.4, SvgUnit.In => 96.0, _ => 1.0 };
            if ((double)w * scale > MaxDimension || (double)h * scale > MaxDimension)
                return null;
            var pixels = new ImageSize(Math.Max(1, (int)Math.Ceiling((double)w * scale)), Math.Max(1, (int)Math.Ceiling((double)h * scale)));
            return new NewImageOptions(pixels, ColorBgra.Transparent, new SvgDrawingOptions((double)w, (double)h, Unit));
        }
        if (w < 1 || h < 1 || w > MaxDimension || h > MaxDimension)
            return null;
        return new NewImageOptions(
            new ImageSize((int)w, (int)h),
            TransparentBackground ? ColorBgra.Transparent : ColorBgra.White);
    }
}
