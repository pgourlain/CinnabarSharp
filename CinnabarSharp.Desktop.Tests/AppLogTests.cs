using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using CinnabarSharp.Desktop.Services;

namespace CinnabarSharp.Desktop.Tests;

/// <summary>log.txt: what a user can send after a problem (performance-tasks.md P6 / automation-tasks.md).</summary>
[Collection("AppLog")]
public sealed class AppLogTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("cinnabarsharp-log-");

    public void Dispose()
    {
        AppLog.Configure(null, toStderr: false);
        _folder.Delete(recursive: true);
    }

    private string Text => File.ReadAllText(Path.Combine(_folder.FullName, "log.txt"));

    [Fact]
    public void A_session_starts_with_the_version_and_system_and_lines_have_a_level_and_source()
    {
        AppLog.Configure(_folder.FullName, toStderr: false);

        AppLog.Info("test", "hello");
        AppLog.Error("test", "broken", new InvalidOperationException("because"));

        var lines = Text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("INFO  app: CinnabarSharp", lines[0]);
        Assert.Contains(".NET", lines[0]);
        Assert.Contains("INFO  test: hello", lines[1]);
        Assert.Contains("ERROR test: broken", lines[2]);
        Assert.Contains("System.InvalidOperationException: because", Text); // the exception is in the file, indented
    }

    [Fact]
    public void The_file_is_rotated_when_it_gets_too_big()
    {
        AppLog.Configure(_folder.FullName, toStderr: false, maxBytes: 500);

        for (var i = 0; i < 40; i++)
            AppLog.Info("test", new string('x', 40) + i);

        Assert.True(File.Exists(Path.Combine(_folder.FullName, "log.1.txt")));
        Assert.True(new FileInfo(Path.Combine(_folder.FullName, "log.txt")).Length < 1500);
        Assert.Contains("x39", Text); // the newest lines are in log.txt
    }

    [Fact]
    public void Nothing_is_written_without_a_folder_and_a_folder_that_cannot_be_written_does_not_throw()
    {
        AppLog.Configure(null, toStderr: false);
        AppLog.Error("test", "nowhere");
        Assert.Null(AppLog.FilePath);

        var blocked = Path.Combine(_folder.FullName, "file");
        File.WriteAllText(blocked, "a file where the folder should be");
        AppLog.Configure(Path.Combine(blocked, "sub"), toStderr: false);
        AppLog.Error("test", "no place for this"); // must not throw
    }

    [Fact]
    public void Avalonia_messages_are_formatted_and_only_warnings_and_errors_are_kept()
    {
        AppLog.Configure(_folder.FullName, toStderr: false);
        var sink = new AppLogSink();

        Assert.False(sink.IsEnabled(LogEventLevel.Information, "Binding"));
        Assert.True(sink.IsEnabled(LogEventLevel.Warning, "Binding"));
        sink.Log(LogEventLevel.Warning, "Binding", new object(), "Error in binding to {Target}.{Property}: {Message}", "Button", "Content", "no value");

        Assert.Contains("WARN  avalonia/Binding: Error in binding to Button.Content: no value [Object]", Text);
    }

    [AvaloniaFact]
    public void An_error_shown_to_the_user_is_logged_before_the_dialog_appears()
    {
        AppLog.Configure(_folder.FullName, toStderr: false);
        using var h = new TestHarness();

        _ = new DialogService(h.Window).ShowErrorAsync("Could not open \"a.png\"", "The file is damaged.");

        Assert.Contains("ERROR dialog: Could not open \"a.png\": The file is damaged.", Text);
    }

    [Fact]
    public void An_exception_in_a_background_task_nobody_awaits_is_logged()
    {
        AppLog.Configure(_folder.FullName, toStderr: false);
        AppLog.HookGlobalHandlers();

        FaultAndForget();
        for (var i = 0; i < 20 && !File.ReadAllText(Path.Combine(_folder.FullName, "log.txt")).Contains("nobody awaited"); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Thread.Sleep(50);
        }

        Assert.Contains("ERROR task: Exception in a background task nobody awaited", Text);
        Assert.Contains("the preview failed", Text);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FaultAndForget() => _ = Task.Run(() => throw new InvalidOperationException("the preview failed"));

    [AvaloniaFact]
    public async Task Open_Log_Folder_shows_the_log_folder()
    {
        using var h = new TestHarness();
        AppLog.Configure(_folder.FullName, toStderr: false);

        await h.Vm.OpenLogFolderCommand.ExecuteAsync(null);

        Assert.Equal([_folder.FullName], h.Dialogs.OpenedFolders);
    }
}
