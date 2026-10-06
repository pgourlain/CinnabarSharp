using System.Xml.Linq;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>
/// Applies a transformation to an element in the most natural form it can keep.
/// <list type="bullet">
/// <item>An element without a <c>transform</c> keeps its own geometry attributes: a <c>rect</c> changes <c>x</c>, <c>y</c>,
/// <c>width</c>, <c>height</c>; circles, ellipses, lines and polygons their coordinates; paths their data; text its
/// <c>x</c>/<c>y</c>, for moves and axis-aligned scaling (a <c>rect</c>, <c>circle</c> or <c>ellipse</c> is never rotated or
/// skewed this way, and a <c>circle</c> only scaled uniformly).</item>
/// <item>Anything else (a rotation of a rect, a flip, a group, an image, an element that already has a transform) edits the
/// <c>transform</c> attribute: <c>new = m * old</c>.</item>
/// <item>Rewriting geometry scales the stroke width and dashes with the transformation, like a real scale would.</item>
/// <item>An element painted with a gradient in user space gets the transform attribute instead, so the gradient follows it.</item>
/// </list>
/// The matrix is in the coordinates of the element's parent.
/// </summary>
public static class SvgTransformer
{
    private const double Epsilon = 1e-9;

    public static void Apply(SvgElement element, Matrix2D m, IGlyphOutlineProvider? provider = null)
    {
        if (m.IsIdentity)
            return;
        if (element.Transform.IsIdentity && !UsesUserSpacePaint(element) && ApplyNatural(element, m))
            return;
        element.Transform = m * element.Transform;
    }

    /// <summary>True when the element is painted with a gradient whose coordinates are in user space (moving it must move the gradient too).</summary>
    public static bool UsesUserSpacePaint(SvgElement element)
    {
        if (element.DocumentRoot is not { } root)
            return false;
        var style = StyleResolver.ComputeFor(element);
        foreach (var paint in new[] { style.Fill, style.Stroke })
            if (paint.Kind == PaintKind.Url && root.FindById(paint.Id) is SvgGradient { Units: GradientUnits.UserSpaceOnUse })
                return true;
        return false;
    }

    private static bool IsTranslation(Matrix2D m) => Math.Abs(m.A - 1) < Epsilon && Math.Abs(m.D - 1) < Epsilon
        && Math.Abs(m.B) < Epsilon && Math.Abs(m.C) < Epsilon;

    private static bool IsAxisScale(Matrix2D m) => Math.Abs(m.B) < Epsilon && Math.Abs(m.C) < Epsilon && m.A > Epsilon && m.D > Epsilon;

    private static bool IsSimilarity(Matrix2D m) =>
        Math.Abs(m.A - m.D) < 1e-9 * Math.Max(1, Math.Abs(m.A)) && Math.Abs(m.B + m.C) < 1e-9 * Math.Max(1, Math.Abs(m.B))
        && Math.Abs(m.A * m.A + m.B * m.B) > Epsilon;

