using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tools;

/// <summary>
/// Drag to select a shape. The mode comes from the tool options, overridden by modifiers:
/// ⌘/Ctrl = union, Alt = exclude, ⌘/Ctrl + Alt = intersect, right button = exclude (as in Paint.NET).
/// A click without dragging in Replace mode deselects.
/// </summary>
public abstract class ShapeSelectionTool(ToolSettings settings) : ITool
{
    private bool _active;

    public abstract string Name { get; }

    protected PointD Start { get; private set; }

    /// <summary>The selection before the current drag.</summary>
    protected SelectionMask? Before { get; private set; }

    /// <summary>How the dragged shape combines with <see cref="Before"/>.</summary>
    protected SelectionMode Mode { get; private set; }

    protected abstract void Begin(PointD point);
    protected abstract void Extend(PointD point);
    protected abstract SelectionMask BuildShape(int width, int height, PointD current);
    protected abstract bool HasArea(PointD current);

    public static SelectionMode ModeFor(ToolPointer pointer, SelectionMode configured)
    {
        var command = pointer.Modifiers.HasFlag(ToolModifiers.Command);
        var alt = pointer.Modifiers.HasFlag(ToolModifiers.Alt);
        return (command, alt) switch
        {
            (true, true) => SelectionMode.Intersect,
            (true, false) => SelectionMode.Union,
            (false, true) => SelectionMode.Exclude,
            _ when pointer.Button == ToolButton.Right => SelectionMode.Exclude,
            _ => configured,
        };
    }

    public virtual void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        Before = document.Selection;
        Mode = ModeFor(pointer, settings.SelectionMode);
        _active = true;
        Start = pointer.Position;
        Begin(pointer.Position);
    }

    public virtual void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (!_active)
            return;
        Extend(pointer.Position);
        Preview(document, pointer.Position);
    }

    public virtual void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        if (!_active)
            return;
        _active = false;
        Extend(pointer.Position);
        if (HasArea(pointer.Position))
            Preview(document, pointer.Position);
        else
            document.SetSelection(Mode == SelectionMode.Replace ? null : Before);
        document.Actions.RecordSelectionChange(Before, Name);
    }

    private void Preview(ImageDocument document, PointD current)
    {
        var (w, h) = (document.ImageSize.Width, document.ImageSize.Height);
        var shape = BuildShape(w, h, current);
        var basis = Before ?? SelectionMask.Empty(w, h);
        document.SetSelection(basis.Combine(shape, Mode));
    }
}

/// <summary>
/// A selection shape defined by a box (rectangle, ellipse). After drawing, the box keeps eight square handles
/// (corners and middle of each edge) that resize the shape, as in Paint.NET, until the selection changes in any
/// other way (undo, Select All, another tool...). Each resize is one history step.
/// </summary>
public abstract class BoxSelectionTool(ToolSettings settings) : ShapeSelectionTool(settings), IOverlayTool, IGridSnappingTool
{
    // Screen pixels around a handle that grab it.
    private const double GrabRadius = 6;

    private ImageDocument? _document;
    private SelectionMask? _result;
    private SelectionMask? _basis;
    private SelectionMode _mode;
    private PointD _a, _b;

    private int _handle = -1;
    private SelectionMask? _dragBefore;

    protected abstract SelectionMask ShapeBetween(int width, int height, PointD a, PointD b);
    protected abstract bool HasAreaBetween(PointD a, PointD b);

    protected sealed override void Begin(PointD point) { }
    protected sealed override void Extend(PointD point) { }

    protected sealed override SelectionMask BuildShape(int width, int height, PointD current) =>
        ShapeBetween(width, height, Start, current);

    protected sealed override bool HasArea(PointD current) => HasAreaBetween(Start, current);

    /// <summary>True while the last drawn shape is the document's selection, so its handles can resize it.</summary>
    public bool IsEditing(ImageDocument document) =>
        _document == document && _result is not null && ReferenceEquals(document.Selection, _result);

    /// <summary>The handles: top-left, top, top-right, right, bottom-right, bottom, bottom-left, left.</summary>
    public IReadOnlyList<PointD> Handles(ImageDocument document)
    {
        if (!IsEditing(document))
            return [];
        var (x1, y1, x2, y2) = (Math.Min(_a.X, _b.X), Math.Min(_a.Y, _b.Y), Math.Max(_a.X, _b.X), Math.Max(_a.Y, _b.Y));
        var (cx, cy) = ((x1 + x2) / 2, (y1 + y2) / 2);
        return
        [
            new(x1, y1), new(cx, y1), new(x2, y1), new(x2, cy),
            new(x2, y2), new(cx, y2), new(x1, y2), new(x1, cy),
        ];
    }

    private int HandleAt(ImageDocument document, PointD point)
    {
        var handles = Handles(document);
        var radius = GrabRadius / Math.Max(document.Workspace.Scale, 0.01);
        var (best, bestDistance) = (-1, double.MaxValue);
        for (var i = 0; i < handles.Count; i++)
        {
            var (dx, dy) = (Math.Abs(handles[i].X - point.X), Math.Abs(handles[i].Y - point.Y));
            if (dx <= radius && dy <= radius && dx + dy < bestDistance)
                (best, bestDistance) = (i, dx + dy);
        }
        return best;
    }

