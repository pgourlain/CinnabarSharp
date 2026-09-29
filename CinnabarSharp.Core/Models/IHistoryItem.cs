namespace CinnabarSharp.Core.Models;

/// <summary>
/// One undoable step. It is created after the change was applied, so it starts in the "done" state.
/// Disposing releases pixel data that only the history still references (see <see cref="HistoryItem.OnDispose"/>).
/// </summary>
public interface IHistoryItem : IDisposable
{
    string Text { get; }
    bool IsUndone { get; }

    /// <summary>Approximate bytes this step holds in memory (pixel/mask data only), for the history's memory budget.</summary>
    long Bytes { get; }

    /// <summary>
    /// The rectangle this step's undo/redo changes on the canvas, in image coordinates, when it's narrower than
    /// the whole image (e.g. a brush stroke or a small fill); null means "the whole image, or unknown" — the
    /// safe default every step had before this existed. <see cref="ImageDocumentHistory"/> uses this so
    /// undoing/redoing a small pixel edit redraws only that rectangle instead of re-flattening every layer.
    /// </summary>
    RectangleI? TouchedRect { get; }

    void Undo();
    void Redo();
}

/// <summary>A step that can move the memory it holds to disk when it's far from the current position (see
/// <see cref="ImageDocumentHistory"/>).</summary>
public interface ISpillableHistoryItem
{
    void Spill(Services.IHistoryDocumentStorage storage);
}

public abstract class HistoryItem(string text) : IHistoryItem
{
    public string Text { get; } = text;
    public bool IsUndone { get; private set; }
    public virtual long Bytes => 0;
    public virtual RectangleI? TouchedRect => null;

    public void Undo()
    {
        if (IsUndone)
            throw new InvalidOperationException($"'{Text}' is already undone.");
        OnUndo();
        IsUndone = true;
    }

    public void Redo()
    {
        if (!IsUndone)
            throw new InvalidOperationException($"'{Text}' is not undone.");
        OnRedo();
        IsUndone = false;
    }

    public void Dispose()
    {
        OnDispose();
        GC.SuppressFinalize(this);
    }

    protected abstract void OnUndo();
    protected abstract void OnRedo();

    /// <summary>Dispose surfaces that are not part of the document in the item's current state.</summary>
    protected virtual void OnDispose()
    {
    }
}

/// <summary>The first entry of every history ("New Image", "Open Image"); it can't be undone.</summary>
public sealed class BaseHistoryItem(string text) : HistoryItem(text)
{
    protected override void OnUndo() => throw new InvalidOperationException("The first history step can't be undone.");
    protected override void OnRedo() => throw new InvalidOperationException("The first history step can't be redone.");
}

/// <summary>Several steps shown as one; undone in reverse order.</summary>
public sealed class CompoundHistoryItem(string text, IReadOnlyList<IHistoryItem> items) : HistoryItem(text)
{
    public override long Bytes => items.Sum(i => i.Bytes);

    /// <summary>Union of every child's touched rectangle, or null (whole image) if any child doesn't report one.</summary>
    public override RectangleI? TouchedRect
    {
        get
        {
            RectangleI? union = null;
            foreach (var item in items)
            {
                if (item.TouchedRect is not { } r)
                    return null;
                union = union is { } u ? CoverageMask.Union(u, r) : r;
            }
            return union;
        }
    }

    protected override void OnUndo()
    {
        for (var i = items.Count - 1; i >= 0; i--)
            items[i].Undo();
    }

    protected override void OnRedo()
    {
        foreach (var item in items)
            item.Redo();
    }

    protected override void OnDispose()
    {
        foreach (var item in items)
            item.Dispose();
    }
}
