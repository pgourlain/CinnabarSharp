using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Models;

public class ImageDocumentHistory : IImageDocumentHistory
{
    private const int Unreachable = -2;

    private readonly ImageDocument _document;
    private readonly IDocumentEventsService _events;
    private readonly List<IHistoryItem> _items = [];
    // The first step (index 0) is the state the document was created or opened in.
    private int _cleanPointer = 0;

    public ImageDocumentHistory(ImageDocument document, IDocumentEventsService events)
    {
        _document = document;
        _events = events;
    }

    public IReadOnlyList<IHistoryItem> Items => _items;
    public int Pointer { get; private set; } = -1;
    public bool CanUndo => Pointer > 0;
    public bool CanRedo => Pointer < _items.Count - 1;

    public void PushNewItem(IHistoryItem newItem)
    {
        if (_cleanPointer > Pointer && CanRedo)
            _cleanPointer = Unreachable;
        for (var i = _items.Count - 1; i > Pointer; i--)
        {
            _items[i].Dispose();
            _items.RemoveAt(i);
        }

        _items.Add(newItem);
        Pointer = _items.Count - 1;
        Changed(invalidate: false);
    }

    public void Undo()
    {
        if (!CanUndo)
            throw new InvalidOperationException("Nothing to undo.");
        _items[Pointer].Undo();
        Pointer--;
        Changed(invalidate: true);
    }

    public void Redo()
    {
        if (!CanRedo)
            throw new InvalidOperationException("Nothing to redo.");
        Pointer++;
        _items[Pointer].Redo();
        Changed(invalidate: true);
    }

    public void JumpTo(int index)
    {
        if (index < 0 || index >= _items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        while (Pointer > index)
            Undo();
        while (Pointer < index)
            Redo();
    }

    public void SetClean()
    {
        _cleanPointer = Pointer;
        _document.IsDirty = false;
    }

    public void Clear()
    {
        foreach (var item in _items)
            item.Dispose();
        _items.Clear();
        Pointer = -1;
        _cleanPointer = 0;
        _events.PushEvent(new DocumentEventItem(_document, DocumentEventEnum.HistoryChanged));
    }

    private void Changed(bool invalidate)
    {
        _document.IsDirty = Pointer != _cleanPointer;
        if (invalidate)
            _document.Workspace.Invalidate();
        _events.PushEvent(new DocumentEventItem(_document, DocumentEventEnum.HistoryChanged));
    }
}
