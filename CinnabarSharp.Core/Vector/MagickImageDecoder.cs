using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Vector;
using ImageMagick;

namespace CinnabarSharp.Core.Vector;

/// <summary>
/// Decodes the pictures of <c>&lt;image&gt;</c> elements with Magick.NET: PNG, JPEG, WebP and the first frame of a GIF.
/// The format is read from the first bytes and forced, so a crafted file cannot make Magick.NET use another coder
/// (an SVG, MVG or MSL script).
/// </summary>
public sealed class MagickImageDecoder : IImageDecoder
{
    public DecodedImage? Decode(ReadOnlySpan<byte> data)
    {
        var format = Sniff(data);
        if (format is null)
            return null;
        try
        {
            var settings = new MagickReadSettings { Format = format.Value, FrameIndex = 0, FrameCount = 1 };
            using var image = new MagickImage(data, settings);
            if (image.Width == 0 || image.Height == 0 || image.Width > 16384 || image.Height > 16384)
                return null;
            image.AutoOrient();
            var width = (int)image.Width;
            var height = (int)image.Height;
            return new DecodedImage(image.ToBgra(), width, height);
        }
        catch (MagickException)
        {
            return null;
        }
    }

    private static MagickFormat? Sniff(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47)
            return MagickFormat.Png;
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
            return MagickFormat.Jpeg;
        if (d.Length >= 6 && d[0] == 'G' && d[1] == 'I' && d[2] == 'F' && d[3] == '8')
            return MagickFormat.Gif;
        if (d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P')
            return MagickFormat.WebP;
        return null;
    }
}
