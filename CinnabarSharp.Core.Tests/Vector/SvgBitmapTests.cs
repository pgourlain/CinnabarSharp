using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class SvgBitmapTests : VectorToolTestBase, IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("cinnabar-bitmaps");
    private IWorkspaceService? _workspace;

    public void Dispose() => _folder.Delete(recursive: true);

    private SvgDocument OpenDrawing(string body = "")
    {
        var sp = CinnabarSharpService();
        _workspace = sp.GetRequiredService<IWorkspaceService>();
        var doc = _workspace.OpenSvgDocument(SvgParser.Parse(Header + body + "</svg>").Root, null, null);
        doc.GlyphProvider = new FakeBoxProvider();
        return doc;
    }

    private FileInfo Picture(string name, MagickFormat format, int width, int height, string color = "#ff8000")
    {
        using var image = new MagickImage(new MagickColor(color), (uint)width, (uint)height);
        var file = new FileInfo(Path.Combine(_folder.FullName, name));
        file.Directory!.Create();
        image.Write(file.FullName, format);
        return file;
    }

    private static (byte R, byte G, byte B, byte A) PixelAtUser(SvgDocument doc, int x, int y)
    {
        var picture = VectorRasterizer.RenderAll(doc.Root, 1, doc.RenderOptions);
        var i = (y * picture.Width + x) * 4;
        return (picture.Bgra[i + 2], picture.Bgra[i + 1], picture.Bgra[i], picture.Bgra[i + 3]);
    }

    [Fact]
    public void Importing_embeds_a_png_fitted_into_the_page_in_one_step()
    {
        var doc = OpenDrawing();
        var steps = Steps(doc);
        var image = doc.Actions.ImportImage(Picture("wide.png", MagickFormat.Png, 400, 100), linked: false);

        Assert.Equal(steps + 1, Steps(doc));
        Assert.StartsWith("data:image/png;base64,", image.Href);
        Assert.Equal((200, 50, 0, 75), (image.Width, image.Height, image.X, image.Y));
        Assert.Equal("Import Picture", doc.History.Items[^1].Text);
        Assert.Equal((255, 128, 0, 255), PixelAtUser(doc, 100, 100));
        doc.History.Undo();
        Assert.Empty(doc.Root.Descendants().OfType<SvgImage>());
    }

    [Fact]
    public void Jpeg_keeps_its_bytes_and_other_formats_become_png()
    {
        var doc = OpenDrawing();
        Assert.StartsWith("data:image/jpeg;base64,", doc.Actions.ImportImage(Picture("a.jpg", MagickFormat.Jpeg, 50, 50), false).Href);
        Assert.StartsWith("data:image/png;base64,", doc.Actions.ImportImage(Picture("b.gif", MagickFormat.Gif, 30, 30), false).Href);
        Assert.Throws<InvalidOperationException>(() =>
        {
            var junk = new FileInfo(Path.Combine(_folder.FullName, "junk.png"));
            File.WriteAllText(junk.FullName, "not a picture");
            doc.Actions.ImportImage(junk, false);
        });
    }

    [Fact]
    public void A_linked_picture_is_stored_relative_and_must_stay_in_the_folder_of_the_drawing()
    {
        var doc = OpenDrawing();
        var pictures = Picture("pics/p.png", MagickFormat.Png, 20, 20, "#00ff00");
        // The drawing has to be saved somewhere first.
        Assert.Throws<InvalidOperationException>(() => doc.Actions.ImportImage(pictures, linked: true));

        doc.File = new FileInfo(Path.Combine(_folder.FullName, "drawing", "logo.svg"));
        // pics is next to the drawing's folder, not inside it.
        var outside = Assert.Throws<InvalidOperationException>(() => doc.Actions.ImportImage(pictures, linked: true));
        Assert.Contains("folder", outside.Message);

        doc.File = new FileInfo(Path.Combine(_folder.FullName, "logo.svg"));
        var image = doc.Actions.ImportImage(pictures, linked: true);
        Assert.Equal("pics/p.png", image.Href);
        Assert.Equal((0, 255, 0, 255), PixelAtUser(doc, 100, 100));
        Assert.Equal("Import Linked Picture", doc.History.Items[^1].Text);
    }

    [Fact]
    public void An_image_moves_scales_rotates_and_fades_like_other_objects()
    {
        var doc = OpenDrawing();
        var image = doc.Actions.ImportImage(Picture("p.png", MagickFormat.Png, 100, 50), false);
        var xml = Xml(doc);
        doc.Actions.MoveBy([image], 10, 20);
        Assert.Equal((60, 95), (image.X, image.Y));
        doc.Actions.Resize([image], new VRect(0, 0, 50, 25));
        Assert.Equal((50, 25), (image.Width, image.Height));
        doc.Actions.Rotate([image], 90);
        Assert.NotNull(image.GetAttribute("transform"));
        doc.Actions.SetStyle([image], "opacity", "0.5");
        Assert.Equal("0.5", image.Style.Get("opacity"));
        for (var i = 0; i < 4; i++)
            doc.History.Undo();
        Assert.Equal(Xml(doc), xml);
    }

    [Fact]
    public void Set_clip_clips_the_image_with_the_shape_above_and_release_removes_it()
    {
        var doc = OpenDrawing("<circle id='c' cx='100' cy='100' r='30' fill='#000'/>");
        var image = doc.Actions.ImportImage(Picture("p.png", MagickFormat.Png, 200, 200), false);
        var circle = El(doc, "c");
        // Put the circle above the picture: the picture is the last element added, so raise the circle.
        doc.Actions.RaiseToTop([circle]);
        var xml = Xml(doc);
        var steps = Steps(doc);

        var clipped = doc.Actions.SetClip([image, circle]);

        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal([image], clipped);
        Assert.Null(doc.Root.FindById("c"));
        Assert.Single(doc.Root.Descendants().OfType<SvgClipPath>());
        Assert.StartsWith("url(#clip", image.GetAttribute("clip-path"));
        Assert.Equal((255, 128, 0, 255), PixelAtUser(doc, 100, 100));
        Assert.Equal(0, PixelAtUser(doc, 10, 10).A);
        Assert.Equal(0, PixelAtUser(doc, 100, 140).A);

        doc.Actions.ReleaseClip([image]);
        Assert.Null(image.GetAttribute("clip-path"));
        Assert.Empty(doc.Root.Descendants().OfType<SvgClipPath>());
        Assert.Equal(255, PixelAtUser(doc, 10, 10).A);

        doc.History.Undo();
        Assert.StartsWith("url(#clip", image.GetAttribute("clip-path"));
        doc.History.Undo();
        Assert.Equal(xml, Xml(doc));
    }

    [Fact]
    public void Set_clip_works_on_a_transformed_object_and_needs_a_shape_on_top()
    {
        var doc = OpenDrawing("<g id='g' transform='translate(50 0)'><rect id='r' x='0' y='0' width='100' height='100' fill='#0000ff'/></g>" +
            "<rect id='m' x='60' y='10' width='30' height='30' fill='#000'/>");
        doc.Actions.SetClip([El(doc, "g"), El(doc, "m")]);
        Assert.Equal(0, PixelAtUser(doc, 55, 50).A);          // outside the mask
        Assert.Equal((0, 0, 255, 255), PixelAtUser(doc, 70, 20));   // inside it, at the same place as before

        var empty = OpenDrawing("<rect id='a' x='0' y='0' width='9' height='9'/>");
        Assert.Throws<InvalidOperationException>(() => empty.Actions.SetClip([El(empty, "a")]));
        var withText = OpenDrawing("<rect id='a' x='0' y='0' width='9' height='9'/><text id='t' x='5' y='50'>T</text>");
        Assert.Throws<InvalidOperationException>(() => withText.Actions.SetClip([El(withText, "a"), El(withText, "t")]));
    }

    [Fact]
    public void Edit_bitmap_round_trip_updates_the_embedded_data_in_one_undoable_step()
    {
        var doc = OpenDrawing();
        var image = doc.Actions.ImportImage(Picture("p.png", MagickFormat.Png, 40, 30), false);
        var before = Xml(doc);
        var steps = Steps(doc);

        var raster = SvgBitmapEditing.Open(_workspace!, doc, image);
        Assert.Equal(new ImageSize(40, 30), raster.ImageSize);
        Assert.False(raster.IsDirty);
        Assert.Equal(DocumentKind.Image, raster.Kind);

        // Closing without updating leaves the drawing alone.
        Assert.Equal(before, Xml(doc));
        Assert.Equal(steps, Steps(doc));

        raster.Actions.FillSelection(ColorBgra.FromBgra(200, 0, 0, 255));      // blue, in BGRA
        Assert.True(SvgBitmapEditing.CanUpdate(raster));
        Assert.True(SvgBitmapEditing.Update(raster));
        Assert.Equal(steps + 1, Steps(doc));
        Assert.Equal("Edit Bitmap", doc.History.Items[^1].Text);
        Assert.Equal((0, 0, 200, 255), PixelAtUser(doc, 100, 100));

        doc.History.Undo();
        Assert.Equal(before, Xml(doc));
        doc.History.Redo();
        Assert.Equal((0, 0, 200, 255), PixelAtUser(doc, 100, 100));

        doc.Actions.Delete([image]);
        Assert.False(SvgBitmapEditing.CanUpdate(raster));
        Assert.False(SvgBitmapEditing.Update(raster));
    }

    [Fact]
    public void A_linked_picture_outside_the_folder_cannot_be_edited_and_pasting_embeds()
    {
        var doc = OpenDrawing("<image id='i' x='0' y='0' width='50' height='50' href='../secret.png'/>");
        doc.File = new FileInfo(Path.Combine(_folder.FullName, "d.svg"));
        Assert.Null(doc.Actions.DecodeImage((SvgImage)El(doc, "i")));
        Assert.Throws<InvalidOperationException>(() => SvgBitmapEditing.Open(_workspace!, doc, (SvgImage)El(doc, "i")));

        var steps = Steps(doc);
        var pasted = doc.Actions.PasteImage(new Core.Services.ClipboardImage(new byte[8 * 8 * 4], 8, 8));
        Assert.Equal(steps + 1, Steps(doc));
        Assert.StartsWith("data:image/png;base64,", pasted.Href);
    }
}
