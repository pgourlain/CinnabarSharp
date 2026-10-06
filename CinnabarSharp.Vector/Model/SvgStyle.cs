using System.Text;

namespace CinnabarSharp.Vector;

/// <summary>Where a specified property value comes from.</summary>
public enum StyleOrigin
{
    None,
    /// <summary>A presentation attribute (<c>fill="red"</c>).</summary>
    Attribute,
    /// <summary>The inline <c>style</c> attribute.</summary>
    Inline,
    /// <summary>A rule of a <c>&lt;style&gt;</c> element.</summary>
    Stylesheet,
}

public readonly record struct StyleDeclaration(string Name, string Value, StyleOrigin Origin);

/// <summary>
/// The properties specified on one element: presentation attributes, rules of the stylesheet and the inline style.
/// Reads give the winner (attribute &lt; stylesheet &lt; inline, <c>!important</c> in a rule beats inline). Writes go back to the
/// place the property came from, so an edit never duplicates a property.
/// </summary>
public sealed class SvgStyle
{
    /// <summary>Properties that may be written as presentation attributes (and inline).</summary>
    public static readonly IReadOnlySet<string> Properties = new HashSet<string>(StringComparer.Ordinal)
    {
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin",
        "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "opacity", "display", "visibility", "color",
        "font-family", "font-size", "font-weight", "font-style", "text-anchor", "stop-color", "stop-opacity", "clip-path",
        "clip-rule", "mask", "filter",
    };

    private readonly SvgElement _element;
    private string? _parsedFrom;
    private List<(string Name, string Value)>? _inline;

    internal SvgStyle(SvgElement element) => _element = element;

    internal void Invalidate() => _parsedFrom = null;

    private List<(string Name, string Value)> Inline
    {
        get
        {
            var text = _element.GetAttribute("style");
            if (_inline is null || text != _parsedFrom)
            {
                _inline = ParseDeclarations(text).Select(d => (d.Name, d.Value)).ToList();
                _parsedFrom = text;
            }
            return _inline;
        }
    }

    /// <summary>Every property specified on the element, the winner of each.</summary>
    public IEnumerable<StyleDeclaration> Specified
    {
        get
        {
            var seen = new HashSet<string>();
            var all = new List<StyleDeclaration>();
            foreach (var name in Names())
                if (seen.Add(name) && TryGet(name, out var value, out var origin))
                    all.Add(new StyleDeclaration(name, value, origin));
            return all;
        }
    }

    private IEnumerable<string> Names()
    {
        foreach (var (name, _) in Inline)
            yield return name;
        foreach (var d in StylesheetFor(_element))
            yield return d.Name;
        foreach (var a in _element.Attributes)
            if (a.Name.Namespace == XNamespaceNone && Properties.Contains(a.Name.LocalName))
                yield return a.Name.LocalName;
    }

    private static readonly System.Xml.Linq.XNamespace XNamespaceNone = System.Xml.Linq.XNamespace.None;

    private static IEnumerable<CssDeclaration> StylesheetFor(SvgElement element) =>
        element.DocumentRoot?.Stylesheet.Match(element) ?? [];

    public bool TryGet(string name, out string value, out StyleOrigin origin)
    {
        value = "";
        origin = StyleOrigin.None;
        // Lowest priority first, later ones overwrite.
        if (_element.GetAttribute(name) is { } attribute && Properties.Contains(name))
        {
            value = attribute.Trim();
            origin = StyleOrigin.Attribute;
        }
        var important = false;
        foreach (var d in StylesheetFor(_element))
        {
            if (d.Name != name)
                continue;
            value = d.Value;
            origin = StyleOrigin.Stylesheet;
            important = d.Important;
        }
        if (!important)
        {
            foreach (var (n, v) in Inline)
            {
                if (n != name)
                    continue;
                value = v;
                origin = StyleOrigin.Inline;
            }
        }
        return origin != StyleOrigin.None;
    }

    public string? Get(string name) => TryGet(name, out var value, out _) ? value : null;

    public StyleOrigin OriginOf(string name) => TryGet(name, out _, out var origin) ? origin : StyleOrigin.None;

