using System.Xml.Linq;

namespace CinnabarSharp.Vector;

/// <summary>An attribute as written in the file: qualified name and text.</summary>
public readonly record struct SvgAttribute(XName Name, string Value);

/// <summary>
/// A node of the SVG tree. The tree keeps everything the file had: typed elements for what the engine understands
/// and verbatim copies (<see cref="SvgRawElement"/>, <see cref="SvgRawContent"/>) for the rest, in their original place.
/// </summary>
public abstract class SvgNode
{
    private static long s_nextId;

    /// <summary>Stable identity for the life of the node (and its clones, when history snapshots keep ids). Not the XML id.</summary>
    public long InternalId { get; internal set; } = Interlocked.Increment(ref s_nextId);

    public SvgContainer? Parent { get; internal set; }

    /// <summary>The XML this node was read from; untouched nodes are written back from it.</summary>
    internal XNode? SourceNode { get; set; }

    /// <summary>
    /// True when this node's own XML differs from what was read: attributes or children changed, or the node is new. Computed by
    /// comparison, so a change that was undone leaves the node clean again, and the writer then writes its original XML.
    /// </summary>
    public abstract bool IsDirty { get; }

    /// <summary>True when a descendant is dirty.</summary>
    public bool SubtreeDirty => Children.Any(c => c.IsDirty || c.SubtreeDirty);

    public virtual IReadOnlyList<SvgNode> Children => [];

    /// <summary>The topmost <c>svg</c> element above (or at) this node, or null for a node outside a document.</summary>
    public SvgRoot? DocumentRoot
    {
        get
        {
            SvgNode node = this;
            while (node.Parent is { } parent)
                node = parent;
            return node as SvgRoot;
        }
    }

    public IEnumerable<SvgNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var deeper in child.Descendants())
                yield return deeper;
        }
    }

    public IEnumerable<SvgNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var d in Descendants())
            yield return d;
    }

    public IEnumerable<SvgNode> Ancestors()
    {
        for (var p = Parent; p is not null; p = p.Parent)
            yield return p;
    }

    /// <summary>Tells the document something in the tree changed (caches such as the id index and the stylesheet are rebuilt).</summary>
    public void MarkChanged() => DocumentRoot?.Touch();

    /// <summary>
    /// A deep copy. With <paramref name="keepIds"/> the copy has the same <see cref="InternalId"/>s (history snapshots find
    /// their node again); without it, every node gets a new id (duplicate, paste).
    /// </summary>
    public SvgNode DeepClone(bool keepIds = false) => SvgNodeFactory.Clone(this, keepIds);
}

/// <summary>Comments, processing instructions, CDATA and text that are not part of a modeled element, kept in place.</summary>
public sealed class SvgRawContent : SvgNode
{
    public SvgRawContent(XNode content)
    {
        SourceNode = content;
        Content = content;
    }

    public XNode Content { get; }

    public bool IsComment => Content is XComment;

    public override bool IsDirty => false;
}

/// <summary>An element with attributes: a shape, a group, a gradient, or a raw copy of something unknown.</summary>
public abstract class SvgElement : SvgNode
{
    private readonly List<SvgAttribute> _attributes = [];
    private SvgStyle? _style;
    private string? _transformText;
    private Matrix2D _transform = Matrix2D.Identity;

    public static readonly XNamespace Ns = "http://www.w3.org/2000/svg";
    public static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    public static readonly XNamespace Xml = XNamespace.Xml;
    public static readonly XNamespace Inkscape = "http://www.inkscape.org/namespaces/inkscape";
    public static readonly XNamespace Sodipodi = "http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd";

    private SvgAttribute[]? _originalAttributes;

    protected SvgElement(XName name) => XmlName = name;

    /// <summary>Remembers the attributes as read, so <see cref="IsDirty"/> can tell whether they changed.</summary>
    internal void FreezeOriginal() => _originalAttributes = [.. _attributes];

    public override bool IsDirty => SourceNode is null || _originalAttributes is null || !_attributes.SequenceEqual(_originalAttributes);

    /// <summary>Qualified name of the element (<c>{http://www.w3.org/2000/svg}rect</c>).</summary>
    public XName XmlName { get; }

    /// <summary>Local name: "rect", "g", "path"…</summary>
    public string ElementName => XmlName.LocalName;

