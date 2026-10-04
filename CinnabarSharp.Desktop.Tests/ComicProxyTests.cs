using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

/// <summary>
/// The comic page works on reduced copies (proxies) of its photos until Apply (performance-tasks.md P7.1): sources are read
/// only when a panel needs them, a bigger proxy is made when a panel needs more pixels, Apply reads the full photos.
/// </summary>
public sealed class ComicProxyTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private static BgraImage Noise(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        new Random(1).NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        return new BgraImage(pixels, width, height);
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), what);
    }

    [AvaloniaFact]
    public async Task Open_images_are_read_only_when_a_panel_needs_them()
    {
        for (var i = 0; i < 3; i++)
            Vm.CreateImage(new NewImageOptions(new ImageSize(400, 300), ColorBgra.White));
        _h.Dialogs.ComicPageAnswer = comic =>
        {
            Assert.All(comic.Sources, s => Assert.Null(s.Source.Proxy)); // nothing read just to show the dialog
            comic.SelectedLayout = comic.Layouts.Single(l => l.Name == "2 rows"); // two panels for three photos
            return true;
        };

        await Vm.ComicPageCommand.ExecuteAsync(null);

        var sources = Vm.Comic!.Sources.Select(s => s.Source).ToList();
        Assert.NotNull(sources[0].Proxy);
        Assert.NotNull(sources[1].Proxy);
        Assert.Null(sources[2].Proxy); // no panel for it: never read
    }

    [Fact]
    public async Task A_proxy_is_made_for_what_the_panel_shows_and_a_bigger_one_when_it_needs_more()
    {
        var reads = 0;
        var source = new ComicSource("big", 4000, 3000, () => { reads++; return Noise(4000, 3000); });
        var comic = new ComicPageViewModel([source]);
        comic.SelectedLayout = comic.Layouts.Single(l => l.Name == "3 × 3 grid");
        var tool = comic.CreateTool();
        Assert.True(tool.Contents[0]!.Photo.Width < 100); // a stand-in with the photo's proportions until the proxy is made

        await ComicProxies.EnsureAsync(tool, 0.5, CancellationToken.None);

        Assert.Equal(1, reads);
        var first = source.ProxyScale;
        Assert.InRange(first, 0.1, 0.3); // a 4000 pixel wide photo in a 1260 pixel panel shown at half size
        Assert.Same(source.Proxy, tool.Contents[0]!.Photo);
        Assert.Equal((4000, 3000), (source.FullWidth, source.FullHeight));
        Assert.Equal(4000.0 / 3000, (double)source.Proxy!.Width / source.Proxy.Height, 2);

        await ComicProxies.EnsureAsync(tool, 0.5, CancellationToken.None);
        Assert.Equal(1, reads); // nothing changed: nothing read again

        tool.SetZoom(0, 4); // a quarter of the width shown in the same panel: four times the resolution
        await ComicProxies.EnsureAsync(tool, 0.5, CancellationToken.None);
        Assert.Equal(2, reads);
        Assert.True(source.ProxyScale > first * 3);
        Assert.Same(source.Proxy, tool.Contents[0]!.Photo);
        Assert.Equal(4.0, tool.Contents[0]!.Zoom); // the framing is kept

        tool.SetZoom(0, 3.9); // a little less: the proxy it has is enough
        await ComicProxies.EnsureAsync(tool, 0.5, CancellationToken.None);
        Assert.Equal(2, reads);
    }

    [AvaloniaFact]
    public async Task Zooming_a_panel_in_the_app_makes_a_sharper_proxy_in_the_background()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(4000, 3000), ColorBgra.White));
        _h.Dialogs.ComicPageAnswer = comic =>
        {
            comic.SelectedLayout = comic.Layouts.Single(l => l.Name == "3 × 3 grid");
            return true;
        };
        await Vm.ComicPageCommand.ExecuteAsync(null);
        var source = Vm.Comic!.Sources[0].Source;
        var before = source.ProxyScale;
        Assert.InRange(before, 0.05, 0.4);

        Vm.Comic.PanelZoom = 400;

        await Eventually(() => source.ProxyScale > before * 2, "a sharper proxy for the zoomed panel");
        await Eventually(() => Vm.Overlay?.Picture is not null, "a preview");
    }

    [AvaloniaFact]
    public async Task Apply_reads_the_files_again_and_a_missing_one_keeps_the_page_open()
    {
        await Vm.OpenFileAsync(TestHarness.SampleImage);
        var copy = _h.TempPath("photo.png");
        File.Copy(TestHarness.SampleImage, copy);
        _h.Dialogs.ComicPageAnswer = comic =>
        {
            Assert.Equal(1, comic.AddFiles([copy]));
            return true;
        };
        await Vm.ComicPageCommand.ExecuteAsync(null);
        Assert.True(Vm.IsComicMode);
        File.Delete(copy); // moved while the page was being edited

        await Vm.ApplyComicCommand.ExecuteAsync(null);

        Assert.Contains("Could not create the comic page", _h.Dialogs.Errors);
        Assert.True(Vm.IsComicMode); // still there: another photo can go in that panel
        Vm.CancelComicCommand.Execute(null);
        Assert.False(Vm.IsComicMode);
    }

    [AvaloniaFact]
    public async Task The_thumbnail_of_an_open_image_is_made_in_the_background()
    {
        var source = new ComicSource("lazy", 800, 600, () => Noise(800, 600));
        var choice = new ComicSourceViewModel(source, included: true);
        var changed = false;
        choice.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(ComicSourceViewModel.Thumbnail);

        Assert.Null(choice.Thumbnail); // asked for: starts reading, doesn't block
        await Eventually(() => changed, "the thumbnail to be announced");

        Assert.NotNull(choice.Thumbnail);
        Assert.Null(source.Proxy); // a thumbnail doesn't keep the photo
    }
}
