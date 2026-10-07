using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

public enum AlignEdge
{
    Left,
    CenterHorizontal,
    Right,
    Top,
    CenterVertical,
    Bottom,
}

/// <summary>What <see cref="SvgActions.Align"/> lines the objects up with.</summary>
public enum AlignRelativeTo
{
    /// <summary>The object selected first stays where it is.</summary>
    FirstSelected,

    /// <summary>The object selected last stays where it is.</summary>
    LastSelected,

    /// <summary>The object with the largest box stays where it is.</summary>
    Biggest,

    /// <summary>The page (the viewBox).</summary>
    Page,

    /// <summary>The box around all the objects.</summary>
    Selection,
}

public enum DistributeMode
{
    Left,
    CenterHorizontal,
    Right,
    Top,
    CenterVertical,
    Bottom,

    /// <summary>Equal horizontal gaps between the boxes.</summary>
    GapHorizontal,

    /// <summary>Equal vertical gaps between the boxes.</summary>
    GapVertical,
}

public enum PathOperation
{
    Union,
    Difference,
    Intersection,
    Exclusion,

    /// <summary>The bottom object cut into pieces along the outline of the others.</summary>
    Division,

    /// <summary>One path with the outlines of all the objects (no boolean operation).</summary>
    Combine,
}

// Object menu (align, distribute, quarter turns) and Path menu (stroke to path, boolean operations, combine, break apart...).
public sealed partial class SvgActions
{
    /// <summary>Quarter turn around the middle of the box of the elements.</summary>
    public void Rotate90(IEnumerable<SvgElement>? nodes, bool clockwise)
    {
        var targets = TopLevel(nodes);
        if (BoundsOf(targets)?.Center is not { } c)
            return;
        Transform(targets, Matrix2D.Rotate(clockwise ? 90 : -90, c.X, c.Y), clockwise ? "Rotate 90° Clockwise" : "Rotate 90° Counterclockwise");
    }

    // ---- Align and distribute ----

    private List<SvgElement> Ordered(IEnumerable<SvgElement>? nodes)
    {
        var all = Targets(nodes);
        var set = all.ToHashSet();
        return all.Where(n => !n.Ancestors().Any(set.Contains)).ToList();
    }

    private VRect Page => Root.ViewBox ?? new VRect(0, 0, Root.UserSize.Width, Root.UserSize.Height);

    private void MoveEach(IReadOnlyList<(SvgElement Node, double Dx, double Dy)> moves, string name)
    {
        var changes = moves.Where(m => Math.Abs(m.Dx) > 1e-9 || Math.Abs(m.Dy) > 1e-9).ToList();
        if (changes.Count == 0)
            return;
        var tx = Begin(name);
        tx.Edit(changes.Select(m => m.Node), () =>
        {
            foreach (var (node, dx, dy) in changes)
                SvgTransformer.Apply(node, ToParentSpace(node, Matrix2D.Translate(dx, dy)), Provider);
        });
        tx.Commit();
    }

    /// <summary>Moves the objects so that their <paramref name="edge"/> lines up with that of the reference.</summary>
    public void Align(IEnumerable<SvgElement>? nodes, AlignEdge edge, AlignRelativeTo relativeTo)
    {
        var targets = Ordered(nodes);
        var boxes = targets.Select(n => (Node: n, Box: SvgBounds.InDocument(n, Provider))).Where(b => b.Box is not null)
            .Select(b => (b.Node, Box: b.Box!.Value)).ToList();
        if (boxes.Count == 0 || (boxes.Count < 2 && relativeTo != AlignRelativeTo.Page))
            return;
        var reference = relativeTo switch
        {
            AlignRelativeTo.FirstSelected => boxes[0].Box,
            AlignRelativeTo.LastSelected => boxes[^1].Box,
            AlignRelativeTo.Biggest => boxes.MaxBy(b => b.Box.Width * b.Box.Height).Box,
            AlignRelativeTo.Page => Page,
            _ => boxes.Select(b => b.Box).Aggregate((a, b) => a.Union(b)),
        };
        var moves = boxes.Select(b =>
        {
            var (dx, dy) = edge switch
            {
                AlignEdge.Left => (reference.Left - b.Box.Left, 0.0),
                AlignEdge.CenterHorizontal => (reference.Center.X - b.Box.Center.X, 0.0),
                AlignEdge.Right => (reference.Right - b.Box.Right, 0.0),
                AlignEdge.Top => (0.0, reference.Top - b.Box.Top),
                AlignEdge.CenterVertical => (0.0, reference.Center.Y - b.Box.Center.Y),
                _ => (0.0, reference.Bottom - b.Box.Bottom),
            };
            return (b.Node, dx, dy);
        }).ToList();
        MoveEach(moves, "Align");
    }

