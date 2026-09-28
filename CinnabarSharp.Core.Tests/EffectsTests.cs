using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public sealed class EffectsTests : BaseTests
{
    private const int W = 24, H = 16;

    private static byte[] Uniform(byte b, byte g, byte r, byte a = 255)
    {
        var px = new byte[W * H * 4];
        for (var i = 0; i < px.Length; i += 4)
            (px[i], px[i + 1], px[i + 2], px[i + 3]) = (b, g, r, a);
        return px;
    }

    private static byte[] Pattern()
    {
        var px = new byte[W * H * 4];
        for (var i = 0; i < W * H; i++)
            (px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]) = ((byte)(i * 13), (byte)(i * 7), (byte)(i * 3), (byte)(255 - i % 50));
        return px;
    }

    private static byte[] Render(Effect effect, byte[] source, params double[] values)
    {
        var ctx = new EffectContext(source, W, H, ColorBgra.FromBgra(0, 0, 255, 255), ColorBgra.FromBgra(255, 0, 0, 255));
        var dst = new byte[W * H * 4];
        effect.Render(ctx, new RectangleI(0, 0, W, H), dst, values.Length > 0 ? values : effect.Defaults, CancellationToken.None);
        return dst;
    }

    public static TheoryData<string> EffectNames => new(EffectCatalog.All.Select(e => e.Name));

    private static Effect Find(string name) => EffectCatalog.All.Single(e => e.Name == name);

    [Theory]
    [MemberData(nameof(EffectNames))]
    public void Every_effect_is_deterministic_and_cancellable(string name)
    {
        var effect = Find(name);

        var a = Render(effect, Pattern());
        var b = Render(effect, Pattern());
        Assert.Equal(a, b);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ctx = new EffectContext(Pattern(), W, H, ColorBgra.Black, ColorBgra.White);
        Assert.Throws<OperationCanceledException>(() =>
            effect.Render(ctx, new RectangleI(0, 0, W, H), new byte[W * H * 4], effect.Defaults, cts.Token));
    }

    [Theory]
    [InlineData("Gaussian Blur")]
    [InlineData("Motion Blur")]
    [InlineData("Radial Blur")]
    [InlineData("Zoom Blur")]
    [InlineData("Median")]
    [InlineData("Pixelate")]
    public void Blurs_keep_a_flat_color_flat(string name)
    {
        var red = Uniform(0, 0, 255);
        Assert.Equal(red, Render(Find(name), red));
    }

    [Fact]
    public void Blurring_next_to_transparency_does_not_darken_colors()
    {
        var px = Uniform(0, 0, 0, 0);
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W / 2; x++)
                (px[(y * W + x) * 4 + 2], px[(y * W + x) * 4 + 3]) = (255, 255);

        var result = Render(new GaussianBlurEffect(), px, 4);

        var edge = (8 * W + W / 2) * 4;
        Assert.InRange(result[edge + 3], 40, 215);
        Assert.Equal(255, result[edge + 2]);
        Assert.Equal(0, result[edge + 1]);
    }

    [Theory]
    [InlineData("Gaussian Blur", 0.0)]
    [InlineData("Bulge", 0.0)]
    [InlineData("Twist", 0.0)]
    [InlineData("Add Noise", 0.0, 100.0, 0.0)]
    public void Neutral_settings_change_nothing(string name, params double[] values)
    {
        var src = Pattern();
        var result = Render(Find(name), src, values);
        for (var i = 3; i < src.Length; i += 4)
            if (src[i] == 255)
                Assert.Equal(src.AsSpan(i - 3, 4).ToArray(), result.AsSpan(i - 3, 4).ToArray());
    }

    [Fact]
    public void Median_removes_isolated_specks()
    {
        var px = Uniform(100, 100, 100);
        (px[(5 * W + 5) * 4], px[(5 * W + 5) * 4 + 1], px[(5 * W + 5) * 4 + 2]) = (255, 255, 255);

        var result = Render(new MedianEffect(), px, 1, 50);

        Assert.Equal(100, result[(5 * W + 5) * 4]);
    }

    [Fact]
    public void Pixelate_averages_each_cell()
    {
        var px = Uniform(0, 0, 0);
        for (var i = 0; i < W * H; i++)
            if ((i % W + i / W) % 2 == 0)
                (px[i * 4], px[i * 4 + 1], px[i * 4 + 2]) = (200, 200, 200);

        var result = Render(new PixelateEffect(), px, 2);

        Assert.All(Enumerable.Range(0, W * H), i => Assert.Equal(100, result[i * 4]));
    }

    [Fact]
    public void Emboss_of_a_flat_image_is_mid_gray()
    {
        var result = Render(new EmbossEffect(), Uniform(10, 200, 90));
        Assert.Equal(new byte[] { 128, 128, 128, 255 }, result[..4]);
    }

    [Fact]
    public void Clouds_blend_between_primary_and_secondary()
    {
        var result = Render(new CloudsEffect(), Uniform(0, 0, 0), 8, 50, 3);
        for (var i = 0; i < result.Length; i += 4)
        {
            Assert.Equal(0, result[i + 1]);
            Assert.InRange(result[i] + result[i + 2], 254, 256);
        }
    }

    [Fact]
    public void Effect_session_respects_selection_and_records_one_step()
    {
        var doc = CinnabarSharpService().GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(20, 10), ColorBgra.White);
        doc.SetSelection(SelectionMask.Rectangle(20, 10, new PointD(0, 0), new PointD(10, 10)));

        EffectSession.ApplyNow(doc, new CloudsEffect(), [4, 50, 1], ColorBgra.Black, ColorBgra.Black);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, doc.Layers[0].Surface.ReadRegion(new RectangleI(3, 3, 1, 1)));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, doc.Layers[0].Surface.ReadRegion(new RectangleI(15, 3, 1, 1)));
        Assert.Equal("Clouds", doc.Workspace.History.Items[^1].Text);
    }

    [Fact]
    public void Cartoon_reduces_a_gradient_to_a_few_flat_tones()
    {
        var px = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var v = (byte)(x * 255 / (W - 1));
                var i = (y * W + x) * 4;
                (px[i], px[i + 1], px[i + 2], px[i + 3]) = (v, v, v, 255);
            }

        // No smoothing, 4 tones, no saturation change, no outlines.
        var result = Render(new CartoonEffect(), px, 0, 4, 100, 20, 1, 0);

        var tones = Enumerable.Range(0, W * H).Select(i => result[i * 4]).Distinct().Count();
        Assert.InRange(tones, 2, 4);
    }

    [Fact]
    public void Cartoon_draws_a_dark_outline_on_a_sharp_edge_and_keeps_alpha()
    {
        var px = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                (px[i], px[i + 1], px[i + 2], px[i + 3]) = x < W / 2 ? ((byte)40, (byte)40, (byte)40, (byte)200) : ((byte)230, (byte)230, (byte)230, (byte)200);
            }

        var result = Render(new CartoonEffect(), px);

        byte[] At(int x, int y) => result.AsSpan((y * W + x) * 4, 4).ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, 200 }, At(W / 2, H / 2));     // on the edge: black outline
        Assert.True(At(W - 2, H / 2)[0] > 150);                          // far from it: the light flat tone
        Assert.Equal(200, At(2, 2)[3]);                                    // alpha kept
    }

    [Fact]
    public void Cartoon_on_part_of_the_image_matches_the_same_part_of_the_whole()
    {
        // Colored blocks: flat areas and edges, so both the tones and the outlines are exercised.
        var source = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var i = (y * W + x) * 4;
                (source[i], source[i + 1], source[i + 2], source[i + 3]) =
                    ((byte)(x / 6 * 60), (byte)(y / 5 * 70 + x), (byte)(200 - x * 3), (byte)255);
            }
        var whole = Render(new CartoonEffect(), source);
        Assert.Contains(whole.Where((_, i) => i % 4 == 0), b => b > 40); // not all outlines

        var region = new RectangleI(5, 3, 10, 8);
        var part = new byte[region.Width * region.Height * 4];
        var ctx = new EffectContext(source, W, H, ColorBgra.Black, ColorBgra.White);
        new CartoonEffect().Render(ctx, region, part, new CartoonEffect().Defaults, CancellationToken.None);

        Assert.Equal(PixelRegion.Extract(whole, W, region), part);
    }

    [Fact]
    public void Effects_are_bit_identical_across_platforms()
    {
        var output = new List<byte>();
        foreach (var effect in EffectCatalog.Effects)
            output.AddRange(Render(effect, Pattern()));
        // The pattern is so busy that Cartoon's defaults outline everything: pin its flat tones too.
        output.AddRange(Render(new CartoonEffect(), Pattern(), 3, 5, 150, 100, 1, 0));

        var hash = Convert.ToHexString(SHA256.HashData(output.ToArray()));
        Assert.Equal(ExpectedHash, hash);
    }

    private const string ExpectedHash = "A09A9F1B6B7DEA819ABBE27D412915B784EBF2CD9E25ABB0484EAC12ACAA9AC0";
}
