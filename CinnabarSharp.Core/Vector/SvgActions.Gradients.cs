using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

public enum SvgGradientKind
{
    Linear,
    Radial,
}

/// <summary>A color stop of a gradient: position 0 to 1 and a color (with its alpha).</summary>
public sealed record GradientStop(double Offset, VColor Color);

public sealed partial class SvgActions
{
    private SvgDefs EnsureDefs(Transaction tx)
    {
        if (Root.Elements.OfType<SvgDefs>().FirstOrDefault() is { } defs)
            return defs;
        defs = new SvgDefs();
        tx.Insert(Root, 0, defs);
        return defs;
    }

    private static SvgStop NewStop(GradientStop stop)
    {
        var element = new SvgStop();
        element.Offset = stop.Offset;
        element.SetColor(stop.Color);
        return element;
    }

    /// <summary>
    /// Gives each element its own new gradient (an object bounding box gradient, left to right or from the center) for its fill
    /// or stroke. Default stops: the color the element has now, fading to transparent.
    /// </summary>
    public SvgGradient? CreateGradient(IEnumerable<SvgElement>? nodes, SvgGradientKind kind, bool stroke, IReadOnlyList<GradientStop>? stops = null)
    {
        var targets = Targets(nodes);
        if (targets.Count == 0)
            return null;
        var tx = Begin(stroke ? "Gradient Stroke" : "Gradient Fill");
        var defs = EnsureDefs(tx);
        SvgGradient? last = null;
        foreach (var node in targets)
        {
            var computed = StyleResolver.ComputeFor(node);
            var paint = stroke ? computed.Stroke : computed.Fill;
            var start = paint.Kind switch
            {
                PaintKind.Color => paint.Color,
                PaintKind.CurrentColor => computed.Color,
                _ => VColor.Black,
            };
            var list = stops ?? [new GradientStop(0, start), new GradientStop(1, start.WithAlpha(0))];
            var gradient = kind == SvgGradientKind.Linear
                ? (SvgGradient)new SvgLinearGradient()
                : new SvgRadialGradient();
            gradient.Id = Root.NewId(kind == SvgGradientKind.Linear ? "linearGradient" : "radialGradient");
            foreach (var stopElement in list.Select(NewStop))
                gradient.AddChild(stopElement);
            tx.Insert(defs, defs.Children.Count, gradient);
            var url = SvgPaint.FromUrl(gradient.Id).ToText();
            tx.Edit([node], () => node.Style.Set(stroke ? "stroke" : "fill", url));
            last = gradient;
        }
        tx.Commit();
        return last;
    }

    /// <summary>
    /// Gives each element its own new gradient in user space: linear from <paramref name="startDocument"/> to
    /// <paramref name="endDocument"/>, or radial around the start with the distance to the end as radius (points in the document's user
    /// space, converted to each element's own coordinates). Stops default to the element's color fading out.
    /// </summary>
    public SvgGradient? ApplyGradient(IEnumerable<SvgElement>? nodes, SvgGradientKind kind, bool stroke, VPoint startDocument, VPoint endDocument,
        IReadOnlyList<GradientStop>? stops = null)
    {
        var targets = TopLevel(nodes);
        if (targets.Count == 0)
            return null;
        var tx = Begin(stroke ? "Gradient Stroke" : "Gradient Fill");
        var defs = EnsureDefs(tx);
        SvgGradient? last = null;
        foreach (var node in targets)
        {
            var gradient = BuildGradient(node, kind, stroke, startDocument, endDocument, stops);
            gradient.Id = Root.NewId(kind == SvgGradientKind.Linear ? "linearGradient" : "radialGradient");
            tx.Insert(defs, defs.Children.Count, gradient);
            var url = SvgPaint.FromUrl(gradient.Id).ToText();
            tx.Edit([node], () => node.Style.Set(stroke ? "stroke" : "fill", url));
            last = gradient;
        }
        tx.Commit();
        return last;
    }

