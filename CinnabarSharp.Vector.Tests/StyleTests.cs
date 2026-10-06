namespace CinnabarSharp.Vector.Tests;

public class StyleTests
{
    private static SvgRoot Load(string name) => SvgParser.ParseFile(SvgTestFiles.PathOf(name)).Root;

    private static SvgElement El(SvgRoot root, string id) => Assert.IsAssignableFrom<SvgElement>(root.FindById(id));

    private static ComputedStyle Computed(SvgRoot root, string id) => StyleResolver.ComputeFor(El(root, id));

    [Fact]
    public void Presentation_attributes()
    {
        var root = Load("styles");
        var style = Computed(root, "attr");
        Assert.Equal(VColor.FromRgb(255, 128, 0), style.Fill.Color);
        Assert.Equal(VColor.FromRgb(0, 0, 0), style.Stroke.Color);
        Assert.Equal(2, style.StrokeWidth);
        Assert.Equal(StyleOrigin.Attribute, El(root, "attr").Style.OriginOf("fill"));
    }

    [Fact]
    public void Inline_style_wins_over_attributes_and_is_parsed()
    {
        var root = Load("styles");
        var style = Computed(root, "inline");
        Assert.Equal(VColor.FromRgb(0, 128, 255), style.Fill.Color);
        Assert.Equal(3, style.StrokeWidth);
        Assert.Equal(StyleOrigin.Inline, El(root, "inline").Style.OriginOf("fill"));
    }

    [Fact]
    public void Class_rules_apply_and_id_rules_win_by_specificity()
    {
        var root = Load("styles");
        var cls = Computed(root, "cls");
        Assert.Equal(VColor.FromRgb(0xd0, 0x30, 0x30), cls.Fill.Color);
        Assert.Equal(2, cls.StrokeWidth);
        Assert.Equal(StyleOrigin.Stylesheet, El(root, "cls").Style.OriginOf("fill"));

        var special = Computed(root, "special");
        Assert.Equal(VColor.FromRgb(0x30, 0xd0, 0x30), special.Fill.Color); // #special beats .red
        Assert.Equal(2, special.StrokeWidth); // still from .red
    }

    [Fact]
    public void Compound_selectors_and_comma_lists()
    {
        var root = Load("styles");
        Assert.Equal(VColor.FromRgb(0x30, 0x30, 0xd0), Computed(root, "blue").Fill.Color); // circle.blue
        Assert.Equal(0.5, Computed(root, "blue").Opacity);
    }

    [Fact]
    public void Descendant_selectors()
    {
        var root = Load("styles");
        Assert.Equal(0.5, Computed(root, "inner").FillOpacity); // g .inner
        Assert.Equal(1, Computed(root, "attr").FillOpacity);
    }

    [Fact]
    public void Current_color_inherits_color_from_the_group()
    {
        var root = Load("styles");
        var style = Computed(root, "cc");
        Assert.Equal(PaintKind.CurrentColor, style.Fill.Kind);
        Assert.Equal(VColor.FromRgb(0xc0, 0xc0, 0), style.Color);
    }

    [Fact]
    public void None_opacity_and_fill_rule()
    {
        var root = Load("styles");
        Assert.Equal(PaintKind.None, Computed(root, "none").Fill.Kind);
        Assert.Equal(0.4, Computed(root, "inner").StrokeOpacity);
        Assert.Equal(FillRule.EvenOdd, Computed(root, "eo").FillRule);
        Assert.Equal(FillRule.NonZero, Computed(root, "nz").FillRule);
    }

    [Fact]
    public void Properties_inherit_but_opacity_and_display_do_not()
    {
        var root = SvgParser.Parse(@"<svg xmlns='http://www.w3.org/2000/svg'>
            <g fill='red' stroke='blue' stroke-width='5' opacity='0.5' display='inline' font-size='20' visibility='hidden'>
              <rect id='a'/>
              <rect id='b' fill='inherit' opacity='inherit'/>
              <rect id='c' visibility='visible'/>
            </g></svg>").Root;
        var a = Computed(root, "a");
        Assert.Equal(VColor.FromRgb(255, 0, 0), a.Fill.Color);
        Assert.Equal(5, a.StrokeWidth);
        Assert.Equal(1, a.Opacity); // not inherited
        Assert.False(a.Visible);
        Assert.Equal(20, a.FontSize);
        Assert.Equal(0.5, Computed(root, "b").Opacity); // inherit forced
        Assert.True(Computed(root, "c").Visible);
    }

    [Fact]
    public void Display_none_is_not_inherited_by_computation_but_flagged_on_the_element()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='g' style='display:none'><rect id='r'/></g></svg>").Root;
        Assert.True(Computed(root, "g").DisplayNone);
        Assert.False(Computed(root, "r").DisplayNone);
    }

