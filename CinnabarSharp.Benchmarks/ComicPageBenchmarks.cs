using BenchmarkDotNet.Attributes;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;

namespace CinnabarSharp.Benchmarks;

/// <summary>
/// The comic page preview (performance-tasks.md P7.1): a 3 × 3 page of a 16:9 4K TV, 9 photos of 12 MP, composed at the
/// size the preview takes on screen (1920 px wide), as every drag, zoom or divider move does.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
public class ComicPageBenchmarks
{
    private static readonly ImageSize PreviewSize = new(1920, 1080);
    private ComicLayout _layout = null!;
    private ComicPageOptions _options = null!;
    private ComicPanelContent[] _fullPhotos = null!;
    private ComicPanelContent[] _proxies = null!;

    [GlobalSetup]
    public void Setup()
    {
        _layout = ComicPage.Layouts.Single(l => l.Name == "3 × 3 grid");
        _options = new ComicPageOptions(new ImageSize(3840, 2160), 20, 8, ColorBgra.Black, ColorBgra.White);
        var (width, height) = BenchmarkSizes.Medium;
        _fullPhotos = Enumerable.Range(0, 9).Select(i => new ComicPanelContent(Noise(width, height, i))).ToArray();

        // What the app does now: each panel holds a proxy sized for what it shows at this preview (with the 25 % margin).
        var rects = ComicPage.PanelRects(_layout, _options.Page, _options.Gutter);
        var previewScale = (double)PreviewSize.Width / _options.Page.Width;
        _proxies = _fullPhotos.Select((c, i) =>
        {
            var scale = Math.Min(1.0, ComicPage.NeededScale(width, height, c, rects[i], previewScale) * 1.25);
            var (pixels, w, h) = CinnabarSharp.Core.Effects.PhotoMath.Downscale(c.Photo.Pixels, width, height, (int)Math.Ceiling(Math.Max(width, height) * scale));
            return c with { Photo = new BgraImage(pixels, w, h) };
        }).ToArray();
    }

    /// <summary>Today: every panel crops and resizes its full 12 MP photo.</summary>
    [Benchmark(Baseline = true)]
    public BgraImage PreviewFromFullPhotos() => ComicPage.Compose(_layout, _options, _fullPhotos, PreviewSize);

    /// <summary>Now: the preview from proxies, bilinear.</summary>
    [Benchmark]
    public BgraImage PreviewFromProxies() => ComicPage.Compose(_layout, _options, _proxies, PreviewSize, quality: ComicQuality.Preview);

    private static BgraImage Noise(int width, int height, int seed)
    {
        var pixels = new byte[width * height * 4];
        new Random(seed).NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
            pixels[i] = 255;
        return new BgraImage(pixels, width, height);
    }
}
