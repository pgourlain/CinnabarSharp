using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>
/// Select tool (S / F1). Click picks the top-most object (Alt+click the one below, Ctrl+click enters groups, Shift+click
/// toggles), a drag on empty space is a rubber band, a drag on an object moves the selection (snapping to the page and to
/// other objects), the eight handles resize (Shift keeps the ratio, Alt works from the center). Clicking an already selected
/// object again switches the handles to rotate (corners) and skew (edge middles), with a movable rotation center.
/// Arrow keys move by 1 unit, with Shift by 10; Delete removes; Escape deselects. Each gesture is one history step.
/// </summary>
public sealed class VectorSelectTool(ToolSettings settings) : IVectorKeyboardTool
{
    private enum Mode { None, Pending, Move, Band, Resize, Rotate, Skew, MoveCenter }

    private const double HandleReach = 7;     // screen pixels
    private const double DragThreshold = 3;   // screen pixels
    private const double SnapReach = 6;       // screen pixels

    private Mode _mode;
    private VPoint _start;
    private List<SvgElement> _before = [];
    private List<(SvgElement Node, NodeSnapshot Snapshot)> _snapshots = [];
    private VRect _box;                       // selection box at the start of a gesture, document space
    private int _handle;
    private bool _clickedSelected;
    private bool _rotateMode;
    private VPoint? _center;
    private VRect _band;
    private List<SvgElement> _bandBase = [];
    private List<VRect> _snapTargets = [];
    private Matrix2D _applied = Matrix2D.Identity;

    public string Name => "Select";

    /// <summary>The handles show rotate and skew grips instead of resize ones.</summary>
    public bool RotateMode => _rotateMode;

    public bool IsEditing(SvgDocument document) => false;

    public void Finish(SvgDocument document) => Cancel(document);

    public void Refresh(SvgDocument document)
    {
    }

    // ---- Pointer ----

    public void OnPointerDown(SvgDocument document, ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left)
            return;
        var p = pointer.Position.ToVector();
        _start = p;
        _before = document.Selection.Nodes.ToList();
        _clickedSelected = false;
        _moved = false;
        _applied = Matrix2D.Identity;
        var reach = document.ScreenToUser(HandleReach);

        if (!document.Selection.IsEmpty && document.Actions.BoundsOf() is { } box)
        {
            _box = box;
            if (_rotateMode && CenterPoint(box) is var center && p.DistanceTo(center) <= reach)
            {
                _mode = Mode.MoveCenter;
                return;
            }
            if (HandleAt(box, p, reach) is { } handle)
            {
                _handle = handle;
                BeginTransform(document);
                _mode = !_rotateMode ? Mode.Resize : IsCorner(handle) ? Mode.Rotate : Mode.Skew;
                return;
            }
        }

        var tolerance = document.ScreenToUser(3);
        var below = pointer.Modifiers.HasFlag(ToolModifiers.Alt) ? document.Selection.Primary : null;
        var hit = SvgHitTester.HitTest(document, p, tolerance, pointer.Modifiers.HasFlag(ToolModifiers.Command), below);
        if (hit is not null)
        {
            if (pointer.Modifiers.HasFlag(ToolModifiers.Shift))
            {
                document.Selection.Toggle(hit);
                _mode = Mode.None;
                return;
            }
            if (document.Selection.Contains(hit))
                _clickedSelected = true;
            else
                document.Selection.Set(hit);
            _mode = Mode.Pending;
            return;
        }

