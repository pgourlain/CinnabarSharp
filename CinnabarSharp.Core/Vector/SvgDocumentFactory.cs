using System.Globalization;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

public enum SvgUnit
{
    Px,
    Mm,
    In,
}

/// <summary>Creates the tree of a new, empty drawing.</summary>
public static class SvgDocumentFactory
{
    public const string LayerId = "layer1";

    /// <summary>
    /// A drawing of <paramref name="width"/> × <paramref name="height"/> in the chosen unit: the size attributes carry the unit,
    /// the viewBox is in that unit (like Inkscape), and there is one empty layer group for the first objects.
    /// </summary>
    public static SvgRoot Create(double width, double height, SvgUnit unit = SvgUnit.Px, string layerName = "Layer 1")
    {
        width = Math.Clamp(width, 0.01, 100000);
        height = Math.Clamp(height, 0.01, 100000);
        var suffix = unit switch { SvgUnit.Mm => "mm", SvgUnit.In => "in", _ => "" };
        string N(double v) => NumberFormat.Format(v, 4);
        var inv = CultureInfo.InvariantCulture;
        var text = string.Create(inv, $"""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" width="{N(width)}{suffix}" height="{N(height)}{suffix}" viewBox="0 0 {N(width)} {N(height)}">
              <g id="{LayerId}" inkscape:groupmode="layer" inkscape:label="{System.Security.SecurityElement.Escape(layerName)}"/>
            </svg>
            """);
        return SvgParser.Parse(text).Root;
    }

    /// <summary>The first layer group (or the root when there is none): where new objects go by default.</summary>
    public static SvgContainer DefaultParent(SvgRoot root) =>
        root.Elements.OfType<SvgGroup>().LastOrDefault(g => g.IsLayer) is { } layer ? layer : root;
}
