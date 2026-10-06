using System.Xml.Linq;

namespace CinnabarSharp.Vector;

/// <summary>The <c>svg</c> element: the document root, or a nested one.</summary>
public sealed class SvgRoot : SvgContainer
{
    private Dictionary<string, SvgElement>? _index;
    private int _indexVersion = -1;
    private Dictionary<long, SvgNode>? _internalIndex;
    private int _internalIndexVersion = -1;
    private Stylesheet _stylesheet = Stylesheet.Empty;
    private int _stylesheetVersion = -1;

    public SvgRoot(XName? name = null) : base(name ?? Ns + "svg")
    {
    }

    /// <summary>Incremented on every change of the tree (attributes, structure); caches key on it.</summary>
    public int Version { get; private set; }

    public bool IsDocumentRoot => Parent is null;

    /// <summary>Problems found while reading (unsupported CSS, malformed values) that did not stop the parse.</summary>
    public List<string> Warnings { get; } = [];

    // ---- Document-level XML kept for the writer ----

    internal XDeclaration? Declaration { get; set; }
    internal List<XNode> LeadingNodes { get; } = [];
    internal List<XNode> TrailingNodes { get; } = [];

    /// <summary>The indentation of the file (two spaces, a tab…) or null when it was not indented; the writer indents the same way so new nodes look like the rest.</summary>
    internal string? IndentText { get; set; }

    internal void Touch()
    {
        Version++;
    }

    // ---- Geometry of the document ----

    public SvgLength? Width
    {
        get => GetLengthValue("width");
        set => SetAttribute("width", value?.ToText());
    }

    public SvgLength? Height
    {
        get => GetLengthValue("height");
        set => SetAttribute("height", value?.ToText());
    }

    public VRect? ViewBox
    {
        get => ViewBoxParser.TryParse(GetAttribute("viewBox"));
        set => SetAttribute("viewBox", value is { } box ? ViewBoxParser.ToText(box) : null);
    }

    public PreserveAspectRatio AspectRatio
    {
        get => PreserveAspectRatio.Parse(GetAttribute("preserveAspectRatio"));
        set => SetAttribute("preserveAspectRatio", value == PreserveAspectRatio.Default ? null : value.ToText());
    }

    /// <summary>Size of the user space: the viewBox, or the width and height when there is none (300 × 150 like browsers when neither).</summary>
    public (double Width, double Height) UserSize
    {
        get
        {
            if (ViewBox is { } box)
                return (box.Width, box.Height);
            var (w, h) = AbsoluteSize();
            return (w ?? 300, h ?? 150);
        }
    }

    /// <summary>Size of the picture in pixels at 96 dpi: <c>width</c> and <c>height</c> in px, or the viewBox size for a missing/relative one.</summary>
    public (double Width, double Height) PixelSize
    {
        get
        {
            var (w, h) = AbsoluteSize();
            if (ViewBox is { } box)
            {
                // One missing dimension follows the viewBox's ratio.
                if (w is null && h is null)
                    return (box.Width, box.Height);
                if (w is null)
                    return (h!.Value * box.Width / box.Height, h.Value);
                if (h is null)
                    return (w.Value, w.Value * box.Height / box.Width);
                return (w.Value, h.Value);
            }
            return (w ?? 300, h ?? 150);
        }
    }

    private (double? Width, double? Height) AbsoluteSize()
    {
        double? w = Width is { } wl && wl.IsAbsolute && wl.Value > 0 ? wl.ToUser() : null;
        double? h = Height is { } hl && hl.IsAbsolute && hl.Value > 0 ? hl.ToUser() : null;
        return (w, h);
    }

    /// <summary>What 100 % means on an axis: the user space's width, height, or their normalized diagonal.</summary>
    public double PercentBase(LengthAxis axis)
    {
        var (w, h) = UserSize;
        return axis switch
        {
            LengthAxis.X => w,
            LengthAxis.Y => h,
            _ => Math.Sqrt((w * w + h * h) / 2),
        };
    }

    /// <summary>Maps the user space onto the picture (viewBox with preserveAspectRatio into the pixel size).</summary>
    public Matrix2D UserToPixel
    {
        get
        {
            if (ViewBox is not { } box)
                return Matrix2D.Identity;
            var (w, h) = PixelSize;
            return AspectRatio.ViewBoxTransform(box, w, h);
        }
    }

    // ---- Ids and CSS ----

