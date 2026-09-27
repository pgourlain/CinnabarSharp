using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;
using PointD = CinnabarSharp.Core.Models.PointD;

namespace CinnabarSharp.Core.Tests;

public sealed class CropAndTvTests : BaseTests, IDisposable
{
    private readonly IServiceProvider _sp;
    private readonly ToolSettings _settings = new();
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cinnabarsharp-tv-");

    public CropAndTvTests() => _sp = CinnabarSharpService();

    public void Dispose() => _dir.Delete(recursive: true);

    private ImageDocument NewDoc(int w, int h, ColorBgra? color = null) =>
        _sp.GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(w, h), color ?? ColorBgra.White);

    private static void Drag(ITool tool, ImageDocument doc, params (double X, double Y)[] points)
    {
        ToolPointer P((double X, double Y) p) => new(new PointD(p.X, p.Y), ToolButton.Left, ToolModifiers.None);
        tool.OnPointerDown(doc, P(points[0]));
        foreach (var p in points.Skip(1))
            tool.OnPointerMove(doc, P(p));
        tool.OnPointerUp(doc, P(points[^1]));
    }

    // ---- Crop tool ----

    [Fact]
    public void Crop_frame_keeps_16_9_and_stays_inside_the_image()
    {
        var doc = NewDoc(400, 300);
        var tool = new CropTool(_settings);

        Drag(tool, doc, (10, 10), (170, 40));
        Assert.Equal(new RectangleI(10, 10, 160, 90), tool.Frame(doc));

        Drag(tool, doc, (300, 200), (500, 500)); // Dragged past the corner: shrunk to fit.
        var frame = tool.Frame(doc)!.Value;
        Assert.Equal(300 + 100, frame.X + frame.Width);
        Assert.Equal(16 / 9.0, (double)frame.Width / frame.Height, 1);
        Assert.True(frame.Y + frame.Height <= 300);
    }

    [Fact]
    public void Frame_moves_resizes_from_a_corner_and_enter_crops()
    {
        var doc = NewDoc(400, 300);
        var tool = new CropTool(_settings);
        Drag(tool, doc, (10, 10), (170, 100));
        Assert.Equal(new RectangleI(10, 10, 160, 90), tool.Frame(doc));

        Drag(tool, doc, (50, 50), (100, 80)); // Inside: move.
        Assert.Equal(new RectangleI(60, 40, 160, 90), tool.Frame(doc));

        Drag(tool, doc, (220, 130), (380, 130)); // Bottom-right corner: resize, top-left stays.
        Assert.Equal(new RectangleI(60, 40, 320, 180), tool.Frame(doc));
        Assert.NotNull(tool.GetOverlay(doc)!.Shade);
        Assert.Equal(4, tool.GetOverlay(doc)!.Lines.Count);

        Assert.True(tool.OnKeyDown(doc, ToolKey.Enter, ToolModifiers.None));
        Assert.Equal(new ImageSize(320, 180), doc.ImageSize);
        Assert.False(tool.IsEditing(doc));
        Assert.Equal("Crop", doc.Workspace.History.Items[^1].Text);

        doc.Workspace.History.Undo();
        Assert.Equal(new ImageSize(400, 300), doc.ImageSize);
    }

    [Fact]
    public void Escape_removes_the_frame_and_changing_the_ratio_refits_it()
    {
        var doc = NewDoc(400, 300);
        var tool = new CropTool(_settings);
        Drag(tool, doc, (0, 0), (160, 90));

        _settings.CropAspect = CropAspect.Square;
        tool.Refresh(doc);
        var square = tool.Frame(doc)!.Value;
        Assert.Equal(square.Width, square.Height);

        _settings.CropAspect = CropAspect.Free;
        Drag(tool, doc, (10, 10), (50, 200));
        Assert.Equal(new RectangleI(10, 10, 40, 190), tool.Frame(doc));

        Assert.True(tool.OnKeyDown(doc, ToolKey.Escape, ToolModifiers.None));
        Assert.Null(tool.Frame(doc));
        Assert.Equal(new ImageSize(400, 300), doc.ImageSize);
    }

    // ---- TV ----

    private static BgraImage Solid(int w, int h, byte b, byte g, byte r)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
            (px[i], px[i + 1], px[i + 2], px[i + 3]) = (b, g, r, 255);
        return new BgraImage(px, w, h);
    }

    /// <summary>Left half red, right half blue.</summary>
    private static BgraImage Halves(int w, int h)
    {
        var image = Solid(w, h, 0, 0, 255);
        for (var y = 0; y < h; y++)
            for (var x = w / 2; x < w; x++)
                (image.Pixels[(y * w + x) * 4], image.Pixels[(y * w + x) * 4 + 2]) = (255, 0);
        return image;
    }

    private static (byte B, byte G, byte R) At(BgraImage image, int x, int y)
    {
        var i = (y * image.Width + x) * 4;
        return (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]);
    }

    [Theory]
    [InlineData(TvResolution.FullHd, 1920, 1080)]
    [InlineData(TvResolution.Uhd4K, 3840, 2160)]
    public void Every_fit_produces_the_tv_size(TvResolution resolution, int w, int h)
    {
        var photo = Halves(300, 400);
        foreach (var fit in Enum.GetValues<TvFit>())
        {
            var result = TvExport.Compose(photo, new TvOptions(resolution, fit));
            Assert.Equal((w, h), (result.Width, result.Height));
            Assert.Equal(w * h * 4, result.Pixels.Length);
        }
        Assert.Equal(new ImageSize(7680, 4320), TvExport.SizeOf(TvResolution.Uhd8K));
        Assert.Equal("_8K", TvExport.Suffix(TvResolution.Uhd8K));
    }

    [Fact]
    public void Crop_to_fill_keeps_the_crop_area()
    {
        var photo = Halves(320, 180);

        var left = TvExport.Compose(photo, new TvOptions(TvResolution.FullHd, TvFit.CropToFill), new RectangleI(0, 0, 150, 180));

        Assert.Equal((0, 0, 255), At(left, 100, 500));
        Assert.Equal((0, 0, 255), At(left, 1800, 500));
    }

    [Fact]
    public void Fit_with_borders_centers_the_photo_on_the_background()
    {
        var portrait = Solid(300, 600, 0, 200, 0);

        var black = TvExport.Compose(portrait, new TvOptions(TvResolution.FullHd, TvFit.FitWithBorders));
        Assert.Equal((0, 0, 0), At(black, 50, 540));
        Assert.Equal((0, 200, 0), At(black, 960, 540));

        var blurred = TvExport.Compose(Halves(300, 600), new TvOptions(TvResolution.FullHd, TvFit.FitWithBorders, TvBackground.Blurred));
        var border = At(blurred, 50, 540);
        Assert.True(border.R > 60 && border.R < 255, border.ToString()); // A darkened, blurred copy of the red half.
    }

    [Fact]
    public void Side_by_side_puts_one_photo_in_each_half()
    {
        var result = TvExport.SideBySide(Solid(300, 600, 0, 0, 255), Solid(300, 600, 255, 0, 0),
            new TvOptions(TvResolution.FullHd, TvFit.CropToFill));

        Assert.Equal((1920, 1080), (result.Width, result.Height));
        Assert.Equal((0, 0, 255), At(result, 400, 540));
        Assert.Equal((255, 0, 0), At(result, 1500, 540));
    }

    [Fact]
    public void Upscale_factor_tells_when_the_photo_is_too_small()
    {
        var options = new TvOptions(TvResolution.Uhd4K, TvFit.CropToFill);
        Assert.Equal(2, TvExport.UpscaleFactor(1920, 1080, options), 3);
        Assert.True(TvExport.UpscaleFactor(8000, 6000, options) < 1);
        Assert.Equal(new RectangleI(0, 75, 400, 225), TvExport.CenteredCrop(400, 375, 16 / 9.0));
    }

    [Fact]
    public void Folder_export_writes_srgb_jpegs_and_skips_other_files()
    {
        new MagickImage(MagickColors.Orange, 400, 300).Write(Path.Combine(_dir.FullName, "a.png"));
        new MagickImage(MagickColors.Teal, 300, 400).Write(Path.Combine(_dir.FullName, "b.jpg"));
        File.WriteAllText(Path.Combine(_dir.FullName, "notes.txt"), "not a photo");

        var (output, count) = TvExport.ExportFolder(_dir, new TvOptions(TvResolution.FullHd, TvFit.FitWithBorders), 80);

        Assert.Equal(2, count);
        Assert.Equal("TV 2K", output.Name);
        using var a = new MagickImage(Path.Combine(output.FullName, "a_2K.jpg"));
        Assert.Equal((1920u, 1080u), (a.Width, a.Height));
        Assert.NotNull(a.GetColorProfile());
        Assert.InRange(a.Quality, 75u, 85u);
        Assert.True(File.Exists(Path.Combine(output.FullName, "b_2K.jpg")));
    }

    [Fact]
    public void Saved_jpegs_use_the_quality_and_carry_an_srgb_profile()
    {
        var doc = NewDoc(64, 48, ColorBgra.FromBgra(30, 120, 200, 255));
        var formats = _sp.GetRequiredService<IFormatManager>();
        var jpeg = (JpegFormat)formats.GetFormatByExtension("jpg")!;
        var file = new FileInfo(Path.Combine(_dir.FullName, "q.jpg"));

        jpeg.Quality = 60;
        formats.Save(doc, file, jpeg);

        using var saved = new MagickImage(file);
        Assert.InRange(saved.Quality, 55u, 65u);
        Assert.Equal("icc", saved.GetColorProfile()!.Name);
        jpeg.Quality = JpegFormat.DefaultQuality;
    }
}
