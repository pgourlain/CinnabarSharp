namespace CinnabarSharp.Vector.Tests;

public class PathDataTests
{
    private static string Roundtrip(string d) => PathDataWriter.Write(PathDataParser.Parse(d));

    [Theory]
    [InlineData("M10 20L30 40", "M10 20L30 40")]
    [InlineData("m10 20 l30 40", "M10 20L40 60")]
    [InlineData("M0 0 H10 V10 H0 Z", "M0 0H10V10H0Z")]
    [InlineData("M0 0 h10 v10 h-10 z", "M0 0H10V10H0Z")]
    [InlineData("M10 10 20 20 30 10", "M10 10L20 20 30 10")]
    [InlineData("m10 10 10 10 10 -10", "M10 10L20 20 30 10")]
    [InlineData("M0 0 C 10 0 10 10 0 10", "M0 0C10 0 10 10 0 10")]
    [InlineData("M0,0C10,0,10,10,0,10", "M0 0C10 0 10 10 0 10")]
    [InlineData("M0 0 c10 0 10 10 0 10", "M0 0C10 0 10 10 0 10")]
    [InlineData("M0 0 Q5 10 10 0", "M0 0Q5 10 10 0")]
    [InlineData("M0 0 q5 10 10 0", "M0 0Q5 10 10 0")]
    public void Commands_become_absolute_compact_data(string input, string expected) => Assert.Equal(expected, Roundtrip(input));

    [Fact]
    public void Smooth_cubic_reflects_the_previous_control_point()
    {
        var path = PathDataParser.Parse("M0 0 C0 10 10 10 10 0 S20 -10 20 0");
        var second = path.Segments[2];
        Assert.Equal(SegmentKind.CubicTo, second.Kind);
        Assert.Equal(new VPoint(10, -10), second.C1); // reflection of (10,10) around (10,0)
        Assert.Equal(new VPoint(20, -10), second.C2);
    }

    [Fact]
    public void Smooth_cubic_without_a_previous_curve_uses_the_current_point()
    {
        var path = PathDataParser.Parse("M5 5 S10 10 20 5");
        Assert.Equal(new VPoint(5, 5), path.Segments[1].C1);
    }

    [Fact]
    public void Smooth_quad_reflects_and_chains()
    {
        var path = PathDataParser.Parse("M0 0 Q5 10 10 0 T20 0 T30 0");
        Assert.Equal(new VPoint(15, -10), path.Segments[2].C1);
        Assert.Equal(new VPoint(25, 10), path.Segments[3].C1);
    }

    [Fact]
    public void Smooth_command_after_another_kind_does_not_reflect()
    {
        var path = PathDataParser.Parse("M0 0 Q5 10 10 0 S20 5 30 0");
        Assert.Equal(new VPoint(10, 0), path.Segments[2].C1);
    }

    [Fact]
    public void Arc_flags_can_be_written_without_separators()
    {
        var path = PathDataParser.Parse("M0 0a5 5 0 1010 0");
        var arc = path.Segments[1];
        Assert.Equal(SegmentKind.ArcTo, arc.Kind);
        Assert.True(arc.LargeArc);
        Assert.False(arc.Sweep);
        Assert.Equal(new VPoint(10, 0), arc.End);
    }

    [Fact]
    public void Arc_parameters_are_read()
    {
        var path = PathDataParser.Parse("M10 10 A 20 15 30 0 1 50 10");
        var arc = path.Segments[1];
        Assert.Equal((20, 15, 30), (arc.Rx, arc.Ry, arc.XAxisRotation));
        Assert.False(arc.LargeArc);
        Assert.True(arc.Sweep);
        Assert.Equal("M10 10A20 15 30 0 1 50 10", Roundtrip("M10 10 A 20 15 30 0 1 50 10"));
    }

    [Theory]
    [InlineData("M1e-3 5", 0.001, 5)]
    [InlineData("M.5.5", 0.5, 0.5)]
    [InlineData("M-1-2", -1, -2)]
    [InlineData("M1.5e2,-2E1", 150, -20)]
    [InlineData("M +3 +4", 3, 4)]
    [InlineData("M10-5", 10, -5)]
    public void Number_formats(string d, double x, double y)
    {
        var p = PathDataParser.Parse(d).Segments[0].End;
        Assert.Equal(x, p.X, 9);
        Assert.Equal(y, p.Y, 9);
    }

