using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>
/// Gradient tool (G). A drag on the selected objects gives each its own linear (or, by the option, radial) gradient on its fill (or
/// stroke): start at the press, end at the release; it is shown live. An object that already has a gradient shows its handles
/// (start and end, or center, radius and focus) and the stops along the line: drag a handle or a stop, double click on the line
/// adds a stop, Delete removes the selected stop. The stops are the same ones the Properties panel edits.
/// </summary>
public sealed class VectorGradientTool(ToolSettings settings) : IVectorKeyboardTool, IGridSnappingTool
{
    private enum Mode { None, Create, Handle, Stop }

    private const double Reach = 7;       // screen pixels
    private const double LineReach = 5;

    private Mode _mode;
    private VPoint _start;
    private int _index;
    private int _selectedStop = -1;
    private bool _moved;
    private List<SvgElement> _targets = [];
    private List<SvgElement> _before = [];

    // Live creation: the gradients added quietly and the elements they were applied to.
    private readonly List<(SvgElement Node, NodeSnapshot Snapshot, SvgGradient Gradient)> _preview = [];
    private SvgDefs? _quietDefs;

    // Live editing of an existing gradient.
    private SvgGradient? _editing;
    private NodeSnapshot? _gradientSnapshot;
    private List<(SvgStop Stop, NodeSnapshot Snapshot)> _stopSnapshots = [];

    public string Name => "Gradient";

    public bool IsEditing(SvgDocument document) => false;

    public void Finish(SvgDocument document) => Cancel(document);

    public void Refresh(SvgDocument document)
    {
    }

    private SvgGradientKind Kind => settings.GradientKind == GradientKind.Radial ? SvgGradientKind.Radial : SvgGradientKind.Linear;

    // ---- Handles of the existing gradient ----

    private sealed record Handles(SvgElement Node, SvgGradient Gradient, Matrix2D ToDocument, List<VPoint> Geometry, List<VPoint> StopPoints,
        List<SvgStop> Stops, VPoint AxisStart, VPoint AxisEnd);

    private Handles? HandlesOf(SvgDocument document)
    {
        if (document.Selection.Primary is not { } node)
            return null;
        var style = StyleResolver.ComputeFor(node);
        var paint = settings.GradientOnStroke ? style.Stroke : style.Fill;
        if (paint.Kind != PaintKind.Url || document.Root.FindById(paint.Id) is not SvgGradient gradient)
            return null;
        var matrix = SvgBounds.ToDocument(node);
        if (gradient.Units == GradientUnits.ObjectBoundingBox)
        {
            if (SvgBounds.Object(node, document.GlyphProvider) is not { } box || box.Width <= 0 || box.Height <= 0)
                return null;
            matrix *= Matrix2D.Translate(box.X, box.Y) * Matrix2D.Scale(box.Width, box.Height);
        }
        matrix *= gradient.GradientTransform;
        List<VPoint> geometry;
        VPoint axisStart, axisEnd;
        switch (gradient)
        {
            case SvgLinearGradient linear:
                axisStart = matrix.Transform(new VPoint(linear.X1, linear.Y1));
                axisEnd = matrix.Transform(new VPoint(linear.X2, linear.Y2));
                geometry = [axisStart, axisEnd];
                break;
            case SvgRadialGradient radial:
                axisStart = matrix.Transform(new VPoint(radial.Cx, radial.Cy));
                axisEnd = matrix.Transform(new VPoint(radial.Cx + radial.R, radial.Cy));
                geometry = [axisStart, axisEnd, matrix.Transform(new VPoint(radial.Fx, radial.Fy))];
                break;
            default:
                return null;
        }
        var stops = gradient.ResolvedStops().ToList();
        var points = stops.Select(s => axisStart.Lerp(axisEnd, s.Offset)).ToList();
        return new Handles(node, gradient, matrix, geometry, points, stops, axisStart, axisEnd);
    }

    // ---- Pointer ----

