using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// Glyph outlines from the platform's fonts, for the SVG renderer. Avalonia gives no access to a font's outline
/// segments, so the text is drawn once, large (192 px per em), with the platform's own text stack and its contours are
/// traced from that picture (marching squares on the coverage, then simplified to 1/2000 em). The outline is scaled to the
/// size asked for and kept per text and font; it is a polygon with sub-pixel facets, not the font's curves.
/// </summary>
public sealed class AvaloniaGlyphOutlineProvider : IGlyphOutlineProvider
{
    private const double BuildSize = 192;
    private const int MaxChunk = 24;
    private const int MaxCacheEntries = 2000;

    private readonly ConcurrentDictionary<(string Text, string Family, int Weight, bool Italic), (VectorPath Outline, double Advance)> _cache = new();

    public VectorPath Outline(string text, TextStyle style, out double advance)
    {
        advance = 0;
        if (text.Length == 0 || style.Size <= 0)
            return new VectorPath();
        var key = (text, style.Family, style.Weight, style.Italic);
        if (!_cache.TryGetValue(key, out var built))
        {
            built = BuildChunks(text, style);
            if (_cache.Count > MaxCacheEntries)
                _cache.Clear();
            _cache[key] = built;
        }
        var scale = style.Size / BuildSize;
        advance = built.Advance * scale;
        return built.Outline.Transformed(Matrix2D.Scale(scale));
    }

    // Long text is traced in pieces (at word boundaries) so no picture gets enormous.
    private static (VectorPath Outline, double Advance) BuildChunks(string text, TextStyle style)
    {
        var outline = new VectorPath();
        var x = 0.0;
        foreach (var chunk in Split(text))
        {
            var (path, advance) = BuildChunk(chunk, style);
            outline.Append(path.Transformed(Matrix2D.Translate(x, 0)));
            x += advance;
        }
        return (outline, x);
    }

    private static IEnumerable<string> Split(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(text.Length, start + MaxChunk);
            if (end < text.Length)
            {
                var space = text.LastIndexOf(' ', end - 1, end - start);
                if (space > start)
                    end = space + 1;
            }
            yield return text[start..end];
            start = end;
        }
    }

    private static (VectorPath Outline, double Advance) BuildChunk(string text, TextStyle style)
    {
        var typeface = new Typeface(ParseFamily(style.Family), style.Italic ? FontStyle.Italic : FontStyle.Normal,
            (FontWeight)Math.Clamp(style.Weight, 1, 1000));
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, BuildSize, Brushes.White);
        var advance = formatted.WidthIncludingTrailingWhitespace;
        var margin = (int)Math.Ceiling(BuildSize / 2) + 2;
        var width = (int)Math.Ceiling(advance) + 2 * margin;
        var height = (int)Math.Ceiling(formatted.Height) + 2 * margin;
        if (width <= 0 || height <= 0 || width * (long)height > 40_000_000)
            return (new VectorPath(), advance);

        var coverage = RenderCoverage(formatted, width, height, margin);
        var contours = ContourTracer.Trace(coverage, width, height, 0.5, 0.06 * 1.0);
        var outline = new VectorPath();
        var baseline = margin + formatted.Baseline;
        foreach (var contour in contours)
        {
            if (contour.Count < 3)
                continue;
            outline.MoveTo(contour[0].X - margin, contour[0].Y - baseline);
            for (var i = 1; i < contour.Count; i++)
                outline.LineTo(contour[i].X - margin, contour[i].Y - baseline);
            outline.Close();
        }
        return (outline, advance);
    }

    private static float[] RenderCoverage(FormattedText formatted, int width, int height, int margin)
    {
        using var target = new RenderTargetBitmap(new PixelSize(width, height), new Avalonia.Vector(96, 96));
        using (var context = target.CreateDrawingContext())
            context.DrawText(formatted, new Point(margin, margin));
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        var coverage = new float[width * height];
        for (var i = 0; i < coverage.Length; i++)
            coverage[i] = pixels[i * 4 + 3] / 255f;
        return coverage;
    }

    private static FontFamily ParseFamily(string family)
    {
        // "Arial, Helvetica, sans-serif": the first family; generic names map to the platform defaults.
        var first = family.Split(',')[0].Trim().Trim('\'', '"');
        return first.ToLowerInvariant() switch
        {
            "" or "sans-serif" or "system-ui" or "ui-sans-serif" => FontFamily.Default,
            "monospace" or "ui-monospace" => new FontFamily("monospace"),
            "serif" or "ui-serif" => new FontFamily("serif"),
            _ => new FontFamily(first),
        };
    }
}

/// <summary>Closed contours of the region where a scalar field is at least <c>iso</c>: marching squares, then Douglas-Peucker.</summary>
public static class ContourTracer
{
    private readonly record struct EdgeId(int X, int Y, bool Horizontal);

