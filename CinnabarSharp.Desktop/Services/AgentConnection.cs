using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
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
public sealed class AgentConnection(IWorkspaceService workspace, IFormatManager formats,
    CinnabarSharp.Core.Tools.ITextRasterizer textRasterizer, ILogger<AgentConnection> logger)
    : IDisposable
{
    /// <summary>Name of the server in the agent's configuration.</summary>
    public const string ServerName = "cinnabarsharp-live";

    public const string DocsUrl = "https://github.com/pgourlain/CinnabarSharp/blob/main/docs/mcp.md";

    private AttachListener? _listener;

    public bool IsRunning => _listener is not null;

    /// <summary>The socket agents connect to (tests use their own).</summary>
    public string SocketPath { get; set; } = AttachListener.DefaultSocketPath;

    /// <summary>Agents connected right now.</summary>
    public int ConnectedAgents => _listener?.ClientCount ?? 0;

    /// <summary>Raised on the UI thread when the server starts or stops, or an agent connects or disconnects.</summary>
    public event Action? Changed;

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
        var context = new McpContext(workspace, formats, policy, new UiThreadDispatcher(), attached: true, textRasterizer);
        _listener = AttachListener.Start(context, SocketPath, logger);
        if (_listener is null)
            return false;
        _listener.ClientsChanged += () => Dispatcher.UIThread.Post(() => Changed?.Invoke());
        Changed?.Invoke();
        return true;
    }

    public void Stop()
    {
        if (_listener is null)
            return;
        _listener.Dispose();
        _listener = null;
        Changed?.Invoke();
    }

    public void Dispose() => Stop();

    /// <summary>
    /// The command an agent runs to reach this app: this executable with "--mcp --attach", or "dotnet CinnabarSharp.dll"
    /// when the app runs from a build folder.
    /// </summary>
    public static IReadOnlyList<string> LaunchCommand(string? processPath = null, string? appAssembly = null)
    {
        processPath ??= Environment.ProcessPath ?? "CinnabarSharp";
        // Either separator: a Windows path must be recognized whatever the OS running this code.
        var name = processPath[(processPath.LastIndexOfAny(['/', '\\']) + 1)..];
        var viaDotnet = name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
        return viaDotnet
            ? [processPath, appAssembly ?? typeof(AgentConnection).Assembly.Location, "--mcp", "--attach"]
            : [processPath, "--mcp", "--attach"];
    }

    /// <summary>Shell command that registers this app with Claude Code.</summary>
    public static string ClaudeCodeCommand(IReadOnlyList<string> launch) =>
        $"claude mcp add {ServerName} -- " + string.Join(" ", launch.Select(a => a.StartsWith("--") ? a : $"\"{a}\""));

    /// <summary>The "mcpServers" entry for claude_desktop_config.json.</summary>
    public static string ClaudeDesktopConfig(IReadOnlyList<string> launch) => JsonSerializer.Serialize(
        new Dictionary<string, object>
        {
            ["mcpServers"] = new Dictionary<string, object>
            {
                [ServerName] = new { command = launch[0], args = launch.Skip(1).ToArray() },
            },
        },
        new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private sealed class UiThreadDispatcher : IMcpDispatcher
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
