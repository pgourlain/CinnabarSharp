using System.Net.Sockets;
using System.Reflection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CinnabarSharp.Mcp;

/// <summary>
/// Entry points of the MCP server. <c>CinnabarSharp --mcp</c> runs it headless over stdio;
/// <c>CinnabarSharp --mcp --attach</c> relays stdio to the running app (see <see cref="AttachListener"/>).
/// </summary>
public static class McpHost
{
    /// <summary>Environment variable with more allowed folders, separated like PATH.</summary>
    public const string AllowEnvironmentVariable = "CINNABARSHARP_MCP_ALLOW";

    public const string Instructions =
        "CinnabarSharp is a Paint.NET-like image editor. Open or create an image (open_image, new_image), edit it with " +
        "the tools (apply_effect with names from list_effects, crop, resize_image, layers, selections), look at the " +
        "result with render_preview, then save_image or export_image. Edits apply to the current layer inside the " +
        "selection, if any. Every edit is one undo step (undo/redo). Files can only be read and written inside the " +
        "allowed folders; replacing a file needs overwrite=true and closing unsaved work needs discardChanges=true.";

    public static bool IsMcpCommand(string[] args) => args.Contains("--mcp");

    /// <summary>Runs the server for the command line; returns the process exit code.</summary>
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellation = default)
    {
        if (args.Contains("--attach"))
            return await AttachProxy.RunAsync(Option(args, "--socket") ?? AttachListener.DefaultSocketPath, cancellation);

        var folders = Options(args, "--allow")
            .Concat((Environment.GetEnvironmentVariable(AllowEnvironmentVariable) ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .DefaultIfEmpty(Environment.CurrentDirectory);
        var policy = new FileAccessPolicy(folders);

        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        // stdout carries the protocol: logs go to stderr.
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace).SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddCinnabarSharpServices();
        builder.Services.AddSingleton<IMcpDispatcher, SerialDispatcher>();
        builder.Services.AddSingleton(policy);
        builder.Services.AddSingleton(sp => new McpContext(sp.GetRequiredService<IWorkspaceService>(),
            sp.GetRequiredService<IFormatManager>(), policy, sp.GetRequiredService<IMcpDispatcher>()));
        AddServer(builder.Services).WithStdioServerTransport();
        await builder.Build().RunAsync(cancellation);
        return 0;
    }

    /// <summary>Serves one client over a stream (attached mode: one per connection), until the client disconnects.</summary>
    public static async Task ServeAsync(Stream input, Stream output, McpContext context, CancellationToken cancellation = default)
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Services.AddSingleton(context);
        AddServer(builder.Services).WithStreamServerTransport(input, output);
        using var host = builder.Build();
        await host.RunAsync(cancellation);
    }

    private static IMcpServerBuilder AddServer(IServiceCollection services) => services
        .AddMcpServer(o =>
        {
            o.ServerInfo = new Implementation
            {
                Name = "CinnabarSharp",
                Version = (Assembly.GetEntryAssembly() ?? typeof(McpHost).Assembly).GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion.Split('+')[0] ?? "0.0.0",
            };
            o.ServerInstructions = Instructions;
        })
        .WithTools<ImageTools>()
        .WithResources<ImageResources>();

    private static string? Option(string[] args, string name) => Options(args, name).LastOrDefault();

    private static IEnumerable<string> Options(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                yield return args[i + 1];
    }
}

/// <summary>
/// Attached mode, app side: listens on a Unix domain socket (supported on Windows 10+ too) in the user's app-data
/// folder and serves each connection with <see cref="McpHost.ServeAsync"/>, so an agent edits the open documents.
/// </summary>
public sealed class AttachListener : IDisposable
{
    private readonly Socket _socket;
    private readonly string _path;
    private readonly McpContext _context;
    private readonly CancellationTokenSource _stop = new();
    private readonly ILogger? _logger;

    public static string DefaultSocketPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CinnabarSharp", "mcp.sock");

    private AttachListener(Socket socket, string path, McpContext context, ILogger? logger)
    {
        (_socket, _path, _context, _logger) = (socket, path, context, logger);
        _ = AcceptLoopAsync();
    }

    /// <summary>Starts listening, or returns null if another CinnabarSharp window already accepts agents.</summary>
    public static AttachListener? Start(McpContext context, string? path = null, ILogger? logger = null)
    {
        path ??= DefaultSocketPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            if (CanConnect(path))
                return null;
            File.Delete(path); // left over by an app that crashed
        }
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        socket.Listen();
        return new AttachListener(socket, path, context, logger);
    }

    private static bool CanConnect(string path)
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Connect(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public string SocketPath => _path;

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _socket.AcceptAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(async () =>
            {
                await using var stream = new NetworkStream(client, ownsSocket: true);
                try
                {
                    await McpHost.ServeAsync(stream, stream, _context, _stop.Token);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger?.LogWarning(e, "MCP client session ended with an error");
                }
            });
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _socket.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Attached mode, agent side: relays the agent's stdio to the app's socket.</summary>
public static class AttachProxy
{
    public static async Task<int> RunAsync(string socketPath, CancellationToken cancellation = default)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellation);
        }
        catch (SocketException)
        {
            await Console.Error.WriteLineAsync(
                "CinnabarSharp is not running or doesn't accept agents. Start it and turn on File › Allow AI Agents (MCP), " +
                "or run 'CinnabarSharp --mcp' without --attach to edit files without the window.");
            return 1;
        }
        await using var remote = new NetworkStream(socket, ownsSocket: false);
        await using var stdin = Console.OpenStandardInput();
        await using var stdout = Console.OpenStandardOutput();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var up = Pump(stdin, remote, stop.Token, () =>
        {
            try
            {
                socket.Shutdown(SocketShutdown.Send);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
            }
        });
        var down = Pump(remote, stdout, stop.Token, null);
        await Task.WhenAny(up, down);
        // The app closed the connection, or the agent closed stdin and the app has answered everything.
        if (down.IsCompleted)
            stop.Cancel();
        await down;
        return 0;
    }

    private static async Task Pump(Stream from, Stream to, CancellationToken cancellation, Action? onEnd)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            int read;
            while ((read = await from.ReadAsync(buffer, cancellation)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read), cancellation);
                await to.FlushAsync(cancellation);
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }
        onEnd?.Invoke();
    }
}
