using CinnabarSharp.Core.Models;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>
/// What a history step remembers of an element: its attributes and, for text, its content (runs and spans). Children of
/// other elements are not part of it: groups change through insert, delete and reorder steps.
/// </summary>
public sealed class NodeSnapshot
{
    private readonly SvgAttribute[] _attributes;
    private readonly TextChild[]? _textChildren;

    /// <summary>A child of a text element: the node itself (so undo puts the same objects back) and its state.</summary>
    private sealed record TextChild(SvgNode Node, string? Text, NodeSnapshot? Inner);

    private NodeSnapshot(SvgAttribute[] attributes, TextChild[]? textChildren)
    {
        _attributes = attributes;
        _textChildren = textChildren;
    }

    public IReadOnlyList<SvgAttribute> Attributes => _attributes;

    public static NodeSnapshot Capture(SvgElement element) => new([.. element.Attributes],
        element is SvgTextBase text
            ? [.. text.Children.Select(c => c switch
            {
                SvgTextRun run => new TextChild(run, run.Text, null),
                SvgTextBase span => new TextChild(span, null, Capture(span)),
                _ => new TextChild(c, null, null),
            })]
            : null);

    /// <summary>True when both hold the same attributes (in the same order) and the same text content.</summary>
    public bool SameAs(NodeSnapshot other)
    {
        if (!_attributes.AsSpan().SequenceEqual(other._attributes))
            return false;
        if (_textChildren is null || other._textChildren is null)
            return _textChildren is null && other._textChildren is null;
        return _textChildren.Length == other._textChildren.Length
            && _textChildren.Zip(other._textChildren).All(p => ReferenceEquals(p.First.Node, p.Second.Node)
                && p.First.Text == p.Second.Text && (p.First.Inner is null ? p.Second.Inner is null : p.Second.Inner is not null && p.First.Inner.SameAs(p.Second.Inner)));
    }

    /// <summary>Puts the element back in this state: its attributes, and for text the same child nodes in their saved state.</summary>
    public void Restore(SvgElement element)
    {
        element.RestoreAttributes(_attributes);
        if (_textChildren is null || element is not SvgTextBase text)
            return;
        if (!text.Children.SequenceEqual(_textChildren.Select(c => c.Node)))
            text.ReplaceChildren(_textChildren.Select(c => c.Node));
        foreach (var child in _textChildren)
        {
            if (child is { Node: SvgTextRun run, Text: { } content })
                run.SetText(content);
            else if (child is { Node: SvgTextSpan span, Inner: { } inner })
                inner.Restore(span);
        }
    }
}

/// <summary>
/// One undoable step of an SVG document. It is created after the change was made. Several steps can be pushed as one
/// history entry (<see cref="VectorCompositeItem"/>), which runs their <see cref="UndoStep"/> and <see cref="RedoStep"/>.
/// </summary>
public abstract class VectorStepItem(SvgDocument document, string text) : HistoryItem(text)
{
    protected SvgDocument Document { get; } = document;

    /// <summary>Selection (internal ids) when the entry was made, and after it: undo and redo put it back. Set on the pushed entry only.</summary>
    internal IReadOnlyList<long>? SelectionBefore { get; set; }

    internal IReadOnlyList<long>? SelectionAfter { get; set; }

    internal abstract void UndoStep();

    internal abstract void RedoStep();

    protected override void OnUndo()
    {
        UndoStep();
        RestoreSelection(SelectionBefore);
    }

    protected override void OnRedo()
    {
        RedoStep();
        RestoreSelection(SelectionAfter);
    }

    private void RestoreSelection(IReadOnlyList<long>? ids)
    {
        if (ids is not null)
            Document.Selection.Set(ids.Select(id => Document.Root.FindByInternalId(id)).OfType<SvgElement>());
    }

    /// <summary>Redrawing after undo/redo is the history's job (a full canvas invalidation), so steps only raise tree/node events.</summary>
    protected void TreeChanged()
    {
        Document.Selection.PruneQuietly();
        Document.NotifyTreeChanged();
        Document.Selection.NotifyIfPruned();
    }
}

/// <summary>One element's attributes (and text) before and after a change.</summary>
public sealed record NodeChange(long Id, NodeSnapshot Before, NodeSnapshot After, VRect? BoundsBefore, VRect? BoundsAfter);

/// <summary>Attributes of one or more elements changed (move, style, transform, rename, text…). Found again by internal id, not by XML id.</summary>
public sealed class VectorNodeChangeItem(SvgDocument document, string text, IReadOnlyList<NodeChange> changes) : VectorStepItem(document, text)
{
    public IReadOnlyList<NodeChange> Changes { get; } = changes;

