using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests.Vector;

public sealed class SvgActionsTests : BaseTests
{
    private const string Header = "<svg xmlns='http://www.w3.org/2000/svg' xmlns:xlink='http://www.w3.org/1999/xlink' width='200' height='200' viewBox='0 0 200 200'>";

    private SvgDocument Open(string body)
    {
        var sp = CinnabarSharpService();
        var doc = sp.GetRequiredService<IWorkspaceService>().OpenSvgDocument(SvgParser.Parse(Header + body + "</svg>").Root, null, null);
        doc.GlyphProvider = new FakeBoxProvider();
        return doc;
    }

    private static SvgElement El(SvgDocument doc, string id) => (SvgElement)doc.Root.FindById(id)!;

    private static byte[] Pixels(SvgDocument doc) => VectorRasterizer.RenderAll(doc.Root, 1, doc.RenderOptions).Bgra;

    // ---- Natural form of transformations ----

    [Fact]
    public void Moving_a_rect_changes_x_and_y_not_a_transform()
    {
        var doc = Open("<rect id='r' x='10' y='20' width='30' height='40' fill='red'/>");
        doc.Actions.MoveBy([El(doc, "r")], 5, -4);
        var r = El(doc, "r");
        Assert.Equal(("15", "16", null), (r.GetAttribute("x"), r.GetAttribute("y"), r.GetAttribute("transform")));
    }

    [Fact]
    public void Moving_a_rotated_rect_edits_its_transform()
    {
        var doc = Open("<rect id='r' x='10' y='20' width='30' height='40' transform='rotate(30)'/>");
        doc.Actions.MoveBy([El(doc, "r")], 5, 0);
        Assert.Equal("10", El(doc, "r").GetAttribute("x"));
        var t = El(doc, "r").Transform;
        Assert.Equal(5 + 0, t.E, 6);   // translation of the whole is in the document x direction
        Assert.Equal(30, t.Decompose().Rotation, 3);
    }

    [Fact]
    public void Rotating_a_rect_edits_the_transform_and_leaves_the_geometry()
    {
        var doc = Open("<rect id='r' x='10' y='20' width='30' height='40'/>");
        doc.Actions.Rotate([El(doc, "r")], 90);
        var r = El(doc, "r");
        Assert.Equal(("10", "20", "30", "40"), (r.GetAttribute("x"), r.GetAttribute("y"), r.GetAttribute("width"), r.GetAttribute("height")));
        Assert.Equal(90, r.Transform.Decompose().Rotation, 6);
    }

    [Fact]
    public void Scaling_a_rect_changes_its_size_and_stroke_scaling_a_circle_only_when_uniform()
    {
        var doc = Open("<rect id='r' x='0' y='0' width='10' height='10' stroke='black' stroke-width='2'/><circle id='c' cx='50' cy='50' r='10'/><circle id='d' cx='100' cy='100' r='10'/>");
        doc.Actions.Transform([El(doc, "r")], Matrix2D.Scale(2, 3));
        var r = El(doc, "r");
        Assert.Equal(("20", "30", null), (r.GetAttribute("width"), r.GetAttribute("height"), r.GetAttribute("transform")));
        Assert.Equal(2 * Math.Sqrt(6), ((SvgRect)r).Style.StrokeWidth!.Value, 4);   // stroke follows the (geometric mean) scale

        doc.Actions.Transform([El(doc, "c")], Matrix2D.Scale(2));
        Assert.Equal(("100", "100", "20", null), (El(doc, "c").GetAttribute("cx"), El(doc, "c").GetAttribute("cy"), El(doc, "c").GetAttribute("r"), El(doc, "c").GetAttribute("transform")));

        doc.Actions.Transform([El(doc, "d")], Matrix2D.Scale(2, 1));
        Assert.Equal("10", El(doc, "d").GetAttribute("r"));
        Assert.False(El(doc, "d").Transform.IsIdentity);
    }

