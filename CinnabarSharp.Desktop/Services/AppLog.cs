using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Logging;

namespace CinnabarSharp.Desktop.Services;

/// <summary>
/// Diagnostics for problems a user can't see: errors shown in a dialog, exceptions of background tasks nobody awaits,
/// Avalonia's warnings. Written to <c>log.txt</c> in the app-data folder (rotated at 1 MB: the previous one is
/// <c>log.1.txt</c>) so a user can send it after a problem, and to stderr too when the environment variable
/// <c>CINNABARSHARP_LOG</c> is set. Writing never throws. Without <see cref="Configure"/> nothing is written (tests).
/// </summary>
public static class AppLog
{
    public const string EnvironmentVariable = "CINNABARSHARP_LOG";
    public const long MaxBytes = 1_000_000;

    private static readonly object Gate = new();
    private static string? _folder;
    private static bool _toStderr;
    private static long _maxBytes = MaxBytes;

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CinnabarSharp");

    /// <summary>The folder of the log file, or null when logging to a file is off.</summary>
    public static string? Folder => _folder;

    public static string? FilePath => _folder is null ? null : Path.Combine(_folder, "log.txt");

    /// <summary>
    /// Starts a session: <paramref name="folder"/> (null = no file) and, if asked, a copy on stderr. The first line of the
    /// session says which version runs on which system.
    /// </summary>
    public static void Configure(string? folder, bool toStderr, long maxBytes = MaxBytes)
    {
        lock (Gate)
        {
            _folder = folder;
            _toStderr = toStderr;
            _maxBytes = maxBytes;
        }
        if (folder is not null || toStderr)
        {
            var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion.Split('+')[0] ?? "?";
            Write("INFO", "app", $"CinnabarSharp {version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), " +
                                 $"{RuntimeInformation.FrameworkDescription}, " +
                                 $"{(RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT")}", null, session: true);
        }
    }

    public static void Info(string source, string message) => Write("INFO", source, message, null);

    public static void Warning(string source, string message, Exception? exception = null) => Write("WARN", source, message, exception);

    public static void Error(string source, string message, Exception? exception = null) => Write("ERROR", source, message, exception);

    /// <summary>Logs exceptions nobody handled: of background tasks that were never awaited, and the fatal ones.</summary>
    public static void HookGlobalHandlers()
    {
        TaskScheduler.UnobservedTaskException += (_, e) => Error("task", "Exception in a background task nobody awaited", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Error("app", e.IsTerminating ? "Unhandled exception, the app stops" : "Unhandled exception", e.ExceptionObject as Exception);
    }

    private static void Write(string level, string source, string message, Exception? exception, bool session = false)
    {
        try
        {
            var time = DateTime.Now;
            var text = message + (exception is null ? "" : Environment.NewLine + "    " + exception.ToString().ReplaceLineEndings(Environment.NewLine + "    "));
            string? file;
            bool stderr;
            lock (Gate)
            {
                file = FilePath;
                stderr = _toStderr;
                if (file is not null)
                {
                    Directory.CreateDirectory(_folder!);
                    if (File.Exists(file) && new FileInfo(file).Length > _maxBytes)
                        File.Move(file, Path.Combine(_folder!, "log.1.txt"), overwrite: true);
                    File.AppendAllText(file, (session ? Environment.NewLine : "") +
                        $"{time:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {source}: {text}{Environment.NewLine}");
                }
            }
            if (stderr)
                Console.Error.WriteLine($"log {time:HH:mm:ss.fff} {level,-5} {source}: {text}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A log that can't be written must never break the app.
        }
    }
}

/// <summary>Avalonia's log (bindings that don't resolve, rendering problems…) at Warning and above, into <see cref="AppLog"/>.</summary>
public sealed class AppLogSink : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Write(level, area, source, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Write(level, area, source, messageTemplate, propertyValues);

    private static void Write(LogEventLevel level, string area, object? source, string template, object?[] values)
    {
        var message = Format(template, values) + (source is null ? "" : $" [{source.GetType().Name}]");
        if (level >= LogEventLevel.Error)
            AppLog.Error("avalonia/" + area, message);
        else
            AppLog.Warning("avalonia/" + area, message);
    }

    /// <summary>Fills the {Placeholders} of an Avalonia message template in order.</summary>
    internal static string Format(string template, object?[] values)
    {
        var index = 0;
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{[^{}]*\}",
            _ => index < values.Length ? values[index++]?.ToString() ?? "null" : "?");
    }
}

public static class AppLogExtensions
{
    /// <summary>Replaces <c>LogToTrace</c>: Avalonia's warnings and errors go to <see cref="AppLog"/>.</summary>
    public static AppBuilder LogToAppLog(this AppBuilder builder) => builder.AfterSetup(_ => Logger.Sink = new AppLogSink());
}
