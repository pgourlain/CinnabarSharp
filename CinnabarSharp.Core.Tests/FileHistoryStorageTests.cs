using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public sealed class FileHistoryStorageTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("cinnabarsharp-history-storage-");

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public async Task Written_blob_reads_back_the_same_bytes()
    {
        var storage = new FileHistoryStorage(_root);
        var scope = storage.OpenDocumentStorage();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var blob = await scope.WriteAsync(data);

        Assert.NotNull(blob);
        Assert.Equal(data, blob.Read());
    }

    [Fact]
    public async Task Disposing_a_blob_deletes_its_file_but_leaves_the_scope_usable()
    {
        var storage = new FileHistoryStorage(_root);
        var scope = storage.OpenDocumentStorage();
        var blob = await scope.WriteAsync([1, 2, 3]);
        Assert.NotNull(blob);

        blob.Dispose();

        var blob2 = await scope.WriteAsync([4, 5, 6]);
        Assert.NotNull(blob2);
        Assert.Equal(new byte[] { 4, 5, 6 }, blob2.Read());
    }

    [Fact]
    public async Task Disposing_the_scope_deletes_every_blob_still_held()
    {
        var storage = new FileHistoryStorage(_root);
        var scope = storage.OpenDocumentStorage();
        await scope.WriteAsync([1]);
        await scope.WriteAsync([2]);
        var scopeDir = _root.GetDirectories().Single();
        Assert.NotEmpty(scopeDir.GetFiles());

        scope.Dispose();

        scopeDir.Refresh(); // DirectoryInfo caches Exists; force a fresh check
        Assert.False(scopeDir.Exists);
    }

    [Fact]
    public async Task Writing_after_the_scope_is_disposed_fails_gracefully()
    {
        var storage = new FileHistoryStorage(_root);
        var scope = storage.OpenDocumentStorage();
        scope.Dispose();

        var blob = await scope.WriteAsync([1, 2, 3]);

        Assert.Null(blob);
    }

    [Fact]
    public async Task Different_document_scopes_do_not_collide()
    {
        var storage = new FileHistoryStorage(_root);
        var a = storage.OpenDocumentStorage();
        var b = storage.OpenDocumentStorage();

        var blobA = await a.WriteAsync([1]);
        var blobB = await b.WriteAsync([2]);

        Assert.Equal(2, _root.GetDirectories().Length);
        Assert.NotNull(blobA);
        Assert.NotNull(blobB);
        Assert.Equal(new byte[] { 1 }, blobA.Read());
        Assert.Equal(new byte[] { 2 }, blobB.Read());
    }

    [Fact]
    public void CleanOrphans_removes_every_leftover_folder()
    {
        _root.CreateSubdirectory("leftover-1");
        _root.CreateSubdirectory("leftover-2");

        FileHistoryStorage.CleanOrphans(_root);

        Assert.Empty(_root.GetDirectories());
    }

    [Fact]
    public void CleanOrphans_on_a_missing_root_does_nothing()
    {
        var missing = new DirectoryInfo(Path.Combine(_root.FullName, "does-not-exist"));

        FileHistoryStorage.CleanOrphans(missing); // must not throw
    }
}
