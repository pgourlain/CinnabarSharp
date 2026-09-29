namespace CinnabarSharp.Core.Services;

/// <summary>
/// Spills history steps to <c>LocalApplicationData/CinnabarSharp/history/&lt;run&gt;/&lt;document&gt;/&lt;step&gt;.bin</c>.
/// Every failure (read-only filesystem, disk full, folder removed from under it...) is swallowed and reported
/// as a failed write/missing folder, never thrown: callers fall back to keeping the data in memory.
/// </summary>
public sealed class FileHistoryStorage(DirectoryInfo root) : IHistoryStorage
{
    public static DirectoryInfo DefaultRoot => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CinnabarSharp", "history"));

    private int _scopeCounter;

    /// <summary>A ready-to-use storage rooted at <see cref="DefaultRoot"/>, after cleaning up orphan folders left
    /// by a previous run that didn't exit cleanly (crash, forced quit).</summary>
    public static FileHistoryStorage CreateDefault()
    {
        CleanOrphans(DefaultRoot);
        return new FileHistoryStorage(DefaultRoot);
    }

    public IHistoryDocumentStorage OpenDocumentStorage()
    {
        var name = $"{Environment.ProcessId}-{Interlocked.Increment(ref _scopeCounter)}-{Guid.NewGuid():N}";
        return new FileHistoryDocumentStorage(new DirectoryInfo(Path.Combine(root.FullName, name)));
    }

    /// <summary>Deletes every folder under <paramref name="root"/>: each run gets a freshly named one, so anything
    /// already there was left behind by a run that didn't clean up after itself. Best effort.</summary>
    public static void CleanOrphans(DirectoryInfo root)
    {
        if (!root.Exists)
            return;
        foreach (var dir in root.GetDirectories())
        {
            try { dir.Delete(recursive: true); }
            catch (IOException) { /* still in use (another instance?) or already gone: leave it */ }
            catch (UnauthorizedAccessException) { /* leave it */ }
        }
    }
}

internal sealed class FileHistoryDocumentStorage(DirectoryInfo dir) : IHistoryDocumentStorage
{
    private readonly bool _ready = TryCreate(dir);
    private int _counter;
    private bool _disposed;

    private static bool TryCreate(DirectoryInfo dir)
    {
        try
        {
            dir.Create();
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(dir.FullName,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public async Task<IHistoryBlob?> WriteAsync(byte[] data, CancellationToken cancellation = default)
    {
        if (!_ready || _disposed)
            return null;
        var file = new FileInfo(Path.Combine(dir.FullName, $"{Interlocked.Increment(ref _counter)}.bin"));
        try
        {
            await using var stream = new FileStream(file.FullName, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 4096, useAsync: true);
            await stream.WriteAsync(data, cancellation);
            return new FileHistoryBlob(file);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { if (dir.Exists) dir.Delete(recursive: true); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }
}

internal sealed class FileHistoryBlob(FileInfo file) : IHistoryBlob
{
    private bool _disposed;

    public byte[] Read() => File.ReadAllBytes(file.FullName);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { file.Delete(); }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }
}
