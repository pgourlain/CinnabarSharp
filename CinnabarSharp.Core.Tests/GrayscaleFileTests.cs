using Microsoft.Extensions.DependencyInjection;
using ImageMagick;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

/// <summary>A grayscale file must open as a color layer, or every colored pixel written into it turns gray.</summary>
public sealed class GrayscaleFileTests : BaseTests, IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cinnabarsharp-gray-");

    public void Dispose() => _dir.Delete(recursive: true);

    private FileInfo GrayFile(string extension)
    {
        var path = Path.Combine(_dir.FullName, "gray" + extension);
        using var image = new MagickImage(MagickColors.White, 40, 30) { ColorType = ColorType.Grayscale };
        image.Write(path);
        using var check = new MagickImage(path);
        Assert.True(check.ColorSpace is ColorSpace.Gray or ColorSpace.LinearGray, $"test file is {check.ColorSpace}");
        return new FileInfo(path);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    public void A_grayscale_file_opens_as_a_color_layer(string extension)
    {
        var document = CinnabarSharpService().GetRequiredService<IFormatManager>().Open(GrayFile(extension)).AsImage();

        Assert.Equal(ColorSpace.sRGB, document.Layers.CurrentUserLayer.Surface.ColorSpace);
    }

    [Fact]
    public void Colored_pixels_written_into_a_grayscale_file_stay_colored()
    {
        var document = CinnabarSharpService().GetRequiredService<IFormatManager>().Open(GrayFile(".jpg")).AsImage();
        var red = new byte[4] { 40, 40, 230, 255 };

        document.Layers.CurrentUserLayer.Surface.WriteRegion(new RectangleI(3, 4, 1, 1), red);

        Assert.Equal(red, document.Layers.CurrentUserLayer.Surface.ReadRegion(new RectangleI(3, 4, 1, 1)));
    }

    [Fact]
    public void A_grayscale_file_imported_as_a_layer_is_color_too()
    {
        var service = CinnabarSharpService();
        var document = service.GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(40, 30), ColorBgra.White);

        var layer = document.Actions.ImportFromFile(GrayFile(".png"));

        Assert.Equal(ColorSpace.sRGB, layer.Surface.ColorSpace);
    }
}
