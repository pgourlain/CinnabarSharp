using System.Globalization;

namespace CinnabarSharp.Vector;

/// <summary>Invariant-culture number text for SVG attributes.</summary>
public static class NumberFormat
{
    /// <summary>
    /// Shortest text with at most <paramref name="decimals"/> decimals ("12", "0.5", "-3.142"). A value too small
    /// for that precision keeps significant digits instead of collapsing to 0.
    /// </summary>
    public static string Format(double value, int decimals = 3)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return "0";
        var rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            if (value == 0 || Math.Abs(value) < 1e-12)
                return "0";
            return value.ToString("0.######", CultureInfo.InvariantCulture) is { } small && small != "0" && small != "-0"
                ? small
                : value.ToString("G3", CultureInfo.InvariantCulture);
        }
        var text = rounded.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }

    /// <summary>Exact round-trip text, for values the user typed.</summary>
    public static string FormatExact(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? "0" : value.ToString("R", CultureInfo.InvariantCulture);

    public static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}

/// <summary>Reads the numbers, flags and separators of SVG micro-syntaxes (path data, transforms, point lists).</summary>
internal struct SvgScanner
{
    private readonly string _text;
    private int _position;

    public SvgScanner(string text)
    {
        _text = text;
        _position = 0;
    }

    public readonly int Position => _position;
    public readonly bool AtEnd => _position >= _text.Length;
    public readonly char Current => _text[_position];

    public void SkipWhitespace()
    {
        while (_position < _text.Length && IsWhitespace(_text[_position]))
            _position++;
    }

    public void SkipWhitespaceAndComma()
    {
        SkipWhitespace();
        if (_position < _text.Length && _text[_position] == ',')
        {
            _position++;
            SkipWhitespace();
        }
    }

    public static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    /// <summary>True when the next characters can start a number (digit, sign or dot).</summary>
    public readonly bool AtNumberStart => !AtEnd && (char.IsAsciiDigit(Current) || Current is '+' or '-' or '.');

    public bool TryReadNumber(out double value)
    {
        value = 0;
        var start = _position;
        var i = _position;
        if (i < _text.Length && _text[i] is '+' or '-')
            i++;
        var digits = 0;
        while (i < _text.Length && char.IsAsciiDigit(_text[i])) { i++; digits++; }
        if (i < _text.Length && _text[i] == '.')
        {
            i++;
            while (i < _text.Length && char.IsAsciiDigit(_text[i])) { i++; digits++; }
        }
        if (digits == 0)
            return false;
        if (i < _text.Length && _text[i] is 'e' or 'E')
        {
            // An exponent only counts when digits follow (otherwise "1em" would swallow the e).
            var j = i + 1;
            if (j < _text.Length && _text[j] is '+' or '-')
                j++;
            if (j < _text.Length && char.IsAsciiDigit(_text[j]))
            {
                while (j < _text.Length && char.IsAsciiDigit(_text[j])) j++;
                i = j;
            }
        }
        if (!double.TryParse(_text.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
            return false;
        _position = i;
        return true;
    }

    /// <summary>A single '0' or '1' (arc flags may be written without separators).</summary>
    public bool TryReadFlag(out bool flag)
    {
        flag = false;
        if (AtEnd || Current is not ('0' or '1'))
            return false;
        flag = Current == '1';
        _position++;
        return true;
    }

    public bool TryRead(char expected)
    {
        if (AtEnd || Current != expected)
            return false;
        _position++;
        return true;
    }

    public string ReadWhile(Func<char, bool> predicate)
    {
        var start = _position;
        while (_position < _text.Length && predicate(_text[_position]))
            _position++;
        return _text.Substring(start, _position - start);
    }
}
