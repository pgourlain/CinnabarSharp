using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Core.Tests;

public class CoverageMaskTests
{
    private static string Draw(CoverageMask m) =>
        string.Join("/", Enumerable.Range(0, m.Height).Select(y =>
            new string(Enumerable.Range(0, m.Width).Select(x => m[x, y] switch { 0 => '.', 255 => '#', _ => '+' }).ToArray())));

    [Fact]
    public void Aliased_disc_is_round_and_symmetric()
    {
        var m = new CoverageMask(7, 7);
        m.Disc(3.5, 3.5, 2.5, antialias: false);

        Assert.Equal("......./..###../.#####./.#####./.#####./..###../.......", Draw(m));
    }

    [Fact]
    public void Antialiased_disc_has_soft_edges_and_solid_center()
    {
        var m = new CoverageMask(9, 9);
        m.Disc(4.5, 4.5, 3, antialias: true);

        Assert.Equal(255, m[4, 4]);
        Assert.InRange(m[1, 4], 1, 254);
        Assert.Equal(0, m[0, 0]);
    }

    [Fact]
    public void Pixel_line_is_one_pixel_wide()
    {
        var m = new CoverageMask(5, 3);
        m.PixelLine(new PointI(0, 0), new PointI(4, 2));

        Assert.Equal("#..../.##../...##", Draw(m));
    }

    [Fact]
    public void Rectangle_fill_and_stroke()
    {
        var fill = new CoverageMask(6, 5);
        fill.FillRectangle(new PointD(1, 1), new PointD(5, 4), antialias: false);
        Assert.Equal("....../.####./.####./.####./......", Draw(fill));

        var stroke = new CoverageMask(7, 7);
        stroke.StrokeRectangle(new PointD(1, 1), new PointD(6, 6), 1, antialias: false);
        Assert.Equal("......./.#####./.#...#./.#...#./.#...#./.#####./.......", Draw(stroke));
    }

    [Fact]
    public void Coverage_merges_with_max_not_sum()
    {
        var m = new CoverageMask(5, 5);
        m.Disc(2.5, 2.5, 1.2, antialias: true);
        var once = m[1, 2];
        m.Disc(2.5, 2.5, 1.2, antialias: true);

        Assert.Equal(once, m[1, 2]);
    }
}

public sealed class PaintToolsTests : BaseTests
{
    private readonly IServiceProvider _sp;
    private readonly ToolSettings _settings = new() { BrushWidth = 4, Antialiasing = false };
    private static readonly ColorBgra Red = ColorBgra.FromBgra(0, 0, 255, 255);
    private static readonly ColorBgra Blue = ColorBgra.FromBgra(255, 0, 0, 255);

    public PaintToolsTests()
    {
        _sp = CinnabarSharpService();
        _settings.PrimaryColor = Red;
        _settings.SecondaryColor = Blue;
    }

