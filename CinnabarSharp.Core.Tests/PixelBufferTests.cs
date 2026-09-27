using ImageMagick;
using CinnabarSharp.Core.Extensions;

namespace CinnabarSharp.Core.Tests;

public class PixelBufferTests
{
    [Fact]
    public void FromBgra_reads_blue_green_red_alpha_order()
    {
        using var image = Utility.FromBgra([10, 20, 30, 40], 1, 1);

        var color = image.GetPixels().GetPixel(0, 0).ToColor()!;

        Assert.Equal((byte)30, color.R);
        Assert.Equal((byte)20, color.G);
        Assert.Equal((byte)10, color.B);
        Assert.Equal((byte)40, color.A);
    }

    [Fact]
    public void ToBgra_writes_blue_green_red_alpha_order()
    {
        using var image = new MagickImage(new MagickColor(30, 20, 10, 40), 1, 1);

        Assert.Equal(new byte[] { 10, 20, 30, 40 }, image.ToBgra());
    }

    [Fact]
    public void ToBgra_of_image_without_alpha_is_opaque()
    {
        using var image = new MagickImage(new MagickColor(30, 20, 10), 1, 1);
        image.Alpha(AlphaOption.Off);

        Assert.Equal(new byte[] { 10, 20, 30, 255 }, image.ToBgra());
    }
}
