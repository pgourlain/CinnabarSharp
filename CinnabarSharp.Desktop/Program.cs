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
        // "--run script.txt": runs a script of tool calls without a window (see CinnabarSharp.Mcp.Scripting).
        if (Mcp.Scripting.ScriptRunner.IsRunCommand(args))
            return Mcp.Scripting.ScriptRunner.RunAsync(args).GetAwaiter().GetResult();
        // "--mcp": MCP server for AI agents over stdio, without a window (see CinnabarSharp.Mcp).
        if (Mcp.McpHost.IsMcpCommand(args))
            return Mcp.McpHost.RunAsync(args).GetAwaiter().GetResult();
        AppLog.Configure(AppLog.DefaultFolder, toStderr: !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AppLog.EnvironmentVariable)));
        AppLog.HookGlobalHandlers();
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
            .LogToAppLog();
}
