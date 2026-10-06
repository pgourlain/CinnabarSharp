using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public sealed class LayerOperationsTests : BaseTests, IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cinnabarsharp-layers-");
    private readonly IServiceProvider _sp;
    private readonly IWorkspaceService _workspace;

    public LayerOperationsTests()
    {
        _sp = CinnabarSharpService();
        _workspace = _sp.GetRequiredService<IWorkspaceService>();
    }

    public void Dispose() => _dir.Delete(recursive: true);

    /// <summary>3 layers, bottom to top: "Background" white, "Red" half-opacity red, "Blue" hidden blue.</summary>
    private ImageDocument ThreeLayers()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);
        Fill(doc.Layers.AddNewLayer("Red"), ColorBgra.FromBgra(0, 0, 255, 255));
        doc.Layers[1].Opacity = 0.5;
        doc.Layers.SetCurrentUserLayer(1);
        Fill(doc.Layers.AddNewLayer("Blue"), ColorBgra.FromBgra(255, 0, 0, 255));
        doc.Layers[2].Hidden = true;
        doc.Layers.SetCurrentUserLayer(2);
        return doc;
    }

    [Fact]
    public void Flattened_thumbnail_is_the_reduced_composite_of_the_visible_layers()
    {
        var doc = _workspace.NewDocument(new ImageSize(80, 40), ColorBgra.White);
        Fill(doc.Layers.AddNewLayer("Red"), ColorBgra.FromBgra(0, 0, 255, 255));
        doc.Layers[1].Opacity = 0.5;
        Fill(doc.Layers.AddNewLayer("Blue"), ColorBgra.FromBgra(255, 0, 0, 255));
        doc.Layers[2].Hidden = true;

        var (bgra, width, height) = doc.Layers.GetFlattenedThumbnail(20);

        Assert.Equal((20, 10), (width, height));
        Assert.Equal(width * height * 4, bgra.Length);
        // Half red over white (the hidden blue layer is left out), the same as the full-size composite.
        var full = doc.Layers.GetFlattenedBgra(includeToolLayer: false);
        var i = (5 * width + 10) * 4;
        Assert.InRange(bgra[i + 2], full[2] - 2, full[2] + 2);
        Assert.InRange(bgra[i + 1], full[1] - 2, full[1] + 2);
        Assert.InRange(bgra[i], full[0] - 2, full[0] + 2);
        Assert.Equal(255, bgra[i + 3]);
    }

    [Fact]
    public void Flattened_thumbnail_is_never_larger_than_the_image()
    {
        var doc = _workspace.NewDocument(new ImageSize(6, 4), ColorBgra.White);

        var (_, width, height) = doc.Layers.GetFlattenedThumbnail(44);

        Assert.Equal((6, 4), (width, height));
    }

    private static void Fill(Layer layer, ColorBgra color)
    {
        var w = (int)layer.Surface.Width;
        var h = (int)layer.Surface.Height;
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = color.B;
            px[i + 1] = color.G;
            px[i + 2] = color.R;
            px[i + 3] = color.A;
        }
        layer.Surface.Dispose();
        layer.Surface = Utility.FromBgra(px, w, h);
    }

    private static byte[] Pixel(IMagickImage<byte> image, int x = 1, int y = 1)
    {
        var bgra = image.ToBgra();
        var i = (y * (int)image.Width + x) * 4;
        return bgra[i..(i + 4)];
    }

    private static string[] Names(ImageDocument doc) => doc.Layers.UserLayers.Select(l => l.Name).ToArray();

    [Fact]
    public void Flattened_image_applies_opacity_and_skips_hidden_layers()
    {
        var doc = ThreeLayers();

        using var flat = doc.GetFlattenedImage();

        Assert.Equal(new byte[] { 127, 127, 255, 255 }, Pixel(flat));
    }

    [Fact]
    public void Flattened_image_applies_blend_mode()
    {
        var doc = ThreeLayers();
        doc.Layers[1].Opacity = 1;
        doc.Layers[1].BlendMode = BlendMode.Difference;

        using var flat = doc.GetFlattenedImage();

        // white - red = cyan
        Assert.Equal(new byte[] { 255, 255, 0, 255 }, Pixel(flat));
    }

    [Fact]
    public void Duplicate_copies_pixels_and_properties_above_current()
    {
        var doc = ThreeLayers();
        doc.Layers.SetCurrentUserLayer(1);

        var copy = doc.Layers.DuplicateCurrentLayer();

        Assert.Equal(["Background", "Red", "Red copy", "Blue"], Names(doc));
        Assert.Same(copy, doc.Layers.CurrentUserLayer);
        Assert.Equal(0.5, copy.Opacity);
        Assert.Equal(Pixel(doc.Layers[1].Surface), Pixel(copy.Surface));
        Assert.NotSame(doc.Layers[1].Surface, copy.Surface);
    }

    [Fact]
    public void Delete_selects_layer_below_and_keeps_last_layer()
    {
        var doc = ThreeLayers();
        doc.Layers.SetCurrentUserLayer(1);

        doc.Layers.DeleteCurrentLayer();
        Assert.Equal(["Background", "Blue"], Names(doc));
        Assert.Equal("Background", doc.Layers.CurrentUserLayer.Name);

        doc.Layers.DeleteCurrentLayer();
        Assert.Equal(["Blue"], Names(doc));
        Assert.Throws<InvalidOperationException>(() => doc.Layers.DeleteCurrentLayer());
    }

    [Fact]
    public void Move_up_and_down_keep_the_layer_selected()
    {
        var doc = ThreeLayers();
        doc.Layers.SetCurrentUserLayer(0);

        doc.Layers.MoveCurrentLayerUp();
        Assert.Equal(["Red", "Background", "Blue"], Names(doc));
        Assert.Equal("Background", doc.Layers.CurrentUserLayer.Name);

        doc.Layers.MoveCurrentLayerUp();
        Assert.Equal(["Red", "Blue", "Background"], Names(doc));
        Assert.Throws<InvalidOperationException>(() => doc.Layers.MoveCurrentLayerUp());

        doc.Layers.MoveCurrentLayerDown();
        Assert.Equal(["Red", "Background", "Blue"], Names(doc));
        Assert.Equal(1, doc.Layers.CurrentUserLayerIndex);
    }

    [Fact]
    public void Merge_down_composites_with_opacity_into_layer_below()
    {
        var doc = ThreeLayers();
        doc.Layers.SetCurrentUserLayer(1);

        doc.Layers.MergeCurrentLayerDown();

        Assert.Equal(["Background", "Blue"], Names(doc));
        Assert.Equal("Background", doc.Layers.CurrentUserLayer.Name);
        Assert.Equal(new byte[] { 127, 127, 255, 255 }, Pixel(doc.Layers[0].Surface));
    }

    [Fact]
    public void Flatten_merges_everything_into_one_visible_layer()
    {
        var doc = ThreeLayers();

        doc.Layers.FlattenLayers();

        Assert.Equal(["Background"], Names(doc));
        Assert.Equal(new byte[] { 127, 127, 255, 255 }, Pixel(doc.Layers[0].Surface));
    }

    [Fact]
    public void Import_from_file_adds_layer_cropped_to_canvas()
    {
        var doc = ThreeLayers();
        doc.Layers.SetCurrentUserLayer(0);

        var layer = doc.Layers.ImportFromFile(ImageSample1());

        Assert.Equal(["Background", "sample1.png", "Red", "Blue"], Names(doc));
        Assert.Same(layer, doc.Layers.CurrentUserLayer);
        Assert.Equal(4u, layer.Surface.Width);
        Assert.Equal(3u, layer.Surface.Height);
    }

    private static byte[] Px(byte[] bgra, int width, int x, int y) => bgra.AsSpan(((y * width) + x) * 4, 4).ToArray();

    private static FileInfo LogoSvg() => new(Path.Combine(AppContext.BaseDirectory, "Data", "SampleFiles", "logo.svg"));

    [Fact]
    public void Import_svg_keeps_natural_size_when_it_fits_the_canvas()
    {
        var doc = _workspace.NewDocument(new ImageSize(800, 600), ColorBgra.White);

        var layer = doc.Layers.ImportFromFile(LogoSvg());

        var bgra = layer.Surface.ToBgra();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Px(bgra, 800, 100, 100)); // red square
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Px(bgra, 800, 300, 100)); // blue circle
        Assert.Equal(0, Px(bgra, 800, 600, 100)[3]); // outside the logo: transparent
    }

    [Fact]
    public void Import_svg_larger_than_canvas_is_scaled_down_to_fit()
    {
        var doc = _workspace.NewDocument(new ImageSize(200, 100), ColorBgra.White);

        var layer = doc.Layers.ImportFromFile(LogoSvg());

        var bgra = layer.Surface.ToBgra();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Px(bgra, 200, 50, 50));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Px(bgra, 200, 150, 50));
    }

    [Fact]
    public void Flip_current_layer_mirrors_pixels()
    {
        var doc = _workspace.NewDocument(new ImageSize(2, 1), ColorBgra.White);
        doc.Layers[0].Surface.Dispose();
        doc.Layers[0].Surface = Utility.FromBgra([0, 0, 255, 255, 255, 0, 0, 255], 2, 1);

        doc.Layers.FlipCurrentLayerHorizontal();

        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 }, doc.Layers[0].Surface.ToBgra());
    }

    [Fact]
    public void Ora_round_trip_keeps_layers_and_properties()
    {
        var doc = ThreeLayers();
        doc.Layers[1].BlendMode = BlendMode.Glow;
        var file = new FileInfo(Path.Combine(_dir.FullName, "layers.ora"));
        var formats = _sp.GetRequiredService<IFormatManager>();

        formats.Save(doc, file);
        _workspace.CloseDocument(doc);
        var reopened = formats.Open(new FileInfo(file.FullName));

        Assert.Equal(new ImageSize(4, 3), reopened.ImageSize);
        Assert.Equal(["Background", "Red", "Blue"], Names(reopened));
        Assert.Equal(0.5, reopened.Layers[1].Opacity);
        Assert.Equal(BlendMode.Glow, reopened.Layers[1].BlendMode);
        Assert.True(reopened.Layers[2].Hidden);
        Assert.False(reopened.Layers[0].Hidden);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(reopened.Layers[2].Surface));
        Assert.Equal("Blue", reopened.Layers.CurrentUserLayer.Name);
        Assert.False(reopened.IsDirty);
    }

    [Fact]
    public void Ora_is_recognised_by_content_and_has_merged_image()
    {
        var doc = ThreeLayers();
        var file = new FileInfo(Path.Combine(_dir.FullName, "layers.ora"));
        var formats = _sp.GetRequiredService<IFormatManager>();
        formats.Save(doc, file);

        var renamed = Path.Combine(_dir.FullName, "renamed.bin");
        File.Copy(file.FullName, renamed);
        Assert.Equal("OraFormat", formats.GetFormatForFile(new FileInfo(renamed))?.Name);

        using var zip = System.IO.Compression.ZipFile.OpenRead(file.FullName);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.NotNull(zip.GetEntry("mergedimage.png"));
        Assert.NotNull(zip.GetEntry("Thumbnails/thumbnail.png"));
    }

    [Theory]
    [InlineData(BlendMode.Multiply, "svg:multiply")]
    [InlineData(BlendMode.Additive, "svg:plus")]
    [InlineData(BlendMode.Reflect, "pdn:reflect")]
    public void Ora_composite_ops(BlendMode mode, string op)
    {
        Assert.Equal(op, OraFormat.ToCompositeOp(mode));
        Assert.Equal(mode, OraFormat.FromCompositeOp(op));
        Assert.Equal(BlendMode.Normal, OraFormat.FromCompositeOp("svg:unknown"));
    }
}
