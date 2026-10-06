using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Tests.Vector;

public class VectorInteropTests
{
    [Fact]
    public void Points_round_trip()
    {
        var p = new Core.Models.PointD(1.5, -2.25);
        Assert.Equal(new VPoint(1.5, -2.25), p.ToVector());
        Assert.Equal(p, p.ToVector().ToCore());
    }

    [Fact]
    public void Double_rectangles_round_trip()
    {
        var r = new RectangleD(1, 2, 30.5, 40.25);
        Assert.Equal(new VRect(1, 2, 30.5, 40.25), r.ToVector());
        Assert.Equal(r, r.ToVector().ToCore());
    }

    [Fact]
    public void Integer_rectangles_round_trip()
    {
        var r = new RectangleI(-3, 4, 50, 60);
        Assert.Equal(new VRectI(-3, 4, 50, 60), r.ToVector());
        Assert.Equal(r, r.ToVector().ToCore());
    }

    [Fact]
    public void Colors_keep_every_channel()
    {
        var c = ColorBgra.FromBgra(10, 20, 30, 40);
        var v = c.ToVector();
        Assert.Equal((10, 20, 30, 40), (v.B, v.G, v.R, v.A));
        Assert.Equal(c, v.ToCore());
        Assert.Equal(VColor.FromRgba(30, 20, 10, 40), v);
    }

    [Fact]
    public void Outer_pixels_cover_the_rectangle()
    {
        Assert.Equal(new RectangleI(1, 2, 4, 4), new VRect(1.2, 2.5, 3.1, 3.2).ToOuterPixels());
        Assert.Equal(RectangleI.Zero, VRect.Empty.ToOuterPixels());
    }
}