    /// <summary>The first element (in document order) with this id, like browsers resolve duplicates.</summary>
    public SvgElement? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        if (_index is null || _indexVersion != Version)
        {
            _index = new Dictionary<string, SvgElement>(StringComparer.Ordinal);
            foreach (var node in SelfAndDescendants())
                if (node is SvgElement { Id: { Length: > 0 } nodeId } element)
                    _index.TryAdd(nodeId, element);
            _indexVersion = Version;
        }
        return _index.GetValueOrDefault(id);
    }

    /// <summary>The node with this <see cref="SvgNode.InternalId"/> (the stable identity history steps use), or null when it is not in the tree.</summary>
    public SvgNode? FindByInternalId(long id)
    {
        if (_internalIndex is null || _internalIndexVersion != Version)
        {
            _internalIndex = [];
            foreach (var node in SelfAndDescendants())
                _internalIndex[node.InternalId] = node;
            _internalIndexVersion = Version;
        }
        return _internalIndex.GetValueOrDefault(id);
    }

    /// <summary>True when an element of the document already uses this id.</summary>
    public bool IdExists(string id) => FindById(id) is not null;

    /// <summary>A free id like "rect12": the prefix and the first number that is not used.</summary>
    public string NewId(string prefix)
    {
        for (var i = 1; ; i++)
        {
            var id = prefix + i;
            if (!IdExists(id))
                return id;
        }
    }

    /// <summary>The rules of the document's style elements (rebuilt after a change).</summary>
    public Stylesheet Stylesheet
    {
        get
        {
            if (_stylesheetVersion != Version)
            {
                // Rebuilding reads the style elements' text only; matching happens lazily on elements.
                _stylesheet = BuildStylesheet();
                _stylesheetVersion = Version;
            }
            return _stylesheet;
        }
    }

    internal Stylesheet BuildStylesheet()
    {
        var css = new System.Text.StringBuilder();
        foreach (var node in SelfAndDescendants())
        {
            if (node is not SvgRawElement { ElementName: "style" } style)
                continue;
            var type = style.GetAttribute("type");
            if (type is { Length: > 0 } && !type.Equals("text/css", StringComparison.OrdinalIgnoreCase))
                continue;
            css.Append(style.Source.Value).Append('\n');
        }
        if (css.Length == 0)
            return Stylesheet.Empty;
        var warnings = new List<string>();
        var sheet = Stylesheet.Parse(css.ToString(), warnings);
        foreach (var w in warnings)
            if (!Warnings.Contains(w))
                Warnings.Add(w);
        return sheet;
    }
}

public sealed class SvgGroup : SvgContainer
{
    public SvgGroup(XName? name = null) : base(name ?? Ns + "g")
    {
    }

    /// <summary>Inkscape marks layers with <c>inkscape:groupmode="layer"</c>.</summary>
    public bool IsLayer
    {
        get => GetAttribute(Inkscape + "groupmode") == "layer";
        set => SetAttribute(Inkscape + "groupmode", value ? "layer" : null);
    }
}

public sealed class SvgDefs : SvgContainer
{
    public SvgDefs(XName? name = null) : base(name ?? Ns + "defs")
    {
    }
}

public sealed class SvgSymbol : SvgContainer
{
    public SvgSymbol(XName? name = null) : base(name ?? Ns + "symbol")
    {
    }

    public VRect? ViewBox => ViewBoxParser.TryParse(GetAttribute("viewBox"));

    public PreserveAspectRatio AspectRatio => PreserveAspectRatio.Parse(GetAttribute("preserveAspectRatio"));
}

public sealed class SvgClipPath : SvgContainer
{
    public SvgClipPath(XName? name = null) : base(name ?? Ns + "clipPath")
    {
    }

    /// <summary>True for <c>clipPathUnits="objectBoundingBox"</c> (userSpaceOnUse is the default).</summary>
    public bool UsesBoundingBox => GetAttribute("clipPathUnits") == "objectBoundingBox";
}

public sealed class SvgMask : SvgContainer
{
    public SvgMask(XName? name = null) : base(name ?? Ns + "mask")
    {
    }

    /// <summary><c>maskUnits</c>: objectBoundingBox by default (for the x/y/width/height region).</summary>
    public bool RegionUsesBoundingBox => GetAttribute("maskUnits") != "userSpaceOnUse";

    public bool ContentUsesBoundingBox => GetAttribute("maskContentUnits") == "objectBoundingBox";
}

public sealed class SvgUse : SvgElement
{
    public SvgUse(XName? name = null) : base(name ?? Ns + "use")
    {
    }

    public double X { get => GetLength("x", 0, LengthAxis.X); set => SetNumber("x", value); }
    public double Y { get => GetLength("y", 0, LengthAxis.Y); set => SetNumber("y", value); }

    /// <summary>Width and height only matter for a <c>symbol</c> (or nested svg) target.</summary>
    public double? Width => HasAttribute("width") ? GetLength("width", 0, LengthAxis.X) : null;
    public double? Height => HasAttribute("height") ? GetLength("height", 0, LengthAxis.Y) : null;

    /// <summary>The referenced element, or null when the link is missing, external or broken.</summary>
    public SvgElement? Target => DocumentRoot?.FindById(HrefId);
}

