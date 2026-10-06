using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Vector;
using ImageMagick;

namespace CinnabarSharp.Desktop.Tests;

public sealed class SvgFileUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "svg", name);

    private string CopySample(string name)
    {
        var target = _h.TempPath(name);
        File.Copy(Sample(name), target);
        return target;
    }

    private SvgDocument ActiveSvg => Assert.IsType<SvgDocument>(Vm.ActiveDocument!.Document);

    private (byte R, byte G, byte B) CanvasPixel(Avalonia.Media.Imaging.WriteableBitmap frame, double x, double y)
    {
        var scale = ActiveSvg.Workspace.Scale;
        return TestHarness.PixelAt(frame, _h.CanvasToWindow((x + 0.5) * scale, (y + 0.5) * scale));
    }

    [AvaloniaFact]
    public async Task Open_svg_shows_the_shapes_on_the_canvas()
    {
        Assert.True(await Vm.OpenFileAsync(Sample("shapes.svg")));
        Dispatcher.UIThread.RunJobs();
        var frame = _h.Capture("svg-10-opened-shapes");

        var tab = Assert.Single(Vm.Documents);
        Assert.True(tab.IsSvg);
        Assert.Equal("shapes.svg - CinnabarSharp", _h.Window.Title);
        Assert.Equal("shapes.svg", tab.Name);
        Assert.False(tab.IsDirty);
        Assert.Equal("200 × 120", Vm.ImageSizeText);
        Assert.Equal("100%", Vm.ZoomText);

        Assert.Equal((0xe0, 0x30, 0x20), CanvasPixel(frame, 30, 30));    // rect
        Assert.Equal((0x20, 0x60, 0xe0), CanvasPixel(frame, 110, 30));   // circle
        Assert.Equal((0x20, 0xa0, 0x40), CanvasPixel(frame, 160, 30));   // ellipse
        Assert.Equal((0x80, 0x40, 0x00), CanvasPixel(frame, 40, 90));    // line
        Assert.Equal((0xa0, 0x20, 0xa0), CanvasPixel(frame, 100, 90));   // polyline
        Assert.Equal((0xe0, 0xc0, 0x20), CanvasPixel(frame, 175, 85));   // polygon
        Assert.Equal([Path.GetFullPath(Sample("shapes.svg"))], Vm.RecentFiles.Files);
    }

    [AvaloniaFact]
    public async Task The_drawing_is_rendered_again_sharply_at_a_new_zoom()
    {
        await Vm.OpenFileAsync(Sample("shapes.svg"));
        ActiveSvg.Workspace.Scale = 4;
        Dispatcher.UIThread.RunJobs();
        var frame = _h.Capture("svg-11-zoom-400");

        Assert.Equal("400%", Vm.ZoomText);
        Assert.Equal((0xe0, 0x30, 0x20), CanvasPixel(frame, 30, 30));
        // A hard edge stays a hard edge: the circle's inside and the white outside are not blended over several pixels.
        var inside = CanvasPixel(frame, 110, 30);
        Assert.Equal((0x20, 0x60, 0xe0), inside);
    }

    [AvaloniaFact]
    public async Task Edits_redraw_and_save_writes_the_file_and_clears_the_dirty_mark()
    {
        var path = CopySample("shapes.svg");
        await Vm.OpenFileAsync(path);
        var drawing = ActiveSvg;
        var rect = (SvgRect)drawing.Root.FindById("r1")!;

        rect.Style.Fill = SvgPaint.FromColor(VColor.FromRgb(0x11, 0x22, 0x33));
        drawing.History.PushNewItem(new TestItem("Change fill"));
        drawing.Workspace.Invalidate();
        Dispatcher.UIThread.RunJobs();
        var frame = _h.Capture("svg-12-edited");
        Assert.Equal((0x11, 0x22, 0x33), CanvasPixel(frame, 30, 30));
        Assert.True(Vm.ActiveDocument!.IsDirty);
        Assert.Equal("shapes.svg *", Vm.ActiveDocument.Title);

        await Vm.SaveCommand.ExecuteAsync(null);

        Assert.False(Vm.ActiveDocument.IsDirty);
        Assert.Empty(_h.Dialogs.Errors);
        var saved = File.ReadAllText(path);
        Assert.Contains("#112233", saved);
        Assert.Contains("id=\"c1\"", saved);
        Assert.False(File.Exists(path + ".tmp"));
    }

    private sealed class TestItem(string text) : HistoryItem(text)
    {
        protected override void OnUndo() { }
        protected override void OnRedo() { }
    }

    [AvaloniaFact]
    public async Task Undo_of_a_drawing_edit_redraws_the_canvas()
    {
        await Vm.OpenFileAsync(CopySample("shapes.svg"));
        var drawing = ActiveSvg;
        var rect = (SvgRect)drawing.Root.FindById("r1")!;
        var oldFill = rect.Style.Fill;
        var item = new RevertingItem(rect, oldFill!, SvgPaint.FromColor(VColor.FromRgb(1, 2, 3)));
        rect.Style.Fill = SvgPaint.FromColor(VColor.FromRgb(1, 2, 3));
        drawing.History.PushNewItem(item);
        drawing.Workspace.Invalidate();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((1, 2, 3), CanvasPixel(_h.Capture("svg-13-before-undo"), 30, 30));

        Vm.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0xe0, 0x30, 0x20), CanvasPixel(_h.Capture("svg-14-after-undo"), 30, 30));
        Assert.False(Vm.ActiveDocument!.IsDirty);
    }

    private sealed class RevertingItem(SvgRect rect, SvgPaint before, SvgPaint after) : HistoryItem("Change fill")
    {
        protected override void OnUndo() => rect.Style.Fill = before;
        protected override void OnRedo() => rect.Style.Fill = after;
    }

    [AvaloniaFact]
    public async Task Svg_tab_disables_raster_only_commands_and_enables_the_shared_ones()
    {
        await Vm.OpenFileAsync(Sample("shapes.svg"));

        Assert.True(Vm.SaveCommand.CanExecute(null));
        Assert.True(Vm.SaveAsCommand.CanExecute(null));
        Assert.True(Vm.ZoomInCommand.CanExecute(null));
        Assert.True(Vm.RasterizeCommand.CanExecute(null));
        Assert.False(Vm.AddNewLayerCommand.CanExecute(null));
        Assert.False(Vm.ResizeImageCommand.CanExecute(null));
        Assert.True(Vm.CopyCommand.CanExecute(null));            // copy, cut, paste, select all and delete work on objects
        Assert.True(Vm.SelectAllCommand.CanExecute(null));
        Assert.False(Vm.SepiaCommand.CanExecute(null));
        Assert.False(Vm.PrepareForTvCommand.CanExecute(null));
        Assert.False(Vm.FlattenCommand.CanExecute(null));
        Assert.False(Vm.InvertSelectionCommand.CanExecute(null));
        Assert.Empty(Vm.Layers);

        Vm.CreateImage(new NewImageOptions(new ImageSize(10, 10), ColorBgra.White));
        Assert.True(Vm.AddNewLayerCommand.CanExecute(null));
        Assert.False(Vm.RasterizeCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void New_svg_drawing_creates_a_drawing_with_its_unit()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(794, 1123), ColorBgra.Transparent, new SvgDrawingOptions(210, 297, SvgUnit.Mm)));
        Dispatcher.UIThread.RunJobs();
        _h.Capture("svg-20-new-drawing");

        var tab = Assert.Single(Vm.Documents);
        Assert.True(tab.IsSvg);
        Assert.Equal("Unsaved Drawing 1", tab.Name);
        Assert.Equal("794 × 1123", Vm.ImageSizeText);
        Assert.Equal("210mm", tab.Svg.Root.GetAttribute("width"));
    }

    [AvaloniaFact]
    public void The_new_dialog_view_model_builds_options_for_both_kinds()
    {
        var vm = new NewImageViewModel(new ImageSize(800, 600));
        Assert.Equal(new NewImageOptions(new ImageSize(800, 600), ColorBgra.White), vm.ToOptions());

        vm.IsSvg = true;
        vm.Unit = SvgUnit.Mm;
        vm.Width = 210;
        vm.Height = 297;
        var options = vm.ToOptions()!;
        Assert.Equal(new SvgDrawingOptions(210, 297, SvgUnit.Mm), options.Svg);
        Assert.Equal(new ImageSize(794, 1123), options.Size);
        Assert.Equal("mm", vm.UnitText);

        vm.Unit = SvgUnit.In;
        vm.Width = 2;
        vm.Height = 1.5m;
        Assert.Equal(new SvgDrawingOptions(2, 1.5, SvgUnit.In), vm.ToOptions()!.Svg);
        vm.Width = 0;
        Assert.Null(vm.ToOptions());
    }

    [AvaloniaFact]
    public async Task Save_as_svg_for_a_new_drawing_asks_for_a_path_and_names_the_tab()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(40, 30), ColorBgra.Transparent, new SvgDrawingOptions(40, 30, SvgUnit.Px)));
        var path = _h.TempPath("drawing.svg");
        _h.Dialogs.SavePaths.Enqueue(path);

        await Vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("drawing.svg", Vm.ActiveDocument!.Name);
        Assert.Equal("svg", _h.Dialogs.LastSuggestedSaveFormat!.SupportedExtensions[0]);
        Assert.True(File.Exists(path));
        Assert.Contains("<svg", File.ReadAllText(path));
        Assert.Equal([Path.GetFullPath(path)], Vm.RecentFiles.Files);
    }

    [AvaloniaFact]
    public async Task Save_as_png_exports_and_keeps_the_drawing_unchanged()
    {
        var svgPath = CopySample("shapes.svg");
        await Vm.OpenFileAsync(svgPath);
        ActiveSvg.History.PushNewItem(new TestItem("Edit"));
        var png = _h.TempPath("out.png");
        _h.Dialogs.SavePaths.Enqueue(png);

        await Vm.SaveAsCommand.ExecuteAsync(null);

        Assert.Equal(["Export as PNG"], _h.Dialogs.SvgExportsAsked);
        using var image = new MagickImage(png);
        Assert.Equal((200u, 120u), (image.Width, image.Height));
        Assert.True(Vm.ActiveDocument!.IsDirty);                       // an export is not a save
        Assert.Equal(Path.GetFullPath(svgPath), ActiveSvg.File!.FullName);
        Assert.Equal([Path.GetFullPath(svgPath)], Vm.RecentFiles.Files);   // the export is not a recent drawing
    }

    [AvaloniaFact]
    public async Task Export_options_come_from_the_dialog()
    {
        await Vm.OpenFileAsync(CopySample("shapes.svg"));
        _h.Dialogs.SvgExportAnswer = _ => new SvgExportOptions(Width: 100, Background: VColor.White);
        var png = _h.TempPath("small.png");
        _h.Dialogs.SavePaths.Enqueue(png);

        await Vm.SaveAsCommand.ExecuteAsync(null);

        using var image = new MagickImage(png);
        Assert.Equal((100u, 60u), (image.Width, image.Height));
    }

    [AvaloniaFact]
    public async Task Cancelling_the_export_dialog_writes_nothing()
    {
        await Vm.OpenFileAsync(CopySample("shapes.svg"));
        _h.Dialogs.SvgExportAnswer = _ => null;
        var png = _h.TempPath("none.png");
        _h.Dialogs.SavePaths.Enqueue(png);

        await Vm.SaveAsCommand.ExecuteAsync(null);

        Assert.False(File.Exists(png));
    }

    [AvaloniaFact]
    public async Task Rasterize_opens_a_new_image_and_keeps_the_drawing()
    {
        await Vm.OpenFileAsync(Sample("shapes.svg"));
        _h.Dialogs.SvgExportAnswer = _ => new SvgExportOptions(Width: 400, Height: 240);

        await Vm.RasterizeCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        _h.Capture("svg-30-rasterized");

        Assert.Equal(2, Vm.Documents.Count);
        Assert.True(Vm.Documents[0].IsSvg);
        var image = Vm.ActiveDocument!;
        Assert.True(image.IsImage);
        Assert.Equal("shapes (raster)", image.Name);
        Assert.Equal(new ImageSize(400, 240), image.Document.ImageSize);
        var pixels = image.Image.Layers.GetFlattenedBgra();
        var i = (60 * 400 + 60) * 4;                                   // inside the red rect, at 2x
        Assert.Equal((0xe0, 0x30, 0x20), (pixels[i + 2], pixels[i + 1], pixels[i]));
        Assert.Equal(["Rasterize"], _h.Dialogs.SvgExportsAsked);
    }

    [AvaloniaFact]
    public async Task Open_as_image_rasterizes_an_svg_into_a_raster_tab()
    {
        _h.Dialogs.FilesToOpen.Enqueue([Sample("logo.svg")]);

        await Vm.OpenAsImageCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var tab = Assert.Single(Vm.Documents);
        Assert.True(tab.IsImage);
        Assert.Equal("logo.svg", tab.Name);
        Assert.Equal(new ImageSize(400, 200), tab.Document.ImageSize);
        Assert.Null(tab.Document.File);
        Assert.True(Vm.AddNewLayerCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Closing_a_drawing_with_changes_asks_like_an_image()
    {
        await Vm.OpenFileAsync(CopySample("shapes.svg"));
        ActiveSvg.History.PushNewItem(new TestItem("Edit"));

        _h.Dialogs.SaveChangesAnswers.Enqueue(Services.SaveChangesChoice.Cancel);
        Assert.False(await Vm.CloseDocumentAsync(Vm.ActiveDocument!));
        Assert.Equal(["shapes.svg"], _h.Dialogs.SaveChangesAsked);
        Assert.Single(Vm.Documents);

        _h.Dialogs.SaveChangesAnswers.Enqueue(Services.SaveChangesChoice.DontSave);
        Assert.True(await Vm.CloseDocumentAsync(Vm.ActiveDocument!));
        Assert.Empty(Vm.Documents);
    }

    [AvaloniaFact]
    public async Task Closing_with_save_writes_the_svg_first()
    {
        var path = CopySample("shapes.svg");
        await Vm.OpenFileAsync(path);
        ((SvgCircle)ActiveSvg.Root.FindById("c1")!).R = 7;
        ActiveSvg.History.PushNewItem(new TestItem("Edit"));

        _h.Dialogs.SaveChangesAnswers.Enqueue(Services.SaveChangesChoice.Save);
        Assert.True(await Vm.CloseDocumentAsync(Vm.ActiveDocument!));

        Assert.Contains("r=\"7\"", File.ReadAllText(path));
        Assert.Empty(Vm.Documents);
    }

    [AvaloniaFact]
    public async Task Tab_thumbnail_is_a_render_of_the_drawing()
    {
        await Vm.OpenFileAsync(Sample("shapes.svg"));
        Dispatcher.UIThread.RunJobs();
        var thumbnail = Vm.ActiveDocument!.Thumbnail;
        Assert.NotNull(thumbnail);
        Assert.Equal((44, 27), (thumbnail.PixelSize.Width, thumbnail.PixelSize.Height));
    }

    [AvaloniaFact]
    public async Task A_recent_drawing_gets_a_rendered_thumbnail_on_the_welcome_screen()
    {
        _h.Vm.RecentFiles.Add(Sample("shapes.svg"));
        var item = Assert.Single(_h.Vm.RecentItems);
        for (var i = 0; i < 100 && !item.HasThumbnail; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(item.HasThumbnail);
        var bitmap = item.Thumbnail!;
        Assert.Equal((Services.RecentThumbnails.Width, Services.RecentThumbnails.Height), (bitmap.PixelSize.Width, bitmap.PixelSize.Height));
        _h.Capture("svg-15-welcome-thumbnail");

        // Opening it from the tile opens a drawing, not an image.
        await _h.Vm.OpenRecentItemCommand.ExecuteAsync(item);
        Assert.True(_h.Vm.ActiveDocument!.IsSvg);
    }

    [AvaloniaFact]
    public async Task Invalid_svg_shows_an_error_and_opens_nothing()
    {
        var path = _h.TempPath("broken.svg");
        await File.WriteAllTextAsync(path, "<svg><oops></svg>");

        Assert.False(await Vm.OpenFileAsync(path));

        Assert.Empty(Vm.Documents);
        Assert.Equal(["Could not open \"broken.svg\""], _h.Dialogs.Errors);
    }
}