    internal override void UndoStep() => Apply(c => c.Before);

    internal override void RedoStep() => Apply(c => c.After);

    private void Apply(Func<NodeChange, NodeSnapshot> pick)
    {
        foreach (var change in Changes)
        {
            if (Document.Root.FindByInternalId(change.Id) is not SvgElement node)
                continue;
            pick(change).Restore(node);
            var dirty = change.BoundsBefore is { } b
                ? change.BoundsAfter is { } a ? b.Union(a) : b
                : change.BoundsAfter;
            Document.NotifyNodeChanged(node, dirty);
        }
    }
}

/// <summary>A node and where it sits: its parent's internal id and its index among the parent's children.</summary>
public sealed record NodePlace(long ParentId, int Index, SvgNode Node);

/// <summary>Nodes inserted into the tree (add, paste, duplicate, group, ungroup's children…).</summary>
public sealed class VectorInsertItem(SvgDocument document, string text, IReadOnlyList<NodePlace> places) : VectorStepItem(document, text)
{
    public IReadOnlyList<NodePlace> Places { get; } = places;

    internal override void UndoStep() { Remove(Places, Document); TreeChanged(); }

    internal override void RedoStep() { Insert(Places, Document); TreeChanged(); }

    internal static void Insert(IReadOnlyList<NodePlace> places, SvgDocument document)
    {
        // In index order, so each index is right when its turn comes.
        foreach (var place in places.OrderBy(p => p.ParentId).ThenBy(p => p.Index))
            if (document.Root.FindByInternalId(place.ParentId) is SvgContainer parent)
                parent.InsertChild(place.Index, place.Node);
    }

    internal static void Remove(IReadOnlyList<NodePlace> places, SvgDocument document)
    {
        foreach (var place in places.OrderByDescending(p => p.ParentId).ThenByDescending(p => p.Index))
            place.Node.Parent?.RemoveChild(place.Node);
    }
}

/// <summary>Nodes removed from the tree. The step keeps the nodes themselves, so undo puts the very same objects back.</summary>
public sealed class VectorDeleteItem(SvgDocument document, string text, IReadOnlyList<NodePlace> places) : VectorStepItem(document, text)
{
    public IReadOnlyList<NodePlace> Places { get; } = places;

    internal override void UndoStep() { VectorInsertItem.Insert(Places, Document); TreeChanged(); }

    internal override void RedoStep() { VectorInsertItem.Remove(Places, Document); TreeChanged(); }
}

/// <summary>A node moved to another index or another parent (z-order, move into or out of a group).</summary>
public sealed record NodeMove(long NodeId, long FromParentId, int FromIndex, long ToParentId, int ToIndex);

public sealed class VectorReorderItem(SvgDocument document, string text, IReadOnlyList<NodeMove> moves) : VectorStepItem(document, text)
{
    public IReadOnlyList<NodeMove> Moves { get; } = moves;

    internal override void UndoStep()
    {
        foreach (var move in Moves.Reverse())
            MoveNode(move.NodeId, move.FromParentId, move.FromIndex);
        TreeChanged();
    }

    internal override void RedoStep()
    {
        foreach (var move in Moves)
            MoveNode(move.NodeId, move.ToParentId, move.ToIndex);
        TreeChanged();
    }

    private void MoveNode(long nodeId, long parentId, int index)
    {
        if (Document.Root.FindByInternalId(nodeId) is not { } node || Document.Root.FindByInternalId(parentId) is not SvgContainer parent)
            return;
        node.Parent?.RemoveChild(node);
        parent.InsertChild(index, node);
    }
}

/// <summary>An undoable change of the selection (made at the end of a tool gesture).</summary>
public sealed class VectorSelectionItem(SvgDocument document, string text, IReadOnlyList<long> before, IReadOnlyList<long> after)
    : VectorStepItem(document, text)
{
    internal override void UndoStep() => Select(before);

    internal override void RedoStep() => Select(after);

    private void Select(IReadOnlyList<long> ids) =>
        Document.Selection.Set(ids.Select(id => Document.Root.FindByInternalId(id)).OfType<SvgElement>());
}

/// <summary>Several steps as one entry of the history list: undone together, in reverse order.</summary>
public sealed class VectorCompositeItem(SvgDocument document, string text, IReadOnlyList<VectorStepItem> steps) : VectorStepItem(document, text)
{
    public IReadOnlyList<VectorStepItem> Steps { get; } = steps;

    internal override void UndoStep()
    {
        for (var i = Steps.Count - 1; i >= 0; i--)
            Steps[i].UndoStep();
    }

    internal override void RedoStep()
    {
        foreach (var step in Steps)
            step.RedoStep();
    }
}
