namespace CinnabarSharp.Vector.Tests;

internal sealed class FakeImageDecoder : IImageDecoder
{
    /// <summary>A 2 × 2 picture: red, green / blue, white (all opaque).</summary>
    public DecodedImage? Decode(ReadOnlySpan<byte> data) =>
        data.Length == 0 ? null : new DecodedImage(
        [
            0, 0, 255, 255, 0, 255, 0, 255,
            255, 0, 0, 255, 255, 255, 255, 255,
        ], 2, 2);
}

internal sealed class BoxGlyphProvider : IGlyphOutlineProvider
{
    public VectorPath Outline(string text, TextStyle style, out double advance)
    {
        advance = text.Length * style.Size * 0.6;
        return VectorPath.FromRect(0, -style.Size * 0.7, advance, style.Size * 0.7);
    }
}

public class RasterizerTests
{
    private static SvgRoot Parse(string svg) => SvgParser.Parse(svg).Root;

    private static (byte[] Bgra, int Width, int Height) Render(string svg, double scale = 1, RenderOptions? options = null) =>
        VectorRasterizer.RenderAll(Parse(svg), scale, options);

    private static (byte B, byte G, byte R, byte A) Px((byte[] Bgra, int Width, int Height) image, int x, int y) =>
        PixelAssert.PixelAt(image.Bgra, image.Width, x, y);

    private static void AssertPixel((byte[] Bgra, int Width, int Height) image, int x, int y, int r, int g, int b, int a = 255, int tolerance = 0)
    {
        var p = Px(image, x, y);
        Assert.InRange(p.R, r - tolerance, r + tolerance);
        Assert.InRange(p.G, g - tolerance, g + tolerance);
        Assert.InRange(p.B, b - tolerance, b + tolerance);
        Assert.InRange(p.A, a - tolerance, a + tolerance);
    }

    private const string Open = "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='40' height='40' viewBox='0 0 40 40'>";

    [Fact]
    public void Shapes_sample_has_the_right_colors_at_known_points()
    {
        var image = VectorRasterizer.RenderAll(SvgParser.ParseFile(SvgTestFiles.PathOf("shapes")).Root, 1);
        Assert.Equal((200, 120), (image.Width, image.Height));
        AssertPixel(image, 30, 30, 0xe0, 0x30, 0x20);        // rect
        AssertPixel(image, 110, 30, 0x20, 0x60, 0xe0);       // circle
        AssertPixel(image, 160, 30, 0x20, 0xa0, 0x40);       // ellipse
        AssertPixel(image, 40, 90, 0x80, 0x40, 0x00);        // line
        AssertPixel(image, 100, 90, 0xa0, 0x20, 0xa0);       // polyline
        AssertPixel(image, 175, 85, 0xe0, 0xc0, 0x20);       // polygon
        AssertPixel(image, 5, 115, 0, 0, 0, 0);              // nothing
        AssertPixel(image, 10, 12, 0, 0, 0, 0);              // outside the rounded corner
    }

    [Fact]
    public void Output_has_channels_in_bgra_order()
    {
        var image = Render(Open + "<rect width='40' height='40' fill='#102030'/></svg>");
        Assert.Equal((0x30, 0x20, 0x10, 255), (image.Bgra[0], image.Bgra[1], image.Bgra[2], image.Bgra[3]));
    }

    [Fact]
    public void Stroke_and_fill_are_both_drawn_and_the_stroke_is_centered_on_the_edge()
    {
        var image = Render(Open + "<rect x='10' y='10' width='20' height='20' fill='#ff0000' stroke='#0000ff' stroke-width='4'/></svg>");
        AssertPixel(image, 20, 20, 255, 0, 0);
        AssertPixel(image, 10, 20, 0, 0, 255);   // on the edge: blue
        AssertPixel(image, 8, 20, 0, 0, 255);    // 2 px outside
        AssertPixel(image, 11, 20, 0, 0, 255);   // 2 px inside
        AssertPixel(image, 13, 20, 255, 0, 0);   // past the stroke
        AssertPixel(image, 6, 20, 0, 0, 0, 0);
    }

