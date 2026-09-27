using System.Security.Cryptography;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tests;

public class BlendOpsTests
{
    [Theory]
    [InlineData(BlendMode.Normal, 100, 200, 200)]
    [InlineData(BlendMode.Multiply, 128, 128, 64)]
    [InlineData(BlendMode.Multiply, 255, 77, 77)]
    [InlineData(BlendMode.Additive, 200, 100, 255)]
    [InlineData(BlendMode.Additive, 20, 30, 50)]
    [InlineData(BlendMode.ColorBurn, 200, 0, 0)]
    [InlineData(BlendMode.ColorBurn, 255, 100, 255)]
    [InlineData(BlendMode.ColorDodge, 100, 255, 255)]
    [InlineData(BlendMode.ColorDodge, 0, 200, 0)]
    [InlineData(BlendMode.Reflect, 100, 155, 100)]
    [InlineData(BlendMode.Glow, 155, 100, 100)]
    [InlineData(BlendMode.Overlay, 64, 128, 64)]
    [InlineData(BlendMode.Overlay, 192, 128, 192)]
    [InlineData(BlendMode.Difference, 50, 200, 150)]
    [InlineData(BlendMode.Negation, 200, 200, 110)]
    [InlineData(BlendMode.Lighten, 50, 200, 200)]
    [InlineData(BlendMode.Darken, 50, 200, 50)]
    [InlineData(BlendMode.Screen, 128, 128, 192)]
    [InlineData(BlendMode.Xor, 0b1100, 0b1010, 0b0110)]
    [InlineData(BlendMode.HardLight, 128, 64, 64)]
    [InlineData(BlendMode.SoftLight, 128, 128, 128)]
    [InlineData(BlendMode.SoftLight, 0, 255, 0)]
    public void Blend_function(BlendMode mode, byte bottom, byte top, byte expected)
    {
        Assert.Equal(expected, BlendOps.Blend(mode, bottom, top));
    }

    [Fact]
    public void Every_blend_mode_is_implemented()
    {
        foreach (var mode in Enum.GetValues<BlendMode>())
            BlendOps.Blend(mode, 100, 200);
    }

    private static byte[] Px(byte b, byte g, byte r, byte a) => [b, g, r, a];

    [Fact]
    public void Opaque_normal_replaces_pixel()
    {
        var bottom = Px(10, 20, 30, 255);
        BlendOps.Composite(bottom, Px(1, 2, 3, 255), BlendMode.Normal, 1);
        Assert.Equal(Px(1, 2, 3, 255), bottom);
    }

    [Fact]
    public void Half_opacity_mixes_evenly()
    {
        var bottom = Px(0, 0, 0, 255);
        BlendOps.Composite(bottom, Px(200, 100, 50, 255), BlendMode.Normal, 0.5);
        Assert.Equal(Px(100, 50, 25, 255), bottom);
    }

    [Fact]
    public void Top_over_transparent_keeps_top_color_and_alpha()
    {
        var bottom = Px(0, 0, 0, 0);
        BlendOps.Composite(bottom, Px(200, 100, 50, 128), BlendMode.Multiply, 1);
        Assert.Equal(Px(200, 100, 50, 128), bottom);
    }

    [Fact]
    public void Transparent_top_changes_nothing()
    {
        var bottom = Px(10, 20, 30, 40);
        BlendOps.Composite(bottom, Px(200, 100, 50, 0), BlendMode.Difference, 1);
        Assert.Equal(Px(10, 20, 30, 40), bottom);
    }

    [Fact]
    public void Zero_opacity_changes_nothing()
    {
        var bottom = Px(10, 20, 30, 255);
        BlendOps.Composite(bottom, Px(200, 100, 50, 255), BlendMode.Normal, 0);
        Assert.Equal(Px(10, 20, 30, 255), bottom);
    }

    [Fact]
    public void Semi_transparent_over_semi_transparent_uses_source_over_alpha()
    {
        var bottom = Px(0, 0, 255, 128);
        BlendOps.Composite(bottom, Px(255, 0, 0, 128), BlendMode.Normal, 1);
        // alpha = 0.5 + 0.5 * 0.5 = 0.75
        Assert.Equal(192, bottom[3]);
        Assert.InRange(bottom[0], 168, 172);
        Assert.InRange(bottom[2], 83, 87);
    }

    /// <summary>
    /// Every mode over every opacity on a fixed gradient: the checksum must be the same on all OSes.
    /// If a formula changes on purpose, update the expected hash.
    /// </summary>
    [Fact]
    public void Compositing_is_bit_identical_across_platforms()
    {
        const int n = 64;
        var output = new List<byte>();
        foreach (var mode in Enum.GetValues<BlendMode>())
        {
            foreach (var opacity in new[] { 1.0, 0.7, 0.33 })
            {
                var bottom = new byte[n * n * 4];
                var top = new byte[n * n * 4];
                for (var i = 0; i < n * n; i++)
                {
                    bottom[i * 4 + 0] = (byte)(i * 7);
                    bottom[i * 4 + 1] = (byte)(i * 3);
                    bottom[i * 4 + 2] = (byte)(i / 16);
                    bottom[i * 4 + 3] = (byte)(255 - i % 200);
                    top[i * 4 + 0] = (byte)(i * 5);
                    top[i * 4 + 1] = (byte)(255 - i % 256);
                    top[i * 4 + 2] = (byte)(i * 11);
                    top[i * 4 + 3] = (byte)(i % 256);
                }
                BlendOps.Composite(bottom, top, mode, opacity);
                output.AddRange(bottom);
            }
        }

        var hash = Convert.ToHexString(SHA256.HashData(output.ToArray()));
        Assert.Equal(ExpectedHash, hash);
    }

    private const string ExpectedHash = "22582FBC7042C9FE34991700943BECCC7F0965EFEF7CEB6A617824309910768A";
}
