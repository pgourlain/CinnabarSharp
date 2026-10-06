using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>
/// Pencil (P): draw freehand; the line is simplified (Ramer–Douglas–Peucker) and fitted with cubic Béziers (Schneider's
/// algorithm). The smoothing option says how far the curve may stray from what was drawn. A line that ends where it began is closed.
/// </summary>
public sealed class VectorPencilTool(ToolSettings settings) : IVectorTool
{
    private readonly List<VPoint> _points = [];
    private SvgPath? _live;
    private bool _drawing;

    public string Name => "Pencil";

    public void OnPointerDown(SvgDocument document, ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left)
            return;
        _points.Clear();
        _points.Add(pointer.Position.ToVector());
        _drawing = true;
    }

    public void OnPointerMove(SvgDocument document, ToolPointer pointer)
    {
        if (!_drawing)
            return;
        var p = pointer.Position.ToVector();
        if (p.DistanceTo(_points[^1]) < document.ScreenToUser(1))
            return;
        _points.Add(p);
        UpdateLive(document);
    }

    public void OnPointerUp(SvgDocument document, ToolPointer pointer)
    {
        if (!_drawing)
            return;
        _drawing = false;
        var p = pointer.Position.ToVector();
        if (p.DistanceTo(_points[^1]) >= document.ScreenToUser(1))
            _points.Add(p);
        var live = _live;
        _live = null;
        if (live?.Parent is { } parent)
        {
            parent.RemoveChild(live);
            document.NotifyTreeChanged();
        }
        if (_points.Count < 2)
            return;
        var path = new SvgPath { Id = document.Root.NewId("path") };
        ShapeStyling.Apply(document, path, settings, hasFill: false);
        path.SetPath(Fit(document), 3);
        document.Actions.AddNode(path, name: "Pencil");
    }

    private VectorPath Fit(SvgDocument document)
    {
        // 0.5 to 5.5 screen pixels of freedom, from the smoothing option.
        var tolerance = document.ScreenToUser(0.5 + Math.Clamp(settings.PencilSmoothing, 0, 100) / 100.0 * 5);
        var simple = CurveFitter.Simplify(_points, tolerance / 2);
        var closed = simple.Count > 3 && simple[0].DistanceTo(simple[^1]) < document.ScreenToUser(3);
        if (closed)
            simple[^1] = simple[0];
        var path = CurveFitter.Fit(simple, tolerance);
        return closed ? path.Close() : path;
    }

    private void UpdateLive(SvgDocument document)
    {
        if (_points.Count < 2)
            return;
        // A long stroke is refitted every few points: the fit is the costly part.
        if (_live is not null && _points.Count > 300 && _points.Count % 5 != 0)
            return;
        if (_live is null)
        {
            _live = new SvgPath { Id = document.Root.NewId("path") };
            ShapeStyling.Apply(document, _live, settings, hasFill: false);
            SvgDocumentFactory.DefaultParent(document.Root).AddChild(_live);
            document.NotifyTreeChanged();
        }
        _live.SetPath(Fit(document), 3);
        document.NotifyNodeChanged(_live, SvgBounds.Visual(_live, document.GlyphProvider));
    }
}
