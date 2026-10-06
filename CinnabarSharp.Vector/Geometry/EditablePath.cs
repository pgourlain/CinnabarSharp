namespace CinnabarSharp.Vector;

public enum NodeType
{
    /// <summary>The two handles move independently.</summary>
    Corner,

    /// <summary>The handles stay on one line; their lengths are free.</summary>
    Smooth,

    /// <summary>The handles stay on one line and have the same length.</summary>
    Symmetric,
}

/// <summary>A node of a path being edited: its point and the handles of the segments on each side (null: no handle, a straight side).</summary>
public sealed class PathNode
{
    public PathNode(VPoint point, VPoint? handleIn = null, VPoint? handleOut = null, NodeType type = NodeType.Corner)
    {
        Point = point;
        In = handleIn;
        Out = handleOut;
        Type = type;
    }

    public VPoint Point { get; set; }

    /// <summary>The handle towards the previous node (absolute position).</summary>
    public VPoint? In { get; set; }

    /// <summary>The handle towards the next node (absolute position).</summary>
    public VPoint? Out { get; set; }

    public NodeType Type { get; set; }

    public PathNode Clone() => new(Point, In, Out, Type);

    /// <summary>Moves the node with its handles.</summary>
    public void MoveBy(VVector delta)
    {
        Point += delta;
        if (In is { } i)
            In = i + delta;
        if (Out is { } o)
            Out = o + delta;
    }

    /// <summary>Moves the incoming handle; the other follows when the node is smooth or symmetric.</summary>
    public void SetIn(VPoint handle)
    {
        In = handle;
        MirrorTo(Out, handle, isOut: true);
    }

    public void SetOut(VPoint handle)
    {
        Out = handle;
        MirrorTo(In, handle, isOut: false);
    }

    private void MirrorTo(VPoint? other, VPoint moved, bool isOut)
    {
        if (Type == NodeType.Corner)
            return;
        var direction = Point - moved;
        if (direction.Length < 1e-9)
            return;
        var length = Type == NodeType.Symmetric ? direction.Length : other is { } o ? (o - Point).Length : direction.Length;
        if (other is null && Type == NodeType.Smooth)
            return;
        var mirrored = Point + direction.Normalized() * length;
        if (isOut)
            Out = mirrored;
        else
            In = mirrored;
    }

    /// <summary>Applies a node type: smooth and symmetric align the two handles on one line (symmetric also equalizes their lengths); a node with fewer than two handles only remembers the type.</summary>
    public void SetType(NodeType type)
    {
        Type = type;
        if (type == NodeType.Corner)
            return;
        if (In is { } i && Out is { } o)
        {
            // Keep the incoming direction, mirror the other; symmetric also equalizes the lengths.
            var direction = (Point - i).Normalized();
            var length = type == NodeType.Symmetric ? ((i - Point).Length + (o - Point).Length) / 2 : (o - Point).Length;
            Out = Point + direction * length;
            if (type == NodeType.Symmetric)
                In = Point - direction * length;
        }
    }
}

/// <summary>A figure: nodes in order, open or closed (a closed one also has a segment from the last node to the first).</summary>
public sealed class EditableFigure
{
    public List<PathNode> Nodes { get; } = [];

    public bool Closed { get; set; }

    public int SegmentCount => Closed ? Nodes.Count : Math.Max(0, Nodes.Count - 1);

    /// <summary>The nodes at the ends of segment <paramref name="index"/>: from <c>index</c> to <c>index + 1</c> (wrapping when closed).</summary>
    public (PathNode From, PathNode To) Segment(int index) => (Nodes[index], Nodes[(index + 1) % Nodes.Count]);

    /// <summary>A straight segment: neither end has a handle on that side.</summary>
    public bool IsLine(int index)
    {
        var (from, to) = Segment(index);
        return from.Out is null && to.In is null;
    }
}

/// <summary>
/// A path as editable nodes and handles (the Node tool and the Pen tool). Lines, cubic and quadratic curves and arcs
/// all become nodes; arcs and quadratics are converted to cubics when read.
/// </summary>
public sealed class EditablePath
{
    public List<EditableFigure> Figures { get; } = [];

