using System.Globalization;
using System.Text.Json;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Models;
using ModelContextProtocol;

namespace CinnabarSharp.Mcp;

/// <summary>Turns the agent's loosely typed arguments into Core values, with error messages that say what is valid.</summary>
public static class Parse
{
    /// <summary>Lower case letters and digits only: "Brightness / Contrast", "brightness_contrast" and "BrightnessContrast" match.</summary>
    public static string Key(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static T Enum<T>(string? text, T fallback, string what) where T : struct, System.Enum
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        foreach (var value in System.Enum.GetValues<T>())
            if (Key(value.ToString()) == Key(text))
                return value;
        throw new McpException($"Unknown {what} '{text}'. Valid values: {string.Join(", ", System.Enum.GetNames<T>())}.");
    }

    /// <summary>"#RRGGBB", "#RRGGBBAA" (hex, # optional), or white, black, transparent.</summary>
    public static ColorBgra Color(string? text, ColorBgra fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        switch (Key(text))
        {
            case "white": return ColorBgra.White;
            case "black": return ColorBgra.Black;
            case "transparent": return ColorBgra.Transparent;
        }
        var hex = text.Trim().TrimStart('#');
        if ((hex.Length == 6 || hex.Length == 8) && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            var (r, g, b, a) = hex.Length == 6
                ? ((byte)(v >> 16), (byte)(v >> 8), (byte)v, (byte)255)
                : ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return ColorBgra.FromBgra(b, g, r, a);
        }
        throw new McpException($"Invalid color '{text}'. Use #RRGGBB, #RRGGBBAA, white, black or transparent.");
    }

    public static string Hex(ColorBgra c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}{(c.A == 255 ? "" : c.A.ToString("X2"))}";

    /// <summary>"16:9", "4:3", "1.5"…</summary>
    public static double Ratio(string text)
    {
        var parts = text.Split(':', '/', 'x');
        if (parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var w)
                              && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var h) && w > 0 && h > 0)
            return w / h;
        if (parts.Length == 1 && double.TryParse(text, CultureInfo.InvariantCulture, out var r) && r > 0)
            return r;
        throw new McpException($"Invalid aspect ratio '{text}'. Use a ratio such as 16:9, 4:3 or 1:1.");
    }

    public static Effect Effect(string name)
    {
        var key = Key(name);
        return EffectCatalogInfo.Everything.FirstOrDefault(e => Key(e.Name) == key)
            ?? throw new McpException($"Unknown effect '{name}'. Use list_effects to see the adjustments and effects.");
    }

    /// <summary>
    /// The effect's values: its defaults, replaced by <paramref name="parameters"/> given by name (a number, or a choice
    /// name for list parameters), or the full encoded list <paramref name="raw"/> for effects with a custom dialog.
    /// </summary>
    public static IReadOnlyList<double> EffectValues(Effect effect, IReadOnlyDictionary<string, JsonElement>? parameters,
        IReadOnlyList<double>? raw)
    {
        if (raw is { Count: > 0 })
            return raw;
        var values = effect.Defaults.ToArray();
        if (parameters is null || parameters.Count == 0)
            return values;
        if (values.Length != effect.Parameters.Count)
            throw new McpException($"'{effect.Name}' takes its encoded values in 'values', not named parameters.");
        foreach (var (name, json) in parameters)
        {
            var index = effect.Parameters.ToList().FindIndex(p => Key(p.Name) == Key(name));
            if (index < 0)
                throw new McpException($"'{effect.Name}' has no parameter '{name}'. Parameters: " +
                                       string.Join(", ", effect.Parameters.Select(p => p.Name)) + ".");
            values[index] = Value(effect.Parameters[index], json);
        }
        return values;
    }

    private static double Value(EffectParameter p, JsonElement json)
    {
        double value;
        if (json.ValueKind == JsonValueKind.String && p.Choices is { } choices
                                                   && choices.ToList().FindIndex(c => Key(c) == Key(json.GetString()!)) is var i and >= 0)
            value = i;
        else if (json.ValueKind == JsonValueKind.Number)
            value = json.GetDouble();
        else if (json.ValueKind == JsonValueKind.String && double.TryParse(json.GetString(), CultureInfo.InvariantCulture, out var n))
            value = n;
        else if (json.ValueKind is JsonValueKind.True or JsonValueKind.False)
            value = json.GetBoolean() ? 1 : 0;
        else
            throw new McpException(p.Choices is { } list
                ? $"'{p.Name}' must be one of: {string.Join(", ", list)}."
                : $"'{p.Name}' must be a number between {p.Minimum} and {p.Maximum}.");
        if (value < p.Minimum || value > p.Maximum)
            throw new McpException($"'{p.Name}' must be between {p.Minimum} and {p.Maximum} (got {value}).");
        return value;
    }
}
