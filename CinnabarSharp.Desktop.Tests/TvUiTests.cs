using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ImageMagick;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

public sealed class TvUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;
    private ImageDocument Doc => Vm.ActiveDocument!.Document;

    private async Task OpenSample() => Assert.True(await Vm.OpenFileAsync(TestHarness.SampleImage));

    [AvaloniaFact]
    public void Crop_tool_frame_is_16_9_and_enter_crops()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(400, 300), ColorBgra.White));
        Vm.SelectedTool = Vm.Tools.First(t => t.Name == "Crop");
        Dispatcher.UIThread.RunJobs();
        Assert.True(Vm.ShowCropOptions);
        Assert.Equal("16:9 (TV)", Vm.SelectedCropAspect.Label);

        _h.Window.MouseDown(_h.CanvasToWindow(20, 20), MouseButton.Left);
        _h.Window.MouseMove(_h.CanvasToWindow(340, 100), RawInputModifiers.LeftMouseButton);
        _h.Window.MouseUp(_h.CanvasToWindow(340, 100), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(Vm.Overlay?.Shade);
        Assert.True(Vm.ApplyCropCommand.CanExecute(null));
        _h.Capture("90-crop-frame");

        _h.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new ImageSize(320, 180), Doc.ImageSize);
        Assert.Equal("Crop", Vm.History[^1].Text);
        Assert.Null(Vm.Overlay);
    }

    [AvaloniaFact]
    public async Task Prepare_for_tv_opens_a_new_image_with_the_tv_size_and_name()
    {
        await OpenSample();
        _h.Dialogs.PrepareForTvAnswer = options =>
        {
            Assert.True(options.HasWarning); // 1024 × 576 is smaller than 4K.
            options.Resolution = TvResolution.FullHd;
            options.Fit = TvFit.FitWithBorders;
            options.Background = TvBackground.Blurred;
            return true;
        };

        await Vm.PrepareForTvCommand.ExecuteAsync(null);

        Assert.Equal(new ImageSize(1920, 1080), Doc.ImageSize);
        Assert.Equal("sample1_2K", Doc.DisplayName);
        Assert.Equal("jpg", Doc.FileType);
        Assert.Equal(2, Vm.Documents.Count);
        Assert.Equal(TvResolution.FullHd, Vm.TvOptions.Resolution);
    }

    [AvaloniaFact]
    public async Task Side_by_side_combines_two_open_photos()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(300, 600), ColorBgra.FromBgra(0, 0, 255, 255)));
        Vm.CreateImage(new NewImageOptions(new ImageSize(300, 600), ColorBgra.FromBgra(255, 0, 0, 255)));
        _h.Dialogs.PrepareForTvAnswer = options =>
        {
            Assert.True(options.CanSideBySide);
            options.Resolution = TvResolution.FullHd;
            options.SideBySide = true;
            Assert.False(options.HasWarning);
            return true;
        };

        await Vm.PrepareForTvCommand.ExecuteAsync(null);

        var px = Doc.Layers[0].Surface.ReadRegion(new RectangleI(400, 540, 1, 1));
        var right = Doc.Layers[0].Surface.ReadRegion(new RectangleI(1500, 540, 1, 1));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, px);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, right);
    }

    [AvaloniaFact]
    public async Task Saving_a_jpeg_asks_its_quality_and_remembers_it()
    {
        await OpenSample();
        var path = _h.TempPath("photo.jpg");
        _h.Dialogs.SavePaths.Enqueue(path);
        _h.Dialogs.JpegQualityAnswer = 70;

        await Vm.SaveAsCommand.ExecuteAsync(null);

        Assert.Equal([90], _h.Dialogs.JpegQualityAsked);
        Assert.Equal(70, Vm.JpegQuality);
        using (var saved = new MagickImage(path))
            Assert.InRange(saved.Quality, 65u, 75u);

        _h.Dialogs.SavePaths.Enqueue(_h.TempPath("cancelled.jpg"));
        _h.Dialogs.JpegQualityAnswer = null;
        await Vm.SaveAsCommand.ExecuteAsync(null);
        Assert.False(File.Exists(_h.TempPath("cancelled.jpg")));
        Assert.Equal([90, 70], _h.Dialogs.JpegQualityAsked);
    }

    [AvaloniaFact]
    public async Task Folder_of_photos_is_prepared_in_one_go()
    {
        var folder = Directory.CreateDirectory(_h.TempPath("photos"));
        File.Copy(TestHarness.SampleImage, Path.Combine(folder.FullName, "one.png"));
        File.Copy(TestHarness.SampleImage, Path.Combine(folder.FullName, "two.png"));
        _h.Dialogs.Folders.Enqueue(folder.FullName);
        _h.Dialogs.PrepareForTvAnswer = options =>
        {
            Assert.True(options.IsBatch);
            Assert.False(options.CanSideBySide);
            options.Resolution = TvResolution.FullHd;
            return true;
        };

        await Vm.PrepareFolderForTvCommand.ExecuteAsync(null);

        Assert.Contains("2 photos saved", Assert.Single(_h.Dialogs.Messages));
        Assert.True(File.Exists(Path.Combine(folder.FullName, "TV 2K", "one_2K.jpg")));
        Assert.True(File.Exists(Path.Combine(folder.FullName, "TV 2K", "two_2K.jpg")));
    }

    [AvaloniaFact]
    public async Task Tv_dialogs_render()
    {
        await OpenSample();
        var window = new PrepareForTvWindow
        {
            DataContext = new PrepareForTvViewModel(Vm.TvOptions, Doc.ImageSize, null, Vm.Documents.ToList()),
        };
        window.Show();
        TestHarness.CaptureWindow(window, "91-prepare-for-tv");
        window.Close();

        var quality = new JpegQualityWindow { DataContext = new JpegQualityViewModel(90) };
        quality.Show();
        TestHarness.CaptureWindow(quality, "92-jpeg-quality");
        quality.Close();
    }
}