    /// <summary>A gradient element (not in the document) for the element, with its geometry in the element's coordinates.</summary>
    internal static SvgGradient BuildGradient(SvgElement node, SvgGradientKind kind, bool stroke, VPoint startDocument, VPoint endDocument,
        IReadOnlyList<GradientStop>? stops)
    {
        var computed = StyleResolver.ComputeFor(node);
        var paint = stroke ? computed.Stroke : computed.Fill;
        var start = paint.Kind switch
        {
            PaintKind.Color => paint.Color,
            PaintKind.CurrentColor => computed.Color,
            _ => VColor.Black,
        };
        var list = stops ?? [new GradientStop(0, start), new GradientStop(1, start.WithAlpha(0))];
        var world = SvgBounds.ToDocument(node);
        var inverse = world.Invert() ?? Matrix2D.Identity;
        var a = inverse.Transform(startDocument);
        var b = inverse.Transform(endDocument);
        SvgGradient gradient;
        if (kind == SvgGradientKind.Linear)
        {
            gradient = new SvgLinearGradient();
            gradient.SetAttribute("gradientUnits", "userSpaceOnUse");
            gradient.SetAttribute("x1", NumberFormat.Format(a.X, 4));
            gradient.SetAttribute("y1", NumberFormat.Format(a.Y, 4));
            gradient.SetAttribute("x2", NumberFormat.Format(b.X, 4));
            gradient.SetAttribute("y2", NumberFormat.Format(b.Y, 4));
        }
        else
        {
            gradient = new SvgRadialGradient();
            gradient.SetAttribute("gradientUnits", "userSpaceOnUse");
            gradient.SetAttribute("cx", NumberFormat.Format(a.X, 4));
            gradient.SetAttribute("cy", NumberFormat.Format(a.Y, 4));
            gradient.SetAttribute("r", NumberFormat.Format(Math.Max(a.DistanceTo(b), 0.001), 4));
        }
        foreach (var stopElement in list.Select(NewStop))
            gradient.AddChild(stopElement);
        return gradient;
    }

    /// <summary>
    /// Replaces the stops of a gradient. When only offsets and colors change, the existing stop elements are edited (one entry that
    /// follows a drag); when stops are added or removed, they are inserted and removed.
    /// </summary>
    public void SetStops(SvgGradient gradient, IReadOnlyList<GradientStop> stops, bool coalesce = false)
    {
        var existing = gradient.OwnStops.ToList();
        var tx = Begin("Edit Gradient", coalesce ? $"gradient:{gradient.InternalId}" : null);
        if (existing.Count == stops.Count)
        {
            tx.Edit(existing, () =>
            {
                for (var i = 0; i < stops.Count; i++)
                {
                    existing[i].Offset = stops[i].Offset;
                    existing[i].SetColor(stops[i].Color);
                }
            });
        }
        else
        {
            foreach (var stop in Enumerable.Reverse(existing))
                tx.Remove(stop);
            var index = 0;
            foreach (var stop in stops)
                tx.Insert(gradient, index++, NewStop(stop));
        }
        tx.Commit();
    }

    /// <summary>Turns a gradient into the other kind (a new gradient with the same stops) for the element's fill or stroke.</summary>
    public SvgGradient? ChangeGradientKind(SvgElement node, bool stroke, SvgGradientKind kind)
    {
        var current = StyleResolver.ComputeFor(node);
        var paint = stroke ? current.Stroke : current.Fill;
        if (paint.Kind != PaintKind.Url || Root.FindById(paint.Id) is not SvgGradient old)
            return CreateGradient([node], kind, stroke);
        if (old is SvgLinearGradient == (kind == SvgGradientKind.Linear))
            return old;
        var stops = old.ResolvedStops().Select(s => new GradientStop(s.Offset, s.Color)).ToList();
        return CreateGradient([node], kind, stroke, stops);
    }

    /// <summary>Sets the geometry attributes of a gradient (the Gradient tool's handles); units stay as the gradient has them.</summary>
    public void SetGradientGeometry(SvgGradient gradient, IReadOnlyDictionary<string, double> values, bool coalesce = false)
    {
        var tx = Begin("Edit Gradient", coalesce ? $"gradient:{gradient.InternalId}:geometry" : null);
        tx.Edit([gradient], () =>
        {
            foreach (var (name, value) in values)
                gradient.SetAttribute(name, NumberFormat.Format(value, 5));
        });
        tx.Commit();
    }
}
