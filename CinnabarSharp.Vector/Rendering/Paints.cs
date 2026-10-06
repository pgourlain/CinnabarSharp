namespace CinnabarSharp.Vector;

/// <summary>A gradient evaluated per pixel: stops interpolated in sRGB with straight alpha, spread by pad, reflect or repeat.</summary>
internal sealed class GradientPaint : PaintSource
{
    private readonly (double Offset, VColor Color)[] _stops;
    private readonly SpreadMethod _spread;
    private readonly Matrix2D _deviceToGradient;
    private readonly bool _radial;
    // Linear: start and the vector to the end. Radial: focal point, focal-to-center vector and the radius.
    private readonly double _ax, _ay, _bx, _by, _r;
    private readonly double _lengthSquared;

    private GradientPaint(IReadOnlyList<(double Offset, VColor Color)> stops, SpreadMethod spread, Matrix2D deviceToGradient,
        bool radial, double ax, double ay, double bx, double by, double r)
    {
        // Offsets never go backwards: a smaller one is raised to the previous.
        var fixedStops = new (double, VColor)[stops.Count];
        var last = 0.0;
        for (var i = 0; i < stops.Count; i++)
        {
            last = Math.Max(last, Math.Clamp(stops[i].Offset, 0, 1));
            fixedStops[i] = (last, stops[i].Color);
        }
        _stops = fixedStops;
        _spread = spread;
        _deviceToGradient = deviceToGradient;
        _radial = radial;
        (_ax, _ay, _bx, _by, _r) = (ax, ay, bx, by, r);
        _lengthSquared = bx * bx + by * by;
    }

    public static GradientPaint Linear(IReadOnlyList<(double, VColor)> stops, SpreadMethod spread, Matrix2D deviceToGradient,
        VPoint p1, VPoint p2) =>
        new(stops, spread, deviceToGradient, false, p1.X, p1.Y, p2.X - p1.X, p2.Y - p1.Y, 0);

    public static GradientPaint Radial(IReadOnlyList<(double, VColor)> stops, SpreadMethod spread, Matrix2D deviceToGradient,
        VPoint center, double radius, VPoint focus)
    {
        var vx = center.X - focus.X;
        var vy = center.Y - focus.Y;
        // A focus outside the circle is moved onto its edge (just inside, so the formula stays defined).
        var distance = Math.Sqrt(vx * vx + vy * vy);
        if (distance >= radius * 0.9999 && distance > 0)
        {
            var k = radius * 0.9999 / distance;
            focus = new VPoint(center.X - vx * k, center.Y - vy * k);
            vx = center.X - focus.X;
            vy = center.Y - focus.Y;
        }
        return new GradientPaint(stops, spread, deviceToGradient, true, focus.X, focus.Y, vx, vy, radius);
    }

    public override VColor ColorAt(int x, int y)
    {
        var p = _deviceToGradient.Transform(new VPoint(x + 0.5, y + 0.5));
        double t;
        if (!_radial)
        {
            t = _lengthSquared == 0 ? 1 : ((p.X - _ax) * _bx + (p.Y - _ay) * _by) / _lengthSquared;
        }
        else
        {
            var ux = p.X - _ax;
            var uy = p.Y - _ay;
            var uv = ux * _bx + uy * _by;
            var a = _bx * _bx + _by * _by - _r * _r;
            var discriminant = uv * uv - a * (ux * ux + uy * uy);
            t = a == 0 ? 0 : (uv - Math.Sqrt(Math.Max(0, discriminant))) / a;
        }
        return Color(Spread(t));
    }

    private double Spread(double t)
    {
        if (!double.IsFinite(t))
            return 1;
        switch (_spread)
        {
            case SpreadMethod.Repeat:
                return t - Math.Floor(t);
            case SpreadMethod.Reflect:
                var m = t - 2 * Math.Floor(t / 2);
                return m > 1 ? 2 - m : m;
            default:
                return Math.Clamp(t, 0, 1);
        }
    }

    private VColor Color(double t)
    {
        var stops = _stops;
        if (t <= stops[0].Offset)
            return stops[0].Color;
        for (var i = 1; i < stops.Length; i++)
        {
            if (t > stops[i].Offset)
                continue;
            var (o0, c0) = stops[i - 1];
            var (o1, c1) = stops[i];
            if (o1 <= o0)
                return c1;
            var f = (t - o0) / (o1 - o0);
            return new VColor(Lerp(c0.B, c1.B, f), Lerp(c0.G, c1.G, f), Lerp(c0.R, c1.R, f), Lerp(c0.A, c1.A, f));
        }
        return stops[^1].Color;
    }

    private static byte Lerp(byte a, byte b, double f) => (byte)(a + (b - a) * f + 0.5);
}
