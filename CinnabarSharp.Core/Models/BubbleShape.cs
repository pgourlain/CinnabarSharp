using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Core.Models;

/// <summary>
/// Outline of a speech bubble as a signed distance (negative inside): a body (square, rounded, oval or cloud) and a
/// tail pointing at <see cref="Tail"/> — a triangle from the body's center, or for a thought bubble a trail of
/// shrinking circles. Filling the shape, then filling it again inset by the outline width with another color, gives
/// one seamless outline around body and tail. Pure math, so bubbles are identical on every OS.
/// </summary>
public sealed class BubbleShape
{
    private readonly (PointD Center, double Radius)[] _discs;
    private readonly PointD[]? _triangle;
    private readonly double _cx, _cy, _rx, _ry;

    public BubbleShape(BubbleStyle style, RectangleD body, PointD tail, double cornerRadius)
    {
        Style = style;
        Body = body;
        Tail = tail;
        CornerRadius = cornerRadius;
        (_cx, _cy, _rx, _ry) = (body.X + body.Width / 2, body.Y + body.Height / 2, body.Width / 2, body.Height / 2);

        var discs = new List<(PointD, double)>();
        var tailOutside = BodyDistance(tail.X, tail.Y) > 0;
        if (style == BubbleStyle.Thought)
        {
            // Bumps: circles centered on a smaller ellipse, spaced about 1.5 radii apart.
            var bump = BumpRadius;
            var (ex, ey) = (Math.Max(_rx - bump * 0.6, 1), Math.Max(_ry - bump * 0.6, 1));
            var perimeter = Math.PI * (3 * (ex + ey) - Math.Sqrt((3 * ex + ey) * (ex + 3 * ey)));
            var count = Math.Max(6, (int)Math.Round(perimeter / (bump * 1.5)));
            for (var i = 0; i < count; i++)
            {
                var angle = 2 * Math.PI * i / count;
                discs.Add((new PointD(_cx + ex * Math.Cos(angle), _cy + ey * Math.Sin(angle)), bump));
            }
            if (tailOutside)
            {
                var edge = EdgeTowards(tail);
                // Shrinking circles, the smallest one on the tip.
                var size = Math.Min(body.Width, body.Height);
                foreach (var (t, r) in new[] { (0.25, 0.2), (0.6, 0.14), (1.0, 0.09) })
                    discs.Add((new PointD(edge.X + (tail.X - edge.X) * t, edge.Y + (tail.Y - edge.Y) * t), Math.Max(3, size * r)));
            }
        }
        else if (tailOutside)
        {
            var (dx, dy) = (tail.X - _cx, tail.Y - _cy);
            var length = Math.Sqrt(dx * dx + dy * dy);
            var half = Math.Clamp(Math.Min(body.Width, body.Height) * 0.3, 4, 60);
            var (px, py) = (-dy / length * half, dx / length * half);
            _triangle = [new PointD(_cx + px, _cy + py), new PointD(_cx - px, _cy - py), tail];
        }
        _discs = [.. discs];
    }

    public BubbleStyle Style { get; }
    public RectangleD Body { get; }
    public PointD Tail { get; }
    public double CornerRadius { get; }

    private double BumpRadius => Math.Clamp(Math.Min(Body.Width, Body.Height) * 0.16, 4, 40);

    /// <summary>Bounding box of everything (body, bumps, tail).</summary>
    public RectangleD Extent
    {
        get
        {
            var margin = Style == BubbleStyle.Thought ? BumpRadius : 0;
            double x0 = Body.X - margin, y0 = Body.Y - margin;
            double x1 = Body.X + Body.Width + margin, y1 = Body.Y + Body.Height + margin;
            foreach (var p in (_triangle ?? []).Append(Tail))
                (x0, y0, x1, y1) = (Math.Min(x0, p.X), Math.Min(y0, p.Y), Math.Max(x1, p.X), Math.Max(y1, p.Y));
            foreach (var (c, r) in _discs)
                (x0, y0, x1, y1) = (Math.Min(x0, c.X - r), Math.Min(y0, c.Y - r), Math.Max(x1, c.X + r), Math.Max(y1, c.Y + r));
            return new RectangleD(x0, y0, x1 - x0, y1 - y0);
        }
    }

    public double SignedDistance(double x, double y)
    {
        var d = BodyDistance(x, y);
        if (_triangle is { } t)
            d = Math.Min(d, PolygonDistance(t, x, y));
        foreach (var (c, r) in _discs)
            d = Math.Min(d, Math.Sqrt((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y)) - r);
        return d;
    }

