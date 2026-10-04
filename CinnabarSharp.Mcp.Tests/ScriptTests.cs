using System.Diagnostics;
using System.Text.Json;
using CinnabarSharp.Mcp.Scripting;
using ModelContextProtocol.Client;

namespace CinnabarSharp.Mcp.Tests;

/// <summary>
/// "CinnabarSharp --run script.txt": runs the real app (or the native build in CINNABARSHARP_TEST_EXE) on scripts and
/// checks the files, the exit code and the messages. The smoke test script is the one the release workflow runs on every
/// packaged build.
/// </summary>
public class ScriptTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("cinnabar-script-");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ScriptTests()
    {
        Directory.CreateDirectory(Samples);
        Directory.CreateDirectory(Out);
        foreach (var name in new[] { "sample1.png", "sample1.heic" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", name), Path.Combine(Samples, name));
    }

    public void Dispose() => _folder.Delete(recursive: true);

    private string Samples => Path.Combine(_folder.FullName, "samples");
    private string Out => Path.Combine(_folder.FullName, "out");

    private async Task<(int Exit, string Stderr, string Stdout)> Run(string scriptText, params string[] options)
    {
        var script = Path.Combine(_folder.FullName, "script.txt");
        await File.WriteAllTextAsync(script, scriptText, Ct);
        return await RunFile(script, options);
    }

    private async Task<(int Exit, string Stderr, string Stdout)> RunFile(string script, params string[] options)
    {
        var start = new ProcessStartInfo(McpTestServer.Command)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = _folder.FullName,
        };
        foreach (var argument in McpTestServer.Arguments(["--run", script, "--var", "samples=" + Samples, "--var", "out=" + Out, "--allow", Samples, "--allow", Out, .. options]))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync(Ct);
        var stdout = process.StandardOutput.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);
        return (process.ExitCode, await stderr, await stdout);
    }

    [Fact]
    public async Task A_script_edits_and_saves_an_image()
    {
        var (exit, stderr, _) = await Run("""
            # black and white, half size, as PNG
            open_image path=$samples/sample1.heic
            apply_effect effect="Black and White"
            resize_image percent=50
            save_image path=$out/result.png overwrite=true
            close_image
            """);

        Assert.True(exit == 0, stderr);
        Assert.Contains("[4/5] save_image", stderr);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(Out, "result.png"), Ct);
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, bytes[..4]);
    }

    [Fact]
    public async Task The_script_runs_for_each_input_file_with_its_variables()
    {
        var (exit, stderr, _) = await Run("""
            open_image path=$file
            save_image path=$out/$name-$ext.jpg format=jpeg overwrite=true
            """, "--input", Path.Combine(Samples, "*.*"), "--quiet");

        Assert.True(exit == 0, stderr);
        Assert.Equal(["sample1-heic.jpg", "sample1-png.jpg"],
            Directory.GetFiles(Out).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_failing_file_stops_the_run_unless_keep_going()
    {
        var broken = Path.Combine(Samples, "a-broken.png");
        await File.WriteAllTextAsync(broken, "not an image", Ct);
        const string script = "open_image path=$file\nsave_image path=$out/$name.png overwrite=true";

        var (exit, stderr, _) = await Run(script, "--input", Path.Combine(Samples, "*.png"), "--quiet");
        Assert.Equal(1, exit);
        Assert.Contains("script.txt:1: open_image", stderr);
        Assert.False(File.Exists(Path.Combine(Out, "sample1.png"))); // a-broken.png comes first and stopped the run

        (exit, stderr, _) = await Run(script, "--input", Path.Combine(Samples, "*.png"), "--quiet", "--keep-going");
        Assert.Equal(1, exit);
        Assert.Contains("1 of 2 files failed", stderr);
        Assert.True(File.Exists(Path.Combine(Out, "sample1.png")));
    }

    [Theory]
    [InlineData("open_image path=$samples/sample1.png\nblur_all radius=3", 2, "Unknown command 'blur_all'")]
    [InlineData("open_image file=$samples/sample1.png", 1, "has no parameter 'file'")]
    [InlineData("open_image", 1, "needs path")]
    [InlineData("open_image path=$samples/sample1.png\nresize_image width=abc", 2, "width expects integer")]
    [InlineData("open_image path=\"$samples/sample1.png", 1, "never closed")]
    public async Task A_bad_script_is_refused_before_anything_runs(string script, int line, string message)
    {
        var (exit, stderr, _) = await Run(script);

        Assert.Equal(2, exit);
        Assert.Contains($"line {line}:", stderr);
        Assert.Contains(message, stderr);
        Assert.DoesNotContain("[1/", stderr); // nothing ran
    }

    [Theory]
    [InlineData("open_image path=$samples/missing.png", "open_image")]
    [InlineData("open_image path=$samples/sample1.png\napply_effect effect=Nope", "Unknown effect 'Nope'")]
    [InlineData("open_image path=$samples/sample1.png\nsave_image path=$samples/sample1.png", "already exists")]
    [InlineData("open_image path=$samples/sample1.png\nsave_image path=$nowhere/x.png", "Unknown variable $nowhere")]
    public async Task A_failing_command_stops_the_script_with_its_line_and_exit_code_1(string script, string message)
    {
        var (exit, stderr, _) = await Run(script);

        Assert.Equal(1, exit);
        Assert.Contains("script.txt:", stderr);
        Assert.Contains(message, stderr);
    }

    [Fact]
    public async Task Files_outside_the_allowed_folders_can_not_be_written()
    {
        var outside = Path.Combine(Path.GetTempPath(), "cinnabar-outside-" + Guid.NewGuid().ToString("N") + ".png");

        var (exit, stderr, _) = await Run($"open_image path=$samples/sample1.png\nsave_image path=\"{outside}\" overwrite=true");

        Assert.Equal(1, exit);
        Assert.Contains("outside the allowed folders", stderr);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task Verbose_prints_the_tool_results_and_a_missing_script_or_option_is_a_usage_error()
    {
        var (exit, _, stdout) = await Run("open_image path=$samples/sample1.png\nlist_documents", "--verbose", "--quiet");
        Assert.Equal(0, exit);
        Assert.Contains("sample1.png", stdout);

        var (missing, stderr, _) = await RunFile(Path.Combine(_folder.FullName, "nothing.txt"));
        Assert.Equal(2, missing);
        Assert.Contains("Script not found", stderr);

        var (bad, stderr2, _) = await Run("list_documents", "--bogus");
        Assert.Equal(2, bad);
        Assert.Contains("Unknown option --bogus", stderr2);
    }

    // ---- The smoke test run by the release workflow ----

    private static string SmokeScript => Path.Combine(AppContext.BaseDirectory, "Data", "smoke-test.txt");

    [Fact]
    public async Task The_smoke_test_script_runs_and_writes_every_format()
    {
        var (exit, stderr, _) = await RunFile(SmokeScript, "--quiet");

        Assert.True(exit == 0, stderr);
        foreach (var file in new[] { "result.png", "result.jpg", "result.ora", "flat.png", "flat.jpg" })
            Assert.True(new FileInfo(Path.Combine(Out, file)).Length > 1000, file);
    }

    [Fact]
    public async Task The_smoke_test_script_calls_every_tool_and_applies_every_effect()
    {
        var commands = ScriptParser.Parse(await File.ReadAllTextAsync(SmokeScript, Ct));
        var called = commands.Select(c => c.Name).ToHashSet();
        var applied = commands.Where(c => c.Name == "apply_effect")
            .Select(c => c.Arguments.First(a => a.Name == "effect").Value).ToHashSet();

        await using var server = await McpTestServer.StartAsync();
        var tools = (await server.Client.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name);
        var effects = JsonDocument.Parse((await server.Call("list_effects")).GetRawText()).RootElement
            .EnumerateArray().Select(e => e.GetProperty("name").GetString()!);

        // add_speech_bubble draws text, which headless mode can't (no fonts): the UI tests cover it.
        Assert.Empty(tools.Except(called).Where(t => t != "add_speech_bubble"));
        Assert.Empty(effects.Except(applied));
    }
}