    [Fact]
    public void Errors_stop_parsing_and_keep_what_was_read()
    {
        var result = PathDataParser.ParseWithResult("M0 0 L10 10 L20 x L30 30");
        Assert.True(result.HadError);
        Assert.Equal(2, result.Path.Segments.Count);
        Assert.Equal("M0 0L10 10", PathDataWriter.Write(result.Path));
    }

    [Theory]
    [InlineData("L10 10")]
    [InlineData("10 10 L20 20")]
    [InlineData("")]
    public void A_path_must_start_with_a_moveto(string d) => Assert.True(PathDataParser.Parse(d).IsEmpty);

    [Fact]
    public void Incomplete_arguments_are_dropped()
    {
        var result = PathDataParser.ParseWithResult("M0 0 L10");
        Assert.True(result.HadError);
        Assert.Single(result.Path.Segments);
    }

    [Fact]
    public void After_close_a_drawing_command_starts_at_the_figure_start()
    {
        var path = PathDataParser.Parse("M10 10 L20 10 L20 20 Z L30 30");
        var figures = path.Figures.ToList();
        Assert.Equal(2, figures.Count);
        Assert.Equal(new VPoint(10, 10), figures[1].Start);
        Assert.Equal("M10 10H20V20ZM10 10L30 30", PathDataWriter.Write(path));
    }

    [Fact]
    public void Implicit_lineto_after_relative_moveto_is_relative()
    {
        var path = PathDataParser.Parse("m10 10 5 5 5 5");
        Assert.Equal(new VPoint(15, 15), path.Segments[1].End);
        Assert.Equal(new VPoint(20, 20), path.Segments[2].End);
    }

    [Fact]
    public void Number_after_close_is_an_error()
    {
        var result = PathDataParser.ParseWithResult("M0 0 L10 0 Z 5 5");
        Assert.True(result.HadError);
    }

    [Fact]
    public void Writer_limits_decimals_and_uses_invariant_culture()
    {
        var path = new VectorPath().MoveTo(1.23456, 2.5).LineTo(10, 7.0004);
        Assert.Equal("M1.235 2.5L10 7", PathDataWriter.Write(path));
        Assert.Equal("M1.23456 2.5L10 7.0004", PathDataWriter.Write(path, 6));
    }

    [Fact]
    public void Writer_keeps_tiny_values()
    {
        var path = new VectorPath().MoveTo(0.00001, 0).LineTo(5, 0);
        Assert.Contains("0.00001", PathDataWriter.Write(path));
    }

    [Fact]
    public void Writer_separates_negative_numbers_without_space()
    {
        var path = new VectorPath().MoveTo(10, -5).LineTo(-3, 4);
        Assert.Equal("M10-5L-3 4", PathDataWriter.Write(path));
    }

    [Fact]
    public void Write_then_parse_gives_the_same_path()
    {
        var original = PathDataParser.Parse(SvgPathsSample());
        var again = PathDataParser.Parse(PathDataWriter.Write(original, 6));
        Assert.Equal(original.Segments.Count, again.Segments.Count);
        for (var i = 0; i < original.Segments.Count; i++)
        {
            Assert.Equal(original.Segments[i].Kind, again.Segments[i].Kind);
            Assert.Equal(original.Segments[i].End.X, again.Segments[i].End.X, 5);
            Assert.Equal(original.Segments[i].End.Y, again.Segments[i].End.Y, 5);
        }
    }

    private static string SvgPathsSample() =>
        "M10 10 L60 10 H100 V50 C110 60 130 60 140 50 S170 40 180 50 Q200 10 220 50 T260 50 A20 20 0 0 1 290 50 Z";

    [Fact]
    public void Every_path_in_the_sample_file_parses_without_error()
    {
        var doc = System.Xml.Linq.XDocument.Parse(SvgTestFiles.ReadText("paths"));
        var ds = doc.Descendants().Select(e => (string?)e.Attribute("d")).Where(d => d is not null).ToList();
        Assert.Equal(4, ds.Count);
        foreach (var d in ds)
            Assert.False(PathDataParser.ParseWithResult(d!).HadError, d);
    }
}

