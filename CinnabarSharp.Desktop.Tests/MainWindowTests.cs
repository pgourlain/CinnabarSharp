using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Controls;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

public class MainWindowTests
{
    private IServiceProvider _services = null!;

    private (MainWindow Window, MainViewModel Vm) Open()
    {
        var h = new TestHarness();
        _services = h.Services;
        _harness = h;
        return (h.Window, h.Vm);
    }

    private TestHarness _harness = null!;

    private WriteableBitmap Capture(Window window, string name) => _harness.Capture(name);

    private static (byte R, byte G, byte B) PixelAt(WriteableBitmap frame, Point p) => TestHarness.PixelAt(frame, p);

    private Point CanvasToWindow(MainWindow window, double x, double y) => _harness.CanvasToWindow(x, y);

    [AvaloniaFact]
    public void Starts_empty_with_document_commands_disabled()
    {
        var (window, vm) = Open();

        Assert.Empty(vm.Documents);
        Assert.False(vm.HasDocument);
        Assert.False(vm.ZoomInCommand.CanExecute(null));
        Assert.False(vm.AddNewLayerCommand.CanExecute(null));
        Assert.True(vm.NewImageCommand.CanExecute(null));
        Assert.Equal("CinnabarSharp", window.Title);
        Assert.True(window.FindControl<Control>("WelcomePanel")!.IsVisible);
        Assert.False(window.FindControl<TextBlock>("EmptyHint")!.IsVisible);

        Capture(window, "01-empty");
    }

