using System.Security.Cryptography;
using CinnabarSharp.Core.Adjustments;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tests;

public sealed class CurvesAndLevelsTests
{
    private static byte[] Run(ColorAdjustment adjustment, byte[] bgra, IReadOnlyList<double> values)
    {
        var px = (byte[])bgra.Clone();
        var f = adjustment.Create(values, px);
        for (var i = 0; i < px.Length; i += 4)
            f(px.AsSpan(i, 4));
        return px;
    }

    private static PointI P(int x, int y) => new(x, y);

    [Fact]
    public void Diagonal_spline_is_the_identity()
    {
        Assert.Equal(Enumerable.Range(0, 256).Select(i => (byte)i), Curves.Spline(CurvesSettings.Diagonal));
    }

    [Fact]
    public void Spline_goes_through_the_points_without_overshoot()
    {
        var lut = Curves.Spline([P(0, 0), P(64, 180), P(128, 200), P(255, 255)]);

        Assert.Equal(180, lut[64]);
        Assert.Equal(200, lut[128]);
        for (var i = 1; i < 256; i++)
            Assert.True(lut[i] >= lut[i - 1], $"not monotone at {i}");
    }

    [Fact]
    public void Spline_is_flat_outside_its_points_and_can_invert()
    {
        var clipped = Curves.Spline([P(50, 20), P(200, 240)]);
        Assert.Equal(20, clipped[0]);
        Assert.Equal(20, clipped[50]);
        Assert.Equal(240, clipped[255]);

        var inverted = Curves.Spline([P(0, 255), P(255, 0)]);
        Assert.Equal(255, inverted[0]);
        Assert.Equal(128, inverted[127]);
        Assert.Equal(0, inverted[255]);
    }

    [Fact]
    public void Settings_round_trip_through_values()
    {
        var settings = new CurvesSettings(CurvesMode.Rgb, CurvesSettings.Diagonal,
            [P(0, 10), P(100, 150), P(255, 250)], [P(0, 0), P(255, 128)], CurvesSettings.Diagonal);

        var back = CurvesSettings.FromValues(settings.ToValues());

        Assert.Equal(CurvesMode.Rgb, back.Mode);
        Assert.Equal(settings.Red, back.Red);
        Assert.Equal(settings.Green, back.Green);
        Assert.Equal(settings.Blue, back.Blue);
        Assert.Equal(new Curves().Defaults, CurvesSettings.Identity.ToValues());
    }

    [Fact]
    public void Rgb_curves_change_each_channel_separately()
    {
        var settings = CurvesSettings.Identity with { Mode = CurvesMode.Rgb, Green = [P(0, 0), P(255, 128)] };

        var px = Run(new Curves(), [100, 200, 50, 128], settings.ToValues());

        Assert.Equal(new byte[] { 100, 100, 50, 128 }, px);
    }

    [Fact]
    public void Luminosity_curve_shifts_all_channels_and_keeps_alpha()
    {
        var settings = CurvesSettings.Identity with { Luminosity = [P(0, 20), P(255, 255)] };

        var px = Run(new Curves(), [0, 0, 0, 77, 128, 128, 128, 255], settings.ToValues());

        Assert.Equal(new byte[] { 20, 20, 20, 77 }, px[..4]);
        Assert.Equal(px[4], px[5]);
        Assert.Equal(px[5], px[6]);
        Assert.True(px[4] > 128);
    }

    [Fact]
    public void Levels_per_channel_only_changes_that_channel()
    {
        var values = Levels.PerChannel(LevelsChannel.Identity with { InWhite = 128 }, LevelsChannel.Identity, LevelsChannel.Identity);

        var px = Run(new Levels(), [60, 70, 64, 255], values);

        Assert.Equal(new byte[] { 60, 70, 128, 255 }, px);
        Assert.Equal(new byte[] { 120, 139, 128, 255 }, Run(new Levels(), [60, 70, 64, 255], [0, 128, 1, 0, 255]));
    }

    [Fact]
    public void Histogram_skips_transparent_and_unselected_pixels()
    {
        byte[] bgra = [10, 20, 30, 255, 10, 20, 30, 0, 40, 50, 60, 255];
        var all = Histogram.Compute(bgra);
        Assert.Equal(1, all.Red[30]);
        Assert.Equal(1, all.Red[60]);
        Assert.Equal(2, all.Red.Sum());
        Assert.Equal(2, all.Luminosity.Sum());

        var selection = SelectionMask.Rectangle(3, 1, new PointD(2, 0), new PointD(3, 1));
        var selected = Histogram.Compute(bgra, selection);
        Assert.Equal(1, selected.Blue.Sum());
        Assert.Equal(1, selected.Blue[40]);

        var doubled = all.Map(Levels.Curve(0, 128, 1, 0, 255), LevelsChannel.Identity.Lookup(), LevelsChannel.Identity.Lookup());
        Assert.Equal(1, doubled.Red[60]);
        Assert.Equal(1, doubled.Red[120]);
    }

    [Fact]
    public void Auto_levels_clip_the_extremes_of_the_histogram()
    {
        var histogram = new long[256];
        histogram[40] = 500;
        histogram[200] = 500;

        var auto = LevelsChannel.Auto(histogram);

        Assert.Equal(40, auto.InBlack);
        Assert.Equal(200, auto.InWhite);
        Assert.Equal(LevelsChannel.Identity, LevelsChannel.Auto(new long[256]));
    }

    /// <summary>Curves and per-channel levels must give the same pixels on every OS.</summary>
    [Fact]
    public void Curves_and_levels_are_bit_identical_across_platforms()
    {
        var px = new byte[64 * 4];
        for (var i = 0; i < 64; i++)
            (px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]) = ((byte)(i * 4), (byte)(255 - i * 3), (byte)(i * 7 % 256), (byte)(128 + i));

        var output = new List<byte>();
        output.AddRange(Run(new Curves(), px, (CurvesSettings.Identity with { Luminosity = [P(0, 30), P(90, 60), P(180, 230), P(255, 240)] }).ToValues()));
        output.AddRange(Run(new Curves(), px, (CurvesSettings.Identity with
        {
            Mode = CurvesMode.Rgb,
            Red = [P(0, 255), P(128, 60), P(255, 0)],
            Blue = [P(20, 0), P(60, 200), P(255, 255)],
        }).ToValues()));
        output.AddRange(Run(new Levels(), px, Levels.PerChannel(
            new LevelsChannel(10, 200, 1.4, 0, 255), new LevelsChannel(0, 255, 0.6, 30, 220), LevelsChannel.Identity)));

        var hash = Convert.ToHexString(SHA256.HashData(output.ToArray()));
        Assert.Equal(ExpectedHash, hash);
    }

    private const string ExpectedHash = "684D5C7C08AD700F756424ED83936E195846BBF2603B4EDE2C45ECE6F86D67D2";
}
