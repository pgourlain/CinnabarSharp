using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Desktop.ViewModels;
using PointD = CinnabarSharp.Core.Models.PointD;

namespace CinnabarSharp.Desktop.Tests;

/// <summary>Page de BD: photos assembled into a comic page, framed on the canvas.</summary>
public sealed class ComicPageUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;
    private ImageDocument Doc => Vm.ActiveDocument!.Document;

    private static readonly ColorBgra Blue = ColorBgra.FromBgra(255, 0, 0, 255);

    /// <summary>The sample photo and a plain blue image are open.</summary>
    private async Task OpenTwoImages()
    {
        Assert.True(await Vm.OpenFileAsync(TestHarness.SampleImage));
        Vm.CreateImage(new NewImageOptions(new ImageSize(300, 200), Blue));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Drags between two points in page pixels (the page is zoomed out to fit).</summary>
    private void Drag(double x1, double y1, double x2, double y2)
    {
        Dispatcher.UIThread.RunJobs();
        _h.Window.UpdateLayout();
        var scale = Doc.Workspace.Scale;
        var a = _h.CanvasToWindow(x1 * scale, y1 * scale);
        var b = _h.CanvasToWindow(x2 * scale, y2 * scale);
        _h.Window.MouseDown(a, MouseButton.Left);
        _h.Window.MouseMove(b, RawInputModifiers.LeftMouseButton);
        _h.Window.MouseUp(b, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static (int X, int Y) Center(RectangleI r) => (r.X + r.Width / 2, r.Y + r.Height / 2);

    [AvaloniaFact]
    public async Task Open_images_become_a_page_edited_on_the_canvas_then_applied()
    {
        await OpenTwoImages();
        ComicPageViewModel? dialog = null;
        _h.Dialogs.ComicPageAnswer = comic =>
        {
            dialog = comic;
            Assert.Equal(["sample1.png", "Unsaved Image 1"], comic.Sources.Select(s => s.Name));
            Assert.Equal("2 rows", comic.SelectedLayout.Name); // chosen for two photos
            return true;
        };

        await Vm.ComicPageCommand.ExecuteAsync(null);

        Assert.True(Vm.IsComicMode);
        Assert.Equal(3, Vm.Documents.Count);
        Assert.Equal("Comic page", Doc.DisplayName);
        Assert.Equal(new ImageSize(2480, 3508), Doc.ImageSize); // A4 portrait
        Assert.NotNull(Vm.Overlay?.Picture);                  // the page preview
        Assert.False(_h.Window.FindControl<ListBox>("ToolsList")!.IsEffectivelyEnabled); // tools locked while editing the page

        var tool = Vm.Comic!.Tool!;
        var rects = tool.PanelRects;
        Assert.Equal(0, tool.Selected);
        Assert.Equal("Panel 1:", Vm.Comic.PanelText);

        // Click the second panel: it is selected (the options bar follows).
        var (x2, y2) = Center(rects[1]);
        Drag(x2, y2, x2, y2);
        Assert.Equal(1, tool.Selected);
        Assert.Equal("Panel 2:", Vm.Comic.PanelText);
        Assert.Equal("Unsaved Image 1", Vm.Comic.SelectedPanelChoice!.Name);

        // Drag the photo in the first panel, then zoom it.
        var (x1, y1) = Center(rects[0]);
        Drag(x1, y1, x1 + 300, y1);
        Assert.Equal(0, tool.Selected);
        Assert.True(tool.Contents[0]!.Center.X < 0.5); // moved right: shows more of its left part
        Vm.Comic.PanelZoom = 200;
        Assert.Equal(2, tool.Contents[0]!.Zoom);
        _h.Capture("101-comic-page-mode");

        _h.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        for (var i = 0; i < 500 && (Vm.IsBusy || Vm.History[^1].Text != "Comic Page"); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.False(Vm.IsComicMode);
        Assert.Equal(["New Image", "Comic Page"], Vm.History.Select(h => h.Text));
        Assert.True(Doc.IsDirty);
        var pixels = Doc.Layers[0].Surface;
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels.ReadRegion(new RectangleI(10, 10, 1, 1)));        // white page
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.ReadRegion(new RectangleI(x2, y2, 1, 1)));            // the blue image
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixels.ReadRegion(new RectangleI(rects[1].X + 2, y2, 1, 1))); // its border
        Assert.NotNull(dialog);
    }

    [AvaloniaFact]
    public async Task A_panel_can_be_emptied_and_the_layout_changed()
    {
        await OpenTwoImages();
        await Vm.ComicPageCommand.ExecuteAsync(null);
        var comic = Vm.Comic!;
        var tool = comic.Tool!;

        comic.SelectedPanelChoice = ComicPageViewModel.Empty;
        Assert.Null(tool.Contents[0]);
        Assert.False(comic.PanelHasPhoto);

        comic.SelectedLayout = ComicPageViewModel.Layouts.Single(l => l.Name == "Classic (2 + 1 + 2)");
        Assert.Equal(5, tool.PanelRects.Count);
        Assert.NotNull(tool.Contents[1]); // the blue image stays in panel 2

        comic.Gutter = 100;
        Assert.Equal(100, tool.Options.Gutter);
        comic.SelectedBackground = ComicPageViewModel.Backgrounds[1];
        Assert.Equal(ColorBgra.Black, tool.Options.Background);
        Assert.Equal(ColorBgra.White, tool.Options.Border);
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_page_and_cancel_in_the_dialog_changes_nothing()
    {
        await OpenTwoImages();
        _h.Dialogs.ComicPageAnswer = _ => false;
        await Vm.ComicPageCommand.ExecuteAsync(null);
        Assert.False(Vm.IsComicMode);
        Assert.Equal(2, Vm.Documents.Count);

        _h.Dialogs.ComicPageAnswer = _ => true;
        await Vm.ComicPageCommand.ExecuteAsync(null);
        Assert.Equal(3, Vm.Documents.Count);

        _h.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Vm.IsComicMode);
        Assert.Equal(2, Vm.Documents.Count);
        Assert.Null(Vm.Overlay?.Picture);
    }

    [AvaloniaFact]
    public void Dialog_adds_files_orders_photos_and_renders()
    {
        var comic = new ComicPageViewModel([]);
        Assert.Equal("1 panel", comic.SelectedLayout.Name);

        Assert.Equal(1, comic.AddFiles([TestHarness.SampleImage, Path.Combine(_h.TempDir.FullName, "missing.png")]));
        Assert.Equal(2, comic.AddFiles([TestHarness.SampleImage, TestHarness.SampleImage]));
        Assert.Equal(3, comic.Sources.Count);
        Assert.Equal("1 large + 2 small", comic.SelectedLayout.Name);

        var last = comic.Sources[2];
        comic.MoveUpCommand.Execute(last);
        Assert.Same(last, comic.Sources[1]);
        comic.Sources[0].IsIncluded = false;
        Assert.Equal(2, comic.Included.Count);

        var window = new Views.ComicPageWindow { DataContext = comic };
        window.Show();
        TestHarness.CaptureWindow(window, "102-comic-page-dialog");
        window.Close();
    }
}
