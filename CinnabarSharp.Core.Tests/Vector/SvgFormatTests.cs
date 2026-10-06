using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests.Vector;

public class SvgFormatTests : BaseTests, IDisposable
{
    private readonly IServiceProvider _sp;
    private readonly IFormatManager _formats;
    private readonly IWorkspaceService _workspace;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "svgformat-" + Guid.NewGuid().ToString("N"));

    public SvgFormatTests()
    {
        _sp = CinnabarSharpService();
        _formats = _sp.GetRequiredService<IFormatManager>();
        _workspace = _sp.GetRequiredService<IWorkspaceService>();
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, true);

    private FileInfo Copy(string sample, string? name = null)
    {
        var target = Path.Combine(_folder, name ?? sample);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "svg", sample), target);
        return new FileInfo(target);
    }

    [Theory]
    [InlineData("shapes.svg", "shapes.svg")]
    [InlineData("shapes.svg", "no-extension-file")]
    [InlineData("shapes.svg", "weird.dat")]
    public void Open_by_extension_or_content_sniffing(string sample, string name)
    {
        var file = Copy(sample, name);
        var doc = _formats.Open(file);
        var svg = Assert.IsType<SvgDocument>(doc);
        Assert.Equal(file.Name, svg.DisplayName);
        Assert.False(svg.IsDirty);
        Assert.Equal(["Open Drawing"], svg.History.Items.Select(i => i.Text));
        Assert.IsType<SvgFormat>(_formats.GetFormatForFile(file));
        Assert.Same(doc, _workspace.ActiveDocument);
    }

    [Fact]
    public void Opening_the_same_file_twice_activates_the_open_tab()
    {
        var file = Copy("shapes.svg");
        var first = _formats.Open(file);
        _workspace.NewDocument(new ImageSize(4, 4), ColorBgra.White);
        var again = _formats.Open(new FileInfo(file.FullName));
        Assert.Same(first, again);
        Assert.Same(first, _workspace.ActiveDocument);
        Assert.Equal(2, _workspace.OpenDocuments.Count);
    }

    [Fact]
    public async Task Open_async_parses_off_the_calling_thread()
    {
        var file = Copy("gradients.svg");
        var doc = await _formats.OpenAsync(file);
        Assert.IsType<SvgDocument>(doc);
    }

    [Fact]
    public void Content_sniffing_skips_declaration_comments_and_doctype()
    {
        Assert.True(SvgFormat.LooksLikeSvg("<?xml version='1.0'?>\n<!-- c -->\n<!DOCTYPE svg PUBLIC 'x' 'y' [<!ENTITY a 'b'>]>\n<svg xmlns='http://www.w3.org/2000/svg'/>"));
        Assert.True(SvgFormat.LooksLikeSvg("﻿  <svg>"));
        Assert.True(SvgFormat.LooksLikeSvg("<svg:svg xmlns:svg='http://www.w3.org/2000/svg'/>"));
        Assert.False(SvgFormat.LooksLikeSvg("<html><svg/></html>"));
        Assert.False(SvgFormat.LooksLikeSvg("<svgfoo/>"));
        Assert.False(SvgFormat.LooksLikeSvg("hello"));
        Assert.False(SvgFormat.LooksLikeSvg(""));
    }

    [Fact]
    public void Invalid_svg_gives_a_not_supported_error_with_the_reason()
    {
        var file = new FileInfo(Path.Combine(_folder, "bad.svg"));
        File.WriteAllText(file.FullName, "<svg><rect></svg>");
        var e = Assert.Throws<NotSupportedException>(() => _formats.Open(file));
        Assert.Contains("bad.svg", e.Message);
    }

    [Fact]
    public void Save_without_changes_keeps_the_file_semantically_the_same()
    {
        var file = Copy("unknown.svg");
        var doc = _formats.Open(file);
        _formats.Save(doc, file);
        Assert.False(doc.IsDirty);
        var original = XDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "svg", "unknown.svg")));
        var saved = XDocument.Load(file.FullName);
        Assert.Equal(original.Descendants().Count(), saved.Descendants().Count());
        Assert.True(XNode.DeepEquals(original.Root!, saved.Root!));
    }

    [Fact]
    public void Dirty_state_after_edit_save_and_undo()
    {
        var file = Copy("shapes.svg");
        var doc = Assert.IsType<SvgDocument>(_formats.Open(file));
        doc.History.PushNewItem(new TestItem("Edit"));
        Assert.True(doc.IsDirty);
        _formats.Save(doc, file);
        Assert.False(doc.IsDirty);
        doc.History.Undo();
        Assert.True(doc.IsDirty);
        doc.History.Redo();
        Assert.False(doc.IsDirty);
    }

    private sealed class TestItem(string text) : HistoryItem(text)
    {
        protected override void OnUndo() { }
        protected override void OnRedo() { }
    }

    [Fact]
    public void Save_writes_the_edited_tree()
    {
        var file = Copy("shapes.svg");
        var doc = Assert.IsType<SvgDocument>(_formats.Open(file));
        ((SvgRect)doc.Root.FindById("r1")!).Width = 61;
        _formats.Save(doc, file);
        var saved = XDocument.Load(file.FullName);
        Assert.Equal("61", (string?)saved.Descendants().First(e => (string?)e.Attribute("id") == "r1").Attribute("width"));
        Assert.Equal("8", (string?)saved.Descendants().First(e => (string?)e.Attribute("id") == "r1").Attribute("rx"));
        Assert.False(File.Exists(file.FullName + ".tmp"));
    }

    [Fact]
    public void Save_as_svgz_compresses_and_reopens()
    {
        var file = Copy("shapes.svg");
        var doc = Assert.IsType<SvgDocument>(_formats.Open(file));
        var target = new FileInfo(Path.Combine(_folder, "out.svgz"));
        _formats.Save(doc, target);
        Assert.Equal(("svgz", "out.svgz"), (doc.FileType, doc.File!.Name));
        using (var stream = target.OpenRead())
            Assert.Equal((0x1F, 0x8B), (stream.ReadByte(), stream.ReadByte()));
        _workspace.CloseDocument(doc);
        var again = Assert.IsType<SvgDocument>(_formats.Open(target));
        Assert.NotNull(again.Root.FindById("c1"));
        Assert.Equal("svgz", again.FileType);
    }

    [Fact]
    public async Task Save_async_works()
    {
        var file = Copy("shapes.svg");
        var doc = Assert.IsType<SvgDocument>(_formats.Open(file));
        ((SvgCircle)doc.Root.FindById("c1")!).R = 5;
        doc.History.PushNewItem(new TestItem("Edit"));
        await _formats.SaveAsync(doc, file);
        Assert.False(doc.IsDirty);
        Assert.Contains("r=\"5\"", File.ReadAllText(file.FullName));
    }

    [Fact]
    public void Save_as_lists_svg_first_then_the_raster_exports()
    {
        var formats = _formats.GetSaveFormats(DocumentKind.Svg).Select(f => f.SupportedExtensions[0]).ToList();
        Assert.Equal("svg", formats[0]);
        Assert.Contains("png", formats);
        Assert.Contains("jpg", formats);
        Assert.Contains("ora", formats);
        Assert.DoesNotContain("heic", formats);
        var image = _formats.GetSaveFormats(DocumentKind.Image).Select(f => f.SupportedExtensions[0]).ToList();
        Assert.DoesNotContain("svg", image);
        Assert.Single(_formats.Formats, f => f.SupportedExtensions.Contains("svg"));
    }

    [Theory]
    [InlineData("png", MagickFormat.Png)]
    [InlineData("jpg", MagickFormat.Jpeg)]
    [InlineData("webp", MagickFormat.WebP)]
    [InlineData("bmp", MagickFormat.Bmp3)]
    [InlineData("tiff", MagickFormat.Tiff)]
    public void Export_renders_the_drawing_and_leaves_the_document_alone(string extension, MagickFormat expected)
    {
        var file = Copy("shapes.svg");
        var doc = Assert.IsType<SvgDocument>(_formats.Open(file));
        doc.History.PushNewItem(new TestItem("Edit"));
        var target = new FileInfo(Path.Combine(_folder, "export." + extension));

        _formats.Save(doc, target);

        Assert.True(doc.IsDirty);                              // unchanged by an export
        Assert.Equal(file.FullName, doc.File!.FullName);       // still the SVG file
        Assert.Equal("svg", doc.FileType);
        using var image = new MagickImage(target);
        Assert.Equal((200u, 120u), (image.Width, image.Height));
        if (expected == MagickFormat.Bmp3)
            Assert.Contains(image.Format, new[] { MagickFormat.Bmp, MagickFormat.Bmp2, MagickFormat.Bmp3 });
        else
            Assert.Equal(expected, image.Format);
    }

    [Fact]
    public void Export_scale_size_and_background()
    {
        var doc = Assert.IsType<SvgDocument>(_formats.Open(Copy("shapes.svg")));
        var png = new FileInfo(Path.Combine(_folder, "big.png"));

        _formats.Export(doc, png, new SvgExportOptions(Scale: 2));
        using (var image = new MagickImage(png))
            Assert.Equal((400u, 240u), (image.Width, image.Height));

        _formats.Export(doc, png, new SvgExportOptions(Width: 100));
        using (var image = new MagickImage(png))
            Assert.Equal((100u, 60u), (image.Width, image.Height));

        _formats.Export(doc, png, new SvgExportOptions(Height: 30, Background: VColor.White));
        using (var image = new MagickImage(png))
        {
            Assert.Equal((50u, 30u), (image.Width, image.Height));
            var pixel = image.GetPixels().GetPixel(48, 28).ToColor()!;       // empty corner: the white background
            Assert.Equal((255, 255, 255, 255), (pixel.R, pixel.G, pixel.B, pixel.A));
        }

        _formats.Export(doc, png, SvgExportOptions.Default);
        using (var image = new MagickImage(png))
        {
            var pixel = image.GetPixels().GetPixel(5, 115).ToColor()!;        // transparent
            Assert.Equal(0, pixel.A);
            var red = image.GetPixels().GetPixel(30, 30).ToColor()!;
            Assert.Equal((0xe0, 0x30, 0x20), (red.R, red.G, red.B));
        }
    }

    [Fact]
    public void Export_to_ora_has_one_layer_with_the_picture()
    {
        var doc = Assert.IsType<SvgDocument>(_formats.Open(Copy("shapes.svg")));
        var ora = new FileInfo(Path.Combine(_folder, "out.ora"));
        _formats.Save(doc, ora);
        using var zip = ZipFile.OpenRead(ora.FullName);
        Assert.NotNull(zip.GetEntry("mergedimage.png"));
        var stack = XDocument.Load(zip.GetEntry("stack.xml")!.Open());
        Assert.Single(stack.Descendants("layer"));
        // And it opens as an image.
        var reopened = Assert.IsType<ImageDocument>(_formats.Open(ora));
        Assert.Equal(new ImageSize(200, 120), reopened.ImageSize);
    }

    [Fact]
    public async Task Export_async()
    {
        var doc = Assert.IsType<SvgDocument>(_formats.Open(Copy("shapes.svg")));
        var png = new FileInfo(Path.Combine(_folder, "async.png"));
        await _formats.ExportAsync(doc, png, SvgExportOptions.Default);
        Assert.True(png.Exists);
    }

    [Fact]
    public void An_svg_cannot_be_exported_to_svg_or_heic_and_an_image_cannot_be_saved_as_svg()
    {
        var doc = Assert.IsType<SvgDocument>(_formats.Open(Copy("shapes.svg")));
        Assert.Throws<NotSupportedException>(() => _formats.Export(doc, new FileInfo(Path.Combine(_folder, "x.heic")), SvgExportOptions.Default));
        var image = _workspace.NewDocument(new ImageSize(4, 4), ColorBgra.White);
        Assert.Throws<NotSupportedException>(() => _formats.Save(image, new FileInfo(Path.Combine(_folder, "image.svg"))));
    }

    [Fact]
    public void Open_as_image_rasterizes_the_svg_into_an_unsaved_image()
    {
        var file = Copy("logo.svg");
        var doc = _formats.OpenAsImage(file);
        var image = Assert.IsType<ImageDocument>(doc);
        Assert.Equal(new ImageSize(400, 200), image.ImageSize);
        Assert.Null(image.File);
        Assert.Equal("logo.svg", image.DisplayName);
        Assert.Single(image.Layers.UserLayers);
        // The SVG can still be open as a drawing next to it.
        Assert.IsType<SvgDocument>(_formats.Open(file));
        Assert.Equal(2, _workspace.OpenDocuments.Count);
    }

    [Fact]
    public void New_svg_drawing_has_a_viewbox_in_its_unit_and_one_layer()
    {
        var px = _workspace.NewSvgDocument(800, 600, SvgUnit.Px);
        Assert.Equal(new ImageSize(800, 600), px.ImageSize);
        Assert.Equal("Unsaved Drawing 1", px.DisplayName);
        var layer = Assert.IsType<SvgGroup>(Assert.Single(px.Root.Elements));
        Assert.True(layer.IsLayer);
        Assert.Equal("layer1", layer.Id);
        Assert.Equal(new VRect(0, 0, 800, 600), px.Root.ViewBox);

        var mm = _workspace.NewSvgDocument(210, 297, SvgUnit.Mm);
        Assert.Equal("210mm", mm.Root.GetAttribute("width"));
        Assert.Equal(new VRect(0, 0, 210, 297), mm.Root.ViewBox);
        Assert.Equal(new ImageSize(794, 1123), mm.ImageSize);

        var inches = _workspace.NewSvgDocument(2, 1.5, SvgUnit.In);
        Assert.Equal(new ImageSize(192, 144), inches.ImageSize);
        Assert.Same(inches.Root.Elements.Single(), SvgDocumentFactory.DefaultParent(inches.Root));
    }

    [Fact]
    public void A_new_drawing_saves_and_reopens()
    {
        var doc = _workspace.NewSvgDocument(100, 50, SvgUnit.Px);
        var rect = new SvgRect { Id = "r" };
        rect.Width = 10;
        rect.Height = 10;
        SvgDocumentFactory.DefaultParent(doc.Root).AddChild(rect);
        var file = new FileInfo(Path.Combine(_folder, "new.svg"));
        _formats.Save(doc, file);
        Assert.Equal("new.svg", doc.DisplayName);
        _workspace.CloseDocument(doc);
        var again = Assert.IsType<SvgDocument>(_formats.Open(file));
        Assert.IsType<SvgRect>(again.Root.FindById("r"));
        Assert.Contains("inkscape:groupmode", Encoding.UTF8.GetString(File.ReadAllBytes(file.FullName)));
    }
}