    [Fact]
    public void Fill_opacity_blends_with_what_is_behind()
    {
        var image = Render(Open + "<rect width='40' height='40' fill='#ffffff'/><rect width='40' height='40' fill='#000000' fill-opacity='0.5'/></svg>");
        AssertPixel(image, 5, 5, 128, 128, 128, 255, tolerance: 1);
    }

    [Fact]
    public void Group_opacity_is_applied_to_the_group_as_a_whole()
    {
        // Two overlapping opaque rects at group opacity 0.5: the overlap must not show the lower one through the upper one.
        var image = Render(Open + "<g opacity='0.5'><rect x='0' y='0' width='30' height='30' fill='#ff0000'/><rect x='10' y='10' width='30' height='30' fill='#0000ff'/></g></svg>");
        AssertPixel(image, 20, 20, 0, 0, 255, 128, tolerance: 1);   // overlap: blue only
        AssertPixel(image, 5, 5, 255, 0, 0, 128, tolerance: 1);
        AssertPixel(image, 35, 35, 0, 0, 255, 128, tolerance: 1);
    }

    [Fact]
    public void Element_opacity_applies_to_fill_and_stroke_together()
    {
        var image = Render(Open + "<rect x='10' y='10' width='20' height='20' fill='#ff0000' stroke='#ff0000' stroke-width='10' opacity='0.5'/></svg>");
        AssertPixel(image, 10, 20, 255, 0, 0, 128, tolerance: 1); // stroke over fill, not stacked
        AssertPixel(image, 20, 20, 255, 0, 0, 128, tolerance: 1);
    }

    [Fact]
    public void Display_none_and_visibility_hidden()
    {
        var image = Render(Open + "<rect width='20' height='20' fill='red' display='none'/><rect x='20' width='20' height='20' fill='red' visibility='hidden'/>" +
            "<g visibility='hidden'><rect y='20' width='20' height='20' fill='lime' visibility='visible'/></g></svg>");
        AssertPixel(image, 10, 10, 0, 0, 0, 0);
        AssertPixel(image, 30, 10, 0, 0, 0, 0);
        AssertPixel(image, 10, 30, 0, 255, 0);
    }

    [Fact]
    public void Transforms_apply_to_nested_groups()
    {
        var image = Render(Open + "<g transform='translate(20 0)'><g transform='scale(2)'><rect width='5' height='5' fill='#00ff00'/></g></g></svg>");
        AssertPixel(image, 25, 5, 0, 255, 0);
        AssertPixel(image, 31, 5, 0, 0, 0, 0);
        AssertPixel(image, 25, 11, 0, 0, 0, 0);
    }

    [Fact]
    public void Viewbox_scales_the_drawing_to_the_picture()
    {
        var image = Render("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20' viewBox='0 0 4 2'><rect width='2' height='2' fill='#ff0000'/></svg>");
        Assert.Equal((40, 20), (image.Width, image.Height));
        AssertPixel(image, 15, 10, 255, 0, 0);
        AssertPixel(image, 25, 10, 0, 0, 0, 0);
    }

    [Fact]
    public void Preserve_aspect_ratio_centers_with_meet()
    {
        var image = Render("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20' viewBox='0 0 10 10'><rect width='10' height='10' fill='#0000ff'/></svg>");
        AssertPixel(image, 20, 10, 0, 0, 255);
        AssertPixel(image, 5, 10, 0, 0, 0, 0);   // letterbox
        AssertPixel(image, 35, 10, 0, 0, 0, 0);
    }

    [Fact]
    public void Background_option_fills_behind_the_drawing()
    {
        var image = Render(Open + "<rect width='20' height='20' fill='red'/></svg>", 1, new RenderOptions { Background = VColor.White });
        AssertPixel(image, 30, 30, 255, 255, 255);
        AssertPixel(image, 5, 5, 255, 0, 0);
    }

    [Fact]
    public void Linear_gradient_goes_from_the_first_to_the_last_stop()
    {
        var image = Render(Open + "<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#0000ff'/></linearGradient></defs><rect width='40' height='40' fill='url(#g)'/></svg>");
        AssertPixel(image, 0, 20, 255, 0, 0, tolerance: 7);
        AssertPixel(image, 39, 20, 0, 0, 255, tolerance: 7);
        AssertPixel(image, 20, 20, 128, 0, 128, tolerance: 7);
    }