    public static EditablePath From(VectorPath path)
    {
        var result = new EditablePath();
        EditableFigure? figure = null;
        var current = default(VPoint);
        var start = default(VPoint);

        void Begin(VPoint p)
        {
            figure = new EditableFigure();
            figure.Nodes.Add(new PathNode(p));
            result.Figures.Add(figure);
            current = start = p;
        }

        void AddCubic(VPoint c1, VPoint c2, VPoint end)
        {
            if (figure is null)
                Begin(current);
            figure!.Nodes[^1].Out = c1;
            figure.Nodes.Add(new PathNode(end, handleIn: c2));
            current = end;
        }

        foreach (var segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.MoveTo:
                    Begin(segment.End);
                    break;
                case SegmentKind.LineTo:
                    if (figure is null)
                        Begin(current);
                    figure!.Nodes.Add(new PathNode(segment.End));
                    current = segment.End;
                    break;
                case SegmentKind.CubicTo:
                    AddCubic(segment.C1, segment.C2, segment.End);
                    break;
                case SegmentKind.QuadTo:
                    AddCubic(current + (segment.C1 - current) * (2.0 / 3), segment.End + (segment.C1 - segment.End) * (2.0 / 3), segment.End);
                    break;
                case SegmentKind.ArcTo:
                    if (segment.Rx == 0 || segment.Ry == 0 || current == segment.End)
                    {
                        if (figure is null)
                            Begin(current);
                        figure!.Nodes.Add(new PathNode(segment.End));
                        current = segment.End;
                    }
                    else
                    {
                        foreach (var cubic in ArcConverter.ToCubics(current, segment))
                            AddCubic(cubic.C1, cubic.C2, cubic.End);
                    }
                    break;
                case SegmentKind.Close:
                    if (figure is not null)
                    {
                        figure.Closed = true;
                        // A last node on top of the first is the same node: its incoming handle moves to the first.
                        if (figure.Nodes.Count > 1 && figure.Nodes[^1].Point.DistanceTo(figure.Nodes[0].Point) < 1e-9)
                        {
                            figure.Nodes[0].In = figure.Nodes[^1].In;
                            figure.Nodes.RemoveAt(figure.Nodes.Count - 1);
                        }
                    }
                    figure = null;
                    current = start;
                    break;
            }
        }
        foreach (var f in result.Figures)
            foreach (var node in f.Nodes)
                node.Type = InferType(node);
        return result;
    }

    private static NodeType InferType(PathNode node)
    {
        if (node.In is not { } i || node.Out is not { } o)
            return NodeType.Corner;
        var a = (node.Point - i);
        var b = (o - node.Point);
        if (a.Length < 1e-9 || b.Length < 1e-9)
            return NodeType.Corner;
        var cross = Math.Abs(a.Normalized().Cross(b.Normalized()));
        if (cross > 1e-3 || a.Normalized().Dot(b.Normalized()) < 0)
            return NodeType.Corner;
        return Math.Abs(a.Length - b.Length) < 1e-6 * Math.Max(a.Length, 1) ? NodeType.Symmetric : NodeType.Smooth;
    }

    public VectorPath ToPath()
    {
        var path = new VectorPath();
        foreach (var figure in Figures)
        {
            if (figure.Nodes.Count == 0)
                continue;
            path.MoveTo(figure.Nodes[0].Point);
            for (var i = 0; i < figure.SegmentCount; i++)
            {
                var (from, to) = figure.Segment(i);
                if (from.Out is null && to.In is null)
                {
                    // The straight way back to the start is what Close draws.
                    if (!(figure.Closed && i == figure.SegmentCount - 1))
                        path.LineTo(to.Point);
                }
                else
                    path.CubicTo(from.Out ?? from.Point, to.In ?? to.Point, to.Point);
            }
            if (figure.Closed)
                path.Close();
        }
        return path;
    }

    public EditablePath Clone()
    {
        var copy = new EditablePath();
        foreach (var figure in Figures)
        {
            var f = new EditableFigure { Closed = figure.Closed };
            f.Nodes.AddRange(figure.Nodes.Select(n => n.Clone()));
            copy.Figures.Add(f);
        }
        return copy;
    }

    // ---- Editing operations ----

    /// <summary>Splits segment <paramref name="segment"/> of the figure at parameter <paramref name="t"/> with a new node; the curve keeps its shape.</summary>
    public static PathNode InsertNode(EditableFigure figure, int segment, double t)
    {
        var (from, to) = figure.Segment(segment);
        PathNode added;
        if (from.Out is null && to.In is null)
        {
            added = new PathNode(from.Point.Lerp(to.Point, t));
        }
        else
        {
            // de Casteljau
            var p0 = from.Point;
            var p1 = from.Out ?? from.Point;
            var p2 = to.In ?? to.Point;
            var p3 = to.Point;
            var a = p0.Lerp(p1, t);
            var b = p1.Lerp(p2, t);
            var c = p2.Lerp(p3, t);
            var d = a.Lerp(b, t);
            var e = b.Lerp(c, t);
            var f = d.Lerp(e, t);
            from.Out = a;
            to.In = c;
            added = new PathNode(f, d, e, NodeType.Smooth);
        }
        figure.Nodes.Insert(segment + 1, added);
        return added;
    }

    /// <summary>The closest point of the figure's segments to <paramref name="p"/>: the segment and parameter, and the distance.</summary>
    public static (int Segment, double T, double Distance)? Nearest(EditableFigure figure, VPoint p)
    {
        (int, double, double)? best = null;
        for (var i = 0; i < figure.SegmentCount; i++)
        {
            var (from, to) = figure.Segment(i);
            var (t, distance) = NearestOnSegment(from, to, p);
            if (best is null || distance < best.Value.Item3)
                best = (i, t, distance);
        }
        return best;
    }

    private static (double T, double Distance) NearestOnSegment(PathNode from, PathNode to, VPoint p)
    {
        if (from.Out is null && to.In is null)
        {
            // A straight segment is parameterized linearly (a cubic with its handles on its ends is not).
            var ab = to.Point - from.Point;
            var lengthSquared = ab.Dot(ab);
            var line = lengthSquared == 0 ? 0 : Math.Clamp(ab.Dot(p - from.Point) / lengthSquared, 0, 1);
            return (line, p.DistanceTo(from.Point + ab * line));
        }
        const int Samples = 40;
        var p0 = from.Point;
        var p1 = from.Out ?? from.Point;
        var p2 = to.In ?? to.Point;
        var p3 = to.Point;
        double bestT = 0, best = double.MaxValue;
        for (var i = 0; i <= Samples; i++)
        {
            var t = (double)i / Samples;
            var q = At(p0, p1, p2, p3, t);
            var d = q.DistanceSquaredTo(p);
            if (d < best)
            {
                best = d;
                bestT = t;
            }
        }
        // Refine around the best sample.
        var lo = Math.Max(0, bestT - 1.0 / Samples);
        var hi = Math.Min(1, bestT + 1.0 / Samples);
        for (var i = 0; i <= 20; i++)
        {
            var t = lo + (hi - lo) * i / 20;
            var d = At(p0, p1, p2, p3, t).DistanceSquaredTo(p);
            if (d < best)
            {
                best = d;
                bestT = t;
            }
        }
        return (bestT, Math.Sqrt(best));
    }

    public static VPoint At(VPoint p0, VPoint p1, VPoint p2, VPoint p3, double t)
    {
        var mt = 1 - t;
        var (a, b, c, d) = (mt * mt * mt, 3 * mt * mt * t, 3 * mt * t * t, t * t * t);
        return new VPoint(a * p0.X + b * p1.X + c * p2.X + d * p3.X, a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
    }

    /// <summary>
    /// Removes a node keeping the shape as close as possible: when both neighbouring sides are curves, their handles are
    /// stretched to span the gap (by the ratio of the lengths), so the two curves become one that follows them.
    /// </summary>
    public static void RemoveNode(EditableFigure figure, int index)
    {
        var n = figure.Nodes.Count;
        if (n <= 1)
        {
            figure.Nodes.Clear();
            return;
        }
        var removed = figure.Nodes[index];
        var hasPrevious = figure.Closed || index > 0;
        var hasNext = figure.Closed || index < n - 1;
        if (hasPrevious && hasNext && n > 2)
        {
            var previous = figure.Nodes[(index + n - 1) % n];
            var next = figure.Nodes[(index + 1) % n];
            var first = previous.Point.DistanceTo(removed.Point);
            var second = removed.Point.DistanceTo(next.Point);
            var total = first + second;
            if (previous.Out is { } po && first > 1e-9)
                previous.Out = previous.Point + (po - previous.Point) * (total / first);
            if (next.In is { } ni && second > 1e-9)
                next.In = next.Point + (ni - next.Point) * (total / second);
        }
        figure.Nodes.RemoveAt(index);
        if (figure.Closed && figure.Nodes.Count < 2)
            figure.Closed = false;
    }

    /// <summary>Makes segment <paramref name="segment"/> straight (drops the handles on it) or curved (handles a third of the way along).</summary>
    public static void SetSegmentKind(EditableFigure figure, int segment, bool line)
    {
        var (from, to) = figure.Segment(segment);
        if (line)
        {
            from.Out = null;
            to.In = null;
            if (from.Type != NodeType.Corner && from.In is null)
                from.Type = NodeType.Corner;
            if (to.Type != NodeType.Corner && to.Out is null)
                to.Type = NodeType.Corner;
            return;
        }
        if (from.Out is null)
            from.Out = from.Point.Lerp(to.Point, 1.0 / 3);
        if (to.In is null)
            to.In = to.Point.Lerp(from.Point, 1.0 / 3);
    }

    /// <summary>Opens a closed figure at a node or splits an open one into two there (Break nodes). Returns the new figure when one was made.</summary>
    public EditableFigure? BreakAt(EditableFigure figure, int index)
    {
        if (figure.Closed)
        {
            // Rotate so the node is first and copy it to the end: an open figure from that node round to itself.
            var nodes = figure.Nodes.Skip(index).Concat(figure.Nodes.Take(index)).ToList();
            var last = nodes[0].Clone();
            last.Out = null;
            nodes[0].In = null;
            nodes.Add(last);
            figure.Nodes.Clear();
            figure.Nodes.AddRange(nodes);
            figure.Closed = false;
            return null;
        }
        if (index <= 0 || index >= figure.Nodes.Count - 1)
            return null;
        var tail = new EditableFigure();
        var split = figure.Nodes[index];
        var copy = split.Clone();
        copy.In = null;
        split.Out = null;
        tail.Nodes.Add(copy);
        tail.Nodes.AddRange(figure.Nodes.Skip(index + 1));
        figure.Nodes.RemoveRange(index + 1, figure.Nodes.Count - index - 1);
        Figures.Insert(Figures.IndexOf(figure) + 1, tail);
        return tail;
    }

    /// <summary>
    /// Joins two end nodes of open figures (Join nodes): the same figure closes; two figures become one. The ends meet half way.
    /// </summary>
    public bool Join(EditableFigure a, bool atEndOfA, EditableFigure b, bool atEndOfB)
    {
        if (a == b)
        {
            if (a.Closed || a.Nodes.Count < 2)
                return false;
            var (first, last) = (a.Nodes[0], a.Nodes[^1]);
            var middle = first.Point.Lerp(last.Point, 0.5);
            first.MoveBy(middle - first.Point);
            first.In = last.In is { } i ? i + (middle - last.Point) : null;
            a.Nodes.RemoveAt(a.Nodes.Count - 1);
            a.Closed = true;
            return true;
        }
        if (a.Closed || b.Closed)
            return false;
        if (!atEndOfA)
            a.Nodes.Reverse();
        foreach (var node in a.Nodes)
            (node.In, node.Out) = !atEndOfA ? (node.Out, node.In) : (node.In, node.Out);
        if (atEndOfB)
        {
            b.Nodes.Reverse();
            foreach (var node in b.Nodes)
                (node.In, node.Out) = (node.Out, node.In);
        }
        var tail = a.Nodes[^1];
        var head = b.Nodes[0];
        var meet = tail.Point.Lerp(head.Point, 0.5);
        var shiftTail = meet - tail.Point;
        var shiftHead = meet - head.Point;
        tail.Point = meet;
        tail.Out = head.Out is { } o ? o + shiftHead : null;
        if (tail.In is { } ti)
            tail.In = ti + shiftTail;
        a.Nodes.AddRange(b.Nodes.Skip(1));
        Figures.Remove(b);
        return true;
    }
}
