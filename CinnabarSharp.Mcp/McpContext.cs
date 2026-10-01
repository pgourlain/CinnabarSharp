using System.Runtime.CompilerServices;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;
using ImageMagick;
using ModelContextProtocol;

namespace CinnabarSharp.Mcp;

/// <summary>
/// Runs document edits on the thread that owns the documents: the UI thread in attached mode (the view model reacts
/// to Core events there), one call at a time in headless mode.
/// </summary>
public interface IMcpDispatcher
{
    Task<T> InvokeAsync<T>(Func<T> action);
}

/// <summary>Headless mode: calls run one after another on the caller's thread.</summary>
public sealed class SerialDispatcher : IMcpDispatcher
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<T> InvokeAsync<T>(Func<T> action)
    {
        await _lock.WaitAsync();
        try
        {
            return action();
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>What the MCP tools work on: the workspace (shared with the window in attached mode) and the rules.</summary>
public sealed class McpContext(IWorkspaceService workspace, IFormatManager formats, FileAccessPolicy files,
    IMcpDispatcher dispatcher, bool attached = false, ITextRasterizer? textRasterizer = null)
{
    private readonly ConditionalWeakTable<ImageDocument, object> _ids = new();
    private int _nextId = 1;

    public IWorkspaceService Workspace { get; } = workspace;
    public IFormatManager Formats { get; } = formats;
    public FileAccessPolicy Files { get; } = files;

    /// <summary>True when the desktop app hosts the server and the user sees the edits.</summary>
    public bool Attached { get; } = attached;

    /// <summary>The app's fonts, for tools that draw text; null in headless mode, which has no font rendering.</summary>
    public ITextRasterizer? TextRasterizer { get; } = textRasterizer;

    /// <summary>Stable id of a document for the whole session ("1", "2"…), even when other documents are closed.</summary>
    public string IdOf(ImageDocument document) =>
        ((StrongBox<int>)_ids.GetValue(document, _ => new StrongBox<int>(_nextId++))).Value.ToString();

    /// <summary>
    /// Runs <paramref name="action"/> on the documents' thread. Errors the agent can fix (bad names, missing files,
    /// unsupported formats) come back as tool errors with their message instead of a generic failure.
    /// </summary>
    public Task<T> Run<T>(Func<T> action) => dispatcher.InvokeAsync(() =>
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException
                                      or IOException or UnauthorizedAccessException or MagickException)
        {
            throw new McpException(e.Message, e);
        }
    });

    /// <summary>The document with this id or display name, or the active document when <paramref name="id"/> is empty.</summary>
    public ImageDocument Document(string? id)
    {
        if (!Workspace.HasOpenDocuments)
            throw new McpException("No image is open. Use open_image or new_image first.");
        if (string.IsNullOrWhiteSpace(id))
            return Workspace.ActiveDocument;
        return Workspace.OpenDocuments.FirstOrDefault(d => IdOf(d) == id)
            ?? Workspace.OpenDocuments.FirstOrDefault(d => string.Equals(d.DisplayName, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"No open image has the id or name '{id}'. Use list_documents to see them.");
    }
}