    public override void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        // With ⌘/Ctrl or Alt the drag always draws a new shape to combine with the selection.
        if (pointer.Button == ToolButton.Left && pointer.Modifiers == ToolModifiers.None
                                              && HandleAt(document, pointer.Position) is var handle and >= 0)
        {
            // Resize from the box as drawn: a = top-left, b = bottom-right, then move the grabbed sides.
            (_a, _b) = (new PointD(Math.Min(_a.X, _b.X), Math.Min(_a.Y, _b.Y)), new PointD(Math.Max(_a.X, _b.X), Math.Max(_a.Y, _b.Y)));
            _handle = handle;
            _dragBefore = document.Selection;
            return;
        }
        base.OnPointerDown(document, pointer);
    }

    public override void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (_handle < 0)
        {
            base.OnPointerMove(document, pointer);
            return;
        }
        var p = pointer.Position;
        if (_handle is 0 or 6 or 7)
            _a = _a with { X = p.X };
        if (_handle is 2 or 3 or 4)
            _b = _b with { X = p.X };
        if (_handle is 0 or 1 or 2)
            _a = _a with { Y = p.Y };
        if (_handle is 4 or 5 or 6)
            _b = _b with { Y = p.Y };
        if (!HasAreaBetween(_a, _b))
            return;
        var (w, h) = (document.ImageSize.Width, document.ImageSize.Height);
        _result = (_basis ?? SelectionMask.Empty(w, h)).Combine(ShapeBetween(w, h, _a, _b), _mode);
        document.SetSelection(_result);
    }

    public override void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        if (_handle >= 0)
        {
            OnPointerMove(document, pointer);
            _handle = -1;
            document.Actions.RecordSelectionChange(_dragBefore, Name);
            return;
        }
        base.OnPointerUp(document, pointer);
        if (HasArea(pointer.Position))
        {
            (_document, _result, _basis, _mode) = (document, document.Selection, Before, Mode);
            (_a, _b) = (Start, pointer.Position);
        }
        else
        {
            _result = null;
        }
    }

    public ToolOverlay? GetOverlay(ImageDocument document)
    {
        if (!IsEditing(document))
            return null;
        return new ToolOverlay { Handles = Handles(document), SquareHandles = true };
    }

    public ToolCursor CursorAt(ImageDocument document, PointD point) => HandleAt(document, point) switch
    {
        0 or 4 => ToolCursor.ResizeDiagonal,
        2 or 6 => ToolCursor.ResizeAntiDiagonal,
        1 or 5 => ToolCursor.ResizeVertical,
        3 or 7 => ToolCursor.ResizeHorizontal,
        _ => ToolCursor.Default,
    };
}

public sealed class RectangleSelectTool(ToolSettings settings) : BoxSelectionTool(settings)
{
    public override string Name => "Rectangle Select";

    protected override SelectionMask ShapeBetween(int width, int height, PointD a, PointD b) =>
        SelectionMask.Rectangle(width, height, a, b);

    protected override bool HasAreaBetween(PointD a, PointD b) =>
        Math.Abs(Math.Round(b.X) - Math.Round(a.X)) >= 1 && Math.Abs(Math.Round(b.Y) - Math.Round(a.Y)) >= 1;
}

public sealed class EllipseSelectTool(ToolSettings settings) : BoxSelectionTool(settings)
{
    public override string Name => "Ellipse Select";

    protected override SelectionMask ShapeBetween(int width, int height, PointD a, PointD b) =>
        SelectionMask.Ellipse(width, height, a, b);

    protected override bool HasAreaBetween(PointD a, PointD b) =>
        Math.Abs(b.X - a.X) >= 2 && Math.Abs(b.Y - a.Y) >= 2;
}

public sealed class LassoSelectTool(ToolSettings settings) : ShapeSelectionTool(settings)
{
    private readonly List<PointD> _points = [];

    public override string Name => "Lasso Select";

    protected override void Begin(PointD point)
    {
        _points.Clear();
        _points.Add(point);
    }

    protected override void Extend(PointD point)
    {
        if (_points.Count == 0 || _points[^1] != point)
            _points.Add(point);
    }

    protected override SelectionMask BuildShape(int width, int height, PointD current) =>
        SelectionMask.Polygon(width, height, _points);

    protected override bool HasArea(PointD current) => _points.Count >= 3;
}

/// <summary>Click to select similar colors on the current layer; Shift selects them anywhere (global).</summary>
public sealed class MagicWandTool(ToolSettings settings) : ITool
{
    public string Name => "Magic Wand";

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        var (w, h) = (document.ImageSize.Width, document.ImageSize.Height);
        var start = new PointI((int)Math.Floor(pointer.Position.X), (int)Math.Floor(pointer.Position.Y));
        if (start.X < 0 || start.Y < 0 || start.X >= w || start.Y >= h)
            return;

        var pixels = document.Layers.CurrentUserLayer.Surface.ToBgra();
        var global = settings.GlobalFill || pointer.Modifiers.HasFlag(ToolModifiers.Shift);
        var shape = SelectionMask.MagicWand(pixels, w, h, start, settings.Tolerance, global);
        var mode = ShapeSelectionTool.ModeFor(pointer, settings.SelectionMode);

        var before = document.Selection;
        document.SetSelection((before ?? SelectionMask.Empty(w, h)).Combine(shape, mode));
        document.Actions.RecordSelectionChange(before, Name);
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer) { }
    public void OnPointerUp(ImageDocument document, ToolPointer pointer) { }
}