    public IReadOnlyList<SvgAttribute> Attributes => _attributes;

    /// <summary>The style of this element alone (inline, stylesheet and presentation attributes); see <see cref="StyleResolver"/> for inheritance.</summary>
    public SvgStyle Style => _style ??= new SvgStyle(this);

    // ---- Attributes ----

    public string? GetAttribute(XName name)
    {
        foreach (var a in _attributes)
            if (a.Name == name)
                return a.Value;
        return null;
    }

    public bool HasAttribute(XName name) => GetAttribute(name) is not null;

    /// <summary>Sets an attribute (null removes it) and marks the node changed. Keeps the attribute's place when it exists.</summary>
    public void SetAttribute(XName name, string? value)
    {
        var index = _attributes.FindIndex(a => a.Name == name);
        if (value is null)
        {
            if (index < 0)
                return;
            _attributes.RemoveAt(index);
        }
        else if (index >= 0)
        {
            if (_attributes[index].Value == value)
                return;
            _attributes[index] = new SvgAttribute(name, value);
        }
        else
        {
            _attributes.Add(new SvgAttribute(name, value));
        }
        OnAttributeChanged(name);
        MarkChanged();
    }

    /// <summary>Replaces all attributes at once (a history step restoring a snapshot) and tells the document.</summary>
    public void RestoreAttributes(IEnumerable<SvgAttribute> attributes)
    {
        ReplaceAttributes(attributes);
        MarkChanged();
    }

    /// <summary>Adds an attribute while building the node from XML (no dirty flag).</summary>
    internal void AddParsedAttribute(XName name, string value)
    {
        _attributes.Add(new SvgAttribute(name, value));
        OnAttributeChanged(name);
    }

    internal void ReplaceAttributes(IEnumerable<SvgAttribute> attributes)
    {
        _attributes.Clear();
        _attributes.AddRange(attributes);
        _transformText = null;
        _style?.Invalidate();
    }

    protected virtual void OnAttributeChanged(XName name)
    {
        if (name == "transform")
            _transformText = null;
        else if (name == "style")
            _style?.Invalidate();
    }

    // ---- Common attributes ----

    public string? Id
    {
        get => GetAttribute("id");
        set => SetAttribute("id", string.IsNullOrEmpty(value) ? null : value);
    }

    /// <summary>The class names of the <c>class</c> attribute.</summary>
    public IReadOnlyList<string> Classes =>
        GetAttribute("class")?.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries) ?? [];

    /// <summary>Inkscape's layer/object name, or the id, or the element name: what a panel shows.</summary>
    public string Label => GetAttribute(Inkscape + "label") is { Length: > 0 } label ? label
        : Id is { Length: > 0 } id ? id : ElementName;

    /// <summary>Matrix of the <c>transform</c> attribute (identity when absent or invalid).</summary>
    public Matrix2D Transform
    {
        get
        {
            var text = GetAttribute("transform");
            if (!ReferenceEquals(text, _transformText) && text != _transformText)
            {
                _transformText = text;
                _transform = TransformParser.Parse(text);
            }
            return _transform;
        }
        set => SetAttribute("transform", value.IsIdentity ? null : TransformParser.Write(value));
    }

    public bool IsLocked
    {
        get => GetAttribute(Sodipodi + "insensitive") == "true";
        set => SetAttribute(Sodipodi + "insensitive", value ? "true" : null);
    }

    // ---- Typed attribute helpers for subclasses ----

    /// <summary>A length attribute in user units (percentages relative to the document's viewport); the default when absent or invalid.</summary>
    protected double GetLength(XName name, double fallback = 0, LengthAxis axis = LengthAxis.Diagonal)
    {
        if (!SvgLength.TryParse(GetAttribute(name), out var length))
            return fallback;
        return length.ToUser(DocumentRoot?.PercentBase(axis) ?? 0, ResolvedFontSize());
    }

    /// <summary>A length attribute with its unit as written, or null.</summary>
    public SvgLength? GetLengthValue(XName name) => SvgLength.TryParse(GetAttribute(name), out var l) ? l : null;

    protected void SetNumber(XName name, double value) => SetAttribute(name, NumberFormat.Format(value, 6));

    protected double GetNumber(XName name, double fallback = 0) =>
        NumberFormat.TryParse(GetAttribute(name) ?? "", out var v) ? v : fallback;

    protected double[] GetNumberList(XName name)
    {
        var text = GetAttribute(name);
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var result = new List<double>();
        var s = new SvgScanner(text);
        s.SkipWhitespace();
        while (!s.AtEnd && s.TryReadNumber(out var v))
        {
            result.Add(v);
            s.SkipWhitespaceAndComma();
        }
        return [.. result];
    }

    protected void SetNumberList(XName name, IEnumerable<double>? values) =>
        SetAttribute(name, values is null ? null : string.Join(' ', values.Select(v => NumberFormat.Format(v, 6))));

    private double ResolvedFontSize() => StyleResolver.FontSizeOf(this);

    /// <summary>The <c>href</c> (or <c>xlink:href</c>) without the leading #, when it points inside the document.</summary>
    public string? HrefId => Href is { } h && h.StartsWith('#') ? h[1..] : null;

    public string? Href
    {
        get => GetAttribute("href") ?? GetAttribute(XLink + "href");
        set
        {
            // Keep the spelling the file used.
            if (GetAttribute(XLink + "href") is not null && GetAttribute("href") is null)
                SetAttribute(XLink + "href", value);
            else
                SetAttribute("href", value);
        }
    }
}

