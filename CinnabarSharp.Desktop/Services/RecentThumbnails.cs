using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CinnabarSharp.Core.Extensions;
using ImageMagick;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// Small pictures of the recent files for the welcome screen, read in the background one at a time (a HEIC photo
/// takes seconds to decode, and the first window must not wait for them). Kept for the session by path and date.
/// </summary>
public static class RecentThumbnails
{
    public const int Width = 208;
    public const int Height = 144;

    private static readonly SemaphoreSlim OneAtATime = new(1);
    private static readonly ConcurrentDictionary<(string Path, DateTime Modified), Bitmap?> Cache = new();

    /// <summary>The picture, filling <see cref="Width"/> × <see cref="Height"/>; null when the file can't be read.</summary>
    public static async Task<Bitmap?> LoadAsync(string path)
    {
        try
        {
            var key = (path, File.GetLastWriteTimeUtc(path));
            if (Cache.TryGetValue(key, out var cached))
                return cached;
            await OneAtATime.WaitAsync();
            try
            {
                var pixels = await Task.Run(() => Read(path));
                var bitmap = pixels is { } p ? BitmapFactory.FromBgra(p.Bgra, p.Width, p.Height) : null;
                Cache[key] = bitmap;
                return bitmap;
            }
            finally
            {
                OneAtATime.Release();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (byte[] Bgra, int Width, int Height)? Read(string path)
    {
        try
        {
            using var image = path.EndsWith(".ora", StringComparison.OrdinalIgnoreCase) ? ReadOra(path) : ReadPhoto(path);
            if (image is null)
                return null;
            image.AutoOrient();
            image.Thumbnail(new MagickGeometry(Width, Height) { FillArea = true });
            image.Crop(Width, Height, Gravity.Center);
            image.ResetPage();
            return (image.ToBgra(), (int)image.Width, (int)image.Height);
        }
        catch (MagickException)
        {
            return null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            return null;
        }
    }

    private static MagickImage ReadPhoto(string path)
    {
        var settings = new MagickReadSettings();
        // JPEG can be decoded at a fraction of its size, much faster than a full decode.
        settings.SetDefine(MagickFormat.Jpeg, "size", $"{Width * 2}x{Height * 2}");
        return new MagickImage(path, settings);
    }

    private static MagickImage? ReadOra(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("mergedimage.png") ?? zip.GetEntry("Thumbnails/thumbnail.png");
        if (entry is null)
            return null;
        using var stream = entry.Open();
        return new MagickImage(stream);
    }
}
