using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector.Tools;

/// <summary>Finds the objects under a point or inside a rectangle of the drawing, the way a user expects to pick them.</summary>
public static class SvgHitTester
{
    /// <summary>
    /// Objects a click can select, bottom to top: the children of the root and of layer groups. A group that is not a layer is
    /// one object. Hidden (<c>display:none</c>) and locked objects are not selectable, and neither is what is inside a locked group.
    /// </summary>
    public static IEnumerable<SvgElement> SelectableObjects(SvgDocument document) => Selectable(document.Root);

    private static IEnumerable<SvgElement> Selectable(SvgContainer container)
    {
        foreach (var child in container.Elements)
        {
            if (!IsObject(child) || child.IsLocked || child.Style.Get("display") == "none")
                continue;
            if (child is SvgGroup { IsLayer: true } layer)
            {
                foreach (var inner in Selectable(layer))
                    yield return inner;
            }
            else
            {
                yield return child;
            }
        }
    }

    public static bool IsObject(SvgElement e) => e is SvgShape or SvgGroup or SvgText or SvgImage or SvgUse or SvgRoot;

    /// <summary>The top-most object under the point; with <paramref name="enterGroups"/> the top-most object inside groups.</summary>
    public static SvgElement? HitTest(SvgDocument document, VPoint point, double tolerance, bool enterGroups = false, SvgElement? below = null)
    {
        var hits = HitAll(document, point, tolerance, enterGroups);
        if (hits.Count == 0)
            return null;
        if (below is null)
            return hits[0];
        // Alt+click: the next one under the selected object, going back to the top after the last.
        var index = hits.IndexOf(below);
        return hits[index < 0 ? 0 : (index + 1) % hits.Count];
    }

    /// <summary>Every object under the point, top-most first.</summary>
    public static List<SvgElement> HitAll(SvgDocument document, VPoint point, double tolerance, bool enterGroups = false)
    {
        var result = new List<SvgElement>();
        foreach (var obj in SelectableObjects(document).Reverse())
        {
            if (enterGroups && obj is SvgGroup group)
                CollectLeaves(group, point, tolerance, result);
            else if (Hits(obj, point, tolerance))
                result.Add(obj);
        }
        return result;
    }

    private static void CollectLeaves(SvgContainer container, VPoint point, double tolerance, List<SvgElement> result)
    {
        foreach (var child in container.Elements.Reverse())
        {
            if (!IsObject(child) || child.IsLocked || child.Style.Get("display") == "none")
                continue;
            if (child is SvgGroup group)
                CollectLeaves(group, point, tolerance, result);
            else if (Hits(child, point, tolerance))
                result.Add(child);
        }
    }

    /// <summary>Whether the point (document user space) hits the element: its fill, its stroke, or for groups any child.</summary>
    public static bool Hits(SvgElement element, VPoint point, double tolerance)
    {
        switch (element)
        {
            case SvgGroup or SvgRoot:
                return ((SvgContainer)element).Elements.Any(c => IsObject(c) && c.Style.Get("display") != "none" && Hits(c, point, tolerance));
            case SvgShape shape:
                return HitsShape(shape, point, tolerance);
            default:
            {
                // Text, images and clones: their box.
                var box = SvgBounds.InDocument(element);
                return box is { } b && b.Inflate(tolerance, tolerance).Contains(point);
            }
        }
    }

    private static bool HitsShape(SvgShape shape, VPoint documentPoint, double tolerance)
    {
        var world = SvgBounds.ToDocument(shape);
        if (world.Invert() is not { } inverse)
            return false;
        var local = inverse.Transform(documentPoint);
        var scale = Math.Max(world.MeanScale, 1e-9);
        var localTolerance = tolerance / scale;
        var path = shape.CreatePath();
        if (path.IsEmpty)
            return false;
        var style = StyleResolver.ComputeFor(shape);
        var bounds = path.Bounds;
        var reach = (style.Stroke.Kind != PaintKind.None ? style.StrokeWidth / 2 : 0) + localTolerance;
        if (!bounds.Inflate(reach, reach).Contains(local))
            return false;

        var polylines = Flattener.Flatten(path, Matrix2D.Identity, Math.Max(localTolerance / 4, 1e-6));
        // An open shape has no inside to click: a line, a polyline without fill.
        var filled = style.Fill.Kind != PaintKind.None && !(shape is SvgLine);
        if (filled && Inside(polylines, local, style.FillRule))
            return true;
        if (style.Stroke.Kind != PaintKind.None || !filled)
        {
            var halfWidth = style.Stroke.Kind != PaintKind.None ? style.StrokeWidth / 2 : 0;
            var limit = halfWidth + localTolerance;
            foreach (var polyline in polylines)
                if (NearPolyline(polyline, local, limit))
                    return true;
        }
        return false;
    }

    private static bool Inside(List<Polyline> polylines, VPoint p, FillRule rule)
    {
        var winding = 0;
        var crossings = 0;
        foreach (var polyline in polylines)
        {
            var points = polyline.Points;
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                if ((a.Y <= p.Y) == (b.Y <= p.Y))
                    continue;
                var x = a.X + (p.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X);
                if (x > p.X)
                {
                    crossings++;
                    winding += b.Y > a.Y ? 1 : -1;
                }
            }
        }
        return rule == FillRule.EvenOdd ? (crossings & 1) == 1 : winding != 0;
    }

    private static bool NearPolyline(Polyline polyline, VPoint p, double limit)
    {
        var points = polyline.Points;
        var count = polyline.Closed ? points.Count : points.Count - 1;
        if (points.Count == 1)
            return p.DistanceTo(points[0]) <= limit;
        for (var i = 0; i < count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var ab = b - a;
            var lengthSquared = ab.Dot(ab);
            var t = lengthSquared == 0 ? 0 : Math.Clamp(ab.Dot(p - a) / lengthSquared, 0, 1);
            if (p.DistanceTo(a + ab * t) <= limit)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Objects inside the rectangle (a rubber band): those whose bounding box is entirely inside it, or, with
    /// <paramref name="touching"/>, any that it touches.
    /// </summary>
    public static List<SvgElement> InRect(SvgDocument document, VRect rect, bool touching = false)
    {
        var result = new List<SvgElement>();
        foreach (var obj in SelectableObjects(document))
        {
            if (SvgBounds.InDocument(obj) is not { } box)
                continue;
            if (touching ? box.IntersectsWith(rect) : rect.Contains(new VPoint(box.Left, box.Top)) && rect.Contains(new VPoint(box.Right, box.Bottom)))
                result.Add(obj);
        }
        return result;
    }
}
