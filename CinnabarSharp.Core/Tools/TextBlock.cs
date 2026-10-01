using System.Globalization;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Tools;

/// <summary>
/// Layout of a <see cref="TextEngine"/>'s lines with one style, shared by the tools that edit text (Text, Speech
/// Bubble): rows (lines, word-wrapped when a wrap width is given), alignment, caret and selection geometry, hit
/// testing and rasterizing to coverage. Positions are relative to <see cref="Origin"/>, the top-left of the box.
/// </summary>
internal sealed class TextBlock
{
    /// <summary>One displayed row: <paramref name="Text"/> is the part of engine line <paramref name="Line"/> that
    /// starts at offset <paramref name="Start"/> (trailing spaces kept, so offsets stay contiguous).</summary>
    public sealed record Row(int Line, int Start, string Text, double Width);

    private readonly ITextRasterizer _rasterizer;
    private readonly double? _wrapWidth;

    public TextBlock(IReadOnlyList<string> lines, TextStyle style, TextAlignment alignment, ITextRasterizer rasterizer,
        PointD origin, double? wrapWidth = null)
    {
        _rasterizer = rasterizer;
        _wrapWidth = wrapWidth;
        Style = style;
        Alignment = alignment;
        Origin = origin;
        LineHeight = rasterizer.LineHeight(style);
        var rows = new List<Row>();
        for (var i = 0; i < lines.Count; i++)
            rows.AddRange(wrapWidth is { } w ? Wrap(i, lines[i], w) : [new Row(i, 0, lines[i], Measure(lines[i]))]);
        Rows = rows;
        MaxWidth = rows.Select(r => r.Width).DefaultIfEmpty(0).Max();
    }

    public TextStyle Style { get; }
    public TextAlignment Alignment { get; }
    public PointD Origin { get; set; }
    public double LineHeight { get; }
    public IReadOnlyList<Row> Rows { get; }

    /// <summary>Width of the widest row.</summary>
    public double MaxWidth { get; }

    /// <summary>Width rows are aligned in: the wrap width, or the widest row.</summary>
    public double BoxWidth => _wrapWidth ?? MaxWidth;

    public double Height => LineHeight * Rows.Count;

    public RectangleD Bounds => new(Origin.X, Origin.Y, Math.Max(BoxWidth, 1), Height);

    private double Measure(string text) => _rasterizer.MeasureWidth(text, Style);

    /// <summary>Greedy word wrap; a word wider than the row is broken between characters.</summary>
    private IEnumerable<Row> Wrap(int line, string text, double width)
    {
        if (text.Length == 0)
        {
            yield return new Row(line, 0, "", 0);
            yield break;
        }
        var start = 0;
        while (start < text.Length)
        {
            // Longest prefix (ending after a space or at the end) whose visible part fits.
            var end = start;
            while (end < text.Length)
            {
                var next = end;
                while (next < text.Length && text[next] != ' ')
                    next++;
                while (next < text.Length && text[next] == ' ')
                    next++;
                if (end > start && Measure(text[start..next].TrimEnd()) > width)
                    break;
                end = next;
            }
            if (Measure(text[start..end].TrimEnd()) > width)
            {
                // A single word too wide: keep as many characters as fit (at least one).
                var cut = start + 1;
                foreach (var offset in StringInfo.ParseCombiningCharacters(text[start..end]).Skip(1).Select(o => start + o))
                {
                    if (Measure(text[start..offset]) > width)
                        break;
                    cut = offset;
                }
                end = cut;
            }
            var part = text[start..end];
            yield return new Row(line, start, part, Measure(part.TrimEnd()));
            start = end;
        }
    }

    public double RowX(int row) => Origin.X + Alignment switch
    {
        TextAlignment.Center => (BoxWidth - Rows[row].Width) / 2,
        TextAlignment.Right => BoxWidth - Rows[row].Width,
        _ => 0,
    };

    /// <summary>The row showing position <paramref name="p"/>: at a wrap point, the start of the next row.</summary>
    private int RowOf(TextPosition p)
    {
        var result = 0;
        for (var r = 0; r < Rows.Count; r++)
        {
            if (Rows[r].Line < p.Line)
                result = r;
            else if (Rows[r].Line == p.Line && Rows[r].Start <= p.Offset)
                result = r;
        }
        return result;
    }

    public PointD CaretPoint(TextPosition p)
    {
        var r = RowOf(p);
        var row = Rows[r];
        var prefix = row.Text[..Math.Clamp(p.Offset - row.Start, 0, row.Text.Length)];
        return new PointD(RowX(r) + Measure(prefix), Origin.Y + r * LineHeight);
    }

    /// <summary>The caret position closest to a point.</summary>
    public TextPosition PositionAt(PointD p)
    {
        var r = Math.Clamp((int)Math.Floor((p.Y - Origin.Y) / LineHeight), 0, Rows.Count - 1);
        var row = Rows[r];
        var x = p.X - RowX(r);
        // A wrapped row (not the last of its line) ends before its trailing space, which belongs to the next row.
        var lastOfLine = r + 1 >= Rows.Count || Rows[r + 1].Line != row.Line;
        var limit = lastOfLine ? row.Text.Length : Math.Max(0, row.Text.TrimEnd().Length);
        var best = 0;
        var bestDistance = double.MaxValue;
        foreach (var offset in StringInfo.ParseCombiningCharacters(row.Text).Append(row.Text.Length).Where(o => o <= limit))
        {
            var d = Math.Abs(Measure(row.Text[..offset]) - x);
            if (d < bestDistance)
                (best, bestDistance) = (offset, d);
        }
        return new TextPosition(row.Line, row.Start + best);
    }

