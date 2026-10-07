using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>
/// Pen (B): a click adds a corner node, a click and drag adds a smooth node whose handles follow the drag, a click on the first
/// node closes the path, Enter or a double click finishes it open, Escape cancels, Backspace removes the last node, Shift
/// snaps the direction from the last node to 15°. The path is editable until finished and becomes one history step then.
/// </summary>
public sealed class VectorPenTool(ToolSettings settings) : IVectorKeyboardTool, IGridSnappingTool
{
    private const double CloseReach = 7;      // screen pixels
    private const double DragThreshold = 3;   // screen pixels

    private EditableFigure? _figure;
    private SvgPath? _live;
    private int _pointer;
    private bool _creating;
    private VPoint _downAt;

    public string Name => "Pen";

    public bool IsEditing(SvgDocument document)
    {
        if (_figure is null)
            return false;
        // Any other change of the history (an undo, another action) ends the drawing: the path is dropped.
        if (document.History.Pointer != _pointer)
        {
            Abandon(document);
            return false;
        }
        return true;
    }

    public void Refresh(SvgDocument document)
    {
        if (IsEditing(document) && _live is not null)
            ShapeStyling.Apply(document, _live, settings, hasFill: true);
    }

    public void OnPointerDown(SvgDocument document, ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left)
            return;
        IsEditing(document);
        var p = pointer.Position.ToVector();
        if (_figure is not null && pointer.ClickCount >= 2)
        {
            Finish(document);
            return;
        }
        if (_figure is { Nodes.Count: >= 2 } figure && p.DistanceTo(figure.Nodes[0].Point) <= document.ScreenToUser(CloseReach))
        {
            figure.Closed = true;
            Finish(document);
            return;
        }
        if (_figure is { Nodes.Count: > 0 } open && pointer.Modifiers.HasFlag(ToolModifiers.Shift))
            p = SnapAngle(open.Nodes[^1].Point, p);
        if (_figure is null)
        {
            _figure = new EditableFigure();
            _pointer = document.History.Pointer;
        }
        _figure.Nodes.Add(new PathNode(p));
        _creating = true;
        _downAt = p;
        UpdateLive(document);
    }

    public void OnPointerMove(SvgDocument document, ToolPointer pointer)
    {
        if (!_creating || _figure is not { Nodes.Count: > 0 })
            return;
        var p = pointer.Position.ToVector();
        if (p.DistanceTo(_downAt) < document.ScreenToUser(DragThreshold))
            return;
        var node = _figure.Nodes[^1];
        if (pointer.Modifiers.HasFlag(ToolModifiers.Shift))
            p = SnapAngle(node.Point, p);
        node.Type = NodeType.Symmetric;
        node.Out = p;
        node.In = node.Point + (node.Point - p);
        UpdateLive(document);
    }

    public void OnPointerUp(SvgDocument document, ToolPointer pointer) => _creating = false;

    private static VPoint SnapAngle(VPoint from, VPoint to)
    {
        var d = to - from;
        if (d.Length < 1e-9)
            return to;
        var angle = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 12)) * (Math.PI / 12);
        return from + new VVector(Math.Cos(angle), Math.Sin(angle)) * d.Length;
    }

    // ---- The live path ----

    private VectorPath CurrentPath() => new EditablePath { Figures = { _figure! } }.ToPath();

    private void UpdateLive(SvgDocument document)
    {
        var path = CurrentPath();
        if (_live is null)
        {
            _live = new SvgPath { Id = document.Root.NewId("path") };
            ShapeStyling.Apply(document, _live, settings, hasFill: true);
            SvgDocumentFactory.DefaultParent(document.Root).AddChild(_live);
            document.NotifyTreeChanged();
        }
        _live.SetPath(path);
        document.NotifyNodeChanged(_live, SvgBounds.Visual(_live, document.GlyphProvider));
    }

    /// <summary>Keeps the path as drawn: one history step. Fewer than two nodes draw nothing and are dropped.</summary>
    public void Finish(SvgDocument document)
    {
        var figure = _figure;
        var live = _live;
        Reset(document);
        if (figure is null || live is null || figure.Nodes.Count < 2)
            return;
        live.SetPath(new EditablePath { Figures = { figure } }.ToPath());
        document.Actions.AddNode(live, name: "Pen");
    }

    /// <summary>Drops the path being drawn without a trace.</summary>
    public void Cancel(SvgDocument document) => Reset(document);

    private void Abandon(SvgDocument document) => Reset(document);

    private void Reset(SvgDocument document)
    {
        if (_live?.Parent is { } parent)
        {
            parent.RemoveChild(_live);
            document.NotifyTreeChanged();
        }
        _live = null;
        _figure = null;
        _creating = false;
    }

    // ---- Keys ----

    public bool OnKeyDown(SvgDocument document, ToolKey key, ToolModifiers modifiers)
    {
        if (!IsEditing(document))
            return false;
        switch (key)
        {
            case ToolKey.Enter:
                Finish(document);
                return true;
            case ToolKey.Escape:
                Cancel(document);
                return true;
            case ToolKey.Backspace:
            case ToolKey.Delete:
                _figure!.Nodes.RemoveAt(_figure.Nodes.Count - 1);
                if (_figure.Nodes.Count == 0)
                    Cancel(document);
                else
                    UpdateLive(document);
                return true;
            default:
                return false;
        }
    }

    // ---- Overlay ----

    public ToolOverlay? GetOverlay(SvgDocument document)
    {
        if (!IsEditing(document) || _figure is null)
            return null;
        var to = document.UserToImage;
        PointD I(VPoint p) => to.Transform(p).ToCore();
        var handles = new List<PointD>();
        var lines = new List<(PointD, PointD)>();
        foreach (var node in _figure.Nodes)
        {
            handles.Add(I(node.Point));
            foreach (var handle in new[] { node.In, node.Out })
                if (handle is { } h)
                {
                    handles.Add(I(h));
                    lines.Add((I(node.Point), I(h)));
                }
        }
        return new ToolOverlay { Handles = handles, Lines = lines };
    }
}
