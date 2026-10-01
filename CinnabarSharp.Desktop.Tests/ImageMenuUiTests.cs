using Avalonia.Headless.XUnit;
using Avalonia.Media;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

public sealed class ImageMenuUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private void NewImage(int w = 300, int h = 200) =>
        Vm.CreateImage(new NewImageOptions(new ImageSize(w, h), ColorBgra.White));

    [AvaloniaFact]
    public void Resize_dialog_keeps_aspect_ratio_and_supports_percentage()
    {
        var vm = new ResizeImageViewModel(new ImageSize(400, 200));

        vm.Width = 100;
        Assert.Equal(50, vm.Height);

        vm.MaintainAspectRatio = false;
        vm.Height = 70;
        Assert.Equal(new ImageSize(100, 70), vm.ToOptions()!.Size);

        vm.ByPercentage = true;
        vm.Percentage = 25;
        Assert.Equal(new ImageSize(100, 50), vm.ToOptions()!.Size);

        vm.Percentage = 0;
        Assert.Null(vm.ToOptions());
    }

    [AvaloniaFact]
    public void Canvas_size_dialog_defaults_to_center_anchor()
    {
        var vm = new CanvasSizeViewModel(new ImageSize(400, 200));
        Assert.False(vm.MaintainAspectRatio);

        vm.Width = 500;
        vm.SetAnchorCommand.Execute(Anchor.SE);

        Assert.Equal(new CanvasSizeOptions(new ImageSize(500, 200), Anchor.SE), vm.ToOptions());
    }

    [AvaloniaFact]
    public async Task Resize_and_canvas_size_commands_change_the_image()
    {
        NewImage();
        _h.Dialogs.ResizeAnswers.Enqueue(new ResizeImageOptions(new ImageSize(150, 100), ResamplingMode.BestQuality));
        await Vm.ResizeImageCommand.ExecuteAsync(null);
        Assert.Equal("150 × 100", Vm.ImageSizeText);

        Vm.SecondaryColor = Colors.Blue;
        _h.Dialogs.CanvasSizeAnswers.Enqueue(new CanvasSizeOptions(new ImageSize(250, 100), Anchor.W));
        await Vm.CanvasSizeCommand.ExecuteAsync(null);

        Assert.Equal("250 × 100", Vm.ImageSizeText);
        var frame = _h.Capture("60-canvas-size");
        Assert.Equal((0, 0, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(200, 50)));
        Assert.Equal((255, 255, 255), TestHarness.PixelAt(frame, _h.CanvasToWindow(50, 50)));
        Assert.Equal(["New Image", "Resize Image", "Canvas Size"], Vm.History.Select(h => h.Text));
    }

    [AvaloniaFact]
    public void Rotate_and_flip_commands()
    {
        NewImage(300, 200);
        Vm.SelectedTool = Vm.Tools.First(t => t.Name == "Pencil");
        var layer = Vm.ActiveDocument!.Document.Layers[0];
        layer.Surface.WriteRegion(new RectangleI(0, 0, 1, 1), [0, 0, 255, 255]);

        Vm.RotateClockwiseCommand.Execute(null);
        Assert.Equal("200 × 300", Vm.ImageSizeText);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, layer.Surface.ReadRegion(new RectangleI(199, 0, 1, 1)));

        Vm.FlipImageHorizontalCommand.Execute(null);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, layer.Surface.ReadRegion(new RectangleI(0, 0, 1, 1)));

        Vm.UndoCommand.Execute(null);
        Vm.UndoCommand.Execute(null);
        Assert.Equal("300 × 200", Vm.ImageSizeText);
    }

    [AvaloniaFact]
    public void Dialogs_render()
    {
        var resize = new ResizeImageWindow { DataContext = new ResizeImageViewModel(new ImageSize(1024, 576)) };
        resize.Show();
        TestHarness.CaptureWindow(resize, "61-resize-dialog");
        resize.Close();

        var canvas = new CanvasSizeWindow { DataContext = new CanvasSizeViewModel(new ImageSize(1024, 576)) };
        canvas.Show();
        TestHarness.CaptureWindow(canvas, "62-canvas-size-dialog");
        canvas.Close();

        var besideVm = new PasteBesideViewModel(new ImageSize(1024, 576), new ImageSize(400, 800));
        var beside = new PasteBesideWindow { DataContext = besideVm };
        beside.Show();
        Assert.Equal("New size: 1424 × 800 pixels", besideVm.ResultSizeText); // right (default)
        besideVm.SetSideCommand.Execute(CinnabarSharp.Core.Models.PasteSide.Bottom);
        Assert.Equal("New size: 1024 × 1376 pixels", besideVm.ResultSizeText);
        Assert.Equal("Left", besideVm.StartLabel);
        TestHarness.CaptureWindow(beside, "63-paste-beside-dialog");
        beside.Close();
    }
}
