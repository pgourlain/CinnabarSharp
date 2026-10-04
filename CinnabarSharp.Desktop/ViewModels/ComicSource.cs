using System;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Photo;

namespace CinnabarSharp.Desktop.ViewModels;

/// <summary>
/// A photo that can go in a comic page panel: an open image (flattened when needed) or a file added in the dialog. It does
/// not hold the photo at full resolution while the page is edited: only a <see cref="Proxy"/>, a reduced copy sized for
/// what the panels show, made on demand (performance-tasks.md P7.1). <see cref="LoadFull"/> reads the whole photo, for
/// making a proxy and for Apply.
/// </summary>
public sealed class ComicSource
{
    /// <summary>The proxy of a file added in the dialog: made from the one decoding done to read its size.</summary>
    public const int FileProxySide = 1600;

    /// <summary>A proxy bigger than this (bytes) is made smaller again when the page needs much less.</summary>
    private const long SmallProxyBytes = 16L * 1024 * 1024;

    /// <summary>A proxy has this much more resolution than needed, so small zoom or divider changes don't make a new one.</summary>
    private const double Margin = 1.25;

    public ComicSource(string name, int fullWidth, int fullHeight, Func<BgraImage> loadFull, BgraImage? proxy = null)
    {
        Name = name;
        FullWidth = fullWidth;
        FullHeight = fullHeight;
        LoadFull = loadFull;
        Proxy = proxy;
    }

    /// <summary>A photo already in memory (no reduced copy to make: it is its own proxy).</summary>
    public ComicSource(string name, BgraImage photo) : this(name, photo.Width, photo.Height, () => photo, photo)
    {
    }

    public string Name { get; }
    public int FullWidth { get; }
    public int FullHeight { get; }

    /// <summary>Reads the photo at full resolution (a new image each time; may take a while, run it off the UI thread).</summary>
    public Func<BgraImage> LoadFull { get; }

    /// <summary>The reduced copy the panels draw their preview from, or null before the first one is made.</summary>
    public BgraImage? Proxy { get; private set; }

    /// <summary>The proxy's size over the photo's, from 0 to 1 (0 without a proxy).</summary>
    public double ProxyScale => Proxy is null || FullWidth <= 0 ? 0 : Math.Min(1.0, (double)Proxy.Width / FullWidth);

    /// <summary>
    /// A stand-in with the photo's proportions and almost no pixels, for a panel until its proxy is made: the framing
    /// maths only needs the shape.
    /// </summary>
    public BgraImage Placeholder()
    {
        var (w, h) = (Math.Max(1, FullWidth / 64), Math.Max(1, FullHeight / 64));
        return new BgraImage(new byte[w * h * 4], w, h);
    }

    /// <summary>
    /// Makes a proxy if there is none, if it has less resolution than <paramref name="needed"/> (see
    /// <see cref="ComicPage.NeededScale"/>), or if it is big and much more than needed. Returns true when it made one.
    /// Reads the photo, so call it off the UI thread.
    /// </summary>
    public bool EnsureProxy(double needed)
    {
        var tooSmall = Proxy is null || ProxyScale < needed - 1e-6;
        var tooBig = Proxy is not null && ProxyScale > 2 * needed && Proxy.Pixels.LongLength > SmallProxyBytes;
        if (!tooSmall && !tooBig)
            return false;
        var scale = Math.Min(1.0, needed * Margin);
        var full = LoadFull();
        Proxy = scale >= 0.95
            ? full
            : Reduce(full, (int)Math.Ceiling(Math.Max(full.Width, full.Height) * scale));
        return true;
    }

    /// <summary>The photo reduced so its longer side is <paramref name="longestSide"/> pixels (box filter, cheap).</summary>
    public static BgraImage Reduce(BgraImage photo, int longestSide)
    {
        var (pixels, width, height) = PhotoMath.Downscale(photo.Pixels, photo.Width, photo.Height, Math.Max(1, longestSide));
        return new BgraImage(pixels, width, height);
    }
}
