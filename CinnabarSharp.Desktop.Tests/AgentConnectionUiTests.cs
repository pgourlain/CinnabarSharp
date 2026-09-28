using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop.Tests;

/// <summary>MCP attached mode: an agent connected to the app's socket edits the documents shown in the window.</summary>
public sealed class AgentConnectionUiTests : IDisposable
{
    private readonly TestHarness _h = new();
    private readonly string _socket = Path.Combine(Path.GetTempPath(), $"cs-{Guid.NewGuid():N}"[..12] + ".sock");

    public void Dispose()
    {
        _h.Vm.AllowAgents = false;
        _h.Dispose();
    }

    [AvaloniaFact]
    public async Task Agent_edits_are_shown_and_undoable_in_the_window()
    {
        var agents = _h.Services.GetRequiredService<AgentConnection>();
        agents.SocketPath = _socket;
        _h.Vm.Agents = agents;
        _h.Vm.AllowAgents = true;
        Assert.True(agents.IsRunning);
        Assert.True(File.Exists(_socket));

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socket));
        var stream = new NetworkStream(socket, ownsSocket: true);
        await using (var client = await McpClient.CreateAsync(new StreamClientTransport(stream, stream)))
        {
            var created = await client.CallToolAsync("new_image",
                new Dictionary<string, object?> { ["width"] = 64, ["height"] = 32, ["background"] = "#2060A0" });
            Assert.NotEqual(true, created.IsError);
            var inverted = await client.CallToolAsync("apply_effect", new Dictionary<string, object?> { ["effect"] = "Invert Colors" });
            Assert.NotEqual(true, inverted.IsError);
        }
        Dispatcher.UIThread.RunJobs();

        var doc = Assert.Single(_h.Vm.Documents);
        Assert.Same(doc, _h.Vm.ActiveDocument);
        Assert.Equal(["New Image", "Invert Colors"], _h.Vm.History.Select(h => h.Text).ToArray());
        var frame = _h.Capture("agent-attached");
        Assert.Equal((0xDF, 0x9F, 0x5F), TestHarness.PixelAt(frame, _h.CanvasToWindow(10, 10)));

        _h.Vm.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0x20, 0x60, 0xA0), TestHarness.PixelAt(_h.Capture("agent-attached-undo"), _h.CanvasToWindow(10, 10)));

        _h.Vm.AllowAgents = false;
        Assert.False(agents.IsRunning);
        Assert.False(File.Exists(_socket));
    }

    private AgentConnection Allow()
    {
        var agents = _h.Services.GetRequiredService<AgentConnection>();
        agents.SocketPath = _socket;
        _h.Vm.Agents = agents;
        _h.Vm.AllowAgents = true;
        Dispatcher.UIThread.RunJobs();
        return agents;
    }

    private static void WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public void Turning_agents_on_shows_how_to_connect_but_not_when_restoring_settings()
    {
        Allow();
        var shown = Assert.Single(_h.Dialogs.AgentConnectionsShown);
        Assert.StartsWith("claude mcp add cinnabarsharp-live -- ", shown.ClaudeCodeCommand);
        Assert.EndsWith(" --mcp --attach", shown.ClaudeCodeCommand);

        _h.Vm.AllowAgents = false;
        _h.Vm.ApplySettings(new AppSettings { AllowAgents = true });
        Dispatcher.UIThread.RunJobs();
        Assert.True(_h.Vm.AllowAgents);
        Assert.Single(_h.Dialogs.AgentConnectionsShown);

        Assert.True(_h.Vm.ShowAgentConnectionCommand.CanExecute(null));
        _h.Vm.ShowAgentConnectionCommand.Execute(null);
        Assert.Equal(2, _h.Dialogs.AgentConnectionsShown.Count);
    }

    [AvaloniaFact]
    public async Task Status_bar_shows_connected_agents()
    {
        Allow();
        var status = _h.Window.FindControl<Button>("AgentStatusButton")!;
        Assert.True(status.IsEffectivelyVisible);
        Assert.Equal("Waiting for an AI agent", _h.Vm.AgentStatusText);

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socket));
        var stream = new NetworkStream(socket, ownsSocket: true);
        await using (var client = await McpClient.CreateAsync(new StreamClientTransport(stream, stream)))
        {
            WaitFor(() => _h.Vm.AgentStatusText == "1 AI agent connected");
            _h.Capture("80-agent-connected-status");
        }
        await stream.DisposeAsync(); // what happens when the agent's proxy process exits
        WaitFor(() => _h.Vm.AgentStatusText == "Waiting for an AI agent");

        _h.Vm.AllowAgents = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(status.IsEffectivelyVisible);
        Assert.False(_h.Vm.ShowAgentConnectionCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("/Applications/CinnabarSharp.app/Contents/MacOS/CinnabarSharp", null,
        "claude mcp add cinnabarsharp-live -- \"/Applications/CinnabarSharp.app/Contents/MacOS/CinnabarSharp\" --mcp --attach")]
    [InlineData(@"C:\Program Files\CinnabarSharp\CinnabarSharp.exe", null,
        "claude mcp add cinnabarsharp-live -- \"C:\\Program Files\\CinnabarSharp\\CinnabarSharp.exe\" --mcp --attach")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", @"C:\My Apps\CinnabarSharp.dll",
        "claude mcp add cinnabarsharp-live -- \"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\My Apps\\CinnabarSharp.dll\" --mcp --attach")]
    [InlineData("/usr/share/dotnet/dotnet", "/home/me/CinnabarSharp.dll",
        "claude mcp add cinnabarsharp-live -- \"/usr/share/dotnet/dotnet\" \"/home/me/CinnabarSharp.dll\" --mcp --attach")]
    public void Claude_code_command_uses_the_running_executable(string process, string? assembly, string expected)
    {
        var launch = AgentConnection.LaunchCommand(process, assembly);
        Assert.Equal(expected, AgentConnection.ClaudeCodeCommand(launch));

        var json = System.Text.Json.JsonDocument.Parse(AgentConnection.ClaudeDesktopConfig(launch)).RootElement
            .GetProperty("mcpServers").GetProperty("cinnabarsharp-live");
        Assert.Equal(process, json.GetProperty("command").GetString());
        Assert.Equal(launch.Skip(1).ToArray(), json.GetProperty("args").EnumerateArray().Select(a => a.GetString()!).ToArray());
    }

    [AvaloniaFact]
    public async Task Connection_window_copies_the_commands()
    {
        var agents = Allow();
        using var vm = new AgentConnectionViewModel(agents, _h.Clipboard, "/Applications/CinnabarSharp.app/Contents/MacOS/CinnabarSharp");
        await vm.CopyClaudeCodeCommand.ExecuteAsync(null);
        Assert.Equal(vm.ClaudeCodeCommand, _h.Clipboard.Text);
        await vm.CopyClaudeDesktopCommand.ExecuteAsync(null);
        Assert.Equal(vm.ClaudeDesktopConfig, _h.Clipboard.Text);
        Assert.Equal("Waiting for an AI agent", vm.StatusText);

        var window = new AgentConnectionWindow { DataContext = vm };
        window.Show();
        TestHarness.CaptureWindow(window, "81-connect-ai-agent");
        window.Close();
    }
}