    private ImageDocument NewDoc(int w = 20, int h = 10) =>
        _sp.GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(w, h), ColorBgra.White);

    private static byte[] Pixel(ImageDocument doc, int x, int y) =>
        doc.Layers.CurrentUserLayer.Surface.ReadRegion(new RectangleI(x, y, 1, 1));

    private static void Drag(ITool tool, ImageDocument doc, ToolButton button, params (double X, double Y)[] points)
    {
        tool.OnPointerDown(doc, new ToolPointer(new PointD(points[0].X, points[0].Y), button, ToolModifiers.None));
        foreach (var p in points.Skip(1))
            tool.OnPointerMove(doc, new ToolPointer(new PointD(p.X, p.Y), button, ToolModifiers.None));
        var last = points[^1];
        tool.OnPointerUp(doc, new ToolPointer(new PointD(last.X, last.Y), button, ToolModifiers.None));
    }

    [Fact]
    public void Paintbrush_paints_primary_with_left_and_secondary_with_right()
    {
        var doc = NewDoc();

        Drag(new PaintbrushTool(_settings), doc, ToolButton.Left, (2, 5), (8, 5));
        Drag(new PaintbrushTool(_settings), doc, ToolButton.Right, (14, 5), (17, 5));

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(doc, 5, 5));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(doc, 15, 5));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(doc, 11, 5));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(doc, 5, 0));
    }

    [Fact]
    public void Stroke_is_one_history_step_storing_only_its_rectangle()
    {
        var doc = NewDoc();
        var before = doc.Layers[0].Surface.ToBgra();

        Drag(new PaintbrushTool(_settings), doc, ToolButton.Left, (2, 5), (5, 5), (8, 5));

        var item = Assert.IsType<PixelRegionHistoryItem>(doc.Workspace.History.Items[^1]);
        Assert.Equal("Paintbrush", item.Text);
        Assert.Equal(2, doc.Workspace.History.Items.Count);
        Assert.True(doc.IsDirty);

        doc.Workspace.History.Undo();
        Assert.Equal(before, doc.Layers[0].Surface.ToBgra());
        doc.Workspace.History.Redo();
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(doc, 5, 5));
    }

    [Fact]
    public void Painting_is_clipped_to_the_selection()
    {
        var doc = NewDoc();
        doc.SetSelection(SelectionMask.Rectangle(20, 10, new PointD(0, 0), new PointD(5, 10)));

        Drag(new PaintbrushTool(_settings), doc, ToolButton.Left, (2, 5), (15, 5));

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(doc, 3, 5));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(doc, 10, 5));
    }

    [Fact]
    public void Eraser_makes_pixels_transparent()
    {
        var doc = NewDoc();

        Drag(new EraserTool(_settings), doc, ToolButton.Left, (5, 5), (10, 5));

        Assert.Equal(0, Pixel(doc, 7, 5)[3]);
        Assert.Equal("Eraser", doc.Workspace.History.Items[^1].Text);
    }

    [Fact]
    public void Pencil_draws_hard_single_pixels()
    {
        var doc = NewDoc();
        _settings.BrushWidth = 10;

        Drag(new PencilTool(_settings), doc, ToolButton.Left, (1.5, 1.5), (6.5, 1.5));

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(doc, 4, 1));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(doc, 4, 2));
    }

    [Fact]
    public void Line_preview_is_replaced_on_each_move()
    {
        var doc = NewDoc();

        Drag(new LineTool(_settings), doc, ToolButton.Left, (2, 2), (18, 2), (2, 8));

        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(doc, 15, 2));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(doc, 2, 5));
        Assert.Single(doc.Workspace.History.Items.Skip(1));
    }

    [Fact]
    public void Shapes_outline_and_fill_use_primary_and_secondary()
    {
        var doc = NewDoc(20, 20);
        _settings.BrushWidth = 2;
        _settings.ShapeStyle = ShapeStyle.OutlineAndFill;

        Drag(new ShapesTool(_settings), doc, ToolButton.Left, (2, 2), (18, 18));

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(doc, 2, 10));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(doc, 10, 10));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(doc, 0, 0));
    }

    [Theory]
    [InlineData(GradientKind.Linear)]
    [InlineData(GradientKind.Radial)]
    [InlineData(GradientKind.Diamond)]
    [InlineData(GradientKind.Conical)]
    public void Gradient_goes_from_primary_to_secondary(GradientKind kind)
    {
        var doc = NewDoc(21, 3);
        _settings.GradientKind = kind;

        Drag(new GradientTool(_settings), doc, ToolButton.Left, (0.5, 1.5), (20.5, 1.5));

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(doc, 0, 1));
        var end = Pixel(doc, 20, 1);
        if (kind == GradientKind.Conical)
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, end); // angle 0 along the drag direction
        else
            Assert.True(end[0] > 240 && end[2] < 15, $"{kind} end pixel should be blue: {string.Join(",", end)}");
    }

    [Fact]
    public void Paint_bucket_fills_contiguous_area()
    {
        var doc = NewDoc();
        Drag(new PencilTool(_settings), doc, ToolButton.Left, (10.5, 0.5), (10.5, 9.5));

        new PaintBucketTool(_settings).OnPointerDown(doc, new ToolPointer(new PointD(3, 3), ToolButton.Right, ToolModifiers.None));

        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(doc, 0, 0));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, Pixel(doc, 15, 5));
        Assert.Equal("Paint Bucket", doc.Workspace.History.Items[^1].Text);
    }

    [Fact]
    public void Color_picker_sets_primary_and_secondary()
    {
        var doc = NewDoc();
        Drag(new PaintbrushTool(_settings), doc, ToolButton.Right, (5, 5), (6, 5));
        var changes = 0;
        _settings.ColorsChanged += () => changes++;

        var picker = new ColorPickerTool(_settings);
        picker.OnPointerDown(doc, new ToolPointer(new PointD(5, 5), ToolButton.Left, ToolModifiers.None));
        picker.OnPointerUp(doc, new ToolPointer(new PointD(5, 5), ToolButton.Left, ToolModifiers.None));
        picker.OnPointerDown(doc, new ToolPointer(new PointD(15, 5), ToolButton.Right, ToolModifiers.None));

        Assert.Equal(Blue, _settings.PrimaryColor);
        Assert.Equal(ColorBgra.White, _settings.SecondaryColor);
        Assert.Equal(2, changes);
        Assert.Single(doc.Workspace.History.Items.Skip(1));
    }

    [Fact]
    public void Region_flatten_matches_full_flatten()
    {
        var doc = NewDoc(30, 20);
        doc.Actions.AddNewLayer();
        doc.Layers[1].Opacity = 0.6;
        doc.Layers[1].BlendMode = BlendMode.Multiply;
        Drag(new PaintbrushTool(_settings), doc, ToolButton.Left, (3, 3), (25, 15));
        var region = new RectangleI(4, 2, 17, 11);

        var full = doc.Layers.GetFlattenedBgra();

        Assert.Equal(PixelRegion.Extract(full, 30, region), doc.Layers.GetFlattenedBgra(region));
    }

    [Fact]
    public void Painting_invalidates_only_the_touched_rectangle()
    {
        var doc = NewDoc(100, 100);
        var rects = new List<RectangleI>();
        using var sub = _sp.GetRequiredService<IDocumentEventsService>().DocumentEvents
            .Subscribe(new Observer(e => { if (e is CanvasEventItem c) rects.Add(c.Rect); }));

        Drag(new PaintbrushTool(_settings), doc, ToolButton.Left, (10, 10), (20, 10));

        Assert.NotEmpty(rects);
        Assert.All(rects, r => Assert.True(!r.IsEmpty && r.Width < 30 && r.Height < 10, r.ToString()));
    }

    private sealed class Observer(Action<EventItem<DocumentEventEnum>> onNext) : IObserver<EventItem<DocumentEventEnum>>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(EventItem<DocumentEventEnum> value) => onNext(value);
    }

    /// <summary>Strokes, shapes and gradients must produce the same pixels on every OS.</summary>
    [Fact]
    public void Painting_is_bit_identical_across_platforms()
    {
        var doc = NewDoc(64, 48);
        _settings.Antialiasing = true;
        _settings.BrushWidth = 5;
        Drag(new PaintbrushTool(_settings), doc, ToolButton.Left, (3.3, 4.1), (20.7, 30.2), (60.1, 10.9));
        _settings.ShapeKind = ShapeKind.Ellipse;
        _settings.ShapeStyle = ShapeStyle.OutlineAndFill;
        Drag(new ShapesTool(_settings), doc, ToolButton.Left, (10.2, 10.6), (50.4, 40.3));
        _settings.GradientKind = GradientKind.Radial;
        doc.SetSelection(SelectionMask.Rectangle(64, 48, new PointD(0, 30), new PointD(30, 48)));
        Drag(new GradientTool(_settings), doc, ToolButton.Right, (5, 40), (25, 44));
        doc.SetSelection(null);
        Drag(new EraserTool(_settings), doc, ToolButton.Left, (40, 5), (45, 45));

        var hash = Convert.ToHexString(SHA256.HashData(doc.Layers[0].Surface.ToBgra()));
        Assert.Equal(ExpectedHash, hash);
    }

    private const string ExpectedHash = "DA2507694B2A1C0D3A378D5E6A46331790F32E26CE9AE93DC733247ECC4E01F8";
}
