using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>A node of the path being edited: which figure and which node.</summary>
public readonly record struct NodeRef(int Figure, int Index);

/// <summary>
/// Node tool (N / F2). On a selected path it shows the nodes and the handles of the selected nodes: click or rubber band selects
/// nodes, drag moves nodes and handles (smooth and symmetric nodes keep their handles aligned), double click on a segment adds a
/// node, double click on a node switches corner and smooth, Delete removes nodes keeping the shape as close as it can. The commands
/// (node types, segments to lines or curves, break, join) are methods for the options bar. On a rectangle or ellipse it shows that
/// shape's own handles (corner radii, radii) and "Convert to path" turns it into a path to edit.
/// Edits are shown while dragging and become one history step on release.
/// </summary>
public sealed class VectorNodeTool(ToolSettings settings) : IVectorKeyboardTool, IGridSnappingTool
{
    private enum Mode { None, Nodes, Handle, Band, Shape }

    private const double Reach = 7;        // screen pixels
    private const double SegmentReach = 5; // screen pixels
    private const double HandleInset = 14; // screen pixels: how far inside a corner the radius handles of a rectangle sit

    private SvgPath? _path;
    private string? _data;
    private EditablePath? _editable;
    private Matrix2D _world = Matrix2D.Identity;
    private readonly HashSet<NodeRef> _selected = [];
    private Mode _mode;
    private VPoint _start;
    private NodeSnapshot? _snapshot;
    private EditablePath? _original;
    private NodeRef _handleOwner;
    private bool _handleIsOut;
    private VRect _band;
    private bool _moved;
    private int _shapeHandle;
    private List<SvgElement> _before = [];

    public string Name => "Node";

    public IReadOnlyCollection<NodeRef> SelectedNodes => _selected;

    public EditablePath? Editable => _editable;

    public bool IsEditing(SvgDocument document) => false;

    public void Finish(SvgDocument document) => Cancel(document);

    public void Refresh(SvgDocument document)
    {
    }

    // ---- Following the selection ----

    /// <summary>Loads the selected path (or forgets it); also when its data changed behind the tool's back (undo).</summary>
    private void Sync(SvgDocument document)
    {
        var path = document.Selection.Primary as SvgPath;
        if (path is null || path.DocumentRoot != document.Root)
        {
            _path = null;
            _editable = null;
            _data = null;
            _selected.Clear();
            return;
        }
        if (!ReferenceEquals(path, _path))
        {
            _path = path;
            _selected.Clear();
            _data = null;
        }
        if (_mode is Mode.Nodes or Mode.Handle)
            return;
        var data = path.Data;
        if (_data != data || _editable is null)
        {
            _data = data;
            _editable = EditablePath.From(path.CreatePath());
            _selected.RemoveWhere(n => n.Figure >= _editable.Figures.Count || n.Index >= _editable.Figures[n.Figure].Nodes.Count);
        }
        _world = SvgBounds.ToDocument(path);
    }

    private VPoint ToDocument(VPoint local) => _world.Transform(local);

    private VPoint ToLocal(VPoint document) => (_world.Invert() ?? Matrix2D.Identity).Transform(document);

    private VVector ToLocalVector(VVector delta) => (_world.Invert() ?? Matrix2D.Identity).TransformVector(delta);

    // ---- Pointer ----

    public void OnPointerDown(SvgDocument document, ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left)
            return;
        Sync(document);
        _start = pointer.Position.ToVector();
        _moved = false;
        _before = [.. document.Selection.Nodes];
        var p = _start;
        var reach = document.ScreenToUser(Reach);

        // Pressing a point of the outline of any other shape (rectangle, ellipse, polygon, line…) turns it into a path
        // first, so every point of every object can be edited. The radius handles of an ellipse, a circle and a rounded
        // rectangle keep priority; the corner radius handles sit a little inside the corners, which are plain points.
        if (document.Selection.Primary is SvgShape { } outlined and not SvgPath
            && ShapeHandleAt(document, p, reach) is null
            && OnOutline(document, outlined, p))
        {
            document.Actions.ObjectToPath([outlined]);
            Sync(document);
        }

