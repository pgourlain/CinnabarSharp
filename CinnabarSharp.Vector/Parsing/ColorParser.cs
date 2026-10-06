using System.Globalization;

namespace CinnabarSharp.Vector;

/// <summary>Parses CSS/SVG colors: names, #rgb, #rgba, #rrggbb, #rrggbbaa, rgb(), rgba(), hsl(), hsla().</summary>
public static class ColorParser
{
    // The 147 CSS/SVG color keywords (plus transparent), as name=rrggbb.
    private const string NamedColorTable =
        "aliceblue=f0f8ff antiquewhite=faebd7 aqua=00ffff aquamarine=7fffd4 azure=f0ffff beige=f5f5dc bisque=ffe4c4 black=000000 " +
        "blanchedalmond=ffebcd blue=0000ff blueviolet=8a2be2 brown=a52a2a burlywood=deb887 cadetblue=5f9ea0 chartreuse=7fff00 " +
        "chocolate=d2691e coral=ff7f50 cornflowerblue=6495ed cornsilk=fff8dc crimson=dc143c cyan=00ffff darkblue=00008b " +
        "darkcyan=008b8b darkgoldenrod=b8860b darkgray=a9a9a9 darkgreen=006400 darkgrey=a9a9a9 darkkhaki=bdb76b darkmagenta=8b008b " +
        "darkolivegreen=556b2f darkorange=ff8c00 darkorchid=9932cc darkred=8b0000 darksalmon=e9967a darkseagreen=8fbc8f " +
        "darkslateblue=483d8b darkslategray=2f4f4f darkslategrey=2f4f4f darkturquoise=00ced1 darkviolet=9400d3 deeppink=ff1493 " +
        "deepskyblue=00bfff dimgray=696969 dimgrey=696969 dodgerblue=1e90ff firebrick=b22222 floralwhite=fffaf0 forestgreen=228b22 " +
        "fuchsia=ff00ff gainsboro=dcdcdc ghostwhite=f8f8ff gold=ffd700 goldenrod=daa520 gray=808080 grey=808080 green=008000 " +
        "greenyellow=adff2f honeydew=f0fff0 hotpink=ff69b4 indianred=cd5c5c indigo=4b0082 ivory=fffff0 khaki=f0e68c lavender=e6e6fa " +
        "lavenderblush=fff0f5 lawngreen=7cfc00 lemonchiffon=fffacd lightblue=add8e6 lightcoral=f08080 lightcyan=e0ffff " +
        "lightgoldenrodyellow=fafad2 lightgray=d3d3d3 lightgreen=90ee90 lightgrey=d3d3d3 lightpink=ffb6c1 lightsalmon=ffa07a " +
        "lightseagreen=20b2aa lightskyblue=87cefa lightslategray=778899 lightslategrey=778899 lightsteelblue=b0c4de lightyellow=ffffe0 " +
        "lime=00ff00 limegreen=32cd32 linen=faf0e6 magenta=ff00ff maroon=800000 mediumaquamarine=66cdaa mediumblue=0000cd " +
        "mediumorchid=ba55d3 mediumpurple=9370db mediumseagreen=3cb371 mediumslateblue=7b68ee mediumspringgreen=00fa9a " +
        "mediumturquoise=48d1cc mediumvioletred=c71585 midnightblue=191970 mintcream=f5fffa mistyrose=ffe4e1 moccasin=ffe4b5 " +
        "navajowhite=ffdead navy=000080 oldlace=fdf5e6 olive=808000 olivedrab=6b8e23 orange=ffa500 orangered=ff4500 orchid=da70d6 " +
        "palegoldenrod=eee8aa palegreen=98fb98 paleturquoise=afeeee palevioletred=db7093 papayawhip=ffefd5 peachpuff=ffdab9 peru=cd853f " +
        "pink=ffc0cb plum=dda0dd powderblue=b0e0e6 purple=800080 rebeccapurple=663399 red=ff0000 rosybrown=bc8f8f royalblue=4169e1 " +
        "saddlebrown=8b4513 salmon=fa8072 sandybrown=f4a460 seagreen=2e8b57 seashell=fff5ee sienna=a0522d silver=c0c0c0 skyblue=87ceeb " +
        "slateblue=6a5acd slategray=708090 slategrey=708090 snow=fffafa springgreen=00ff7f steelblue=4682b4 tan=d2b48c teal=008080 " +
        "thistle=d8bfd8 tomato=ff6347 turquoise=40e0d0 violet=ee82ee wheat=f5deb3 white=ffffff whitesmoke=f5f5f5 yellow=ffff00 " +
        "yellowgreen=9acd32";

    private static readonly Dictionary<string, VColor> Named = BuildNamed();

    /// <summary>All color keywords, name to color (for tests and color pickers).</summary>
    public static IReadOnlyDictionary<string, VColor> NamedColors => Named;

