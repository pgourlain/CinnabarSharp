namespace CinnabarSharp.Vector.Tests;

public class MatrixTests
{
    private static void Near(VPoint expected, VPoint actual, double tolerance = 1e-9)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
    }

    [Fact]
    public void Multiply_applies_the_right_operand_first()
    {
        var m = Matrix2D.Translate(10, 0) * Matrix2D.Scale(2);
        Near(new VPoint(12, 2), m.Transform(new VPoint(1, 1)));
    }

    [Fact]
    public void Rotate_90_is_exact_and_clockwise_on_screen()
    {
        var m = Matrix2D.Rotate(90);
        Assert.Equal(new Matrix2D(0, 1, -1, 0, 0, 0), m);
        Near(new VPoint(0, 1), m.Transform(new VPoint(1, 0)));
    }

    [Fact]
    public void Rotate_around_a_center_keeps_the_center()
    {
        var m = Matrix2D.Rotate(37, 50, 20);
        Near(new VPoint(50, 20), m.Transform(new VPoint(50, 20)));
    }

    [Fact]
    public void Invert_gives_back_the_identity()
    {
        var m = Matrix2D.Translate(5, -3) * Matrix2D.Rotate(30) * Matrix2D.Scale(2, 3) * Matrix2D.SkewX(10);
        var inverse = m.Invert();
        Assert.NotNull(inverse);
        var p = new VPoint(7, 11);
        Near(p, inverse.Value.Transform(m.Transform(p)));
        Assert.Null(Matrix2D.Scale(0, 1).Invert());
    }

    [Fact]
    public void TransformVector_ignores_translation()
    {
        Assert.Equal(new VVector(2, 4), (Matrix2D.Translate(100, 100) * Matrix2D.Scale(2)).TransformVector(new VVector(1, 2)));
    }

    [Fact]
    public void TransformBounds_of_a_rotated_rectangle()
    {
        var bounds = Matrix2D.Rotate(90).TransformBounds(new VRect(0, 0, 10, 4));
        Assert.Equal(-4, bounds.X, 9);
        Assert.Equal(0, bounds.Y, 9);
        Assert.Equal(4, bounds.Width, 9);
        Assert.Equal(10, bounds.Height, 9);
    }

    [Theory]
    [InlineData(10, 20, 30, 2, 3, 15)]
    [InlineData(-5, 7, -40, 1, 1, 0)]
    [InlineData(0, 0, 0, 2, 2, 0)]
    public void Decompose_recomposes_the_matrix(double tx, double ty, double rotation, double sx, double sy, double skew)
    {
        var m = Matrix2D.Translate(tx, ty) * Matrix2D.Rotate(rotation) * Matrix2D.SkewX(skew) * Matrix2D.Scale(sx, sy);
        var d = m.Decompose();
        var back = Matrix2D.Translate(d.Translation.X, d.Translation.Y) * Matrix2D.Rotate(d.Rotation)
            * Matrix2D.SkewX(d.SkewX) * Matrix2D.Scale(d.ScaleX, d.ScaleY);
        Assert.InRange(back.A, m.A - 1e-9, m.A + 1e-9);
        Assert.InRange(back.B, m.B - 1e-9, m.B + 1e-9);
        Assert.InRange(back.C, m.C - 1e-9, m.C + 1e-9);
        Assert.InRange(back.D, m.D - 1e-9, m.D + 1e-9);
        Assert.InRange(back.E, m.E - 1e-9, m.E + 1e-9);
        Assert.InRange(back.F, m.F - 1e-9, m.F + 1e-9);
    }

    [Fact]
    public void Decompose_of_a_mirror_keeps_the_determinant_sign()
    {
        var d = Matrix2D.Scale(-1, 1).Decompose();
        var back = Matrix2D.Rotate(d.Rotation) * Matrix2D.Scale(d.ScaleX, d.ScaleY);
        Assert.InRange(back.A, -1 - 1e-9, -1 + 1e-9);
        Assert.InRange(back.D, 1 - 1e-9, 1 + 1e-9);
    }
}

public class VectorPathTests
{
    [Fact]
    public void Bounds_include_curve_extrema()
    {
        var path = new VectorPath().MoveTo(0, 0).CubicTo(new VPoint(0, 10), new VPoint(10, 10), new VPoint(10, 0));
        var bounds = path.Bounds;
        Assert.Equal(0, bounds.X, 9);
        Assert.Equal(0, bounds.Y, 9);
        Assert.Equal(10, bounds.Width, 9);
        Assert.Equal(7.5, bounds.Height, 9); // the curve peaks at 3/4 of the control height
    }

    [Fact]
    public void Bounds_of_a_circle_are_its_box()
    {
        var bounds = VectorPath.FromEllipse(50, 40, 20, 10).Bounds;
        Assert.Equal(30, bounds.X, 6);
        Assert.Equal(30, bounds.Y, 6);
        Assert.Equal(40, bounds.Width, 6);
        Assert.Equal(20, bounds.Height, 6);
    }

