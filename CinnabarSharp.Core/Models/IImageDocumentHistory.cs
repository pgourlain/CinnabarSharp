namespace CinnabarSharp.Core.Models;

/// <summary>
/// Undo/redo steps of one document. <see cref="Pointer"/> is the index of the step the document is at;
/// steps after it are undone and can be redone until a new step is pushed.
/// The document is dirty whenever the pointer differs from the step at which it was last saved.
/// </summary>
public interface IImageDocumentHistory
{
    IReadOnlyList<IHistoryItem> Items { get; }
    int Pointer { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }

    /// <summary>Sum of every step's <see cref="IHistoryItem.Bytes"/> currently held in memory.</summary>
    long Bytes { get; }

    /// <summary>Records a change that was already applied, discarding any undone steps.</summary>
    void PushNewItem(IHistoryItem newItem);

    void Undo();
    void Redo();

    /// <summary>Undoes or redoes until <see cref="Pointer"/> equals <paramref name="index"/>.</summary>
    void JumpTo(int index);

    /// <summary>Marks the current step as saved.</summary>
    void SetClean();

    /// <summary>Disposes every step.</summary>
    void Clear();
}