    private static Dictionary<string, VColor> BuildNamed()
    {
        var map = new Dictionary<string, VColor>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in NamedColorTable.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('=');
            var rgb = int.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            map[parts[0]] = VColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        map["transparent"] = VColor.Transparent;
        return map;
    }

    /// <summary>The name of an exact keyword match (for display), or null.</summary>
    public static string? NameOf(VColor color) =>
        color.A == 255 ? Named.FirstOrDefault(p => p.Value == color && p.Key != "transparent").Key : null;

    public static bool TryParse(string? text, out VColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();
        if (text[0] == '#')
            return TryParseHex(text.AsSpan(1), out color);
        if (Named.TryGetValue(text, out color))
            return true;
        var open = text.IndexOf('(');
        if (open <= 0 || !text.EndsWith(')'))
            return false;
        var function = text[..open].Trim().ToLowerInvariant();
        var args = SplitArguments(text.AsSpan(open + 1, text.Length - open - 2));
        if (args is null)
            return false;
        switch (function)
        {
            case "rgb" or "rgba":
                return TryParseRgb(args, out color);
            case "hsl" or "hsla":
                return TryParseHsl(args, out color);
            default:
                return false;
        }
    }

    public static VColor Parse(string text, VColor fallback) => TryParse(text, out var c) ? c : fallback;

    private static bool TryParseHex(ReadOnlySpan<char> hex, out VColor color)
    {
        color = default;
        foreach (var c in hex)
            if (!char.IsAsciiHexDigit(c))
                return false;
        static int H(char c) => Convert.ToInt32(c.ToString(), 16);
        switch (hex.Length)
        {
            case 3:
                color = VColor.FromRgb((byte)(H(hex[0]) * 17), (byte)(H(hex[1]) * 17), (byte)(H(hex[2]) * 17));
                return true;
            case 4:
                color = VColor.FromRgba((byte)(H(hex[0]) * 17), (byte)(H(hex[1]) * 17), (byte)(H(hex[2]) * 17), (byte)(H(hex[3]) * 17));
                return true;
            case 6:
                color = VColor.FromRgb((byte)(H(hex[0]) * 16 + H(hex[1])), (byte)(H(hex[2]) * 16 + H(hex[3])), (byte)(H(hex[4]) * 16 + H(hex[5])));
                return true;
            case 8:
                color = VColor.FromRgba((byte)(H(hex[0]) * 16 + H(hex[1])), (byte)(H(hex[2]) * 16 + H(hex[3])),
                    (byte)(H(hex[4]) * 16 + H(hex[5])), (byte)(H(hex[6]) * 16 + H(hex[7])));
                return true;
            default:
                return false;
        }
    }

    // Arguments separated by commas and/or spaces, with an optional "/" before the alpha.
    private static List<string>? SplitArguments(ReadOnlySpan<char> text)
    {
        var parts = new List<string>();
        foreach (var token in text.ToString().Split([',', ' ', '\t', '\n', '\r', '/'], StringSplitOptions.RemoveEmptyEntries))
            parts.Add(token);
        return parts.Count is 3 or 4 ? parts : null;
    }

    private static bool TryParseComponent(string text, double percentScale, out double value)
    {
        value = 0;
        if (text.EndsWith('%'))
        {
            if (!NumberFormat.TryParse(text[..^1], out var percent))
                return false;
            value = percent / 100 * percentScale;
            return true;
        }
        return NumberFormat.TryParse(text, out value);
    }

    private static bool TryParseAlpha(List<string> args, out byte alpha)
    {
        alpha = 255;
        if (args.Count < 4)
            return true;
        if (!TryParseComponent(args[3], 1, out var a))
            return false;
        alpha = (byte)Math.Clamp((int)Math.Round(a * 255), 0, 255);
        return true;
    }