    [Fact]
    public void Ellipses_lines_polygons_and_paths_keep_their_natural_form()
    {
        var doc = Open("<ellipse id='e' cx='10' cy='10' rx='5' ry='2'/><line id='l' x1='0' y1='0' x2='10' y2='10' stroke='#000'/><polygon id='pg' points='0,0 10,0 5,10'/><path id='p' d='M0 0 L10 0 L10 10 Z' fill='red'/>");
        doc.Actions.Transform([El(doc, "e"), El(doc, "l"), El(doc, "pg"), El(doc, "p")], Matrix2D.Translate(100, 50) * Matrix2D.Scale(2));
        Assert.Equal(("120", "70", "10", "4"), (El(doc, "e").GetAttribute("cx"), El(doc, "e").GetAttribute("cy"), El(doc, "e").GetAttribute("rx"), El(doc, "e").GetAttribute("ry")));
        Assert.Equal(("100", "50", "120", "70"), (El(doc, "l").GetAttribute("x1"), El(doc, "l").GetAttribute("y1"), El(doc, "l").GetAttribute("x2"), El(doc, "l").GetAttribute("y2")));
        Assert.Equal("100,50 120,50 110,70", El(doc, "pg").GetAttribute("points"));
        Assert.Equal("M100 50H120V70Z", El(doc, "p").GetAttribute("d"));
        Assert.All(new[] { "e", "l", "pg", "p" }, id => Assert.Null(El(doc, id).GetAttribute("transform")));
    }

    [Fact]
    public void Moving_a_path_keeps_its_arcs()
    {
        var doc = Open("<path id='p' d='M0 0 A10 10 0 0 1 20 0' fill='red'/>");
        doc.Actions.MoveBy([El(doc, "p")], 5, 5);
        Assert.Equal("M5 5A10 10 0 0 1 25 5", El(doc, "p").GetAttribute("d"));
    }

    [Fact]
    public void Text_moves_by_its_coordinates_and_scales_by_a_transform()
    {
        var doc = Open("<text id='t' x='10' y='20'>ab<tspan x='30' y='20'>cd</tspan></text>");
        doc.Actions.MoveBy([El(doc, "t")], 5, 7);
        Assert.Equal(("15", "27"), (El(doc, "t").GetAttribute("x"), El(doc, "t").GetAttribute("y")));
        Assert.Equal("35", ((SvgElement)El(doc, "t").Children.OfType<SvgTextSpan>().First()).GetAttribute("x"));
        doc.Actions.Transform([El(doc, "t")], Matrix2D.Scale(2));
        Assert.False(El(doc, "t").Transform.IsIdentity);
    }

    [Fact]
    public void Gradient_in_user_space_makes_a_move_use_the_transform_so_the_gradient_follows()
    {
        var doc = Open("<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' x2='40'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></linearGradient>" +
            "<linearGradient id='b'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></linearGradient></defs>" +
            "<rect id='u' width='40' height='10' fill='url(#g)'/><rect id='o' y='20' width='40' height='10' fill='url(#b)'/>");
        doc.Actions.MoveBy([El(doc, "u"), El(doc, "o")], 50, 0);
        Assert.Equal("0", El(doc, "u").GetAttribute("x") ?? "0");
        Assert.Equal(50, El(doc, "u").Transform.E, 6);
        Assert.Equal("50", El(doc, "o").GetAttribute("x"));
        // Rendered: the gradient's red end is at the left edge of both moved rects.
        var px = Pixels(doc);
        int Red(int x, int y) => px[(y * 200 + x) * 4 + 2];
        Assert.True(Red(52, 5) > 200 && Red(52, 25) > 200);
    }

    [Fact]
    public void Nested_groups_convert_the_move_into_the_parents_coordinates()
    {
        var doc = Open("<g transform='scale(2)'><rect id='r' width='10' height='10'/></g>");
        doc.Actions.MoveBy([El(doc, "r")], 20, 0);
        Assert.Equal("10", El(doc, "r").GetAttribute("x"));     // 20 document units are 10 in the group
        Assert.Equal(20, SvgBounds.InDocument(El(doc, "r"))!.Value.X, 6);
    }

