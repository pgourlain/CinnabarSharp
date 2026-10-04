using Avalonia;
using System;
using CinnabarSharp.Desktop.Services;

namespace CinnabarSharp.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        StartupTrace.Mark("Main");
        // "--mcp": MCP server for AI agents over stdio, without a window (see CinnabarSharp.Mcp).
        if (Mcp.McpHost.IsMcpCommand(args))
            return Mcp.McpHost.RunAsync(args).GetAwaiter().GetResult();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