    /// <summary>Spaces the objects evenly between the outermost two, which stay where they are.</summary>
    public void Distribute(IEnumerable<SvgElement>? nodes, DistributeMode mode)
    {
        var boxes = Ordered(nodes).Select(n => (Node: n, Box: SvgBounds.InDocument(n, Provider))).Where(b => b.Box is not null)
            .Select(b => (b.Node, Box: b.Box!.Value)).ToList();
        if (boxes.Count < 3)
            return;
        var horizontal = mode is DistributeMode.Left or DistributeMode.CenterHorizontal or DistributeMode.Right or DistributeMode.GapHorizontal;
        double Start(VRect b) => horizontal ? b.Left : b.Top;
        double Size(VRect b) => horizontal ? b.Width : b.Height;
        double Anchor(VRect b) => mode switch
        {
            DistributeMode.Left or DistributeMode.Top => Start(b),
            DistributeMode.CenterHorizontal or DistributeMode.CenterVertical => Start(b) + Size(b) / 2,
            _ => Start(b) + Size(b),
        };
        var sorted = boxes.OrderBy(b => mode is DistributeMode.GapHorizontal or DistributeMode.GapVertical ? Start(b.Box) : Anchor(b.Box)).ToList();
        var moves = new List<(SvgElement Node, double Dx, double Dy)>();
        double[] targets;
        if (mode is DistributeMode.GapHorizontal or DistributeMode.GapVertical)
        {
            var total = Start(sorted[^1].Box) + Size(sorted[^1].Box) - Start(sorted[0].Box);
            var gap = (total - sorted.Sum(b => Size(b.Box))) / (sorted.Count - 1);
            targets = new double[sorted.Count];
            var position = Start(sorted[0].Box);
            for (var i = 0; i < sorted.Count; i++)
            {
                targets[i] = position;
                position += Size(sorted[i].Box) + gap;
            }
            for (var i = 0; i < sorted.Count; i++)
            {
                var shift = targets[i] - Start(sorted[i].Box);
                moves.Add((sorted[i].Node, horizontal ? shift : 0, horizontal ? 0 : shift));
            }
        }
        else
        {
            var first = Anchor(sorted[0].Box);
            var last = Anchor(sorted[^1].Box);
            for (var i = 0; i < sorted.Count; i++)
            {
                var shift = first + (last - first) * i / (sorted.Count - 1) - Anchor(sorted[i].Box);
                moves.Add((sorted[i].Node, horizontal ? shift : 0, horizontal ? 0 : shift));
            }
        }
        MoveEach(moves, "Distribute");
    }

    // ---- Path menu ----

    private static readonly string[] CarriedStyle = ["opacity", "clip-path", "mask", "filter"];

    private static VectorPath InDocumentSpace(SvgShape shape) => shape.CreatePath().Transformed(SvgBounds.ToDocument(shape));

    /// <summary>A path with the style and attributes of <paramref name="donor"/> and the outline <paramref name="document"/> (document space).</summary>
    private SvgPath PathLike(SvgShape donor, VectorPath document, SvgContainer parent)
    {
        var path = PathFromShape(donor);
        path.SetAttribute("transform", null);
        path.Style.Set("fill-rule", "nonzero");
        path.Style.Set("clip-rule", null);
        var toParent = WorldOf(parent).Invert() ?? Matrix2D.Identity;
        path.SetPath(document.Transformed(toParent), 4);
        return path;
    }

