using System.Xml.Linq;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>
/// Copy and paste of SVG objects as SVG text (<c>image/svg+xml</c>, readable by Inkscape, Figma and browsers). What an object
/// gets from its surroundings is baked in so it looks the same pasted anywhere: the transform of its ancestors, the style
/// it inherits and the CSS rules that apply to it. Gradients, clip paths and masks it uses travel with it in <c>defs</c>.
/// </summary>
public static class SvgClipboard
{
    private static readonly string[] InheritedProperties =
    [
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin",
        "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "color", "font-family", "font-size", "font-weight",
        "font-style", "text-anchor", "visibility", "clip-rule",
    ];

    /// <summary>The elements (in document order, without those inside another listed one) as a small SVG document.</summary>
    public static string Serialize(SvgDocument document, IEnumerable<SvgElement> nodes)
    {
        var root = document.Root;
        var set = nodes.Where(n => n.DocumentRoot == root && n.Parent is not null).ToHashSet();
        var order = new Dictionary<SvgNode, int>();
        foreach (var node in root.SelfAndDescendants())
            order[node] = order.Count;
        var top = set.Where(n => !n.Ancestors().Any(set.Contains)).OrderBy(n => order[n]).ToList();

        var output = SvgParser.Parse(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"{ViewBoxText(root)}\"/>").Root;
        var copies = new List<SvgElement>();
        foreach (var node in top)
        {
            var copy = (SvgElement)node.DeepClone();
            BakeContext(node, copy);
            output.AddChild(copy);
            copies.Add(copy);
        }

        // Definitions the copies use, with the ones those use, and so on.
        var defs = new SvgDefs();
        var included = new HashSet<string>();
        var queue = new Queue<SvgElement>(copies);
        while (queue.Count > 0)
        {
            var element = queue.Dequeue();
            foreach (var id in ReferencedIds(element))
            {
                if (!included.Add(id) || root.FindById(id) is not { } target || copies.Any(c => c.SelfAndDescendants().Contains(target)))
                    continue;
                var definition = (SvgElement)target.DeepClone();
                defs.AddChild(definition);
                queue.Enqueue(definition);
            }
        }
        if (defs.Children.Count > 0)
            output.InsertChild(0, defs);
        return SvgWriter.ToText(output);
    }

    private static string ViewBoxText(SvgRoot root)
    {
        var (w, h) = root.UserSize;
        return $"{NumberFormat.Format(root.ViewBox?.X ?? 0, 4)} {NumberFormat.Format(root.ViewBox?.Y ?? 0, 4)} {NumberFormat.Format(w, 4)} {NumberFormat.Format(h, 4)}";
    }

    private static void BakeContext(SvgElement original, SvgElement copy)
    {
        var world = original.Parent is { } parent ? SvgBounds.ToDocument(parent) : Matrix2D.Identity;
        copy.Transform = world * original.Transform;
        // Style first from the ancestors, then (below) the CSS rules, element by element.
        foreach (var property in InheritedProperties)
        {
            if (original.Style.TryGet(property, out _, out _))
                continue;
            for (var a = original.Parent; a is not null; a = a.Parent)
                if (a.Style.Get(property) is { } inherited)
                {
                    copy.Style.Set(property, inherited);
                    break;
                }
        }
        BakeRules(original, copy);
    }

    private static void BakeRules(SvgElement original, SvgElement copy)
    {
        foreach (var declaration in original.Style.Specified)
            if (declaration.Origin == StyleOrigin.Stylesheet)
                copy.Style.Set(declaration.Name, declaration.Value);
        var (a, b) = (original.Children.OfType<SvgElement>().ToList(), copy.Children.OfType<SvgElement>().ToList());
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++)
            BakeRules(a[i], b[i]);
    }

    /// <summary>Ids this element and its subtree refer to (<c>url(#id)</c>, <c>href="#id"</c>).</summary>
    public static IEnumerable<string> ReferencedIds(SvgElement element)
    {
        foreach (var e in element.SelfAndDescendants().OfType<SvgElement>())
            foreach (var attribute in e.Attributes)
            {
                var value = attribute.Value;
                if (attribute.Name.LocalName == "href" && value.StartsWith('#'))
                    yield return value[1..];
                var from = 0;
                while ((from = value.IndexOf("url(", from, StringComparison.Ordinal)) >= 0)
                {
                    var close = value.IndexOf(')', from);
                    if (close < 0)
                        break;
                    var reference = value[(from + 4)..close].Trim().Trim('\'', '"');
                    if (reference.StartsWith('#') && reference.Length > 1)
                        yield return reference[1..];
                    from = close;
                }
            }
    }

    /// <summary>True when the text looks like an SVG document or fragment worth trying to paste.</summary>
    public static bool LooksLikeSvg(string? text) => !string.IsNullOrWhiteSpace(text) && SvgFormat.LooksLikeSvg(text);
}
