using System.Xml.Linq;

namespace CinnabarSharp.Vector.Tests;

public class SvgModelTests
{
    private static SvgRoot Load(string name) => SvgParser.ParseFile(SvgTestFiles.PathOf(name)).Root;

    private static T Find<T>(SvgRoot root, string id) where T : SvgElement => Assert.IsType<T>(root.FindById(id));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Every_sample_parses(string name)
    {
        var result = SvgParser.ParseFile(SvgTestFiles.PathOf(name));
        Assert.True(result.Root.IsDocumentRoot);
        Assert.NotEmpty(result.Root.Children);
    }

    public static IEnumerable<object[]> Samples() => SvgTestFiles.AllAsData();

    [Fact]
    public void Shapes_have_typed_geometry()
    {
        var root = Load("shapes");
        var rect = Find<SvgRect>(root, "r1");
        Assert.Equal((10, 10, 60, 40, 8, 8), (rect.X, rect.Y, rect.Width, rect.Height, rect.Rx, rect.EffectiveRadii.Ry));
        var circle = Find<SvgCircle>(root, "c1");
        Assert.Equal((110, 30, 20), (circle.Cx, circle.Cy, circle.R));
        var ellipse = Find<SvgEllipse>(root, "e1");
        Assert.Equal((160, 30, 25, 15), (ellipse.Cx, ellipse.Cy, ellipse.Rx, ellipse.Ry));
        var line = Find<SvgLine>(root, "l1");
        Assert.Equal((10, 70, 70, 110), (line.X1, line.Y1, line.X2, line.Y2));
        Assert.Equal(4, Find<SvgPolyline>(root, "pl1").Points.Count);
        Assert.Equal(3, Find<SvgPolygon>(root, "pg1").Points.Count);
    }

    [Fact]
    public void Shape_paths_have_the_right_bounds()
    {
        var root = Load("shapes");
        Assert.Equal(new VRect(10, 10, 60, 40), RoundBounds(Find<SvgRect>(root, "r1").CreatePath()));
        Assert.Equal(new VRect(90, 10, 40, 40), RoundBounds(Find<SvgCircle>(root, "c1").CreatePath()));
        Assert.Equal(new VRect(135, 15, 50, 30), RoundBounds(Find<SvgEllipse>(root, "e1").CreatePath()));
        Assert.Equal(new VRect(10, 70, 60, 40), RoundBounds(Find<SvgLine>(root, "l1").CreatePath()));
        Assert.Equal(new VRect(90, 70, 60, 40), RoundBounds(Find<SvgPolyline>(root, "pl1").CreatePath()));
        var polygon = Find<SvgPolygon>(root, "pg1").CreatePath();
        Assert.Equal(SegmentKind.Close, polygon.Segments[^1].Kind);
    }

    private static VRect RoundBounds(VectorPath p)
    {
        var b = p.Bounds;
        return new VRect(Math.Round(b.X, 4), Math.Round(b.Y, 4), Math.Round(b.Width, 4), Math.Round(b.Height, 4));
    }

    [Fact]
    public void Path_elements_parse_their_data()
    {
        var root = Load("paths");
        var path = Find<SvgPath>(root, "p-abs");
        Assert.Contains(path.CreatePath().Segments, s => s.Kind == SegmentKind.ArcTo);
        // A relative path draws the same shape lower down.
        Assert.Equal(path.CreatePath().Bounds.Width, Find<SvgPath>(root, "p-rel").CreatePath().Bounds.Width, 6);
    }

    [Fact]
    public void Rect_without_size_has_no_geometry()
    {
        var rect = new SvgRect();
        Assert.True(rect.CreatePath().IsEmpty);
        rect.Width = 10;
        rect.Height = 5;
        Assert.False(rect.CreatePath().IsEmpty);
    }

    [Fact]
    public void Ellipse_with_one_radius_is_a_circle()
    {
        var e = new SvgEllipse();
        e.SetAttribute("rx", "10");
        Assert.Equal(20, e.CreatePath().Bounds.Height, 6);
    }

