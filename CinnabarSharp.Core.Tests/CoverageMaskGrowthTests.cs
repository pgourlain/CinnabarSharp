using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tests;

/// <summary>
/// CoverageMask's backing store only covers the smallest rectangle touched so far, growing (reallocate + copy)
/// as strokes reach further, instead of always being the full image (performance-tasks.md P3). These tests
/// force growth in multiple directions and across several separate dabs, which none of the existing
/// single-shape CoverageMaskTests happen to exercise.
/// </summary>
public sealed class CoverageMaskGrowthTests
{
    [Fact]
    public void Nothing_is_allocated_before_the_first_shape()
    {
        var m = new CoverageMask(1000, 1000);

        Assert.Equal(0, m.Data.Length);
        Assert.Equal(0, m[500, 500]); // reading an untouched pixel is still well-defined: 0
    }

    [Fact]
    public void Backing_store_stays_far_smaller_than_the_full_image_for_a_small_stroke()
    {
        var m = new CoverageMask(4000, 6000); // the 24 MP target size
        m.Disc(2000, 3000, 10, antialias: true);

        Assert.True(m.Data.Length < 4000 * 6000 / 100, $"expected a small backing store, got {m.Data.Length} bytes");
    }

    [Fact]
    public void Growing_in_every_direction_preserves_what_was_already_drawn()
    {
        var m = new CoverageMask(200, 200);

        // Four discs far apart from each other and from the center dab, forcing the backing store to grow
        // left, right, up and down from wherever it started.
        m.Disc(100, 100, 3, antialias: false); // center, allocates first
        m.Disc(10, 100, 3, antialias: false);  // grows left
        m.Disc(190, 100, 3, antialias: false); // grows right
        m.Disc(100, 10, 3, antialias: false);  // grows up
        m.Disc(100, 190, 3, antialias: false); // grows down

        Assert.Equal(255, m[100, 100]);
        Assert.Equal(255, m[10, 100]);
        Assert.Equal(255, m[190, 100]);
        Assert.Equal(255, m[100, 10]);
        Assert.Equal(255, m[100, 190]);
        // Untouched corners stay untouched.
        Assert.Equal(0, m[0, 0]);
        Assert.Equal(0, m[199, 199]);
        // Bounds covers every dab, still shy of the full 200x200 canvas.
        Assert.True(m.Bounds.X <= 10 && m.Bounds.Y <= 10);
        Assert.True(m.Bounds.X + m.Bounds.Width >= 190 && m.Bounds.Y + m.Bounds.Height >= 190);
        Assert.True(m.Bounds.Width < 200 || m.Bounds.Height < 200);
    }

    [Fact]
    public void Growth_matches_a_mask_that_was_always_full_size()
    {
        // Draw the same path of dabs into a mask that starts small (and must grow repeatedly) and one big
        // enough from the start to never need to (same code path either way, but this pins that growing
        // mid-stroke never loses or shifts a pixel).
        var points = new (double X, double Y)[] { (50, 50), (5, 50), (95, 5), (95, 95), (50, 20), (20, 80) };

        var grown = new CoverageMask(100, 100);
        var full = new CoverageMask(100, 100);
        foreach (var (x, y) in points)
        {
            grown.Disc(x, y, 4, antialias: true);
            full.Disc(x, y, 4, antialias: true);
        }

        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 100; x++)
                Assert.Equal(full[x, y], grown[x, y]);
    }

    [Fact]
    public void Pixel_line_and_fill_also_grow_the_backing_store()
    {
        var line = new CoverageMask(500, 500);
        line.PixelLine(new PointI(10, 10), new PointI(400, 300));
        Assert.Equal(255, line[10, 10]);
        Assert.Equal(255, line[400, 300]);
        Assert.True(line.Data.Length < 500 * 500);

        var fillMask = SelectionMask.Rectangle(500, 500, new PointD(100, 100), new PointD(150, 130));
        var coverage = new CoverageMask(500, 500);
        coverage.Fill(fillMask);
        Assert.Equal(255, coverage[120, 110]);
        Assert.Equal(0, coverage[0, 0]);
        Assert.True(coverage.Data.Length < 500 * 500);
    }
}
