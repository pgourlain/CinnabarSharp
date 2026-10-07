namespace CinnabarSharp.Vector;

public enum BooleanOperation
{
    /// <summary>Everything covered by any operand.</summary>
    Union,

    /// <summary>What every operand covers.</summary>
    Intersection,

    /// <summary>The first operand minus all the others.</summary>
    Difference,

    /// <summary>What an odd number of operands cover.</summary>
    Exclusion,
}

/// <summary>
/// Boolean operations on filled paths. The paths are flattened and their points snapped to a 1/128 grid, so the arithmetic
/// on coordinates is exact (integers); every edge is split where it meets another one (repeated until snapping creates
/// no new crossing); an edge belongs to the result when the result covers one of its sides and not the other, which is
/// decided from the winding numbers of the operands at a point just beside it; the kept edges are chained into closed
/// outlines, with the inside on the left. Runs of short, gently turning edges are fitted back into cubic Béziers.
/// Deterministic: only integer math and IEEE arithmetic. Written for this project (no third-party code).
/// </summary>
public static class PathBoolean
{
    private const double Grid = 128;
    private const double Offset = 0.05;
    private const int MaxPasses = 12;

    public static VectorPath Combine(VectorPath a, FillRule ruleA, VectorPath b, FillRule ruleB, BooleanOperation operation,
        double tolerance = 0.05) =>
        Combine([(a, ruleA), (b, ruleB)], operation, tolerance);

