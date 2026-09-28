using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ImageMagick;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using PointD = CinnabarSharp.Core.Models.PointD;
using System.Linq;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Core.Tools;
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

    /// <summary>Drags between two points in image pixels (the photos here are zoomed out to fit).</summary>
    private void Drag(double x1, double y1, double x2, double y2)
    {
        Dispatcher.UIThread.RunJobs();
        _h.Window.UpdateLayout(); // positions after a zoom
        var scale = Doc.Workspace.Scale;
        var a = _h.CanvasToWindow(x1 * scale, y1 * scale);
        var b = _h.CanvasToWindow(x2 * scale, y2 * scale);
        _h.Window.MouseDown(a, MouseButton.Left);
        _h.Window.MouseMove(b, RawInputModifiers.LeftMouseButton);
        _h.Window.MouseUp(b, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private void Press(Key key, PhysicalKey physical)
    {
        _h.Window.KeyPress(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    // 2000 × 1500 white photo with a red band at the bottom (y ≥ 1460).
    private ImageDocument NewPhotoWithRedBottom()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(2000, 1500), ColorBgra.White));
        Doc.SetSelection(SelectionMask.Rectangle(2000, 1500, new PointD(0, 1460), new PointD(2000, 1500)));
        Doc.Actions.FillSelection(ColorBgra.FromBgra(0, 0, 255, 255));
        Doc.SetSelection(null);
        Dispatcher.UIThread.RunJobs();
        return Doc;
    }

    [AvaloniaFact]
    public async Task Tv_frame_has_the_resolution_size_and_can_be_moved_then_applied()
    {
        var photo = NewPhotoWithRedBottom();
        var history = photo.Workspace.History.Items.Count;
        Vm.TvOptions = new TvOptions(TvResolution.FullHd, TvFit.CropToFill);

        Vm.PrepareForTvCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Vm.IsTvMode);
        Assert.Equal(new RectangleI(40, 210, 1920, 1080), Vm.Tv!.Crop); // 2K frame, centered on the photo
        Assert.NotNull(Vm.Overlay?.Shade);
        Assert.False(Vm.Tv.HasWarning);
        Assert.False(Vm.ShowCropOptions);

        Drag(1000, 750, 1000, 1400); // move the frame down: it stops at the bottom edge
        Assert.Equal(new RectangleI(40, 420, 1920, 1080), Vm.Tv.Crop);

        Drag(10, 10, 60, 60); // outside the frame: no new frame
        Assert.Equal(new RectangleI(40, 420, 1920, 1080), Vm.Tv.Crop);

        Press(Key.Enter, PhysicalKey.Enter);
        for (var i = 0; i < 200 && Vm.Documents.Count < 2; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.False(Vm.IsTvMode);
        Assert.Equal("Rectangle Select", Vm.SelectedTool.Name);
        Assert.Equal(new ImageSize(1920, 1080), Doc.ImageSize);
        Assert.EndsWith("_2K", Doc.DisplayName);
        Assert.Equal("jpg", Doc.FileType);
        Assert.Equal(TvResolution.FullHd, Vm.TvOptions.Resolution);
        // The kept area includes the red band at the bottom of the photo.
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Doc.Layers[0].Surface.ReadRegion(new RectangleI(960, 1075, 1, 1)));
        Assert.Equal(history, photo.Workspace.History.Items.Count); // the photo itself is unchanged
    }

    [AvaloniaFact]
    public void Changing_the_resolution_resets_the_frame_to_its_size_and_shows_a_frame_larger_than_the_photo()
    {
        NewPhotoWithRedBottom();
        Vm.TvOptions = new TvOptions(TvResolution.FullHd, TvFit.CropToFill);
        Vm.PrepareForTvCommand.Execute(null);
        var tv = Vm.Tv!;

        // Resize the frame from its bottom-right corner.
        Drag(1960, 1290, 1400, 1000);
        Assert.True(tv.Crop!.Value.Width < 1920);

        // 4K is larger than the photo: the frame covers it and extends beyond it, and the view zooms out to show it.
        var zoom = Doc.Workspace.Scale;
        tv.Resolution = TvResolution.Uhd4K;
        Dispatcher.UIThread.RunJobs();
        var frame = tv.Crop!.Value;
        Assert.Equal((3840, 2160), (frame.Width, frame.Height));
        Assert.True(frame.X < 0 && frame.Y < 0 && frame.X + frame.Width > 2000 && frame.Y + frame.Height > 1500);
        Assert.True(tv.HasWarning);
        Assert.True(Doc.Workspace.Scale < zoom);
        Assert.True(3840 * Doc.Workspace.Scale <= Vm.ViewportSize.Width);
        _h.Capture("90-prepare-for-tv-frame-larger-than-photo");

        // Back to 2K: the frame has the 2K size again, whatever it was resized to.
        tv.Resolution = TvResolution.FullHd;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((1920, 1080), (tv.Crop!.Value.Width, tv.Crop.Value.Height));
        Assert.False(tv.HasWarning);
        _h.Capture("90-prepare-for-tv-frame");
    }

    [AvaloniaFact]
    public void Escape_leaves_prepare_for_tv_without_changes()
    {
        NewPhotoWithRedBottom();
        Vm.SelectedTool = Vm.Tools.First(t => t.Name == "Paintbrush");
        Vm.PrepareForTvCommand.Execute(null);
        Assert.Equal("Crop", Vm.SelectedTool.Name);

        Press(Key.Escape, PhysicalKey.Escape);

        Assert.False(Vm.IsTvMode);
        Assert.Null(Vm.Overlay);
        Assert.Equal("Paintbrush", Vm.SelectedTool.Name);
        Assert.Single(Vm.Documents);
    }

    [AvaloniaFact]
    public void Frame_is_proposed_inside_the_selection_and_another_tool_leaves()
    {
        NewPhotoWithRedBottom();
        Vm.TvOptions = new TvOptions(TvResolution.FullHd, TvFit.CropToFill);
        Doc.SetSelection(SelectionMask.Rectangle(2000, 1500, new PointD(1600, 1200), new PointD(2000, 1500)));

        Vm.PrepareForTvCommand.Execute(null);
        Assert.Equal(new RectangleI(80, 420, 1920, 1080), Vm.Tv!.Crop); // centered on the selection, kept on the photo

        Vm.SelectedTool = Vm.Tools.First(t => t.Name == "Ellipse Select");
        Assert.False(Vm.IsTvMode);
        Assert.Equal("Ellipse Select", Vm.SelectedTool.Name);
    }

    /// <summary>Waits for a TV preview other than <paramref name="previous"/> and returns it.</summary>
    private OverlayPicture NextPreview(OverlayPicture? previous = null)
    {
        for (var i = 0; i < 500 && (Vm.Overlay?.Picture is null || ReferenceEquals(Vm.Overlay.Picture, previous)); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        var picture = Vm.Overlay?.Picture;
        Assert.NotNull(picture);
        Assert.NotSame(previous, picture);
        return picture;
    }

    /// <summary>Pixel of the preview at a fraction of its width and height, as (R, G, B).</summary>
    private static (byte R, byte G, byte B) At(OverlayPicture picture, double fx, double fy)
    {
        var i = ((int)(fy * (picture.Height - 1)) * picture.Width + (int)(fx * (picture.Width - 1))) * 4;
        return (picture.Bgra[i + 2], picture.Bgra[i + 1], picture.Bgra[i]);
    }

    [AvaloniaFact]
    public void Fit_with_borders_and_stretch_preview_the_tv_image_at_its_size()
    {
        NewPhotoWithRedBottom(); // 2000 × 1500 (4:3), red band at the bottom
        Vm.TvOptions = new TvOptions(TvResolution.FullHd, TvFit.CropToFill);
        Vm.PrepareForTvCommand.Execute(null);
        var tv = Vm.Tv!;

        // 2K screen (1920 × 1080) centered on the photo; the photo, taller than the screen, is scaled down to
        // 1440 × 1080 without distortion, with borders of 240 on each side.
        tv.Fit = TvFit.FitWithBorders;
        var fit = NextPreview();
        Assert.Equal(new RectangleD(40, 210, 1920, 1080), fit.Area);
        Assert.Equal(16 / 9.0, (double)fit.Width / fit.Height, 1);
        Assert.Equal((0, 0, 0), At(fit, 0.06, 0.5));        // left border
        Assert.Equal((0, 0, 0), At(fit, 0.94, 0.5));        // right border
        Assert.Equal((255, 255, 255), At(fit, 0.5, 0.5));   // the photo
        Assert.Equal((255, 0, 0), At(fit, 0.5, 1));         // its red band, at the bottom of the screen
        Assert.Empty(Vm.Overlay!.Handles); // nothing to drag
        Assert.Contains("without distortion", tv.Hint);
        _h.Capture("93-prepare-for-tv-fit-with-borders");

        tv.Background = TvBackground.White;
        var borders = NextPreview(fit);
        Assert.Equal((255, 255, 255), At(borders, 0.06, 0.5));

        // Stretch: the whole photo fills the screen, 33 % wider, no borders.
        tv.Fit = TvFit.Stretch;
        var stretch = NextPreview(borders);
        Assert.Equal((255, 255, 255), At(stretch, 0.01, 0.5));
        Assert.Equal((255, 0, 0), At(stretch, 0.01, 1));
        Assert.Contains("33", tv.Hint);
        Assert.Contains("wider", tv.Hint);
        _h.Capture("94-prepare-for-tv-stretch");

        // 4K: the screen is larger than the photo; the view zooms out to show all of it.
        var zoom = Doc.Workspace.Scale;
        tv.Resolution = TvResolution.Uhd4K;
        var large = NextPreview(stretch);
        Assert.Equal(new RectangleD(-920, -330, 3840, 2160), large.Area);
        Assert.True(Doc.Workspace.Scale < zoom);
        Assert.True(tv.HasWarning); // enlarged

        tv.Fit = TvFit.CropToFill;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Vm.Overlay!.Picture);
        Assert.NotEmpty(Vm.Overlay.Handles); // the frame to place is back
    }

    [AvaloniaFact]
    public async Task Fit_with_borders_applies_to_the_whole_photo()
    {
        await OpenSample();
        Vm.PrepareForTvCommand.Execute(null);
        Vm.Tv!.Fit = TvFit.FitWithBorders;
        Vm.Tv.Background = TvBackground.Blurred;
        Vm.Tv.Resolution = TvResolution.FullHd;

        await Vm.ApplyTvCommand.ExecuteAsync(null);

        Assert.Equal(new ImageSize(1920, 1080), Doc.ImageSize);
        Assert.Equal("sample1_2K", Doc.DisplayName);
        Assert.Equal(2, Vm.Documents.Count);
    }

    [AvaloniaFact]
    public async Task Side_by_side_combines_two_open_photos()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(300, 600), ColorBgra.FromBgra(0, 0, 255, 255)));
        Vm.CreateImage(new NewImageOptions(new ImageSize(300, 600), ColorBgra.FromBgra(255, 0, 0, 255)));
        Vm.PrepareForTvCommand.Execute(null);
        var tv = Vm.Tv!;
        Assert.True(tv.CanSideBySide);
        tv.Resolution = TvResolution.FullHd;
        tv.SideBySide = true;
        Assert.False(tv.HasWarning);
        Assert.False(tv.ShowsFrame);
        var preview = NextPreview(); // this photo (blue) on the left, the other one (red) on the right
        Assert.Equal((0, 0, 255), At(preview, 0.25, 0.5));
        Assert.Equal((255, 0, 0), At(preview, 0.75, 0.5));

        await Vm.ApplyTvCommand.ExecuteAsync(null);

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
            DataContext = new PrepareForTvViewModel(Vm.TvOptions, folder: _h.TempDir.FullName),
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
