using ImageMagick;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Photo;

public enum TvResolution
{
    /// <summary>1920 × 1080.</summary>
    FullHd,

    /// <summary>3840 × 2160.</summary>
    Uhd4K,

    /// <summary>7680 × 4320.</summary>
    Uhd8K,
}

public enum TvFit
{
    /// <summary>Fill the screen, cutting what doesn't fit (the crop frame or selection chooses what is kept).</summary>
    CropToFill,

    /// <summary>Show the whole photo; the rest of the screen is the background.</summary>
    FitWithBorders,

    /// <summary>Fill the screen by distorting the photo.</summary>
    Stretch,
}

public enum TvBackground
{
    Black,
    White,

    /// <summary>A blurred, darkened copy of the photo fills the borders (good for portrait photos).</summary>
    Blurred,
}

public sealed record TvOptions(TvResolution Resolution, TvFit Fit, TvBackground Background = TvBackground.Black);

/// <summary>Straight-alpha BGRA pixels with their size.</summary>
public sealed record BgraImage(byte[] Pixels, int Width, int Height);

/// <summary>
/// Makes photos fit a 16:9 TV (2K, 4K or 8K): crop to fill, fit with borders (plain or blurred) or stretch, two
/// portraits side by side, and a batch mode for a whole folder. Resampling uses Magick.NET's Lanczos filter.
/// </summary>
public static class TvExport
{
    public static ImageSize SizeOf(TvResolution resolution) => resolution switch
    {
        TvResolution.FullHd => new ImageSize(1920, 1080),
        TvResolution.Uhd8K => new ImageSize(7680, 4320),
        _ => new ImageSize(3840, 2160),
    };

    /// <summary>File name suffix: "_2K", "_4K", "_8K".</summary>
    public static string Suffix(TvResolution resolution) => resolution switch
    {
        TvResolution.FullHd => "_2K",
        TvResolution.Uhd8K => "_8K",
        _ => "_4K",
    };

    /// <summary>The largest rectangle of the given ratio centered in a w×h image.</summary>
    public static RectangleI CenteredCrop(int width, int height, double ratio)
    {
        var (w, h) = (double)width / height > ratio ? ((int)Math.Round(height * ratio), height) : (width, (int)Math.Round(width / ratio));
        return new RectangleI((width - w) / 2, (height - h) / 2, Math.Max(1, w), Math.Max(1, h));
    }

    /// <summary>
    /// How much the photo is enlarged to fill the target (above 1 means upscaling: fewer pixels than the TV has).
    /// </summary>
    public static double UpscaleFactor(int width, int height, TvOptions options, RectangleI? crop = null)
    {
        var target = SizeOf(options.Resolution);
        return options.Fit switch
        {
            TvFit.CropToFill => (double)target.Width / Area(width, height, target, crop).Width,
            TvFit.FitWithBorders => Math.Min((double)target.Width / width, (double)target.Height / height),
            _ => Math.Max((double)target.Width / width, (double)target.Height / height),
        };
    }

    /// <param name="size">Size of the result; the TV's resolution by default. A smaller 16:9 size gives the same
    /// picture at a lower resolution (a preview).</param>
    public static BgraImage Compose(BgraImage source, TvOptions options, RectangleI? crop = null, ImageSize? size = null)
    {
        var target = size ?? SizeOf(options.Resolution);
        return Fill(source, target.Width, target.Height, options, crop);
    }

    /// <summary>Two photos (typically portraits) side by side, each filling half of the screen.</summary>
    public static BgraImage SideBySide(BgraImage left, BgraImage right, TvOptions options, ImageSize? size = null)
    {
        var target = size ?? SizeOf(options.Resolution);
        var half = target.Width / 2;
        var halfOptions = options with { Fit = options.Fit == TvFit.Stretch ? TvFit.Stretch : TvFit.CropToFill };
        var a = Fill(left, half, target.Height, halfOptions, null);
        var b = Fill(right, target.Width - half, target.Height, halfOptions, null);
        var result = new byte[target.Width * target.Height * 4];
        PixelRegion.Place(result, target.Width, target.Height, a.Pixels, a.Width, a.Height, 0, 0, composite: false);
        PixelRegion.Place(result, target.Width, target.Height, b.Pixels, b.Width, b.Height, half, 0, composite: false);
        return new BgraImage(result, target.Width, target.Height);
    }

