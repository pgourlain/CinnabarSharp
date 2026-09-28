using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public class ImageTransformsTests
{
    // 3 × 2 image, pixel values 1..6 in the blue channel, row by row:
    // 1 2 3
    // 4 5 6
    private static readonly byte[] Src = Enumerable.Range(1, 6).SelectMany(v => new[] { (byte)v, (byte)0, (byte)0, (byte)255 }).ToArray();

    private static int[] Blue(byte[] px) => Enumerable.Range(0, px.Length / 4).Select(i => (int)px[i * 4]).ToArray();

    [Fact]
    public void Flips()
    {
        Assert.Equal([3, 2, 1, 6, 5, 4], Blue(ImageTransforms.FlipHorizontal(Src, 3, 2)));
        Assert.Equal([4, 5, 6, 1, 2, 3], Blue(ImageTransforms.FlipVertical(Src, 3, 2)));
        Assert.Equal([6, 5, 4, 3, 2, 1], Blue(ImageTransforms.Rotate180(Src, 3, 2)));
    }

    [Fact]
    public void Rotate_clockwise_and_counter_clockwise()
    {
        // Clockwise → 2 × 3:  4 1 / 5 2 / 6 3
        Assert.Equal([4, 1, 5, 2, 6, 3], Blue(ImageTransforms.Rotate90(Src, 3, 2, clockwise: true)));
        // Counter-clockwise → 2 × 3:  3 6 / 2 5 / 1 4
        Assert.Equal([3, 6, 2, 5, 1, 4], Blue(ImageTransforms.Rotate90(Src, 3, 2, clockwise: false)));
    }

    [Theory]
    [InlineData(Anchor.NW, 0, 0)]
    [InlineData(Anchor.Center, 1, 2)]
    [InlineData(Anchor.SE, 2, 4)]
    [InlineData(Anchor.E, 2, 2)]
    public void Anchor_offsets(Anchor anchor, int x, int y)
    {
        Assert.Equal(new PointI(x, y), ImageTransforms.AnchorOffset(new ImageSize(3, 2), new ImageSize(5, 6), anchor));
    }

    [Fact]
    public void Resize_canvas_fills_new_area_and_crops_when_shrinking()
    {
        var bigger = ImageTransforms.ResizeCanvas(Src, new ImageSize(3, 2), new ImageSize(4, 2), Anchor.E,
            ColorBgra.FromBgra(9, 0, 0, 255));
        Assert.Equal([9, 1, 2, 3, 9, 4, 5, 6], Blue(bigger));

        var smaller = ImageTransforms.ResizeCanvas(Src, new ImageSize(3, 2), new ImageSize(1, 1), Anchor.Center,
            ColorBgra.Transparent);
        Assert.Equal([2], Blue(smaller));
    }

    [Fact]
    public void Nearest_neighbor_resample_doubles_pixels()
    {
        using var image = Utility.FromBgra(Src, 3, 2);
        using var doubled = ImageTransforms.Resample(image, new ImageSize(6, 4), ResamplingMode.NearestNeighbor);

        Assert.Equal((6u, 4u), (doubled.Width, doubled.Height));
        Assert.Equal([1, 1, 2, 2, 3, 3], Blue(doubled.ToBgra())[..6]);
    }
}

public sealed class ImageMenuActionsTests : BaseTests
{
    private readonly IWorkspaceService _workspace;

    public ImageMenuActionsTests() => _workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();

    [Fact]
    public void Canvas_size_uses_background_color_only_on_bottom_layer()
    {
        var doc = _workspace.NewDocument(new ImageSize(2, 2), ColorBgra.White);
        doc.Actions.AddNewLayer();

        doc.Actions.ResizeCanvas(new ImageSize(4, 2), Anchor.W, ColorBgra.FromBgra(0, 255, 0, 255));

        Assert.Equal(new ImageSize(4, 2), doc.ImageSize);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, doc.Layers[0].Surface.ReadRegion(new RectangleI(3, 0, 1, 1)));
        Assert.Equal(0, doc.Layers[1].Surface.ReadRegion(new RectangleI(3, 0, 1, 1))[3]);
        Assert.Equal("Canvas Size", doc.Workspace.History.Items[^1].Text);
    }

    [Fact]
    public void Zoom_is_kept_exactly_when_the_view_size_is_rounded()
    {
        var doc = _workspace.NewDocument(new ImageSize(1023, 767), ColorBgra.White);
        doc.Workspace.Scale = 0.25;

        Assert.Equal(0.25, doc.Workspace.Scale);
        Assert.Equal(new ImageSize(256, 192), doc.Workspace.ViewSize);

        // Resizing many times must not make the zoom drift.
        for (var i = 0; i < 10; i++)
            doc.Actions.RotateImage90(clockwise: true);
        Assert.Equal(0.25, doc.Workspace.Scale);
        Assert.Equal(new ImageSize(256, 192), doc.Workspace.ViewSize);
    }

    [Fact]
    public void Rotating_a_non_square_image_swaps_its_size_and_keeps_zoom()
    {
        var doc = _workspace.NewDocument(new ImageSize(40, 20), ColorBgra.White);
        doc.Workspace.Scale = 2;

        doc.Actions.RotateImage90(clockwise: true);

        Assert.Equal(new ImageSize(20, 40), doc.ImageSize);
        Assert.Equal(new ImageSize(40, 80), doc.Workspace.ViewSize);
        doc.Workspace.History.Undo();
        Assert.Equal(new ImageSize(40, 20), doc.ImageSize);
    }

    [Fact]
    public void Resize_image_scales_every_layer()
    {
        var doc = _workspace.NewDocument(new ImageSize(10, 10), ColorBgra.White);
        doc.Actions.AddNewLayer();

        doc.Actions.ResizeImage(new ImageSize(25, 5), ResamplingMode.BestQuality);

        Assert.All(doc.Layers.UserLayers, l => Assert.Equal((25u, 5u), (l.Surface.Width, l.Surface.Height)));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, doc.Layers[0].Surface.ReadRegion(new RectangleI(12, 2, 1, 1)));
    }
}