    [Fact]
    public void Resize_sets_the_bounding_box_of_a_selection()
    {
        var doc = Open("<rect id='a' x='0' y='0' width='10' height='10'/><rect id='b' x='20' y='0' width='10' height='10'/>");
        doc.Actions.Resize([El(doc, "a"), El(doc, "b")], new VRect(10, 10, 60, 20));
        var box = doc.Actions.BoundsOf([El(doc, "a"), El(doc, "b")])!.Value;
        Assert.Equal((10, 10, 60, 20), (Math.Round(box.X, 6), Math.Round(box.Y, 6), Math.Round(box.Width, 6), Math.Round(box.Height, 6)));
        Assert.Equal("10", El(doc, "a").GetAttribute("x"));
        Assert.Equal("20", El(doc, "a").GetAttribute("width"));
    }

    [Fact]
    public void Flip_mirrors_around_the_middle_of_the_selection()
    {
        var doc = Open("<rect id='a' x='10' y='10' width='20' height='10' fill='red'/>");
        doc.Actions.Flip([El(doc, "a")], horizontal: true);
        var box = SvgBounds.InDocument(El(doc, "a"))!.Value;
        Assert.Equal((10, 20), (Math.Round(box.X, 6), Math.Round(box.Width, 6)));   // same place, mirrored
        Assert.True(El(doc, "a").Transform.A < 0);
    }

    // ---- Ids ----

    [Fact]
    public void New_nodes_get_ids_and_clashing_ids_are_renamed_with_their_references()
    {
        var doc = Open("<g id='layer1' xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape' inkscape:groupmode='layer'/>");
        var rect = (SvgRect)doc.Actions.AddNode(new SvgRect());
        Assert.Equal("rect1", rect.Id);
        Assert.Same(El(doc, "layer1"), rect.Parent);
        Assert.Same(rect, doc.Selection.Primary);

        var group = new SvgGroup { Id = "rect1" };
        var inner = new SvgRect { Id = "inner" };
        var use = new SvgUse();
        use.Href = "#inner";
        group.AddChild(inner);
        group.AddChild(use);
        doc.Actions.AddNode(group);
        Assert.Equal("rect2", group.Id);
        Assert.Equal("inner", inner.Id);
        Assert.Equal("#inner", use.Href);

        doc.Actions.AddNode((SvgElement)group.DeepClone());
        var copy = El(doc, "layer1").Children.OfType<SvgGroup>().Last();
        Assert.Equal("rect3", copy.Id);
        // The copy's inner id changed and its use follows it.
        var copyInner = copy.Elements.OfType<SvgRect>().Single();
        Assert.NotEqual("inner", copyInner.Id);
        Assert.Equal("#" + copyInner.Id, copy.Elements.OfType<SvgUse>().Single().Href);
    }

    [Fact]
    public void Duplicate_puts_copies_above_with_new_ids_and_selects_them()
    {
        var doc = Open("<rect id='a' width='5' height='5'/><rect id='b' x='10' width='5' height='5'/>");
        var copies = doc.Actions.Duplicate([El(doc, "a")]);
        var copy = Assert.Single(copies);
        Assert.NotEqual("a", copy.Id);
        Assert.Equal([doc.Root.Elements.ElementAt(0), copy, doc.Root.Elements.ElementAt(2)], doc.Root.Elements.ToList());
        Assert.Equal([copy], doc.Selection.Nodes);
        Assert.NotEqual(El(doc, "a").InternalId, copy.InternalId);
    }