    [Fact]
    public void Gradient_spread_methods()
    {
        string Svg(string spread) => Open + $"<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='10' x2='20' spreadMethod='{spread}'><stop offset='0' stop-color='#000000'/><stop offset='1' stop-color='#ffffff'/></linearGradient></defs><rect width='40' height='40' fill='url(#g)'/></svg>";
        var pad = Render(Svg("pad"));
        AssertPixel(pad, 2, 5, 0, 0, 0);
        AssertPixel(pad, 35, 5, 255, 255, 255);
        var repeat = Render(Svg("repeat"));
        AssertPixel(repeat, 25, 5, 128, 128, 128, tolerance: 15);   // 1.5 periods: back at mid grey
        AssertPixel(repeat, 21, 5, 38, 38, 38, tolerance: 8);       // just after a restart: dark
        var reflect = Render(Svg("reflect"));
        AssertPixel(reflect, 21, 5, 217, 217, 217, tolerance: 8);   // just past the end: still light, going back
    }

    [Fact]
    public void Gradient_stop_opacity_gives_transparent_pixels()
    {
        var image = Render(Open + "<defs><linearGradient id='g'><stop offset='0' stop-color='#ff0000' stop-opacity='0'/><stop offset='1' stop-color='#ff0000'/></linearGradient></defs><rect width='40' height='40' fill='url(#g)'/></svg>");
        Assert.InRange(Px(image, 0, 5).A, 0, 8);
        Assert.InRange(Px(image, 39, 5).A, 247, 255);
    }

    [Fact]
    public void Radial_gradient_is_centered()
    {
        var image = Render(Open + "<defs><radialGradient id='g'><stop offset='0' stop-color='#ffffff'/><stop offset='1' stop-color='#000000'/></radialGradient></defs><rect width='40' height='40' fill='url(#g)'/></svg>");
        AssertPixel(image, 20, 20, 255, 255, 255, tolerance: 12);
        AssertPixel(image, 2, 20, 10, 10, 10, tolerance: 25);
        AssertPixel(image, 2, 2, 0, 0, 0, tolerance: 2); // beyond the radius: the last stop
    }

    [Fact]
    public void Radial_focal_point_shifts_the_bright_spot()
    {
        var image = Render(Open + "<defs><radialGradient id='g' cx='0.5' cy='0.5' r='0.5' fx='0.25' fy='0.5'><stop offset='0' stop-color='#ffffff'/><stop offset='1' stop-color='#000000'/></radialGradient></defs><rect width='40' height='40' fill='url(#g)'/></svg>");
        Assert.True(Px(image, 10, 20).R > Px(image, 30, 20).R + 40);
    }

    [Fact]
    public void Object_bounding_box_units_follow_the_shape()
    {
        // The same gradient on two shapes of different size spans each of them.
        var image = Render(Open + "<defs><linearGradient id='g'><stop offset='0' stop-color='#000000'/><stop offset='1' stop-color='#ffffff'/></linearGradient></defs>" +
            "<rect x='0' y='0' width='10' height='10' fill='url(#g)'/><rect x='20' y='20' width='20' height='20' fill='url(#g)'/></svg>");
        Assert.True(Px(image, 1, 5).R < 40);
        Assert.True(Px(image, 8, 5).R > 200);
        Assert.True(Px(image, 21, 25).R < 40);
        Assert.True(Px(image, 38, 25).R > 200);
    }

    [Fact]
    public void Gradient_href_inheritance_and_fallback()
    {
        var image = Render(Open + "<defs><linearGradient id='a'><stop offset='0' stop-color='#ff0000'/><stop offset='1' stop-color='#ff0000'/></linearGradient><linearGradient id='b' href='#a'/></defs>" +
            "<rect width='20' height='20' fill='url(#b)'/><rect x='20' width='20' height='20' fill='url(#missing) #00ff00'/><rect y='20' width='20' height='20' fill='url(#missing)'/></svg>");
        AssertPixel(image, 10, 10, 255, 0, 0);
        AssertPixel(image, 30, 10, 0, 255, 0);
        AssertPixel(image, 10, 30, 0, 0, 0, 0);
    }

