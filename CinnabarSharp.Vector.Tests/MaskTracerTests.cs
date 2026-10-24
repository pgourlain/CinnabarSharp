namespace CinnabarSharp.Vector.Tests;

public class MaskTracerTests
{
    private const int W = 80, H = 60;

    private static byte[] Mask(params (int X, int Y, int Width, int Height)[] rects)
    {
        var mask = new byte[W * H];
        foreach (var (x, y, w, h) in rects)
            for (var j = y; j < y + h; j++)
                for (var i = x; i < x + w; i++)
                    mask[j * W + i] = 255;
        return mask;
    }

    private static List<VPoint> Corners(VectorPath path) =>
        path.Segments.Where(s => s.Kind is SegmentKind.MoveTo or SegmentKind.LineTo).Select(s => s.End).ToList();

    [Fact]
    public void Rectangle_is_four_exact_corners()
    {
        var paths = MaskTracer.Trace(Mask((5, 5, 40, 10)), W, H);

        var corners = Corners(Assert.Single(paths));

        Assert.Equal(4, corners.Count);
        Assert.Equal(
            [new VPoint(5, 5), new VPoint(45, 5), new VPoint(5, 15), new VPoint(45, 15)],
            corners.OrderBy(p => p.Y).ThenBy(p => p.X).ToList());
    }

    [Fact]
    public void L_shape_has_six_corners()
    {
        var paths = MaskTracer.Trace(Mask((5, 5, 10, 40), (5, 35, 40, 10)), W, H);

        var corners = Corners(Assert.Single(paths));
        Assert.Equal(6, corners.Count);
        Assert.All(corners, c => Assert.True(c.X is 5 or 15 or 45 && c.Y is 5 or 35 or 45));
    }

    [Fact]
    public void Room_is_one_path_with_the_outline_and_a_hole()
    {
        // Four walls of 6 pixels around an empty room.
        var mask = Mask((5, 5, 50, 6), (5, 39, 50, 6), (5, 5, 6, 40), (49, 5, 6, 40));

        var path = Assert.Single(MaskTracer.Trace(mask, W, H));

        var figures = path.Figures.ToList();
        Assert.Equal(2, figures.Count);
        Assert.All(figures, f => Assert.Equal(4, f.Segments.Count(s => s.Kind == SegmentKind.LineTo) + 1));
    }

    [Fact]
    public void Separate_walls_are_separate_paths()
    {
        var paths = MaskTracer.Trace(Mask((5, 5, 20, 6), (5, 30, 20, 6)), W, H);

        Assert.Equal(2, paths.Count);
    }

    [Fact]
    public void Ragged_edges_become_straight_orthogonal_sides()
    {
        var mask = Mask((10, 10, 50, 12));
        // One-pixel bumps on the top side and notches in the bottom side, as a scan would give.
        for (var x = 14; x < 56; x += 5)
        {
            mask[9 * W + x] = 255;
            mask[21 * W + x] = 0;
        }

        var corners = Corners(Assert.Single(MaskTracer.Trace(mask, W, H)));

        Assert.Equal(4, corners.Count);
        Assert.Equal(2, corners.Select(c => Math.Round(c.X, 3)).Distinct().Count());
        Assert.Equal(2, corners.Select(c => Math.Round(c.Y, 3)).Distinct().Count());
        Assert.InRange(corners.Min(c => c.X), 9.5, 10.5);
        Assert.InRange(corners.Max(c => c.X), 59.5, 60.5);
    }

    [Fact]
    public void Specks_below_the_minimum_area_are_dropped()
    {
        var paths = MaskTracer.Trace(Mask((5, 5, 20, 6), (50, 40, 1, 1)), W, H, new TraceOptions(MinArea: 4));

        Assert.Single(paths);
    }

    [Fact]
    public void Scale_multiplies_the_coordinates()
    {
        var path = Assert.Single(MaskTracer.Trace(Mask((5, 5, 40, 10)), W, H, new TraceOptions(Scale: 2)));

        Assert.Contains(new VPoint(90, 30), Corners(path));
    }

    [Fact]
    public void Tracing_is_deterministic()
    {
        var mask = Mask((5, 5, 50, 6), (5, 39, 50, 6), (5, 5, 6, 40), (49, 5, 6, 40), (20, 20, 3, 3));

        var first = MaskTracer.Trace(mask, W, H).Select(p => PathDataWriter.Write(p)).ToList();
        var second = MaskTracer.Trace(mask, W, H).Select(p => PathDataWriter.Write(p)).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Rendering_the_outline_gives_back_the_mask()
    {
        // A room with a door gap, plus a wall stub: the filled paths must cover the same pixels (± the edge pixels).
        var mask = Mask((5, 5, 50, 6), (5, 39, 20, 6), (35, 39, 20, 6), (5, 5, 6, 40), (49, 5, 6, 40), (25, 20, 6, 15));
        var paths = MaskTracer.Trace(mask, W, H);
        var data = string.Join(" ", paths.Select(p => PathDataWriter.Write(p)));
        var svg = SvgParser.Parse($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\">" +
                                  $"<path d=\"{data}\" fill=\"#000\" fill-rule=\"evenodd\"/></svg>").Root;

        var (bgra, width, height) = VectorRasterizer.RenderAll(svg, 1);

        Assert.Equal((W, H), (width, height));
        var different = 0;
        for (var i = 0; i < W * H; i++)
            if ((bgra[i * 4 + 3] >= 128) != (mask[i] != 0))
                different++;
        Assert.True(different <= 2, $"{different} pixels differ");
    }
}