    [AvaloniaFact]
    public void Active_tab_stays_visible_when_many_images_are_open()
    {
        var (window, vm) = Open();
        window.Width = 600;

        for (var i = 0; i < 15; i++)
            vm.CreateImage(new NewImageOptions(new ImageSize(40, 30), ColorBgra.White));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var tabs = window.FindControl<ListBox>("TabsList")!;
        var scroller = tabs.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(ScrollBarVisibility.Auto, scroller.HorizontalScrollBarVisibility);
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width);
        // The last document is the active one: its tab must be inside the viewport.
        Assert.True(scroller.Offset.X > 0);
    }

    [AvaloniaFact]
    public void New_white_image_is_shown_on_canvas()
    {
        var (window, vm) = Open();

        vm.CreateImage(new NewImageOptions(new ImageSize(400, 300), ColorBgra.White));
        var frame = Capture(window, "02-new-white-image");

        var doc = Assert.Single(vm.Documents);
        Assert.Same(doc, vm.ActiveDocument);
        Assert.Equal("Unsaved Image 1 - CinnabarSharp", window.Title);
        Assert.Equal("400 × 300", vm.ImageSizeText);
        Assert.Equal("100%", vm.ZoomText);
        var layer = Assert.Single(vm.Layers);
        Assert.Equal("Background", layer.Name);
        Assert.Same(layer, vm.SelectedLayer);
        Assert.False(window.FindControl<TextBlock>("EmptyHint")!.IsVisible);

        Assert.Equal((255, 255, 255), PixelAt(frame, CanvasToWindow(window, 200, 150)));
        Assert.Equal((69, 66, 62), PixelAt(frame, CanvasToWindow(window, -8, 150)));
    }

    [AvaloniaFact]
    public void Transparent_image_shows_checkerboard()
    {
        var (window, vm) = Open();

        vm.CreateImage(new NewImageOptions(new ImageSize(400, 300), ColorBgra.Transparent));
        var frame = Capture(window, "03-new-transparent-image");

        Assert.Equal((255, 255, 255), PixelAt(frame, CanvasToWindow(window, 2, 2)));
        Assert.Equal((204, 204, 204), PixelAt(frame, CanvasToWindow(window, 10, 2)));
    }

    [AvaloniaFact]
    public void Zoom_follows_paint_dot_net_presets()
    {
        var (window, vm) = Open();
        vm.CreateImage(new NewImageOptions(new ImageSize(200, 100), ColorBgra.White));

        vm.ZoomInCommand.Execute(null);
        Assert.Equal("125%", vm.ZoomText);
        vm.ZoomInCommand.Execute(null);
        Assert.Equal("150%", vm.ZoomText);
        vm.ActualSizeCommand.Execute(null);
        Assert.Equal("100%", vm.ZoomText);
        vm.ZoomOutCommand.Execute(null);
        Assert.Equal("75%", vm.ZoomText);

        Dispatcher.UIThread.RunJobs();
        var canvas = window.FindControl<CanvasView>("Canvas")!;
        Assert.Equal(new Size(150, 75), canvas.Bounds.Size);

        for (var i = 0; i < 10; i++)
            vm.ZoomInCommand.Execute(null);
        Capture(window, "04-zoomed-in");
    }

    [AvaloniaFact]
    public void Large_image_opens_fitted_to_window()
    {
        var (window, vm) = Open();

        vm.CreateImage(new NewImageOptions(new ImageSize(4000, 3000), ColorBgra.White));
        Capture(window, "05-large-image-fitted");

        var zoom = vm.ActiveDocument!.Image.Workspace.Scale;
        Assert.InRange(zoom, 0.05, 0.99);
        var canvas = window.FindControl<CanvasView>("Canvas")!;
        var scroller = window.FindControl<ScrollViewer>("CanvasScroller")!;
        Assert.True(canvas.Bounds.Width <= scroller.Bounds.Width);
        Assert.True(canvas.Bounds.Height <= scroller.Bounds.Height);
    }

    [AvaloniaFact]
    public void Add_layer_and_hide_background()
    {
        var (window, vm) = Open();
        vm.CreateImage(new NewImageOptions(new ImageSize(400, 300), ColorBgra.White));

        vm.AddNewLayerCommand.Execute(null);

        Assert.Equal(["Layer 2", "Background"], vm.Layers.Select(l => l.Name));
        Assert.Equal("Layer 2", vm.SelectedLayer!.Name);

        vm.Layers[1].IsVisible = false;
        var frame = Capture(window, "06-background-hidden");

        Assert.True(vm.Layers[1].Layer.Hidden);
        Assert.Equal((255, 255, 255), PixelAt(frame, CanvasToWindow(window, 2, 2)));
        Assert.Equal((204, 204, 204), PixelAt(frame, CanvasToWindow(window, 10, 2)));
    }

    [AvaloniaFact]
    public void Switching_tabs_changes_active_document()
    {
        var (window, vm) = Open();
        vm.CreateImage(new NewImageOptions(new ImageSize(100, 100), ColorBgra.White));
        vm.CreateImage(new NewImageOptions(new ImageSize(300, 200), ColorBgra.Transparent));
        Assert.Equal("300 × 200", vm.ImageSizeText);

        vm.ActiveDocument = vm.Documents[0];
        Capture(window, "07-two-tabs");

        var workspace = _services.GetRequiredService<IWorkspaceService>();
        Assert.Same(vm.Documents[0].Document, workspace.ActiveDocument);
        Assert.Equal("100 × 100", vm.ImageSizeText);
        Assert.Equal("Unsaved Image 1 - CinnabarSharp", window.Title);
    }

    [AvaloniaFact]
    public void Status_bar_shows_cursor_position_in_image_pixels()
    {
        var (window, vm) = Open();
        vm.CreateImage(new NewImageOptions(new ImageSize(400, 300), ColorBgra.White));
        vm.ZoomInCommand.Execute(null);
        vm.ZoomInCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        window.MouseMove(CanvasToWindow(window, 150, 75));
        Assert.Equal("100, 50", vm.CursorPositionText);

        window.MouseMove(new Point(2, 2));
        Assert.Equal("", vm.CursorPositionText);
    }

    [AvaloniaFact]
    public void Menu_is_native_on_macos_and_in_window_elsewhere()
    {
        var (window, _) = Open();
        var menuHost = window.FindControl<ContentControl>("MenuHost")!;
        var nativeMenu = NativeMenu.GetMenu(window);

        string[] expected = ["File", "Edit", "View", "Object", "Path", "Image", "Layers", "Adjustments", "Effects", "Photo"];
        if (OperatingSystem.IsMacOS())
        {
            Assert.Null(menuHost.Content);
            Assert.Equal(expected, nativeMenu!.Items.OfType<NativeMenuItem>().Select(i => i.Header));
        }
        else
        {
            Assert.Null(nativeMenu);
            var menu = Assert.IsType<Menu>(menuHost.Content);
            var headers = menu.ItemsSource!.Cast<MenuItem>().Select(i => ((string)i.Header!).Replace("_", ""));
            Assert.Equal([.. expected, "Help"], headers);
        }
    }

    [AvaloniaFact]
    public void Swap_colors_with_x_key()
    {
        var (window, vm) = Open();

        window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, "x");

        Assert.Equal(Avalonia.Media.Colors.White, vm.PrimaryColor);
        Assert.Equal(Avalonia.Media.Colors.Black, vm.SecondaryColor);
    }
}

public class NewImageViewModelTests
{
    [Theory]
    [InlineData(800, 600, true)]
    [InlineData(1, 1, true)]
    [InlineData(0, 600, false)]
    [InlineData(800, 16385, false)]
    public void Validates_dimensions(int width, int height, bool valid)
    {
        var vm = new NewImageViewModel(new ImageSize(width, height));
        Assert.Equal(valid, vm.ToOptions() is not null);
    }

    [Fact]
    public void Transparent_background_option()
    {
        var vm = new NewImageViewModel(new ImageSize(10, 10)) { TransparentBackground = true };
        Assert.Equal(ColorBgra.Transparent, vm.ToOptions()!.Background);
    }
}
