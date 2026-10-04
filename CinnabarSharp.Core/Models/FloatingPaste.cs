using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Models;

/// <summary>
/// A paste that can still be moved without damaging the layer (Paint.NET's floating paste): it keeps the pixels the
/// layer had before the paste, so Move Selected Pixels puts them back where the pasted image was lifted from.
/// It follows the paste and the moves that came after it, through undo and redo, and ends (the document drops it)
/// when any other step is added to the history.
/// </summary>
public sealed class FloatingPaste(Layer layer, byte[] underlying, ClipboardImage image, FloatingStep first)
{
    private readonly List<FloatingStep> _steps = [first];

    public Layer Layer { get; } = layer;
    /// <summary>The layer's pixels (BGRA) without the pasted image.</summary>
    public byte[] Underlying { get; } = underlying;
    public ClipboardImage Image { get; } = image;

    /// <summary>The state of the paste matching what the document shows now, or null when it doesn't match any.</summary>
    public FloatingStep? Current(ImageDocument document)
    {
        if (!ReferenceEquals(document.Layers.CurrentUserLayer, Layer)
            || document.Workspace.History is not { Pointer: >= 0 } history)
            return null;
        var item = history.Items[history.Pointer];
        return _steps.Find(s => ReferenceEquals(s.Item, item)
            && ReferenceEquals(Layer.Surface, s.Surface) && ReferenceEquals(document.Selection, s.Selection));
    }

    /// <summary>Records the state after a move (the history step has been pushed).</summary>
    public void AddStep(FloatingStep step) => _steps.Add(step);
}

/// <summary>One state of a <see cref="FloatingPaste"/>: where the image is, and what the document held then.</summary>
public sealed record FloatingStep(PointI Position, IHistoryItem Item, SelectionMask Selection, IImageBuf Surface);
