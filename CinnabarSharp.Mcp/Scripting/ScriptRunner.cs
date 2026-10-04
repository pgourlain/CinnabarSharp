using System.Diagnostics;
using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CinnabarSharp.Mcp.Scripting;

/// <summary>What the command line of <c>--run</c> asked for.</summary>
public sealed record ScriptOptions(
    string ScriptPath,
    IReadOnlyList<string> Inputs,
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlyList<string> AllowedFolders,
    bool KeepGoing,
    bool Quiet,
    bool Verbose);

/// <summary>
/// <c>CinnabarSharp --run script.txt [--input file-or-pattern]… [--var name=value]… [--allow folder]… [--keep-going]
/// [--quiet] [--verbose]</c>: runs a script of MCP tool calls without a window, once, or once per input file. It starts
/// the same server as <c>--mcp</c> inside the process and calls its tools through an MCP client, so a script has exactly
/// what an agent has (tools, arguments, folder rules, undo history). Progress goes to stderr, tool results to stdout
/// with --verbose. Exit code: 0 all good, 1 a command failed, 2 a bad script or command line.
/// </summary>
public static partial class ScriptRunner
{
    public static bool IsRunCommand(string[] args) => args.Contains("--run") && !args.Contains("--mcp");

    public static async Task<int> RunAsync(string[] args, TextWriter? stderr = null, TextWriter? stdout = null,
        CancellationToken cancellation = default)
    {
        stderr ??= Console.Error;
        stdout ??= Console.Out;
        try
        {
            var options = ParseCommandLine(args);
            return await RunAsync(options, stderr, stdout, cancellation);
        }
        catch (ScriptException e)
        {
            stderr.WriteLine(e.Line > 0 ? $"error: line {e.Line}: {e.Message}" : $"error: {e.Message}");
            return 2;
        }
    }

    // ---- Command line ----

