using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class SvgPageTests : VectorToolTestBase
{
    [Fact]
    public void Resize_scales_the_page_with_its_content_and_undo_restores_everything()
    {
        var doc = Open("<rect id='r' x='0' y='0' width='100' height='100' fill='#ff0000'/>");
        var before = Xml(doc);
        var steps = Steps(doc);
        doc.Actions.ResizePage(new ImageSize(400, 400), keepContentAt: null);

        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal(new ImageSize(400, 400), doc.ImageSize);
        Assert.Equal(400, doc.Workspace.ViewSize.Width);
        Assert.Equal(new VRect(0, 0, 200, 200), doc.Root.ViewBox);     // the user space is unchanged
        var picture = VectorRasterizer.RenderAll(doc.Root, 1, doc.RenderOptions);
        Assert.Equal((400, 400), (picture.Width, picture.Height));
        Assert.Equal(255, picture.Bgra[(150 * 400 + 150) * 4 + 3]);     // the 100 × 100 square now covers 200 × 200

        doc.History.Undo();
        Assert.Equal(before, Xml(doc));
        Assert.Equal(new ImageSize(200, 200), doc.ImageSize);
        Assert.Equal(200, doc.Workspace.ViewSize.Width);
    }

    [Fact]
    public void A_different_proportion_stretches_the_content()
    {
        var doc = Open("<rect id='r' width='200' height='200' fill='#ff0000'/>");
        doc.Actions.ResizePage(new ImageSize(400, 100), null);
        var picture = VectorRasterizer.RenderAll(doc.Root, 1, doc.RenderOptions);
        Assert.Equal(255, picture.Bgra[(99 * 400 + 399) * 4 + 3]);      // filled to the corner: no letterbox
        Assert.Equal(255, picture.Bgra[0 * 4 + 3]);
    }

    [Fact]
    public void Canvas_size_keeps_the_content_and_grows_the_page_around_the_anchor()
    {
        var doc = Open("<rect id='r' x='0' y='0' width='100' height='100' fill='#ff0000'/>");
        doc.Actions.ResizePage(new ImageSize(300, 200), Anchor.Center);

        Assert.Equal(new ImageSize(300, 200), doc.ImageSize);
        Assert.Equal(new VRect(-50, 0, 300, 200), doc.Root.ViewBox);
        var picture = VectorRasterizer.RenderAll(doc.Root, 1, doc.RenderOptions);
        Assert.Equal(0, picture.Bgra[(50 * 300 + 20) * 4 + 3]);          // the new margin is empty
        Assert.Equal(255, picture.Bgra[(50 * 300 + 100) * 4 + 3]);       // the square keeps its size, at x = 50..150
        Assert.Equal(0, picture.Bgra[(50 * 300 + 160) * 4 + 3]);

        // Anchored at the top left, the page shrinks from the right and bottom.
        doc.Actions.ResizePage(new ImageSize(150, 120), Anchor.NW);
        Assert.Equal(new ImageSize(150, 120), doc.ImageSize);
        Assert.Equal(new VRect(-50, 0, 150, 120), doc.Root.ViewBox);     // the left edge stays where it was
    }

    [Fact]
    public void The_unit_of_the_page_is_kept_and_a_page_with_no_viewbox_gets_one()
    {
        var sp = CinnabarSharpService();
        var doc = sp.GetRequiredService<IWorkspaceService>().NewSvgDocument(210, 297, SvgUnit.Mm);
        var before = doc.ImageSize;
        doc.Actions.ResizePage(new ImageSize(before.Width * 2, before.Height * 2), null);
        Assert.Equal(LengthUnit.Mm, doc.Root.Width!.Value.Unit);
        Assert.InRange(doc.Root.Width.Value.Value, 419.5, 420.5);   // the pixel size is rounded up, so not exactly twice

        var plain = sp.GetRequiredService<IWorkspaceService>().OpenSvgDocument(
            SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='50'><rect width='10' height='10'/></svg>").Root, null, null);
        plain.Actions.ResizePage(new ImageSize(200, 100), Anchor.NW);
        Assert.Equal(new VRect(0, 0, 200, 100), plain.Root.ViewBox);
        Assert.Equal(new ImageSize(200, 100), plain.ImageSize);
    }

    [Fact]
    public void Invalid_sizes_are_refused_and_the_same_size_does_nothing()
    {
        var doc = Open("<rect width='10' height='10'/>");
        var steps = Steps(doc);
        Assert.Throws<ArgumentException>(() => doc.Actions.ResizePage(new ImageSize(0, 10), null));
        Assert.Throws<ArgumentException>(() => doc.Actions.ResizePage(new ImageSize(70000, 10), null));
        doc.Actions.ResizePage(new ImageSize(200, 200), Anchor.Center);
        Assert.Equal(steps, Steps(doc));
    }
}
