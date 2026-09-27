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
    public void Effects_are_bit_identical_across_platforms()
    {
        var output = new List<byte>();
        foreach (var effect in EffectCatalog.All)
            output.AddRange(Render(effect, Pattern()));

        var hash = Convert.ToHexString(SHA256.HashData(output.ToArray()));
        Assert.Equal(ExpectedHash, hash);
    }

    private const string ExpectedHash = "8DB10E8C6D414133E4742A2B8AC84A5C0E40231C4285715E3D9FC74176068DE3";
}
