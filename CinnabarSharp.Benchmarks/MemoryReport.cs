using System.Diagnostics;
using CinnabarSharp.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Benchmarks;

/// <summary>
/// Memory with many photos open (performance-tasks.md P7): <c>dotnet run -c Release -- --memory [photos] [megapixels]</c>.
/// Opens that many noise "photos" (incompressible, like a real one), edits each once, then opens the comic page's way of
/// holding the photos (a flattened copy of every document) and prints the process's memory at each step.
/// </summary>
public static class MemoryReport
{
    public static void Run(string[] args)
    {
        var photos = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 15;
        var megapixels = args.Length > 2 && double.TryParse(args[2], out var m) ? m : 12;
        var width = (int)Math.Sqrt(megapixels * 1_000_000 * 4 / 3);
        var height = width * 3 / 4;
        Console.WriteLine($"{photos} photos of {width} x {height} ({width * (long)height / 1e6:0.#} MP)");
        Console.WriteLine($"{"step",-34} {"managed MB",11} {"process MB",11}");

        var services = BenchmarkHelpers.BuildServices();
        Print("start");
        var documents = new List<ImageDocument>();
        for (var i = 0; i < photos; i++)
        {
            documents.Add(BenchmarkHelpers.CreateDocument(services, width, height));
            if ((i + 1) % 5 == 0 || i == photos - 1)
                Print($"{i + 1} photos open");
        }

        foreach (var document in documents)
            document.Actions.AddNewLayer();
        Print("each edited once (a layer added)");

        // New (P7.1): a proxy sized for its panel for each of the 9 photos of a 3 x 3 page, the full photo dropped each time.
        var layout = Core.Photo.ComicPage.Layouts.Single(l => l.Name == "3 × 3 grid");
        var options = new Core.Photo.ComicPageOptions(new ImageSize(3840, 2160), 20, 8, ColorBgra.Black, ColorBgra.White);
        var rects = Core.Photo.ComicPage.PanelRects(layout, options.Page, options.Gutter);
        var proxies = documents.Take(9).Select((d, i) =>
        {
            var full = new Core.Photo.BgraImage(d.Layers.GetFlattenedBgra(includeToolLayer: false), width, height);
            var scale = Math.Min(1.0, Core.Photo.ComicPage.NeededScale(width, height, new Core.Photo.ComicPanelContent(full), rects[i], 0.5) * 1.25);
            var (pixels, w, h) = Core.Effects.PhotoMath.Downscale(full.Pixels, width, height, (int)Math.Ceiling(Math.Max(width, height) * scale));
            return new Core.Photo.BgraImage(pixels, w, h);
        }).ToList();
        Print("comic page now: 9 proxies");
        GC.KeepAlive(proxies);
        proxies = null;

        // Old: a full flattened copy of every open image, kept while the page is edited.
        var copies = documents.Select(d => new Core.Photo.BgraImage(d.Layers.GetFlattenedBgra(includeToolLayer: false), width, height)).ToList();
        Print("comic page before: full copies of all");
        GC.KeepAlive(copies);
        GC.KeepAlive(documents);
    }

    private static void Print(string step)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Console.WriteLine($"{step,-34} {GC.GetTotalMemory(true) / 1048576,11} {Process.GetCurrentProcess().WorkingSet64 / 1048576,11}");
    }
}