        // Empty space: a rubber band; without Shift it replaces the selection.
        _bandBase = pointer.Modifiers.HasFlag(ToolModifiers.Shift) ? [.. _before] : [];
        if (!pointer.Modifiers.HasFlag(ToolModifiers.Shift))
            document.Selection.Clear();
        _band = new VRect(p.X, p.Y, 0, 0);
        _mode = Mode.Band;
    }

    private bool _moved;

    public void OnPointerMove(SvgDocument document, ToolPointer pointer)
    {
        var p = pointer.Position.ToVector();
        switch (_mode)
        {
            case Mode.Pending:
                if (p.DistanceTo(_start) > document.ScreenToUser(DragThreshold))
                {
                    BeginTransform(document);
                    _box = document.Actions.BoundsOf() ?? default;
                    _snapTargets = SnapTargets(document);
                    _mode = Mode.Move;
                    goto case Mode.Move;
                }
                break;
            case Mode.Move:
            {
                var delta = Snap(document, p - _start, pointer.Modifiers);
                Preview(document, Matrix2D.Translate(delta.X, delta.Y));
                break;
            }
            case Mode.Band:
                _band = VRect.FromPoints(_start, p);
                var inside = SvgHitTester.InRect(document, _band, touching: pointer.Modifiers.HasFlag(ToolModifiers.Alt));
                document.Selection.Set(_bandBase.Concat(inside));
                break;
            case Mode.Resize:
                if (settings.SnapToGrid && !pointer.Modifiers.HasFlag(ToolModifiers.Alt))
                {
                    var origin = GridOrigin(document);
                    p = new VPoint(GridSnapping.Snap(p.X, settings.GridSize, origin.X), GridSnapping.Snap(p.Y, settings.GridSize, origin.Y));
                }
                Preview(document, ResizeMatrix(p, pointer.Modifiers));
                break;
            case Mode.Rotate:
                Preview(document, RotateMatrix(p, pointer.Modifiers));
                break;
            case Mode.Skew:
                Preview(document, SkewMatrix(p));
                break;
            case Mode.MoveCenter:
                _center = p;
                document.Workspace.Invalidate(RectangleI.Zero);
                break;
        }
    }

    public void OnPointerUp(SvgDocument document, ToolPointer pointer)
    {
        var mode = _mode;
        _mode = Mode.None;
        switch (mode)
        {
            case Mode.Pending:
                // A click on an object that was already selected: rotate handles on and off.
                if (_clickedSelected)
                {
                    _rotateMode = !_rotateMode;
                    _center = null;
                    document.NotifySelectionChanged();
                }
                break;
            case Mode.Move:
            case Mode.Resize:
            case Mode.Rotate:
            case Mode.Skew:
                // One step for the gesture, selecting included: undo goes back to the selection from before it.
                if (Commit(document, mode))
                {
                    _before = [.. document.Selection.Nodes];
                    if (document.Selection.IsEmpty)
                        _rotateMode = false;
                    return;
                }
                break;
            case Mode.MoveCenter:
                break;
        }
        document.Actions.RecordSelectionChange(_before, "Select");
        _before = [.. document.Selection.Nodes];
        if (document.Selection.IsEmpty)
            _rotateMode = false;
    }

    /// <summary>Abandons a gesture in progress: the document is as it was.</summary>
    public void Cancel(SvgDocument document)
    {
        if (_mode is Mode.Move or Mode.Resize or Mode.Rotate or Mode.Skew)
            Revert(document);
        _mode = Mode.None;
    }

    // ---- Gestures ----

    private void BeginTransform(SvgDocument document)
    {
        var targets = TopLevel(document);
        _snapshots = targets.Select(n => (n, NodeSnapshot.Capture(n))).ToList();
        _box = document.Actions.BoundsOf() ?? _box;
    }

    private static List<SvgElement> TopLevel(SvgDocument document)
    {
        var set = document.Selection.Nodes.ToHashSet();
        return document.Selection.Nodes.Where(n => !n.Ancestors().Any(set.Contains)).ToList();
    }

    private void Revert(SvgDocument document)
    {
        foreach (var (node, snapshot) in _snapshots)
        {
            var before = SvgBounds.Visual(node, document.GlyphProvider);
            snapshot.Restore(node);
            document.NotifyNodeChanged(node, before is { } b && SvgBounds.Visual(node, document.GlyphProvider) is { } a ? b.Union(a) : before);
        }
    }

    // Applies the total transformation to the snapshots: no history, no accumulation of rounding.
    private void Preview(SvgDocument document, Matrix2D matrix)
    {
        _applied = matrix;
        _moved = true;
        foreach (var (node, snapshot) in _snapshots)
        {
            var before = SvgBounds.Visual(node, document.GlyphProvider);
            snapshot.Restore(node);
            SvgTransformer.Apply(node, SvgActions.ToParentSpace(node, matrix), document.GlyphProvider);
            var after = SvgBounds.Visual(node, document.GlyphProvider);
            document.NotifyNodeChanged(node, before is { } b && after is { } a ? b.Union(a) : before ?? after);
        }
    }

    private bool Commit(SvgDocument document, Mode mode)
    {
        var matrix = _applied;
        Revert(document);
        _snapshots = [];
        if (!_moved || matrix.IsIdentity)
            return false;
        var name = mode switch { Mode.Move => "Move", Mode.Resize => "Resize", Mode.Rotate => "Rotate", _ => "Skew" };
        document.Actions.Transform(document.Selection.Nodes, matrix, name, _before);
        return true;
    }

    // ---- Matrices ----

    private Matrix2D ResizeMatrix(VPoint p, ToolModifiers modifiers)
    {
        var box = _box;
        var handle = HandlePoint(box, _handle);
        var center = box.Center;
        var fromCenter = modifiers.HasFlag(ToolModifiers.Alt);
        // The point that stays where it is: the opposite corner or edge middle, or the center with Alt.
        var anchor = fromCenter ? center : HandlePoint(box, (_handle + 4) % 8);
        var horizontalEdge = _handle is 1 or 5;     // top/bottom: only the height changes
        var verticalEdge = _handle is 3 or 7;       // left/right: only the width changes
        var sx = verticalEdge || IsCorner(_handle) ? Factor(p.X - anchor.X, handle.X - anchor.X) : 1;
        var sy = horizontalEdge || IsCorner(_handle) ? Factor(p.Y - anchor.Y, handle.Y - anchor.Y) : 1;
        if (modifiers.HasFlag(ToolModifiers.Shift))
        {
            // Keep the ratio: both axes by the larger change (an edge handle drives the other axis too).
            var s = horizontalEdge ? Math.Abs(sy) : verticalEdge ? Math.Abs(sx) : Math.Max(Math.Abs(sx), Math.Abs(sy));
            sx = (sx < 0 ? -1 : 1) * s;
            sy = (sy < 0 ? -1 : 1) * s;
            if (horizontalEdge)
                anchor = new VPoint(center.X, anchor.Y);
            else if (verticalEdge)
                anchor = new VPoint(anchor.X, center.Y);
        }
        return Matrix2D.Translate(anchor.X, anchor.Y) * Matrix2D.Scale(sx, sy) * Matrix2D.Translate(-anchor.X, -anchor.Y);
    }

    // Distance ratio, never zero (a collapsed box could not be scaled back) and keeping the side: dragging past the anchor flips.
    private static double Factor(double now, double original)
    {
        if (Math.Abs(original) < 1e-9)
            return 1;
        var f = now / original;
        return Math.Abs(f) < 1e-3 ? (f < 0 ? -1e-3 : 1e-3) : f;
    }

    private Matrix2D RotateMatrix(VPoint p, ToolModifiers modifiers)
    {
        var c = _center ?? _box.Center;
        var a0 = Math.Atan2(_start.Y - c.Y, _start.X - c.X);
        var a1 = Math.Atan2(p.Y - c.Y, p.X - c.X);
        var degrees = (a1 - a0) * 180 / Math.PI;
        if (modifiers.HasFlag(ToolModifiers.Shift))
            degrees = Math.Round(degrees / 15) * 15;
        return Matrix2D.Rotate(degrees, c.X, c.Y);
    }

    private Matrix2D SkewMatrix(VPoint p)
    {
        var c = _center ?? _box.Center;
        var handle = HandlePoint(_box, _handle);
        if (_handle is 1 or 5)
        {
            var distance = handle.Y - c.Y;
            if (Math.Abs(distance) < 1e-9)
                return Matrix2D.Identity;
            var t = (p.X - _start.X) / distance;
            return new Matrix2D(1, 0, t, 1, -t * c.Y, 0);
        }
        var run = handle.X - c.X;
        if (Math.Abs(run) < 1e-9)
            return Matrix2D.Identity;
        var u = (p.Y - _start.Y) / run;
        return new Matrix2D(1, u, 0, 1, 0, -u * c.X);
    }

    // ---- Snapping ----

    private List<VRect> SnapTargets(SvgDocument document)
    {
        var selected = document.Selection.Nodes.ToHashSet();
        var result = new List<VRect>();
        if (!settings.SnapToObjects)
            return result;
        var (pageWidth, pageHeight) = document.Root.UserSize;
        var origin = document.Root.ViewBox is { } vb ? new VPoint(vb.X, vb.Y) : default;
        result.Add(new VRect(origin.X, origin.Y, pageWidth, pageHeight));
        foreach (var obj in SvgHitTester.SelectableObjects(document))
            if (!selected.Contains(obj) && !obj.Ancestors().Any(selected.Contains) && SvgBounds.InDocument(obj) is { } box)
                result.Add(box);
        return result;
    }

    private VVector Snap(SvgDocument document, VVector delta, ToolModifiers modifiers)
    {
        if (modifiers.HasFlag(ToolModifiers.Alt))
            return delta;
        var byObjects = SnapToObjects(document, delta);
        // Objects win where they snap; then the guides; otherwise the edges of the moved box go to the grid.
        if (byObjects != delta)
            return byObjects;
        var byGuides = SnapToGuides(document, delta);
        if (byGuides != delta || !settings.SnapToGrid)
            return byGuides;
        var origin = GridOrigin(document);
        var reach = Math.Min(document.ScreenToUser(SnapReach), settings.GridSize / 2);
        var moved = _box.Offset(delta.X, delta.Y);
        var dx = GridSnapping.ShiftToGrid([moved.Left, moved.Right], settings.GridSize, origin.X, reach);
        var dy = GridSnapping.ShiftToGrid([moved.Top, moved.Bottom], settings.GridSize, origin.Y, reach);
        return new VVector(delta.X + dx, delta.Y + dy);
    }

    /// <summary>Moves the edges and the middle of the moved box onto a guide line that is within reach.</summary>
    private VVector SnapToGuides(SvgDocument document, VVector delta)
    {
        var guides = document.Workspace.Guides;
        if (!settings.SnapToGuides || guides.Count == 0)
            return delta;
        var reach = document.ScreenToUser(SnapReach);
        var moved = _box.Offset(delta.X, delta.Y);
        double Shift(IEnumerable<double> guideLines, double[] mine)
        {
            var best = double.NaN;
            var distance = reach;
            foreach (var line in guideLines)
                foreach (var m in mine)
                    if (Math.Abs(line - m) < distance)
                    {
                        distance = Math.Abs(line - m);
                        best = line - m;
                    }
            return double.IsNaN(best) ? 0 : best;
        }
        var xs = guides.Items.Where(g => g.Orientation == GuideOrientation.Vertical).Select(g => document.ImageToUserPoint(new VPoint(g.Position, 0)).X);
        var ys = guides.Items.Where(g => g.Orientation == GuideOrientation.Horizontal).Select(g => document.ImageToUserPoint(new VPoint(0, g.Position)).Y);
        return new VVector(delta.X + Shift(xs, [moved.Left, moved.Left + moved.Width / 2, moved.Right]),
            delta.Y + Shift(ys, [moved.Top, moved.Top + moved.Height / 2, moved.Bottom]));
    }

    /// <summary>The page's top-left corner in user space: where the grid lines start.</summary>
    internal static VPoint GridOrigin(SvgDocument document) =>
        document.Root.ViewBox is { } vb ? new VPoint(vb.X, vb.Y) : default;

    private VVector SnapToObjects(SvgDocument document, VVector delta)
    {
        if (!settings.SnapToObjects || _snapTargets.Count == 0)
            return delta;
        var reach = document.ScreenToUser(SnapReach);
        double Best(double[] mine, IEnumerable<double> targets)
        {
            var best = double.NaN;
            var distance = reach;
            foreach (var m in mine)
                foreach (var t in targets)
                    if (Math.Abs(t - m) < distance)
                    {
                        distance = Math.Abs(t - m);
                        best = t - m;
                    }
            return best;
        }
        var moved = _box.Offset(delta.X, delta.Y);
        var xs = _snapTargets.SelectMany(t => new[] { t.Left, t.Left + t.Width / 2, t.Right });
        var ys = _snapTargets.SelectMany(t => new[] { t.Top, t.Top + t.Height / 2, t.Bottom });
        var sx = Best([moved.Left, moved.Left + moved.Width / 2, moved.Right], xs);
        var sy = Best([moved.Top, moved.Top + moved.Height / 2, moved.Bottom], ys);
        return new VVector(delta.X + (double.IsNaN(sx) ? 0 : sx), delta.Y + (double.IsNaN(sy) ? 0 : sy));
    }

    // ---- Handles ----

    private static bool IsCorner(int handle) => handle is 0 or 2 or 4 or 6;

    private static VPoint HandlePoint(VRect box, int index) => index switch
    {
        0 => new VPoint(box.Left, box.Top),
        1 => new VPoint(box.Left + box.Width / 2, box.Top),
        2 => new VPoint(box.Right, box.Top),
        3 => new VPoint(box.Right, box.Top + box.Height / 2),
        4 => new VPoint(box.Right, box.Bottom),
        5 => new VPoint(box.Left + box.Width / 2, box.Bottom),
        6 => new VPoint(box.Left, box.Bottom),
        _ => new VPoint(box.Left, box.Top + box.Height / 2),
    };

    private int? HandleAt(VRect box, VPoint p, double reach)
    {
        for (var i = 0; i < 8; i++)
            if (p.DistanceTo(HandlePoint(box, i)) <= reach)
                return i;
        return null;
    }

    private VPoint CenterPoint(VRect box) => _center ?? box.Center;

    // ---- Keys ----

    public bool OnKeyDown(SvgDocument document, ToolKey key, ToolModifiers modifiers)
    {
        if (document.Selection.IsEmpty)
            return false;
        var step = modifiers.HasFlag(ToolModifiers.Shift) ? 10 : 1;
        switch (key)
        {
            case ToolKey.Left: document.Actions.MoveBy(null, -step, 0); return true;
            case ToolKey.Right: document.Actions.MoveBy(null, step, 0); return true;
            case ToolKey.Up: document.Actions.MoveBy(null, 0, -step); return true;
            case ToolKey.Down: document.Actions.MoveBy(null, 0, step); return true;
            case ToolKey.Delete:
            case ToolKey.Backspace:
                document.Actions.DeleteSelection();
                return true;
            case ToolKey.Escape:
                if (_mode is Mode.Move or Mode.Resize or Mode.Rotate or Mode.Skew)
                {
                    Cancel(document);
                    return true;
                }
                _rotateMode = false;
                document.Selection.Clear();
                return true;
            default:
                return false;
        }
    }

    // ---- Overlay and cursors ----

    public ToolOverlay? GetOverlay(SvgDocument document)
    {
        var to = document.UserToImage;
        if (_mode == Mode.Band)
        {
            var band = to.TransformBounds(_band);
            return new ToolOverlay { Frame = new RectangleD(band.X, band.Y, band.Width, band.Height) };
        }
        if (document.Selection.IsEmpty || document.Actions.BoundsOf() is not { } box)
            return null;
        var frame = to.TransformBounds(box);
        var handles = Enumerable.Range(0, 8).Select(i => to.Transform(HandlePoint(box, i)).ToCore()).ToList();
        if (!_rotateMode)
        {
            return new ToolOverlay
            {
                Frame = new RectangleD(frame.X, frame.Y, frame.Width, frame.Height),
                Handles = handles,
                SquareHandles = true,
            };
        }
        var center = to.Transform(CenterPoint(box));
        const double arm = 5;
        return new ToolOverlay
        {
            Frame = new RectangleD(frame.X, frame.Y, frame.Width, frame.Height),
            Handles = handles,
            SquareHandles = false,
            Lines =
            [
                (new PointD(center.X - arm, center.Y), new PointD(center.X + arm, center.Y)),
                (new PointD(center.X, center.Y - arm), new PointD(center.X, center.Y + arm)),
            ],
        };
    }

    public ToolCursor CursorAt(SvgDocument document, PointD userPoint)
    {
        if (document.Selection.IsEmpty || document.Actions.BoundsOf() is not { } box)
            return ToolCursor.Default;
        var p = userPoint.ToVector();
        var reach = document.ScreenToUser(HandleReach);
        if (_rotateMode && p.DistanceTo(CenterPoint(box)) <= reach)
            return ToolCursor.Move;
        if (HandleAt(box, p, reach) is { } handle)
        {
            if (_rotateMode)
                return IsCorner(handle) ? ToolCursor.Rotate : handle is 1 or 5 ? ToolCursor.ResizeHorizontal : ToolCursor.ResizeVertical;
            return handle switch
            {
                0 or 4 => ToolCursor.ResizeDiagonal,
                2 or 6 => ToolCursor.ResizeAntiDiagonal,
                1 or 5 => ToolCursor.ResizeVertical,
                _ => ToolCursor.ResizeHorizontal,
            };
        }
        return SvgHitTester.HitTest(document, p, document.ScreenToUser(3)) is { } hit && document.Selection.Contains(hit)
            ? ToolCursor.Move
            : ToolCursor.Default;
    }
}