    /// <summary>
    /// Sets (or, with null, removes) a property in the place it came from. A new property goes to the inline style when
    /// the element has one or a stylesheet rule sets it (an attribute would lose against the rule), else to an attribute.
    /// </summary>
    public void Set(string name, string? value)
    {
        var origin = OriginOf(name);
        var inline = Inline;
        var inInline = inline.FindIndex(d => d.Name == name);

        if (value is null)
        {
            if (inInline >= 0)
            {
                inline.RemoveAt(inInline);
                WriteInline();
            }
            _element.SetAttribute(name, null);
            return;
        }

        var target = origin switch
        {
            StyleOrigin.Inline => StyleOrigin.Inline,
            StyleOrigin.Attribute => StyleOrigin.Attribute,
            StyleOrigin.Stylesheet => StyleOrigin.Inline,
            _ => _element.HasAttribute("style") ? StyleOrigin.Inline : StyleOrigin.Attribute,
        };
        if (target == StyleOrigin.Inline)
        {
            if (inInline >= 0)
                inline[inInline] = (name, value);
            else
                inline.Add((name, value));
            // A same-named attribute would only be shadowed by the inline value: drop it so there is one place.
            if (_element.HasAttribute(name) && Properties.Contains(name))
                _element.SetAttribute(name, null);
            WriteInline();
        }
        else
        {
            _element.SetAttribute(name, value);
        }
    }

    private void WriteInline()
    {
        var text = string.Join(';', Inline.Select(d => $"{d.Name}:{d.Value}"));
        _element.SetAttribute("style", text.Length == 0 ? null : text);
        // SetAttribute invalidated the cache; the list is already current.
        _parsedFrom = _element.GetAttribute("style");
    }

    // ---- Typed convenience ----

    public SvgPaint? Fill
    {
        get => SvgPaint.TryParse(Get("fill"));
        set => Set("fill", value?.ToText());
    }

    public SvgPaint? Stroke
    {
        get => SvgPaint.TryParse(Get("stroke"));
        set => Set("stroke", value?.ToText());
    }

    public double? GetNumber(string name) => NumberFormat.TryParse(Get(name) ?? "", out var v) ? v : null;

    public void SetNumber(string name, double? value) =>
        Set(name, value is null ? null : NumberFormat.Format(value.Value, 6));

    public double? FillOpacity { get => GetNumber("fill-opacity"); set => SetNumber("fill-opacity", value); }
    public double? StrokeOpacity { get => GetNumber("stroke-opacity"); set => SetNumber("stroke-opacity", value); }
    public double? Opacity { get => GetNumber("opacity"); set => SetNumber("opacity", value); }
    public double? StrokeWidth { get => GetNumber("stroke-width"); set => SetNumber("stroke-width", value); }

    // ---- Declaration syntax ----

    /// <summary>Splits "a:b; c:d !important" into declarations (names lower-cased); a malformed one is skipped.</summary>
    public static List<CssDeclaration> ParseDeclarations(string? text)
    {
        var result = new List<CssDeclaration>();
        if (string.IsNullOrWhiteSpace(text))
            return result;
        foreach (var part in SplitTopLevel(text, ';'))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = part[..colon].Trim().ToLowerInvariant();
            var value = part[(colon + 1)..].Trim();
            var important = false;
            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
            {
                important = true;
                value = value[..^"!important".Length].Trim();
            }
            if (name.Length > 0 && value.Length > 0)
                result.Add(new CssDeclaration(name, value, important, StyleOrigin.Inline, 0, 0));
        }
        return result;
    }

    internal static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var depth = 0;
        char? quote = null;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
            {
                if (c == quote)
                    quote = null;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (c == separator && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        if (start < text.Length)
            yield return text[start..];
    }
}

/// <summary>A declaration with its place in the cascade (for sorting rules).</summary>
public readonly record struct CssDeclaration(string Name, string Value, bool Important, StyleOrigin Origin, int Specificity, int Order);

/// <summary>The rules of the document's <c>&lt;style&gt;</c> elements. Selectors: type, .class, #id, *, compounds of them, descendant chains, commas.</summary>
public sealed class Stylesheet
{
    public static Stylesheet Empty { get; } = new([]);

    private readonly List<CssRule> _rules;

    private Stylesheet(List<CssRule> rules) => _rules = rules;

    public int RuleCount => _rules.Count;