    private double BodyDistance(double x, double y) => Style switch
    {
        BubbleStyle.Square => CoverageMask.RoundedBoxDistance(x, y, Body.X, Body.Y, Body.X + Body.Width, Body.Y + Body.Height, 0),
        BubbleStyle.Rounded => CoverageMask.RoundedBoxDistance(x, y, Body.X, Body.Y, Body.X + Body.Width, Body.Y + Body.Height, CornerRadius),
        BubbleStyle.Thought => CoverageMask.EllipseDistance(x - _cx, y - _cy, Math.Max(_rx - BumpRadius * 0.6, 1), Math.Max(_ry - BumpRadius * 0.6, 1)),
        _ => CoverageMask.EllipseDistance(x - _cx, y - _cy, Math.Max(_rx, 0.5), Math.Max(_ry, 0.5)),
    };

    /// <summary>Where the line from the body's center to <paramref name="p"/> leaves the (elliptic) body.</summary>
    private PointD EdgeTowards(PointD p)
    {
        var (dx, dy) = (p.X - _cx, p.Y - _cy);
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0)
            return p;
        var (ux, uy) = (dx / length, dy / length);
        var r = _rx * _ry / Math.Sqrt(_ry * ux * _ry * ux + _rx * uy * _rx * uy);
        return new PointD(_cx + ux * r, _cy + uy * r);
    }

    // Exact signed distance to a polygon (Inigo Quilez's formula; even-odd sign).
    private static double PolygonDistance(PointD[] v, double x, double y)
    {
        var d = (x - v[0].X) * (x - v[0].X) + (y - v[0].Y) * (y - v[0].Y);
        var sign = 1.0;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            var (ex, ey) = (v[j].X - v[i].X, v[j].Y - v[i].Y);
            var (wx, wy) = (x - v[i].X, y - v[i].Y);
            var t = Math.Clamp((wx * ex + wy * ey) / (ex * ex + ey * ey), 0, 1);
            var (bx, by) = (wx - ex * t, wy - ey * t);
            d = Math.Min(d, bx * bx + by * by);
            bool c1 = y >= v[i].Y, c2 = y < v[j].Y, c3 = ex * wy > ey * wx;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3))
                sign = -sign;
        }
        return sign * Math.Sqrt(d);
    }

    /// <summary>The rectangle text goes in, <paramref name="padding"/> inside the body.</summary>
    public static RectangleD TextArea(BubbleStyle style, RectangleD body, double padding)
    {
        // Oval and cloud: the rectangle inscribed in the ellipse.
        var (w, h) = style is BubbleStyle.Oval or BubbleStyle.Thought
            ? (body.Width / Math.Sqrt(2) - padding, body.Height / Math.Sqrt(2) - padding)
            : (body.Width - 2 * padding, body.Height - 2 * padding);
        (w, h) = (Math.Max(w, 1), Math.Max(h, 1));
        return new RectangleD(body.X + (body.Width - w) / 2, body.Y + (body.Height - h) / 2, w, h);
    }

    /// <summary>Body size whose <see cref="TextArea"/> is <paramref name="textWidth"/> × <paramref name="textHeight"/>.</summary>
    public static (double Width, double Height) BodySizeFor(BubbleStyle style, double textWidth, double textHeight, double padding) =>
        style is BubbleStyle.Oval or BubbleStyle.Thought
            ? ((textWidth + padding) * Math.Sqrt(2), (textHeight + padding) * Math.Sqrt(2))
            : (textWidth + 2 * padding, textHeight + 2 * padding);

    /// <summary>Inverse of <see cref="BodySizeFor"/> for one dimension: the text width a body width leaves.</summary>
    public static double TextWidthFor(BubbleStyle style, double bodyWidth, double padding) =>
        style is BubbleStyle.Oval or BubbleStyle.Thought ? bodyWidth / Math.Sqrt(2) - padding : bodyWidth - 2 * padding;

    /// <summary>Center of the number badge: the body's top-left corner, or the matching point on an ellipse.</summary>
    public static PointD BadgeCenter(BubbleStyle style, RectangleD body) =>
        style is BubbleStyle.Oval or BubbleStyle.Thought
            ? new PointD(body.X + body.Width / 2 * (1 - Math.Sqrt(0.5)), body.Y + body.Height / 2 * (1 - Math.Sqrt(0.5)))
            : new PointD(body.X, body.Y);
}
