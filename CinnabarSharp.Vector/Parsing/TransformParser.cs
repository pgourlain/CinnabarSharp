using System.Text;

namespace CinnabarSharp.Vector;

/// <summary>Parses and writes the <c>transform</c> attribute (a list such as "translate(10 20) rotate(45)").</summary>
public static class TransformParser
{
    /// <summary>The matrix of the list, or null when the text is not a valid transform list (browsers then ignore it).</summary>
    public static Matrix2D? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Matrix2D.Identity;
        var s = new SvgScanner(text);
        var result = Matrix2D.Identity;
        s.SkipWhitespace();
        while (!s.AtEnd)
        {
            var name = s.ReadWhile(char.IsAsciiLetter);
            s.SkipWhitespace();
            if (name.Length == 0 || !s.TryRead('('))
                return null;
            var args = new List<double>();
            s.SkipWhitespace();
            while (!s.AtEnd && s.Current != ')')
            {
                if (!s.TryReadNumber(out var value))
                    return null;
                args.Add(value);
                s.SkipWhitespaceAndComma();
            }
            if (!s.TryRead(')'))
                return null;
            Matrix2D? next = (name, args.Count) switch
            {
                ("matrix", 6) => new Matrix2D(args[0], args[1], args[2], args[3], args[4], args[5]),
                ("translate", 1) => Matrix2D.Translate(args[0], 0),
                ("translate", 2) => Matrix2D.Translate(args[0], args[1]),
                ("scale", 1) => Matrix2D.Scale(args[0], args[0]),
                ("scale", 2) => Matrix2D.Scale(args[0], args[1]),
                ("rotate", 1) => Matrix2D.Rotate(args[0]),
                ("rotate", 3) => Matrix2D.Rotate(args[0], args[1], args[2]),
                ("skewX", 1) => Matrix2D.SkewX(args[0]),
                ("skewY", 1) => Matrix2D.SkewY(args[0]),
                _ => null,
            };
            if (next is not { } m)
                return null;
            result *= m;
            s.SkipWhitespaceAndComma();
        }
        return result;
    }

    /// <summary>Same as <see cref="TryParse"/>, with the identity for invalid text.</summary>
    public static Matrix2D Parse(string? text) => TryParse(text) ?? Matrix2D.Identity;

    /// <summary>Shortest form: translate, scale or rotate when the matrix is exactly one of them, else matrix(…).</summary>
    public static string Write(Matrix2D m, int decimals = 6)
    {
        string N(double v) => NumberFormat.Format(v, decimals);
        if (m.IsIdentity)
            return "";
        if (m.IsTranslation)
            return m.F == 0 ? $"translate({N(m.E)})" : $"translate({N(m.E)} {N(m.F)})";
        if (m.B == 0 && m.C == 0 && m.E == 0 && m.F == 0)
            return m.A == m.D ? $"scale({N(m.A)})" : $"scale({N(m.A)} {N(m.D)})";
        if (m.E == 0 && m.F == 0 && Math.Abs(m.A - m.D) < 1e-12 && Math.Abs(m.B + m.C) < 1e-12
            && Math.Abs(m.A * m.A + m.B * m.B - 1) < 1e-9)
        {
            var angle = Math.Atan2(m.B, m.A) * 180 / Math.PI;
            return $"rotate({N(angle)})";
        }
        return new StringBuilder("matrix(")
            .Append(N(m.A)).Append(' ').Append(N(m.B)).Append(' ').Append(N(m.C)).Append(' ')
            .Append(N(m.D)).Append(' ').Append(N(m.E)).Append(' ').Append(N(m.F)).Append(')').ToString();
    }
}
