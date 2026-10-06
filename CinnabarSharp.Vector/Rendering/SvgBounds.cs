namespace CinnabarSharp.Vector;

/// <summary>Bounding boxes of elements, for gradients, clips, selection frames and invalidation.</summary>
public static class SvgBounds
{
    /// <summary>
    /// The object bounding box of <paramref name="element"/> (geometry only, no stroke) in its own coordinates, that is inside
    /// its <c>transform</c>. Null when it draws nothing. Text uses the provider's outlines, or estimated boxes without one.
    /// </summary>
    public static VRect? Object(SvgElement element, IGlyphOutlineProvider? provider = null) =>
        ObjectCore(element, provider, 0);

    /// <summary>The element's box in the coordinates of its parent: <see cref="Object"/> mapped by its own transform.</summary>
    public static VRect? InParent(SvgElement element, IGlyphOutlineProvider? provider = null) =>
        Object(element, provider) is { } box ? element.Transform.TransformBounds(box) : null;

    /// <summary>The element's box in the document's user space (all ancestors' transforms applied).</summary>
    public static VRect? InDocument(SvgElement element, IGlyphOutlineProvider? provider = null)
    {
        if (Object(element, provider) is not { } box)
            return null;
        var matrix = ToDocument(element);
        return matrix.TransformBounds(box);
    }

    /// <summary>The matrix from the element's own coordinates (inside its transform) to the document's user space.</summary>
    public static Matrix2D ToDocument(SvgElement element)
    {
        var matrix = element.Transform;
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
            if (parent.Parent is not null)
                matrix = parent.Transform * matrix;
        return matrix;
    }

    /// <summary>The box including the stroke (what must be redrawn when the element changes), in the document's user space.</summary>
    public static VRect? Visual(SvgElement element, IGlyphOutlineProvider? provider = null)
    {
        if (InDocument(element, provider) is not { } box)
            return null;
        var style = StyleResolver.ComputeFor(element);
        var expand = 0.0;
        if (style.Stroke.Kind != PaintKind.None)
        {
            var join = style.LineJoin == LineJoin.Miter ? style.MiterLimit : 1;
            expand = style.StrokeWidth / 2 * Math.Max(join, style.LineCap == LineCap.Square ? 1.5 : 1);
        }
        // Group strokes of children are covered by growing with the largest scale of the ancestors.
        var scale = ToDocument(element).MeanScale;
        return box.Inflate(expand * Math.Max(scale, 1), expand * Math.Max(scale, 1));
    }

    private static VRect? ObjectCore(SvgElement element, IGlyphOutlineProvider? provider, int depth)
    {
        if (depth > 16)
            return null;
        switch (element)
        {
            case SvgShape shape:
            {
                var path = shape.CreatePath();
                return path.IsEmpty ? null : path.Bounds;
            }
            case SvgImage image:
                return image.Width > 0 && image.Height > 0 ? image.Bounds : null;
            case SvgText text:
            {
                var style = StyleResolver.ComputeFor(text);
                VRect? box = null;
                foreach (var run in TextLayout.Layout(text, style, provider))
                    box = Union(box, run.Outline.Bounds is { IsEmpty: false } b ? b : null);
                return box;
            }
            case SvgUse use:
            {
                if (use.Target is not { } target || IsAncestor(target, use))
                    return null;
                var inner = ObjectCore(target, provider, depth + 1);
                if (inner is null)
                    return null;
                var m = Matrix2D.Translate(use.X, use.Y) * target.Transform;
                return m.TransformBounds(inner.Value);
            }
            case SvgGroup or SvgRoot:
            {
                VRect? box = null;
                foreach (var child in ((SvgContainer)element).Elements)
                {
                    if (child is SvgDefs or SvgSymbol or SvgClipPath or SvgMask or SvgGradient || child.Style.Get("display") == "none")
                        continue;
                    if (ObjectCore(child, provider, depth + 1) is { } childBox)
                        box = Union(box, child.Transform.TransformBounds(childBox));
                }
                return box;
            }
            default:
                return null;
        }
    }

    private static bool IsAncestor(SvgElement candidate, SvgElement of) => of.Ancestors().Contains(candidate) || candidate == of;

    private static VRect? Union(VRect? a, VRect? b) => a is null ? b : b is null ? a : a.Value.Union(b.Value);
}