    private static ScriptOptions ParseCommandLine(string[] args)
    {
        string? script = null;
        var inputs = new List<string>();
        var allow = new List<string>();
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        bool keepGoing = false, quiet = false, verbose = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Value() => i + 1 < args.Length ? args[++i] : throw new ScriptException(0, args[i] == "--run" ? "--run needs a script file." : $"{args[i]} needs a value.");
            switch (args[i])
            {
                case "--run": script = Value(); break;
                case "--input": inputs.Add(Value()); break;
                case "--allow": allow.Add(Value()); break;
                case "--var":
                    var pair = Value();
                    var equals = pair.IndexOf('=');
                    if (equals <= 0)
                        throw new ScriptException(0, $"--var {pair}: use --var name=value.");
                    variables[pair[..equals]] = pair[(equals + 1)..];
                    break;
                case "--keep-going": keepGoing = true; break;
                case "--quiet": quiet = true; break;
                case "--verbose": verbose = true; break;
                default:
                    throw new ScriptException(0, $"Unknown option {args[i]}. Options: --run script --input file --var name=value --allow folder --keep-going --quiet --verbose.");
            }
        }
        return new ScriptOptions(script ?? throw new ScriptException(0, "--run needs a script file."), inputs, variables, allow, keepGoing, quiet, verbose);
    }

    /// <summary>The files an --input names: a file, or a pattern (* and ?) in a folder; sorted, so runs are repeatable.</summary>
    public static IReadOnlyList<string> ExpandInputs(IEnumerable<string> patterns)
    {
        var files = new List<string>();
        foreach (var pattern in patterns)
        {
            var path = ScriptParser.Expand(pattern.Replace("$", "$$"), new Dictionary<string, string>(), 0);
            var name = Path.GetFileName(path);
            if (name.Contains('*') || name.Contains('?'))
            {
                var folder = Path.GetDirectoryName(path);
                var found = Directory.Exists(string.IsNullOrEmpty(folder) ? "." : folder)
                    ? Directory.EnumerateFiles(string.IsNullOrEmpty(folder) ? "." : folder, name).OrderBy(f => f, StringComparer.Ordinal).ToList()
                    : [];
                if (found.Count == 0)
                    throw new ScriptException(0, $"--input {pattern}: no file matches.");
                files.AddRange(found.Select(Path.GetFullPath));
            }
            else if (File.Exists(path))
                files.Add(Path.GetFullPath(path));
            else
                throw new ScriptException(0, $"--input {pattern}: file not found.");
        }
        return files;
    }

    // ---- Running ----

    private static async Task<int> RunAsync(ScriptOptions options, TextWriter stderr, TextWriter stdout, CancellationToken cancellation)
    {
        if (!File.Exists(options.ScriptPath))
            throw new ScriptException(0, $"Script not found: {options.ScriptPath}");
        var script = ScriptParser.Parse(File.ReadAllText(options.ScriptPath));
        if (script.Count == 0)
            throw new ScriptException(0, "The script has no command.");
        var inputs = ExpandInputs(options.Inputs);

        // Folders the scripts may use: the command line's, else the current one; the input files' folders are named by the user.
        var folders = options.AllowedFolders
            .Concat((Environment.GetEnvironmentVariable(McpHost.AllowEnvironmentVariable) ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Concat(inputs.Select(f => Path.GetDirectoryName(f)!))
            .DefaultIfEmpty(Environment.CurrentDirectory)
            .Select(f => ScriptParser.Expand(f.Replace("$", "$$"), new Dictionary<string, string>(), 0));

        using var services = new ServiceCollection().AddLogging().AddCinnabarSharpServices().BuildServiceProvider();
        var workspace = services.GetRequiredService<IWorkspaceService>();
        var context = new McpContext(workspace, services.GetRequiredService<IFormatManager>(), new FileAccessPolicy(folders), new SerialDispatcher());

        // The server and the client talk through two in-memory pipes.
        var toServer = new Pipe();
        var toClient = new Pipe();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var server = McpHost.ServeAsync(toServer.Reader.AsStream(), toClient.Writer.AsStream(), context, stop.Token);
        await using var client = await McpClient.CreateAsync(
            new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: cancellation);

        try
        {
            var tools = (await client.ListToolsAsync(cancellationToken: cancellation))
                .ToDictionary(t => t.Name, t => t.ProtocolTool.InputSchema, StringComparer.OrdinalIgnoreCase);
            Validate(script, tools);

            var failures = new List<string>();
            foreach (var input in inputs.Count == 0 ? [null] : inputs.Select(i => (string?)i))
            {
                foreach (var document in workspace.OpenDocuments.ToList())
                    workspace.CloseDocument(document);
                var variables = new Dictionary<string, string>(options.Variables);
                if (input is not null)
                {
                    variables["file"] = input;
                    variables["name"] = Path.GetFileNameWithoutExtension(input);
                    variables["dir"] = Path.GetDirectoryName(input)!;
                    variables["ext"] = Path.GetExtension(input).TrimStart('.');
                    if (!options.Quiet)
                        stderr.WriteLine($"== {input}");
                }
                var error = await RunScriptAsync(script, tools, client, variables, options, stderr, stdout, cancellation);
                if (error is null)
                    continue;
                failures.Add(input is null ? error : $"{input}: {error}");
                stderr.WriteLine($"{options.ScriptPath}:{error}");
                if (!options.KeepGoing)
                    break;
            }
            if (failures.Count > 0 && options.KeepGoing && inputs.Count > 1)
                stderr.WriteLine($"{failures.Count} of {inputs.Count} files failed.");
            return failures.Count == 0 ? 0 : 1;
        }
        finally
        {
            await client.DisposeAsync();
            stop.Cancel();
            try
            {
                await server.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch (Exception e) when (e is OperationCanceledException or TimeoutException)
            {
            }
        }
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^An error occurred invoking '[^']*': ")]
    private static partial System.Text.RegularExpressions.Regex ErrorPrefix();

    /// <summary>Checks the whole script before anything runs: command names, parameter names, required parameters.</summary>
    private static void Validate(IReadOnlyList<ScriptCommand> script, IReadOnlyDictionary<string, JsonElement> tools)
    {
        foreach (var command in script)
        {
            if (!tools.TryGetValue(command.Name, out var schema))
                throw new ScriptException(command.Line, $"Unknown command '{command.Name}'. Commands are the MCP tools (e.g. open_image, apply_effect, save_image); see docs/mcp.md.");
            var properties = schema.TryGetProperty("properties", out var p) ? p : default;
            var known = properties.ValueKind == JsonValueKind.Object ? properties.EnumerateObject().Select(x => x.Name).ToList() : [];
            foreach (var argument in command.Arguments)
            {
                if (!known.Contains(argument.Name, StringComparer.OrdinalIgnoreCase))
                    throw new ScriptException(command.Line, $"{command.Name} has no parameter '{argument.Name}'. Parameters: {string.Join(", ", known)}.");
                // A value without a variable can be checked now; the others when their file is known.
                if (!argument.Value.Contains('$'))
                {
                    var property = properties.EnumerateObject().First(x => x.Name.Equals(argument.Name, StringComparison.OrdinalIgnoreCase));
                    try
                    {
                        ScriptArguments.Convert(property.Name, argument.Value, property.Value);
                    }
                    catch (ScriptException e)
                    {
                        throw new ScriptException(command.Line, e.Message);
                    }
                }
            }
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
                    if (!command.Arguments.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        throw new ScriptException(command.Line, $"{command.Name} needs {name}=…");
        }
    }

    /// <summary>Runs the script once; returns null, or "line: command: message" for the first command that failed.</summary>
    private static async Task<string?> RunScriptAsync(IReadOnlyList<ScriptCommand> script,
        IReadOnlyDictionary<string, JsonElement> tools, McpClient client, IReadOnlyDictionary<string, string> variables,
        ScriptOptions options, TextWriter stderr, TextWriter stdout, CancellationToken cancellation)
    {
        for (var i = 0; i < script.Count; i++)
        {
            var command = script[i];
            var watch = Stopwatch.StartNew();
            try
            {
                var schema = tools[command.Name];
                var properties = schema.GetProperty("properties");
                var arguments = new Dictionary<string, object?>();
                foreach (var argument in command.Arguments)
                {
                    var property = properties.EnumerateObject().First(x => x.Name.Equals(argument.Name, StringComparison.OrdinalIgnoreCase));
                    var text = ScriptParser.Expand(argument.Value, variables, command.Line);
                    try
                    {
                        arguments[property.Name] = ScriptArguments.Convert(property.Name, text, property.Value);
                    }
                    catch (ScriptException e)
                    {
                        throw new ScriptException(command.Line, e.Message);
                    }
                }
                var toolName = tools.Keys.First(k => k.Equals(command.Name, StringComparison.OrdinalIgnoreCase));
                var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellation);
                // The SDK prefixes errors with "An error occurred invoking 'tool': "; the tool name is shown already.
                var output = ErrorPrefix().Replace(string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text)), "");
                if (result.IsError == true)
                    return $"{command.Line}: {toolName}: {(output.Length > 0 ? output : "the command failed")}";
                if (!options.Quiet)
                    stderr.WriteLine($"[{i + 1}/{script.Count}] {command} … ok, {watch.ElapsedMilliseconds} ms");
                if (options.Verbose && output.Length > 0)
                    stdout.WriteLine(output);
            }
            catch (ScriptException e)
            {
                return $"{e.Line}: {command.Name}: {e.Message}";
            }
            catch (McpException e)
            {
                return $"{command.Line}: {command.Name}: {e.Message}";
            }
        }
        return null;
    }
}
