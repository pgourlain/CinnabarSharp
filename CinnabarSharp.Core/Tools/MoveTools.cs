using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tools;

/// <summary>Drag to move the selection outline without changing pixels.</summary>
public sealed class MoveSelectionTool : ITool
{
    private SelectionMask? _before;
    private PointD _start;
    private bool _active;

    public string Name => "Move Selection";

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        _before = document.Selection;
        _active = _before is not null;
        _start = pointer.Position;
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_active)
            document.SetSelection(_before!.Offset(Delta(pointer).X, Delta(pointer).Y));
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        if (!_active)
            return;
        _active = false;
        OnPointerMove(document, pointer);
        if (Delta(pointer) == PointI.Zero)
            document.SetSelection(_before);
        document.Actions.RecordSelectionChange(_before, Name);
    }

    private PointI Delta(ToolPointer pointer) => new(
        (int)Math.Round(pointer.Position.X - _start.X), (int)Math.Round(pointer.Position.Y - _start.Y));
}

/// <summary>
/// Drag to move the selected pixels of the current layer (the whole layer when nothing is selected).
/// The area left behind becomes transparent; the selection moves with the pixels.
/// </summary>
public sealed class MoveSelectedPixelsTool : ITool
{
    private Layer? _layer;
    private IImageBuf? _original;
    private IImageBuf? _preview;
    private byte[] _base = [];
    private byte[] _lifted = [];
    private SelectionMask? _selectionBefore;
    private PointD _start;

    public string Name => "Move Selected Pixels";

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        var (w, h) = (document.ImageSize.Width, document.ImageSize.Height);
        _layer = document.Layers.CurrentUserLayer;
        _original = _layer.Surface;
        _selectionBefore = document.Selection;
        _start = pointer.Position;

        var mask = (_selectionBefore ?? SelectionMask.All(w, h)).Data;
        var pixels = _original.ToBgra();
        _lifted = new byte[pixels.Length];
        _base = pixels;
        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0)
                continue;
            pixels.AsSpan(i * 4, 4).CopyTo(_lifted.AsSpan(i * 4, 4));
            _base.AsSpan(i * 4, 4).Clear();
        }
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_layer is null)
            return;
        var (w, h) = (document.ImageSize.Width, document.ImageSize.Height);
        var d = Delta(pointer);

        var composed = (byte[])_base.Clone();
        PixelRegion.Place(composed, w, h, _lifted, w, h, d.X, d.Y, composite: true);
        var previous = _preview;
        _preview = Utility.FromBgra(composed, w, h);
        _layer.Surface = _preview;
        previous?.Dispose();

        document.SetSelection(_selectionBefore?.Offset(d.X, d.Y));
        document.Workspace.Invalidate();
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        if (_layer is null)
            return;
        var layer = _layer;
        _layer = null;

        if (Delta(pointer) == PointI.Zero)
        {
            layer.Surface = _original!;
            _preview?.Dispose();
            document.SetSelection(_selectionBefore);
            document.Workspace.Invalidate();
        }
        else
        {
            if (_preview is null)
            {
                _layer = layer;
                OnPointerMove(document, pointer);
                _layer = null;
            }
            document.Workspace.History.PushNewItem(new CompoundHistoryItem(Name,
            [
                new SwapSurfaceHistoryItem("", layer, _original!, layer.Surface),
                new SelectionHistoryItem("", document, _selectionBefore, document.Selection),
            ]));
        }
        _preview = null;
        _base = [];
        _lifted = [];
    }

    private PointI Delta(ToolPointer pointer) => new(
        (int)Math.Round(pointer.Position.X - _start.X), (int)Math.Round(pointer.Position.Y - _start.Y));
}
