using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public class MagickImageDecoderTests
{
    private static byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "svg", name));

    [Fact]
    public void Decodes_a_png_with_its_colors()
    {
        var image = new MagickImageDecoder().Decode(Sample("linked.png"));
        Assert.NotNull(image);
        Assert.Equal((8, 8), (image.Width, image.Height));
        Assert.Equal(8 * 8 * 4, image.Bgra.Length);
        Assert.Equal((20, 60, 200, 255), (image.Bgra[0], image.Bgra[1], image.Bgra[2], image.Bgra[3])); // #c83c14 as BGRA
    }

    [Fact]
    public void Decodes_the_embedded_png_of_a_svg()
    {
        var root = SvgParser.ParseFile(Path.Combine(AppContext.BaseDirectory, "Data", "svg", "image.svg")).Root;
        var href = ((SvgImage)root.FindById("emb")!).Href!;
        var bytes = ImageResolver.Resolve(href, null)!;
        var image = new MagickImageDecoder().Decode(bytes)!;
        Assert.Equal((4, 4), (image.Width, image.Height));
        Assert.Equal((220, 120, 20), (image.Bgra[0], image.Bgra[1], image.Bgra[2]));
    }

    [Fact]
    public void Refuses_what_is_not_a_picture_or_not_a_supported_format()
    {
        var decoder = new MagickImageDecoder();
        Assert.Null(decoder.Decode([]));
        Assert.Null(decoder.Decode("<svg xmlns='http://www.w3.org/2000/svg'/>"u8));
        Assert.Null(decoder.Decode("push graphic-context\nviewbox 0 0 10 10\nfill red\nrectangle 0,0 5,5\npop graphic-context"u8));
        Assert.Null(decoder.Decode([0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 1, 2, 3]));  // PNG signature, garbage after
        Assert.Null(decoder.Decode([0x42, 0x4D, 0, 0, 0, 0]));                        // BMP is not accepted here
    }

    [Fact]
    public void Linked_images_render_through_the_decoder()
    {
        var root = SvgParser.ParseFile(Path.Combine(AppContext.BaseDirectory, "Data", "svg", "image.svg")).Root;
        var options = new RenderOptions { ImageDecoder = new MagickImageDecoder(), BaseFolder = Path.Combine(AppContext.BaseDirectory, "Data", "svg") };
        var (pixels, width, _) = VectorRasterizer.RenderAll(root, 1, options);
        var linked = PixelOf(pixels, width, 75, 25);
        Assert.Equal((200, 60, 20, 255), (linked.R, linked.G, linked.B, linked.A));
        var embedded = PixelOf(pixels, width, 25, 25);
        Assert.Equal((20, 120, 220, 255), (embedded.R, embedded.G, embedded.B, embedded.A));
    }

    private static (byte R, byte G, byte B, byte A) PixelOf(byte[] bgra, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (bgra[i + 2], bgra[i + 1], bgra[i], bgra[i + 3]);
    }
}
