using System.Xml.Linq;

namespace CinnabarSharp.Vector;

/// <summary>Builds typed nodes from XML (parser and cloning).</summary>
internal static class SvgNodeFactory
{
    private static bool IsSvgName(XName name) => name.Namespace == SvgElement.Ns || name.Namespace == XNamespace.None;

    /// <summary>The typed node for an element (without children); unknown elements become <see cref="SvgRawElement"/>.</summary>
    public static SvgElement CreateElement(XElement xe)
    {
        if (!IsSvgName(xe.Name))
            return new SvgRawElement(xe);

        SvgElement element = xe.Name.LocalName switch
        {
            "svg" => new SvgRoot(xe.Name),
            "g" or "a" => new SvgGroup(xe.Name),
            "defs" => new SvgDefs(xe.Name),
            "symbol" => new SvgSymbol(xe.Name),
            "path" => new SvgPath(xe.Name),
            "rect" => new SvgRect(xe.Name),
            "circle" => new SvgCircle(xe.Name),
            "ellipse" => new SvgEllipse(xe.Name),
            "line" => new SvgLine(xe.Name),
            "polyline" => new SvgPolyline(xe.Name),
            "polygon" => new SvgPolygon(xe.Name),
            "text" => new SvgText(xe.Name),
            "tspan" => new SvgTextSpan(xe.Name),
            "image" => new SvgImage(xe.Name),
            "use" => new SvgUse(xe.Name),
            "linearGradient" => new SvgLinearGradient(xe.Name),
            "radialGradient" => new SvgRadialGradient(xe.Name),
            "stop" => new SvgStop(xe.Name),
            "clipPath" => new SvgClipPath(xe.Name),
            "mask" => new SvgMask(xe.Name),
            _ => new SvgRawElement(xe),
        };
        if (element is SvgRawElement)
            return element;
        element.SourceNode = xe;
        foreach (var attribute in xe.Attributes())
            element.AddParsedAttribute(attribute.Name, attribute.Value);
        return element;
    }

    /// <summary>Builds the node (and its subtree) for an XML node; null for whitespace that is not content.</summary>
    public static SvgNode? Build(XNode xml, bool inText)
    {
        switch (xml)
        {
            case XElement xe:
            {
                var element = CreateElement(xe);
                if (element is SvgContainer container)
                    BuildChildren(xe, container, element is SvgTextBase);
                return element;
            }
            case XText text when inText:
                return new SvgTextRun(text.Value) { SourceNode = text };
            case XText text when text is not XCData && string.IsNullOrWhiteSpace(text.Value):
                return null;
            default:
                return new SvgRawContent(xml);
        }
    }

    private static void BuildChildren(XElement xe, SvgContainer container, bool inText)
    {
        foreach (var child in xe.Nodes())
            if (Build(child, inText) is { } node)
                container.AddParsedChild(node);
    }

    public static SvgNode Clone(SvgNode original, bool keepIds)
    {
        var xml = SvgWriter.ToXNode(original);
        var copy = Build(xml, original is SvgTextRun or SvgTextBase || original.Parent is SvgTextBase)
            ?? throw new InvalidOperationException("The node could not be copied.");
        CopyState(original, copy, keepIds);
        return copy;
    }

    private static void CopyState(SvgNode original, SvgNode copy, bool keepIds)
    {
        if (keepIds)
            copy.InternalId = original.InternalId;
        copy.IsDirty = original.IsDirty;
        copy.SubtreeDirty = original.SubtreeDirty;
        var (a, b) = (original.Children, copy.Children);
        if (a.Count != b.Count)
            return;
        for (var i = 0; i < a.Count; i++)
            CopyState(a[i], b[i], keepIds);
    }
}