    [Fact]
    public void Clip_path_cuts_the_element()
    {
        var image = Render(Open + "<defs><clipPath id='c'><circle cx='20' cy='20' r='10'/></clipPath></defs><rect width='40' height='40' fill='#ff0000' clip-path='url(#c)'/></svg>");
        AssertPixel(image, 20, 20, 255, 0, 0);
        AssertPixel(image, 2, 2, 0, 0, 0, 0);
        AssertPixel(image, 20, 31, 0, 0, 0, 0);
        AssertPixel(image, 20, 28, 255, 0, 0);
    }

    [Fact]
    public void Clip_path_object_bounding_box_units()
    {
        var image = Render(Open + "<defs><clipPath id='c' clipPathUnits='objectBoundingBox'><rect width='0.5' height='1'/></clipPath></defs><rect x='10' y='10' width='20' height='20' fill='#ff0000' clip-path='url(#c)'/></svg>");
        AssertPixel(image, 15, 20, 255, 0, 0);
        AssertPixel(image, 25, 20, 0, 0, 0, 0);
    }

    [Fact]
    public void Clip_path_uses_clip_rule_and_ignores_stroke_and_fill_paint()
    {
        var image = Render(Open + "<defs><clipPath id='c'><path clip-rule='evenodd' fill='blue' stroke='green' stroke-width='20' d='M0 0H40V40H0Z M10 10H30V30H10Z'/></clipPath></defs><rect width='40' height='40' fill='#ff0000' clip-path='url(#c)'/></svg>");
        AssertPixel(image, 5, 5, 255, 0, 0);
        AssertPixel(image, 20, 20, 0, 0, 0, 0);
    }

    [Fact]
    public void Clip_path_on_a_group_and_a_missing_clip_path()
    {
        var image = Render(Open + "<defs><clipPath id='c'><rect width='20' height='40'/></clipPath></defs><g clip-path='url(#c)'><rect width='40' height='20' fill='red'/><rect y='20' width='40' height='20' fill='lime'/></g><rect x='30' y='30' width='10' height='10' fill='blue' clip-path='url(#nothing)'/></svg>");
        AssertPixel(image, 10, 10, 255, 0, 0);
        AssertPixel(image, 30, 10, 0, 0, 0, 0);
        AssertPixel(image, 10, 30, 0, 255, 0);
        AssertPixel(image, 35, 35, 0, 0, 255);
    }

    [Fact]
    public void Mask_uses_the_luminance_of_its_content()
    {
        var image = Render(Open + "<defs><linearGradient id='g'><stop offset='0' stop-color='#000'/><stop offset='1' stop-color='#fff'/></linearGradient><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='40' height='40'><rect width='40' height='40' fill='url(#g)'/></mask></defs>" +
            "<rect width='40' height='40' fill='#ff0000' mask='url(#m)'/></svg>");
        Assert.InRange(Px(image, 1, 20).A, 0, 20);
        Assert.InRange(Px(image, 20, 20).A, 110, 145);
        Assert.InRange(Px(image, 38, 20).A, 235, 255);
        Assert.Equal(255, Px(image, 38, 20).R);
    }

    [Fact]
    public void Use_instances_apply_position_transform_and_inherited_style()
    {
        var image = Render(Open + "<defs><rect id='r' width='10' height='10'/></defs><use href='#r' x='5' y='5' fill='#ff0000'/><use xlink:href='#r' x='25' y='5' fill='#00ff00' transform='translate(0 20)'/></svg>");
        AssertPixel(image, 8, 8, 255, 0, 0);
        AssertPixel(image, 28, 28, 0, 255, 0);
        AssertPixel(image, 28, 8, 0, 0, 0, 0);
    }

    [Fact]
    public void Symbol_is_scaled_to_its_viewport_and_clipped()
    {
        var image = Render(Open + "<defs><symbol id='s' viewBox='0 0 10 10'><rect width='10' height='10' fill='#0000ff'/><circle cx='5' cy='5' r='50' fill='#ff0000'/></symbol></defs><use href='#s' x='10' y='10' width='20' height='20'/></svg>");
        AssertPixel(image, 20, 20, 255, 0, 0);          // scaled 2x, circle covers the box
        AssertPixel(image, 5, 5, 0, 0, 0, 0);
        AssertPixel(image, 32, 20, 0, 0, 0, 0);          // clipped at the viewport
    }

