using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tests;

public sealed class CompressedDiffTests
{
    [Fact]
    public async Task Get_returns_the_original_bytes_before_compression_finishes()
    {
        var raw = new byte[] { 1, 2, 3, 4, 5 };
        var diff = new CompressedDiff(raw);

        Assert.Equal(raw, diff.Get());
        await diff.PendingCompression;
        Assert.Equal(raw, diff.Get());
    }

    [Fact]
    public async Task Mostly_zero_diff_compresses_smaller_once_the_background_task_finishes()
    {
        var raw = new byte[64 * 1024]; // untouched pixels diff to zero
        raw[100] = 42;
        raw[50_000] = 7;
        var diff = new CompressedDiff(raw);

        await diff.PendingCompression;

        Assert.True(diff.Bytes < raw.Length / 10, $"expected strong compression, got {diff.Bytes} bytes from {raw.Length}");
        Assert.Equal(raw, diff.Get());
    }

    [Fact]
    public async Task Set_replaces_the_value_even_while_the_previous_compression_is_pending()
    {
        var diff = new CompressedDiff(new byte[100_000]); // large enough to still be compressing
        var replacement = new byte[] { 9, 8, 7 };

        diff.Set(replacement);
        await diff.PendingCompression; // the replacement's own compression, not the superseded one

        Assert.Equal(replacement, diff.Get()); // never the stale 100,000-byte buffer's result
    }
}
