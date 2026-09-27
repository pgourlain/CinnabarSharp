using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Adjustments;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public sealed class AdjustmentsTests : BaseTests
{
    private readonly IWorkspaceService _workspace;

    public AdjustmentsTests() => _workspace = CinnabarSharpService().GetRequiredService<IWorkspaceService>();

    private static byte[] Run(ColorAdjustment adjustment, byte[] bgra, params double[] values)
    {
        var px = (byte[])bgra.Clone();
        var f = adjustment.Create(values.Length > 0 ? values : adjustment.Defaults, px);
        for (var i = 0; i < px.Length; i += 4)
            f(px.AsSpan(i, 4));
        return px;
    }

    // B, G, R, A
    private static readonly byte[] Orange = [0, 128, 255, 200];

    [Fact]
    public void Invert_keeps_alpha()
    {
        Assert.Equal(new byte[] { 255, 127, 0, 200 }, Run(new InvertColors(), Orange));
    }

    [Fact]
    public void Black_and_white_uses_paint_dot_net_intensity()
    {
        var i = (byte)((7471 * 0 + 38470 * 128 + 19595 * 255) >> 16);
        Assert.Equal(new[] { i, i, i, (byte)200 }, Run(new BlackAndWhite(), Orange));
    }

    [Fact]
    public void Sepia_is_warm()
    {
        var px = Run(new Sepia(), [128, 128, 128, 255]);
        Assert.True(px[2] > px[1] && px[1] > px[0], string.Join(",", px));
    }

    [Fact]
    public void Posterize_with_two_levels_gives_black_or_white()
    {
        Assert.Equal(new byte[] { 0, 255, 255, 200 }, Run(new Posterize(), Orange, 2));
    }

    [Fact]
    public void Levels_maps_input_range_to_output_range()
    {
        var px = Run(new Levels(), [50, 100, 150, 255], 50, 150, 1, 0, 255);
        Assert.Equal(new byte[] { 0, 128, 255, 255 }, px);
    }

    [Fact]
    public void Brightness_and_contrast()
    {
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Run(new BrightnessContrast(), [200, 220, 240, 255], 100, 0));
        Assert.Equal(new byte[] { 128, 128, 128, 255 }, Run(new BrightnessContrast(), [128, 128, 128, 255], 0, 80));
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, Run(new BrightnessContrast(), [10, 20, 30, 255]));
    }

    [Fact]
    public void Hue_rotation_and_desaturation()
    {
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, Run(new HueSaturation(), [0, 0, 255, 255], 120, 100, 0));
        var gray = Run(new HueSaturation(), [0, 0, 255, 255], 0, 0, 0);
        Assert.True(gray[0] == gray[1] && gray[1] == gray[2]);
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, Run(new HueSaturation(), [10, 20, 30, 255]));
    }

    [Fact]
    public void Auto_level_stretches_a_low_contrast_image()
    {
        var px = new byte[100 * 4];
        for (var i = 0; i < 100; i++)
            px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = (byte)(100 + i / 2);
        for (var i = 0; i < 100; i++)
            px[i * 4 + 3] = 255;

        var result = Run(new AutoLevel(), px);

        Assert.True(result[0] < 10, result[0].ToString());
        Assert.True(result[^2] > 245, result[^2].ToString());
    }

    [Fact]
    public void Session_previews_many_times_then_commits_one_step_inside_the_selection()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 1), ColorBgra.White);
        doc.SetSelection(SelectionMask.Rectangle(4, 1, new PointD(0, 0), new PointD(2, 1)));
        var session = new EffectSession(doc, new BrightnessContrast());

        session.Preview([-50, 0]);
        session.Preview([-100, 0]);
        session.Commit();

        Assert.Equal(new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255, 255 },
            doc.Layers[0].Surface.ToBgra());
        Assert.Equal(["New Image", "Brightness / Contrast"], doc.Workspace.History.Items.Select(i => i.Text));
        doc.Workspace.History.Undo();
        Assert.All(doc.Layers[0].Surface.ToBgra(), b => Assert.Equal(255, b));
    }

    [Fact]
    public void Cancel_restores_and_records_nothing()
    {
        var doc = _workspace.NewDocument(new ImageSize(3, 3), ColorBgra.White);
        var session = new EffectSession(doc, new InvertColors());

        session.Preview([]);
        session.Cancel();

        Assert.All(doc.Layers[0].Surface.ToBgra(), b => Assert.Equal(255, b));
        Assert.Single(doc.Workspace.History.Items);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Compute_does_not_touch_the_layer_and_can_be_cancelled()
    {
        var doc = _workspace.NewDocument(new ImageSize(50, 50), ColorBgra.White);
        var session = new EffectSession(doc, new InvertColors());

        var pixels = session.Compute([]);
        Assert.Equal(0, pixels[0]);
        Assert.Equal(255, doc.Layers[0].Surface.ToBgra()[0]);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.Compute([], cts.Token));
    }

    [Fact]
    public void Adjustments_are_bit_identical_across_platforms()
    {
        var px = new byte[64 * 64 * 4];
        for (var i = 0; i < 64 * 64; i++)
            (px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]) = ((byte)(i * 7), (byte)(i * 3), (byte)(i / 16), (byte)(255 - i % 97));

        var output = new List<byte>();
        output.AddRange(Run(new InvertColors(), px));
        output.AddRange(Run(new BlackAndWhite(), px));
        output.AddRange(Run(new Sepia(), px));
        output.AddRange(Run(new BrightnessContrast(), px, 30, -40));
        output.AddRange(Run(new Posterize(), px, 5));
        output.AddRange(Run(new Levels(), px, 20, 230, 1.7, 10, 240));
        output.AddRange(Run(new AutoLevel(), px));
        output.AddRange(Run(new HueSaturation(), px, 73, 140, -20));

        var hash = Convert.ToHexString(SHA256.HashData(output.ToArray()));
        Assert.Equal(ExpectedHash, hash);
    }

    private const string ExpectedHash = "57D4DFA18297A1D5CF32E98B8100E7C9D854AD66D12793BF4F8B01849A23D312";
}