    [Fact]
    public void Use_cycles_do_not_hang()
    {
        var image = Render(Open + "<defs><g id='a'><use href='#b'/></g><g id='b'><use href='#a'/></g></defs><use href='#a'/><rect width='5' height='5' fill='red'/></svg>");
        AssertPixel(image, 2, 2, 255, 0, 0);
    }

    [Fact]
    public void Nested_svg_clips_to_its_viewport()
    {
        var image = Render(Open + "<svg x='10' y='10' width='10' height='10'><rect width='100' height='100' fill='#ff0000'/></svg></svg>");
        AssertPixel(image, 15, 15, 255, 0, 0);
        AssertPixel(image, 25, 15, 0, 0, 0, 0);
        AssertPixel(image, 5, 5, 0, 0, 0, 0);
    }

    [Fact]
    public void Even_odd_fill_rule_makes_a_hole()
    {
        var image = Render(Open + "<path fill='#ff0000' fill-rule='evenodd' d='M0 0H40V40H0Z M10 10H30V30H10Z'/></svg>");
        AssertPixel(image, 5, 5, 255, 0, 0);
        AssertPixel(image, 20, 20, 0, 0, 0, 0);
    }

    [Fact]
    public void Dashed_strokes_leave_gaps()
    {
        var image = Render(Open + "<path d='M0 20H40' stroke='#000' stroke-width='4' stroke-dasharray='10 10'/></svg>");
        AssertPixel(image, 5, 20, 0, 0, 0);
        AssertPixel(image, 15, 20, 0, 0, 0, 0);
        AssertPixel(image, 25, 20, 0, 0, 0);
    }

    [Fact]
    public void Stroke_widths_scale_with_the_transform()
    {
        var image = Render(Open + "<path d='M0 5H40' stroke='#000' stroke-width='2' transform='scale(1 4)'/></svg>");
        AssertPixel(image, 20, 20, 0, 0, 0);   // 8 px tall now: y 16 to 24
        AssertPixel(image, 20, 17, 0, 0, 0);
        AssertPixel(image, 20, 13, 0, 0, 0, 0);
    }