public enum LengthAxis
{
    X,
    Y,
    Diagonal,
}

/// <summary>An element the engine does not model (filter, metadata, foreignObject, other namespaces…): kept verbatim, never edited.</summary>
public sealed class SvgRawElement : SvgElement
{
    public SvgRawElement(XElement source) : base(source.Name)
    {
        SourceNode = source;
        foreach (var a in source.Attributes())
            AddParsedAttribute(a.Name, a.Value);
    }

    public XElement Source => (XElement)SourceNode!;

    public override bool IsDirty => false;
}

/// <summary>An element that holds child nodes.</summary>
public abstract class SvgContainer : SvgElement
{
    private readonly List<SvgNode> _children = [];
    private SvgNode[]? _originalChildren;

    protected SvgContainer(XName name) : base(name)
    {
    }

    /// <summary>Remembers the children as read (and the attributes), for <see cref="SvgNode.IsDirty"/>.</summary>
    internal void FreezeOriginalWithChildren()
    {
        FreezeOriginal();
        _originalChildren = [.. _children];
    }

    public override bool IsDirty => base.IsDirty || _originalChildren is null || !_children.SequenceEqual(_originalChildren);

    public override IReadOnlyList<SvgNode> Children => _children;

    /// <summary>The modeled children (no comments or other raw content), in order.</summary>
    public IEnumerable<SvgElement> Elements => _children.OfType<SvgElement>();

    public void AddChild(SvgNode child) => InsertChild(_children.Count, child);

    public void InsertChild(int index, SvgNode child)
    {
        if (child.Parent is not null)
            throw new InvalidOperationException("The node already has a parent; remove it first.");
        if (ReferenceEquals(child, this) || child.SelfAndDescendants().Contains(this))
            throw new InvalidOperationException("A node cannot become its own descendant.");
        _children.Insert(Math.Clamp(index, 0, _children.Count), child);
        child.Parent = this;
        MarkChanged();
    }

    /// <summary>Removes the child and returns the index it had, or -1 when it was not a child.</summary>
    public int RemoveChild(SvgNode child)
    {
        var index = _children.IndexOf(child);
        if (index < 0)
            return -1;
        _children.RemoveAt(index);
        child.Parent = null;
        MarkChanged();
        return index;
    }

    /// <summary>Moves a child to a new index among the children (the index it will have afterwards).</summary>
    public void MoveChild(SvgNode child, int newIndex)
    {
        var index = _children.IndexOf(child);
        if (index < 0)
            throw new ArgumentException("Not a child of this node.", nameof(child));
        _children.RemoveAt(index);
        _children.Insert(Math.Clamp(newIndex, 0, _children.Count), child);
        MarkChanged();
    }

    internal void AddParsedChild(SvgNode child)
    {
        _children.Add(child);
        child.Parent = this;
    }

    internal void ClearChildren()
    {
        foreach (var c in _children)
            c.Parent = null;
        _children.Clear();
    }
}