    [Fact]
    public void Bounds_of_a_quadratic()
    {
        var path = new VectorPath().MoveTo(0, 0).QuadTo(new VPoint(5, 10), new VPoint(10, 0));
        Assert.Equal(5, path.Bounds.Height, 9);
    }

    [Fact]
    public void Figures_split_at_move_and_close()
    {
        var path = new VectorPath().MoveTo(0, 0).LineTo(10, 0).LineTo(10, 10).Close().LineTo(20, 20).MoveTo(30, 30).LineTo(40, 30);
        var figures = path.Figures.ToList();
        Assert.Equal(3, figures.Count);
        Assert.True(figures[0].IsClosed);
        Assert.Equal(2, figures[0].Segments.Count);
        // Drawing after a close starts a new figure at the start of the closed one.
        Assert.Equal(new VPoint(0, 0), figures[1].Start);
        Assert.False(figures[1].IsClosed);
        Assert.Equal(new VPoint(30, 30), figures[2].Start);
    }

    [Fact]
    public void Transformed_converts_arcs_and_moves_points()
    {
        var path = VectorPath.FromEllipse(0, 0, 10, 10).Transformed(Matrix2D.Translate(100, 50) * Matrix2D.Scale(2, 1));
        Assert.DoesNotContain(path.Segments, s => s.Kind == SegmentKind.ArcTo);
        var bounds = path.Bounds;
        Assert.Equal(80, bounds.X, 6);
        Assert.Equal(40, bounds.Y, 6);
        Assert.Equal(40, bounds.Width, 6);
        Assert.Equal(20, bounds.Height, 6);
    }

    [Fact]
    public void Rounded_rect_has_arcs_and_the_right_bounds()
    {
        var path = VectorPath.FromRect(10, 20, 100, 50, 8, 8);
        Assert.Contains(path.Segments, s => s.Kind == SegmentKind.ArcTo);
        Assert.Equal(new VRect(10, 20, 100, 50), path.Bounds with
        {
            X = Math.Round(path.Bounds.X, 6), Y = Math.Round(path.Bounds.Y, 6),
            Width = Math.Round(path.Bounds.Width, 6), Height = Math.Round(path.Bounds.Height, 6),
        });
    }

    [Fact]
    public void Radius_is_clamped_to_half_the_size()
    {
        var path = VectorPath.FromRect(0, 0, 10, 10, 100, 100);
        Assert.Equal(5, path.Segments.First(s => s.Kind == SegmentKind.ArcTo).Rx);
    }
}

public class ArcConverterTests
{
    [Fact]
    public void Quarter_circle_is_one_cubic_with_the_standard_constants()
    {
        var cubics = ArcConverter.ToCubics(new VPoint(0, 0), new VPoint(10, 10), 10, 10, 0, false, true);
        var cubic = Assert.Single(cubics);
        Assert.Equal(5.5228, cubic.C1.X, 3);
        Assert.Equal(0, cubic.C1.Y, 6);
        Assert.Equal(10, cubic.C2.X, 6);
        Assert.Equal(4.4772, cubic.C2.Y, 3);
        Assert.Equal(new VPoint(10, 10), cubic.End);
    }

    [Fact]
    public void Half_circle_makes_two_cubics_and_reaches_the_end()
    {
        var cubics = ArcConverter.ToCubics(new VPoint(0, 0), new VPoint(20, 0), 10, 10, 0, false, true);
        Assert.Equal(2, cubics.Count);
        Assert.Equal(new VPoint(20, 0), cubics[^1].End);
        // Passes through the top of the circle.
        Assert.Equal(10, cubics[0].End.X, 6);
        Assert.Equal(-10, cubics[0].End.Y, 6);
    }

    [Fact]
    public void Large_arc_flag_chooses_the_longer_way()
    {
        var small = ArcConverter.ToCubics(new VPoint(0, 0), new VPoint(10, 10), 10, 10, 0, false, true);
        var large = ArcConverter.ToCubics(new VPoint(0, 0), new VPoint(10, 10), 10, 10, 0, true, true);
        Assert.Single(small);
        Assert.Equal(3, large.Count);
    }

    [Fact]
    public void Radii_too_small_are_scaled_up()
    {
        var cubics = ArcConverter.ToCubics(new VPoint(0, 0), new VPoint(20, 0), 1, 1, 0, false, true);
        Assert.Equal(2, cubics.Count);
        Assert.Equal(10, cubics[0].End.X, 6);
    }

    [Fact]
    public void Zero_radius_or_same_point_gives_nothing()
    {
        Assert.Empty(ArcConverter.ToCubics(new VPoint(0, 0), new VPoint(5, 5), 0, 5, 0, false, true));
        Assert.Empty(ArcConverter.ToCubics(new VPoint(3, 3), new VPoint(3, 3), 5, 5, 0, false, true));
    }

    [Fact]
    public void Rotated_ellipse_stays_on_its_end_points()
    {
        var cubics = ArcConverter.ToCubics(new VPoint(0, 0), new VPoint(30, 10), 30, 10, 45, true, false);
        Assert.Equal(new VPoint(0, 0), cubics[0].Start);
        Assert.Equal(new VPoint(30, 10), cubics[^1].End);
    }
}
