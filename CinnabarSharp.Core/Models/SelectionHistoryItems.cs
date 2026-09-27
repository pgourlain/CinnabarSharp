namespace CinnabarSharp.Core.Models;

public sealed class SelectionHistoryItem(string text, ImageDocument document, SelectionMask? before, SelectionMask? after)
    : HistoryItem(text)
{
    protected override void OnUndo() => document.SetSelection(before);
    protected override void OnRedo() => document.SetSelection(after);
}

/// <summary>The image size changed and every layer's pixels were replaced (crop, and later resize/rotate).</summary>
public sealed class ResizeImageHistoryItem(
    string text,
    ImageDocument document,
    ImageSize sizeBefore,
    ImageSize sizeAfter,
    IReadOnlyList<(Layer Layer, IImageBuf Before, IImageBuf After)> surfaces,
    SelectionMask? selectionBefore,
    SelectionMask? selectionAfter)
    : HistoryItem(text)
{
    protected override void OnUndo() => Apply(sizeBefore, s => s.Before, selectionBefore);
    protected override void OnRedo() => Apply(sizeAfter, s => s.After, selectionAfter);

    private void Apply(ImageSize size, Func<(Layer Layer, IImageBuf Before, IImageBuf After), IImageBuf> pick,
        SelectionMask? selection)
    {
        document.SetSelection(null);
        foreach (var s in surfaces)
            s.Layer.Surface = pick(s);
        document.Resize(size);
        document.SetSelection(selection);
    }

    protected override void OnDispose()
    {
        foreach (var s in surfaces)
            (IsUndone ? s.After : s.Before).Dispose();
    }
}