    private static List<SvgShape> Shapes(List<SvgElement> nodes) => nodes.OfType<SvgShape>().ToList();

    private static void RequireShapes(List<SvgElement> nodes, string what)
    {
        if (nodes.Any(n => n is not SvgShape))
            throw new InvalidOperationException($"{what} works on shapes and paths; convert text and groups to paths first.");
    }

    /// <summary>
    /// Boolean operation on the selected shapes (in document order; the bottom one gives the style and, for Difference, is the
    /// one the others are cut from). The shapes are replaced by the result (nothing, when it is empty); returns the new objects.
    /// </summary>
    public IReadOnlyList<SvgElement> ApplyPathOperation(PathOperation operation, IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes).Where(n => n is not SvgDefs).ToList();
        if (targets.Count < 2)
            return [];
        RequireShapes(targets, "Path operations");
        var shapes = Shapes(targets);
        var donor = shapes[0];
        var parent = donor.Parent!;
        var index = parent.IndexOf(donor);
        var operands = shapes.Select(s => (InDocumentSpace(s), StyleResolver.ComputeFor(s).FillRule)).ToList();

        var results = new List<VectorPath>();
        switch (operation)
        {
            case PathOperation.Division:
                results.AddRange(PathBoolean.Divide(operands[0].Item1, operands[0].FillRule, Union(operands.Skip(1)), FillRule.NonZero));
                break;
            case PathOperation.Combine:
                var combined = new VectorPath();
                foreach (var (path, _) in operands)
                    combined.Append(path);
                results.Add(combined);
                break;
            default:
                var op = operation switch
                {
                    PathOperation.Union => BooleanOperation.Union,
                    PathOperation.Intersection => BooleanOperation.Intersection,
                    PathOperation.Difference => BooleanOperation.Difference,
                    _ => BooleanOperation.Exclusion,
                };
                var result = PathBoolean.Combine(operands.Select(o => (o.Item1, o.FillRule)).ToList(), op);
                if (!result.IsEmpty)
                    results.Add(result);
                break;
        }

        var name = operation switch
        {
            PathOperation.Union => "Union",
            PathOperation.Difference => "Difference",
            PathOperation.Intersection => "Intersection",
            PathOperation.Exclusion => "Exclusion",
            PathOperation.Division => "Division",
            _ => "Combine",
        };
        var tx = Begin(name);
        var created = new List<SvgElement>();
        // Combine keeps the fill rule of the donor, since it does not merge the outlines.
        foreach (var result in results)
        {
            var path = PathLike(donor, result, parent);
            if (operation == PathOperation.Combine)
                path.Style.Set("fill-rule", StyleResolver.ComputeFor(donor).FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero");
            tx.Insert(parent, index + created.Count + 1, path);
            created.Add(path);
        }
        foreach (var shape in shapes)
            tx.Remove(shape);
        tx.Commit(created);
        return created;
    }

    private static VectorPath Union(IEnumerable<(VectorPath Path, FillRule Rule)> operands) =>
        PathBoolean.Combine(operands.ToList(), BooleanOperation.Union);

    /// <summary>Splits each path into one path per sub-path (the holes of a shape become paths of their own).</summary>
    public IReadOnlyList<SvgElement> BreakApart(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes).Where(n => n is SvgShape).Cast<SvgShape>().ToList();
        var created = new List<SvgElement>();
        var tx = Begin("Break Apart");
        foreach (var shape in targets)
        {
            var parts = PathBoolean.BreakApart(InDocumentSpace(shape));
            if (parts.Count < 2)
            {
                created.Add(shape);
                continue;
            }
            var parent = shape.Parent!;
            var index = parent.IndexOf(shape);
            for (var i = 0; i < parts.Count; i++)
            {
                var path = PathLike(shape, parts[i], parent);
                path.Style.Set("fill-rule", StyleResolver.ComputeFor(shape).FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero");
                path.Id = i == 0 ? shape.Id : null;
                tx.Insert(parent, index + 1 + i, path);
                created.Add(path);
            }
            tx.Remove(shape);
        }
        tx.Commit(created);
        return created;
    }