    [Fact]
    public void Rename_updates_references_and_rejects_bad_or_used_ids()
    {
        var doc = Open("<defs><linearGradient id='g'><stop offset='0' stop-color='red'/></linearGradient><linearGradient id='g2' xlink:href='#g'/></defs>" +
            "<rect id='r' width='5' height='5' fill='url(#g)' style='stroke:url(#g)'/>");
        doc.Actions.SetId(El(doc, "g"), "grad");
        Assert.Equal("url(#grad)", El(doc, "r").GetAttribute("fill"));
        Assert.Equal("stroke:url(#grad)", El(doc, "r").GetAttribute("style"));
        Assert.Equal("#grad", El(doc, "g2").Href);
        Assert.Throws<ArgumentException>(() => doc.Actions.SetId(El(doc, "r"), "grad"));
        Assert.Throws<ArgumentException>(() => doc.Actions.SetId(El(doc, "r"), "1bad id"));
        Assert.Single(doc.History.Items, i => i.Text == "Rename");
    }

    [Fact]
    public void Label_declares_the_inkscape_namespace_when_the_file_lacks_it()
    {
        var doc = Open("<rect id='r' width='5' height='5'/>");
        doc.Actions.SetLabel(El(doc, "r"), "Box");
        Assert.Equal("Box", El(doc, "r").Label);
        var xml = SvgWriter.ToText(doc.Root);
        Assert.Contains("xmlns:inkscape=", xml);
        Assert.DoesNotContain("xmlns:p1", xml);
        var reparsed = SvgParser.Parse(xml).Root;
        Assert.Equal("Box", ((SvgElement)reparsed.FindById("r")!).Label);
        doc.History.Undo();
        Assert.DoesNotContain("inkscape", SvgWriter.ToText(doc.Root));
    }

    // ---- Style, visibility, lock ----

    [Fact]
    public void Style_edits_go_back_to_where_the_property_came_from()
    {
        var doc = Open("<rect id='a' width='5' height='5' fill='red'/><rect id='b' x='10' width='5' height='5' style='fill:red;stroke:blue'/>");
        doc.Actions.SetFill([El(doc, "a"), El(doc, "b")], SvgPaint.FromColor(VColor.FromRgb(0, 128, 0)));
        Assert.Equal("green", El(doc, "a").GetAttribute("fill"));
        Assert.Equal("fill:green;stroke:blue", El(doc, "b").GetAttribute("style"));
        Assert.Single(doc.History.Items, i => i.Text == "Set Fill");
    }

    [Fact]
    public void Hide_show_and_lock_use_display_and_sodipodi()
    {
        var doc = Open("<rect id='a' width='5' height='5' fill='red'/>");
        doc.Actions.SetVisible(El(doc, "a"), false);
        Assert.Equal("none", El(doc, "a").GetAttribute("display"));
        Assert.True(StyleResolver.ComputeFor(El(doc, "a")).DisplayNone);
        doc.Actions.SetVisible(El(doc, "a"), true);
        Assert.Null(El(doc, "a").GetAttribute("display"));
        doc.Actions.SetLocked(El(doc, "a"), true);
        Assert.True(El(doc, "a").IsLocked);
        Assert.Contains("xmlns:sodipodi", SvgWriter.ToText(doc.Root));
        doc.Actions.SetLocked(El(doc, "a"), false);
        Assert.False(El(doc, "a").IsLocked);
    }

    // ---- Order and groups ----

    [Fact]
    public void Raise_and_lower_move_one_step_among_elements_skipping_comments()
    {
        var doc = Open("<rect id='a' width='5' height='5'/><!-- note --><rect id='b' width='5' height='5'/><rect id='c' width='5' height='5'/>");
        doc.Actions.Raise([El(doc, "a")]);
        Assert.Equal(["b", "a", "c"], doc.Root.Elements.Select(e => e.Id));
        doc.Actions.Lower([El(doc, "c")]);
        Assert.Equal(["b", "c", "a"], doc.Root.Elements.Select(e => e.Id));
        doc.Actions.RaiseToTop([El(doc, "b")]);
        Assert.Equal(["c", "a", "b"], doc.Root.Elements.Select(e => e.Id));
        doc.Actions.LowerToBottom([El(doc, "b"), El(doc, "a")]);
        Assert.Equal(["a", "b", "c"], doc.Root.Elements.Select(e => e.Id));   // relative order (a below b) kept
    }

