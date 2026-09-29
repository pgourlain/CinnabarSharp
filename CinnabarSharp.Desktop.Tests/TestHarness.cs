using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Controls;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

/// <summary>Real MainWindow + services, with scripted dialogs and a throwaway temp folder.</summary>
public sealed class TestHarness : IDisposable
{
    private static readonly string ScreenshotDir = Path.Combine(AppContext.BaseDirectory, "screenshots");

    public TestHarness()
    {
        TempDir = Directory.CreateTempSubdirectory("cinnabarsharp-ui-");
        var recentPath = Path.Combine(TempDir.FullName, "recent.json");
        var historyDir = new DirectoryInfo(Path.Combine(TempDir.FullName, "history"));
        Services = AppServices.Build(s =>
        {
            s.AddSingleton(new RecentFilesStore(recentPath));
            s.AddSingleton<IHistoryStorage>(new FileHistoryStorage(historyDir));
        });
        Vm = Services.GetRequiredService<MainViewModel>();
        Window = new MainWindow { DataContext = Vm, Width = 1280, Height = 800 };
        Window.Show();
        Vm.Dialogs = Dialogs;
        Vm.Clipboard = Clipboard;
        Dispatcher.UIThread.RunJobs();
    }

    public DirectoryInfo TempDir { get; }
    public IServiceProvider Services { get; }
    public MainViewModel Vm { get; }
    public MainWindow Window { get; }
    public FakeDialogService Dialogs { get; } = new();
    public FakeClipboardService Clipboard { get; } = new();

    public CanvasView Canvas => Window.FindControl<CanvasView>("Canvas")!;
    public ScrollViewer Scroller => Window.FindControl<ScrollViewer>("CanvasScroller")!;

    public string TempPath(string name) => Path.Combine(TempDir.FullName, name);

    public static string SampleImage => Path.Combine(AppContext.BaseDirectory, "Data", "sample1.png");

    public WriteableBitmap Capture(string name) => CaptureWindow(Window, name);

    /// <summary>Renders any window and saves it to the screenshots folder (created if needed).</summary>
    public static WriteableBitmap CaptureWindow(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame()!;
        Directory.CreateDirectory(ScreenshotDir);
        frame.Save(Path.Combine(ScreenshotDir, name + ".png"), PngBitmapEncoderOptions.Default);
        return frame;
    }

    public static (byte R, byte G, byte B) PixelAt(WriteableBitmap frame, Point p)
    {
        using var fb = frame.Lock();
        var offset = (int)p.Y * fb.RowBytes + (int)p.X * 4;
        var px = new byte[4];
        Marshal.Copy(fb.Address + offset, px, 0, 4);
        if (fb.Format == Avalonia.Platform.PixelFormat.Rgba8888)
            return (px[0], px[1], px[2]);
        if (fb.Format == Avalonia.Platform.PixelFormat.Bgra8888)
            return (px[2], px[1], px[0]);
        throw new NotSupportedException($"Unexpected frame format {fb.Format}.");
    }

    public Point CanvasToWindow(double x, double y) => Canvas.TranslatePoint(new Point(x, y), Window)!.Value;

    /// <summary>Image coordinates of a window point at the current zoom.</summary>
    public Point WindowToImage(Point p)
    {
        var local = Window.TranslatePoint(p, Canvas)!.Value;
        var scale = Vm.ActiveDocument!.Document.Workspace.Scale;
        return new Point(local.X / scale, local.Y / scale);
    }

    public void Dispose()
    {
        Window.Close();
        try
        {
            TempDir.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
