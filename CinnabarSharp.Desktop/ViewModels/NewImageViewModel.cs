using CommunityToolkit.Mvvm.ComponentModel;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Desktop.ViewModels;

public record NewImageOptions(ImageSize Size, ColorBgra Background);

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

    public int Max => MaxDimension;

    public NewImageOptions? ToOptions()
    {
        if (Width is not { } w || Height is not { } h || w < 1 || h < 1 || w > MaxDimension || h > MaxDimension)
            return null;
        return new NewImageOptions(
            new ImageSize((int)w, (int)h),
            TransparentBackground ? ColorBgra.Transparent : ColorBgra.White);
    }
}