    public void OnPointerDown(SvgDocument document, ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left)
            return;
        _start = pointer.Position.ToVector();
        _moved = false;
        _before = [.. document.Selection.Nodes];
        var reach = document.ScreenToUser(Reach);
        if (HandlesOf(document) is { } handles)
        {
            for (var i = 0; i < handles.Geometry.Count; i++)
                if (handles.Geometry[i].DistanceTo(_start) <= reach)
                {
                    BeginEditing(handles);
                    _index = i;
                    _mode = Mode.Handle;
                    return;
                }
            for (var i = 0; i < handles.StopPoints.Count; i++)
                if (handles.StopPoints[i].DistanceTo(_start) <= reach)
                {
                    BeginEditing(handles);
                    _index = i;
                    _selectedStop = i;
                    _mode = Mode.Stop;
                    document.NotifySelectionChanged();
                    return;
                }
            if (pointer.ClickCount >= 2 && DistanceToSegment(_start, handles.AxisStart, handles.AxisEnd) <= document.ScreenToUser(LineReach))
            {
                AddStopAt(document, handles, _start);
                return;
            }
        }
        _targets = document.Selection.Nodes.ToList();
        if (_targets.Count > 0)
            _mode = Mode.Create;
    }

    public void OnPointerMove(SvgDocument document, ToolPointer pointer)
    {
        var p = pointer.Position.ToVector();
        switch (_mode)
        {
            case Mode.Create:
                if (!_moved && p.DistanceTo(_start) < document.ScreenToUser(3))
                    return;
                _moved = true;
                PreviewCreate(document, p, pointer.Modifiers);
                break;
            case Mode.Handle:
                _moved = true;
                DragHandle(document, p);
                break;
            case Mode.Stop:
                _moved = true;
                DragStop(document, p);
                break;
        }
    }

    public void OnPointerUp(SvgDocument document, ToolPointer pointer)
    {
        var mode = _mode;
        _mode = Mode.None;
        var p = pointer.Position.ToVector();
        switch (mode)
        {
            case Mode.Create when _moved:
            {
                var end = Constrain(_start, p, pointer.Modifiers);
                RevertCreate(document);
                var nodes = _targets;
                _targets = [];
                document.Selection.Set(nodes);
                document.Actions.ApplyGradient(nodes, Kind, settings.GradientOnStroke, _start, end, Stops());
                break;
            }
            case Mode.Handle when _moved && _editing is { } gradient && _gradientSnapshot is { } snapshot:
            {
                var values = CurrentGeometry(gradient);
                snapshot.Restore(gradient);
                document.Actions.SetGradientGeometry(gradient, values);
                break;
            }
            case Mode.Stop when _moved && _editing is { } owner:
            {
                var stops = owner.ResolvedStops().Select(s => new GradientStop(s.Offset, s.Color)).ToList();
                foreach (var (stop, snapshot) in _stopSnapshots)
                    snapshot.Restore(stop);
                document.Actions.SetStops(OwnerOfStops(owner), stops);
                break;
            }
            default:
                break;
        }
        _editing = null;
        _gradientSnapshot = null;
        _stopSnapshots = [];
        document.NotifySelectionChanged();
    }

    public void Cancel(SvgDocument document)
    {
        if (_mode == Mode.Create)
            RevertCreate(document);
        else if (_mode is Mode.Handle or Mode.Stop)
        {
            if (_gradientSnapshot is not null && _editing is not null)
                _gradientSnapshot.Restore(_editing);
            foreach (var (stop, snapshot) in _stopSnapshots)
                snapshot.Restore(stop);
        }
        _mode = Mode.None;
        _editing = null;
    }

    private IReadOnlyList<GradientStop>? Stops() => settings.GradientTransparency
        ? null
        : [new GradientStop(0, settings.PrimaryColor.ToVector()), new GradientStop(1, settings.SecondaryColor.ToVector())];

    private static VPoint Constrain(VPoint start, VPoint p, ToolModifiers modifiers)
    {
        if (!modifiers.HasFlag(ToolModifiers.Shift))
            return p;
        var d = p - start;
        if (d.Length < 1e-9)
            return p;
        var angle = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 12)) * (Math.PI / 12);
        return start + new VVector(Math.Cos(angle), Math.Sin(angle)) * d.Length;
    }

    // ---- Creating, shown live ----

    private void PreviewCreate(SvgDocument document, VPoint p, ToolModifiers modifiers)
    {
        var end = Constrain(_start, p, modifiers);
        if (_preview.Count == 0)
        {
            _quietDefs = document.Root.Elements.OfType<SvgDefs>().FirstOrDefault();
            if (_quietDefs is null)
            {
                _quietDefs = new SvgDefs { Id = "preview-defs" };
                document.Root.InsertChild(0, _quietDefs);
            }
            foreach (var node in _targets)
            {
                var snapshot = NodeSnapshot.Capture(node);
                var gradient = SvgActions.BuildGradient(node, Kind, settings.GradientOnStroke, _start, end, Stops());
                gradient.Id = "preview-" + node.InternalId;
                _quietDefs.AddChild(gradient);
                node.Style.Set(settings.GradientOnStroke ? "stroke" : "fill", SvgPaint.FromUrl(gradient.Id).ToText());
                _preview.Add((node, snapshot, gradient));
            }
            document.NotifyTreeChanged();
        }
        else
        {
            foreach (var (node, _, gradient) in _preview)
            {
                var fresh = SvgActions.BuildGradient(node, Kind, settings.GradientOnStroke, _start, end, Stops());
                foreach (var name in new[] { "x1", "y1", "x2", "y2", "cx", "cy", "r" })
                    gradient.SetAttribute(name, fresh.GetAttribute(name));
            }
        }
        foreach (var (node, _, _) in _preview)
            document.NotifyNodeChanged(node, SvgBounds.Visual(node, document.GlyphProvider));
    }

    private void RevertCreate(SvgDocument document)
    {
        foreach (var (node, snapshot, gradient) in _preview)
        {
            snapshot.Restore(node);
            gradient.Parent?.RemoveChild(gradient);
        }
        _preview.Clear();
        if (_quietDefs is { Id: "preview-defs" } defs)
            defs.Parent?.RemoveChild(defs);
        _quietDefs = null;
        document.NotifyTreeChanged();
    }

    // ---- Editing an existing gradient ----

    private void BeginEditing(Handles handles)
    {
        _editing = handles.Gradient;
        _gradientSnapshot = NodeSnapshot.Capture(handles.Gradient);
        _stopSnapshots = handles.Stops.Select(s => (s, NodeSnapshot.Capture(s))).ToList();
    }

    private static SvgGradient OwnerOfStops(SvgGradient gradient) => gradient.InheritanceChain().FirstOrDefault(g => g.OwnStops.Any()) ?? gradient;

    private void DragHandle(SvgDocument document, VPoint p)
    {
        if (HandlesOf(document) is not { } handles || handles.ToDocument.Invert() is not { } inverse)
            return;
        var local = inverse.Transform(p);
        var gradient = handles.Gradient;
        void Set(string x, string y, VPoint v)
        {
            gradient.SetAttribute(x, NumberFormat.Format(v.X, 5));
            gradient.SetAttribute(y, NumberFormat.Format(v.Y, 5));
        }
        switch (gradient)
        {
            case SvgLinearGradient:
                if (_index == 0)
                    Set("x1", "y1", local);
                else
                    Set("x2", "y2", local);
                break;
            case SvgRadialGradient radial:
                if (_index == 0)
                {
                    // The center moves with the focus when the focus was on it.
                    var focusOnCenter = Math.Abs(radial.Fx - radial.Cx) < 1e-9 && Math.Abs(radial.Fy - radial.Cy) < 1e-9;
                    Set("cx", "cy", local);
                    if (focusOnCenter)
                        gradient.SetAttribute("fx", null);
                    if (focusOnCenter)
                        gradient.SetAttribute("fy", null);
                }
                else if (_index == 1)
                {
                    gradient.SetAttribute("r", NumberFormat.Format(Math.Max(new VPoint(radial.Cx, radial.Cy).DistanceTo(local), 0.001), 5));
                }
                else
                {
                    Set("fx", "fy", local);
                }
                break;
        }
        document.NotifyNodeChanged(handles.Node, SvgBounds.Visual(handles.Node, document.GlyphProvider));
    }

    private Dictionary<string, double> CurrentGeometry(SvgGradient gradient)
    {
        var values = new Dictionary<string, double>();
        foreach (var name in new[] { "x1", "y1", "x2", "y2", "cx", "cy", "r", "fx", "fy" })
            if (gradient.GetAttribute(name) is { } text && NumberFormat.TryParse(text, out var v))
                values[name] = v;
        return values;
    }

    private void DragStop(SvgDocument document, VPoint p)
    {
        if (HandlesOf(document) is not { } handles || _index >= handles.Stops.Count)
            return;
        var axis = handles.AxisEnd - handles.AxisStart;
        var lengthSquared = axis.Dot(axis);
        if (lengthSquared < 1e-12)
            return;
        var t = Math.Round(Math.Clamp(axis.Dot(p - handles.AxisStart) / lengthSquared, 0, 1), 4);
        handles.Stops[_index].Offset = t;
        document.NotifyNodeChanged(handles.Node, SvgBounds.Visual(handles.Node, document.GlyphProvider));
    }

    private void AddStopAt(SvgDocument document, Handles handles, VPoint p)
    {
        var axis = handles.AxisEnd - handles.AxisStart;
        var lengthSquared = axis.Dot(axis);
        if (lengthSquared < 1e-12)
            return;
        var t = Math.Clamp(axis.Dot(p - handles.AxisStart) / lengthSquared, 0, 1);
        var stops = handles.Gradient.ResolvedStops().Select(s => new GradientStop(s.Offset, s.Color)).OrderBy(s => s.Offset).ToList();
        var after = stops.FindIndex(s => s.Offset >= t);
        VColor color;
        if (after <= 0)
            color = stops[0].Color;
        else
        {
            var (a, b) = (stops[after - 1], stops[after]);
            var f = b.Offset > a.Offset ? (t - a.Offset) / (b.Offset - a.Offset) : 0;
            color = new VColor((byte)(a.Color.B + (b.Color.B - a.Color.B) * f), (byte)(a.Color.G + (b.Color.G - a.Color.G) * f),
                (byte)(a.Color.R + (b.Color.R - a.Color.R) * f), (byte)(a.Color.A + (b.Color.A - a.Color.A) * f));
        }
        stops.Add(new GradientStop(Math.Round(t, 4), color));
        document.Actions.SetStops(OwnerOfStops(handles.Gradient), [.. stops.OrderBy(s => s.Offset)]);
    }

    private static double DistanceToSegment(VPoint p, VPoint a, VPoint b)
    {
        var ab = b - a;
        var lengthSquared = ab.Dot(ab);
        var t = lengthSquared == 0 ? 0 : Math.Clamp(ab.Dot(p - a) / lengthSquared, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    // ---- Keys ----

    public bool OnKeyDown(SvgDocument document, ToolKey key, ToolModifiers modifiers)
    {
        if (key is ToolKey.Delete or ToolKey.Backspace && _selectedStop >= 0 && HandlesOf(document) is { } handles && handles.Stops.Count > 2
            && _selectedStop < handles.Stops.Count)
        {
            var stops = handles.Gradient.ResolvedStops().Select(s => new GradientStop(s.Offset, s.Color)).ToList();
            stops.RemoveAt(_selectedStop);
            _selectedStop = -1;
            document.Actions.SetStops(OwnerOfStops(handles.Gradient), stops);
            return true;
        }
        if (key == ToolKey.Escape && _mode != Mode.None)
        {
            Cancel(document);
            return true;
        }
        return false;
    }

    // ---- Overlay ----

    public ToolOverlay? GetOverlay(SvgDocument document)
    {
        var to = document.UserToImage;
        PointD I(VPoint p) => to.Transform(p).ToCore();
        if (_mode == Mode.Create && _moved && HandlesOf(document) is null)
            return null;
        if (HandlesOf(document) is not { } handles)
            return null;
        var points = handles.Geometry.Select(I).Concat(handles.StopPoints.Select(I)).ToList();
        var lines = new List<(PointD, PointD)> { (I(handles.AxisStart), I(handles.AxisEnd)) };
        if (handles.Gradient is SvgRadialGradient && handles.Geometry.Count > 2)
            lines.Add((I(handles.AxisStart), I(handles.Geometry[2])));
        var highlights = new List<RectangleD>();
        if (_selectedStop >= 0 && _selectedStop < handles.StopPoints.Count)
        {
            var s = I(handles.StopPoints[_selectedStop]);
            highlights.Add(new RectangleD(s.X - 6, s.Y - 6, 12, 12));
        }
        return new ToolOverlay { Handles = points, Lines = lines, Highlights = highlights, SquareHandles = false };
    }

    public ToolCursor CursorAt(SvgDocument document, PointD userPoint)
    {
        if (HandlesOf(document) is not { } handles)
            return ToolCursor.Default;
        var p = userPoint.ToVector();
        var reach = document.ScreenToUser(Reach);
        return handles.Geometry.Concat(handles.StopPoints).Any(h => h.DistanceTo(p) <= reach) ? ToolCursor.Move : ToolCursor.Default;
    }
}
