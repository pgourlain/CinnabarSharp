using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public sealed class HeicTests : BaseTests, IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cinnabarsharp-heic-");
    private readonly IFormatManager _formats;

    public HeicTests() => _formats = CinnabarSharpService().GetRequiredService<IFormatManager>();

    public void Dispose() => _dir.Delete(recursive: true);

    private static FileInfo HeicSample() => new(Path.Combine(AppContext.BaseDirectory, "Data", "SampleFiles", "sample1.heic"));

    private static byte[] PixelAt(ImageDocument doc, int x, int y)
    {
        var bgra = doc.Layers[0].Surface.ToBgra();
        var i = (y * doc.ImageSize.Width + x) * 4;
        return bgra[i..(i + 4)];
    }

    [Fact]
    public void Opens_heic_photo()
    {
        var doc = _formats.Open(HeicSample()).AsImage();

        Assert.Equal(new ImageSize(1024, 576), doc.ImageSize);
        Assert.Equal("heic", doc.FileType);
        // Center of the red disc in the sample (227, 66, 52), within HEVC compression error.
        var px = PixelAt(doc, 330, 290);
        Assert.InRange(px[2], 215, 240);
        Assert.InRange(px[1], 55, 80);
        Assert.InRange(px[0], 40, 65);
        Assert.Equal(255, px[3]);
    }

    [Theory]
    [InlineData("heic")]
    [InlineData(".HEIF")]
    public void Heic_and_heif_extensions_map_to_the_read_only_format(string extension)
    {
        var format = _formats.GetFormatByExtension(extension)!;

        Assert.Equal("HeicFormat", format.Name);
        Assert.False(format.SupportsSaving);
        Assert.DoesNotContain(format, _formats.SaveFormats);
    }

    [Fact]
    public void Heic_is_recognised_by_content()
    {
        var renamed = new FileInfo(Path.Combine(_dir.FullName, "photo.bin"));
        HeicSample().CopyTo(renamed.FullName);

        Assert.Equal("HeicFormat", _formats.GetFormatForFile(renamed)?.Name);
    }

    [Fact]
    public void Saving_as_heic_is_refused()
    {
        var doc = _formats.Open(HeicSample()).AsImage();

        Assert.Throws<NotSupportedException>(() =>
            _formats.Save(doc, new FileInfo(Path.Combine(_dir.FullName, "out.heic"))));
    }

    [Fact]
    public void Images_with_a_color_profile_are_converted_to_srgb_on_open()
    {
        var file = Path.Combine(_dir.FullName, "apple-rgb.png");
        using (var image = new MagickImage(new MagickColor(128, 64, 32), 4, 4))
        {
            image.SetProfile(ColorProfiles.AppleRGB);
            image.Write(file);
        }

        var doc = _formats.Open(new FileInfo(file)).AsImage();

        var px = PixelAt(doc, 1, 1);
        Assert.NotEqual(new byte[] { 32, 64, 128, 255 }, px);
        Assert.True(px[2] > px[1] && px[1] > px[0], "hue must be preserved");
    }
}