    /// <summary>The result as one path (fill with <see cref="FillRule.NonZero"/>: outer outlines run one way, holes the other).</summary>
    public static VectorPath Combine(IReadOnlyList<(VectorPath Path, FillRule Rule)> operands, BooleanOperation operation,
        double tolerance = 0.05)
    {
        if (operands.Count == 0)
            return new VectorPath();
        var vertices = new Vertices();
        var segments = new List<Seg>();
        for (var k = 0; k < operands.Count; k++)
        {
            foreach (var polyline in Flattener.Flatten(operands[k].Path, Matrix2D.Identity, tolerance))
            {
                var ring = new List<int>();
                foreach (var p in polyline.Points)
                {
                    var v = vertices.Add(p);
                    if (ring.Count == 0 || ring[^1] != v)
                        ring.Add(v);
                }
                while (ring.Count > 1 && ring[0] == ring[^1])
                    ring.RemoveAt(ring.Count - 1);
                if (ring.Count < 3)
                    continue;
                for (var i = 0; i < ring.Count; i++)
                    segments.Add(new Seg(ring[i], ring[(i + 1) % ring.Count], k));
            }
        }
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            if (!Node(vertices, ref segments))
                break;
        }
        var rings = Trace(vertices, BuildEdges(segments, operands.Count), operands.Select(o => o.Rule).ToArray(), operation);
        var result = new VectorPath();
        foreach (var ring in rings)
            AppendRing(result, ring.Select(v => vertices.Point(v)).ToList(), tolerance);
        return result;
    }

    /// <summary>One path per sub-path (the holes of a path become paths of their own).</summary>
    public static IReadOnlyList<VectorPath> BreakApart(VectorPath path)
    {
        var parts = new List<VectorPath>();
        var current = new VectorPath();
        foreach (var segment in path.Segments)
        {
            if (segment.Kind == SegmentKind.MoveTo && !current.IsEmpty)
            {
                parts.Add(current);
                current = new VectorPath();
            }
            current.Add(segment);
            if (segment.Kind == SegmentKind.Close)
            {
                parts.Add(current);
                current = new VectorPath();
            }
        }
        if (!current.IsEmpty)
            parts.Add(current);
        return parts.Where(p => p.Segments.Count > 1 || p.Segments.Any(s => s.Kind != SegmentKind.MoveTo)).ToList();
    }

    /// <summary>Division: the first operand cut along the outline of the others; the pieces are returned separately.</summary>
    public static IReadOnlyList<VectorPath> Divide(VectorPath a, FillRule ruleA, VectorPath b, FillRule ruleB, double tolerance = 0.05)
    {
        var pieces = new List<VectorPath>();
        pieces.AddRange(BreakApart(Combine(a, ruleA, b, ruleB, BooleanOperation.Difference, tolerance)));
        pieces.AddRange(BreakApart(Combine(a, ruleA, b, ruleB, BooleanOperation.Intersection, tolerance)));
        return pieces;
    }

    // ---- Vertices and segments ----

    private readonly record struct Seg(int From, int To, int Operand);

    private sealed class Vertices
    {
        private readonly List<(long X, long Y)> _points = [];
        private readonly Dictionary<(long, long), int> _ids = [];

        public int Count => _points.Count;

        public (long X, long Y) this[int id] => _points[id];

        public int Add(VPoint p) => Add((long)Math.Round(p.X * Grid, MidpointRounding.AwayFromZero),
            (long)Math.Round(p.Y * Grid, MidpointRounding.AwayFromZero));

        public int Add(long x, long y)
        {
            if (_ids.TryGetValue((x, y), out var id))
                return id;
            id = _points.Count;
            _points.Add((x, y));
            _ids[(x, y)] = id;
            return id;
        }

        public VPoint Point(int id) => new(_points[id].X / Grid, _points[id].Y / Grid);
    }

    private static Int128 Cross(long ax, long ay, long bx, long by) => (Int128)ax * by - (Int128)ay * bx;

    private static Int128 Cross(long ox, long oy, long ax, long ay, long bx, long by) => Cross(ax - ox, ay - oy, bx - ox, by - oy);

    private static bool OnSegment(long px, long py, long ax, long ay, long bx, long by) =>
        px >= Math.Min(ax, bx) && px <= Math.Max(ax, bx) && py >= Math.Min(ay, by) && py <= Math.Max(ay, by);

    // ---- Noding ----

    /// <summary>Splits segments where they meet; returns true when something was split.</summary>
    private static bool Node(Vertices vertices, ref List<Seg> segmentList)
    {
        var segments = segmentList;
        var n = segments.Count;
        var splits = new Dictionary<int, HashSet<int>>();
        void Split(int segment, int vertex)
        {
            var s = segments[segment];
            if (vertex == s.From || vertex == s.To)
                return;
            if (!splits.TryGetValue(segment, out var set))
                splits[segment] = set = [];
            set.Add(vertex);
        }

        var minX = new long[n];
        var maxX = new long[n];
        for (var i = 0; i < n; i++)
        {
            var a = vertices[segments[i].From];
            var b = vertices[segments[i].To];
            minX[i] = Math.Min(a.X, b.X);
            maxX[i] = Math.Max(a.X, b.X);
        }
        var order = Enumerable.Range(0, n).OrderBy(i => minX[i]).ThenBy(i => i).ToArray();
        for (var oi = 0; oi < n; oi++)
        {
            var i = order[oi];
            var p1 = vertices[segments[i].From];
            var p2 = vertices[segments[i].To];
            for (var oj = oi + 1; oj < n; oj++)
            {
                var j = order[oj];
                if (minX[j] > maxX[i])
                    break;
                var q1 = vertices[segments[j].From];
                var q2 = vertices[segments[j].To];
                if (Math.Max(p1.Y, p2.Y) < Math.Min(q1.Y, q2.Y) || Math.Min(p1.Y, p2.Y) > Math.Max(q1.Y, q2.Y))
                    continue;
                Intersect(vertices, segments, i, j, p1, p2, q1, q2, Split);
            }
        }
        if (splits.Count == 0)
            return false;

        var result = new List<Seg>(n + splits.Count * 2);
        for (var i = 0; i < n; i++)
        {
            var s = segments[i];
            if (!splits.TryGetValue(i, out var set))
            {
                result.Add(s);
                continue;
            }
            var from = vertices[s.From];
            var to = vertices[s.To];
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var chain = set.OrderBy(v => (double)(vertices[v].X - from.X) * dx + (double)(vertices[v].Y - from.Y) * dy)
                .ThenBy(v => v).ToList();
            var previous = s.From;
            foreach (var v in chain.Append(s.To))
            {
                if (v != previous)
                    result.Add(new Seg(previous, v, s.Operand));
                previous = v;
            }
        }
        segmentList = result;
        return true;
    }

    private static void Intersect(Vertices vertices, List<Seg> segments, int i, int j, (long X, long Y) p1, (long X, long Y) p2,
        (long X, long Y) q1, (long X, long Y) q2, Action<int, int> split)
    {
        var si = segments[i];
        var sj = segments[j];
        var d1 = Cross(p1.X, p1.Y, p2.X, p2.Y, q1.X, q1.Y);
        var d2 = Cross(p1.X, p1.Y, p2.X, p2.Y, q2.X, q2.Y);
        var d3 = Cross(q1.X, q1.Y, q2.X, q2.Y, p1.X, p1.Y);
        var d4 = Cross(q1.X, q1.Y, q2.X, q2.Y, p2.X, p2.Y);
        if ((d1 > 0 && d2 > 0) || (d1 < 0 && d2 < 0) || (d3 > 0 && d4 > 0) || (d3 < 0 && d4 < 0))
            return;
        if (d1 == 0 && d2 == 0)
        {
            // Collinear: each end that lies on the other segment splits it.
            if (OnSegment(q1.X, q1.Y, p1.X, p1.Y, p2.X, p2.Y))
                split(i, sj.From);
            if (OnSegment(q2.X, q2.Y, p1.X, p1.Y, p2.X, p2.Y))
                split(i, sj.To);
            if (OnSegment(p1.X, p1.Y, q1.X, q1.Y, q2.X, q2.Y))
                split(j, si.From);
            if (OnSegment(p2.X, p2.Y, q1.X, q1.Y, q2.X, q2.Y))
                split(j, si.To);
            return;
        }
        var touching = false;
        if (d1 == 0 && OnSegment(q1.X, q1.Y, p1.X, p1.Y, p2.X, p2.Y)) { split(i, sj.From); touching = true; }
        if (d2 == 0 && OnSegment(q2.X, q2.Y, p1.X, p1.Y, p2.X, p2.Y)) { split(i, sj.To); touching = true; }
        if (d3 == 0 && OnSegment(p1.X, p1.Y, q1.X, q1.Y, q2.X, q2.Y)) { split(j, si.From); touching = true; }
        if (d4 == 0 && OnSegment(p2.X, p2.Y, q1.X, q1.Y, q2.X, q2.Y)) { split(j, si.To); touching = true; }
        if (touching || d1 == 0 || d2 == 0 || d3 == 0 || d4 == 0)
            return;
        // A proper crossing: the point where the cross product along p changes sign, on q.
        var s = (double)d1 / ((double)d1 - (double)d2);
        var x = (long)Math.Round(q1.X + (q2.X - q1.X) * s, MidpointRounding.AwayFromZero);
        var y = (long)Math.Round(q1.Y + (q2.Y - q1.Y) * s, MidpointRounding.AwayFromZero);
        var v = vertices.Add(x, y);
        split(i, v);
        split(j, v);
    }

    // ---- Edges and classification ----

    private sealed class Edge(int from, int to, int operands)
    {
        public int From = from;
        public int To = to;

        /// <summary>Per operand: edges From→To minus edges To→From.</summary>
        public readonly int[] Net = new int[operands];
    }

    private static List<Edge> BuildEdges(List<Seg> segments, int operands)
    {
        var map = new Dictionary<(int, int), Edge>();
        foreach (var s in segments)
        {
            if (s.From == s.To)
                continue;
            var low = Math.Min(s.From, s.To);
            var high = Math.Max(s.From, s.To);
            if (!map.TryGetValue((low, high), out var edge))
                map[(low, high)] = edge = new Edge(low, high, operands);
            edge.Net[s.Operand] += s.From < s.To ? 1 : -1;
        }
        return map.Values.Where(e => e.Net.Any(n => n != 0)).OrderBy(e => e.From).ThenBy(e => e.To).ToList();
    }

    private static bool Result(bool[] inside, BooleanOperation operation)
    {
        switch (operation)
        {
            case BooleanOperation.Union:
                return inside.Any(b => b);
            case BooleanOperation.Intersection:
                return inside.All(b => b);
            case BooleanOperation.Difference:
                for (var i = 1; i < inside.Length; i++)
                    if (inside[i])
                        return false;
                return inside[0];
            default:
                return inside.Count(b => b) % 2 == 1;
        }
    }

    private static List<List<int>> Trace(Vertices vertices, List<Edge> edges, FillRule[] rules, BooleanOperation operation)
    {
        var operands = rules.Length;
        var ax = new double[edges.Count];
        var ay = new double[edges.Count];
        var bx = new double[edges.Count];
        var by = new double[edges.Count];
        for (var i = 0; i < edges.Count; i++)
        {
            ax[i] = vertices[edges[i].From].X;
            ay[i] = vertices[edges[i].From].Y;
            bx[i] = vertices[edges[i].To].X;
            by[i] = vertices[edges[i].To].Y;
        }

        var winding = new int[operands];
        var inside = new bool[operands];
        bool Covered(double px, double py)
        {
            Array.Clear(winding);
            for (var i = 0; i < edges.Count; i++)
            {
                double side;
                if (ay[i] <= py && by[i] > py)
                {
                    side = (bx[i] - ax[i]) * (py - ay[i]) - (px - ax[i]) * (by[i] - ay[i]);
                    if (side > 0)
                        for (var k = 0; k < operands; k++)
                            winding[k] += edges[i].Net[k];
                }
                else if (by[i] <= py && ay[i] > py)
                {
                    side = (bx[i] - ax[i]) * (py - ay[i]) - (px - ax[i]) * (by[i] - ay[i]);
                    if (side < 0)
                        for (var k = 0; k < operands; k++)
                            winding[k] -= edges[i].Net[k];
                }
            }
            for (var k = 0; k < operands; k++)
                inside[k] = rules[k] == FillRule.NonZero ? winding[k] != 0 : (winding[k] & 1) != 0;
            return Result(inside, operation);
        }

        // Directed result edges, inside on the left.
        var outgoing = new Dictionary<int, List<int>>();
        var kept = new List<(int From, int To)>();
        for (var i = 0; i < edges.Count; i++)
        {
            var dx = bx[i] - ax[i];
            var dy = by[i] - ay[i];
            var length = Math.Sqrt(dx * dx + dy * dy);
            var mx = (ax[i] + bx[i]) / 2;
            var my = (ay[i] + by[i]) / 2;
            var nx = -dy / length * Offset;
            var ny = dx / length * Offset;
            var left = Covered(mx + nx, my + ny);
            var right = Covered(mx - nx, my - ny);
            if (left == right)
                continue;
            kept.Add(left ? (edges[i].From, edges[i].To) : (edges[i].To, edges[i].From));
        }
        for (var i = 0; i < kept.Count; i++)
        {
            if (!outgoing.TryGetValue(kept[i].From, out var list))
                outgoing[kept[i].From] = list = [];
            list.Add(i);
        }

        var used = new bool[kept.Count];
        var rings = new List<List<int>>();
        for (var start = 0; start < kept.Count; start++)
        {
            if (used[start])
                continue;
            var ring = new List<int>();
            var current = start;
            used[start] = true;
            var startVertex = kept[start].From;
            while (true)
            {
                ring.Add(kept[current].From);
                var at = kept[current].To;
                var back = (X: vertices[kept[current].From].X - vertices[at].X, Y: vertices[kept[current].From].Y - vertices[at].Y);
                var next = -1;
                if (outgoing.TryGetValue(at, out var candidates))
                {
                    foreach (var c in candidates)
                    {
                        if (used[c] && !(c == start && at == startVertex))
                            continue;
                        if (next < 0 || ClockwiseBefore(vertices, at, back, kept[c].To, kept[next].To))
                            next = c;
                    }
                }
                if (next < 0 || next == start)
                    break;
                used[next] = true;
                current = next;
            }
            if (ring.Count >= 3)
                rings.Add(Simplify(vertices, ring));
        }
        return rings.Where(r => r.Count >= 3).ToList();
    }

    /// <summary>True when the edge from <paramref name="at"/> to <paramref name="a"/> comes before the one to <paramref name="b"/>, turning clockwise from <paramref name="back"/>.</summary>
    private static bool ClockwiseBefore(Vertices vertices, int at, (long X, long Y) back, int a, int b)
    {
        var o = vertices[at];
        var va = (X: vertices[a].X - o.X, Y: vertices[a].Y - o.Y);
        var vb = (X: vertices[b].X - o.X, Y: vertices[b].Y - o.Y);
        var ha = Half(back, va);
        var hb = Half(back, vb);
        if (ha != hb)
            return ha < hb;
        return Cross(va.X, va.Y, vb.X, vb.Y) < 0;
    }

    // 0: strictly clockwise of back (within half a turn); 1: exactly opposite; 2: more than half a turn; 3: the same direction (a full turn).
    private static int Half((long X, long Y) back, (long X, long Y) v)
    {
        var cross = Cross(back.X, back.Y, v.X, v.Y);
        if (cross < 0)
            return 0;
        if (cross > 0)
            return 2;
        return (Int128)back.X * v.X + (Int128)back.Y * v.Y < 0 ? 1 : 3;
    }

    /// <summary>Removes the vertices that lie on the straight line between their neighbours.</summary>
    private static List<int> Simplify(Vertices vertices, List<int> ring)
    {
        var current = ring;
        bool changed;
        do
        {
            changed = false;
            var next = new List<int>(current.Count);
            for (var i = 0; i < current.Count; i++)
            {
                var a = vertices[current[(i + current.Count - 1) % current.Count]];
                var b = vertices[current[i]];
                var c = vertices[current[(i + 1) % current.Count]];
                if (current.Count > 3 && Cross(a.X, a.Y, b.X, b.Y, c.X, c.Y) == 0
                    && (Int128)(b.X - a.X) * (c.X - b.X) + (Int128)(b.Y - a.Y) * (c.Y - b.Y) > 0)
                {
                    changed = true;
                    continue;
                }
                next.Add(current[i]);
            }
            current = next;
        }
        while (changed && current.Count > 3);
        return current;
    }

    // ---- Back to curves ----

    private const double CornerTurn = 0.45;

    private static void AppendRing(VectorPath path, List<VPoint> points, double tolerance)
    {
        var n = points.Count;
        if (n < 3)
            return;
        // Corners: where the outline turns sharply. Lines: an edge much longer than its neighbours.
        var corner = new bool[n];
        var length = new double[n];
        for (var i = 0; i < n; i++)
        {
            var a = points[(i + n - 1) % n];
            var b = points[i];
            var c = points[(i + 1) % n];
            var u = b - a;
            var v = c - b;
            var turn = Math.Abs(Math.Atan2(u.Cross(v), u.Dot(v)));
            corner[i] = turn > CornerTurn;
            length[i] = b.DistanceTo(c);   // edge i goes from point i to point i + 1
        }
        var line = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var before = length[(i + n - 1) % n];
            var after = length[(i + 1) % n];
            line[i] = length[i] > 3 * Math.Min(before, after);
        }
        // Start where a run cannot wrap: a corner, or the end of a line edge.
        var first = -1;
        for (var i = 0; i < n && first < 0; i++)
            if (corner[i] || line[i] || line[(i + n - 1) % n])
                first = i;
        var wholeCurve = first < 0;
        if (wholeCurve)
            first = 0;

        path.MoveTo(points[first]);
        var index = 0;
        while (index < n)
        {
            var at = (first + index) % n;
            // A run of curve edges starting here that goes on while the joints are smooth.
            var run = new List<VPoint> { points[at] };
            var edge = index;
            while (edge < n)
            {
                var e = (first + edge) % n;
                if (line[e])
                    break;
                run.Add(points[(e + 1) % n]);
                edge++;
                var joint = (first + edge) % n;
                if (edge >= n || corner[joint] || line[joint])
                    break;
            }
            if (run.Count >= 4)
            {
                var fitted = CurveFitter.Fit(run, Math.Max(tolerance * 4, 0.2));
                foreach (var segment in fitted.Segments.Skip(1))
                    path.Add(segment);
                index = edge;
            }
            else
            {
                // The last edge ends where the outline started: Close draws it.
                if (index < n - 1)
                    path.LineTo(points[(at + 1) % n]);
                index++;
            }
        }
        path.Close();
    }
}
