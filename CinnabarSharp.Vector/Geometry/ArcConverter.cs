namespace CinnabarSharp.Vector;

/// <summary>Elliptical arc to cubic Bézier conversion (SVG 1.1 implementation notes, F.6).</summary>
public static class ArcConverter
{
    public readonly record struct Cubic(VPoint Start, VPoint C1, VPoint C2, VPoint End);

    /// <summary>
    /// Cubics that approximate <paramref name="arc"/> starting at <paramref name="from"/> (each covers at most 90°).
    /// Radii too small for the end points are scaled up; a zero radius or equal end points give no cubic.
    /// </summary>
    public static IReadOnlyList<Cubic> ToCubics(VPoint from, PathSegment arc) =>
        ToCubics(from, arc.End, arc.Rx, arc.Ry, arc.XAxisRotation, arc.LargeArc, arc.Sweep);

    public static IReadOnlyList<Cubic> ToCubics(VPoint from, VPoint to, double rx, double ry, double xAxisRotationDegrees,
        bool largeArc, bool sweep)
    {
        var result = new List<Cubic>();
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx == 0 || ry == 0 || from == to)
            return result;

        var phi = xAxisRotationDegrees * Math.PI / 180;
        var (sinPhi, cosPhi) = (Math.Sin(phi), Math.Cos(phi));

        // F.6.5.1
        var dx2 = (from.X - to.X) / 2;
        var dy2 = (from.Y - to.Y) / 2;
        var x1p = cosPhi * dx2 + sinPhi * dy2;
        var y1p = -sinPhi * dx2 + cosPhi * dy2;

        // F.6.6: radii too small are scaled up.
        var lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (lambda > 1)
        {
            var s = Math.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }

        // F.6.5.2
        var rx2 = rx * rx;
        var ry2 = ry * ry;
        var numerator = rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p;
        var denominator = rx2 * y1p * y1p + ry2 * x1p * x1p;
        var coefficient = denominator == 0 ? 0 : Math.Sqrt(Math.Max(0, numerator / denominator));
        if (largeArc == sweep)
            coefficient = -coefficient;
        var cxp = coefficient * rx * y1p / ry;
        var cyp = -coefficient * ry * x1p / rx;

        // F.6.5.3
        var cx = cosPhi * cxp - sinPhi * cyp + (from.X + to.X) / 2;
        var cy = sinPhi * cxp + cosPhi * cyp + (from.Y + to.Y) / 2;

        // F.6.5.5 and F.6.5.6
        var theta1 = Math.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var theta2 = Math.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        var delta = theta2 - theta1;
        if (sweep && delta < 0)
            delta += 2 * Math.PI;
        else if (!sweep && delta > 0)
            delta -= 2 * Math.PI;

        var count = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        var step = delta / count;
        var k = 4.0 / 3.0 * Math.Tan(step / 4);

        VPoint Map(double ux, double uy) =>
            new(cx + rx * ux * cosPhi - ry * uy * sinPhi, cy + rx * ux * sinPhi + ry * uy * cosPhi);

        var start = from;
        var angle = theta1;
        for (var i = 0; i < count; i++)
        {
            var next = angle + step;
            var (sin1, cos1) = (Math.Sin(angle), Math.Cos(angle));
            var (sin2, cos2) = (Math.Sin(next), Math.Cos(next));
            var c1 = Map(cos1 - k * sin1, sin1 + k * cos1);
            var c2 = Map(cos2 + k * sin2, sin2 - k * cos2);
            // The last cubic ends exactly on the requested point.
            var end = i == count - 1 ? to : Map(cos2, sin2);
            result.Add(new Cubic(start, c1, c2, end));
            start = end;
            angle = next;
        }
        return result;
    }
}
