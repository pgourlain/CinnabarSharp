using System.Diagnostics;
using System.Text.Json;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services;

/// <summary>An unsaved document left behind by a session that ended without closing (a crash, a power cut).</summary>
/// <param name="DataFile">The saved copy: an .ora for an image, an .svg for a drawing.</param>
public sealed record RecoverableDocument(string SessionFolder, string DataFile, string Name, string? OriginalPath, DocumentKind Kind, DateTime SavedUtc);

/// <summary>
/// Keeps a copy of every document with unsaved changes in a folder of its own, so that work survives a crash. Each running
/// session owns a sub-folder (named after its process id); a normal exit deletes it, so one that is left whose process is gone
/// means the session did not end properly. Copies are written beside the document, never over it, and never change it.
/// </summary>
public sealed class RecoveryStore
{
    private const string SessionFile = "session.json";

    private readonly string _root;
    private readonly IFormatManager _formats;
    private readonly Func<int, bool> _isAlive;
    private readonly int _pid;
    private string? _session;
    private readonly Dictionary<Guid, string> _written = [];

    public RecoveryStore(string root, IFormatManager formats, Func<int, bool>? isAlive = null, int? processId = null)
    {
        _root = root;
        _formats = formats;
        _isAlive = isAlive ?? IsProcessAlive;
        _pid = processId ?? Environment.ProcessId;
    }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CinnabarSharp", "recovery");

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Starts this session's folder.</summary>
    public void Begin()
    {
        _session = Path.Combine(_root, $"{_pid}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_session);
        File.WriteAllText(Path.Combine(_session, SessionFile), JsonSerializer.Serialize(new SessionInfo(_pid, DateTime.UtcNow), RecoveryJson.Default.SessionInfo));
    }

    /// <summary>Normal exit: nothing to recover.</summary>
    public void End()
    {
        if (_session is { } session)
            TryDelete(session);
        _session = null;
        _written.Clear();
    }

    public bool Has(IDocument document) => _written.ContainsKey(document.Id);

    /// <summary>Writes a copy of the document (atomically: a crash in the middle leaves the previous copy).</summary>
    public void Save(IDocument document)
    {
        if (_session is null)
            return;
        var extension = document.Kind == DocumentKind.Svg ? "svg" : "ora";
        var data = Path.Combine(_session, $"{document.Id:N}.{extension}");
        var temporary = data + ".tmp";
        var format = _formats.GetFormatByExtension(extension) ?? throw new InvalidOperationException($"No {extension} format.");
        format.ExportDocument(document, new FileInfo(temporary));
        File.Move(temporary, data, overwrite: true);
        var meta = new RecoveryMeta(document.DisplayName, document.File?.FullName, document.Kind, DateTime.UtcNow);
        File.WriteAllText(Path.Combine(_session, $"{document.Id:N}.json"), JsonSerializer.Serialize(meta, RecoveryJson.Default.RecoveryMeta));
        _written[document.Id] = data;
    }

    /// <summary>The document was saved, or closed: its copy is not needed.</summary>
    public void Remove(Guid id)
    {
        if (_session is null)
            return;
        foreach (var file in Directory.EnumerateFiles(_session, id.ToString("N") + ".*"))
            TryDelete(file);
        _written.Remove(id);
    }

    public IReadOnlyCollection<Guid> Written => _written.Keys.ToList();

    /// <summary>Copies left by sessions whose process is gone (never this session's).</summary>
    public IReadOnlyList<RecoverableDocument> FindRecoverable()
    {
        var result = new List<RecoverableDocument>();
        if (!Directory.Exists(_root))
            return result;
        foreach (var folder in Directory.EnumerateDirectories(_root))
        {
            if (string.Equals(folder, _session, StringComparison.Ordinal) || SessionAlive(folder))
                continue;
            foreach (var metaFile in Directory.EnumerateFiles(folder, "*.json").Where(f => Path.GetFileName(f) != SessionFile))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize(File.ReadAllText(metaFile), RecoveryJson.Default.RecoveryMeta);
                    var data = Path.ChangeExtension(metaFile, meta?.Kind == DocumentKind.Svg ? "svg" : "ora");
                    if (meta is not null && File.Exists(data))
                        result.Add(new RecoverableDocument(folder, data, meta.Name, meta.OriginalPath, meta.Kind, meta.SavedUtc));
                }
                catch (Exception e) when (e is JsonException or IOException)
                {
                    // A copy that cannot be read is skipped; it is removed with its session.
                }
            }
        }
        return result.OrderBy(d => d.SavedUtc).ToList();
    }

    private bool SessionAlive(string folder)
    {
        try
        {
            var info = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(folder, SessionFile)), RecoveryJson.Default.SessionInfo);
            return info is not null && info.Pid != _pid && _isAlive(info.Pid);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return false;
        }
    }

    /// <summary>Deletes the sessions' folders (after the copies were restored or refused).</summary>
    public void Discard(IEnumerable<RecoverableDocument> documents)
    {
        foreach (var folder in documents.Select(d => d.SessionFolder).Distinct())
            TryDelete(folder);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else
                File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    internal sealed record SessionInfo(int Pid, DateTime StartedUtc);

    internal sealed record RecoveryMeta(string Name, string? OriginalPath, DocumentKind Kind, DateTime SavedUtc);
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(RecoveryStore.SessionInfo))]
[System.Text.Json.Serialization.JsonSerializable(typeof(RecoveryStore.RecoveryMeta))]
internal sealed partial class RecoveryJson : System.Text.Json.Serialization.JsonSerializerContext;
