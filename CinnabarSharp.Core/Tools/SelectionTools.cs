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
    private SelectionMask? _before;
    private SelectionMode _mode;
    private bool _active;

    public abstract string Name { get; }

    protected PointD Start { get; private set; }

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

    public void OnPointerDown(ImageDocument document, ToolPointer pointer)
    {
        _before = document.Selection;
        _mode = ModeFor(pointer, settings.SelectionMode);
        _active = true;
        Start = pointer.Position;
        Begin(pointer.Position);
    }

    public void OnPointerMove(ImageDocument document, ToolPointer pointer)
    {
        if (!_active)
            return;
        Extend(pointer.Position);
        Preview(document, pointer.Position);
    }

    public void OnPointerUp(ImageDocument document, ToolPointer pointer)
    {
        if (!_active)
            return;
        _active = false;
        Extend(pointer.Position);
        if (HasArea(pointer.Position))
            Preview(document, pointer.Position);
        else
            document.SetSelection(_mode == SelectionMode.Replace ? null : _before);
        document.Actions.RecordSelectionChange(_before, Name);
    }

    private void Preview(ImageDocument document, PointD current)
    {
        var (w, h) = (document.ImageSize.Width, document.ImageSize.Height);
        var shape = BuildShape(w, h, current);
        var basis = _before ?? SelectionMask.Empty(w, h);
        document.SetSelection(basis.Combine(shape, _mode));
    }
}

public sealed class RectangleSelectTool(ToolSettings settings) : ShapeSelectionTool(settings)
{
    public override string Name => "Rectangle Select";
    protected override void Begin(PointD point) { }
    protected override void Extend(PointD point) { }

    protected override SelectionMask BuildShape(int width, int height, PointD current) =>
        SelectionMask.Rectangle(width, height, Start, current);

    protected override bool HasArea(PointD current) =>
        Math.Abs(Math.Round(current.X) - Math.Round(Start.X)) >= 1 && Math.Abs(Math.Round(current.Y) - Math.Round(Start.Y)) >= 1;
}

public sealed class EllipseSelectTool(ToolSettings settings) : ShapeSelectionTool(settings)
{
    public override string Name => "Ellipse Select";
    protected override void Begin(PointD point) { }
    protected override void Extend(PointD point) { }

    protected override SelectionMask BuildShape(int width, int height, PointD current) =>
        SelectionMask.Ellipse(width, height, Start, current);

    protected override bool HasArea(PointD current) =>
        Math.Abs(current.X - Start.X) >= 2 && Math.Abs(current.Y - Start.Y) >= 2;
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