    [Fact]
    public void Raise_of_the_top_object_does_nothing_and_adds_no_history()
    {
        var doc = Open("<rect id='a' width='5' height='5'/><rect id='b' width='5' height='5'/>");
        var count = doc.History.Items.Count;
        doc.Actions.Raise([El(doc, "b")]);
        Assert.Equal(count, doc.History.Items.Count);
    }

    [Fact]
    public void Group_keeps_every_object_where_it_was_on_the_page()
    {
        var doc = Open("<g transform='translate(30 0)'><rect id='a' width='20' height='20' fill='#ff0000'/></g><rect id='b' x='100' y='100' width='20' height='20' fill='#00ff00' transform='rotate(20 110 110)'/>");
        var before = Pixels(doc);
        var group = doc.Actions.Group([El(doc, "a"), El(doc, "b")])!;
        Assert.Equal(["a", "b"], group.Elements.Select(e => e.Id));
        Assert.Equal(before, Pixels(doc));
        doc.Actions.Ungroup([group]);
        Assert.Equal(before, Pixels(doc));
    }

    [Fact]
    public void Ungroup_pushes_the_transform_and_style_into_the_children()
    {
        var doc = Open("<g id='g' transform='translate(10 20)' fill='#0000ff' stroke='#ff0000' opacity='0.5'><rect id='a' width='10' height='10'/><rect id='b' x='20' width='10' height='10' fill='#00ff00' opacity='0.5'/></g>");
        var before = Pixels(doc);
        doc.Actions.Ungroup([El(doc, "g")]);
        Assert.Null(doc.Root.FindById("g"));
        Assert.Equal(["a", "b"], doc.Root.Elements.Select(e => e.Id));
        Assert.Equal("10", El(doc, "a").GetAttribute("x"));        // moved by the group's transform
        Assert.Equal("#0000ff", El(doc, "a").GetAttribute("fill"));
        Assert.Equal("#00ff00", El(doc, "b").GetAttribute("fill"));  // its own fill wins
        Assert.Equal(0.25, El(doc, "b").Style.Opacity!.Value, 6);  // opacities multiply
        Assert.Equal(before, Pixels(doc));
        Assert.Equal([El(doc, "a"), El(doc, "b")], doc.Selection.Nodes);
    }

    [Fact]
    public void Ungroup_refuses_a_group_with_a_clip_path()
    {
        var doc = Open("<defs><clipPath id='c'><rect width='5' height='5'/></clipPath></defs><g id='g' clip-path='url(#c)'><rect width='10' height='10'/></g>");
        Assert.Throws<NotSupportedException>(() => doc.Actions.Ungroup([El(doc, "g")]));
        Assert.NotNull(doc.Root.FindById("g"));
    }

    [Fact]
    public void Move_to_group_keeps_the_place_on_the_page()
    {
        var doc = Open("<g id='g' transform='translate(50 50) scale(2)'/><rect id='r' x='10' y='10' width='20' height='20' fill='red'/>");
        var before = Pixels(doc);
        doc.Actions.MoveToParent([El(doc, "r")], (SvgContainer)El(doc, "g"));
        Assert.Same(El(doc, "g"), El(doc, "r").Parent);
        Assert.Equal(before, Pixels(doc));
        // Not into itself or its own descendant.
        doc.Actions.MoveToParent([El(doc, "g")], (SvgContainer)El(doc, "g"));
        Assert.Same(doc.Root, El(doc, "g").Parent);
    }

    // ---- Object to path ----