    [Fact]
    public void Stroke_properties_and_dashes()
    {
        var root = Load("strokes");
        var paths = root.Descendants().OfType<SvgPath>().ToList();
        var dashed = paths.First(p => p.GetAttribute("stroke-dasharray") == "10 5 2 5");
        var s = StyleResolver.ComputeFor(dashed);
        Assert.Equal([10.0, 5, 2, 5], s.DashArray!);
        Assert.Equal(7, s.DashOffset);
        var cap = StyleResolver.ComputeFor(paths.First(p => p.GetAttribute("stroke-linecap") == "square"));
        Assert.Equal(LineCap.Square, cap.LineCap);
        Assert.Equal(PaintKind.None, cap.Fill.Kind); // from the g
        var join = StyleResolver.ComputeFor(paths.First(p => p.GetAttribute("stroke-linejoin") == "bevel"));
        Assert.Equal(LineJoin.Bevel, join.LineJoin);
        var miter = StyleResolver.ComputeFor(paths.First(p => p.GetAttribute("stroke-miterlimit") == "4"));
        Assert.Equal(4, miter.MiterLimit);
    }

    [Theory]
    [InlineData("none", null)]
    [InlineData("0 0", null)]
    [InlineData("-1 2", null)]
    [InlineData("5", new[] { 5.0 })]
    [InlineData("1,2 3", new[] { 1.0, 2.0, 3.0 })]
    public void Dash_array_values(string text, double[]? expected)
    {
        var root = SvgParser.Parse($"<svg xmlns='http://www.w3.org/2000/svg'><rect id='r' stroke-dasharray='{text}'/></svg>").Root;
        Assert.Equal(expected, Computed(root, "r").DashArray);
    }

    [Fact]
    public void Font_properties()
    {
        var root = SvgParser.Parse(@"<svg xmlns='http://www.w3.org/2000/svg'>
            <g font-size='10' font-family='Arial, sans-serif' font-weight='bold' font-style='italic' text-anchor='middle'>
              <text id='t'>x</text><text id='u' font-size='2em' font-weight='lighter'>y</text><text id='p' font-size='50%'>z</text>
            </g></svg>").Root;
        var t = Computed(root, "t");
        Assert.Equal((10, "Arial, sans-serif", 700, true, TextAnchor.Middle), (t.FontSize, t.FontFamily, t.FontWeight, t.Italic, t.TextAnchor));
        Assert.Equal(20, Computed(root, "u").FontSize);
        Assert.Equal(400, Computed(root, "u").FontWeight);
        Assert.Equal(5, Computed(root, "p").FontSize);
    }

    [Fact]
    public void Em_lengths_follow_the_font_size()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g font-size='20'><rect id='r' width='2em' height='1em'/></g></svg>").Root;
        var rect = Assert.IsType<SvgRect>(root.FindById("r"));
        Assert.Equal((40, 20), (rect.Width, rect.Height));
    }

