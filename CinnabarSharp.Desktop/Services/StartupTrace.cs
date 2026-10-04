using System;
using System.Diagnostics;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// Start-up timing for performance-tasks.md P6. Does nothing unless the environment variable
/// <c>CINNABARSHARP_STARTUP_TRACE</c> is set; each <see cref="Mark"/> then writes the time since the process started to
/// stderr. With the value <c>exit</c> the app also quits once the first frame is drawn (to script measurements).
/// </summary>
public static class StartupTrace
{
    private static readonly string? Mode = Environment.GetEnvironmentVariable("CINNABARSHARP_STARTUP_TRACE");

    public static bool Enabled => !string.IsNullOrEmpty(Mode);

    public static bool ExitWhenVisible => Mode == "exit";

    public static void Mark(string phase)
    {
        if (!Enabled)
            return;
        var elapsed = DateTime.Now - Process.GetCurrentProcess().StartTime;
        Console.Error.WriteLine($"startup {elapsed.TotalMilliseconds,7:0} ms  {phase}");
    }
}