    [Fact]
    public void Object_to_path_keeps_the_look_and_the_attributes_but_the_geometry()
    {
        var doc = Open("<rect id='r' x='10' y='10' width='40' height='30' rx='5' fill='#ff0000' stroke='#000' stroke-width='3' transform='translate(5 5)' class='box'/><circle id='c' cx='100' cy='30' r='20' fill='#00ff00'/>");
        var before = Pixels(doc);
        var results = doc.Actions.ObjectToPath([El(doc, "r"), El(doc, "c")]);
        Assert.All(results, r => Assert.IsType<SvgPath>(r));
        var path = (SvgPath)El(doc, "r");
        Assert.Equal("translate(5 5)", path.GetAttribute("transform"));
        Assert.Equal("box", path.GetAttribute("class"));
        Assert.Null(path.GetAttribute("width"));
        Assert.Equal(before, Pixels(doc));
    }

    [Fact]
    public void Object_to_path_of_text_makes_a_group_of_paths_with_the_run_styles()
    {
        var doc = Open("<text id='t' x='10' y='50' font-size='20' fill='#ff0000'>ab<tspan fill='#0000ff'>cd</tspan></text>");
        var before = Pixels(doc);
        var group = Assert.IsType<SvgGroup>(Assert.Single(doc.Actions.ObjectToPath([El(doc, "t")])));
        Assert.Equal("t", group.Id);
        var paths = group.Elements.OfType<SvgPath>().ToList();
        Assert.Equal(2, paths.Count);
        Assert.Equal("red", paths[0].GetAttribute("fill"));
        Assert.Equal("blue", paths[1].GetAttribute("fill"));
        Assert.Null(group.GetAttribute("font-size"));
        Assert.Equal(before, Pixels(doc));
    }

    [Fact]
    public void Object_to_path_of_text_needs_fonts()
    {
        var doc = Open("<text id='t' x='10' y='50'>ab</text>");
        doc.GlyphProvider = null;
        Assert.Throws<InvalidOperationException>(() => doc.Actions.ObjectToPath([El(doc, "t")]));
    }

    // ---- Clipboard ----

    [Fact]
    public void Copy_and_paste_between_documents_keeps_the_look_with_context_and_definitions()
    {
        var source = Open("<defs><linearGradient id='g'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></linearGradient></defs>" +
            "<style>.big { stroke-width: 6; stroke: #ff00ff }</style>" +
            "<g transform='translate(40 30)' fill='#00ff00' font-size='20'><rect id='r' class='big' width='30' height='20' fill='url(#g)'/><circle id='c' cx='60' cy='10' r='8'/></g>");
        var text = source.Actions.Copy([El(source, "r"), El(source, "c")]);
        Assert.Contains("<defs", text);
        Assert.Contains("linearGradient", text);

        var target = Open("");
        var pasted = target.Actions.PasteSvg(text);

        Assert.Equal(2, pasted.Count);
        Assert.Equal(["Paste"], target.History.Items.Skip(1).Select(i => i.Text));
        Assert.Equal(pasted, target.Selection.Nodes);
        // Same pixels as in the source (only those two objects drawn there).
        var onlyThose = Open("<defs><linearGradient id='g'><stop offset='0' stop-color='#f00'/><stop offset='1' stop-color='#00f'/></linearGradient></defs>" +
            "<g transform='translate(40 30)' fill='#00ff00'><rect id='r' width='30' height='20' fill='url(#g)' stroke-width='6' stroke='#ff00ff'/><circle id='c' cx='60' cy='10' r='8'/></g>");
        Assert.Equal(Pixels(onlyThose), Pixels(target));
    }

