using System.Diagnostics;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace CinnabarSharp.Mcp.Tests;

/// <summary>
/// "CinnabarSharp --mcp --attach" relays stdio to an app listening on its socket. The app side runs in-process here
/// (the window itself is covered by the desktop UI tests).
/// </summary>
public class AttachTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Short path: Unix socket paths are limited to about 100 characters.
    private static string NewSocketPath() => Path.Combine(Path.GetTempPath(), $"cs-{Guid.NewGuid():N}"[..12] + ".sock");

    [Fact]
    public async Task Agent_drives_the_app_workspace_through_the_proxy()
    {
        var services = new ServiceCollection().AddLogging().AddCinnabarSharpServices().BuildServiceProvider();
        var workspace = services.GetRequiredService<IWorkspaceService>();
        var context = new McpContext(workspace, services.GetRequiredService<IFormatManager>(),
            new FileAccessPolicy([Path.GetTempPath()]), new SerialDispatcher(), attached: true);
        var socket = NewSocketPath();
        using (var listener = AttachListener.Start(context, socket))
        {
            Assert.NotNull(listener);
            Assert.Null(AttachListener.Start(context, socket)); // a second app doesn't take over

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = McpTestServer.DotnetHost,
                Arguments = [McpTestServer.AppPath, "--mcp", "--attach", "--socket", socket],
                ShutdownTimeout = TimeSpan.FromSeconds(1),
            });
            await using (var client = await McpClient.CreateAsync(transport, cancellationToken: Ct))
            {
                var result = await client.CallToolAsync("new_image",
                    new Dictionary<string, object?> { ["width"] = 20, ["height"] = 10 }, cancellationToken: Ct);
                Assert.NotEqual(true, result.IsError);
                Assert.Equal(1, listener.ClientCount);
            }

            // The proxy process exited: the app ends the session.
            for (var i = 0; i < 300 && listener.ClientCount > 0; i++)
                await Task.Delay(10, Ct);
            Assert.Equal(0, listener.ClientCount);

            var doc = Assert.Single(workspace.OpenDocuments);
            Assert.Equal((20, 10), (doc.ImageSize.Width, doc.ImageSize.Height));
        }
        Assert.False(File.Exists(socket));
    }

    /// <summary>Every character a solid block, so the test doesn't depend on fonts.</summary>
    private sealed class BlockText : ITextRasterizer
    {
        public TextRaster RenderLine(string text, TextStyle style)
        {
            var (w, h) = ((int)Math.Ceiling(MeasureWidth(text, style)), (int)style.Size);
            var coverage = new byte[w * h];
            Array.Fill(coverage, (byte)255);
            return new TextRaster(coverage, w, h, 0, 0);
        }

        public double MeasureWidth(string text, TextStyle style) => text.Length * style.Size / 2;
        public double LineHeight(TextStyle style) => style.Size;
    }

    [Fact]
    public async Task Agent_adds_speech_bubbles_with_the_app_fonts()
    {
        var services = new ServiceCollection().AddLogging().AddCinnabarSharpServices().BuildServiceProvider();
        var workspace = services.GetRequiredService<IWorkspaceService>();
        var context = new McpContext(workspace, services.GetRequiredService<IFormatManager>(),
            new FileAccessPolicy([Path.GetTempPath()]), new SerialDispatcher(), attached: true, new BlockText());
        using var listener = AttachListener.Start(context, NewSocketPath());
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = McpTestServer.DotnetHost,
            Arguments = [McpTestServer.AppPath, "--mcp", "--attach", "--socket", listener!.SocketPath],
            ShutdownTimeout = TimeSpan.FromSeconds(1),
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: Ct);
        await client.CallToolAsync("new_image", new Dictionary<string, object?> { ["width"] = 200, ["height"] = 120 }, cancellationToken: Ct);

        foreach (var (x, number) in new[] { (20, 1), (180, 2) })
        {
            var result = await client.CallToolAsync("add_speech_bubble", new Dictionary<string, object?>
            {
                ["text"] = "Look", ["x"] = x, ["y"] = 110, ["bubbleX"] = 100, ["bubbleY"] = 40,
                ["style"] = "oval", ["fill"] = "#FFFF00", ["number"] = number,
            }, cancellationToken: Ct);
            Assert.NotEqual(true, result.IsError);
        }

        var doc = Assert.Single(workspace.OpenDocuments);
        Assert.Equal(["New Image", "Add New Layer", "Speech Bubble", "Speech Bubble"],
            doc.Workspace.History.Items.Select(i => i.Text));
        Assert.Equal(SpeechBubbleTool.LayerName, doc.Layers.CurrentUserLayer.Name);
        // Inside the bubble, above the text: the fill color (BGRA).
        Assert.Equal([0, 255, 255, 255], doc.Layers.CurrentUserLayer.Surface.ReadRegion(new RectangleI(100, 20, 1, 1)));
    }

    [Fact]
    public async Task Proxy_explains_when_the_app_is_not_listening()
    {
        using var process = Process.Start(new ProcessStartInfo(McpTestServer.DotnetHost)
        {
            ArgumentList = { McpTestServer.AppPath, "--mcp", "--attach", "--socket", NewSocketPath() },
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        })!;
        var error = await process.StandardError.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);

        Assert.Equal(1, process.ExitCode);
        Assert.Contains("Allow AI Agents", error);
    }
}
