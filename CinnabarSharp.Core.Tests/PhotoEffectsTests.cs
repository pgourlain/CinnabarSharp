using System.Security.Cryptography;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tests;

public sealed class PhotoEffectsTests
{
    private const int W = 40, H = 30;

    /// <summary>A photo-like gradient: dark bottom-left to bright top-right, with a color cast.</summary>
    private static byte[] Photo(double gain = 1, int blueCast = 0)
    {
        var px = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                var t = (x + (H - y)) / (double)(W + H);
                px[i] = (byte)Math.Clamp((40 + 150 * t) * gain + blueCast, 0, 255);
                px[i + 1] = (byte)Math.Clamp((50 + 160 * t) * gain, 0, 255);
                px[i + 2] = (byte)Math.Clamp((60 + 170 * t) * gain + (x % 3 == 0 ? 6 : 0), 0, 255);
                px[i + 3] = 255;
            }
        return px;
    }

    private static byte[] Render(Effect effect, byte[] source, IReadOnlyList<double> values)
    {
        var ctx = new EffectContext(source, W, H, ColorBgra.Black, ColorBgra.White);
        var dst = new byte[W * H * 4];
        effect.Render(ctx, new RectangleI(0, 0, W, H), dst, values, CancellationToken.None);
        return dst;
    }

    private static double[] Adjust(int slider, double value)
    {
        var v = new double[15];
        v[slider] = value;
        return v;
    }

    private static double MeanLuma(byte[] px)
    {
        double sum = 0;
        for (var i = 0; i < px.Length; i += 4)
            sum += PhotoMath.Luma(px[i + 2], px[i + 1], px[i]);
        return sum / (px.Length / 4);
    }

    private static double MeanChroma(byte[] px)
    {
        double sum = 0;
        for (var i = 0; i < px.Length; i += 4)
            sum += Math.Max(px[i], Math.Max(px[i + 1], px[i + 2])) - Math.Min(px[i], Math.Min(px[i + 1], px[i + 2]));
        return sum / (px.Length / 4);
    }

    [Fact]
    public void Neutral_adjust_and_zero_straighten_leave_the_photo_unchanged()
    {
        var photo = Photo();
        Assert.Equal(photo, Render(new PhotoAdjustEffect(), photo, new double[15]));
        Assert.Equal(photo, Render(new StraightenEffect(), photo, [0]));
        Assert.Equal(photo, Render(new PhotoFilterEffect(), photo, [0, 100]));
    }

    [Theory]
    [InlineData(PhotoAdjustEffect.Exposure)]
    [InlineData(PhotoAdjustEffect.Brightness)]
    [InlineData(PhotoAdjustEffect.Shadows)]
    [InlineData(PhotoAdjustEffect.Highlights)]
    [InlineData(PhotoAdjustEffect.Brilliance)]
    public void Brightening_sliders_raise_the_mean_and_lowering_them_reduces_it(int slider)
    {
        var photo = Photo();
        var mean = MeanLuma(photo);

        Assert.True(MeanLuma(Render(new PhotoAdjustEffect(), photo, Adjust(slider, 60))) > mean + 1);
        Assert.True(MeanLuma(Render(new PhotoAdjustEffect(), photo, Adjust(slider, -60))) < mean - 1);
    }

    [Fact]
    public void Saturation_and_vibrance_change_colorfulness_and_mono_removes_it()
    {
        var photo = Photo();
        var chroma = MeanChroma(photo);

        Assert.True(MeanChroma(Render(new PhotoAdjustEffect(), photo, Adjust(PhotoAdjustEffect.Saturation, 50))) > chroma);
        Assert.True(MeanChroma(Render(new PhotoAdjustEffect(), photo, Adjust(PhotoAdjustEffect.Vibrance, 50))) > chroma);
        Assert.True(MeanChroma(Render(new PhotoAdjustEffect(), photo, Adjust(PhotoAdjustEffect.Saturation, -100))) < 1);
        var mono = PhotoFilterEffect.Presets.ToList().FindIndex(p => p.Name == "Mono");
        Assert.Equal(0, MeanChroma(Render(new PhotoFilterEffect(), photo, [mono, 100])));
    }

    [Fact]
    public void Warmth_moves_red_up_and_blue_down()
    {
        var photo = Photo();
        var warm = Render(new PhotoAdjustEffect(), photo, Adjust(PhotoAdjustEffect.Warmth, 50));

        Assert.True(warm[2] > photo[2]);
        Assert.True(warm[0] < photo[0]);
    }

    [Fact]
    public void Vignette_darkens_corners_not_the_center()
    {
        var photo = Photo();
        var result = Render(new PhotoAdjustEffect(), photo, Adjust(PhotoAdjustEffect.Vignette, 80));
        int At(byte[] px, int x, int y) => px[(y * W + x) * 4 + 1];

        Assert.True(At(result, 0, 0) < At(photo, 0, 0) - 10);
        Assert.InRange(At(result, W / 2, H / 2), At(photo, W / 2, H / 2) - 1, At(photo, W / 2, H / 2));
    }

    [Fact]
    public void Filter_intensity_blends_with_the_original()
    {
        var photo = Photo();
        var noir = PhotoFilterEffect.Presets.ToList().FindIndex(p => p.Name == "Noir");
        var full = Render(new PhotoFilterEffect(), photo, [noir, 100]);
        var half = Render(new PhotoFilterEffect(), photo, [noir, 50]);
        var none = Render(new PhotoFilterEffect(), photo, [noir, 0]);

        Assert.Equal(photo, none);
        Assert.InRange(half[400], Math.Min(photo[400], full[400]) - 1, Math.Max(photo[400], full[400]) + 1);
    }

    [Fact]
    public void Auto_enhance_brightens_a_dark_photo_and_corrects_a_blue_cast()
    {
        var dark = Photo(gain: 0.45);
        var ctx = new EffectContext(dark, W, H, ColorBgra.Black, ColorBgra.White);
        var values = AutoEnhanceEffect.Analyze(ctx);
        Assert.True(values[PhotoAdjustEffect.Exposure] > 0);
        Assert.True(MeanLuma(Render(new AutoEnhanceEffect(), dark, [])) > MeanLuma(dark) + 10);

        var blue = Photo(blueCast: 50);
        var cast = AutoEnhanceEffect.Analyze(new EffectContext(blue, W, H, ColorBgra.Black, ColorBgra.White));
        Assert.True(cast[PhotoAdjustEffect.Warmth] > 0);
        Assert.Equal(cast, new PhotoAdjustEffect().SuggestValues(new EffectContext(blue, W, H, ColorBgra.Black, ColorBgra.White)));
    }

    [Fact]
    public void Straighten_zooms_so_no_transparent_corners_appear()
    {
        var photo = Photo();
        var straight = Render(new StraightenEffect(), photo, [8]);

        for (var i = 3; i < straight.Length; i += 4)
            Assert.Equal(255, straight[i]);
        Assert.True(StraightenEffect.CoverScale(8, W, H) > 1.1);
        Assert.Equal(1, StraightenEffect.CoverScale(0, W, H));
    }

    [Fact]
    public void Tiles_render_the_same_pixels_as_the_whole_image()
    {
        var photo = Photo();
        var values = new double[15];
        values[PhotoAdjustEffect.Definition] = 60;
        values[PhotoAdjustEffect.Sharpness] = 40;
        values[PhotoAdjustEffect.NoiseReduction] = 30;
        var ctx = new EffectContext(photo, W, H, ColorBgra.Black, ColorBgra.White);
        var whole = new byte[W * H * 4];
        new PhotoAdjustEffect().Render(ctx, new RectangleI(0, 0, W, H), whole, values, CancellationToken.None);
        var tile = new RectangleI(10, 8, 15, 12);
        var part = new byte[tile.Width * tile.Height * 4];
        new PhotoAdjustEffect().Render(ctx, tile, part, values, CancellationToken.None);

        Assert.Equal(PixelRegion.Extract(whole, W, tile), part);
    }

    /// <summary>Photo tools must give the same pixels on every OS.</summary>
    [Fact]
    public void Photo_tools_are_bit_identical_across_platforms()
    {
        var photo = Photo(gain: 0.8, blueCast: 20);
        var output = new List<byte>();
        output.AddRange(Render(new PhotoAdjustEffect(), photo,
            [20, 30, -40, 35, 15, 10, 12, 25, 30, -20, 10, 50, 40, 30, 45]));
        output.AddRange(Render(new AutoEnhanceEffect(), photo, []));
        for (var preset = 0; preset < PhotoFilterEffect.Presets.Count; preset++)
            output.AddRange(Render(new PhotoFilterEffect(), photo, [preset, 80]));
        output.AddRange(Render(new StraightenEffect(), photo, [-7.3]));

        var hash = Convert.ToHexString(SHA256.HashData(output.ToArray()));
        Assert.Equal(ExpectedHash, hash);
    }

    private const string ExpectedHash = "AEF22A5216D9793ACFF0F202811BAEFE1B3286AECA70DA90A0C7CB4FC76C3971";
}