    /// <summary>Highlight rectangles of the text between two positions (in any order).</summary>
    public List<RectangleD> SelectionRects(TextPosition a, TextPosition b)
    {
        var start = TextPosition.Min(a, b);
        var end = TextPosition.Max(a, b);
        var rects = new List<RectangleD>();
        for (var r = 0; r < Rows.Count; r++)
        {
            var row = Rows[r];
            if (end.Line < row.Line || start.Line > row.Line)
                continue;
            var length = row.Text.Length;
            var lastOfLine = r + 1 >= Rows.Count || Rows[r + 1].Line != row.Line;
            // Wrapped rows: skip those the selection starts after (a wrap point belongs to the next row) or ends before.
            if (start.Line == row.Line && !lastOfLine && start.Offset >= row.Start + length)
                continue;
            if (end.Line == row.Line && end.Offset < row.Start)
                continue;
            var from = start.Line == row.Line ? Math.Clamp(start.Offset - row.Start, 0, length) : 0;
            var to = end.Line == row.Line ? Math.Clamp(end.Offset - row.Start, 0, length) : length;
            var x0 = RowX(r) + Measure(row.Text[..from]);
            var x1 = RowX(r) + Measure(row.Text[..to]);
            rects.Add(new RectangleD(x0, Origin.Y + r * LineHeight, Math.Max(2, x1 - x0), LineHeight));
        }
        return rects;
    }

    /// <summary>Coverage of the text (clipped to a <paramref name="width"/> × <paramref name="height"/> image),
    /// rows combined with "max" like brush dabs. Empty region when nothing is visible.</summary>
    public (RectangleI Region, byte[] Coverage) Rasterize(int width, int height)
    {
        var rasters = new List<(TextRaster Raster, int X, int Y)>();
        var region = RectangleI.Zero;
        for (var r = 0; r < Rows.Count; r++)
        {
            if (Rows[r].Text.Length == 0)
                continue;
            var raster = _rasterizer.RenderLine(Rows[r].Text, Style);
            var x = (int)Math.Round(RowX(r)) - raster.OriginX;
            var y = (int)Math.Round(Origin.Y + r * LineHeight) - raster.OriginY;
            rasters.Add((raster, x, y));
            region = CoverageMask.Union(region, Clip(new RectangleI(x, y, raster.Width, raster.Height), width, height));
        }
        if (region.IsEmpty)
            return (region, []);

        var coverage = new byte[region.Width * region.Height];
        foreach (var (raster, lx, ly) in rasters)
        {
            for (var ry = 0; ry < raster.Height; ry++)
            {
                var y = ly + ry - region.Y;
                if (y < 0 || y >= region.Height)
                    continue;
                for (var rx = 0; rx < raster.Width; rx++)
                {
                    var x = lx + rx - region.X;
                    if (x < 0 || x >= region.Width)
                        continue;
                    var c = raster.Coverage[ry * raster.Width + rx];
                    if (!Style.Antialias)
                        c = c >= 128 ? (byte)255 : (byte)0;
                    ref var cell = ref coverage[y * region.Width + x];
                    if (c > cell)
                        cell = c;
                }
            }
        }
        return (region, coverage);
    }

    public static RectangleI Clip(RectangleI r, int w, int h)
    {
        int x0 = Math.Clamp(r.X, 0, w), y0 = Math.Clamp(r.Y, 0, h);
        int x1 = Math.Clamp(r.X + r.Width, 0, w), y1 = Math.Clamp(r.Y + r.Height, 0, h);
        return x1 > x0 && y1 > y0 ? new RectangleI(x0, y0, x1 - x0, y1 - y0) : RectangleI.Zero;
    }

    /// <summary>Result of <see cref="HandleKey"/>.</summary>
    public enum KeyResult
    {
        NotHandled,

        /// <summary>Caret or selection moved; the text is unchanged.</summary>
        Moved,

        /// <summary>The text changed and must be redrawn.</summary>
        Edited,

        Escape,
    }

    /// <summary>Keyboard editing common to the text tools.</summary>
    public static KeyResult HandleKey(TextEngine engine, ToolKey key, ToolModifiers modifiers)
    {
        var shift = modifiers.HasFlag(ToolModifiers.Shift);
        // Word-wise moves: Alt on macOS, Ctrl elsewhere; accept both.
        var word = (modifiers & (ToolModifiers.Command | ToolModifiers.Alt)) != 0;
        switch (key)
        {
            case ToolKey.Escape:
                return KeyResult.Escape;
            case ToolKey.Enter:
                engine.PerformEnter();
                return KeyResult.Edited;
            case ToolKey.Backspace:
                engine.PerformBackspace();
                return KeyResult.Edited;
            case ToolKey.Delete:
                engine.PerformDelete();
                return KeyResult.Edited;
            case ToolKey.Left:
                engine.PerformLeft(word, shift);
                return KeyResult.Moved;
            case ToolKey.Right:
                engine.PerformRight(word, shift);
                return KeyResult.Moved;
            case ToolKey.Up:
                engine.PerformUp(shift);
                return KeyResult.Moved;
            case ToolKey.Down:
                engine.PerformDown(shift);
                return KeyResult.Moved;
            case ToolKey.Home:
                engine.PerformHome(word, shift);
                return KeyResult.Moved;
            case ToolKey.End:
                engine.PerformEnd(word, shift);
                return KeyResult.Moved;
            default:
                return KeyResult.NotHandled;
        }
    }
}
