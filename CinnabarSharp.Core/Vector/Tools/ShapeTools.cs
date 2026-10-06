using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>
/// Shared part of the shape tools: a drag shows the shape live (not yet in the history) and the release adds it as one step,
/// styled with the stroke (primary color, brush width) and fill (secondary color) settings like Paint.NET's shape tools,
/// as the Shape style option says: outline, fill or both.
/// </summary>
public abstract class ShapeDrawTool(ToolSettings settings, string name, string historyName) : IVectorTool
{
    private SvgElement? _live;
    private VPoint _start;
    private bool _dragging;

    public string Name { get; } = name;

    protected ToolSettings Settings { get; } = settings;

    /// <summary>Creates or updates <paramref name="current"/> (null at first) for a drag from <paramref name="start"/> to <paramref name="end"/>; null for "too small".</summary>
    protected abstract SvgElement? Build(SvgDocument document, SvgElement? current, VPoint start, VPoint end, ToolModifiers modifiers);

    /// <summary>Lines have no fill, whatever the shape style says.</summary>
    protected virtual bool HasFill => true;

    public void OnPointerDown(SvgDocument document, ToolPointer pointer)
    {
        if (pointer.Button != ToolButton.Left)
            return;
        _start = pointer.Position.ToVector();
        _dragging = true;
        _live = null;
    }

    public void OnPointerMove(SvgDocument document, ToolPointer pointer)
    {
        if (!_dragging)
            return;
        var end = pointer.Position.ToVector();
        if (_live is null && end.DistanceTo(_start) < document.ScreenToUser(3))
            return;
        var before = _live;
        var shape = Build(document, _live, _start, end, pointer.Modifiers);
        if (shape is null)
            return;
        if (before is null)
        {
            ApplyStyle(document, shape);
            SvgDocumentFactory.DefaultParent(document.Root).AddChild(shape);
            document.NotifyTreeChanged();
        }
        _live = shape;
        document.NotifyNodeChanged(shape, SvgBounds.Visual(shape, document.GlyphProvider));
    }

    public void OnPointerUp(SvgDocument document, ToolPointer pointer)
    {
        if (!_dragging)
            return;
        _dragging = false;
        if (_live is null)
            return;
        var final = Build(document, _live, _start, pointer.Position.ToVector(), pointer.Modifiers) ?? _live;
        var live = _live;
        _live = null;
        // Out of the tree without a trace, then in again as the one history step.
        live.Parent?.RemoveChild(live);
        document.NotifyTreeChanged();
        document.Actions.AddNode(final, name: historyName);
    }

    /// <summary>Gives up a shape being dragged.</summary>
    public void Cancel(SvgDocument document)
    {
        _dragging = false;
        if (_live is { } live)
        {
            live.Parent?.RemoveChild(live);
            document.NotifyTreeChanged();
        }
        _live = null;
    }

    private void ApplyStyle(SvgDocument document, SvgElement shape) =>
        ShapeStyling.Apply(document, shape, Settings, HasFill);

    /// <summary>The box dragged from <paramref name="start"/> to <paramref name="end"/>: a square with Shift, around the start with Alt.</summary>
    protected static VRect DragBox(VPoint start, VPoint end, ToolModifiers modifiers)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (modifiers.HasFlag(ToolModifiers.Shift))
        {
            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = (dx < 0 ? -1 : 1) * side;
            dy = (dy < 0 ? -1 : 1) * side;
        }
        return modifiers.HasFlag(ToolModifiers.Alt)
            ? new VRect(start.X - Math.Abs(dx), start.Y - Math.Abs(dy), 2 * Math.Abs(dx), 2 * Math.Abs(dy))
            : VRect.FromPoints(start, new VPoint(start.X + dx, start.Y + dy));
    }
}

