using System.Diagnostics;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Services;
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
            }

            var doc = Assert.Single(workspace.OpenDocuments);
            Assert.Equal((20, 10), (doc.ImageSize.Width, doc.ImageSize.Height));
        }
        Assert.False(File.Exists(socket));
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
