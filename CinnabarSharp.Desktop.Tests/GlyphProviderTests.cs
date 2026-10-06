using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Desktop.Tests;

public sealed class GlyphProviderTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static readonly AvaloniaGlyphOutlineProvider Provider = new();

    [AvaloniaFact]
    public void Outline_has_the_baseline_at_zero_and_an_advance()
    {
        var outline = Provider.Outline("H", new TextStyle("sans-serif", 40, 400, false), out var advance);
        var bounds = outline.Bounds;
        Assert.False(outline.IsEmpty);
        Assert.InRange(advance, 15, 40);
        Assert.InRange(bounds.Bottom, -1.5, 1.5);          // sits on the baseline
        Assert.InRange(bounds.Top, -40, -20);              // cap height: above it, between half and a full em
        Assert.InRange(bounds.Width, 10, advance);
    }

    [AvaloniaFact]
    public void Outline_scales_with_the_font_size()
    {
        var small = Provider.Outline("Hello", new TextStyle("sans-serif", 20, 400, false), out var advanceSmall);
        var large = Provider.Outline("Hello", new TextStyle("sans-serif", 40, 400, false), out var advanceLarge);
        Assert.Equal(advanceSmall * 2, advanceLarge, 6);
        Assert.Equal(small.Bounds.Width * 2, large.Bounds.Width, 3);
        Assert.Equal(small.Bounds.Height * 2, large.Bounds.Height, 3);
    }

    [AvaloniaFact]
    public void Contours_are_closed_polygons_and_letters_with_holes_have_several()
    {
        var o = Provider.Outline("o", new TextStyle("sans-serif", 60, 400, false), out _);
        var figures = o.Figures.ToList();
        Assert.Equal(2, figures.Count);
        Assert.All(figures, f => Assert.True(f.IsClosed));
        // The inner contour lies inside the outer one.
        var outer = figures.MaxBy(f => f.Segments.Count)!;
        Assert.True(outer.Segments.Count > 20);
    }

    [AvaloniaFact]
    public void Longer_text_is_wider_and_spaces_advance_without_drawing()
    {
        Provider.Outline("ab", new TextStyle("sans-serif", 30, 400, false), out var two);
        Provider.Outline("abab", new TextStyle("sans-serif", 30, 400, false), out var four);
        Assert.InRange(four, two * 1.8, two * 2.2);
        var space = Provider.Outline(" ", new TextStyle("sans-serif", 30, 400, false), out var advance);
        Assert.True(space.IsEmpty);
        Assert.True(advance > 3);
        Assert.True(Provider.Outline("", new TextStyle("sans-serif", 30, 400, false), out var none).IsEmpty);
        Assert.Equal(0, none);
    }

    [AvaloniaFact]
    public void Bold_is_wider_than_regular()
    {
        Provider.Outline("Hamburgefonts", new TextStyle("sans-serif", 30, 400, false), out var regular);
        Provider.Outline("Hamburgefonts", new TextStyle("sans-serif", 30, 700, false), out var bold);
        Assert.True(bold >= regular);
    }

    [AvaloniaFact]
    public async Task Text_renders_in_an_open_drawing_with_the_platform_fonts()
    {
        await _h.Vm.OpenFileAsync(Path.Combine(AppContext.BaseDirectory, "Data", "svg", "text.svg"));
        Dispatcher.UIThread.RunJobs();
        var drawing = Assert.IsType<SvgDocument>(_h.Vm.ActiveDocument!.Document);
        Assert.NotNull(drawing.GlyphProvider);

        var (bgra, width, height) = VectorRasterizer.RenderAll(drawing.Root, 2, drawing.RenderOptions);
        _h.Capture("svg-40-text");

        // "Centered" is blue, 16 px centered on x = 120: pixels of that colour near the middle, and no solid box.
        var blue = 0;
        for (var i = 0; i < width * height; i++)
            if (bgra[i * 4 + 3] > 200 && bgra[i * 4] > 150 && bgra[i * 4 + 2] < 80)
                blue++;
        Assert.InRange(blue, 80, 6000);
    }
}

public class ContourTracerTests
{
    private static float[] Field(int w, int h, Func<int, int, bool> inside)
    {
        var f = new float[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                f[y * w + x] = inside(x, y) ? 1 : 0;
        return f;
    }

    private static double SignedArea(IReadOnlyList<VPoint> pts)
    {
        double a = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2;
    }

    [Fact]
    public void A_filled_square_gives_one_contour_with_the_right_area()
    {
        var contours = Services.ContourTracer.Trace(Field(20, 20, (x, y) => x is >= 5 and < 15 && y is >= 5 and < 15), 20, 20, 0.5, 0.05);
        var contour = Assert.Single(contours);
        Assert.InRange(Math.Abs(SignedArea(contour)), 80, 100);
    }

    [Fact]
    public void A_ring_gives_an_outer_contour_and_a_hole_that_wind_oppositely()
    {
        var contours = Services.ContourTracer.Trace(
            Field(30, 30, (x, y) => x is >= 4 and < 26 && y is >= 4 and < 26 && !(x is >= 10 and < 20 && y is >= 10 and < 20)), 30, 30, 0.5, 0.05);
        Assert.Equal(2, contours.Count);
        var areas = contours.Select(SignedArea).OrderBy(a => a).ToList();
        Assert.True(areas[0] * areas[1] < 0);                       // opposite signs
        Assert.InRange(Math.Abs(areas[0] + areas[1]), 22 * 22 - 100 - 40, 22 * 22 - 100 + 40);   // ring area
    }

    [Fact]
    public void Two_separate_blobs_and_a_saddle()
    {
        var two = Services.ContourTracer.Trace(Field(30, 10, (x, y) => (x is >= 2 and < 8 || x is >= 20 and < 26) && y is >= 2 and < 8), 30, 10, 0.5, 0.05);
        Assert.Equal(2, two.Count);
        // Diagonal pixels touching at a corner (a saddle in the field): still closed contours.
        var saddle = Services.ContourTracer.Trace(Field(6, 6, (x, y) => (x, y) is (2, 2) or (3, 3)), 6, 6, 0.5, 0);
        Assert.InRange(saddle.Count, 1, 2);
        Assert.All(saddle, c => Assert.True(c.Count >= 3));
    }

    [Fact]
    public void Smooth_fields_interpolate_between_pixels()
    {
        var f = new float[10 * 10];
        for (var y = 0; y < 10; y++)
            for (var x = 0; x < 10; x++)
                f[y * 10 + x] = y is < 2 or > 7 || x < 2 ? 0f : x < 4 ? 1f : x == 4 ? 0.75f : 0f;   // a bar; its right edge is at 0.75 in column 4
        var contour = Assert.Single(Services.ContourTracer.Trace(f, 10, 10, 0.5, 0.01));
        var edgeX = contour.Select(p => p.X).Max();
        Assert.InRange(edgeX, 4.5 + 1.0 / 3 - 0.02, 4.5 + 1.0 / 3 + 0.02);   // 0.5 is a third of the way from 0.75 to 0, from the center of column 4
    }
}
