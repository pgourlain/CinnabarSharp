using System.Net.Sockets;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using CinnabarSharp.Desktop.Services;

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
}