    [Fact]
    public void Paste_renames_clashing_ids_and_reuses_identical_definitions()
    {
        var doc = Open("<defs><linearGradient id='g'><stop offset='0' stop-color='#f00'/></linearGradient></defs><rect id='r' width='5' height='5' fill='url(#g)'/>");
        var text = doc.Actions.Copy([El(doc, "r")]);
        var pasted = doc.Actions.PasteSvg(text);
        var copy = Assert.Single(pasted);
        Assert.NotEqual("r", copy.Id);
        Assert.Equal("url(#g)", copy.GetAttribute("fill"));                          // identical definition: shared
        Assert.Single(doc.Root.Descendants().OfType<SvgLinearGradient>());

        // A different definition with the same id is imported under a new id, and the pasted object follows it.
        var other = "<svg xmlns='http://www.w3.org/2000/svg'><defs><linearGradient id='g'><stop offset='0' stop-color='#0f0'/></linearGradient></defs><rect id='x' width='5' height='5' fill='url(#g)'/></svg>";
        var second = Assert.Single(doc.Actions.PasteSvg(other));
        var fill = second.GetAttribute("fill")!;
        Assert.NotEqual("url(#g)", fill);
        Assert.IsType<SvgLinearGradient>(doc.Root.FindById(fill[5..^1]));
        Assert.Equal(2, doc.Root.Descendants().OfType<SvgLinearGradient>().Count());
    }

    [Fact]
    public void Paste_text_that_is_not_svg_is_an_error_and_changes_nothing()
    {
        var doc = Open("<rect id='r' width='5' height='5'/>");
        var count = doc.History.Items.Count;
        Assert.Throws<ArgumentException>(() => doc.Actions.PasteSvg("hello"));
        Assert.Equal(count, doc.History.Items.Count);
        Assert.Empty(doc.Actions.PasteSvg("<svg xmlns='http://www.w3.org/2000/svg'><defs/></svg>"));
    }

    [Fact]
    public void Paste_a_fragment_from_another_application()
    {
        var doc = Open("");
        // The kind of text a browser or Inkscape puts on the clipboard: a document with a size and inkscape attributes.
        var pasted = doc.Actions.PasteSvg("<?xml version='1.0'?><svg xmlns='http://www.w3.org/2000/svg' xmlns:inkscape='http://www.inkscape.org/namespaces/inkscape' width='20mm' height='20mm'>" +
            "<metadata/><g inkscape:label='Shape'><circle cx='5' cy='5' r='4'/></g></svg>", dx: 10, dy: 10);
        var group = Assert.IsType<SvgGroup>(Assert.Single(pasted));
        Assert.Equal("Shape", group.Label);
        Assert.Equal(10, group.Transform.E, 6);
    }

    [Fact]
    public void Paste_a_bitmap_embeds_a_png_at_96_dpi_centered_and_fitted()
    {
        var doc = Open("");
        var small = doc.Actions.PasteImage(new ClipboardImage(new byte[10 * 20 * 4], 10, 20));
        Assert.StartsWith("data:image/png;base64,", small.Href);
        Assert.Equal((10, 20), (small.Width, small.Height));
        Assert.Equal((95, 90), (small.X, small.Y));
        var big = doc.Actions.PasteImage(new ClipboardImage(new byte[400 * 100 * 4], 400, 100));
        Assert.Equal((200, 50), (big.Width, big.Height));           // scaled to fit the 200 wide page
        Assert.Equal(0, big.X);
        var bytes = ImageResolver.Resolve(big.Href, null)!;
        var decoded = new MagickImageDecoder().Decode(bytes)!;
        Assert.Equal((400, 100), (decoded.Width, decoded.Height));  // the pixels are not resampled
    }

    [Fact]
    public void Clipboard_service_round_trip_text()
    {
        var doc = Open("<rect id='r' width='5' height='5' fill='#112233'/>");
        var clipboard = new FakeClipboard();
        clipboard.SetSvgAsync(doc.Actions.Copy([El(doc, "r")]), new ClipboardImage(new byte[4], 1, 1)).Wait();
        var text = clipboard.GetSvgAsync().Result;
        Assert.True(SvgClipboard.LooksLikeSvg(text));
        var other = Open("");
        Assert.Single(other.Actions.PasteSvg(text!));
        Assert.NotNull(clipboard.Image);
    }
}