/// <summary>How new shapes are painted: stroke from the primary color and brush width, fill from the secondary color, as the Shape style option says.</summary>
public static class ShapeStyling
{
    public static void Apply(SvgDocument document, SvgElement shape, ToolSettings settings, bool hasFill = true)
    {
        var stroke = settings.PrimaryColor;
        var fill = settings.SecondaryColor;
        var style = hasFill ? settings.ShapeStyle : ShapeStyle.Outline;
        var outline = style is ShapeStyle.Outline or ShapeStyle.OutlineAndFill;
        var filled = style is ShapeStyle.Fill or ShapeStyle.OutlineAndFill;

        if (filled)
        {
            shape.SetAttribute("fill", SvgPaint.FromColor(VColor.FromRgb(fill.R, fill.G, fill.B)).ToText());
            shape.SetAttribute("fill-opacity", fill.A < 255 ? NumberFormat.Format(fill.A / 255.0, 3) : null);
        }
        else
        {
            shape.SetAttribute("fill", "none");
            shape.SetAttribute("fill-opacity", null);
        }
        if (outline)
        {
            shape.SetAttribute("stroke", SvgPaint.FromColor(VColor.FromRgb(stroke.R, stroke.G, stroke.B)).ToText());
            shape.SetAttribute("stroke-width", NumberFormat.Format(Math.Max(settings.BrushWidth, 1) * document.PixelsToUser(1), 4));
            shape.SetAttribute("stroke-opacity", stroke.A < 255 ? NumberFormat.Format(stroke.A / 255.0, 3) : null);
        }
        else
        {
            shape.SetAttribute("stroke", null);
            shape.SetAttribute("stroke-width", null);
            shape.SetAttribute("stroke-opacity", null);
        }
    }
}

/// <summary>Rectangle (R): drag a box; Shift for a square, Alt from the center; the corner radius is an option.</summary>
public sealed class VectorRectangleTool(ToolSettings settings) : ShapeDrawTool(settings, "Rectangle", "Rectangle")
{
    protected override SvgElement? Build(SvgDocument document, SvgElement? current, VPoint start, VPoint end, ToolModifiers modifiers)
    {
        var box = DragBox(start, end, modifiers);
        if (box.Width <= 0 || box.Height <= 0)
            return current;
        var rect = current as SvgRect ?? new SvgRect { Id = document.Root.NewId("rect") };
        rect.X = Math.Round(box.X, 4);
        rect.Y = Math.Round(box.Y, 4);
        rect.Width = Math.Round(box.Width, 4);
        rect.Height = Math.Round(box.Height, 4);
        var radius = Math.Min(Settings.VectorCornerRadius, Math.Min(box.Width, box.Height) / 2);
        if (radius > 0)
        {
            rect.Rx = Math.Round(radius, 4);
            rect.Ry = Math.Round(radius, 4);
        }
        else
        {
            rect.SetAttribute("rx", null);
            rect.SetAttribute("ry", null);
        }
        return rect;
    }
}

/// <summary>Ellipse (E): drag the box around it; Shift for a circle, Alt from the center.</summary>
public sealed class VectorEllipseTool(ToolSettings settings) : ShapeDrawTool(settings, "Ellipse", "Ellipse")
{
    protected override SvgElement? Build(SvgDocument document, SvgElement? current, VPoint start, VPoint end, ToolModifiers modifiers)
    {
        var box = DragBox(start, end, modifiers);
        if (box.Width <= 0 || box.Height <= 0)
            return current;
        var ellipse = current as SvgEllipse ?? new SvgEllipse { Id = document.Root.NewId("ellipse") };
        ellipse.Cx = Math.Round(box.X + box.Width / 2, 4);
        ellipse.Cy = Math.Round(box.Y + box.Height / 2, 4);
        ellipse.Rx = Math.Round(box.Width / 2, 4);
        ellipse.Ry = Math.Round(box.Height / 2, 4);
        return ellipse;
    }
}

/// <summary>Line (L): drag from one end to the other; Shift snaps the angle to 15°, Alt draws from the middle.</summary>
public sealed class VectorLineTool(ToolSettings settings) : ShapeDrawTool(settings, "Line", "Line")
{
    protected override bool HasFill => false;

