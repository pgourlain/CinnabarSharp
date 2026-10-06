using System.IO.Compression;
using System.Text;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>
/// SVG files (<c>.svg</c>, and gzip-compressed <c>.svgz</c>) as vector documents: opened into an <see cref="SvgDocument"/>,
/// saved by writing the tree back. Raster formats are not what this class is for: it is the format of an SVG document,
/// the others are exports (<see cref="ImageFormat.ExportPixels"/>).
/// </summary>
public sealed class SvgFormat : ImageFormat
{
    private readonly IWorkspaceService _workspace;

    public SvgFormat(IWorkspaceService workspace) : base(nameof(SvgFormat), "SVG", ["svg", "svgz"])
    {
        _workspace = workspace;
    }

    public override DocumentKind DocumentKind => DocumentKind.Svg;

    /// <summary>Vector drawings keep their objects: the "flatten image" warning of raster formats does not apply.</summary>
    public override bool SupportsLayers => true;

    public override bool MatchesContent(ImageFile file)
    {
        try
        {
            using var stream = file.OpenRead();
            var head = new byte[64 * 1024];
            var read = stream.Read(head, 0, head.Length);
            if (read > 2 && head[0] == 0x1F && head[1] == 0x8B)
            {
                // Compressed: look at the start of the decompressed data.
                using var compressed = new MemoryStream(head, 0, read);
                using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                read = gzip.Read(head, 0, head.Length);
            }
            return LooksLikeSvg(Encoding.UTF8.GetString(head, 0, read));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when the text is XML whose first element is <c>svg</c> (after a declaration, comments and a DOCTYPE).</summary>
    public static bool LooksLikeSvg(string text)
    {
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿')
            i = 1;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            if (string.CompareOrdinal(text, i, "<?", 0, 2) == 0)
            {
                var end = text.IndexOf("?>", i, StringComparison.Ordinal);
                if (end < 0) return false;
                i = end + 2;
            }
            else if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
            {
                var end = text.IndexOf("-->", i, StringComparison.Ordinal);
                if (end < 0) return false;
                i = end + 3;
            }
            else if (string.CompareOrdinal(text, i, "<!DOCTYPE", 0, 9) == 0)
            {
                // The internal subset can contain '>' inside [...].
                var depth = 0;
                for (; i < text.Length; i++)
                {
                    if (text[i] == '[') depth++;
                    else if (text[i] == ']') depth--;
                    else if (text[i] == '>' && depth <= 0) { i++; break; }
                }
            }
            else
            {
                return string.CompareOrdinal(text, i, "<svg", 0, 4) == 0
                    && (i + 4 >= text.Length || char.IsWhiteSpace(text[i + 4]) || text[i + 4] is '>' or '/' or ':');
            }
        }
        return false;
    }

    public override void Import(ImageFile file) => Import(file, Decode(file));

    /// <summary>Reads and parses the file without touching any document, so it can run on a background thread.</summary>
    public override object? Decode(ImageFile file)
    {
        try
        {
            return SvgParser.ParseFile(file.FullName).Root;
        }
        catch (SvgParseException e)
        {
            // The same kind of error as an unreadable raster file: the caller shows its message.
            throw new NotSupportedException($"'{file.Name}' is not a valid SVG file: {e.Message}", e);
        }
    }

    public override void Import(ImageFile file, object? decoded)
    {
        var root = (SvgRoot)(decoded ?? Decode(file))!;
        var type = file.Extension.Equals(".svgz", StringComparison.OrdinalIgnoreCase) ? "svgz" : "svg";
        _workspace.OpenSvgDocument(root, file, type);
    }

    public override void Export(ImageDocument document, ImageFile file) =>
        throw new NotSupportedException("A raster image cannot be saved as an SVG drawing.");

    public override void ExportDocument(IDocument document, ImageFile file)
    {
        var svg = document as SvgDocument ?? throw new NotSupportedException("Only SVG documents can be saved as SVG.");
        var gzip = file.Extension.Equals(".svgz", StringComparison.OrdinalIgnoreCase);
        // Encode first, then write: a failure while encoding leaves the old file alone.
        var bytes = SvgWriter.ToBytes(svg.Root, gzip);
        var temp = file.FullName + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, file.FullName, overwrite: true);
    }
}

/// <summary>
/// The Magick.NET reading of an SVG as a raster image: File › Open as Image. (Layers › Import From File also goes
/// through Magick.NET, see <c>Utility.OpenVector</c>.)
/// </summary>
public sealed class SvgRasterFormat : MagickImageFormat
{
    public SvgRasterFormat(IWorkspaceService workspace)
        : base(nameof(SvgRasterFormat), "SVG (as image)", ["svg", "svgz"],
            [ImageMagick.MagickFormat.Svg, ImageMagick.MagickFormat.Svgz, ImageMagick.MagickFormat.Msvg], workspace, canSave: false)
    {
    }
}

/// <summary>How a drawing becomes a picture: scale, or a width and/or height in pixels, and what is behind it.</summary>
/// <param name="Scale">1 = the drawing's size at 96 dpi. Ignored when <see cref="Width"/> or <see cref="Height"/> is set.</param>
/// <param name="Width">Width in pixels; with no <see cref="Height"/> the height follows the ratio.</param>
/// <param name="Height">Height in pixels; with no <see cref="Width"/> the width follows the ratio.</param>
/// <param name="Background">Behind the drawing; null for transparent (JPEG flattens it on white anyway).</param>
public sealed record SvgExportOptions(double Scale = 1, int? Width = null, int? Height = null, VColor? Background = null)
{
    public static SvgExportOptions Default { get; } = new();

    /// <summary>The picture size and the scale that give it, for this drawing.</summary>
    public (int Width, int Height, double Scale) Resolve(SvgDocument document)
    {
        var (w, h) = document.Root.PixelSize;
        double scale;
        if (Width is { } width && width > 0)
            scale = width / w;
        else if (Height is { } height && height > 0)
            scale = height / h;
        else
            scale = Scale > 0 ? Scale : 1;
        var pixelWidth = Width is > 0 ? Width.Value : Math.Max(1, (int)Math.Ceiling(w * scale - 1e-9));
        var pixelHeight = Height is > 0 ? Height.Value : Math.Max(1, (int)Math.Ceiling(h * scale - 1e-9));
        // Both given and not matching the ratio: the drawing keeps its proportions, centered in the picture.
        return (Math.Clamp(pixelWidth, 1, 32768), Math.Clamp(pixelHeight, 1, 32768), scale);
    }

    /// <summary>Renders the drawing: straight-alpha BGRA of the resolved size.</summary>
    public (byte[] Bgra, int Width, int Height) Render(SvgDocument document, CancellationToken cancellation = default)
    {
        var (width, height, scale) = Resolve(document);
        var options = new RenderOptions
        {
            ImageDecoder = document.ImageDecoder,
            GlyphProvider = document.GlyphProvider,
            BaseFolder = document.File?.DirectoryName,
            Background = Background,
            Cancellation = cancellation,
        };
        return (VectorRasterizer.Render(document.Root, new VRectI(0, 0, width, height), scale, options), width, height);
    }
}