        if (_path is not null && _editable is not null)
        {
            if (HandleAt(p, reach) is { } handle)
            {
                BeginEdit(document);
                _handleOwner = handle.Node;
                _handleIsOut = handle.IsOut;
                _mode = Mode.Handle;
                return;
            }
            if (NodeAt(p, reach) is { } node)
            {
                if (pointer.ClickCount >= 2)
                {
                    ToggleType(document, node);
                    return;
                }
                if (pointer.Modifiers.HasFlag(ToolModifiers.Shift))
                {
                    if (!_selected.Remove(node))
                        _selected.Add(node);
                    document.NotifySelectionChanged();
                    return;
                }
                if (!_selected.Contains(node))
                {
                    _selected.Clear();
                    _selected.Add(node);
                    document.NotifySelectionChanged();
                }
                BeginEdit(document);
                _mode = Mode.Nodes;
                return;
            }
            if (SegmentAt(p, document.ScreenToUser(SegmentReach)) is { } segment)
            {
                if (pointer.ClickCount >= 2)
                {
                    AddNodeAt(document, segment.Figure, segment.Segment, segment.T);
                    return;
                }
                _selected.Clear();
                var figure = _editable.Figures[segment.Figure];
                _selected.Add(new NodeRef(segment.Figure, segment.Segment));
                _selected.Add(new NodeRef(segment.Figure, (segment.Segment + 1) % figure.Nodes.Count));
                document.NotifySelectionChanged();
                return;
            }
        }

        // Shape handles (corner radius, radii) of the selected rect or ellipse.
        if (ShapeHandleAt(document, p, reach) is { } shapeHandle)
        {
            _shapeHandle = shapeHandle;
            _snapshot = NodeSnapshot.Capture((SvgElement)document.Selection.Primary!);
            _mode = Mode.Shape;
            return;
        }

