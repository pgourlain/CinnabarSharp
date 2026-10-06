namespace CinnabarSharp.Vector;

public enum FillRule { NonZero, EvenOdd }

public enum LineCap { Butt, Round, Square }

public enum LineJoin { Miter, Round, Bevel }

public enum TextAnchor { Start, Middle, End }

/// <summary>The properties of an element after inheritance and the cascade, parsed.</summary>
public sealed record ComputedStyle
{
    public static ComputedStyle Initial { get; } = new();

    public SvgPaint Fill { get; init; } = SvgPaint.FromColor(VColor.Black);
    public double FillOpacity { get; init; } = 1;
    public FillRule FillRule { get; init; } = FillRule.NonZero;
    public FillRule ClipRule { get; init; } = FillRule.NonZero;
    public SvgPaint Stroke { get; init; } = SvgPaint.None;
    public double StrokeWidth { get; init; } = 1;
    public double StrokeOpacity { get; init; } = 1;
    public LineCap LineCap { get; init; } = LineCap.Butt;
    public LineJoin LineJoin { get; init; } = LineJoin.Miter;
    public double MiterLimit { get; init; } = 4;
    /// <summary>Dash lengths in user units, or null for a solid line.</summary>
    public double[]? DashArray { get; init; }
    public double DashOffset { get; init; }
    public bool Visible { get; init; } = true;
    public VColor Color { get; init; } = VColor.Black;
    public string FontFamily { get; init; } = "sans-serif";
    public double FontSize { get; init; } = 16;
    public int FontWeight { get; init; } = 400;
    public bool Italic { get; init; }
    public TextAnchor TextAnchor { get; init; } = TextAnchor.Start;

    // Not inherited.
    public double Opacity { get; init; } = 1;
    public bool DisplayNone { get; init; }
    public VColor StopColor { get; init; } = VColor.Black;
    public double StopOpacity { get; init; } = 1;
    public string? ClipPathId { get; init; }
    public string? MaskId { get; init; }
    public bool HasFilter { get; init; }
}

/// <summary>Computes <see cref="ComputedStyle"/> down the tree.</summary>
public static class StyleResolver
{
    /// <summary>The style of <paramref name="element"/> given its parent's computed style.</summary>
    public static ComputedStyle Compute(SvgElement element, ComputedStyle parent)
    {
        var style = element.Style;
        var result = parent with
        {
            // Properties that do not inherit start from their initial value.
            Opacity = 1,
            DisplayNone = false,
            StopColor = VColor.Black,
            StopOpacity = 1,
            ClipPathId = null,
            MaskId = null,
            HasFilter = false,
        };

        const string Inherit = "\u0001inherit";
        string? Value(string name) => style.TryGet(name, out var v, out _) ? (v == "inherit" ? Inherit : v) : null;

        if (Value("font-size") is { } fontSize)
            result = result with { FontSize = ParseFontSize(fontSize, parent.FontSize) };
        if (Value("color") is { } color && color != Inherit && ColorParser.TryParse(color, out var c))
            result = result with { Color = c };

        if (Value("fill") is { } fill && fill != Inherit && SvgPaint.TryParse(fill) is { } fillPaint)
            result = result with { Fill = fillPaint };
        if (Value("fill-opacity") is { } fo && fo != Inherit && TryOpacity(fo, out var fillOpacity))
            result = result with { FillOpacity = fillOpacity };
        if (Value("fill-rule") is { } fr && fr != Inherit)
            result = result with { FillRule = fr == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero };
        if (Value("clip-rule") is { } cr && cr != Inherit)
            result = result with { ClipRule = cr == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero };

        if (Value("stroke") is { } stroke && stroke != Inherit && SvgPaint.TryParse(stroke) is { } strokePaint)
            result = result with { Stroke = strokePaint };
        if (Value("stroke-width") is { } sw && sw != Inherit && SvgLength.TryParse(sw, out var width))
            result = result with { StrokeWidth = Math.Max(0, width.ToUser(DiagonalOf(element), result.FontSize)) };
        if (Value("stroke-opacity") is { } so && so != Inherit && TryOpacity(so, out var strokeOpacity))
            result = result with { StrokeOpacity = strokeOpacity };
        if (Value("stroke-linecap") is { } cap && cap != Inherit)
            result = result with { LineCap = cap switch { "round" => LineCap.Round, "square" => LineCap.Square, _ => LineCap.Butt } };
        if (Value("stroke-linejoin") is { } join && join != Inherit)
            result = result with { LineJoin = join switch { "round" => LineJoin.Round, "bevel" => LineJoin.Bevel, _ => LineJoin.Miter } };
        if (Value("stroke-miterlimit") is { } ml && ml != Inherit && NumberFormat.TryParse(ml, out var miter) && miter >= 1)
            result = result with { MiterLimit = miter };
        if (Value("stroke-dasharray") is { } da && da != Inherit)
            result = result with { DashArray = ParseDashArray(da, element, result.FontSize) };
        if (Value("stroke-dashoffset") is { } dof && dof != Inherit && SvgLength.TryParse(dof, out var offset))
            result = result with { DashOffset = offset.ToUser(DiagonalOf(element), result.FontSize) };

        if (Value("visibility") is { } vis && vis != Inherit)
            result = result with { Visible = vis == "visible" };

        if (Value("font-family") is { } family && family != Inherit)
            result = result with { FontFamily = family.Trim() };
        if (Value("font-weight") is { } weight && weight != Inherit)
            result = result with { FontWeight = ParseFontWeight(weight, parent.FontWeight) };
        if (Value("font-style") is { } fs && fs != Inherit)
            result = result with { Italic = fs is "italic" or "oblique" };
        if (Value("text-anchor") is { } ta && ta != Inherit)
            result = result with { TextAnchor = ta switch { "middle" => TextAnchor.Middle, "end" => TextAnchor.End, _ => TextAnchor.Start } };

        // Non-inherited properties: only an explicit "inherit" takes the parent's.
        if (Value("opacity") is { } op)
            result = result with { Opacity = op == Inherit ? parent.Opacity : TryOpacity(op, out var o) ? o : 1 };
        if (Value("display") is { } display)
            result = result with { DisplayNone = display == Inherit ? parent.DisplayNone : display == "none" };
        if (Value("stop-color") is { } sc)
            result = result with
            {
                StopColor = sc == Inherit ? parent.StopColor
                    : sc.Equals("currentColor", StringComparison.OrdinalIgnoreCase) ? result.Color
                    : ColorParser.TryParse(sc, out var stopColor) ? stopColor : VColor.Black,
            };
        if (Value("stop-opacity") is { } sop && TryOpacity(sop, out var stopOpacity))
            result = result with { StopOpacity = stopOpacity };
        if (Value("clip-path") is { } clip)
            result = result with { ClipPathId = clip == Inherit ? parent.ClipPathId : ReferencedId(clip) };
        if (Value("mask") is { } mask)
            result = result with { MaskId = mask == Inherit ? parent.MaskId : ReferencedId(mask) };
        if (Value("filter") is { } filter)
            result = result with { HasFilter = filter != "none" };

        return result;
    }