public class TransformParserTests
{
    [Fact]
    public void Parses_each_function()
    {
        Assert.Equal(Matrix2D.Translate(10, 20), TransformParser.Parse("translate(10 20)"));
        Assert.Equal(Matrix2D.Translate(10, 0), TransformParser.Parse("translate(10)"));
        Assert.Equal(Matrix2D.Scale(2, 3), TransformParser.Parse("scale(2, 3)"));
        Assert.Equal(Matrix2D.Scale(2, 2), TransformParser.Parse("scale(2)"));
        Assert.Equal(Matrix2D.Rotate(45), TransformParser.Parse("rotate(45)"));
        Assert.Equal(Matrix2D.Rotate(45, 5, 6), TransformParser.Parse("rotate(45 5 6)"));
        Assert.Equal(Matrix2D.SkewX(20), TransformParser.Parse("skewX(20)"));
        Assert.Equal(Matrix2D.SkewY(20), TransformParser.Parse("skewY(20)"));
        Assert.Equal(new Matrix2D(1, 2, 3, 4, 5, 6), TransformParser.Parse("matrix(1 2 3 4 5 6)"));
    }

    [Fact]
    public void A_list_multiplies_left_to_right()
    {
        var m = TransformParser.Parse("translate(10 0) scale(2) rotate(90),translate(1)");
        var expected = Matrix2D.Translate(10, 0) * Matrix2D.Scale(2) * Matrix2D.Rotate(90) * Matrix2D.Translate(1, 0);
        Assert.Equal(expected, m);
    }

    [Theory]
    [InlineData("translate(10 20")]
    [InlineData("translate(1 2 3)")]
    [InlineData("foo(1)")]
    [InlineData("rotate(a)")]
    [InlineData("matrix(1 2 3)")]
    public void Invalid_lists_return_null(string text)
    {
        Assert.Null(TransformParser.TryParse(text));
        Assert.Equal(Matrix2D.Identity, TransformParser.Parse(text));
    }

    [Fact]
    public void Empty_is_the_identity() => Assert.Equal(Matrix2D.Identity, TransformParser.TryParse(""));

    [Theory]
    [InlineData("translate(10 20)")]
    [InlineData("scale(2 3)")]
    [InlineData("rotate(30)")]
    [InlineData("matrix(1 0.5 0.25 1 10 20)")]
    public void Write_then_parse_is_stable(string text)
    {
        var m = TransformParser.Parse(text);
        var again = TransformParser.Parse(TransformParser.Write(m));
        Assert.InRange(again.A, m.A - 1e-6, m.A + 1e-6);
        Assert.InRange(again.B, m.B - 1e-6, m.B + 1e-6);
        Assert.InRange(again.C, m.C - 1e-6, m.C + 1e-6);
        Assert.InRange(again.D, m.D - 1e-6, m.D + 1e-6);
        Assert.InRange(again.E, m.E - 1e-6, m.E + 1e-6);
        Assert.InRange(again.F, m.F - 1e-6, m.F + 1e-6);
    }

    [Fact]
    public void Write_picks_the_shortest_form()
    {
        Assert.Equal("translate(10 20)", TransformParser.Write(Matrix2D.Translate(10, 20)));
        Assert.Equal("scale(2)", TransformParser.Write(Matrix2D.Scale(2)));
        Assert.Equal("scale(2 3)", TransformParser.Write(Matrix2D.Scale(2, 3)));
        Assert.Equal("rotate(90)", TransformParser.Write(Matrix2D.Rotate(90)));
        Assert.Equal("", TransformParser.Write(Matrix2D.Identity));
        Assert.StartsWith("matrix(", TransformParser.Write(Matrix2D.SkewX(20)));
    }

    [Fact]
    public void Sample_file_transforms_all_parse()
    {
        var doc = System.Xml.Linq.XDocument.Parse(SvgTestFiles.ReadText("transforms"));
        var values = doc.Descendants().Select(e => (string?)e.Attribute("transform")).Where(t => t is not null).ToList();
        Assert.Equal(5, values.Count);
        Assert.All(values, t => Assert.NotNull(TransformParser.TryParse(t)));
    }
}

