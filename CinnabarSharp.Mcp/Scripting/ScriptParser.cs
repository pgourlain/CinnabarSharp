using System.Text;
using System.Text.RegularExpressions;

namespace CinnabarSharp.Mcp.Scripting;

/// <summary>A problem in a script, with the line to show to the user (0 when it isn't about one line).</summary>
public sealed class ScriptException(int line, string message) : Exception(message)
{
    public int Line { get; } = line;
}

/// <summary>One argument of a command: <c>name=value</c> (the value, quotes removed, variables not yet expanded).</summary>
public sealed record ScriptArgument(string Name, string Value);

/// <summary>One line of a script: a tool name and its arguments.</summary>
public sealed record ScriptCommand(int Line, string Name, IReadOnlyList<ScriptArgument> Arguments)
{
    public override string ToString() => Arguments.Count == 0 ? Name : $"{Name} {string.Join(' ', Arguments.Select(a => $"{a.Name}={a.Value}"))}";
}

/// <summary>
/// The script syntax: one command per line, <c>tool_name name=value name="value with spaces" list=[1,2,3]</c>;
/// <c>#</c> starts a comment; blank lines are ignored. Command names are the MCP tool names. Pure text handling, no MCP.
/// </summary>
public static partial class ScriptParser
{
    public static IReadOnlyList<ScriptCommand> Parse(string script)
    {
        var commands = new List<ScriptCommand>();
        var lines = script.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var tokens = Tokenize(lines[i].TrimEnd('\r'), i + 1);
            if (tokens.Count == 0)
                continue;
            var name = tokens[0];
            if (name.Contains('=') || name.StartsWith('"') || name.StartsWith('['))
                throw new ScriptException(i + 1, $"'{name}' should be a command name, such as open_image.");
            var arguments = new List<ScriptArgument>();
            foreach (var token in tokens.Skip(1))
            {
                var equals = token.IndexOf('=');
                if (equals <= 0 || !IsName(token[..equals]))
                    throw new ScriptException(i + 1, $"'{token}' should be name=value (quote a value that contains spaces).");
                arguments.Add(new ScriptArgument(token[..equals], token[(equals + 1)..]));
            }
            commands.Add(new ScriptCommand(i + 1, name, arguments));
        }
        return commands;
    }

    private static bool IsName(string text) => text.Length > 0 && (char.IsLetter(text[0]) || text[0] == '_')
                                                && text.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Splits a line into tokens: the command, then name=value pairs. Quotes ("…" with \" and \\, or '…' as is) group a
    /// value; [...] and {...} are kept whole (JSON); an unquoted # starts a comment. Quotes are removed.
    /// </summary>
    private static List<string> Tokenize(string line, int number)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '#' && !inToken)
                break;
            if (char.IsWhiteSpace(c))
            {
                if (inToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
                continue;
            }
            inToken = true;
            switch (c)
            {
                case '"':
                    i = ReadQuoted(line, i, '"', current, number, escapes: true);
                    break;
                case '\'':
                    i = ReadQuoted(line, i, '\'', current, number, escapes: false);
                    break;
                case '[' or '{' when current.Length > 0 && current[^1] == '=':
                    i = ReadBalanced(line, i, current, number);
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }
        if (inToken)
            tokens.Add(current.ToString());
        return tokens;
    }

    private static int ReadQuoted(string line, int start, char quote, StringBuilder into, int number, bool escapes)
    {
        for (var i = start + 1; i < line.Length; i++)
        {
            if (escapes && line[i] == '\\' && i + 1 < line.Length && line[i + 1] is '"' or '\\')
                into.Append(line[++i]);
            else if (line[i] == quote)
                return i;
            else
                into.Append(line[i]);
        }
        throw new ScriptException(number, $"The {quote} quote is never closed.");
    }

    /// <summary>A JSON array or object, up to its matching bracket (quotes inside it are kept as they are).</summary>
    private static int ReadBalanced(string line, int start, StringBuilder into, int number)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < line.Length; i++)
        {
            var c = line[i];
            into.Append(c);
            if (inString)
            {
                if (c == '\\' && i + 1 < line.Length)
                    into.Append(line[++i]);
                else if (c == '"')
                    inString = false;
            }
            else if (c == '"')
                inString = true;
            else if (c is '[' or '{')
                depth++;
            else if (c is ']' or '}' && --depth == 0)
                return i;
        }
        throw new ScriptException(number, "A [ or { is never closed.");
    }

    // ---- Variables and ~ ----

    [GeneratedRegex(@"\$(?:\$|([A-Za-z_][A-Za-z0-9_]*)|\{([A-Za-z_][A-Za-z0-9_]*)\})")]
    private static partial Regex VariablePattern();

    /// <summary>
    /// Replaces $name and ${name} with their value ($$ is a literal $) and a leading ~ with the home folder. An unknown
    /// variable is an error, so a typo can't write to a wrong path.
    /// </summary>
    public static string Expand(string value, IReadOnlyDictionary<string, string> variables, int line)
    {
        var expanded = VariablePattern().Replace(value, m =>
        {
            if (m.Value == "$$")
                return "$";
            var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return variables.TryGetValue(name, out var found)
                ? found
                : throw new ScriptException(line, $"Unknown variable ${name}. Known: {string.Join(", ", variables.Keys.Select(k => "$" + k))}.");
        });
        if (expanded == "~" || expanded.StartsWith("~/") || expanded.StartsWith("~\\"))
            expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + expanded[1..];
        return expanded;
    }
}
