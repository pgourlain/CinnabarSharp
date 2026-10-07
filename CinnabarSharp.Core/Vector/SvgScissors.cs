using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>
/// The run of an outline the Scissors tool would take out: the part of <see cref="Shape"/> between the two crossings with other
/// shapes that is under the pointer. <see cref="Figures"/> are the sub-paths of the shape cut at those crossings, in the shape's
/// own space; <see cref="Figure"/> and <see cref="Piece"/> say which run was found.
/// </summary>
public sealed record ScissorsHit(SvgShape Shape, IReadOnlyList<CutFigure> Figures, int Figure, int Piece)
{
    public VectorPath Removed => Figures[Figure].Pieces[Piece];
}

/// <summary>Finds what the Scissors tool would remove under a point.</summary>
public static class SvgScissors
{
    /// <summary>
    /// The run of the top-most outline near the point (document user space) that lies between two crossings with the other
    /// shapes of the drawing; null when the point is not on an outline or the outline there is not crossed.
    /// </summary>
    public static ScissorsHit? Find(SvgDocument document, VPoint point, double tolerance)
    {
        foreach (var shape in SvgHitTester.HitAll(document, point, tolerance, enterGroups: true).OfType<SvgShape>())
            if (FindIn(document, shape, point, tolerance) is { } hit)
                return hit;
        return null;
    }

    /// <summary>The same for one shape.</summary>
    public static ScissorsHit? FindIn(SvgDocument document, SvgShape shape, VPoint point, double tolerance)
    {
        var world = SvgBounds.ToDocument(shape);
        if (world.Invert() is not { } inverse)
            return null;
        var scale = Math.Max(world.MeanScale, 1e-9);
        var local = inverse.Transform(point);
        var style = StyleResolver.ComputeFor(shape);
        var reach = tolerance / scale + (style.Stroke.Kind != PaintKind.None ? style.StrokeWidth / 2 : 0);

        var path = shape.CreatePath();
        if (path.IsEmpty || !path.Bounds.Inflate(reach, reach).Contains(local))
            return null;
        var cutters = CuttersOf(document, shape, inverse);
        if (cutters.Count == 0)
            return null;

        var figures = PathCutter.Cut(path, cutters, Math.Clamp(0.05 / scale, 1e-4, 0.05));
        var best = (Distance: double.MaxValue, Figure: -1, Piece: -1);
        for (var f = 0; f < figures.Count; f++)
            for (var p = 0; p < figures[f].Pieces.Count; p++)
            {
                var distance = DistanceTo(figures[f].Pieces[p], local, reach / 8);
                if (distance < best.Distance)
                    best = (distance, f, p);
            }
        return best.Figure >= 0 && best.Distance <= reach ? new ScissorsHit(shape, figures, best.Figure, best.Piece) : null;
    }

    /// <summary>
    /// The outlines of the other visible, unlocked shapes whose box meets the shape's box, in the shape's own space: what the
    /// shape is cut by. Shapes inside groups count; what is inside definitions does not.
    /// </summary>
    private static List<VectorPath> CuttersOf(SvgDocument document, SvgShape shape, Matrix2D toLocal)
    {
        var result = new List<VectorPath>();
        var box = SvgBounds.InDocument(shape);
        foreach (var other in Leaves(SvgHitTester.SelectableObjects(document)))
        {
            if (other == shape)
                continue;
            if (box is { } mine && SvgBounds.InDocument(other) is { } theirs && !mine.Inflate(1, 1).IntersectsWith(theirs))
                continue;
            var outline = other.CreatePath();
            if (!outline.IsEmpty)
                result.Add(outline.Transformed(SvgBounds.ToDocument(other)).Transformed(toLocal));
        }
        return result;
    }

    private static IEnumerable<SvgShape> Leaves(IEnumerable<SvgElement> objects)
    {
        foreach (var element in objects)
        {
            if (element.IsLocked || element.Style.Get("display") == "none")
                continue;
            if (element is SvgShape shape)
                yield return shape;
            else if (element is SvgGroup group)
                foreach (var inner in Leaves(group.Elements.Where(SvgHitTester.IsObject)))
                    yield return inner;
        }
    }

    private static double DistanceTo(VectorPath piece, VPoint p, double tolerance)
    {
        var best = double.MaxValue;
        foreach (var line in Flattener.Flatten(piece, Matrix2D.Identity, Math.Max(tolerance, 1e-4)))
        {
            var points = line.Points;
            if (points.Count == 1)
                best = Math.Min(best, p.DistanceTo(points[0]));
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var ab = points[i + 1] - points[i];
                var lengthSquared = ab.Dot(ab);
                var t = lengthSquared == 0 ? 0 : Math.Clamp(ab.Dot(p - points[i]) / lengthSquared, 0, 1);
                best = Math.Min(best, p.DistanceTo(points[i] + ab * t));
            }
        }
        return best;
    }
}
