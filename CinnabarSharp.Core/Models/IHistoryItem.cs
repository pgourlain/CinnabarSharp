namespace CinnabarSharp.Core.Models;

/// <summary>
/// One undoable step. It is created after the change was applied, so it starts in the "done" state.
/// Disposing releases pixel data that only the history still references (see <see cref="HistoryItem.OnDispose"/>).
/// </summary>
public interface IHistoryItem : IDisposable
{
    string Text { get; }
    bool IsUndone { get; }
    void Undo();
    void Redo();
}

public abstract class HistoryItem(string text) : IHistoryItem
{
    public string Text { get; } = text;
    public bool IsUndone { get; private set; }

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
