using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using ImageMagick;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services;

/// <summary>
/// OpenRaster (.ora): a zip with stack.xml, one PNG per layer, a merged image and a thumbnail.
/// Keeps layer names, visibility, opacity and blend modes. Spec: https://www.openraster.org/
/// </summary>
public class OraFormat : ImageFormat
{
    private const string MimeType = "image/openraster";
    private const int ThumbnailMaxSize = 256;

    // Blend modes without an SVG equivalent use a private prefix; other apps fall back to normal.
    private static readonly (BlendMode Mode, string Op)[] CompositeOps =
    [
        (BlendMode.Normal, "svg:src-over"),
        (BlendMode.Multiply, "svg:multiply"),
        (BlendMode.Additive, "svg:plus"),
        (BlendMode.ColorBurn, "svg:color-burn"),
        (BlendMode.ColorDodge, "svg:color-dodge"),
        (BlendMode.Overlay, "svg:overlay"),
        (BlendMode.Difference, "svg:difference"),
        (BlendMode.Lighten, "svg:lighten"),
        (BlendMode.Darken, "svg:darken"),
        (BlendMode.Screen, "svg:screen"),
        (BlendMode.HardLight, "svg:hard-light"),
        (BlendMode.SoftLight, "svg:soft-light"),
        (BlendMode.Reflect, "pdn:reflect"),
        (BlendMode.Glow, "pdn:glow"),
        (BlendMode.Negation, "pdn:negation"),
        (BlendMode.Xor, "pdn:xor"),
    ];

    private readonly IWorkspaceService _workspaceService;

    public OraFormat(IWorkspaceService workspaceService)
        : base(nameof(OraFormat), "OpenRaster", ["ora"])
    {
        _workspaceService = workspaceService;
    }

    public override bool SupportsLayers => true;

    public override bool MatchesContent(ImageFile file)
    {
        try
        {
            using var zip = ZipFile.OpenRead(file.FullName);
            using var reader = new StreamReader(zip.GetEntry("mimetype")?.Open() ?? Stream.Null);
            return reader.ReadToEnd().Trim() == MimeType;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            return false;
        }
    }

    public override void Import(ImageFile file)
    {
        using var zip = ZipFile.OpenRead(file.FullName);
        var stackEntry = zip.GetEntry("stack.xml") ?? throw new InvalidDataException("stack.xml is missing.");
        XDocument stack;
        using (var s = stackEntry.Open())
            stack = XDocument.Load(s);

        var image = stack.Root ?? throw new InvalidDataException("stack.xml is empty.");
        var size = new ImageSize(IntAttr(image, "w"), IntAttr(image, "h"));
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidDataException("Invalid image size in stack.xml.");

        var doc = _workspaceService.CreateAndActivateDocument(file, SupportedExtensions[0], size);
        doc.Workspace.ViewSize = size;

        // stack.xml lists the top layer first.
        foreach (var element in image.Descendants("layer").Reverse())
        {
            var src = (string?)element.Attribute("src") ?? throw new InvalidDataException("Layer without src.");
            var entry = zip.GetEntry(src) ?? throw new InvalidDataException($"Missing layer image '{src}'.");

            var canvas = Utility.CreateImage(size.Width, size.Height);
            using (var s = entry.Open())
            using (var layerImage = new MagickImage(s))
                canvas.Composite(layerImage, IntAttr(element, "x"), IntAttr(element, "y"), CompositeOperator.Copy);

            var layer = doc.Layers.CreateLayer((string?)element.Attribute("name") ?? "");
            layer.Surface.Dispose();
            layer.Surface = canvas;
            layer.Hidden = (string?)element.Attribute("visibility") == "hidden";
            layer.Opacity = Math.Clamp(DoubleAttr(element, "opacity", 1), 0, 1);
            layer.BlendMode = FromCompositeOp((string?)element.Attribute("composite-op"));
            doc.Layers.Insert(layer, doc.Layers.Count());
        }

        if (doc.Layers.Count() == 0)
            doc.Layers.AddNewLayer(string.Empty);
        doc.Layers.SetCurrentUserLayer(doc.Layers.Count() - 1);
        doc.Workspace.Invalidate();
    }

    public override void Export(ImageDocument document, ImageFile file)
    {
        var size = document.ImageSize;
        var temp = file.FullName + ".tmp";
        using (var stream = File.Create(temp))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            // The mimetype must be the first entry, stored uncompressed.
            var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var w = new StreamWriter(mime.Open()))
                w.Write(MimeType);

            var stack = new XElement("stack");
            var layers = document.Layers.UserLayers;
            for (var i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                var src = $"data/layer{i}.png";
                WritePng(zip, src, layer.Surface);
                stack.Add(new XElement("layer",
                    new XAttribute("name", layer.Name),
                    new XAttribute("src", src),
                    new XAttribute("x", 0),
                    new XAttribute("y", 0),
                    new XAttribute("opacity", layer.Opacity.ToString("0.###", CultureInfo.InvariantCulture)),
                    new XAttribute("visibility", layer.Hidden ? "hidden" : "visible"),
                    new XAttribute("composite-op", ToCompositeOp(layer.BlendMode))));
            }

            var xml = new XDocument(new XElement("image",
                new XAttribute("version", "0.0.3"),
                new XAttribute("w", size.Width),
                new XAttribute("h", size.Height),
                stack));
            using (var s = zip.CreateEntry("stack.xml").Open())
                xml.Save(s);

            using var merged = document.GetFlattenedImage();
            WritePng(zip, "mergedimage.png", merged);
            using var thumbnail = merged.Clone();
            thumbnail.Resize(new MagickGeometry(ThumbnailMaxSize, ThumbnailMaxSize));
            WritePng(zip, "Thumbnails/thumbnail.png", thumbnail);
        }
        File.Move(temp, file.FullName, overwrite: true);
    }

    public static string ToCompositeOp(BlendMode mode) =>
        CompositeOps.First(c => c.Mode == mode).Op;

    public static BlendMode FromCompositeOp(string? op) =>
        CompositeOps.FirstOrDefault(c => c.Op == op, CompositeOps[0]).Mode;

    private static void WritePng(ZipArchive zip, string path, IMagickImage<byte> image)
    {
        using var s = zip.CreateEntry(path).Open();
        image.Write(s, MagickFormat.Png32);
    }

    private static int IntAttr(XElement e, string name) =>
        int.TryParse((string?)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double DoubleAttr(XElement e, string name, double fallback) =>
        double.TryParse((string?)e.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
