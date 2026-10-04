using System.Text.Json;

namespace CinnabarSharp.Mcp.Scripting;

/// <summary>
/// Turns the text of a script argument into the JSON value a tool expects, from the tool's input schema (so
/// <c>width=1920</c> is a number and <c>name=1920</c> stays a string).
/// </summary>
public static class ScriptArguments
{
    /// <summary>The JSON types a schema property accepts, in schema order, without "null".</summary>
    public static IReadOnlyList<string> TypesOf(JsonElement property)
    {
        var types = new List<string>();
        void Add(JsonElement element)
        {
            if (element.TryGetProperty("type", out var type))
            {
                if (type.ValueKind == JsonValueKind.String)
                    types.Add(type.GetString()!);
                else if (type.ValueKind == JsonValueKind.Array)
                    types.AddRange(type.EnumerateArray().Select(t => t.GetString()!));
            }
        }
        Add(property);
        foreach (var key in new[] { "anyOf", "oneOf" })
            if (property.TryGetProperty(key, out var options) && options.ValueKind == JsonValueKind.Array)
                foreach (var option in options.EnumerateArray())
                    Add(option);
        return types.Where(t => t != "null").Distinct().ToList();
    }

    /// <summary>The value as JSON, or throws a message saying what the parameter expects.</summary>
    public static JsonElement Convert(string name, string text, JsonElement property)
    {
        var types = TypesOf(property);
        foreach (var type in types)
        {
            switch (type)
            {
                case "integer" when long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign,
                                                  System.Globalization.CultureInfo.InvariantCulture, out var whole):
                    return JsonSerializer.SerializeToElement(whole, ScriptJson.Default.Int64);
                case "number" when double.TryParse(text, System.Globalization.NumberStyles.Float,
                                                   System.Globalization.CultureInfo.InvariantCulture, out var number)
                                   && double.IsFinite(number):
                    return JsonSerializer.SerializeToElement(number, ScriptJson.Default.Double);
                case "boolean" when text.Equals("true", StringComparison.OrdinalIgnoreCase)
                                    || text.Equals("false", StringComparison.OrdinalIgnoreCase):
                    return JsonSerializer.SerializeToElement(text.Equals("true", StringComparison.OrdinalIgnoreCase), ScriptJson.Default.Boolean);
                case "array" or "object" when TryParseJson(text, type, out var json):
                    return json;
                case "string":
                    return JsonSerializer.SerializeToElement(text, ScriptJson.Default.String);
            }
        }
        // A schema without a type accepts anything: JSON if it is JSON, else text.
        if (types.Count == 0)
            return TryParseJson(text, null, out var any) ? any : JsonSerializer.SerializeToElement(text, ScriptJson.Default.String);
        throw new ScriptException(0, $"{name} expects {string.Join(" or ", types)}, not '{text}'.");
    }

    private static bool TryParseJson(string text, string? expected, out JsonElement element)
    {
        element = default;
        try
        {
            using var document = JsonDocument.Parse(text);
            var kind = document.RootElement.ValueKind;
            if (expected == "array" && kind != JsonValueKind.Array || expected == "object" && kind != JsonValueKind.Object)
                return false;
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
