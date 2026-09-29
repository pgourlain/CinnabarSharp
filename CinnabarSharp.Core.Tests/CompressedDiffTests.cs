using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public sealed class CompressedDiffTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("cinnabarsharp-compresseddiff-");

    public void Dispose() => _root.Delete(recursive: true);

    private IHistoryDocumentStorage Storage() => new FileHistoryStorage(_root).OpenDocumentStorage();

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

    [Fact]
    public async Task Spill_moves_the_bytes_to_disk_and_frees_memory()
    {
        var raw = new byte[] { 1, 2, 3, 4, 5 };
        var diff = new CompressedDiff(raw);
        await diff.PendingCompression;

        diff.Spill(Storage());
        await diff.PendingSpill;

        Assert.True(diff.IsSpilled);
        Assert.Equal(0, diff.Bytes);
        Assert.Equal(raw, diff.Get());
    }

    [Fact]
    public async Task Spill_before_compression_finishes_is_a_no_op_the_caller_can_retry()
    {
        var raw = new byte[100_000];
        var diff = new CompressedDiff(raw);

        diff.Spill(Storage()); // compression very likely still running: nothing to spill yet

        Assert.False(diff.IsSpilled);
        await diff.PendingCompression;
        diff.Spill(Storage()); // retry once compressed
        await diff.PendingSpill;
        Assert.True(diff.IsSpilled);
        Assert.Equal(raw, diff.Get());
    }

    [Fact]
    public async Task Set_after_spilling_discards_the_spilled_blob()
    {
        var diff = new CompressedDiff([1, 2, 3]);
        await diff.PendingCompression;
        diff.Spill(Storage());
        await diff.PendingSpill;
        Assert.True(diff.IsSpilled);

        var replacement = new byte[] { 9, 9, 9 };
        diff.Set(replacement);

        Assert.False(diff.IsSpilled);
        Assert.Equal(replacement, diff.Get());
    }

    [Fact]
    public async Task Dispose_deletes_the_spilled_blob()
    {
        var storage = Storage();
        var diff = new CompressedDiff([1, 2, 3]);
        await diff.PendingCompression;
        diff.Spill(storage);
        await diff.PendingSpill;
        var scopeDir = _root.GetDirectories().Single();
        Assert.NotEmpty(scopeDir.GetFiles());

        diff.Dispose();

        Assert.Empty(scopeDir.GetFiles());
    }
}
