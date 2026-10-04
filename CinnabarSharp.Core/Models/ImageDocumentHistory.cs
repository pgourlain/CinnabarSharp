using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Models;

public class ImageDocumentHistory : IImageDocumentHistory
{
    private const int Unreachable = -2;

    /// <summary>Steps kept regardless of <see cref="Bytes"/>, like Paint.NET's practical undo limit.</summary>
    public const int DefaultMaxSteps = 100;

    /// <summary>Fraction of the machine's available memory one document's history may use by default.</summary>
    private const double DefaultBudgetFraction = 0.25;

    /// <summary>Steps this far (or farther) from <see cref="Pointer"/> are spilled to disk when storage is available.</summary>
    public const int DefaultSpillDistance = 10;

    private readonly ImageDocument _document;
    private readonly IDocumentEventsService _events;
    private readonly List<IHistoryItem> _items = [];
    private readonly long _byteBudget;
    private readonly int _maxSteps;
    private readonly IHistoryStorage? _storageFactory;
    private readonly int _spillDistance;
    private IHistoryDocumentStorage? _storage;
    private bool _trimWarningShown;
    // The first step (index 0) is the state the document was created or opened in.
    private int _cleanPointer = 0;

    public ImageDocumentHistory(ImageDocument document, IDocumentEventsService events,
        long? byteBudget = null, int maxSteps = DefaultMaxSteps,
        IHistoryStorage? storage = null, int spillDistance = DefaultSpillDistance)
    {
        _document = document;
        _events = events;
        _byteBudget = byteBudget ?? DefaultByteBudget();
        _maxSteps = maxSteps;
        _storageFactory = storage;
        _spillDistance = spillDistance;
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

        _document.Floating = null; // a tool that keeps one sets it again after pushing its step
        _items.Add(newItem);
        Pointer = _items.Count - 1;
        TrimToBudget();
        SpillFarSteps();
        Changed();
    }

    /// <summary>
    /// Moves steps <see cref="_spillDistance"/> or more away from <see cref="Pointer"/> to disk when storage is
    /// configured, freeing their <see cref="IHistoryItem.Bytes"/> from memory (see <see cref="CompressedDiff"/>).
    /// Steps that come back within range are not moved back; they just stay spilled, which is still fast enough
    /// to undo/redo. The storage folder itself is only created the first time there's actually something to spill.
    /// </summary>
    private void SpillFarSteps()
    {
        if (_storageFactory is null)
            return;
        for (var i = 0; i < _items.Count; i++)
        {
            if (Math.Abs(i - Pointer) < _spillDistance)
                continue;
            if (_items[i] is ISpillableHistoryItem spillable)
                spillable.Spill(_storage ??= _storageFactory.OpenDocumentStorage());
        }
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
        var item = _items[Pointer];
        item.Undo();
        Pointer--;
        SpillFarSteps();
        InvalidateFor(item);
        Changed();
    }

    public void Redo()
    {
        if (!CanRedo)
            throw new InvalidOperationException("Nothing to redo.");
        Pointer++;
        var item = _items[Pointer];
        item.Redo();
        SpillFarSteps();
        InvalidateFor(item);
        Changed();
    }

    /// <summary>
    /// Redraws only what a step actually changed instead of always re-flattening the whole image: steps that
    /// know their own touched rectangle (e.g. <see cref="PixelRegionHistoryItem"/>) invalidate just that;
    /// everything else falls back to a full redraw, as every undo/redo did before this existed.
    /// </summary>
    private void InvalidateFor(IHistoryItem item)
    {
        if (item.TouchedRect is { } rect)
            _document.Workspace.Invalidate(rect);
        else
            _document.Workspace.Invalidate();
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
        _storage?.Dispose();
        _storage = null;
        _events.PushEvent(new DocumentEventItem(_document, DocumentEventEnum.HistoryChanged));
    }

    /// <summary>Updates <see cref="ImageDocument.IsDirty"/> and raises <see cref="DocumentEventEnum.HistoryChanged"/>.
    /// Canvas invalidation is the caller's job (<see cref="InvalidateFor"/> for undo/redo; a push doesn't
    /// invalidate here because the edit that led to it already showed its own live result).</summary>
    private void Changed()
    {
        _document.IsDirty = Pointer != _cleanPointer;
        _events.PushEvent(new DocumentEventItem(_document, DocumentEventEnum.HistoryChanged));
    }
}
