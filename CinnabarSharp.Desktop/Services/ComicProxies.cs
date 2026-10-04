using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// Keeps each panel of the comic page on a proxy sized for what it shows (performance-tasks.md P7.1): a bigger one is made
/// when the layout, a divider, the gutter or the zoom ask for more pixels, in the background; the preview uses the proxy it
/// has in the meantime.
/// </summary>
public static class ComicProxies
{
    /// <summary>Photos read at once: each is read at full size while its proxy is made.</summary>
    private const int MaxParallel = 3;

    /// <summary>
    /// Makes the proxies the panels need for a preview <paramref name="previewScale"/> times the page's size, then gives them
    /// to the panels. A source that can't be read (a file that was moved) keeps the proxy it has.
    /// </summary>
    public static async Task EnsureAsync(ComicPageTool tool, double previewScale, CancellationToken cancellation)
    {
        var rects = tool.PanelRects;
        var needs = new Dictionary<ComicSource, double>();
        for (var i = 0; i < tool.Contents.Count && i < rects.Count; i++)
        {
            if (tool.Contents[i] is { Source: ComicSource source } content)
            {
                var needed = ComicPage.NeededScale(source.FullWidth, source.FullHeight, content, rects[i], previewScale);
                needs[source] = needs.TryGetValue(source, out var other) ? Math.Max(other, needed) : needed;
            }
        }

        await Parallel.ForEachAsync(needs, new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = cancellation },
            (need, _) =>
            {
                try
                {
                    need.Key.EnsureProxy(need.Value);
                }
                catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ImageMagick.MagickException)
                {
                    AppLog.Warning("comic", $"Could not read {need.Key.Name} to make a sharper preview", e);
                }
                return ValueTask.CompletedTask;
            });

        // The contents may have changed while the proxies were made: look again.
        for (var i = 0; i < tool.Contents.Count; i++)
            if (tool.Contents[i] is { Source: ComicSource source } content && source.Proxy is { } proxy && !ReferenceEquals(content.Photo, proxy))
                tool.SwapPhoto(i, proxy);
    }
}