    [Fact]
    public void Text_without_a_provider_is_a_placeholder_box_with_a_provider_it_is_outlines()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='100' height='40'><text x='10' y='30' font-size='20' fill='#ff0000'>abcd</text></svg>";
        var placeholder = Render(svg);
        Assert.True(Px(placeholder, 20, 20).A > 0);   // box from y=14 to 34
        var withProvider = Render(svg, options: new RenderOptions { GlyphProvider = new BoxGlyphProvider() });
        AssertPixel(withProvider, 20, 20, 255, 0, 0);               // box 10..58 x 16..30 (0.7 * 20 = 14 above the baseline)
        AssertPixel(withProvider, 60, 20, 0, 0, 0, 0);
        AssertPixel(withProvider, 20, 32, 0, 0, 0, 0);
    }

    [Fact]
    public void Text_anchor_moves_the_run()
    {
        var opts = new RenderOptions { GlyphProvider = new BoxGlyphProvider() };
        var middle = Render("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='40'><text x='50' y='30' font-size='20' text-anchor='middle' fill='#000'>abcd</text></svg>", options: opts);
        AssertPixel(middle, 30, 20, 0, 0, 0);
        AssertPixel(middle, 70, 20, 0, 0, 0);
        AssertPixel(middle, 20, 20, 0, 0, 0, 0);
        var end = Render("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='40'><text x='90' y='30' font-size='20' text-anchor='end' fill='#000'>abcd</text></svg>", options: opts);
        AssertPixel(end, 80, 20, 0, 0, 0);
        AssertPixel(end, 20, 20, 0, 0, 0, 0);
    }

    [Fact]
    public void Tspans_follow_each_other_and_take_their_own_style()
    {
        var opts = new RenderOptions { GlyphProvider = new BoxGlyphProvider() };
        var image = Render("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='40'><text x='0' y='30' font-size='10' fill='#ff0000'>ab<tspan fill='#0000ff'>cd</tspan></text></svg>", options: opts);
        AssertPixel(image, 5, 26, 255, 0, 0);   // "ab" is 12 wide
        AssertPixel(image, 18, 26, 0, 0, 255);  // "cd" from 12 to 24
    }

    [Fact]
    public void Images_are_drawn_with_the_decoder_and_aspect_ratio()
    {
        const string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><image x='0' y='0' width='40' height='40' href='data:image/png;base64,AAAA'/></svg>";
        var image = Render(svg, options: new RenderOptions { ImageDecoder = new FakeImageDecoder() });
        AssertPixel(image, 5, 5, 255, 0, 0);       // red, top left
        AssertPixel(image, 35, 5, 0, 255, 0);      // green
        AssertPixel(image, 5, 35, 0, 0, 255);      // blue
        AssertPixel(image, 35, 35, 255, 255, 255);

        var wide = Render("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='40'><image width='80' height='40' href='data:image/png;base64,AAAA'/></svg>",
            options: new RenderOptions { ImageDecoder = new FakeImageDecoder() });
        AssertPixel(wide, 5, 20, 0, 0, 0, 0);      // letterboxed: the 2x2 picture is 40 wide in the middle
        AssertPixel(wide, 25, 5, 255, 0, 0);
    }

    [Fact]
    public void Images_without_decoder_or_with_refused_links_are_placeholders()
    {
        const string embedded = "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><image width='40' height='40' href='data:image/png;base64,AAAA'/></svg>";
        var none = Render(embedded);
        Assert.True(Px(none, 20, 10).A > 0 && Px(none, 20, 10).R == Px(none, 20, 10).G);   // grey
        var link = Render("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><image width='40' height='40' href='linked.png'/></svg>",
            options: new RenderOptions { ImageDecoder = new FakeImageDecoder() });          // no base folder: refused
        Assert.True(Px(link, 20, 10).A > 0);
        Assert.NotEqual((255, 0, 0), (Px(link, 5, 5).R, Px(link, 5, 5).G, Px(link, 5, 5).B));
    }

    [Fact]
    public void Linked_images_are_read_inside_the_base_folder_only()
    {
        var folder = SvgTestFiles.Folder;
        var root = SvgParser.ParseFile(SvgTestFiles.PathOf("image")).Root;
        var allowed = VectorRasterizer.RenderAll(root, 1, new RenderOptions { ImageDecoder = new FakeImageDecoder(), BaseFolder = folder });
        AssertPixel(allowed, 60, 10, 255, 0, 0);               // the linked image: fake picture, red corner
        var refused = VectorRasterizer.RenderAll(root, 1, new RenderOptions { ImageDecoder = new FakeImageDecoder() });
        Assert.NotEqual((255, 0, 0), (Px(refused, 60, 10).R, Px(refused, 60, 10).G, Px(refused, 60, 10).B));

        Assert.Null(ImageResolver.Resolve("../outside.png", folder));
        Assert.Null(ImageResolver.Resolve("/etc/passwd", folder));
        Assert.Null(ImageResolver.Resolve("file:///etc/passwd", folder));
        Assert.Null(ImageResolver.Resolve("http://example.com/a.png", folder));
        Assert.Null(ImageResolver.Resolve("//host/share/a.png", folder));
        Assert.Null(ImageResolver.Resolve("C:\\x.png", folder));
        Assert.Null(ImageResolver.Resolve("linked.png", null));
        Assert.Null(ImageResolver.Resolve("missing.png", folder));
        Assert.NotNull(ImageResolver.Resolve("linked.png", folder));
        Assert.NotNull(ImageResolver.Resolve("./linked.png", folder));
    }

    [Fact]
    public void Data_uris_are_decoded()
    {
        Assert.Equal([1, 2, 3], ImageResolver.Resolve("data:application/octet-stream;base64,AQID", null));
        Assert.Equal("hi"u8.ToArray(), ImageResolver.Resolve("data:text/plain,hi", null));
        Assert.Null(ImageResolver.Resolve("data:image/png;base64,@@@", null));
    }

    [Fact]
    public void Unknown_and_unsupported_elements_render_nothing_without_failing()
    {
        var image = Render(Open + "<filter id='f'><feGaussianBlur stdDeviation='3'/></filter><foo:bar xmlns:foo='urn:x'/><switch><rect width='40' height='40' fill='red'/></switch>" +
            "<pattern id='p' width='4' height='4'><rect width='2' height='2'/></pattern><rect width='10' height='10' fill='url(#p)'/><marker id='m'/><rect x='20' width='10' height='10' fill='#00ff00' filter='url(#f)'/></svg>");
        AssertPixel(image, 5, 5, 0, 0, 0, 0);          // pattern fill: not drawn
        AssertPixel(image, 25, 5, 0, 255, 0);          // filtered element is drawn unfiltered
        AssertPixel(image, 35, 35, 0, 0, 0, 0);        // switch is not rendered
    }

    [Fact]
    public void Zoom_renders_sharp_edges_at_the_new_scale()
    {
        var root = Parse(Open + "<rect x='10' y='10' width='10' height='10' fill='#ff0000'/></svg>");
        var image = VectorRasterizer.RenderAll(root, 2.5);
        Assert.Equal((100, 100), (image.Width, image.Height));
        AssertPixel(image, 25, 25, 255, 0, 0);
        AssertPixel(image, 24, 25, 0, 0, 0, 0);
        AssertPixel(image, 49, 25, 255, 0, 0);
        AssertPixel(image, 50, 25, 0, 0, 0, 0);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(0.37)]
    public void A_region_equals_the_crop_of_the_full_render(double scale)
    {
        var root = SvgParser.ParseFile(SvgTestFiles.PathOf("gradients")).Root;
        var (full, width, height) = VectorRasterizer.RenderAll(root, scale);
        var region = new VRectI(width / 5, height / 4, width / 2, height / 3);
        var part = VectorRasterizer.Render(root, region, scale);
        for (var y = 0; y < region.Height; y++)
            for (var x = 0; x < region.Width; x++)
                for (var c = 0; c < 4; c++)
                    Assert.Equal(full[((region.Y + y) * width + region.X + x) * 4 + c], part[(y * region.Width + x) * 4 + c]);
    }

    [Fact]
    public void Rendering_into_a_span_overwrites_it()
    {
        var root = Parse(Open + "<rect width='10' height='10' fill='#ff0000'/></svg>");
        var buffer = new byte[40 * 40 * 4];
        Array.Fill(buffer, (byte)77);
        VectorRasterizer.Render(root, new VRectI(0, 0, 40, 40), 1, buffer.AsSpan());
        Assert.Equal((0, 0, 255, 255), (buffer[0], buffer[1], buffer[2], buffer[3]));
        Assert.Equal(0, buffer[(30 * 40 + 30) * 4 + 3]);
    }

    [Fact]
    public void Cancellation_stops_the_render()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var root = SvgParser.ParseFile(SvgTestFiles.PathOf("paths")).Root;
        Assert.Throws<OperationCanceledException>(() =>
            VectorRasterizer.RenderAll(root, 1, new RenderOptions { Cancellation = cts.Token }));
    }

    [Fact]
    public void An_empty_region_gives_an_empty_result()
    {
        Assert.Empty(VectorRasterizer.Render(Parse(Open + "</svg>"), VRectI.Empty, 1));
    }

    [Fact]
    public void Rendering_is_deterministic()
    {
        var root = SvgParser.ParseFile(SvgTestFiles.PathOf("paths")).Root;
        var first = VectorRasterizer.RenderAll(root, 1.7).Bgra;
        var second = VectorRasterizer.RenderAll(root, 1.7).Bgra;
        Assert.Equal(first, second);
    }

    [Fact]
    public void Performance_budget_for_the_paths_sample_at_full_hd()
    {
        var root = SvgParser.ParseFile(SvgTestFiles.PathOf("paths")).Root;
        // Warm up, then measure the rendering of 1920 x 1080 pixels of the picture (zoomed to fill it).
        VectorRasterizer.Render(root, new VRectI(0, 0, 100, 100), 1);
        var scale = 1920 / 300.0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        VectorRasterizer.Render(root, new VRectI(0, 0, 1920, 1080), scale);
        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 500, $"Rendering took {watch.ElapsedMilliseconds} ms");
    }
}