public class ColorParserTests
{
    [Theory]
    [InlineData("#f00", 255, 0, 0, 255)]
    [InlineData("#F00A", 255, 0, 0, 170)]
    [InlineData("#ff8000", 255, 128, 0, 255)]
    [InlineData("#ff800080", 255, 128, 0, 128)]
    [InlineData("red", 255, 0, 0, 255)]
    [InlineData("RebeccaPurple", 102, 51, 153, 255)]
    [InlineData("transparent", 0, 0, 0, 0)]
    [InlineData("rgb(10, 20, 30)", 10, 20, 30, 255)]
    [InlineData("rgb(10 20 30)", 10, 20, 30, 255)]
    [InlineData("rgb(100%, 0%, 50%)", 255, 0, 128, 255)]
    [InlineData("rgba(10,20,30,0.5)", 10, 20, 30, 128)]
    [InlineData("rgb(10 20 30 / 50%)", 10, 20, 30, 128)]
    [InlineData("rgb(300, -5, 30)", 255, 0, 30, 255)]
    [InlineData("hsl(0, 100%, 50%)", 255, 0, 0, 255)]
    [InlineData("hsl(120, 100%, 25%)", 0, 128, 0, 255)]
    [InlineData("hsla(240, 100%, 50%, 0.25)", 0, 0, 255, 64)]
    [InlineData("hsl(360deg 0% 50%)", 128, 128, 128, 255)]
    public void Parses(string text, int r, int g, int b, int a)
    {
        Assert.True(ColorParser.TryParse(text, out var c));
        Assert.Equal((r, g, b, a), (c.R, c.G, c.B, c.A));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#gggggg")]
    [InlineData("notacolor")]
    [InlineData("rgb(1,2)")]
    [InlineData("rgb(a,b,c)")]
    public void Rejects(string text) => Assert.False(ColorParser.TryParse(text, out _));

    [Fact]
    public void Full_named_color_list()
    {
        // 148 CSS keywords (including rebeccapurple and the gray/grey pairs) plus transparent.
        Assert.Equal(149, ColorParser.NamedColors.Count);
        Assert.Equal(VColor.FromRgb(0, 128, 128), ColorParser.NamedColors["teal"]);
        Assert.Equal(VColor.FromRgb(255, 215, 0), ColorParser.NamedColors["gold"]);
    }

    [Fact]
    public void Hex_text_and_names_round_trip()
    {
        Assert.Equal("#ff8000", VColor.FromRgb(255, 128, 0).ToHex());
        Assert.Equal("#ff800080", VColor.FromRgba(255, 128, 0, 128).ToHex());
        Assert.Equal("red", SvgPaint.FromColor(VColor.FromRgb(255, 0, 0)).ToText());
    }
}

public class PaintAndLengthTests
{
    [Fact]
    public void Paint_kinds()
    {
        Assert.Equal(PaintKind.None, SvgPaint.TryParse("none")!.Kind);
        Assert.Equal(PaintKind.CurrentColor, SvgPaint.TryParse("currentColor")!.Kind);
        Assert.Equal(VColor.FromRgb(0, 0, 255), SvgPaint.TryParse("blue")!.Color);
        Assert.Null(SvgPaint.TryParse("bogus"));
        Assert.Null(SvgPaint.TryParse(null));
    }

    [Fact]
    public void Paint_url_with_fallback()
    {
        var paint = SvgPaint.TryParse("url(#grad1) #00ff00")!;
        Assert.Equal(PaintKind.Url, paint.Kind);
        Assert.Equal("grad1", paint.Id);
        Assert.Equal(VColor.FromRgb(0, 255, 0), paint.Fallback!.Color);
        Assert.Equal("url(#grad1) lime", paint.ToText());
        Assert.Equal("grad2", SvgPaint.TryParse("url('#grad2')")!.Id);
        Assert.Null(SvgPaint.TryParse("url(http://x/y.svg#a)"));
    }

    [Theory]
    [InlineData("10", 10, LengthUnit.None, 10)]
    [InlineData("10px", 10, LengthUnit.Px, 10)]
    [InlineData("12pt", 12, LengthUnit.Pt, 16)]
    [InlineData("1pc", 1, LengthUnit.Pc, 16)]
    [InlineData("25.4mm", 25.4, LengthUnit.Mm, 96)]
    [InlineData("2.54cm", 2.54, LengthUnit.Cm, 96)]
    [InlineData("1in", 1, LengthUnit.In, 96)]
    [InlineData("2em", 2, LengthUnit.Em, 32)]
    [InlineData("50%", 50, LengthUnit.Percent, 100)]
    [InlineData("1e1", 10, LengthUnit.None, 10)]
    [InlineData("-3.5", -3.5, LengthUnit.None, -3.5)]
    public void Lengths(string text, double value, LengthUnit unit, double user)
    {
        Assert.True(SvgLength.TryParse(text, out var length));
        Assert.Equal((value, unit), (length.Value, length.Unit));
        Assert.Equal(user, length.ToUser(percentBase: 200), 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("px")]
    [InlineData("10foo")]
    [InlineData("1 2")]
    public void Bad_lengths(string text) => Assert.False(SvgLength.TryParse(text, out _));

    [Fact]
    public void Em_uses_the_given_font_size() =>
        Assert.Equal(30, new SvgLength(2, LengthUnit.Em).ToUser(fontSize: 15));
}
