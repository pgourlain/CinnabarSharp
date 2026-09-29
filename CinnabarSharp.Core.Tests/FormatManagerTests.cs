using ImageMagick;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public sealed class FormatManagerTests : BaseTests, IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cinnabarsharp-tests-");
    private readonly IServiceProvider _sp;
    private readonly IFormatManager _formats;
    private readonly IWorkspaceService _workspace;

    public FormatManagerTests()
    {
        _sp = CinnabarSharpService();
        _formats = _sp.GetRequiredService<IFormatManager>();
        _workspace = _sp.GetRequiredService<IWorkspaceService>();
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private FileInfo TempFile(string name) => new(Path.Combine(_dir.FullName, name));

    private ImageDocument NewRedDocument(byte alpha = 255) =>
        _workspace.NewDocument(new ImageSize(6, 4), ColorBgra.FromBgra(0, 0, 255, alpha));

    private static IMagickColor<byte> PixelOf(ImageDocument doc, int x, int y) =>
        doc.Layers[0].Surface.GetPixels().GetPixel(x, y).ToColor()!;

    [Theory]
    [InlineData("png")]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("tiff")]
    [InlineData("webp")]
    [InlineData("jpg")]
    public void Save_then_open_round_trips_size_and_color(string extension)
    {
        var file = TempFile("image." + extension);
        _formats.Save(NewRedDocument(), file);

        var reopened = _formats.Open(new FileInfo(file.FullName));

        Assert.Equal(new ImageSize(6, 4), reopened.ImageSize);
        var pixel = PixelOf(reopened, 2, 2);
        Assert.InRange(pixel.R, 240, 255);
        Assert.InRange(pixel.G, 0, 15);
        Assert.InRange(pixel.B, 0, 15);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("tiff")]
    public void Transparency_is_kept_by_formats_that_support_it(string extension)
    {
        var file = TempFile("t." + extension);
        _formats.Save(NewRedDocument(alpha: 0), file);

        var reopened = _formats.Open(new FileInfo(file.FullName));

        Assert.Equal(0, PixelOf(reopened, 1, 1).A);
    }

    [Fact]
    public void Jpeg_flattens_transparency_onto_white()
    {
        var doc = _workspace.NewDocument(new ImageSize(6, 4), ColorBgra.Transparent);
        var file = TempFile("t.jpg");
        _formats.Save(doc, file);

        var reopened = _formats.Open(new FileInfo(file.FullName));

        var pixel = PixelOf(reopened, 1, 1);
        Assert.InRange(pixel.R, 250, 255);
        Assert.InRange(pixel.G, 250, 255);
        Assert.InRange(pixel.B, 250, 255);
    }

    [Fact]
    public void Save_sets_file_name_and_clears_dirty_flag()
    {
        var doc = NewRedDocument();
        doc.IsDirty = true;
        var file = TempFile("saved.png");

        _formats.Save(doc, file);

        Assert.Equal(file.FullName, doc.File!.FullName);
        Assert.Equal("saved.png", doc.DisplayName);
        Assert.Equal("png", doc.FileType);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_round_trips_like_the_synchronous_Save()
    {
        var file = TempFile("async.png");

        await _formats.SaveAsync(NewRedDocument(), file);

        var reopened = _formats.Open(new FileInfo(file.FullName));
        Assert.Equal(new ImageSize(6, 4), reopened.ImageSize);
        var pixel = PixelOf(reopened, 2, 2);
        Assert.InRange(pixel.R, 240, 255);
        Assert.InRange(pixel.G, 0, 15);
        Assert.InRange(pixel.B, 0, 15);
    }

    [Fact]
    public async Task SaveAsync_sets_file_name_and_clears_dirty_flag()
    {
        var doc = NewRedDocument();
        doc.IsDirty = true;
        var file = TempFile("saved-async.png");

        await _formats.SaveAsync(doc, file);

        Assert.Equal(file.FullName, doc.File!.FullName);
        Assert.Equal("saved-async.png", doc.DisplayName);
        Assert.Equal("png", doc.FileType);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public async Task SaveAsync_runs_the_encode_off_the_calling_thread()
    {
        var doc = NewRedDocument();
        var file = TempFile("thread.png");
        var callingThread = Environment.CurrentManagedThreadId;
        var encodeThread = -1;
        var probe = new ThreadProbingFormat(_workspace, t => encodeThread = t);

        await _formats.SaveAsync(doc, file, probe);

        Assert.NotEqual(callingThread, encodeThread);
    }

    /// <summary>Records which thread its <see cref="Export"/> actually runs on, to pin that
    /// <see cref="IFormatManager.SaveAsync"/> really moves the encode off the calling thread.</summary>
    private sealed class ThreadProbingFormat(IWorkspaceService workspace, Action<int> onExport) : MagickImageFormat(
        nameof(ThreadProbingFormat), "PNG", ["png"], [ImageMagick.MagickFormat.Png], workspace)
    {
        public override void Export(ImageDocument document, FileInfo file)
        {
            onExport(Environment.CurrentManagedThreadId);
            base.Export(document, file);
        }
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("tiff")]
    public void PeekSize_reads_dimensions_without_a_full_open(string extension)
    {
        var file = TempFile("peek." + extension);
        _formats.Save(NewRedDocument(), file); // 6x4

        var size = _formats.PeekSize(file);

        Assert.Equal(new ImageSize(6, 4), size);
    }

    [Fact]
    public void PeekSize_of_an_ora_file_reads_stack_xml_without_decoding_layers()
    {
        var doc = _workspace.NewDocument(new ImageSize(8, 5), ColorBgra.White);
        doc.Layers.AddNewLayer("Second");
        var file = TempFile("peek.ora");
        _formats.Save(doc, file);

        var size = _formats.PeekSize(file);

        Assert.Equal(new ImageSize(8, 5), size);
    }

    [Fact]
    public void PeekSize_of_an_unrecognized_file_is_null()
    {
        var file = TempFile("not-an-image.txt");
        File.WriteAllText(file.FullName, "hello");

        Assert.Null(_formats.PeekSize(file));
    }

    [Theory]
    [InlineData("UPPER.PNG")]
    [InlineData("été ✓ 画像.png")]
    [InlineData("no-extension")]
    [InlineData("wrong-extension.txt")]
    public void Opens_files_with_unusual_names(string name)
    {
        var file = TempFile(name);
        File.Copy(ImageSample1().FullName, file.FullName);

        var doc = _formats.Open(file);

        Assert.Equal(name, doc.DisplayName);
        Assert.True(doc.ImageSize.Width > 0);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Opening_an_open_file_activates_it_instead_of_reopening()
    {
        var first = _formats.Open(ImageSample1());
        _workspace.NewDocument(new ImageSize(5, 5), ColorBgra.White);

        var again = _formats.Open(ImageSample1());

        Assert.Same(first, again);
        Assert.Same(first, _workspace.ActiveDocument);
        Assert.Equal(2, _workspace.OpenDocuments.Count);
    }

    [Fact]
    public void Open_rejects_non_image_files()
    {
        var file = TempFile("notes.txt");
        File.WriteAllText(file.FullName, "not an image");

        Assert.Throws<NotSupportedException>(() => _formats.Open(file));
    }

    [Theory]
    [InlineData(".PNG", "PngFormat")]
    [InlineData("jpeg", "JpegFormat")]
    [InlineData("tif", "TiffFormat")]
    [InlineData("xcf", null)]
    public void Finds_format_by_extension(string extension, string? expected)
    {
        Assert.Equal(expected, _formats.GetFormatByExtension(extension)?.Name);
    }
}

public class CloseDocumentTests : BaseTests
{
    [Fact]
    public void Closing_active_document_activates_neighbour()
    {
        var workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();
        var a = workspace.NewDocument(new ImageSize(5, 5), ColorBgra.White);
        var b = workspace.NewDocument(new ImageSize(5, 5), ColorBgra.White);
        var c = workspace.NewDocument(new ImageSize(5, 5), ColorBgra.White);
        workspace.SetActiveDocument(b);

        workspace.CloseDocument(b);
        Assert.Same(c, workspace.ActiveDocument);

        workspace.CloseDocument(c);
        Assert.Same(a, workspace.ActiveDocument);

        workspace.CloseDocument(a);
        Assert.False(workspace.HasOpenDocuments);
    }

    [Fact]
    public void Closing_inactive_document_keeps_active_one()
    {
        var workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();
        var a = workspace.NewDocument(new ImageSize(5, 5), ColorBgra.White);
        var b = workspace.NewDocument(new ImageSize(5, 5), ColorBgra.White);

        workspace.CloseDocument(a);

        Assert.Same(b, workspace.ActiveDocument);
        Assert.Single(workspace.OpenDocuments);
    }
}