    protected override SvgElement? Build(SvgDocument document, SvgElement? current, VPoint start, VPoint end, ToolModifiers modifiers)
    {
        var d = end - start;
        if (modifiers.HasFlag(ToolModifiers.Shift) && d.Length > 0)
        {
            var angle = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 12)) * (Math.PI / 12);
            d = new VVector(Math.Cos(angle), Math.Sin(angle)) * d.Length;
        }
        var from = modifiers.HasFlag(ToolModifiers.Alt) ? start - d : start;
        var to = start + d;
        if (d.Length <= 0)
            return current;
        var line = current as SvgLine ?? new SvgLine { Id = document.Root.NewId("line") };
        line.X1 = Math.Round(from.X, 4);
        line.Y1 = Math.Round(from.Y, 4);
        line.X2 = Math.Round(to.X, 4);
        line.Y2 = Math.Round(to.Y, 4);
        return line;
    }
}

/// <summary>
/// Polygon and star (*): drag from the center; the distance is the radius and the direction turns the shape (Shift snaps to 15°).
/// Options: number of corners, star ratio (0 is a plain polygon) and rounding of the corners.
/// </summary>
public sealed class VectorPolygonTool(ToolSettings settings) : ShapeDrawTool(settings, "Polygon / Star", "Polygon")
{
    protected override SvgElement? Build(SvgDocument document, SvgElement? current, VPoint start, VPoint end, ToolModifiers modifiers)
    {
        var d = end - start;
        var radius = d.Length;
        if (radius <= 0)
            return current;
        var angle = Math.Atan2(d.Y, d.X);
        if (modifiers.HasFlag(ToolModifiers.Shift))
            angle = Math.Round(angle / (Math.PI / 12)) * (Math.PI / 12);
        var points = Points(start, radius, angle, Math.Max(3, Settings.PolygonCorners), Settings.StarRatio);
        var rounding = Math.Clamp(Settings.PolygonRounding, 0, 1);
        if (rounding > 0)
        {
            var path = current as SvgPath ?? new SvgPath { Id = document.Root.NewId("path") };
            path.SetPath(Rounded(points, rounding), 3);
            return path;
        }
        var polygon = current as SvgPolygon ?? new SvgPolygon { Id = document.Root.NewId(Settings.StarRatio > 0 ? "star" : "polygon") };
        polygon.Points = points.Select(p => new VPoint(Math.Round(p.X, 4), Math.Round(p.Y, 4))).ToList();
        return polygon;
    }

    /// <summary>Corner points; a star alternates the outer radius with <paramref name="ratio"/> times it.</summary>
    public static List<VPoint> Points(VPoint center, double radius, double angle, int corners, double ratio)
    {
        var points = new List<VPoint>();
        var count = ratio > 0 ? corners * 2 : corners;
        for (var i = 0; i < count; i++)
        {
            var r = ratio > 0 && i % 2 == 1 ? radius * Math.Clamp(ratio, 0.01, 1) : radius;
            var a = angle + i * 2 * Math.PI / count;
            points.Add(new VPoint(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a)));
        }
        return points;
    }

    /// <summary>Closed path through the corners with each corner cut by a quadratic curve (<paramref name="rounding"/>: half of the shortest side at 1).</summary>
    public static VectorPath Rounded(IReadOnlyList<VPoint> points, double rounding)
    {
        var path = new VectorPath();
        var n = points.Count;
        var cut = Enumerable.Range(0, n).Min(i => points[i].DistanceTo(points[(i + 1) % n])) / 2 * rounding;
        for (var i = 0; i < n; i++)
        {
            var previous = points[(i + n - 1) % n];
            var current = points[i];
            var next = points[(i + 1) % n];
            var entry = current + (previous - current).Normalized() * cut;
            var exit = current + (next - current).Normalized() * cut;
            if (i == 0)
                path.MoveTo(entry);
            else
                path.LineTo(entry);
            path.QuadTo(current, exit);
        }
        return path.Close();
    }
}
