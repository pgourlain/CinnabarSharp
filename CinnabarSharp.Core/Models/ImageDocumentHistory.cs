using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Models;

public class ImageDocumentHistory : IImageDocumentHistory
{
    private const int Unreachable = -2;

    /// <summary>Steps kept regardless of <see cref="Bytes"/>, like Paint.NET's practical undo limit.</summary>
    public const int DefaultMaxSteps = 100;

    /// <summary>Fraction of the machine's available memory one document's history may use by default.</summary>
    private const double DefaultBudgetFraction = 0.25;

    private readonly ImageDocument _document;
    private readonly IDocumentEventsService _events;
    private readonly List<IHistoryItem> _items = [];
    private readonly long _byteBudget;
    private readonly int _maxSteps;
    private bool _trimWarningShown;
    // The first step (index 0) is the state the document was created or opened in.
    private int _cleanPointer = 0;

    public ImageDocumentHistory(ImageDocument document, IDocumentEventsService events,
        long? byteBudget = null, int maxSteps = DefaultMaxSteps)
    {
        _document = document;
        _events = events;
        _byteBudget = byteBudget ?? DefaultByteBudget();
        _maxSteps = maxSteps;
    }

    private static long DefaultByteBudget()
    {
        try
        {
            var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (available > 0)
                return (long)(available * DefaultBudgetFraction);
        }
        catch
        {
            // Fall through to the fixed fallback below (e.g. not supported by the current GC/runtime).
        }
        return 512L * 1024 * 1024;
    }

    public IReadOnlyList<IHistoryItem> Items => _items;
    public int Pointer { get; private set; } = -1;
    public bool CanUndo => Pointer > 0;
    public bool CanRedo => Pointer < _items.Count - 1;
    public long Bytes => _items.Sum(i => i.Bytes);

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
        TrimToBudget();
        Changed(invalidate: false);
    }

    /// <summary>
    /// Drops the oldest steps (index 0 up) while over the step count or byte budget. Never drops the step the
    /// document is currently at or anything after it, so undo/redo of what remains stays exact; the oldest
    /// remaining step simply becomes unreachable by further undo.
    /// </summary>
    private void TrimToBudget()
    {
        var trimmed = false;
        while (Pointer > 0 && (_items.Count > _maxSteps || Bytes > _byteBudget))
        {
            _items[0].Dispose();
            _items.RemoveAt(0);
            Pointer--;
            if (_cleanPointer != Unreachable)
                _cleanPointer = _cleanPointer > 0 ? _cleanPointer - 1 : Unreachable;
            trimmed = true;
        }
        if (!trimmed)
            return;
        if (!_trimWarningShown)
        {
            _trimWarningShown = true;
            _events.PushEvent(new DocumentEventItem(_document, DocumentEventEnum.HistoryTrimmed));
        }
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
