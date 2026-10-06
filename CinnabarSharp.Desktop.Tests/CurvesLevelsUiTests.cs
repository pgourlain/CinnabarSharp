using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Adjustments;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.Controls;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

public sealed class CurvesLevelsUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;
    private ImageDocument Doc => Vm.ActiveDocument!.Image;

    // B, G, R, A
    private void NewImage() =>
        Vm.CreateImage(new NewImageOptions(new ImageSize(200, 100), ColorBgra.FromBgra(40, 90, 100, 255)));

    private byte[] Pixel(int x = 10, int y = 10) => Doc.Layers[0].Surface.ReadRegion(new RectangleI(x, y, 1, 1));

    [AvaloniaFact]
    public async Task Rgb_curve_on_one_channel_changes_only_that_channel()
    {
        NewImage();
        _h.Dialogs.CurvesAnswer = curves =>
        {
            curves.Mode = CurvesMode.Rgb;
            curves.EditGreen = false;
            curves.EditBlue = false;
            curves.PressAt(100, 100, remove: false);
            curves.DragTo(100, 200);
            curves.Release();
            return true;
        };

        await Vm.CurvesCommand.ExecuteAsync(null);

        Assert.Equal(new byte[] { 40, 90, 200, 255 }, Pixel());
        Assert.Equal(["Curves"], _h.Dialogs.EffectsShown);
        Assert.Equal(["New Image", "Curves"], Vm.History.Select(h => h.Text));
    }

    [AvaloniaFact]
    public async Task Cancelled_curves_leave_no_trace()
    {
        NewImage();
        _h.Dialogs.CurvesAnswer = curves =>
        {
            curves.PressAt(128, 128, remove: false);
            curves.DragTo(128, 250);
            return false;
        };

        await Vm.CurvesCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new byte[] { 40, 90, 100, 255 }, Pixel());
        Assert.Single(Vm.History);
    }

    [AvaloniaFact]
    public void Curve_editor_adds_moves_and_removes_points_with_the_mouse()
    {
        NewImage();
        var curves = new CurvesDialogViewModel(new EffectSession(Doc, new Curves()));
        var window = new CurvesWindow { DataContext = curves };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = window.FindControl<CurveEditor>("Editor")!;
        Point At(double x, double y) => editor.TranslatePoint(new Point(x / 255 * editor.Bounds.Width, (1 - y / 255) * editor.Bounds.Height), window)!.Value;

        window.MouseDown(At(128, 128), MouseButton.Left);
        window.MouseMove(At(128, 200), RawInputModifiers.LeftMouseButton);
        window.MouseUp(At(128, 200), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, curves.Luminosity.Count);
        Assert.InRange(curves.Luminosity[1].Y, 195, 205);
        TestHarness.CaptureWindow(window, "72-curves-dialog");

        window.MouseDown(At(curves.Luminosity[1].X, curves.Luminosity[1].Y), MouseButton.Right);
        window.MouseUp(At(curves.Luminosity[1].X, curves.Luminosity[1].Y), MouseButton.Right);
        Assert.Equal(CurvesSettings.Diagonal, curves.Luminosity);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Levels_on_the_red_channel_and_auto()
    {
        NewImage();
        _h.Dialogs.LevelsAnswer = levels =>
        {
            levels.Target = LevelsTarget.Red;
            levels.InputWhite = 200;
            Assert.Equal(200, levels.InputWhite);
            levels.Target = LevelsTarget.Green;
            Assert.Equal(255, levels.InputWhite);
            return true;
        };

        await Vm.LevelsCommand.ExecuteAsync(null);

        Assert.Equal(new byte[] { 40, 90, 128, 255 }, Pixel());
        Assert.Equal("Levels", Vm.History[^1].Text);
    }

    [AvaloniaFact]
    public void Levels_dialog_shows_input_and_output_histograms()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(256, 60), ColorBgra.White));
        Vm.SelectedTool = Vm.Tools.First(t => t.Name == "Gradient");
        Vm.PrimaryColor = Avalonia.Media.Color.FromRgb(20, 40, 60);
        Vm.SecondaryColor = Avalonia.Media.Color.FromRgb(200, 220, 180);
        Vm.ToolPointerDown(new Core.Tools.ToolPointer(new PointD(0, 30), Core.Tools.ToolButton.Left, Core.Tools.ToolModifiers.None));
        Vm.ToolPointerUp(new Core.Tools.ToolPointer(new PointD(256, 30), Core.Tools.ToolButton.Left, Core.Tools.ToolModifiers.None));

        var levels = new LevelsDialogViewModel(new EffectSession(Doc, new Levels()));
        levels.AutoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var window = new LevelsWindow { DataContext = levels };
        window.Show();
        TestHarness.CaptureWindow(window, "73-levels-dialog");

        Assert.True(levels.InputBlack > 0);
        Assert.True(levels.InputWhite < 255);
        Assert.Equal(levels.OutputHistogram.Red.Sum(), levels.InputHistogram.Red.Sum());
        static int Brightest(long[] h) => Array.FindLastIndex(h, c => c > 0);
        Assert.True(Brightest(levels.OutputHistogram.Red) > Brightest(levels.InputHistogram.Red));
        window.Close();
    }
}