    [Fact]
    public void Units_file_reports_pixel_and_user_size()
    {
        var root = Load("units");
        Assert.Equal((50, 30), root.UserSize);
        var (w, h) = root.PixelSize;
        Assert.Equal(50 * 96 / 25.4, w, 6);
        Assert.Equal(30 * 96 / 25.4, h, 6);
        Assert.Equal(new Matrix2D(w / 50, 0, 0, h / 30, 0, 0).A, root.UserToPixel.A, 9);
    }

    [Fact]
    public void Size_without_viewbox_uses_width_and_height()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' width='120' height='80'/>").Root;
        Assert.Equal((120, 80), root.UserSize);
        Assert.Equal((120, 80), root.PixelSize);
        Assert.Equal(Matrix2D.Identity, root.UserToPixel);
        var bare = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'/>").Root;
        Assert.Equal((300, 150), bare.PixelSize);
    }

    [Fact]
    public void Percent_lengths_use_the_viewbox()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 100'><rect id='r' x='10%' y='50%' width='50%' height='10%'/></svg>").Root;
        var rect = Find<SvgRect>(root, "r");
        Assert.Equal((20, 50, 100, 10), (rect.X, rect.Y, rect.Width, rect.Height));
    }

    [Fact]
    public void ViewBox_aspect_ratio_rules()
    {
        var meet = PreserveAspectRatio.Parse("xMidYMid meet").ViewBoxTransform(new VRect(0, 0, 100, 50), 200, 200);
        Assert.Equal(2, meet.A, 9);
        Assert.Equal(50, meet.F, 9); // centered vertically
        var slice = PreserveAspectRatio.Parse("xMinYMin slice").ViewBoxTransform(new VRect(0, 0, 100, 50), 200, 200);
        Assert.Equal(4, slice.A, 9);
        Assert.Equal(0, slice.E, 9);
        var none = PreserveAspectRatio.Parse("none").ViewBoxTransform(new VRect(0, 0, 100, 50), 200, 200);
        Assert.Equal((2, 4), (none.A, none.D));
    }

    [Fact]
    public void Transforms_nest_in_the_tree()
    {
        var root = Load("transforms");
        Assert.Equal(Matrix2D.Translate(20, 20), Find<SvgGroup>(root, "g1").Transform);
        Assert.Equal(Matrix2D.Identity, Find<SvgRect>(root, "a").Transform);
        Assert.False(Find<SvgRect>(root, "d").Transform.IsIdentity);
    }

    [Fact]
    public void Groups_and_children_know_their_parents()
    {
        var root = Load("transforms");
        var c = Find<SvgRect>(root, "c");
        Assert.Equal(["g3", "g2", "g1"], c.Ancestors().OfType<SvgElement>().Select(e => e.Id).Where(i => i is not null));
        Assert.Same(root, c.DocumentRoot);
    }

    [Fact]
    public void Gradients_resolve_href_chains()
    {
        var root = Load("gradients");
        var lg3 = Find<SvgLinearGradient>(root, "lg3");
        Assert.Empty(lg3.OwnStops);
        Assert.Equal(2, lg3.ResolvedStops().Count);
        Assert.Equal(VColor.FromRgb(255, 0, 0), lg3.ResolvedStops()[0].Color);
        Assert.Equal(GradientUnits.ObjectBoundingBox, lg3.Units);
        Assert.Equal(1, lg3.X2); // inherited from lg1's default
        Assert.False(lg3.GradientTransform.IsIdentity);

        var lg2 = Find<SvgLinearGradient>(root, "lg2");
        Assert.Equal(GradientUnits.UserSpaceOnUse, lg2.Units);
        Assert.Equal((130, 230), (lg2.X1, lg2.X2));
        Assert.Equal(SpreadMethod.Reflect, lg2.Spread);
        Assert.Equal(0.5, lg2.ResolvedStops()[1].Offset);
        Assert.Equal(128, lg2.ResolvedStops()[1].Color.A);

        var rg1 = Find<SvgRadialGradient>(root, "rg1");
        Assert.Equal((0.5, 0.5, 0.5, 0.3, 0.3), (rg1.Cx, rg1.Cy, rg1.R, rg1.Fx, rg1.Fy));
        var rg2 = Find<SvgRadialGradient>(root, "rg2");
        Assert.Equal((190, 110, 30), (rg2.Cx, rg2.Cy, rg2.R));
        Assert.Equal(SpreadMethod.Repeat, rg2.Spread);
        Assert.Equal(190, rg2.Fx);
    }

    [Fact]
    public void Gradient_href_cycles_are_cut()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><linearGradient id='a' href='#b'/><linearGradient id='b' href='#a'/></svg>").Root;
        Assert.Empty(Find<SvgLinearGradient>(root, "a").ResolvedStops());
        Assert.Equal(2, Find<SvgLinearGradient>(root, "a").InheritanceChain().Count());
    }

    [Fact]
    public void Use_resolves_its_target_with_either_href()
    {
        var root = Load("use");
        var uses = root.Descendants().OfType<SvgUse>().ToList();
        Assert.Equal(4, uses.Count);
        Assert.All(uses, u => Assert.NotNull(u.Target));
        Assert.IsType<SvgRect>(uses[0].Target);
        Assert.IsType<SvgSymbol>(uses[2].Target);
        Assert.Equal(new VRect(0, 0, 10, 10), Assert.IsType<SvgSymbol>(uses[2].Target).ViewBox);
        Assert.Equal((40, 40), (uses[2].Width, uses[2].Height));
    }

    [Fact]
    public void Clip_and_mask_elements_are_typed()
    {
        var root = Load("clip");
        Assert.False(Find<SvgClipPath>(root, "cp1").UsesBoundingBox);
        Assert.True(Find<SvgClipPath>(root, "cp2").UsesBoundingBox);
        Assert.True(Find<SvgMask>(root, "m1").RegionUsesBoundingBox);
    }

    [Fact]
    public void Text_content_and_spans()
    {
        var root = Load("text");
        var t1 = Find<SvgText>(root, "t1");
        Assert.Equal((10, 30), (t1.X, t1.Y));
        Assert.Equal("Hello bold world", t1.Content);
        Assert.Single(t1.Children.OfType<SvgTextSpan>());
        var t3 = Find<SvgText>(root, "t3");
        var span = Assert.Single(t3.Children.OfType<SvgTextSpan>());
        Assert.Equal([4.0], span.DxList);
        Assert.Equal([-3.0], span.DyList);
        Assert.Equal("Endraised", t3.Content);
    }

    [Fact]
    public void Text_white_space_collapses_unless_preserved()
    {
        Assert.Equal("a b c", TextNormalizer.Normalize("  a \n\t b   c  ", false));
        Assert.Equal("  a    b   c  ", TextNormalizer.Normalize("  a \n\t b   c  ", true).Replace("    ", "    "));
    }

    [Fact]
    public void Set_plain_text_replaces_the_runs()
    {
        var root = Load("text");
        var text = Find<SvgText>(root, "t1");
        text.SetPlainText("New");
        Assert.Equal("New", text.Content);
        Assert.Empty(text.Children.OfType<SvgTextSpan>());
    }

    [Fact]
    public void Images_expose_href_and_bounds()
    {
        var root = Load("image");
        var embedded = Find<SvgImage>(root, "emb");
        Assert.StartsWith("data:image/png;base64,", embedded.Href);
        Assert.Equal(new VRect(5, 5, 40, 40), embedded.Bounds);
        Assert.Equal("linked.png", Find<SvgImage>(root, "lnk").Href);
    }

    [Fact]
    public void Inkscape_attributes_label_and_lock()
    {
        var root = Load("unknown");
        var rect = Find<SvgRect>(root, "r1");
        Assert.Equal("Red box", rect.Label);
        Assert.True(rect.IsLocked);
        Assert.Equal("c1", Find<SvgCircle>(root, "c1").Label);
        Assert.True(Find<SvgGroup>(root, "layer1").IsLayer);
        Assert.Equal("Layer 1", Find<SvgGroup>(root, "layer1").Label);
    }

    [Fact]
    public void Unknown_elements_are_kept_as_raw_nodes_in_place()
    {
        var root = Load("unknown");
        var kinds = root.Children.Select(c => c.GetType().Name).ToList();
        Assert.Equal(["SvgRawContent", "SvgRawElement", "SvgRawElement", "SvgRawElement", "SvgRawElement", "SvgDefs", "SvgGroup"], kinds);
        Assert.Equal("metadata", Assert.IsType<SvgRawElement>(root.Children[3]).ElementName);
        Assert.Equal("namedview", Assert.IsType<SvgRawElement>(root.Children[4]).ElementName);
        var filter = Assert.IsType<SvgRawElement>(Assert.IsType<SvgDefs>(root.Children[5]).Children.Single());
        Assert.Equal("filter", filter.ElementName);
    }

    [Fact]
    public void Duplicate_ids_resolve_to_the_first()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect id='x' width='1' height='1'/><circle id='x' r='5'/></svg>").Root;
        Assert.IsType<SvgRect>(root.FindById("x"));
        Assert.Equal("x1", root.NewId("x"));
        Assert.False(root.IdExists("y1"));
    }

    [Fact]
    public void Id_index_follows_edits()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><rect id='a'/></svg>").Root;
        var rect = Find<SvgRect>(root, "a");
        rect.Id = "b";
        Assert.Null(root.FindById("a"));
        Assert.Same(rect, root.FindById("b"));
        root.RemoveChild(rect);
        Assert.Null(root.FindById("b"));
    }

    [Fact]
    public void Tree_edits_keep_parents_and_reject_cycles()
    {
        var root = SvgParser.Parse("<svg xmlns='http://www.w3.org/2000/svg'><g id='g'><rect id='r'/></g></svg>").Root;
        var group = Find<SvgGroup>(root, "g");
        var rect = Find<SvgRect>(root, "r");
        Assert.Equal(0, group.RemoveChild(rect));
        Assert.Null(rect.Parent);
        root.InsertChild(0, rect);
        Assert.Same(root, rect.Parent);
        Assert.Throws<InvalidOperationException>(() => root.InsertChild(0, rect));
        Assert.Throws<InvalidOperationException>(() => group.InsertChild(0, root));
        root.MoveChild(rect, 1);
        Assert.Same(rect, root.Children[1]);
    }

    [Fact]
    public void Instances_get_unique_internal_ids_and_clones_can_keep_them()
    {
        var root = Load("shapes");
        var rect = Find<SvgRect>(root, "r1");
        var copy = Assert.IsType<SvgRect>(rect.DeepClone());
        var same = Assert.IsType<SvgRect>(rect.DeepClone(keepIds: true));
        Assert.NotEqual(rect.InternalId, copy.InternalId);
        Assert.Equal(rect.InternalId, same.InternalId);
        Assert.Equal(rect.Attributes, copy.Attributes);
        Assert.Null(copy.Parent);
    }

    [Fact]
    public void Deep_clone_of_a_group_copies_children_and_raw_content()
    {
        var root = Load("unknown");
        var layer = Find<SvgGroup>(root, "layer1");
        var copy = Assert.IsType<SvgGroup>(layer.DeepClone(keepIds: true));
        Assert.Equal(layer.Children.Count, copy.Children.Count);
        Assert.Equal(layer.Children.Select(c => c.InternalId), copy.Children.Select(c => c.InternalId));
        Assert.Equal(SvgWriter.ToXNode(layer).ToString(), SvgWriter.ToXNode(copy).ToString());
    }

    [Fact]
    public void Changes_mark_nodes_dirty_up_to_the_root_and_bump_the_version()
    {
        var root = Load("transforms");
        var version = root.Version;
        var rect = Find<SvgRect>(root, "c");
        Assert.False(rect.IsDirty);
        rect.X = 99;
        Assert.True(rect.IsDirty);
        Assert.True(Find<SvgGroup>(root, "g3").SubtreeDirty);
        Assert.True(root.SubtreeDirty);
        Assert.False(Find<SvgRect>(root, "a").IsDirty);
        Assert.True(root.Version > version);
        root.MarkClean();
        Assert.False(rect.IsDirty);
    }
}
