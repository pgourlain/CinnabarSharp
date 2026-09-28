using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Mcp;
using Microsoft.Extensions.Logging;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// MCP attached mode: while enabled, AI agents connected with "CinnabarSharp --mcp --attach" edit the open documents
/// and the user watches the changes. Edits run on the UI thread and are undoable like the user's own.
/// </summary>
public sealed class AgentConnection(IWorkspaceService workspace, IFormatManager formats, ILogger<AgentConnection> logger)
    : IDisposable
{
    private AttachListener? _listener;

    public bool IsRunning => _listener is not null;

    /// <summary>The socket agents connect to (tests use their own).</summary>
    public string SocketPath { get; set; } = AttachListener.DefaultSocketPath;

    /// <summary>Where agents may open and save files: the user's Pictures, Documents, Desktop and Downloads folders.</summary>
    public static IReadOnlyList<string> AllowedFolders
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    string.IsNullOrEmpty(home) ? "" : Path.Combine(home, "Downloads"),
                }
                .Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f) && f != home)
                .Distinct()
                .ToList();
        }
    }

    /// <summary>Starts accepting agents; false if another CinnabarSharp window already does.</summary>
    public bool Start()
    {
        if (_listener is not null)
            return true;
        var folders = AllowedFolders;
        var policy = new FileAccessPolicy(folders.Count > 0 ? folders : [Path.GetTempPath()]);
        var context = new McpContext(workspace, formats, policy, new UiThreadDispatcher(), attached: true);
        _listener = AttachListener.Start(context, SocketPath, logger);
        return _listener is not null;
    }

    public void Stop()
    {
        _listener?.Dispose();
        _listener = null;
    }

    public void Dispose() => Stop();

    private sealed class UiThreadDispatcher : IMcpDispatcher
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
