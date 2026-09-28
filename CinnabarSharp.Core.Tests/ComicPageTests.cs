using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests;

public sealed class ComicPageTests : BaseTests
{
    private static ComicLayout Layout(string name) => ComicPage.Layouts.Single(l => l.Name == name);

    private static BgraImage Solid(int w, int h, byte b, byte g, byte r)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
            (px[i], px[i + 1], px[i + 2], px[i + 3]) = (b, g, r, 255);
        return new BgraImage(px, w, h);
    }

    private static byte[] At(BgraImage image, int x, int y) => image.Pixels.AsSpan((y * image.Width + x) * 4, 4).ToArray();

    [Fact]
    public void Gutters_separate_the_panels_and_frame_the_page()
    {
        var rects = ComicPage.PanelRects(Layout("2 × 2 grid"), new ImageSize(1000, 800), 20);

        Assert.Equal(
        [
            new RectangleI(20, 20, 470, 370), new RectangleI(510, 20, 470, 370),
            new RectangleI(20, 410, 470, 370), new RectangleI(510, 410, 470, 370),
        ], rects);
    }

    [Fact]
    public void Every_layout_fits_the_page_without_overlapping_panels()
    {
        var page = new ImageSize(2480, 3508);
        foreach (var layout in ComicPage.Layouts)
        {
            var rects = ComicPage.PanelRects(layout, page, 40);
            Assert.Equal(layout.Panels.Count, rects.Count);
            foreach (var r in rects)
            {
                Assert.True(r.X >= 40 && r.Y >= 40 && r.X + r.Width <= page.Width - 40 && r.Y + r.Height <= page.Height - 40, layout.Name);
                Assert.True(r.Width > 100 && r.Height > 100, layout.Name);
            }
            for (var i = 0; i < rects.Count; i++)
                for (var j = i + 1; j < rects.Count; j++)
                {
                    var (a, b) = (rects[i], rects[j]);
                    var overlap = a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
                    Assert.False(overlap, $"{layout.Name}: panels {i} and {j} overlap");
                }
        }
    }

    [Fact]
    public void Visible_area_has_the_panel_shape_zooms_and_stays_in_the_photo()
    {
        // 400 × 300 photo in a square panel: the largest square, centered.
        Assert.Equal(new RectangleD(50, 0, 300, 300), ComicPage.VisibleArea(400, 300, 100, 100, 1, new PointD(0.5, 0.5)));
        // Zoom 2: half the size, still centered.
        Assert.Equal(new RectangleD(125, 75, 150, 150), ComicPage.VisibleArea(400, 300, 100, 100, 2, new PointD(0.5, 0.5)));
        // Centered on a corner: kept inside the photo.
        Assert.Equal(new RectangleD(0, 0, 150, 150), ComicPage.VisibleArea(400, 300, 100, 100, 2, new PointD(0, 0)));
        Assert.Equal(new RectangleD(250, 150, 150, 150), ComicPage.VisibleArea(400, 300, 100, 100, 2, new PointD(1, 1)));
    }

    [Fact]
    public void Page_has_the_background_photos_in_their_panels_and_borders()
    {
        var options = new ComicPageOptions(new ImageSize(200, 100), 10, 2, ColorBgra.Black, ColorBgra.White);
        ComicPanelContent?[] contents = [new ComicPanelContent(Solid(50, 50, 0, 0, 255)), null];

        var page = ComicPage.Compose(Layout("2 columns"), options, contents);

        Assert.Equal((200, 100), (page.Width, page.Height));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, At(page, 5, 5));      // margin: background
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, At(page, 50, 50));        // first panel: the red photo
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, At(page, 10, 50));          // its border
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, At(page, 150, 50));   // empty panel
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, At(page, 189, 50));         // with its border
    }

    [Fact]
    public void A_smaller_size_renders_the_same_page_as_a_preview()
    {
        var options = new ComicPageOptions(new ImageSize(2000, 1000), 100, 20, ColorBgra.Black, ColorBgra.White);
        ComicPanelContent?[] contents = [new ComicPanelContent(Solid(64, 64, 255, 0, 0)), new ComicPanelContent(Solid(64, 64, 0, 255, 0))];

        var preview = ComicPage.Compose(Layout("2 columns"), options, contents, new ImageSize(200, 100));

        Assert.Equal((200, 100), (preview.Width, preview.Height));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, At(preview, 50, 50));
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, At(preview, 150, 50));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, At(preview, 3, 3));
    }

    // ---- Editing on the canvas ----

    private ImageDocument NewPage(int w, int h) =>
        CinnabarSharpService().GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(w, h), ColorBgra.White);

    private static void Drag(ITool tool, ImageDocument doc, (double X, double Y) from, (double X, double Y) to)
    {
        ToolPointer P((double X, double Y) p) => new(new PointD(p.X, p.Y), ToolButton.Left, ToolModifiers.None);
        tool.OnPointerDown(doc, P(from));
        tool.OnPointerMove(doc, P(to));
        tool.OnPointerUp(doc, P(to));
    }

    [Fact]
    public void Clicking_selects_a_panel_and_dragging_moves_the_photo_in_it()
    {
        var doc = NewPage(200, 100);
        var options = new ComicPageOptions(new ImageSize(200, 100), 10, 2, ColorBgra.Black, ColorBgra.White);
        var tool = new ComicPageTool(Layout("2 columns"), options, [new ComicPanelContent(Solid(400, 100, 0, 0, 255)), null]);
        var changes = 0;
        tool.Changed += () => changes++;

        Drag(tool, doc, (150, 50), (150, 50)); // the empty second panel: selected, nothing to move
        Assert.Equal(1, tool.Selected);
        Assert.Equal(ToolCursor.Default, tool.CursorAt(doc, new PointD(150, 50)));
        Assert.Equal(ToolCursor.Move, tool.CursorAt(doc, new PointD(50, 50)));

        // First panel is 85 × 80 (tall): a 400 × 100 photo shows an 106 × 100 area. Drag right by 20 page pixels:
        // the photo moves right, so the visible area moves left.
        Drag(tool, doc, (50, 50), (70, 50));
        Assert.Equal(0, tool.Selected);
        var content = tool.Contents[0]!;
        Assert.True(content.Center.X < 0.5);
        Assert.Equal(0.5, content.Center.Y, 3); // the photo is exactly as tall as the area: no vertical move
        Assert.True(changes > 0);

        // Far past the edge: the visible area stops at the photo's left edge.
        Drag(tool, doc, (50, 50), (500, 50));
        var area = ComicPage.VisibleArea(400, 100, 85, 80, 1, tool.Contents[0]!.Center);
        Assert.Equal(0, area.X, 6);
    }

    [Fact]
    public void Zoom_photo_and_layout_changes_are_kept_per_panel()
    {
        var options = new ComicPageOptions(new ImageSize(200, 100), 10, 2, ColorBgra.Black, ColorBgra.White);
        var tool = new ComicPageTool(Layout("2 columns"), options, [new ComicPanelContent(Solid(40, 40, 0, 0, 255)), null]);

        tool.SetZoom(0, 9);
        Assert.Equal(4, tool.Contents[0]!.Zoom); // at most 4×
        tool.SetPhoto(1, Solid(40, 40, 0, 255, 0));
        Assert.NotNull(tool.Contents[1]);

        tool.SetLayout(Layout("3 rows"));
        Assert.Equal(3, tool.Layout.Panels.Count);
        Assert.Equal(4, tool.Contents[0]!.Zoom);
        Assert.NotNull(tool.Contents[1]);
        Assert.Null(tool.Contents[2]);

        tool.Select(2);
        tool.SetLayout(Layout("1 panel"));
        Assert.Equal(0, tool.Selected);
        Assert.Equal(new RectangleD(10, 10, 180, 80), tool.GetOverlay(null!)!.Frame); // the selected panel is framed
    }
}