public sealed class SvgImage : SvgElement
{
    public SvgImage(XName? name = null) : base(name ?? Ns + "image")
    {
    }

    public double X { get => GetLength("x", 0, LengthAxis.X); set => SetNumber("x", value); }
    public double Y { get => GetLength("y", 0, LengthAxis.Y); set => SetNumber("y", value); }
    public double Width { get => GetLength("width", 0, LengthAxis.X); set => SetNumber("width", value); }
    public double Height { get => GetLength("height", 0, LengthAxis.Y); set => SetNumber("height", value); }

    public PreserveAspectRatio AspectRatio
    {
        get => PreserveAspectRatio.Parse(GetAttribute("preserveAspectRatio"));
        set => SetAttribute("preserveAspectRatio", value == PreserveAspectRatio.Default ? null : value.ToText());
    }

    public VRect Bounds => new(X, Y, Width, Height);
}

/// <summary>A piece of text inside a text or tspan element.</summary>
public sealed class SvgTextRun : SvgNode
{
    private string? _originalText;

    public SvgTextRun(string text) => Text = text;

    public string Text { get; private set; }

    internal void FreezeOriginal() => _originalText = Text;

    public override bool IsDirty => _originalText is null || Text != _originalText;

    public void SetText(string text)
    {
        if (Text == text)
            return;
        Text = text;
        MarkChanged();
    }
}

/// <summary>Text elements keep their runs and tspans; whitespace between them is content.</summary>
public abstract class SvgTextBase : SvgContainer
{
    protected SvgTextBase(XName name) : base(name)
    {
    }

    /// <summary>Per-glyph positions (<c>x</c>, <c>y</c>, <c>dx</c>, <c>dy</c> lists).</summary>
    public double[] XList => GetNumberList("x");
    public double[] YList => GetNumberList("y");
    public double[] DxList => GetNumberList("dx");
    public double[] DyList => GetNumberList("dy");

    /// <summary>All the text of this element and its tspans, as written.</summary>
    public string RawText => string.Concat(Children.Select(c => c switch
    {
        SvgTextRun run => run.Text,
        SvgTextBase span => span.RawText,
        _ => "",
    }));

    /// <summary>xml:space="preserve" on this element or an ancestor.</summary>
    public bool PreserveSpace
    {
        get
        {
            for (SvgElement? e = this; e is not null; e = e.Parent)
                if (e.GetAttribute(Xml + "space") is { } space)
                    return space == "preserve";
            return false;
        }
    }
}

public sealed class SvgText : SvgTextBase
{
    public SvgText(XName? name = null) : base(name ?? Ns + "text")
    {
    }

    public double X
    {
        get => XList is { Length: > 0 } list ? list[0] : 0;
        set => SetNumberList("x", [value]);
    }

    public double Y
    {
        get => YList is { Length: > 0 } list ? list[0] : 0;
        set => SetNumberList("y", [value]);
    }

    /// <summary>The text with white space collapsed like browsers do (unless xml:space="preserve").</summary>
    public string Content => TextNormalizer.Normalize(RawText, PreserveSpace);

    /// <summary>Replaces the content with one text run (tspans are dropped).</summary>
    public void SetPlainText(string text)
    {
        foreach (var child in Children.ToList())
            RemoveChild(child);
        AddChild(new SvgTextRun(text));
    }
}

public sealed class SvgTextSpan : SvgTextBase
{
    public SvgTextSpan(XName? name = null) : base(name ?? Ns + "tspan")
    {
    }
}

public static class TextNormalizer
{
    /// <summary>
    /// Default xml:space handling: newlines and tabs become spaces, runs of spaces collapse to one, leading and
    /// trailing spaces go. With <paramref name="preserve"/>: newlines and tabs become spaces, nothing collapses.
    /// </summary>
    public static string Normalize(string text, bool preserve)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var lastSpace = true;
        foreach (var c in text)
        {
            var ch = c is '\n' or '\r' or '\t' ? ' ' : c;
            if (preserve)
            {
                sb.Append(ch);
                continue;
            }
            if (ch == ' ')
            {
                if (!lastSpace)
                    sb.Append(' ');
                lastSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastSpace = false;
            }
        }
        return preserve ? sb.ToString() : sb.ToString().TrimEnd(' ');
    }
}

public enum GradientUnits
{
    ObjectBoundingBox,
    UserSpaceOnUse,
}

public enum SpreadMethod
{
    Pad,
    Reflect,
    Repeat,
}

/// <summary>Linear and radial gradients: stops, units, transform, spread, and attributes inherited through <c>href</c>.</summary>
public abstract class SvgGradient : SvgContainer
{
    protected SvgGradient(XName name) : base(name)
    {
    }

    public IEnumerable<SvgStop> OwnStops => Children.OfType<SvgStop>();