    /// <summary>Parses CSS text; rules with selectors the engine does not support are dropped and reported in <paramref name="warnings"/>.</summary>
    public static Stylesheet Parse(string css, ICollection<string> warnings, int firstOrder = 0)
    {
        var rules = new List<CssRule>();
        css = StripComments(css);
        var order = firstOrder;
        var i = 0;
        while (i < css.Length)
        {
            while (i < css.Length && char.IsWhiteSpace(css[i]))
                i++;
            if (i >= css.Length)
                break;
            var open = css.IndexOf('{', i);
            var semicolon = css.IndexOf(';', i);
            if (css[i] == '@')
            {
                // @import ends at ';', @media/@font-face have a block: not supported.
                if (open < 0 || (semicolon >= 0 && semicolon < open))
                {
                    warnings.Add($"CSS at-rule ignored: {css[i..(semicolon < 0 ? css.Length : semicolon)].Trim()}");
                    i = semicolon < 0 ? css.Length : semicolon + 1;
                }
                else
                {
                    var atName = css[i..open].Trim();
                    warnings.Add($"CSS at-rule ignored: {atName}");
                    i = SkipBlock(css, open);
                }
                continue;
            }
            if (open < 0)
                break;
            var close = css.IndexOf('}', open);
            if (close < 0)
                close = css.Length;
            var selectorText = css[i..open].Trim();
            var body = css[(open + 1)..close];
            i = close + 1;

            var declarations = SvgStyle.ParseDeclarations(body);
            foreach (var selector in SvgStyle.SplitTopLevel(selectorText, ','))
            {
                var parsed = CssSelector.TryParse(selector.Trim());
                if (parsed is null)
                {
                    warnings.Add($"CSS selector not supported, rule ignored: {selector.Trim()}");
                    continue;
                }
                rules.Add(new CssRule(parsed, declarations, order++));
            }
        }
        return new Stylesheet(rules);
    }

    private static int SkipBlock(string css, int open)
    {
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{')
                depth++;
            else if (css[i] == '}' && --depth == 0)
                return i + 1;
        }
        return css.Length;
    }

    private static string StripComments(string css)
    {
        var sb = new StringBuilder(css.Length);
        for (var i = 0; i < css.Length; i++)
        {
            if (css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? css.Length : end + 1;
            }
            else
            {
                sb.Append(css[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>The declarations of the rules that match, lowest priority first (specificity, then source order).</summary>
    public IEnumerable<CssDeclaration> Match(SvgElement element)
    {
        if (_rules.Count == 0)
            return [];
        List<(int Specificity, int Order, CssDeclaration Declaration)>? matched = null;
        foreach (var rule in _rules)
        {
            if (!rule.Selector.Matches(element))
                continue;
            matched ??= [];
            foreach (var d in rule.Declarations)
                matched.Add((rule.Selector.Specificity, rule.Order, d with { Origin = StyleOrigin.Stylesheet, Specificity = rule.Selector.Specificity, Order = rule.Order }));
        }
        if (matched is null)
            return [];
        return matched.OrderBy(m => m.Declaration.Important ? 1 : 0).ThenBy(m => m.Specificity).ThenBy(m => m.Order)
            .Select(m => m.Declaration).ToList();
    }

    private sealed record CssRule(CssSelector Selector, IReadOnlyList<CssDeclaration> Declarations, int Order);
}

internal sealed class CssSelector
{
    private sealed record Compound(string? Type, string? Id, IReadOnlyList<string> Classes);

    private readonly IReadOnlyList<Compound> _chain;

    private CssSelector(IReadOnlyList<Compound> chain)
    {
        _chain = chain;
        Specificity = chain.Sum(c => (c.Id is null ? 0 : 10000) + c.Classes.Count * 100 + (c.Type is null ? 0 : 1));
    }

    public int Specificity { get; }

    public static CssSelector? TryParse(string text)
    {
        if (text.Length == 0)
            return null;
        if (text.IndexOfAny(['>', '+', '~', '[', ':', '(', '"', '\'']) >= 0)
            return null;
        var chain = new List<Compound>();
        foreach (var part in text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            string? type = null, id = null;
            var classes = new List<string>();
            var i = 0;
            while (i < part.Length)
            {
                var kind = part[i];
                var start = kind is '.' or '#' ? i + 1 : i;
                var end = start;
                while (end < part.Length && part[end] is not ('.' or '#'))
                    end++;
                var name = part[start..end];
                if (name.Length == 0 && kind != '*')
                    return null;
                if (kind == '.')
                    classes.Add(name);
                else if (kind == '#')
                    id = name;
                else if (name != "*")
                    type = name;
                i = end;
            }
            chain.Add(new Compound(type, id, classes));
        }
        return chain.Count == 0 ? null : new CssSelector(chain);
    }

    public bool Matches(SvgElement element) => MatchesFrom(element, _chain.Count - 1);

    private bool MatchesFrom(SvgElement element, int index)
    {
        if (!MatchesCompound(element, _chain[index]))
            return false;
        if (index == 0)
            return true;
        for (var p = element.Parent; p is not null; p = p.Parent)
            if (MatchesFrom(p, index - 1))
                return true;
        return false;
    }

    private static bool MatchesCompound(SvgElement element, Compound c)
    {
        if (c.Type is not null && c.Type != element.ElementName)
            return false;
        if (c.Id is not null && c.Id != element.Id)
            return false;
        if (c.Classes.Count == 0)
            return true;
        var classes = element.Classes;
        return c.Classes.All(cls => classes.Contains(cls));
    }
}
