namespace CinnabarSharp.Core.Services;

/// <summary>
/// Where history steps too far from the current position to keep in memory are spilled to disk
/// (see <see cref="Models.ImageDocumentHistory"/>). Not wired into the app by default; a caller opts in by
/// registering an implementation (e.g. <see cref="FileHistoryStorage"/>) and passing it to
/// <c>ImageDocumentHistory</c>/<c>ImageDocument</c>.
/// </summary>
public interface IHistoryStorage
{
    /// <summary>A fresh, empty storage scope for one document's spilled steps.</summary>
    IHistoryDocumentStorage OpenDocumentStorage();
}

/// <summary>
/// One document's spilled history steps. Disposing it (the document closes, or history is cleared) deletes
/// every blob still held and anything left of the underlying folder.
/// </summary>
public interface IHistoryDocumentStorage : IDisposable
{
    /// <summary>
    /// Writes <paramref name="data"/> to a new blob and returns a handle to it, or null if the write failed
    /// (disk full, not writable, folder gone...) — the caller then keeps the data in memory instead.
    /// </summary>
    Task<IHistoryBlob?> WriteAsync(byte[] data, CancellationToken cancellation = default);
}

/// <summary>One spilled step's bytes. Disposing it deletes the underlying file.</summary>
public interface IHistoryBlob : IDisposable
{
    /// <summary>Reads the blob back. Synchronous: undo/redo stay synchronous, and a spilled step's compressed
    /// pixel diff is small enough to read well within the undo/redo time budget.</summary>
    byte[] Read();
}