    [Fact]
    public void Stop_colors_and_opacity()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><linearGradient id='g'><stop id='s' offset='25%' style='stop-color:#ff0000;stop-opacity:0.5'/></linearGradient></svg>").Root;
        var stop = Assert.IsType<SvgStop>(root.FindById("s"));
        Assert.Equal(0.25, stop.Offset);
        Assert.Equal(VColor.FromRgba(255, 0, 0, 128), stop.Color);
    }

    [Fact]
    public void Clip_path_and_mask_references()
    {
        var root = Load("clip");
        var clipped = root.Descendants().OfType<SvgRect>().First(r => r.GetAttribute("clip-path") == "url(#cp1)");
        Assert.Equal("cp1", StyleResolver.ComputeFor(clipped).ClipPathId);
        var masked = root.Descendants().OfType<SvgRect>().First(r => r.GetAttribute("mask") == "url(#m1)");
        Assert.Equal("m1", StyleResolver.ComputeFor(masked).MaskId);
    }

    // ---- Writing a style property back ----

    [Fact]
    public void Editing_an_attribute_property_stays_an_attribute()
    {
        var root = Load("styles");
        var rect = El(root, "attr");
        rect.Style.Fill = SvgPaint.FromColor(VColor.FromRgb(1, 2, 3));
        Assert.Equal("#010203", rect.GetAttribute("fill"));
        Assert.Null(rect.GetAttribute("style"));
        Assert.Equal(VColor.FromRgb(1, 2, 3), Computed(root, "attr").Fill.Color);
    }

    [Fact]
    public void Editing_an_inline_property_stays_inline_and_does_not_duplicate()
    {
        var root = Load("styles");
        var rect = El(root, "inline");
        rect.Style.Fill = SvgPaint.FromColor(VColor.FromRgb(9, 9, 9));
        var style = rect.GetAttribute("style")!;
        Assert.Contains("fill:#090909", style);
        Assert.Equal(1, style.Split("fill:").Length - 1);
        Assert.Contains("stroke-width:3", style); // others untouched
        Assert.Null(rect.GetAttribute("fill"));
    }

    [Fact]
    public void Editing_a_stylesheet_property_writes_inline_so_it_wins_over_the_rule()
    {
        var root = Load("styles");
        var rect = El(root, "cls");
        rect.Style.Fill = SvgPaint.FromColor(VColor.FromRgb(0, 0, 255));
        Assert.Equal("fill:blue", rect.GetAttribute("style"));
        Assert.Equal(VColor.FromRgb(0, 0, 255), Computed(root, "cls").Fill.Color);
        Assert.Equal(2, Computed(root, "cls").StrokeWidth); // the rule still supplies the rest
    }

    [Fact]
    public void A_new_property_goes_to_style_when_the_element_has_one_else_to_an_attribute()
    {
        var root = Load("styles");
        El(root, "inline").Style.Set("opacity", "0.3");
        Assert.Contains("opacity:0.3", El(root, "inline").GetAttribute("style"));
        El(root, "attr").Style.Set("opacity", "0.3");
        Assert.Equal("0.3", El(root, "attr").GetAttribute("opacity"));
    }

    [Fact]
    public void Moving_a_property_from_attribute_to_inline_never_leaves_both()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect id='r' fill='red' style='stroke:blue'/></svg>").Root;
        var rect = El(root, "r");
        rect.Style.Set("fill", "green");
        Assert.Equal("green", rect.GetAttribute("fill")); // it was an attribute: stays one
        rect.Style.Set("fill", null);
        Assert.Null(rect.GetAttribute("fill"));
        Assert.Equal("stroke:blue", rect.GetAttribute("style"));
        rect.Style.Set("stroke", null);
        Assert.Null(rect.GetAttribute("style"));
    }

    [Fact]
    public void Typed_helpers_round_trip()
    {
        var rect = new SvgRect();
        rect.Style.Fill = SvgPaint.FromUrl("g1");
        rect.Style.FillOpacity = 0.25;
        rect.Style.StrokeWidth = 3;
        Assert.Equal("url(#g1)", rect.GetAttribute("fill"));
        Assert.Equal(0.25, rect.Style.FillOpacity);
        Assert.Equal(3, rect.Style.StrokeWidth);
        Assert.Equal(3, rect.Style.Specified.Count());
    }

    // ---- CSS parsing ----

    [Fact]
    public void Unsupported_selectors_and_at_rules_are_reported_and_skipped()
    {
        var result = SvgParser.Parse(@"<svg xmlns='http://www.w3.org/2000/svg'>
            <style>
              @import url(x.css);
              @media print { rect { fill: red } }
              rect > circle { fill: red }
              rect:hover { fill: red }
              [fill] { fill: red }
              .ok { fill: green }
            </style><rect id='r' class='ok'/></svg>");
        Assert.Contains(result.Warnings, w => w.Contains("@import"));
        Assert.Contains(result.Warnings, w => w.Contains("@media"));
        Assert.Contains(result.Warnings, w => w.Contains("rect > circle"));
        Assert.Contains(result.Warnings, w => w.Contains("rect:hover"));
        Assert.Contains(result.Warnings, w => w.Contains("[fill]"));
        Assert.Equal(1, result.Root.Stylesheet.RuleCount);
        Assert.Equal(VColor.FromRgb(0, 128, 0), StyleResolver.ComputeFor((SvgElement)result.Root.FindById("r")!).Fill.Color);
    }

    [Fact]
    public void Css_comments_cdata_and_important()
    {
        var root = SvgParser.Parse(@"<svg xmlns='http://www.w3.org/2000/svg'>
            <style><![CDATA[ /* c */ rect { fill: red !important; stroke: blue } ]]></style>
            <rect id='r' style='fill:green;stroke:yellow'/></svg>").Root;
        var style = StyleResolver.ComputeFor((SvgElement)root.FindById("r")!);
        Assert.Equal(VColor.FromRgb(255, 0, 0), style.Fill.Color); // !important beats inline
        Assert.Equal(VColor.FromRgb(255, 255, 0), style.Stroke.Color); // inline beats the rule
    }

    [Fact]
    public void Later_rules_win_at_equal_specificity()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>rect{fill:red} rect{fill:blue}</style><rect id='r'/></svg>").Root;
        Assert.Equal(VColor.FromRgb(0, 0, 255), StyleResolver.ComputeFor((SvgElement)root.FindById("r")!).Fill.Color);
    }

    [Fact]
    public void Style_attribute_parsing_handles_urls_with_semicolons_and_junk()
    {
        var declarations = SvgStyle.ParseDeclarations("fill: url('data:x;y'); ; bogus; stroke:red!important");
        Assert.Equal(["fill", "stroke"], declarations.Select(d => d.Name));
        Assert.Equal("url('data:x;y')", declarations[0].Value);
        Assert.True(declarations[1].Important);
    }
}