        // Another object: select it (a path gets its nodes; anything else its own handles).
        var hit = SvgHitTester.HitTest(document, p, document.ScreenToUser(3), enterGroups: true);
        if (hit is not null && !ReferenceEquals(hit, document.Selection.Primary))
        {
            document.Selection.Set(hit);
            document.Actions.RecordSelectionChange(_before, "Select");
            Sync(document);
            return;
        }
        if (hit is null && !pointer.Modifiers.HasFlag(ToolModifiers.Shift))
            _selected.Clear();
        _band = new VRect(p.X, p.Y, 0, 0);
        _mode = _editable is null ? Mode.None : Mode.Band;
        document.NotifySelectionChanged();
    }

    public void OnPointerMove(SvgDocument document, ToolPointer pointer)
    {
        var p = pointer.Position.ToVector();
        switch (_mode)
        {
            case Mode.Nodes:
            {
                if (!_moved && p.DistanceTo(_start) < document.ScreenToUser(3))
                    return;
                _moved = true;
                var delta = ToLocalVector(p - _start);
                var work = _original!.Clone();
                foreach (var n in _selected)
                    work.Figures[n.Figure].Nodes[n.Index].MoveBy(delta);
                Preview(document, work);
                break;
            }
            case Mode.Handle:
            {
                _moved = true;
                var work = _original!.Clone();
                var node = work.Figures[_handleOwner.Figure].Nodes[_handleOwner.Index];
                var local = ToLocal(p);
                // With Alt the handle moves alone, whatever the node type.
                var type = node.Type;
                if (pointer.Modifiers.HasFlag(ToolModifiers.Alt))
                    node.Type = NodeType.Corner;
                if (_handleIsOut)
                    node.SetOut(local);
                else
                    node.SetIn(local);
                node.Type = type;
                Preview(document, work);
                break;
            }
            case Mode.Band:
                _band = VRect.FromPoints(_start, p);
                _selected.Clear();
                if (_editable is not null)
                    for (var f = 0; f < _editable.Figures.Count; f++)
                        for (var i = 0; i < _editable.Figures[f].Nodes.Count; i++)
                            if (_band.Contains(ToDocument(_editable.Figures[f].Nodes[i].Point)))
                                _selected.Add(new NodeRef(f, i));
                document.NotifySelectionChanged();
                break;
            case Mode.Shape:
                _moved = true;
                DragShapeHandle(document, p);
                break;
        }
    }

    public void OnPointerUp(SvgDocument document, ToolPointer pointer)
    {
        var mode = _mode;
        _mode = Mode.None;
        switch (mode)
        {
            case Mode.Nodes or Mode.Handle:
                if (_moved && _path is not null && _snapshot is not null)
                {
                    var result = _editable!.ToPath();
                    _snapshot.Restore(_path);
                    document.Actions.SetPathData(_path, result);
                    _data = _path.Data;
                    _editable = EditablePath.From(_path.CreatePath());
                    // The node types and handles survive the round trip through path data except for what a path cannot say.
                }
                else if (_path is not null && _snapshot is not null)
                {
                    _snapshot.Restore(_path);
                    _editable = _original;
                }
                _snapshot = null;
                _original = null;
                break;
            case Mode.Shape:
                if (_moved && document.Selection.Primary is SvgElement shape && _snapshot is not null)
                {
                    var after = NodeSnapshot.Capture(shape);
                    _snapshot.Restore(shape);
                    document.Actions.Edit(shape is SvgRect ? "Round Corners" : "Resize Shape", [shape], () => after.Restore(shape));
                }
                _snapshot = null;
                break;
            case Mode.Band:
                break;
        }
        document.NotifySelectionChanged();
    }

    public void Cancel(SvgDocument document)
    {
        if (_mode is Mode.Nodes or Mode.Handle && _path is not null && _snapshot is not null)
        {
            _snapshot.Restore(_path);
            _editable = _original;
        }
        _mode = Mode.None;
        _snapshot = null;
    }

    private void BeginEdit(SvgDocument document)
    {
        _snapshot = NodeSnapshot.Capture(_path!);
        _original = _editable!.Clone();
    }

    // Shows the edited nodes live: the path data is written without a history step.
    private void Preview(SvgDocument document, EditablePath work)
    {
        _editable = work;
        var before = SvgBounds.Visual(_path!, document.GlyphProvider);
        _path!.SetPath(work.ToPath(), 4);
        _data = _path.Data;
        var after = SvgBounds.Visual(_path, document.GlyphProvider);
        document.NotifyNodeChanged(_path, before is { } b && after is { } a ? b.Union(a) : before ?? after);
    }

    // ---- Hit testing ----

    private (NodeRef Node, bool IsOut)? HandleAt(VPoint p, double reach)
    {
        foreach (var n in _selected)
        {
            var node = _editable!.Figures[n.Figure].Nodes[n.Index];
            if (node.In is { } i && ToDocument(i).DistanceTo(p) <= reach)
                return (n, false);
            if (node.Out is { } o && ToDocument(o).DistanceTo(p) <= reach)
                return (n, true);
        }
        return null;
    }

    private NodeRef? NodeAt(VPoint p, double reach)
    {
        NodeRef? best = null;
        var distance = reach;
        for (var f = 0; f < _editable!.Figures.Count; f++)
            for (var i = 0; i < _editable.Figures[f].Nodes.Count; i++)
            {
                var d = ToDocument(_editable.Figures[f].Nodes[i].Point).DistanceTo(p);
                if (d <= distance)
                {
                    distance = d;
                    best = new NodeRef(f, i);
                }
            }
        return best;
    }

    private (int Figure, int Segment, double T)? SegmentAt(VPoint p, double reach)
    {
        (int, int, double)? best = null;
        var distance = reach;
        var local = ToLocal(p);
        var scale = Math.Max(_world.MeanScale, 1e-9);
        for (var f = 0; f < _editable!.Figures.Count; f++)
            if (EditablePath.Nearest(_editable.Figures[f], local) is { } near && near.Distance * scale <= distance)
            {
                distance = near.Distance * scale;
                best = (f, near.Segment, near.T);
            }
        return best;
    }

    // ---- Operations (the options bar) ----

    private bool CanEdit => _path is not null && _editable is not null;

    private void ToggleType(SvgDocument document, NodeRef node)
    {
        var type = _editable!.Figures[node.Figure].Nodes[node.Index].Type == NodeType.Corner ? NodeType.Smooth : NodeType.Corner;
        SetNodeType(document, type, [node]);
    }

    /// <summary>Sets the type of the selected nodes (or the given ones).</summary>
    public void SetNodeType(SvgDocument document, NodeType type, IEnumerable<NodeRef>? nodes = null)
    {
        var targets = (nodes ?? _selected).ToList();
        if (!CanEdit || targets.Count == 0)
            return;
        var work = _editable!.Clone();
        foreach (var n in targets)
        {
            var node = work.Figures[n.Figure].Nodes[n.Index];
            // A corner given a smooth type needs handles to be smooth: create them along the line to the neighbours.
            if (type != NodeType.Corner && (node.In is null || node.Out is null))
                CreateHandles(work.Figures[n.Figure], n.Index);
            node.SetType(type);
        }
        document.Actions.SetPathData(_path!, work.ToPath());
        _data = null;
        Sync(document);
        // The type lives only in the tool: keep it for the nodes just set.
        foreach (var n in targets)
            if (n.Figure < _editable!.Figures.Count && n.Index < _editable.Figures[n.Figure].Nodes.Count)
                _editable.Figures[n.Figure].Nodes[n.Index].Type = type;
    }

    private static void CreateHandles(EditableFigure figure, int index)
    {
        var count = figure.Nodes.Count;
        var node = figure.Nodes[index];
        var previous = figure.Closed || index > 0 ? figure.Nodes[(index + count - 1) % count] : null;
        var next = figure.Closed || index < count - 1 ? figure.Nodes[(index + 1) % count] : null;
        var direction = ((next?.Point ?? node.Point) - (previous?.Point ?? node.Point)).Normalized();
        if (direction == default)
            return;
        node.In ??= previous is null ? null : node.Point - direction * (node.Point.DistanceTo(previous.Point) / 3);
        node.Out ??= next is null ? null : node.Point + direction * (node.Point.DistanceTo(next.Point) / 3);
    }

    /// <summary>The segments between selected nodes become straight lines (or curves).</summary>
    public void SetSegments(SvgDocument document, bool line)
    {
        if (!CanEdit)
            return;
        var work = _editable!.Clone();
        var changed = false;
        for (var f = 0; f < work.Figures.Count; f++)
            for (var s = 0; s < work.Figures[f].SegmentCount; s++)
                if (_selected.Contains(new NodeRef(f, s)) && _selected.Contains(new NodeRef(f, (s + 1) % work.Figures[f].Nodes.Count)))
                {
                    EditablePath.SetSegmentKind(work.Figures[f], s, line);
                    changed = true;
                }
        if (!changed)
            return;
        document.Actions.SetPathData(_path!, work.ToPath());
        _data = null;
        Sync(document);
    }

    /// <summary>Removes the selected nodes, keeping the shape close.</summary>
    public void DeleteNodes(SvgDocument document)
    {
        if (!CanEdit || _selected.Count == 0)
            return;
        var work = _editable!.Clone();
        foreach (var n in _selected.OrderByDescending(n => n.Figure).ThenByDescending(n => n.Index))
            EditablePath.RemoveNode(work.Figures[n.Figure], n.Index);
        work.Figures.RemoveAll(f => f.Nodes.Count < 2);
        _selected.Clear();
        if (work.Figures.Count == 0)
        {
            var path = _path!;
            document.Selection.Set(path);
            document.Actions.Delete([path]);
            return;
        }
        document.Actions.SetPathData(_path!, work.ToPath());
        _data = null;
        Sync(document);
    }

    /// <summary>Breaks the path at the selected nodes (a closed figure opens, an open one splits).</summary>
    public void BreakNodes(SvgDocument document)
    {
        if (!CanEdit || _selected.Count == 0)
            return;
        var work = _editable!.Clone();
        foreach (var n in _selected.OrderByDescending(n => n.Figure).ThenByDescending(n => n.Index))
            work.BreakAt(work.Figures[n.Figure], n.Index);
        document.Actions.SetPathData(_path!, work.ToPath());
        _selected.Clear();
        _data = null;
        Sync(document);
    }

    /// <summary>Joins the two selected end nodes (of one figure, which closes, or of two, which merge).</summary>
    public bool JoinNodes(SvgDocument document)
    {
        if (!CanEdit || _selected.Count != 2)
            return false;
        var two = _selected.OrderBy(n => n.Figure).ThenBy(n => n.Index).ToList();
        var work = _editable!.Clone();
        EditableFigure a = work.Figures[two[0].Figure], b = work.Figures[two[1].Figure];
        bool IsEnd(EditableFigure f, int i) => i == 0 || i == f.Nodes.Count - 1;
        if (!IsEnd(a, two[0].Index) || !IsEnd(b, two[1].Index))
            return false;
        var atEndA = two[0].Index != 0 || a.Nodes.Count == 1;
        var atEndB = two[1].Index != 0 || b.Nodes.Count == 1;
        if (a == b)
        {
            atEndA = two[0].Index != 0;
            atEndB = two[1].Index != 0;
            // Both ends of the same figure: first with last.
            atEndA = false;
            atEndB = true;
        }
        if (!work.Join(a, atEndA, b, atEndB))
            return false;
        document.Actions.SetPathData(_path!, work.ToPath());
        _selected.Clear();
        _data = null;
        Sync(document);
        return true;
    }

    private void AddNodeAt(SvgDocument document, int figureIndex, int segment, double t)
    {
        var work = _editable!.Clone();
        var node = EditablePath.InsertNode(work.Figures[figureIndex], segment, t);
        document.Actions.SetPathData(_path!, work.ToPath());
        _data = null;
        Sync(document);
        _selected.Clear();
        _selected.Add(new NodeRef(figureIndex, segment + 1));
        _ = node;
        document.NotifySelectionChanged();
    }

    /// <summary>Turns the selected rectangle, circle, ellipse, line or polygon into a path and edits that.</summary>
    public void ConvertToPath(SvgDocument document)
    {
        if (document.Selection.Primary is SvgShape and not SvgPath)
            document.Actions.ObjectToPath([document.Selection.Primary]);
        Sync(document);
    }

    /// <summary>The nodes of a shape's outline, in document space, as the path they would become.</summary>
    private static List<VPoint> OutlineNodes(SvgShape shape)
    {
        var world = SvgBounds.ToDocument(shape);
        return EditablePath.From(shape.CreatePath()).Figures.SelectMany(f => f.Nodes).Select(n => world.Transform(n.Point)).ToList();
    }

    /// <summary>True when <paramref name="p"/> is on the outline of the shape: within a few screen pixels of one of its edges.</summary>
    private static bool OnOutline(SvgDocument document, SvgShape shape, VPoint p)
    {
        var reach = document.ScreenToUser(SegmentReach);
        var world = SvgBounds.ToDocument(shape);
        foreach (var polyline in Flattener.Flatten(shape.CreatePath(), world, document.ScreenToUser(0.5)))
        {
            var points = polyline.Points;
            for (var i = 0; i + 1 < points.Count || (polyline.Closed && i < points.Count); i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var ab = b - a;
                var length2 = ab.Dot(ab);
                var t = length2 <= 0 ? 0 : Math.Clamp((p - a).Dot(ab) / length2, 0, 1);
                if (p.DistanceTo(a + ab * t) <= reach)
                    return true;
            }
        }
        return false;
    }

    public bool CanConvertToPath(SvgDocument document) => document.Selection.Primary is SvgShape and not SvgPath;

    public void SelectAllNodes(SvgDocument document)
    {
        Sync(document);
        if (_editable is null)
            return;
        _selected.Clear();
        for (var f = 0; f < _editable.Figures.Count; f++)
            for (var i = 0; i < _editable.Figures[f].Nodes.Count; i++)
                _selected.Add(new NodeRef(f, i));
        document.NotifySelectionChanged();
    }

    // ---- Shape handles ----

    private (VPoint Radius1, VPoint Radius2)? ShapePoints(SvgElement shape, double inset)
    {
        var world = SvgBounds.ToDocument(shape);
        switch (shape)
        {
            case SvgRect rect:
            {
                var (rx, ry) = rect.EffectiveRadii;
                // The handles sit at least `inset` inside the corners, so the corners themselves stay free to edit as points.
                return (world.Transform(new VPoint(rect.X + rect.Width - Math.Min(Math.Max(rx, inset), rect.Width / 2), rect.Y)),
                    world.Transform(new VPoint(rect.X, rect.Y + Math.Min(Math.Max(ry, inset), rect.Height / 2))));
            }
            case SvgEllipse ellipse:
            {
                var rx = ellipse.HasAttribute("rx") ? ellipse.Rx : ellipse.Ry;
                var ry = ellipse.HasAttribute("ry") ? ellipse.Ry : ellipse.Rx;
                return (world.Transform(new VPoint(ellipse.Cx + rx, ellipse.Cy)), world.Transform(new VPoint(ellipse.Cx, ellipse.Cy + ry)));
            }
            case SvgCircle circle:
                return (world.Transform(new VPoint(circle.Cx + circle.R, circle.Cy)), world.Transform(new VPoint(circle.Cx, circle.Cy)));
            default:
                return null;
        }
    }

    private int? ShapeHandleAt(SvgDocument document, VPoint p, double reach)
    {
        if (document.Selection.Primary is not { } shape || shape is SvgPath || ShapePoints(shape, document.ScreenToUser(HandleInset)) is not { } points)
            return null;
        if (shape is SvgCircle)
            return p.DistanceTo(points.Radius1) <= reach ? 0 : null;
        if (p.DistanceTo(points.Radius1) <= reach)
            return 0;
        return p.DistanceTo(points.Radius2) <= reach ? 1 : null;
    }

    private void DragShapeHandle(SvgDocument document, VPoint p)
    {
        if (document.Selection.Primary is not SvgElement shape)
            return;
        var local = (SvgBounds.ToDocument(shape).Invert() ?? Matrix2D.Identity).Transform(p);
        var before = SvgBounds.Visual(shape, document.GlyphProvider);
        switch (shape)
        {
            case SvgRect rect:
                if (_shapeHandle == 0)
                    rect.Rx = Math.Round(Math.Clamp(rect.X + rect.Width - local.X, 0, rect.Width / 2), 4);
                else
                    rect.Ry = Math.Round(Math.Clamp(local.Y - rect.Y, 0, rect.Height / 2), 4);
                if (!rect.HasAttribute("ry") && _shapeHandle == 0)
                    rect.Ry = rect.Rx;
                break;
            case SvgEllipse ellipse:
                if (_shapeHandle == 0)
                    ellipse.Rx = Math.Round(Math.Max(Math.Abs(local.X - ellipse.Cx), 0.01), 4);
                else
                    ellipse.Ry = Math.Round(Math.Max(Math.Abs(local.Y - ellipse.Cy), 0.01), 4);
                break;
            case SvgCircle circle:
                circle.R = Math.Round(Math.Max(local.DistanceTo(new VPoint(circle.Cx, circle.Cy)), 0.01), 4);
                break;
        }
        var after = SvgBounds.Visual(shape, document.GlyphProvider);
        document.NotifyNodeChanged(shape, before is { } b && after is { } a ? b.Union(a) : before ?? after);
    }

    // ---- Keys ----

    public bool OnKeyDown(SvgDocument document, ToolKey key, ToolModifiers modifiers)
    {
        Sync(document);
        switch (key)
        {
            case ToolKey.Delete or ToolKey.Backspace when _selected.Count > 0:
                DeleteNodes(document);
                return true;
            case ToolKey.Escape when _selected.Count > 0:
                _selected.Clear();
                document.NotifySelectionChanged();
                return true;
            case ToolKey.Left or ToolKey.Right or ToolKey.Up or ToolKey.Down when _selected.Count > 0 && CanEdit:
            {
                var step = modifiers.HasFlag(ToolModifiers.Shift) ? 10 : 1;
                var delta = key switch
                {
                    ToolKey.Left => new VVector(-step, 0),
                    ToolKey.Right => new VVector(step, 0),
                    ToolKey.Up => new VVector(0, -step),
                    _ => new VVector(0, step),
                };
                var work = _editable!.Clone();
                var local = ToLocalVector(delta);
                foreach (var n in _selected)
                    work.Figures[n.Figure].Nodes[n.Index].MoveBy(local);
                document.Actions.SetPathData(_path!, work.ToPath());
                _data = null;
                Sync(document);
                return true;
            }
            default:
                return false;
        }
    }

    // ---- Overlay and cursor ----

    public ToolOverlay? GetOverlay(SvgDocument document)
    {
        Sync(document);
        var to = document.UserToImage;
        PointD I(VPoint documentPoint) => to.Transform(documentPoint).ToCore();

        if (_mode == Mode.Band)
        {
            var band = to.TransformBounds(_band);
            var nodeHandles = NodeHandles(I);
            return new ToolOverlay { Frame = new RectangleD(band.X, band.Y, band.Width, band.Height), Handles = nodeHandles.Handles, Highlights = nodeHandles.Highlights, Lines = nodeHandles.Lines, SquareHandles = true };
        }
        if (_path is not null && _editable is not null)
        {
            var overlay = NodeHandles(I);
            return new ToolOverlay { Handles = overlay.Handles, Lines = overlay.Lines, Highlights = overlay.Highlights, SquareHandles = true };
        }
        if (document.Selection.Primary is SvgShape outlined)
        {
            // The points that can be edited: press one (or an edge) and the shape becomes a path.
            var markers = OutlineNodes(outlined).Select(n => I(n)).Select(c => new RectangleD(c.X - 4, c.Y - 4, 8, 8)).ToList();
            var handles = new List<PointD>();
            if (ShapePoints(outlined, document.ScreenToUser(HandleInset)) is { } points)
            {
                handles.Add(I(points.Radius1));
                if (outlined is not SvgCircle)
                    handles.Add(I(points.Radius2));
            }
            return new ToolOverlay { Handles = handles, Highlights = markers, SquareHandles = false };
        }
        return null;
    }

    private (List<PointD> Handles, List<(PointD, PointD)> Lines, List<RectangleD> Highlights) NodeHandles(Func<VPoint, PointD> toImage)
    {
        var handles = new List<PointD>();
        var lines = new List<(PointD, PointD)>();
        var highlights = new List<RectangleD>();
        for (var f = 0; f < _editable!.Figures.Count; f++)
        {
            var figure = _editable.Figures[f];
            for (var i = 0; i < figure.Nodes.Count; i++)
            {
                var node = figure.Nodes[i];
                var p = toImage(ToDocument(node.Point));
                handles.Add(p);
                if (_selected.Contains(new NodeRef(f, i)))
                {
                    highlights.Add(new RectangleD(p.X - 5, p.Y - 5, 10, 10));
                    foreach (var h in new[] { node.In, node.Out })
                        if (h is { } handle)
                        {
                            var hp = toImage(ToDocument(handle));
                            handles.Add(hp);
                            lines.Add((p, hp));
                        }
                }
            }
        }
        return (handles, lines, highlights);
    }

    public ToolCursor CursorAt(SvgDocument document, PointD userPoint)
    {
        Sync(document);
        var p = userPoint.ToVector();
        var reach = document.ScreenToUser(Reach);
        if (_editable is null)
        {
            if (ShapeHandleAt(document, p, reach) is not null)
                return ToolCursor.Move;
            return document.Selection.Primary is SvgShape outlined && OnOutline(document, outlined, p) ? ToolCursor.Move : ToolCursor.Default;
        }
        return HandleAt(p, reach) is not null || NodeAt(p, reach) is not null ? ToolCursor.Move : ToolCursor.Default;
    }
}
