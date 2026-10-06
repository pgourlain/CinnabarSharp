using System.Text;

namespace CinnabarSharp.Vector;

public sealed record PathParseResult(VectorPath Path, bool HadError, int ErrorPosition);

/// <summary>
/// Parses the <c>d</c> attribute of a path. Like browsers, parsing stops at the first bad token and keeps
/// what was read before it. The result has absolute coordinates, and every figure starts with a MoveTo.
/// </summary>
public static class PathDataParser
{
    public static VectorPath Parse(string data) => ParseWithResult(data).Path;

    public static PathParseResult ParseWithResult(string data)
    {
        var path = new VectorPath();
        var s = new SvgScanner(data);
        var current = default(VPoint);
        var start = default(VPoint);
        VPoint? lastCubicControl = null;
        VPoint? lastQuadControl = null;
        var previous = '\0';
        var first = true;

        PathParseResult Fail() => new(path, true, s.Position);

        s.SkipWhitespace();
        while (!s.AtEnd)
        {
            char command;
            if (char.IsAsciiLetter(s.Current))
            {
                command = s.Current;
                if ("MmLlHhVvCcSsQqTtAaZz".IndexOf(command) < 0)
                    return Fail();
                s.TryRead(command);
            }
            else if (previous != '\0' && s.AtNumberStart && previous is not ('Z' or 'z'))
            {
                // Implicit repeat of the previous command (a repeated moveto becomes a lineto).
                command = previous switch { 'M' => 'L', 'm' => 'l', _ => previous };
            }
            else
            {
                return Fail();
            }

            if (first && command is not ('M' or 'm'))
                return Fail();
            first = false;

            var relative = char.IsAsciiLetterLower(command);
            var upper = char.ToUpperInvariant(command);
            var origin = relative ? current : default;

            if (upper == 'Z')
            {
                path.Close();
                current = start;
                lastCubicControl = lastQuadControl = null;
                previous = command;
                s.SkipWhitespaceAndComma();
                continue;
            }

            // After a closepath, a drawing command starts a new figure at the figure's start.
            if (previous is 'Z' or 'z' && upper != 'M')
                path.MoveTo(start);

            double x, y;
            switch (upper)
            {
                case 'M':
                    if (!ReadPoint(ref s, out x, out y))
                        return Fail();
                    current = new VPoint(origin.X + x, origin.Y + y);
                    start = current;
                    path.MoveTo(current);
                    lastCubicControl = lastQuadControl = null;
                    break;
                case 'L':
                    if (!ReadPoint(ref s, out x, out y))
                        return Fail();
                    current = new VPoint(origin.X + x, origin.Y + y);
                    path.LineTo(current);
                    lastCubicControl = lastQuadControl = null;
                    break;
                case 'H':
                    if (!ReadNumber(ref s, out x))
                        return Fail();
                    current = new VPoint(relative ? current.X + x : x, current.Y);
                    path.LineTo(current);
                    lastCubicControl = lastQuadControl = null;
                    break;
                case 'V':
                    if (!ReadNumber(ref s, out y))
                        return Fail();
                    current = new VPoint(current.X, relative ? current.Y + y : y);
                    path.LineTo(current);
                    lastCubicControl = lastQuadControl = null;
                    break;
                case 'C':
                {
                    if (!ReadPoint(ref s, out var x1, out var y1) || !ReadPoint(ref s, out var x2, out var y2)
                        || !ReadPoint(ref s, out x, out y))
                        return Fail();
                    var c1 = new VPoint(origin.X + x1, origin.Y + y1);
                    var c2 = new VPoint(origin.X + x2, origin.Y + y2);
                    current = new VPoint(origin.X + x, origin.Y + y);
                    path.CubicTo(c1, c2, current);
                    lastCubicControl = c2;
                    lastQuadControl = null;
                    break;
                }
                case 'S':
                {
                    if (!ReadPoint(ref s, out var x2, out var y2) || !ReadPoint(ref s, out x, out y))
                        return Fail();
                    var c1 = lastCubicControl is { } lc ? new VPoint(2 * current.X - lc.X, 2 * current.Y - lc.Y) : current;
                    var c2 = new VPoint(origin.X + x2, origin.Y + y2);
                    current = new VPoint(origin.X + x, origin.Y + y);
                    path.CubicTo(c1, c2, current);
                    lastCubicControl = c2;
                    lastQuadControl = null;
                    break;
                }
                case 'Q':
                {
                    if (!ReadPoint(ref s, out var x1, out var y1) || !ReadPoint(ref s, out x, out y))
                        return Fail();
                    var c = new VPoint(origin.X + x1, origin.Y + y1);
                    current = new VPoint(origin.X + x, origin.Y + y);
                    path.QuadTo(c, current);
                    lastQuadControl = c;
                    lastCubicControl = null;
                    break;
                }
                case 'T':
                {
                    if (!ReadPoint(ref s, out x, out y))
                        return Fail();
                    var c = lastQuadControl is { } lq ? new VPoint(2 * current.X - lq.X, 2 * current.Y - lq.Y) : current;
                    current = new VPoint(origin.X + x, origin.Y + y);
                    path.QuadTo(c, current);
                    lastQuadControl = c;
                    lastCubicControl = null;
                    break;
                }
                case 'A':
                {
                    if (!ReadNumber(ref s, out var rx) || !ReadNumber(ref s, out var ry) || !ReadNumber(ref s, out var rotation)
                        || !ReadFlag(ref s, out var large) || !ReadFlag(ref s, out var sweep) || !ReadPoint(ref s, out x, out y))
                        return Fail();
                    current = new VPoint(origin.X + x, origin.Y + y);
                    path.ArcTo(rx, ry, rotation, large, sweep, current);
                    lastCubicControl = lastQuadControl = null;
                    break;
                }
            }
            previous = command;
            s.SkipWhitespaceAndComma();
        }
        return new PathParseResult(path, false, s.Position);
    }

