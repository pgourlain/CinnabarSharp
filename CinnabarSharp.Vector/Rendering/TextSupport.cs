namespace CinnabarSharp.Vector;

/// <summary>Font choice for one piece of text.</summary>
public sealed record TextStyle(string Family, double Size, int Weight, bool Italic);

/// <summary>
/// Turns text into glyph outlines. Implemented outside the engine (the desktop app uses the platform's fonts); tests use a
/// fake that returns boxes. Without a provider text is drawn as a placeholder box.
/// </summary>
public interface IGlyphOutlineProvider
{
    /// <summary>
    /// The outline of <paramref name="text"/> with its baseline on y = 0 and its start at x = 0, in the units of
    /// <see cref="TextStyle.Size"/>. <paramref name="advance"/> is the width to move the pen by.
    /// </summary>
    VectorPath Outline(string text, TextStyle style, out double advance);
}

/// <summary>One laid-out piece of text: its outline in user space, and the style it is painted with.</summary>
public sealed record TextGlyphRun(VectorPath Outline, ComputedStyle Style, SvgElement Source);

/// <summary>Positions the runs of a text element (x, y, dx, dy, text-anchor) in user space.</summary>
public static class TextLayout
{
    /// <summary>Estimated advance when no provider measures the text: 0.55 em per character.</summary>
    public const double EstimatedEm = 0.55;

    public static VectorPath EstimateBox(string text, double fontSize, double x, double baseline) =>
        VectorPath.FromRect(x, baseline - fontSize * 0.8, Math.Max(text.Length, 1) * fontSize * EstimatedEm, fontSize);

    /// <summary>
    /// Lays out the text element. <paramref name="provider"/> may be null: boxes are used then
    /// (<see cref="TextGlyphRun.Outline"/> is a rectangle per piece of text).
    /// </summary>
    public static IReadOnlyList<TextGlyphRun> Layout(SvgText text, ComputedStyle textStyle, IGlyphOutlineProvider? provider)
    {
        var runs = new List<TextGlyphRun>();
        var chunk = new List<(VectorPath Outline, ComputedStyle Style, SvgElement Source)>();
        var penX = text.XList is { Length: > 0 } xs ? xs[0] : 0;
        var penY = text.YList is { Length: > 0 } ys ? ys[0] : 0;
        var chunkStart = penX;
        var lastWasSpace = true;

        void FlushChunk(TextAnchor anchor)
        {
            if (chunk.Count == 0)
                return;
            var width = penX - chunkStart;
            var shift = anchor switch { TextAnchor.Middle => -width / 2, TextAnchor.End => -width, _ => 0.0 };
            foreach (var (outline, style, source) in chunk)
                runs.Add(new TextGlyphRun(shift == 0 ? outline : outline.Transformed(Matrix2D.Translate(shift, 0)), style, source));
            chunk.Clear();
        }

        void Visit(SvgTextBase element, ComputedStyle style, bool isRoot)
        {
            if (!isRoot)
            {
                // A span with its own x or y starts a new text chunk.
                var spanX = element.XList;
                var spanY = element.YList;
                if (spanX.Length > 0 || spanY.Length > 0)
                {
                    FlushChunk(style.TextAnchor);
                    if (spanX.Length > 0)
                        penX = spanX[0];
                    if (spanY.Length > 0)
                        penY = spanY[0];
                    chunkStart = penX;
                }
                if (element.DxList.Length > 0)
                    penX += element.DxList[0];
                if (element.DyList.Length > 0)
                    penY += element.DyList[0];
            }
            else
            {
                if (element.DxList.Length > 0)
                    penX += element.DxList[0];
                if (element.DyList.Length > 0)
                    penY += element.DyList[0];
            }

            foreach (var child in element.Children)
            {
                switch (child)
                {
                    case SvgTextRun run:
                    {
                        var content = Collapse(run.Text, element.PreserveSpace, ref lastWasSpace);
                        if (content.Length == 0)
                            break;
                        var font = new TextStyle(style.FontFamily, style.FontSize, style.FontWeight, style.Italic);
                        VectorPath outline;
                        double advance;
                        if (provider is not null)
                        {
                            outline = provider.Outline(content, font, out advance).Transformed(Matrix2D.Translate(penX, penY));
                        }
                        else
                        {
                            outline = EstimateBox(content, style.FontSize, penX, penY);
                            advance = content.Length * style.FontSize * EstimatedEm;
                        }
                        chunk.Add((outline, style, element));
                        penX += advance;
                        break;
                    }
                    case SvgTextSpan span:
                        Visit(span, StyleResolver.Compute(span, style), false);
                        break;
                }
            }
        }

        Visit(text, textStyle, true);
        FlushChunk(textStyle.TextAnchor);
        return runs;
    }

    // White space collapsing across runs: a space after a space is dropped, also between a text and its span.
    private static string Collapse(string text, bool preserve, ref bool lastWasSpace)
    {
        if (preserve)
        {
            lastWasSpace = text.EndsWith(' ');
            return text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        }
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            var ch = c is '\n' or '\r' or '\t' ? ' ' : c;
            if (ch == ' ')
            {
                if (!lastWasSpace)
                    sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }
}
