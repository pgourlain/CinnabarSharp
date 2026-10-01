using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public class PasteBesideTests : BaseTests
{
    private static readonly ColorBgra Red = ColorBgra.FromBgra(0, 0, 255, 255);
    private static readonly ColorBgra Green = ColorBgra.FromBgra(0, 200, 0, 255);
    private static readonly ColorBgra Background = ColorBgra.FromBgra(200, 100, 50, 255);

    [Theory]
    // 4 × 2 image, 3 × 6 pasted: the pasted image is taller, so the image is aligned along it.
    [InlineData(PasteSide.Right, EdgeAlignment.Start, 7, 6, 0, 0, 4, 0)]
    [InlineData(PasteSide.Right, EdgeAlignment.Middle, 7, 6, 0, 2, 4, 0)]
    [InlineData(PasteSide.Right, EdgeAlignment.End, 7, 6, 0, 4, 4, 0)]
    [InlineData(PasteSide.Left, EdgeAlignment.Middle, 7, 6, 3, 2, 0, 0)]
    // Above or below: the image is wider, so the pasted image is aligned along it.
    [InlineData(PasteSide.Bottom, EdgeAlignment.Start, 4, 8, 0, 0, 0, 2)]
    [InlineData(PasteSide.Bottom, EdgeAlignment.End, 4, 8, 0, 0, 1, 2)]
    [InlineData(PasteSide.Top, EdgeAlignment.Middle, 4, 8, 0, 6, 0, 0)]
    public void Layout_puts_both_images_side_by_side_and_aligns_the_shorter_one(PasteSide side, EdgeAlignment alignment,
        int w, int h, int imageX, int imageY, int pastedX, int pastedY)
    {
        var layout = PasteBesideLayout.For(new ImageSize(4, 2), new ImageSize(3, 6), side, alignment);

        Assert.Equal(new PasteBesideLayout(new ImageSize(w, h), new PointI(imageX, imageY), new PointI(pastedX, pastedY)), layout);
    }

    private ImageDocument NewDoc(int w, int h) =>
        CinnabarSharpService().GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(w, h), Red);

    private static ClipboardImage Solid(int w, int h, ColorBgra c) =>
        new(Enumerable.Range(0, w * h).SelectMany(_ => new[] { c.B, c.G, c.R, c.A }).ToArray(), w, h);

    private static ColorBgra At(byte[] bgra, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return ColorBgra.FromBgra(bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]);
    }

    [Fact]
    public void Pastes_into_a_new_layer_beside_the_image_and_grows_the_canvas()
    {
        var doc = NewDoc(4, 2);
        var top = doc.Actions.AddNewLayer();

        var layer = doc.Actions.PasteBeside(Solid(3, 6, Green), PasteSide.Right, EdgeAlignment.Middle, Background);

        Assert.Equal(new ImageSize(7, 6), doc.ImageSize);
        Assert.Equal(3, doc.Layers.UserLayers.Count);
        Assert.Same(layer, doc.Layers.CurrentUserLayer);
        Assert.Equal(new RectangleI(4, 0, 3, 6), doc.Selection!.Bounds);
        Assert.Equal("Paste Beside", doc.Workspace.History.Items[^1].Text);

        var bottom = doc.Layers[0].Surface.ToBgra();
        Assert.Equal(Red, At(bottom, 7, 0, 2));          // the image, centered vertically
        Assert.Equal(Background, At(bottom, 7, 0, 0));   // new area of the bottom layer
        Assert.Equal(Background, At(bottom, 7, 5, 3));   // under the pasted image
        Assert.Equal(0, At(top.Surface.ToBgra(), 7, 0, 0).A); // other layers: transparent
        var pasted = layer.Surface.ToBgra();
        Assert.Equal(Green, At(pasted, 7, 4, 0));
        Assert.Equal(Green, At(pasted, 7, 6, 5));
        Assert.Equal(0, At(pasted, 7, 3, 3).A);

        doc.Workspace.History.Undo();
        Assert.Equal(new ImageSize(4, 2), doc.ImageSize);
        Assert.Equal(2, doc.Layers.UserLayers.Count);
        Assert.Null(doc.Selection);
        Assert.Equal(Red, At(doc.Layers[0].Surface.ToBgra(), 4, 0, 0));
    }
}
