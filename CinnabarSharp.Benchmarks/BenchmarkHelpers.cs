using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Benchmarks;

/// <summary>Shared setup for benchmarks: a fresh DI container per benchmark instance, and synthetic images
/// (noise, so pixel data is incompressible and realistic) instead of files checked into git.</summary>
public static class BenchmarkHelpers
{
    public static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCinnabarSharpServices();
        return services.BuildServiceProvider();
    }

    /// <summary>A document of the given size with <paramref name="layerCount"/> noise-filled layers.</summary>
    public static ImageDocument CreateDocument(IServiceProvider sp, int width, int height, int layerCount = 1)
    {
        var workspace = sp.GetRequiredService<IWorkspaceService>();
        var doc = workspace.NewDocument(new ImageSize(width, height), ColorBgra.White);
        FillNoise(doc.Layers.CurrentUserLayer, width, height, seed: 0);
        for (var i = 1; i < layerCount; i++)
        {
            var layer = doc.Layers.AddNewLayer($"Layer {i}");
            FillNoise(layer, width, height, seed: i);
            layer.Opacity = 0.8;
        }
        return doc;
    }

    private static void FillNoise(Layer layer, int width, int height, int seed)
    {
        var bgra = new byte[width * height * 4];
        new Random(seed).NextBytes(bgra);
        for (var i = 3; i < bgra.Length; i += 4)
            bgra[i] = 255; // opaque: keeps compositing cost representative of a real photo, not random alpha
        var old = layer.Surface;
        layer.Surface = Utility.FromBgra(bgra, width, height);
        old.Dispose();
    }
}
