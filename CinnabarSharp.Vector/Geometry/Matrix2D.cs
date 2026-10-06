namespace CinnabarSharp.Vector;

/// <summary>
/// Affine transform in SVG's order: x' = A·x + C·y + E, y' = B·x + D·y + F.
/// <c>a * b</c> applies <c>b</c> first, then <c>a</c> (the same as the order of an SVG transform list).
/// </summary>
public readonly record struct Matrix2D(double A, double B, double C, double D, double E, double F)
{
    public static Matrix2D Identity => new(1, 0, 0, 1, 0, 0);

    public static Matrix2D Translate(double tx, double ty) => new(1, 0, 0, 1, tx, ty);

    public static Matrix2D Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Matrix2D Scale(double s) => Scale(s, s);

    /// <summary>Rotation by <paramref name="degrees"/> (clockwise on screen, y pointing down) around the origin.</summary>
    public static Matrix2D Rotate(double degrees)
    {
        var (sin, cos) = SinCosDegrees(degrees);
        return new Matrix2D(cos, sin, -sin, cos, 0, 0);
    }

    public static Matrix2D Rotate(double degrees, double cx, double cy) =>
        Translate(cx, cy) * Rotate(degrees) * Translate(-cx, -cy);

    public static Matrix2D SkewX(double degrees) => new(1, 0, Math.Tan(degrees * Math.PI / 180), 1, 0, 0);

    public static Matrix2D SkewY(double degrees) => new(1, Math.Tan(degrees * Math.PI / 180), 0, 1, 0, 0);

    public bool IsIdentity => this == Identity;

    public double Determinant => A * D - B * C;

    public bool IsInvertible => Math.Abs(Determinant) > 1e-12 && double.IsFinite(Determinant);

    /// <summary>True when the matrix only moves things (no scale, rotation or skew).</summary>
    public bool IsTranslation => A == 1 && B == 0 && C == 0 && D == 1;

    /// <summary>True when axes stay axis-aligned (no rotation or skew).</summary>
    public bool IsAxisAligned => (B == 0 && C == 0) || (A == 0 && D == 0);

    public static Matrix2D operator *(Matrix2D l, Matrix2D r) => new(
        l.A * r.A + l.C * r.B,
        l.B * r.A + l.D * r.B,
        l.A * r.C + l.C * r.D,
        l.B * r.C + l.D * r.D,
        l.A * r.E + l.C * r.F + l.E,
        l.B * r.E + l.D * r.F + l.F);

    public Matrix2D Multiply(Matrix2D right) => this * right;

    /// <summary>The inverse, or null when the matrix cannot be inverted (a zero scale).</summary>
    public Matrix2D? Invert()
    {
        var det = Determinant;
        if (!IsInvertible)
            return null;
        var id = 1 / det;
        return new Matrix2D(D * id, -B * id, -C * id, A * id, (C * F - D * E) * id, (B * E - A * F) * id);
    }

    public VPoint Transform(VPoint p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    /// <summary>Transforms a displacement (the translation is ignored).</summary>
    public VVector TransformVector(VVector v) => new(A * v.X + C * v.Y, B * v.X + D * v.Y);

    /// <summary>Bounding box of the transformed rectangle.</summary>
    public VRect TransformBounds(VRect r)
    {
        var p1 = Transform(new VPoint(r.X, r.Y));
        var p2 = Transform(new VPoint(r.Right, r.Y));
        var p3 = Transform(new VPoint(r.Right, r.Bottom));
        var p4 = Transform(new VPoint(r.X, r.Bottom));
        return VRect.FromLTRB(Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X)),
            Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y)),
            Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X)),
            Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y)));
    }

    /// <summary>Length of the transformed unit X and Y vectors.</summary>
    public (double X, double Y) ScaleFactors => (Math.Sqrt(A * A + B * B), Math.Sqrt(C * C + D * D));

    /// <summary>Geometric mean scale: how much areas grow, as a linear factor.</summary>
    public double MeanScale => Math.Sqrt(Math.Abs(Determinant));

    /// <summary>
    /// Splits the matrix into translation, rotation (degrees), scale and skew (degrees along X) such that
    /// <c>Translate * Rotate * SkewX * Scale</c> gives it back. A negative determinant is carried by a negative X scale.
    /// </summary>
    public (VVector Translation, double Rotation, double ScaleX, double ScaleY, double SkewX) Decompose()
    {
        // The CSS matrix decomposition (Gram-Schmidt on the two columns).
        double a = A, b = B, c = C, d = D;
        var scaleX = Math.Sqrt(a * a + b * b);
        if (scaleX == 0)
            return (new VVector(E, F), 0, 0, Math.Sqrt(c * c + d * d), 0);
        a /= scaleX;
        b /= scaleX;
        var skew = a * c + b * d;
        c -= a * skew;
        d -= b * skew;
        var scaleY = Math.Sqrt(c * c + d * d);
        if (scaleY != 0)
        {
            c /= scaleY;
            d /= scaleY;
            skew /= scaleY;
        }
        if (a * d < b * c)
        {
            a = -a;
            b = -b;
            skew = -skew;
            scaleX = -scaleX;
        }
        return (new VVector(E, F), Math.Atan2(b, a) * 180 / Math.PI, scaleX, scaleY, Math.Atan(skew) * 180 / Math.PI);
    }

    private static (double Sin, double Cos) SinCosDegrees(double degrees)
    {
        // Exact values at the multiples of 90 degrees, so rotate(90) stays an integer matrix.
        var turns = degrees / 90;
        var rounded = Math.Round(turns);
        if (Math.Abs(turns - rounded) < 1e-12)
        {
            return (((long)rounded % 4 + 4) % 4) switch
            {
                0 => (0, 1),
                1 => (1, 0),
                2 => (0, -1),
                _ => (-1, 0),
            };
        }
        var radians = degrees * Math.PI / 180;
        return (Math.Sin(radians), Math.Cos(radians));
    }
}