    private static bool TryParseRgb(List<string> args, out VColor color)
    {
        color = default;
        if (!TryParseComponent(args[0], 255, out var r) || !TryParseComponent(args[1], 255, out var g)
            || !TryParseComponent(args[2], 255, out var b) || !TryParseAlpha(args, out var a))
            return false;
        color = VColor.FromRgba(ToByte(r), ToByte(g), ToByte(b), a);
        return true;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private static bool TryParseHsl(List<string> args, out VColor color)
    {
        color = default;
        var hueText = args[0].EndsWith("deg", StringComparison.OrdinalIgnoreCase) ? args[0][..^3] : args[0];
        if (!NumberFormat.TryParse(hueText, out var h) || !TryParseComponent(args[1], 1, out var s)
            || !TryParseComponent(args[2], 1, out var l) || !TryParseAlpha(args, out var a))
            return false;
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        color = VColor.FromRgba(ToByte(HueToRgb(p, q, h + 1.0 / 3) * 255), ToByte(HueToRgb(p, q, h) * 255),
            ToByte(HueToRgb(p, q, h - 1.0 / 3) * 255), a);
        return true;
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}

public enum PaintKind
{
    None,
    Color,
    CurrentColor,
    /// <summary><c>url(#id)</c>, with an optional fallback paint.</summary>
    Url,
}

/// <summary>A parsed <c>fill</c> or <c>stroke</c> value.</summary>
public sealed record SvgPaint(PaintKind Kind, VColor Color = default, string? Id = null, SvgPaint? Fallback = null)
{
    public static SvgPaint None { get; } = new(PaintKind.None);
    public static SvgPaint CurrentColor { get; } = new(PaintKind.CurrentColor);

    public static SvgPaint FromColor(VColor color) => new(PaintKind.Color, color);

    public static SvgPaint FromUrl(string id, SvgPaint? fallback = null) => new(PaintKind.Url, Id: id, Fallback: fallback);

    /// <summary>Parses "none", "currentColor", a color, or "url(#id) [fallback]"; null when it is none of these.</summary>
    public static SvgPaint? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
            return None;
        if (text.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
            return CurrentColor;
        if (text.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var close = text.IndexOf(')');
            if (close < 0)
                return null;
            var reference = text[4..close].Trim().Trim('\'', '"');
            if (!reference.StartsWith('#') || reference.Length < 2)
                return null;
            var rest = text[(close + 1)..].Trim();
            return new SvgPaint(PaintKind.Url, Id: reference[1..], Fallback: rest.Length == 0 ? null : TryParse(rest));
        }
        return ColorParser.TryParse(text, out var color) ? FromColor(color) : null;
    }

    /// <summary>The text to write back to an attribute.</summary>
    public string ToText() => Kind switch
    {
        PaintKind.None => "none",
        PaintKind.CurrentColor => "currentColor",
        PaintKind.Color => ColorParser.NameOf(Color) is { } name && name.Length <= 7 ? name : Color.ToHex(),
        PaintKind.Url => Fallback is null ? $"url(#{Id})" : $"url(#{Id}) {Fallback.ToText()}",
        _ => "none",
    };
}

public enum LengthUnit
{
    None,
    Px,
    Pt,
    Pc,
    Mm,
    Cm,
    In,
    Em,
    Ex,
    Percent,
}

/// <summary>A number with an optional unit (px, pt, pc, mm, cm, in, em, ex, %).</summary>
public readonly record struct SvgLength(double Value, LengthUnit Unit)
{
    public static SvgLength Px(double value) => new(value, LengthUnit.None);

    public static bool TryParse(string? text, out SvgLength length)
    {
        length = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var s = new SvgScanner(text.Trim());
        if (!s.TryReadNumber(out var value))
            return false;
        var unitText = s.ReadWhile(c => char.IsAsciiLetter(c) || c == '%').ToLowerInvariant();
        if (!s.AtEnd)
            return false;
        LengthUnit unit;
        switch (unitText)
        {
            case "": unit = LengthUnit.None; break;
            case "px": unit = LengthUnit.Px; break;
            case "pt": unit = LengthUnit.Pt; break;
            case "pc": unit = LengthUnit.Pc; break;
            case "mm": unit = LengthUnit.Mm; break;
            case "cm": unit = LengthUnit.Cm; break;
            case "in": unit = LengthUnit.In; break;
            case "em": unit = LengthUnit.Em; break;
            case "ex": unit = LengthUnit.Ex; break;
            case "%": unit = LengthUnit.Percent; break;
            default: return false;
        }
        length = new SvgLength(value, unit);
        return true;
    }

    /// <summary>
    /// The length in user units. <paramref name="percentBase"/> is what 100 % means (the viewport's width, height or
    /// diagonal), <paramref name="fontSize"/> what 1em means (16 by default, like browsers).
    /// </summary>
    public double ToUser(double percentBase = 0, double fontSize = 16) => Unit switch
    {
        LengthUnit.None or LengthUnit.Px => Value,
        LengthUnit.Pt => Value * 96 / 72,
        LengthUnit.Pc => Value * 16,
        LengthUnit.Mm => Value * 96 / 25.4,
        LengthUnit.Cm => Value * 96 / 2.54,
        LengthUnit.In => Value * 96,
        LengthUnit.Em => Value * fontSize,
        LengthUnit.Ex => Value * fontSize / 2,
        LengthUnit.Percent => Value / 100 * percentBase,
        _ => Value,
    };

    public bool IsAbsolute => Unit != LengthUnit.Percent && Unit != LengthUnit.Em && Unit != LengthUnit.Ex;

    public string ToText() => NumberFormat.Format(Value, 6) + Unit switch
    {
        LengthUnit.None => "",
        LengthUnit.Px => "px",
        LengthUnit.Pt => "pt",
        LengthUnit.Pc => "pc",
        LengthUnit.Mm => "mm",
        LengthUnit.Cm => "cm",
        LengthUnit.In => "in",
        LengthUnit.Em => "em",
        LengthUnit.Ex => "ex",
        LengthUnit.Percent => "%",
        _ => "",
    };
}