    private static bool ApplyNatural(SvgElement element, Matrix2D m)
    {
        switch (element)
        {
            case SvgRect rect when IsAxisScale(m):
            {
                var (rx, ry) = (rect.HasAttribute("rx") ? rect.Rx : (double?)null, rect.HasAttribute("ry") ? rect.Ry : (double?)null);
                var (x, y, w, h) = (rect.X, rect.Y, rect.Width, rect.Height);
                rect.X = m.A * x + m.E;
                rect.Y = m.D * y + m.F;
                if (!IsTranslation(m))
                {
                    rect.Width = w * m.A;
                    rect.Height = h * m.D;
                    if (rx is { } r1)
                        rect.Rx = r1 * m.A;
                    if (ry is { } r2)
                        rect.Ry = r2 * m.D;
                    ScaleStroke(element, Math.Sqrt(m.A * m.D));
                }
                return true;
            }
            case SvgCircle circle when IsAxisScale(m) && Math.Abs(m.A - m.D) < Epsilon * Math.Max(1, m.A):
                circle.Cx = m.A * circle.Cx + m.E;
                circle.Cy = m.D * circle.Cy + m.F;
                if (!IsTranslation(m))
                {
                    circle.R *= m.A;
                    ScaleStroke(element, m.A);
                }
                return true;
            case SvgEllipse ellipse when IsAxisScale(m):
                (var cx, var cy, var erx, var ery) = (ellipse.Cx, ellipse.Cy, ellipse.HasAttribute("rx") ? ellipse.Rx : ellipse.Ry,
                    ellipse.HasAttribute("ry") ? ellipse.Ry : ellipse.Rx);
                ellipse.Cx = m.A * cx + m.E;
                ellipse.Cy = m.D * cy + m.F;
                if (!IsTranslation(m))
                {
                    ellipse.Rx = erx * m.A;
                    ellipse.Ry = ery * m.D;
                    ScaleStroke(element, Math.Sqrt(m.A * m.D));
                }
                return true;
            case SvgLine line when IsSimilarity(m) || IsAxisScale(m):
            {
                var p1 = m.Transform(new VPoint(line.X1, line.Y1));
                var p2 = m.Transform(new VPoint(line.X2, line.Y2));
                (line.X1, line.Y1, line.X2, line.Y2) = (p1.X, p1.Y, p2.X, p2.Y);
                if (!IsTranslation(m))
                    ScaleStroke(element, m.MeanScale);
                return true;
            }
            case SvgPoly poly when IsSimilarity(m) || IsAxisScale(m):
                poly.Points = poly.Points.Select(m.Transform).ToList();
                if (!IsTranslation(m))
                    ScaleStroke(element, m.MeanScale);
                return true;
            case SvgPath path when IsSimilarity(m) || IsAxisScale(m):
            {
                var data = path.CreatePath();
                path.SetPath(IsTranslation(m) ? data.Translated(m.E, m.F) : data.Transformed(m), 4);
                if (!IsTranslation(m))
                    ScaleStroke(element, m.MeanScale);
                return true;
            }
            case SvgText text when IsTranslation(m):
                MoveText(text, m.E, m.F);
                return true;
            case SvgUse use when IsTranslation(m):
                use.X += m.E;
                use.Y += m.F;
                return true;
            default:
                return false;
        }
    }

    private static void MoveText(SvgTextBase text, double dx, double dy)
    {
        // The anchor lists; a text without them is at (0, 0).
        var xs = text.XList;
        var ys = text.YList;
        if (text is SvgText)
        {
            text.SetAttributeNumbers("x", xs.Length == 0 ? [dx] : xs.Select(v => v + dx));
            text.SetAttributeNumbers("y", ys.Length == 0 ? [dy] : ys.Select(v => v + dy));
        }
        else
        {
            // A span positions itself only when it has absolute coordinates.
            if (xs.Length > 0)
                text.SetAttributeNumbers("x", xs.Select(v => v + dx));
            if (ys.Length > 0)
                text.SetAttributeNumbers("y", ys.Select(v => v + dy));
        }
        foreach (var span in text.Children.OfType<SvgTextSpan>())
            MoveText(span, dx, dy);
    }

    /// <summary>Scales the stroke width and the dashes (those the element or its ancestors define) by <paramref name="factor"/>.</summary>
    public static void ScaleStroke(SvgElement element, double factor)
    {
        if (Math.Abs(factor - 1) < Epsilon || factor <= 0)
            return;
        var style = StyleResolver.ComputeFor(element);
        if (style.Stroke.Kind == PaintKind.None)
            return;
        element.Style.SetNumber("stroke-width", style.StrokeWidth * factor);
        if (style.DashArray is { } dashes)
            element.Style.Set("stroke-dasharray", string.Join(' ', dashes.Select(d => NumberFormat.Format(d * factor, 4))));
        if (style.DashOffset != 0)
            element.Style.SetNumber("stroke-dashoffset", style.DashOffset * factor);
    }
}