    /// <summary>Replaces the outline of each shape by a path of fewer nodes within <paramref name="tolerance"/> user units.</summary>
    public void Simplify(IEnumerable<SvgElement>? nodes = null, double tolerance = 0.5) =>
        EditPaths("Simplify", nodes, path => PathOperations.Simplify(path, tolerance));

    /// <summary>Reverses the direction of each path (useful for holes and for markers).</summary>
    public void Reverse(IEnumerable<SvgElement>? nodes = null) => EditPaths("Reverse", nodes, PathOperations.Reverse);

    private void EditPaths(string name, IEnumerable<SvgElement>? nodes, Func<VectorPath, VectorPath> change)
    {
        var targets = TopLevel(nodes).OfType<SvgShape>().ToList();
        if (targets.Count == 0)
            return;
        var tx = Begin(name);
        // Shapes other than paths become paths first, as one step with the change.
        var result = new List<SvgElement>();
        foreach (var shape in targets)
        {
            if (shape is SvgPath existing)
            {
                tx.Edit([existing], () => existing.SetPath(change(existing.CreatePath()), 4));
                result.Add(existing);
                continue;
            }
            var path = PathFromShape(shape);
            path.SetPath(change(shape.CreatePath()), 4);
            var parent = shape.Parent!;
            tx.Insert(parent, parent.IndexOf(shape) + 1, path);
            tx.Remove(shape);
            result.Add(path);
        }
        tx.Commit(result);
    }

    /// <summary>
    /// Replaces each stroked shape's stroke by a filled outline (the stroke's paint becomes the fill). A shape that also has a
    /// fill keeps it, without its stroke, below the outline. Gradients on the stroke are measured on the new outline's box.
    /// </summary>
    public IReadOnlyList<SvgElement> StrokeToPath(IEnumerable<SvgElement>? nodes = null)
    {
        var targets = TopLevel(nodes).OfType<SvgShape>().ToList();
        var tx = Begin("Stroke to Path");
        var created = new List<SvgElement>();
        foreach (var shape in targets)
        {
            var style = StyleResolver.ComputeFor(shape);
            if (style.Stroke.Kind == PaintKind.None || style.StrokeWidth <= 0)
                continue;
            var local = shape.CreatePath();
            var tolerance = Math.Clamp(style.StrokeWidth / 50, 0.01, 0.1);
            var pieces = Stroker.Stroke(Flattener.Flatten(local, Matrix2D.Identity, tolerance),
                new StrokeStyle(style.StrokeWidth, style.LineCap, style.LineJoin, style.MiterLimit, style.DashArray, style.DashOffset), tolerance);
            var outline = new VectorPath();
            foreach (var piece in pieces)
                outline.Append(VectorPath.FromPolyline(piece.Points, true));
            var merged = PathBoolean.Combine([(outline, FillRule.NonZero)], BooleanOperation.Union, tolerance);
            if (merged.IsEmpty)
                continue;

            var stroke = new SvgPath { Id = Root.NewId("stroke") };
            stroke.SetPath(merged, 4);
            stroke.SetAttribute("transform", shape.GetAttribute("transform"));
            foreach (var property in CarriedStyle)
                if (shape.Style.Get(property) is { } value)
                    stroke.Style.Set(property, value);
            stroke.Style.Set("fill", style.Stroke.ToText());
            if (style.StrokeOpacity < 1)
                stroke.Style.Set("fill-opacity", NumberFormat.Format(style.StrokeOpacity, 4));
            stroke.Style.Set("stroke", "none");

            var parent = shape.Parent!;
            var index = parent.IndexOf(shape);
            var hasFill = style.Fill.Kind != PaintKind.None && shape is not SvgLine;   // a line has nothing to fill
            if (hasFill)
            {
                tx.Edit([shape], () => shape.Style.Set("stroke", "none"));
                tx.Insert(parent, index + 1, stroke);
                created.Add(shape);
            }
            else
            {
                stroke.Id = shape.Id ?? stroke.Id;
                tx.Insert(parent, index + 1, stroke);
                tx.Remove(shape);
            }
            created.Add(stroke);
        }
        tx.Commit(created);
        return created;
    }
}