    private static bool ReadNumber(ref SvgScanner s, out double value)
    {
        s.SkipWhitespace();
        if (!s.TryReadNumber(out value))
            return false;
        s.SkipWhitespaceAndComma();
        return true;
    }

    private static bool ReadPoint(ref SvgScanner s, out double x, out double y)
    {
        y = 0;
        if (!ReadNumber(ref s, out x))
            return false;
        return ReadNumber(ref s, out y);
    }

    private static bool ReadFlag(ref SvgScanner s, out bool flag)
    {
        s.SkipWhitespace();
        if (!s.TryReadFlag(out flag))
            return false;
        s.SkipWhitespaceAndComma();
        return true;
    }
}

/// <summary>Writes a path as compact absolute path data.</summary>
public static class PathDataWriter
{
    /// <summary>
    /// "M10 10L60 10H100V50C…Z": absolute commands, H and V for axis-aligned lines, an omitted letter for repeated
    /// L, C and Q, invariant culture, at most <paramref name="decimals"/> decimals (more only for values too small to keep otherwise).
    /// </summary>
    public static string Write(VectorPath path, int decimals = 3)
    {
        var sb = new StringBuilder();
        var current = default(VPoint);
        var start = default(VPoint);
        var last = '\0';
        var needsSeparator = false;

        void Letter(char c)
        {
            if (c == last && c is 'L' or 'C' or 'Q')
                return;
            sb.Append(c);
            last = c;
            needsSeparator = false;
        }

        void Num(double v)
        {
            var text = NumberFormat.Format(v, decimals);
            if (needsSeparator && text[0] != '-')
                sb.Append(' ');
            sb.Append(text);
            needsSeparator = true;
        }

        void Pt(VPoint p)
        {
            Num(p.X);
            Num(p.Y);
        }

        foreach (var segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case SegmentKind.MoveTo:
                    Letter('M');
                    last = 'M';
                    Pt(segment.End);
                    current = start = segment.End;
                    // A second pair after M would be read as a lineto: keep the letter for what follows.
                    last = '\0';
                    break;
                case SegmentKind.LineTo:
                    if (segment.End.Y == current.Y && segment.End.X != current.X)
                    {
                        Letter('H');
                        Num(segment.End.X);
                    }
                    else if (segment.End.X == current.X && segment.End.Y != current.Y)
                    {
                        Letter('V');
                        Num(segment.End.Y);
                    }
                    else
                    {
                        Letter('L');
                        Pt(segment.End);
                    }
                    current = segment.End;
                    break;
                case SegmentKind.CubicTo:
                    Letter('C');
                    Pt(segment.C1);
                    Pt(segment.C2);
                    Pt(segment.End);
                    current = segment.End;
                    break;
                case SegmentKind.QuadTo:
                    Letter('Q');
                    Pt(segment.C1);
                    Pt(segment.End);
                    current = segment.End;
                    break;
                case SegmentKind.ArcTo:
                    Letter('A');
                    last = '\0';
                    Num(segment.Rx);
                    Num(segment.Ry);
                    Num(segment.XAxisRotation);
                    sb.Append(' ').Append(segment.LargeArc ? '1' : '0').Append(' ').Append(segment.Sweep ? '1' : '0');
                    needsSeparator = true;
                    Pt(segment.End);
                    current = segment.End;
                    break;
                case SegmentKind.Close:
                    sb.Append('Z');
                    last = 'Z';
                    needsSeparator = false;
                    current = start;
                    break;
            }
        }
        return sb.ToString();
    }
}
