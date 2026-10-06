using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>The selected objects of an SVG document, in selection order (not a pixel mask).</summary>
public sealed class SvgSelection
{
    private readonly SvgDocument _document;
    private readonly List<SvgElement> _nodes = [];

    internal SvgSelection(SvgDocument document) => _document = document;

    public IReadOnlyList<SvgElement> Nodes => _nodes;

    public int Count => _nodes.Count;

    public bool IsEmpty => _nodes.Count == 0;

    public bool Contains(SvgElement node) => _nodes.Contains(node);

    /// <summary>The primary (last selected) object, or null.</summary>
    public SvgElement? Primary => _nodes.Count == 0 ? null : _nodes[^1];

    /// <summary>Replaces the selection; raises <c>VectorSelectionChanged</c> only when it really changed.</summary>
    public void Set(IEnumerable<SvgElement> nodes)
    {
        var next = nodes.Distinct().ToList();
        if (next.SequenceEqual(_nodes))
            return;
        _nodes.Clear();
        _nodes.AddRange(next);
        _document.NotifySelectionChanged();
    }

    public void Set(SvgElement node) => Set([node]);

    public void Clear() => Set([]);

    public void Add(SvgElement node)
    {
        if (!_nodes.Contains(node))
            Set(_nodes.Append(node));
    }

    public void Toggle(SvgElement node)
    {
        if (_nodes.Contains(node))
            Set(_nodes.Where(n => n != node));
        else
            Add(node);
    }

    /// <summary>Drops nodes that are no longer in the document (after a delete, undo of an insert…).</summary>
    internal void Prune()
    {
        var root = _document.Root;
        var alive = _nodes.Where(n => n.DocumentRoot == root).ToList();
        if (alive.Count != _nodes.Count)
            Set(alive);
    }
}
