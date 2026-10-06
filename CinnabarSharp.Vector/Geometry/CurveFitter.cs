namespace CinnabarSharp.Vector;

/// <summary>
/// Turns a freehand polyline into a smooth path: Douglas-Peucker to drop redundant points, then Schneider's algorithm
/// ("An Algorithm for Automatically Fitting Digitized Curves", Graphics Gems, 1990) to fit cubic Béziers within an error.
/// Deterministic, double arithmetic only.
/// </summary>
public static class CurveFitter
{
    /// <summary>Douglas-Peucker: the points that matter, keeping the first and last, within <paramref name="tolerance"/> of the original.</summary>
    public static List<VPoint> Simplify(IReadOnlyList<VPoint> points, double tolerance)
    {
        if (points.Count < 3)
            return [.. points];
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (start, end) = stack.Pop();
            var index = -1;
            var max = tolerance;
            for (var i = start + 1; i < end; i++)
            {
                var d = DistanceToSegment(points[i], points[start], points[end]);
                if (d > max)
                {
                    max = d;
                    index = i;
                }
            }
            if (index < 0)
                continue;
            keep[index] = true;
            stack.Push((start, index));
            stack.Push((index, end));
        }
        var result = new List<VPoint>();
        for (var i = 0; i < points.Count; i++)
            if (keep[i])
                result.Add(points[i]);
        return result;
    }

    private static double DistanceToSegment(VPoint p, VPoint a, VPoint b)
    {
        var ab = b - a;
        var lengthSquared = ab.Dot(ab);
        if (lengthSquared == 0)
            return p.DistanceTo(a);
        var t = Math.Clamp(ab.Dot(p - a) / lengthSquared, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>A path through the points: cubic segments within <paramref name="error"/> of them; two points make a line.</summary>
    public static VectorPath Fit(IReadOnlyList<VPoint> input, double error)
    {
        // Repeated points have no direction.
        var points = new List<VPoint>();
        foreach (var p in input)
            if (points.Count == 0 || p != points[^1])
                points.Add(p);
        var path = new VectorPath();
        if (points.Count == 0)
            return path;
        path.MoveTo(points[0]);
        if (points.Count == 1)
            return path;
        if (points.Count == 2)
            return path.LineTo(points[1]);
        var start = (points[1] - points[0]).Normalized();
        var end = (points[^2] - points[^1]).Normalized();
        // The fitting works with squared distances.
        FitCubic(points, 0, points.Count - 1, start, end, error * error, path);
        return path;
    }

    private static void FitCubic(List<VPoint> d, int first, int last, VVector tHat1, VVector tHat2, double errorSquared, VectorPath path)
    {
        var count = last - first + 1;
        if (count == 2)
        {
            var distance = d[first].DistanceTo(d[last]) / 3;
            path.CubicTo(d[first] + tHat1 * distance, d[last] + tHat2 * distance, d[last]);
            return;
        }

        var u = ChordLengthParameterize(d, first, last);
        var bezier = GenerateBezier(d, first, last, u, tHat1, tHat2);
        var (maxError, split) = ComputeMaxError(d, first, last, bezier, u);
        if (maxError < errorSquared)
        {
            path.CubicTo(bezier[1], bezier[2], bezier[3]);
            return;
        }
        // Close: improve the parameter values (Newton-Raphson) and try again.
        if (maxError < errorSquared * 4)
        {
            for (var i = 0; i < 4; i++)
            {
                var better = Reparameterize(d, first, last, u, bezier);
                bezier = GenerateBezier(d, first, last, better, tHat1, tHat2);
                (maxError, split) = ComputeMaxError(d, first, last, bezier, better);
                if (maxError < errorSquared)
                {
                    path.CubicTo(bezier[1], bezier[2], bezier[3]);
                    return;
                }
                u = better;
            }
        }
        // Split at the worst point with a common tangent and fit each half.
        split = Math.Clamp(split, first + 1, last - 1);
        var center = (d[split - 1] - d[split + 1]).Normalized();
        if (center == default)
            center = (d[split - 1] - d[split]).Normalized();
        FitCubic(d, first, split, tHat1, center, errorSquared, path);
        FitCubic(d, split, last, -center, tHat2, errorSquared, path);
    }

    private static VPoint[] GenerateBezier(List<VPoint> d, int first, int last, double[] u, VVector tHat1, VVector tHat2)
    {
        var count = last - first + 1;
        var a = new (VVector, VVector)[count];
        for (var i = 0; i < count; i++)
            a[i] = (tHat1 * B1(u[i]), tHat2 * B2(u[i]));
        double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
        var first0 = d[first];
        var last0 = d[last];
        for (var i = 0; i < count; i++)
        {
            c00 += a[i].Item1.Dot(a[i].Item1);
            c01 += a[i].Item1.Dot(a[i].Item2);
            c11 += a[i].Item2.Dot(a[i].Item2);
            var target = d[first + i] - Blend(first0, first0, last0, last0, u[i]);
            x0 += a[i].Item1.Dot(target);
            x1 += a[i].Item2.Dot(target);
        }
        var det = c00 * c11 - c01 * c01;
        var alphaL = det == 0 ? 0 : (x0 * c11 - x1 * c01) / det;
        var alphaR = det == 0 ? 0 : (c00 * x1 - c01 * x0) / det;
        var segmentLength = first0.DistanceTo(last0);
        var epsilon = 1e-6 * segmentLength;
        if (alphaL < epsilon || alphaR < epsilon)
        {
            // The fit puts a handle behind its end: use a third of the chord (Wu/Barsky heuristic).
            var distance = segmentLength / 3;
            return [first0, first0 + tHat1 * distance, last0 + tHat2 * distance, last0];
        }
        return [first0, first0 + tHat1 * alphaL, last0 + tHat2 * alphaR, last0];
    }

    private static VPoint Blend(VPoint p0, VPoint p1, VPoint p2, VPoint p3, double t)
    {
        // The curve's own value: p0*B0 + p1*B1 + p2*B2 + p3*B3 with the handles at the end points (so only the end points count).
        var mt = 1 - t;
        var (b0, b1, b2, b3) = (mt * mt * mt, 3 * mt * mt * t, 3 * mt * t * t, t * t * t);
        return new VPoint(p0.X * b0 + p1.X * b1 + p2.X * b2 + p3.X * b3, p0.Y * b0 + p1.Y * b1 + p2.Y * b2 + p3.Y * b3);
    }

    private static double B1(double t) { var mt = 1 - t; return 3 * t * mt * mt; }

    private static double B2(double t) { var mt = 1 - t; return 3 * t * t * mt; }

    private static double[] Reparameterize(List<VPoint> d, int first, int last, double[] u, VPoint[] bezier)
    {
        var result = new double[last - first + 1];
        for (var i = 0; i < result.Length; i++)
            result[i] = NewtonRaphson(bezier, d[first + i], u[i]);
        return result;
    }

    private static double NewtonRaphson(VPoint[] q, VPoint p, double u)
    {
        var q1 = new VPoint[3];
        var q2 = new VPoint[2];
        for (var i = 0; i < 3; i++)
            q1[i] = new VPoint((q[i + 1].X - q[i].X) * 3, (q[i + 1].Y - q[i].Y) * 3);
        for (var i = 0; i < 2; i++)
            q2[i] = new VPoint((q1[i + 1].X - q1[i].X) * 2, (q1[i + 1].Y - q1[i].Y) * 2);
        var qu = Evaluate(3, q, u);
        var q1u = Evaluate(2, q1, u);
        var q2u = Evaluate(1, q2, u);
        var numerator = (qu.X - p.X) * q1u.X + (qu.Y - p.Y) * q1u.Y;
        var denominator = q1u.X * q1u.X + q1u.Y * q1u.Y + (qu.X - p.X) * q2u.X + (qu.Y - p.Y) * q2u.Y;
        return denominator == 0 ? u : Math.Clamp(u - numerator / denominator, 0, 1);
    }

    private static VPoint Evaluate(int degree, VPoint[] v, double t)
    {
        var temp = (VPoint[])v.Clone();
        for (var i = 1; i <= degree; i++)
            for (var j = 0; j <= degree - i; j++)
                temp[j] = new VPoint((1 - t) * temp[j].X + t * temp[j + 1].X, (1 - t) * temp[j].Y + t * temp[j + 1].Y);
        return temp[0];
    }

    private static double[] ChordLengthParameterize(List<VPoint> d, int first, int last)
    {
        var u = new double[last - first + 1];
        for (var i = first + 1; i <= last; i++)
            u[i - first] = u[i - first - 1] + d[i].DistanceTo(d[i - 1]);
        var total = u[last - first];
        for (var i = 1; i < u.Length; i++)
            u[i] = total == 0 ? 0 : u[i] / total;
        return u;
    }

    private static (double Max, int Index) ComputeMaxError(List<VPoint> d, int first, int last, VPoint[] bezier, double[] u)
    {
        var split = (last - first + 1) / 2 + first;
        var max = 0.0;
        for (var i = first + 1; i < last; i++)
        {
            var p = Evaluate(3, bezier, u[i - first]);
            var distance = p.DistanceSquaredTo(d[i]);
            if (distance >= max)
            {
                max = distance;
                split = i;
            }
        }
        return (max, split);
    }
}
