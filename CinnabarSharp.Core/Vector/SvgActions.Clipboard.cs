using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Vector;
using ImageMagick;

namespace CinnabarSharp.Core.Vector;

public sealed partial class SvgActions
{
    /// <summary>SVG text of the elements for the clipboard (see <see cref="SvgClipboard"/>); empty when there is nothing to copy.</summary>
    public string Copy(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = Targets(nodes);
        return targets.Count == 0 ? "" : SvgClipboard.Serialize(_document, targets);
    }

    /// <summary>Copies then deletes.</summary>
    public string Cut(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = Targets(nodes);
        var text = Copy(targets);
        if (text.Length > 0)
            Delete(targets);
        return text;
    }

    private static bool IsDefinition(SvgElement e) => e is SvgDefs or SvgGradient or SvgClipPath or SvgMask or SvgSymbol;

    private static bool IsContent(SvgElement e) => e is SvgShape or SvgGroup or SvgText or SvgImage or SvgUse or SvgRoot;

    /// <summary>
    /// Inserts the objects of an SVG document or fragment (from the clipboard) into the drawing's layer, with the definitions they
    /// use; ids that clash are renamed and references follow. One undo step; the pasted objects are selected.
    /// </summary>
    public IReadOnlyList<SvgElement> PasteSvg(string svgText, double dx = 0, double dy = 0)
    {
        SvgRoot pasted;
        try
        {
            pasted = SvgParser.Parse(svgText).Root;
        }
        catch (SvgParseException e)
        {
            throw new ArgumentException("The text is not an SVG drawing: " + e.Message, e);
        }

        var content = pasted.Elements.Where(IsContent).ToList();
        var definitions = pasted.Elements.SelectMany(e => e is SvgDefs d ? d.Elements : IsDefinition(e) ? [e] : []).ToList();
        if (content.Count == 0)
            return [];

        // Definitions that already exist unchanged are reused; others get a free id if theirs is taken.
        var rename = new Dictionary<string, string>();
        var toImport = new List<SvgElement>();
        foreach (var definition in definitions)
        {
            if (definition.Id is not { Length: > 0 } id)
                continue;
            if (Root.FindById(id) is { } existing)
            {
                if (SvgWriter.ToXNode(existing).ToString() == SvgWriter.ToXNode(definition).ToString())
                    continue;
                rename[id] = Root.NewId(BaseOfId(id));
            }
            toImport.Add(definition);
        }
        foreach (var (old, fresh) in rename)
            foreach (var element in content.Concat(toImport).SelectMany(e => e.SelfAndDescendants().OfType<SvgElement>()))
            {
                foreach (var attribute in element.Attributes.ToList())
                {
                    var rewritten = ReplaceReference(attribute.Value, old, fresh);
                    if (rewritten != attribute.Value)
                        element.SetAttribute(attribute.Name, rewritten);
                }
            }
        foreach (var (old, fresh) in rename)
            if (toImport.FirstOrDefault(d => d.Id == old) is { } definition)
                definition.Id = fresh;

        var tx = Begin("Paste");
        if (toImport.Count > 0)
        {
            var defs = Root.Elements.OfType<SvgDefs>().FirstOrDefault();
            if (defs is null)
            {
                defs = new SvgDefs();
                tx.Insert(Root, 0, defs);
            }
            foreach (var definition in toImport)
            {
                definition.Parent?.RemoveChild(definition);
                tx.Insert(defs, defs.Children.Count, definition);
            }
        }

        var layer = SvgDocumentFactory.DefaultParent(Root);
        var inserted = new List<SvgElement>();
        foreach (var element in content)
        {
            element.Parent?.RemoveChild(element);
            MakeIdsUnique(element);
            tx.Insert(layer, layer.Children.Count, element);
            inserted.Add(element);
        }
        if (dx != 0 || dy != 0)
            tx.Edit(inserted, () =>
            {
                foreach (var element in inserted)
                    SvgTransformer.Apply(element, ToParentSpace(element, Matrix2D.Translate(dx, dy)), Provider);
            });
        tx.Commit(inserted);
        return inserted;
    }

    /// <summary>The longest side a pasted or imported picture may have, as a fraction of the page.</summary>
    private const double PageFit = 1.0;

    /// <summary>
    /// Inserts a picture as an <c>&lt;image&gt;</c> with embedded data, at 96 dpi (<paramref name="pixelWidth"/> × <paramref name="pixelHeight"/>
    /// pixels, scaled down to fit the page), centered on the page.
    /// </summary>
    public SvgImage AddImage(byte[] data, string mimeType, int pixelWidth, int pixelHeight, string name = "Add Image")
    {
        var (pageWidth, pageHeight) = Root.UserSize;
        var (pixelsWidth, pixelsHeight) = Root.PixelSize;
        // Pixels of the picture are CSS pixels; the page's user unit may be bigger or smaller than that.
        var toUser = pixelsWidth > 0 ? pageWidth / pixelsWidth : 1;
        double width = pixelWidth * toUser, height = pixelHeight * toUser;
        var fit = Math.Min(1, Math.Min(pageWidth * PageFit / Math.Max(width, 1e-9), pageHeight * PageFit / Math.Max(height, 1e-9)));
        width *= fit;
        height *= fit;
        var image = new SvgImage { Id = Root.NewId("image") };
        image.X = Math.Round((pageWidth - width) / 2, 3) + (Root.ViewBox?.X ?? 0);
        image.Y = Math.Round((pageHeight - height) / 2, 3) + (Root.ViewBox?.Y ?? 0);
        image.Width = Math.Round(width, 3);
        image.Height = Math.Round(height, 3);
        image.SetAttribute("preserveAspectRatio", "none");
        image.Href = $"data:{mimeType};base64,{Convert.ToBase64String(data)}";
        return (SvgImage)AddNode(image, name: name);
    }

    /// <summary>Pastes a bitmap: encoded as PNG and embedded.</summary>
    public SvgImage PasteImage(ClipboardImage picture)
    {
        using var image = Utility.FromBgra(picture.Bgra, picture.Width, picture.Height);
        var png = image.ToByteArray(MagickFormat.Png);
        return AddImage(png, "image/png", picture.Width, picture.Height, "Paste Image");
    }
}