    /// <summary>The element this gradient inherits from through <c>href</c>, or null (a cycle is cut).</summary>
    public SvgGradient? InheritsFrom => DocumentRoot?.FindById(HrefId) as SvgGradient;

    /// <summary>The gradient itself, then the ones it inherits from (each at most once).</summary>
    public IEnumerable<SvgGradient> InheritanceChain()
    {
        var seen = new HashSet<SvgGradient>();
        for (SvgGradient? g = this; g is not null && seen.Add(g); g = g.InheritsFrom)
            yield return g;
    }

    /// <summary>The value of an attribute here or in the first gradient of the chain that has it.</summary>
    public string? InheritedAttribute(XName name)
    {
        foreach (var g in InheritanceChain())
            if (g.GetAttribute(name) is { } value)
                return value;
        return null;
    }

    /// <summary>The stops of this gradient, or of the first one in the chain that has any.</summary>
    public IReadOnlyList<SvgStop> ResolvedStops()
    {
        foreach (var g in InheritanceChain())
        {
            var stops = g.OwnStops.ToList();
            if (stops.Count > 0)
                return stops;
        }
        return [];
    }

    public GradientUnits Units =>
        InheritedAttribute("gradientUnits") == "userSpaceOnUse" ? GradientUnits.UserSpaceOnUse : GradientUnits.ObjectBoundingBox;

    public Matrix2D GradientTransform => TransformParser.Parse(InheritedAttribute("gradientTransform"));

    public SpreadMethod Spread => InheritedAttribute("spreadMethod") switch
    {
        "reflect" => SpreadMethod.Reflect,
        "repeat" => SpreadMethod.Repeat,
        _ => SpreadMethod.Pad,
    };

    /// <summary>A coordinate attribute resolved through the chain: a percentage in bounding-box units is a fraction.</summary>
    protected double Coordinate(XName name, double fallback, LengthAxis axis)
    {
        if (!SvgLength.TryParse(InheritedAttribute(name), out var length))
            return fallback;
        if (Units == GradientUnits.ObjectBoundingBox)
            return length.Unit == LengthUnit.Percent ? length.Value / 100 : length.Value;
        return length.ToUser(DocumentRoot?.PercentBase(axis) ?? 0, 16);
    }
}

public sealed class SvgLinearGradient : SvgGradient
{
    public SvgLinearGradient(XName? name = null) : base(name ?? Ns + "linearGradient")
    {
    }

    public double X1 => Coordinate("x1", 0, LengthAxis.X);
    public double Y1 => Coordinate("y1", 0, LengthAxis.Y);
    public double X2 => Coordinate("x2", Units == GradientUnits.ObjectBoundingBox ? 1 : DocumentRoot?.PercentBase(LengthAxis.X) ?? 0, LengthAxis.X);
    public double Y2 => Coordinate("y2", 0, LengthAxis.Y);
}

public sealed class SvgRadialGradient : SvgGradient
{
    public SvgRadialGradient(XName? name = null) : base(name ?? Ns + "radialGradient")
    {
    }

    private double Half(LengthAxis axis) => Units == GradientUnits.ObjectBoundingBox ? 0.5 : (DocumentRoot?.PercentBase(axis) ?? 0) / 2;

    public double Cx => Coordinate("cx", Half(LengthAxis.X), LengthAxis.X);
    public double Cy => Coordinate("cy", Half(LengthAxis.Y), LengthAxis.Y);
    public double R => Coordinate("r", Half(LengthAxis.Diagonal), LengthAxis.Diagonal);
    public double Fx => Coordinate("fx", Cx, LengthAxis.X);
    public double Fy => Coordinate("fy", Cy, LengthAxis.Y);
}

public sealed class SvgStop : SvgElement
{
    public SvgStop(XName? name = null) : base(name ?? Ns + "stop")
    {
    }

    /// <summary>Position along the gradient, 0 to 1 (a percentage is accepted).</summary>
    public double Offset
    {
        get
        {
            if (!SvgLength.TryParse(GetAttribute("offset"), out var length))
                return 0;
            var value = length.Unit == LengthUnit.Percent ? length.Value / 100 : length.Value;
            return Math.Clamp(value, 0, 1);
        }
        set => SetNumber("offset", Math.Clamp(value, 0, 1));
    }

    /// <summary>The stop's color with its opacity applied.</summary>
    public VColor Color
    {
        get
        {
            var computed = StyleResolver.ComputeFor(this);
            return computed.StopColor.WithOpacity(computed.StopOpacity);
        }
    }

    public void SetColor(VColor color)
    {
        Style.Set("stop-color", SvgPaint.FromColor(color with { A = 255 }).ToText());
        Style.Set("stop-opacity", color.A == 255 ? null : NumberFormat.Format(color.A / 255.0, 4));
    }
}
