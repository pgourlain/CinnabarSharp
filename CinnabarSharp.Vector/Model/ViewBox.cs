namespace CinnabarSharp.Vector;

public enum AspectAlign
{
    None,
    XMinYMin, XMidYMin, XMaxYMin,
    XMinYMid, XMidYMid, XMaxYMid,
    XMinYMax, XMidYMax, XMaxYMax,
}

/// <summary>The <c>preserveAspectRatio</c> attribute.</summary>
public readonly record struct PreserveAspectRatio(AspectAlign Align, bool Slice)
{
    public static PreserveAspectRatio Default => new(AspectAlign.XMidYMid, false);

    public static PreserveAspectRatio Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Default;
        var parts = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && parts[0] == "defer")
            parts.RemoveAt(0);
        if (parts.Count == 0 || !Enum.TryParse<AspectAlign>(parts[0], ignoreCase: true, out var align))
            return Default;
        return new PreserveAspectRatio(align, parts.Count > 1 && parts[1] == "slice");
    }

    public string ToText()
    {
        var align = Align == AspectAlign.None ? "none" : char.ToLowerInvariant(Align.ToString()[0]) + Align.ToString()[1..];
        return Slice ? align + " slice" : align;
    }

    /// <summary>
    /// The matrix that maps <paramref name="viewBox"/> into the rectangle (0, 0, <paramref name="width"/>, <paramref name="height"/>),
    /// the way the aspect ratio rules say (meet: whole box visible, slice: the box covers the area).
    /// </summary>
    public Matrix2D ViewBoxTransform(VRect viewBox, double width, double height)
    {
        if (viewBox.Width <= 0 || viewBox.Height <= 0)
            return Matrix2D.Identity;
        var sx = width / viewBox.Width;
        var sy = height / viewBox.Height;
        if (Align == AspectAlign.None)
            return Matrix2D.Scale(sx, sy) * Matrix2D.Translate(-viewBox.X, -viewBox.Y);
        var s = Slice ? Math.Max(sx, sy) : Math.Min(sx, sy);
        var extraX = width - viewBox.Width * s;
        var extraY = height - viewBox.Height * s;
        var (fx, fy) = Align switch
        {
            AspectAlign.XMinYMin => (0.0, 0.0),
            AspectAlign.XMidYMin => (0.5, 0.0),
            AspectAlign.XMaxYMin => (1.0, 0.0),
            AspectAlign.XMinYMid => (0.0, 0.5),
            AspectAlign.XMidYMid => (0.5, 0.5),
            AspectAlign.XMaxYMid => (1.0, 0.5),
            AspectAlign.XMinYMax => (0.0, 1.0),
            AspectAlign.XMidYMax => (0.5, 1.0),
            _ => (1.0, 1.0),
        };
        return Matrix2D.Translate(extraX * fx, extraY * fy) * Matrix2D.Scale(s) * Matrix2D.Translate(-viewBox.X, -viewBox.Y);
    }
}

public static class ViewBoxParser
{
    /// <summary>"minx miny width height"; null when malformed or when width or height is not positive.</summary>
    public static VRect? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var s = new SvgScanner(text);
        var values = new double[4];
        s.SkipWhitespace();
        for (var i = 0; i < 4; i++)
        {
            if (!s.TryReadNumber(out values[i]))
                return null;
            s.SkipWhitespaceAndComma();
        }
        if (!s.AtEnd || values[2] <= 0 || values[3] <= 0)
            return null;
        return new VRect(values[0], values[1], values[2], values[3]);
    }

    public static string ToText(VRect box) =>
        $"{NumberFormat.Format(box.X, 6)} {NumberFormat.Format(box.Y, 6)} {NumberFormat.Format(box.Width, 6)} {NumberFormat.Format(box.Height, 6)}";
}