    /// <summary>
    /// Contours of the field (row-major <paramref name="width"/> × <paramref name="height"/>, values at pixel centers).
    /// Orientation is consistent: the inside is always on the same side, so outer contours and holes wind oppositely
    /// and the nonzero rule fills the shape correctly. Points are in pixel units (a pixel center is at +0.5).
    /// </summary>
    public static List<List<VPoint>> Trace(float[] field, int width, int height, double iso, double tolerance)
    {
        double V(int x, int y) => field[y * width + x];

        VPoint Cross(EdgeId e)
        {
            var (ax, ay) = (e.X, e.Y);
            var (bx, by) = e.Horizontal ? (e.X + 1, e.Y) : (e.X, e.Y + 1);
            var va = V(ax, ay);
            var vb = V(bx, by);
            var t = vb == va ? 0.5 : (iso - va) / (vb - va);
            return new VPoint(ax + 0.5 + (bx - ax) * t, ay + 0.5 + (by - ay) * t);
        }

        var next = new Dictionary<EdgeId, EdgeId>();
        for (var y = 0; y < height - 1; y++)
        {
            for (var x = 0; x < width - 1; x++)
            {
                double tl = V(x, y), tr = V(x + 1, y), br = V(x + 1, y + 1), bl = V(x, y + 1);
                var index = (tl >= iso ? 8 : 0) | (tr >= iso ? 4 : 0) | (br >= iso ? 2 : 0) | (bl >= iso ? 1 : 0);
                if (index is 0 or 15)
                    continue;
                // Edge ids: 0 top, 1 right, 2 bottom, 3 left.
                EdgeId Edge(int edge) => edge switch
                {
                    0 => new EdgeId(x, y, true),
                    1 => new EdgeId(x + 1, y, false),
                    2 => new EdgeId(x, y + 1, true),
                    _ => new EdgeId(x, y, false),
                };
                var center = (tl + tr + br + bl) / 4 >= iso;
                (int, int)[] segments = index switch
                {
                    1 or 14 => [(3, 2)],
                    2 or 13 => [(2, 1)],
                    3 or 12 => [(3, 1)],
                    4 or 11 => [(0, 1)],
                    6 or 9 => [(0, 2)],
                    7 or 8 => [(3, 0)],
                    5 => center ? [(3, 0), (2, 1)] : [(0, 1), (3, 2)],
                    _ => center ? [(0, 1), (3, 2)] : [(3, 0), (2, 1)],   // 10
                };
                foreach (var (from, to) in segments)
                {
                    var (a, b) = (Edge(from), Edge(to));
                    // Orient so the inside is on the left of the direction of travel.
                    var pa = Cross(a);
                    var pb = Cross(b);
                    var mid = new VPoint((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2);
                    var d = pb - pa;
                    var normal = new VVector(-d.Y, d.X).Normalized();
                    var probe = Bilinear(tl, tr, br, bl, mid.X - x - 0.5, mid.Y - y - 0.5, normal, 0.05);
                    if (probe < 0)
                        (a, b) = (b, a);
                    next[a] = b;
                }
            }
        }

        var result = new List<List<VPoint>>();
        var visited = new HashSet<EdgeId>();
        foreach (var start in next.Keys.ToList())
        {
            if (visited.Contains(start))
                continue;
            var loop = new List<VPoint>();
            var edge = start;
            while (visited.Add(edge))
            {
                loop.Add(Cross(edge));
                if (!next.TryGetValue(edge, out edge))
                    break;
            }
            if (loop.Count >= 3)
                result.Add(Simplify(loop, tolerance));
        }
        return result;
    }

    // Positive when moving along the normal goes toward higher values (the left side is inside).
    private static double Bilinear(double tl, double tr, double br, double bl, double u, double v, VVector normal, double step)
    {
        double F(double fu, double fv)
        {
            fu = Math.Clamp(fu, 0, 1);
            fv = Math.Clamp(fv, 0, 1);
            return tl * (1 - fu) * (1 - fv) + tr * fu * (1 - fv) + bl * (1 - fu) * fv + br * fu * fv;
        }
        return F(u + normal.X * step, v + normal.Y * step) - F(u - normal.X * step, v - normal.Y * step);
    }

    /// <summary>Douglas-Peucker for a closed loop: split at the two points farthest apart, simplify each half.</summary>
    private static List<VPoint> Simplify(List<VPoint> loop, double tolerance)
    {
        if (loop.Count < 8 || tolerance <= 0)
            return loop;
        var first = 0;
        var far = 0;
        var best = -1.0;
        for (var i = 1; i < loop.Count; i++)
        {
            var d = loop[0].DistanceSquaredTo(loop[i]);
            if (d > best)
            {
                best = d;
                far = i;
            }
        }
        var a = Rdp(loop, first, far, tolerance);
        var halfTwo = loop.GetRange(far, loop.Count - far);
        halfTwo.Add(loop[0]);
        var b = Rdp(halfTwo, 0, halfTwo.Count - 1, tolerance);
        var result = new List<VPoint>(a);
        result.AddRange(b.Skip(1).Take(b.Count - 2));
        return result;
    }

    private static List<VPoint> Rdp(List<VPoint> points, int start, int end, double tolerance)
    {
        var keep = new bool[points.Count];
        keep[start] = keep[end] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((start, end));
        while (stack.Count > 0)
        {
            var (s, e) = stack.Pop();
            var index = -1;
            var max = tolerance;
            for (var i = s + 1; i < e; i++)
            {
                var d = DistanceToSegment(points[i], points[s], points[e]);
                if (d > max)
                {
                    max = d;
                    index = i;
                }
            }
            if (index < 0)
                continue;
            keep[index] = true;
            stack.Push((s, index));
            stack.Push((index, e));
        }
        var result = new List<VPoint>();
        for (var i = start; i <= end; i++)
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
}
