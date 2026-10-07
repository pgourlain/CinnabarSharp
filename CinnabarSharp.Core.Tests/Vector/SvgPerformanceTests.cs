using System.Diagnostics;
using System.Text;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Vector;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class SvgPerformanceTests(ITestOutputHelper output) : BaseTests
{
    /// <summary>A map-like drawing: <paramref name="count"/> small closed paths with curves, spread over 4000 × 3000.</summary>
    public static string Map(int count)
    {
        var builder = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='4000' height='3000' viewBox='0 0 4000 3000'><g id='layer1'>");
        var random = new Random(7);
        for (var i = 0; i < count; i++)
        {
            var x = random.Next(0, 3900);
            var y = random.Next(0, 2900);
            builder.Append($"<path id='p{i}' d='M{x} {y} c10 -20 40 -20 50 0 s10 40 -20 50 l-30 -10 z' fill='#{random.Next(0x1000000):x6}' stroke='#222' stroke-width='1.5'/>");
        }
        return builder.Append("</g></svg>").ToString();
    }

    [Fact]
    public void An_svg_with_10000_paths_opens_quickly_and_draws_the_visible_part_quickly()
    {
        var text = Map(10_000);
        var workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();

        var clock = Stopwatch.StartNew();
        var root = SvgParser.Parse(text).Root;
        var parse = clock.ElapsedMilliseconds;
        var doc = workspace.OpenSvgDocument(root, null, null);
        var open = clock.ElapsedMilliseconds;

        // What the canvas draws at 100 %: a window of 1600 × 900 user units.
        clock.Restart();
        VectorRasterizer.Render(doc.Root, new VRectI(0, 0, 1600, 900), 1, doc.RenderOptions);
        var window = clock.ElapsedMilliseconds;

        clock.Restart();
        VectorRasterizer.Render(doc.Root, new VRectI(0, 0, 100, 100), 1, doc.RenderOptions);
        output.WriteLine($"100x100 window {clock.ElapsedMilliseconds} ms");

        clock.Restart();
        var copy = (SvgRoot)doc.Root.DeepClone();
        output.WriteLine($"clone {clock.ElapsedMilliseconds} ms, {copy.Descendants().Count()} nodes");

        clock.Restart();
        var thumbnail = doc.GetThumbnail(200);
        var thumb = clock.ElapsedMilliseconds;

        output.WriteLine($"parse {parse} ms, open {open} ms, 1600x900 window {window} ms, thumbnail {thumb} ms ({thumbnail.Width}x{thumbnail.Height})");
        // Timings go to the test output; shared CI machines are several times slower, so only a very loose bound is asserted.
        Assert.True(open < 30000, $"opening took {open} ms");
        Assert.True(window < 60000, $"drawing the window took {window} ms");
    }
}
