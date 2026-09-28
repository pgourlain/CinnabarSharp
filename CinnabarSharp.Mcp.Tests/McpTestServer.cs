using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CinnabarSharp.Mcp.Tests;

/// <summary>
/// Starts the real app as an MCP server ("CinnabarSharp --mcp") in a temporary allowed folder that holds a copy of
/// the sample photo, and connects an MCP client to it over stdio.
/// </summary>
public sealed class McpTestServer : IAsyncDisposable
{
    private McpTestServer(McpClient client, string folder)
    {
        Client = client;
        Folder = folder;
    }

    public McpClient Client { get; }

    /// <summary>The allowed folder; contains sample1.png (1024×576).</summary>
    public string Folder { get; }

    public string SamplePath => Path.Combine(Folder, "sample1.png");

    public static string AppPath => Path.Combine(AppContext.BaseDirectory, "CinnabarSharp.dll");

    public static async Task<McpTestServer> StartAsync(params string[] extraArguments)
    {
        var folder = Directory.CreateTempSubdirectory("cinnabar-mcp-").FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "sample1.png"), Path.Combine(folder, "sample1.png"));
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "CinnabarSharp",
            Command = DotnetHost,
            Arguments = [AppPath, "--mcp", "--allow", folder, .. extraArguments],
            WorkingDirectory = folder,
            ShutdownTimeout = TimeSpan.FromSeconds(1),
        });
        var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        return new McpTestServer(client, folder);
    }

    /// <summary>The dotnet executable running the tests (set by dotnet test), or "dotnet" from the PATH.</summary>
    public static string DotnetHost => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";

    /// <summary>Calls a tool that must succeed and returns its JSON result.</summary>
    public async Task<JsonElement> Call(string tool, object? arguments = null)
    {
        var result = await CallRaw(tool, arguments);
        Assert.False(result.IsError == true, $"{tool} failed: {Text(result)}");
        return JsonDocument.Parse(Text(result)).RootElement.Clone();
    }

    /// <summary>Calls a tool that must fail and returns its error message.</summary>
    public async Task<string> CallError(string tool, object? arguments = null)
    {
        var result = await CallRaw(tool, arguments);
        Assert.True(result.IsError == true, $"{tool} should have failed but returned {Text(result)}");
        return Text(result);
    }

    public Task<CallToolResult> CallRaw(string tool, object? arguments = null) =>
        Client.CallToolAsync(tool, ToDictionary(arguments), cancellationToken: TestContext.Current.CancellationToken).AsTask();

    public static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    private static Dictionary<string, object?>? ToDictionary(object? arguments) =>
        arguments is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments));

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class JsonExtensions
{
    public static JsonElement Get(this JsonElement element, string name) =>
        element.EnumerateObject().First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

    public static int Int(this JsonElement element, string name) => element.Get(name).GetInt32();

    public static string? Str(this JsonElement element, string name) => element.Get(name).GetString();
}
