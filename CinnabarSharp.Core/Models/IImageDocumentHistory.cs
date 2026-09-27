namespace CinnabarSharp.Core.Models;

public interface IImageDocumentHistory
{
    int Pointer { get; }
    bool CanRedo { get; }
    bool CanUndo { get; }
    void PushNewItem(IHistoryItem newItem);
    IEnumerable<IHistoryItem> Items { get; }
    void Undo();
    void Redo();
    void SetClean();
    void SetDirty();
    void Clear();
}