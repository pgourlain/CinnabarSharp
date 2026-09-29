using System.IO.Compression;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Models;

/// <summary>
/// A byte buffer that compresses itself on a background thread shortly after it is set, and decompresses on
/// demand. <see cref="Get"/> is always correct regardless of whether the background compression has finished:
/// it returns the raw bytes while they are still around, or decompresses otherwise. A generation counter lets
/// a compression (or a disk write, see <see cref="Spill"/>) that finishes after a newer <see cref="Set"/>
/// discard its now-stale result instead of racing it.
/// </summary>
internal sealed class CompressedDiff : IDisposable
{
    private readonly object _gate = new();
    private byte[]? _raw;
    private byte[]? _compressed;
    private IHistoryBlob? _blob;
    private int _rawLength;
    private int _generation;
    private Task _pending = Task.CompletedTask;
    private Task _pendingSpill = Task.CompletedTask;

    public CompressedDiff(byte[] raw)
    {
        Set(raw);
    }

    /// <summary>Bytes currently held in memory: 0 once spilled to disk, the compressed size once ready, the raw
    /// size until then.</summary>
    public long Bytes
    {
        get { lock (_gate) return _blob is not null ? 0 : _compressed?.Length ?? _raw?.Length ?? 0; }
    }

    public bool IsSpilled { get { lock (_gate) return _blob is not null; } }

    /// <summary>The background compression task for the most recent <see cref="Set"/>, for tests to await.</summary>
    public Task PendingCompression { get { lock (_gate) return _pending; } }

    /// <summary>The background disk write for the most recent <see cref="Spill"/>, for tests to await.</summary>
    public Task PendingSpill { get { lock (_gate) return _pendingSpill; } }

    public void Set(byte[] raw)
    {
        int generation;
        lock (_gate)
        {
            _raw = raw;
            _rawLength = raw.Length;
            _compressed = null;
            DisposeBlob();
            generation = ++_generation;
        }
        lock (_gate)
            _pending = Task.Run(() => Compress(raw, generation));
    }

    public byte[] Get()
    {
        lock (_gate)
        {
            if (_raw is not null)
                return _raw;
            var compressedBytes = _blob is not null ? _blob.Read() : _compressed!;
            var result = new byte[_rawLength];
            using var input = new MemoryStream(compressedBytes);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            deflate.ReadExactly(result);
            return result;
        }
    }

    /// <summary>
    /// Moves the compressed bytes to <paramref name="storage"/> on a background thread, freeing them from memory
    /// once the write finishes. A no-op if already spilled or if the background compression hasn't produced
    /// compressed bytes yet (the caller retries later); does nothing to <see cref="Get"/>'s correctness if the
    /// write fails or is superseded by a newer <see cref="Set"/> — the data simply stays in memory.
    /// </summary>
    public void Spill(IHistoryDocumentStorage storage)
    {
        byte[] compressed;
        int generation;
        lock (_gate)
        {
            if (_blob is not null || _compressed is null)
                return;
            compressed = _compressed;
            generation = _generation;
        }
        lock (_gate)
            _pendingSpill = Task.Run(async () =>
            {
                var blob = await storage.WriteAsync(compressed);
                if (blob is null)
                    return; // disk full / not writable / folder gone: stays in memory
                lock (_gate)
                {
                    if (_generation != generation)
                    {
                        blob.Dispose(); // a newer Set() landed while this write was in flight
                        return;
                    }
                    _blob = blob;
                    _compressed = null;
                }
            });
    }

    public void Dispose()
    {
        lock (_gate)
            DisposeBlob();
    }

    private void DisposeBlob()
    {
        _blob?.Dispose();
        _blob = null;
    }

    private void Compress(byte[] raw, int generation)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(raw);
        var compressed = output.ToArray();

        lock (_gate)
        {
            if (_generation != generation)
                return; // superseded by a newer Set() while this ran
            _compressed = compressed;
            _raw = null;
        }
    }
}
