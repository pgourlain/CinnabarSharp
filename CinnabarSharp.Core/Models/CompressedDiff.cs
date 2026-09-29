using System.IO.Compression;

namespace CinnabarSharp.Core.Models;

/// <summary>
/// A byte buffer that compresses itself on a background thread shortly after it is set, and decompresses on
/// demand. <see cref="Get"/> is always correct regardless of whether the background compression has finished:
/// it returns the raw bytes while they are still around, or decompresses otherwise. A generation counter lets
/// a compression that finishes after a newer <see cref="Set"/> discard its (now stale) result instead of racing it.
/// </summary>
internal sealed class CompressedDiff
{
    private readonly object _gate = new();
    private byte[]? _raw;
    private byte[]? _compressed;
    private int _rawLength;
    private int _generation;
    private Task _pending = Task.CompletedTask;

    public CompressedDiff(byte[] raw)
    {
        Set(raw);
    }

    /// <summary>Bytes currently held: the compressed size once ready, the raw size until then.</summary>
    public long Bytes
    {
        get { lock (_gate) return _compressed?.Length ?? _raw?.Length ?? 0; }
    }

    /// <summary>The background compression task for the most recent <see cref="Set"/>, for tests to await.</summary>
    public Task PendingCompression
    {
        get { lock (_gate) return _pending; }
    }

    public void Set(byte[] raw)
    {
        int generation;
        lock (_gate)
        {
            _raw = raw;
            _rawLength = raw.Length;
            _compressed = null;
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
            var result = new byte[_rawLength];
            using var input = new MemoryStream(_compressed!);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            deflate.ReadExactly(result);
            return result;
        }
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
