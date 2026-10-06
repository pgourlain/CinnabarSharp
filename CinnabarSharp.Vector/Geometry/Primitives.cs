namespace CinnabarSharp.Vector;

/// <summary>A point in user space (or device space, depending on the caller).</summary>
public readonly record struct VPoint(double X, double Y)
{
    public static VPoint Zero => default;

    public static VPoint operator +(VPoint a, VVector v) => new(a.X + v.X, a.Y + v.Y);
    public static VPoint operator -(VPoint a, VVector v) => new(a.X - v.X, a.Y - v.Y);
    public static VVector operator -(VPoint a, VPoint b) => new(a.X - b.X, a.Y - b.Y);

    public double DistanceTo(VPoint other) => Math.Sqrt(DistanceSquaredTo(other));

    public double DistanceSquaredTo(VPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    public VPoint Lerp(VPoint other, double t) => new(X + (other.X - X) * t, Y + (other.Y - Y) * t);

    public VVector ToVector() => new(X, Y);
}

/// <summary>A displacement between two points.</summary>
public readonly record struct VVector(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);

    public static VVector operator +(VVector a, VVector b) => new(a.X + b.X, a.Y + b.Y);
    public static VVector operator -(VVector a, VVector b) => new(a.X - b.X, a.Y - b.Y);
    public static VVector operator *(VVector a, double s) => new(a.X * s, a.Y * s);
    public static VVector operator -(VVector a) => new(-a.X, -a.Y);

    public double Dot(VVector other) => X * other.X + Y * other.Y;

    public double Cross(VVector other) => X * other.Y - Y * other.X;

    /// <summary>The same direction with length 1 (zero stays zero).</summary>
    public VVector Normalized()
    {
        var length = Length;
        return length > 0 ? new VVector(X / length, Y / length) : this;
    }
}

/// <summary>An axis-aligned rectangle with double coordinates.</summary>
public readonly record struct VRect(double X, double Y, double Width, double Height)
{
    public static VRect Empty => default;

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public VPoint Center => new(X + Width / 2, Y + Height / 2);
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static VRect FromLTRB(double left, double top, double right, double bottom) =>
        new(left, top, right - left, bottom - top);

    public static VRect FromPoints(VPoint a, VPoint b) =>
        FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public bool Contains(VPoint p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public bool IntersectsWith(VRect other) =>
        X <= other.Right && other.X <= Right && Y <= other.Bottom && other.Y <= Bottom;

    public VRect Union(VRect other) => FromLTRB(Math.Min(X, other.X), Math.Min(Y, other.Y),
        Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    public VRect Intersect(VRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right < left || bottom < top ? Empty : FromLTRB(left, top, right, bottom);
    }

    public VRect Inflate(double dx, double dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public VRect Offset(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>Smallest integer rectangle that contains this one.</summary>
    public VRectI ToOuterInteger()
    {
        var left = (int)Math.Floor(X);
        var top = (int)Math.Floor(Y);
        var right = (int)Math.Ceiling(Right);
        var bottom = (int)Math.Ceiling(Bottom);
        return new VRectI(left, top, right - left, bottom - top);
    }
}

/// <summary>An integer pixel rectangle.</summary>
public readonly record struct VRectI(int X, int Y, int Width, int Height)
{
    public static VRectI Empty => default;

    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public VRectI Intersect(VRectI other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top ? Empty : new VRectI(left, top, right - left, bottom - top);
    }

    public VRectI Union(VRectI other)
    {
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        return new VRectI(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }

    public VRect ToRect() => new(X, Y, Width, Height);
}

/// <summary>A color with straight (non-premultiplied) alpha, stored like Core's BGRA pixels.</summary>
public readonly record struct VColor(byte B, byte G, byte R, byte A)
{
    public static VColor Black => new(0, 0, 0, 255);
    public static VColor White => new(255, 255, 255, 255);
    public static VColor Transparent => default;

    public static VColor FromRgb(byte r, byte g, byte b) => new(b, g, r, 255);

    public static VColor FromRgba(byte r, byte g, byte b, byte a) => new(b, g, r, a);

    public VColor WithAlpha(byte alpha) => this with { A = alpha };

    /// <summary>Multiplies the alpha by <paramref name="opacity"/> (0 to 1).</summary>
    public VColor WithOpacity(double opacity) =>
        this with { A = (byte)Math.Clamp((int)Math.Round(A * Math.Clamp(opacity, 0, 1)), 0, 255) };

    /// <summary>"#rrggbb", or "#rrggbbaa" when not opaque.</summary>
    public string ToHex() => A == 255 ? $"#{R:x2}{G:x2}{B:x2}" : $"#{R:x2}{G:x2}{B:x2}{A:x2}";
}
