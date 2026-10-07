using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>The look of an object, written out: what Copy Style takes and Paste Style applies.</summary>
/// <param name="Paint">Fill, stroke, opacity, line cap and join, dashes: for any object.</param>
/// <param name="Font">Font and alignment: applied to texts only (empty when the source is not a text).</param>
public sealed record CopiedStyle(IReadOnlyDictionary<string, string> Paint, IReadOnlyDictionary<string, string> Font);

// Copy the look of one object and paste it on others.
public sealed partial class SvgActions
{
    /// <summary>The look of <paramref name="element"/> as it is drawn (after inheritance and the cascade), as explicit properties.</summary>
    public static CopiedStyle CopyStyle(SvgElement element)
    {
        var s = StyleResolver.ComputeFor(element);
        string N(double v) => NumberFormat.Format(v, 4);
        var paint = new Dictionary<string, string>
        {
            ["fill"] = s.Fill.ToText(),
            ["fill-opacity"] = N(s.FillOpacity),
            ["fill-rule"] = s.FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero",
            ["stroke"] = s.Stroke.ToText(),
            ["stroke-opacity"] = N(s.StrokeOpacity),
            ["stroke-width"] = N(s.StrokeWidth),
            ["stroke-linecap"] = s.LineCap.ToString().ToLowerInvariant(),
            ["stroke-linejoin"] = s.LineJoin.ToString().ToLowerInvariant(),
            ["stroke-miterlimit"] = N(s.MiterLimit),
            ["stroke-dasharray"] = s.DashArray is { Length: > 0 } dashes ? string.Join(' ', dashes.Select(N)) : "none",
            ["stroke-dashoffset"] = N(s.DashOffset),
            ["opacity"] = N(s.Opacity),
        };
        var font = element is SvgTextBase
            ? new Dictionary<string, string>
            {
                ["font-family"] = s.FontFamily,
                ["font-size"] = N(s.FontSize),
                ["font-weight"] = s.FontWeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["font-style"] = s.Italic ? "italic" : "normal",
                ["text-anchor"] = s.TextAnchor switch { TextAnchor.Middle => "middle", TextAnchor.End => "end", _ => "start" },
            }
            : [];
        return new CopiedStyle(paint, font);
    }

    /// <summary>
    /// Gives the objects the copied look, as one step. A group passes it to every shape and text inside; fonts go to texts only.
    /// A gradient that was copied is shared by reference, so changing it changes every object that uses it.
    /// </summary>
    public void PasteStyle(IEnumerable<SvgElement>? nodes, CopiedStyle style)
    {
        var targets = TopLevel(nodes).SelectMany(Leaves).Distinct().ToList();
        if (targets.Count == 0)
            return;
        var tx = Begin("Paste Style");
        tx.Edit(targets, () =>
        {
            foreach (var node in targets)
            {
                foreach (var (property, value) in style.Paint)
                    node.Style.Set(property, value);
                if (node is SvgTextBase)
                    foreach (var (property, value) in style.Font)
                        node.Style.Set(property, value);
            }
        });
        tx.Commit();
    }

    // The shapes and texts under a node (the node itself when it is one).
    private static IEnumerable<SvgElement> Leaves(SvgElement node) =>
        node is SvgGroup group
            ? group.Descendants().OfType<SvgElement>().Where(e => e is SvgShape or SvgTextBase { Parent: not SvgTextBase })
            : node is SvgShape or SvgTextBase ? [node] : [];
}