    /// <summary>The computed style of an element, from the root down (for tools that need it for one node).</summary>
    public static ComputedStyle ComputeFor(SvgElement element)
    {
        var chain = new Stack<SvgElement>();
        for (SvgElement? e = element; e is not null; e = e.Parent)
            chain.Push(e);
        var style = ComputedStyle.Initial;
        while (chain.Count > 0)
            style = Compute(chain.Pop(), style);
        return style;
    }

    /// <summary>Font size in user units (what 1em is), from the ancestors' <c>font-size</c>.</summary>
    internal static double FontSizeOf(SvgElement element)
    {
        var chain = new Stack<SvgElement>();
        for (SvgElement? e = element; e is not null; e = e.Parent)
            chain.Push(e);
        var size = 16.0;
        while (chain.Count > 0)
            if (chain.Pop().Style.Get("font-size") is { } text)
                size = ParseFontSize(text, size);
        return size;
    }

    private static double DiagonalOf(SvgElement element) => element.DocumentRoot?.PercentBase(LengthAxis.Diagonal) ?? 0;

    private static double ParseFontSize(string text, double parentSize)
    {
        switch (text)
        {
            case "medium": return 16;
            case "small": return 13;
            case "x-small": return 10;
            case "xx-small": return 9;
            case "large": return 18;
            case "x-large": return 24;
            case "xx-large": return 32;
            case "larger": return parentSize * 1.2;
            case "smaller": return parentSize / 1.2;
        }
        return SvgLength.TryParse(text, out var length) && length.Value >= 0
            ? length.ToUser(parentSize, parentSize)
            : parentSize;
    }

    private static int ParseFontWeight(string text, int parentWeight) => text switch
    {
        "normal" => 400,
        "bold" => 700,
        "bolder" => Math.Min(900, parentWeight + 300),
        "lighter" => Math.Max(100, parentWeight - 300),
        _ => int.TryParse(text, out var w) ? Math.Clamp(w, 1, 1000) : parentWeight,
    };

    private static bool TryOpacity(string text, out double value)
    {
        value = 1;
        text = text.Trim();
        if (text.EndsWith('%'))
        {
            if (!NumberFormat.TryParse(text[..^1], out var percent))
                return false;
            value = Math.Clamp(percent / 100, 0, 1);
            return true;
        }
        if (!NumberFormat.TryParse(text, out var v))
            return false;
        value = Math.Clamp(v, 0, 1);
        return true;
    }

    private static string? ReferencedId(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return null;
        var close = text.IndexOf(')');
        if (close < 0)
            return null;
        var reference = text[4..close].Trim().Trim('\'', '"');
        return reference.StartsWith('#') && reference.Length > 1 ? reference[1..] : null;
    }

    private static double[]? ParseDashArray(string text, SvgElement element, double fontSize)
    {
        if (text.Trim() == "none")
            return null;
        var values = new List<double>();
        foreach (var token in text.Split([',', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!SvgLength.TryParse(token, out var length) || length.Value < 0)
                return null;
            values.Add(length.ToUser(DiagonalOf(element), fontSize));
        }
        // All zero would never draw; browsers treat it as solid.
        return values.Count == 0 || values.Sum() <= 0 ? null : [.. values];
    }
}