    private static BgraImage Fill(BgraImage source, int tw, int th, TvOptions options, RectangleI? crop)
    {
        switch (options.Fit)
        {
            case TvFit.Stretch:
                return new BgraImage(Resize(source, tw, th), tw, th);
            case TvFit.CropToFill:
            {
                var area = Area(source.Width, source.Height, new ImageSize(tw, th), crop);
                var cropped = new BgraImage(PixelRegion.Extract(source.Pixels, source.Width, area), area.Width, area.Height);
                return Opaque(new BgraImage(Resize(cropped, tw, th), tw, th), options.Background);
            }
            default:
            {
                var scale = Math.Min((double)tw / source.Width, (double)th / source.Height);
                var (w, h) = (Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)));
                var background = Background(source, tw, th, options.Background);
                var photo = Resize(source, w, h);
                PixelRegion.Place(background, tw, th, photo, w, h, (tw - w) / 2, (th - h) / 2, composite: true);
                return new BgraImage(background, tw, th);
            }
        }
    }

    // The part of the image that is kept when cropping to fill: the given frame adjusted to the target ratio, or centered.
    private static RectangleI Area(int width, int height, ImageSize target, RectangleI? crop)
    {
        var ratio = (double)target.Width / target.Height;
        if (crop is not { } c || c.Width <= 0 || c.Height <= 0)
            return CenteredCrop(width, height, ratio);
        // Keep the part of the frame that is on the photo (the frame can be larger than the photo), then the largest
        // area of the TV's ratio centered in it, so the result is never stretched.
        var (x0, y0) = (Math.Clamp(c.X, 0, width), Math.Clamp(c.Y, 0, height));
        var (x1, y1) = (Math.Clamp(c.X + c.Width, 0, width), Math.Clamp(c.Y + c.Height, 0, height));
        if (x1 - x0 < 1 || y1 - y0 < 1)
            return CenteredCrop(width, height, ratio);
        var inner = CenteredCrop(x1 - x0, y1 - y0, ratio);
        return new RectangleI(x0 + inner.X, y0 + inner.Y, inner.Width, inner.Height);
    }

    private static byte[] Background(BgraImage source, int tw, int th, TvBackground background)
    {
        if (background != TvBackground.Blurred)
        {
            var value = background == TvBackground.White ? (byte)255 : (byte)0;
            var plain = new byte[tw * th * 4];
            for (var i = 0; i < plain.Length; i += 4)
                (plain[i], plain[i + 1], plain[i + 2], plain[i + 3]) = (value, value, value, 255);
            return plain;
        }
        // Cover the screen with the photo at low resolution, blur it heavily, darken it, then scale it up.
        var small = Fill(source, Math.Max(1, tw / 16), Math.Max(1, th / 16),
            new TvOptions(TvResolution.FullHd, TvFit.CropToFill), null);
        var blurred = new byte[small.Pixels.Length];
        var ctx = new EffectContext(small.Pixels, small.Width, small.Height, ColorBgra.Black, ColorBgra.White);
        GaussianBlurEffect.Blur(ctx, new RectangleI(0, 0, small.Width, small.Height), blurred, 6, CancellationToken.None);
        for (var i = 0; i < blurred.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                blurred[i + c] = (byte)(blurred[i + c] * 7 / 10);
            blurred[i + 3] = 255;
        }
        return Resize(new BgraImage(blurred, small.Width, small.Height), tw, th);
    }

    // TVs show no transparency: put transparent parts on the background color.
    private static BgraImage Opaque(BgraImage image, TvBackground background)
    {
        var value = background == TvBackground.White ? 255 : 0;
        var px = image.Pixels;
        for (var i = 0; i < px.Length; i += 4)
        {
            var a = px[i + 3];
            if (a == 255)
                continue;
            for (var c = 0; c < 3; c++)
                px[i + c] = (byte)((px[i + c] * a + value * (255 - a)) / 255);
            px[i + 3] = 255;
        }
        return image;
    }

    private static byte[] Resize(BgraImage source, int width, int height)
    {
        if (source.Width == width && source.Height == height)
            return (byte[])source.Pixels.Clone();
        using var image = Utility.FromBgra(source.Pixels, source.Width, source.Height);
        image.FilterType = FilterType.Lanczos;
        image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
        return image.ToBgra();
    }

    /// <summary>Reads a photo like File › Open does: EXIF orientation applied, converted to sRGB.</summary>
    public static BgraImage Load(FileInfo file)
    {
        using var image = Utility.OpenImage(file);
        image.AutoOrient();
        if (image.GetColorProfile() is not null)
            image.TransformColorSpace(ColorProfiles.SRGB);
        return new BgraImage(image.ToBgra(), (int)image.Width, (int)image.Height);
    }

    /// <summary>Writes a JPEG with the given quality and an sRGB profile.</summary>
    public static void SaveJpeg(BgraImage image, FileInfo file, int quality)
    {
        using var magick = Utility.FromBgra(image.Pixels, image.Width, image.Height);
        magick.BackgroundColor = MagickColors.Black;
        magick.Alpha(AlphaOption.Remove);
        magick.Quality = (uint)Math.Clamp(quality, 1, 100);
        magick.SetProfile(ColorProfiles.SRGB);
        magick.Format = MagickFormat.Jpeg;
        magick.Write(file);
    }

    /// <summary>
    /// Prepares every image of <paramref name="folder"/> (not its subfolders) for the TV and saves each as
    /// "name_4K.jpg" in a "TV 4K" subfolder. Files that can't be read are skipped. Returns the output folder and
    /// the number of photos written.
    /// </summary>
    public static (DirectoryInfo Output, int Count) ExportFolder(DirectoryInfo folder, TvOptions options, int quality,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellation = default)
    {
        var output = new DirectoryInfo(Path.Combine(folder.FullName, "TV" + Suffix(options.Resolution).Replace('_', ' ')));
        var files = folder.GetFiles().Where(f => !f.Name.StartsWith('.')).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var count = 0;
        for (var i = 0; i < files.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            BgraImage source;
            try
            {
                source = Load(files[i]);
            }
            catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
            {
                progress?.Report((i + 1, files.Count));
                continue;
            }
            output.Create();
            var name = Path.GetFileNameWithoutExtension(files[i].Name) + Suffix(options.Resolution) + ".jpg";
            SaveJpeg(Compose(source, options), new FileInfo(Path.Combine(output.FullName, name)), quality);
            count++;
            progress?.Report((i + 1, files.Count));
        }
        return (output, count);
    }
}
