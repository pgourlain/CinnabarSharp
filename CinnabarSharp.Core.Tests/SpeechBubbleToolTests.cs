using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Core.Tests;

public sealed class SpeechBubbleToolTests : BaseTests
{
    private static readonly ColorBgra Red = ColorBgra.FromBgra(0, 0, 255, 255);
    private static readonly ColorBgra Blue = ColorBgra.FromBgra(255, 0, 0, 255);
    private static readonly byte[] RedPx = [0, 0, 255, 255];
    private static readonly byte[] BluePx = [255, 0, 0, 255];
    private static readonly byte[] Transparent = [0, 0, 0, 0];
    private static readonly byte[] White = [255, 255, 255, 255];

    private readonly IServiceProvider _sp;
    private readonly ToolSettings _settings = new() { BrushWidth = 2, Antialiasing = false, FontSize = 10 };
    private readonly SpeechBubbleTool _tool;

    public SpeechBubbleToolTests()
    {
        _sp = CinnabarSharpService();
        _settings.PrimaryColor = Red;
        _settings.SecondaryColor = Blue;
        _tool = new SpeechBubbleTool(_settings, new BlockTextRasterizer());
    }

    private ImageDocument NewDoc(int w = 200, int h = 150) =>
        _sp.GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(w, h), ColorBgra.White);

    private static byte[] Pixel(ImageDocument doc, int x, int y) =>
        doc.Layers.CurrentUserLayer.Surface.ReadRegion(new RectangleI(x, y, 1, 1));

    private static ToolPointer P(double x, double y, ToolModifiers mods = ToolModifiers.None) =>
        new(new PointD(x, y), ToolButton.Left, mods);

    private void Drag(ImageDocument doc, params ToolPointer[] points)
    {
        _tool.OnPointerDown(doc, points[0]);
        foreach (var p in points.Skip(1))
            _tool.OnPointerMove(doc, p);
        _tool.OnPointerUp(doc, points[^1]);
    }

    private static List<string> Steps(ImageDocument doc) =>
        doc.Workspace.History.Items.Take(doc.Workspace.History.Pointer + 1).Select(i => i.Text).ToList();

    private static PointD Center(RectangleD r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    [Fact]
    public void Dragging_puts_the_tail_on_the_press_point_and_the_bubble_on_the_release_point()
    {
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(70, 90), P(120, 50));

        var shape = _tool.Shape(doc)!;
        Assert.Equal(new PointD(20, 130), shape.Tail);
        var center = Center(shape.Body);
        Assert.InRange(center.X, 119, 121);
        Assert.InRange(center.Y, 49, 51);

        Assert.Equal(BluePx, Pixel(doc, 120, 50)); // inside: secondary color
        Assert.Equal(RedPx, Pixel(doc, (int)shape.Body.X, 50)); // outline: primary color
        Assert.Equal(RedPx, Pixel(doc, 45, 110)); // near the tail tip: all outline
        Assert.Equal(Transparent, Pixel(doc, 190, 140)); // the rest of the Bubbles layer
    }

    [Fact]
    public void Bubbles_go_to_one_Bubbles_layer_at_the_top()
    {
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(60, 40));
        Drag(doc, P(180, 140), P(150, 100));

        Assert.Equal(2, doc.Layers.UserLayers.Count);
        Assert.Equal(SpeechBubbleTool.LayerName, doc.Layers.CurrentUserLayer.Name);
        Assert.Equal(["New Image", "Add New Layer", "Speech Bubble", "Speech Bubble"], Steps(doc));
        Assert.Equal(White, doc.Layers[0].Surface.ReadRegion(new RectangleI(60, 40, 1, 1)));

        // Undo removes the bubbles, then the layer.
        doc.Workspace.History.Undo();
        doc.Workspace.History.Undo();
        Assert.Equal(Transparent, Pixel(doc, 60, 40));
        doc.Workspace.History.Undo();
        Assert.Single(doc.Layers.UserLayers);
    }

    [Fact]
    public void Without_own_layer_bubbles_are_painted_on_the_current_layer()
    {
        _settings.BubbleOwnLayer = false;
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(100, 60));

        Assert.Single(doc.Layers.UserLayers);
        Assert.Equal(BluePx, Pixel(doc, 100, 60));
        Assert.Equal(["New Image", "Speech Bubble"], Steps(doc));
    }

    [Fact]
    public void Typing_grows_the_bubble_and_paints_the_text_in_the_primary_color()
    {
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(100, 60));
        var before = _tool.Shape(doc)!.Body;

        _tool.OnTextInput(doc, "Hello world, this is long");
        var after = _tool.Shape(doc)!.Body;

        Assert.True(after.Width > before.Width);
        Assert.InRange(Center(after).X, 99, 101); // grows around its center
        Assert.Equal(["New Image", "Add New Layer", "Speech Bubble"], Steps(doc)); // still one step
        var reds = 0;
        for (var x = (int)after.X + 4; x < after.X + after.Width - 4; x++)
            if (Pixel(doc, x, 60).SequenceEqual(RedPx))
                reds++;
        Assert.True(reds > 20, $"{reds} text pixels");
    }

    [Fact]
    public void A_click_puts_the_bubble_above_right_of_the_point()
    {
        var doc = NewDoc();
        Drag(doc, P(30, 110));

        var shape = _tool.Shape(doc)!;
        Assert.Equal(new PointD(30, 110), shape.Tail);
        Assert.True(Center(shape.Body).X > 30 && Center(shape.Body).Y < 110);
    }

    [Fact]
    public void A_click_near_the_edge_keeps_the_bubble_on_the_image()
    {
        var doc = NewDoc();
        Drag(doc, P(190, 10));

        var body = _tool.Shape(doc)!.Body;
        Assert.True(body.X >= 0 && body.Y >= 0 && body.X + body.Width <= 200 && body.Y + body.Height <= 150);
    }

    [Fact]
    public void Clicking_elsewhere_finishes_the_bubble_and_starts_the_next_one()
    {
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(60, 40));
        _tool.OnTextInput(doc, "One");
        var first = _tool.Shape(doc)!.Body;
        Drag(doc, P(180, 140), P(150, 100));
        _tool.OnTextInput(doc, "Two");

        Assert.Equal(new PointD(180, 140), _tool.Shape(doc)!.Tail);
        Assert.Equal("Two", _tool.Engine.ToString());
        Assert.Equal(BluePx, Pixel(doc, (int)(first.X + first.Width / 2), (int)first.Y + 3)); // first bubble kept
    }

    [Fact]
    public void Dragging_the_tail_handle_moves_only_the_tip()
    {
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(100, 50));
        var body = _tool.Shape(doc)!.Body;

        Drag(doc, P(20, 130), P(180, 140));

        Assert.Equal(new PointD(180, 140), _tool.Shape(doc)!.Tail);
        Assert.Equal(body, _tool.Shape(doc)!.Body);
        Assert.Equal(Transparent, Pixel(doc, 45, 110));
        Assert.Equal(["New Image", "Add New Layer", "Speech Bubble"], Steps(doc));
    }

    [Fact]
    public void Dragging_the_border_moves_the_bubble_and_keeps_the_tail_tip()
    {
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(100, 50));
        _tool.OnTextInput(doc, "Hello world");
        var body = _tool.Shape(doc)!.Body;

        var border = new PointD(body.X + body.Width / 4, body.Y + body.Height - 1);
        Assert.Equal(ToolCursor.Move, _tool.CursorAt(doc, border));
        Drag(doc, P(border.X, border.Y), P(border.X + 30, border.Y + 10));

        var moved = _tool.Shape(doc)!;
        Assert.Equal(body.X + 30, moved.Body.X);
        Assert.Equal(body.Y + 10, moved.Body.Y);
        Assert.Equal(new PointD(20, 130), moved.Tail);
    }

    [Fact]
    public void Resizing_keeps_the_width_and_wraps_the_text()
    {
        var doc = NewDoc(300, 200);
        Drag(doc, P(20, 180), P(150, 60));
        var body = _tool.Shape(doc)!.Body;
        var right = new PointD(body.X + body.Width, body.Y + body.Height / 2);
        Assert.Equal(ToolCursor.ResizeHorizontal, _tool.CursorAt(doc, right));
        Drag(doc, P(right.X, right.Y), P(right.X + 40, right.Y));
        var width = _tool.Shape(doc)!.Body.Width;

        _tool.OnTextInput(doc, "aaaa bbbb cccc dddd eeee ffff gggg hhhh");

        var after = _tool.Shape(doc)!.Body;
        Assert.Equal(width, after.Width);
        Assert.True(after.Height > body.Height);
    }

    [Fact]
    public void Numbered_bubbles_count_up()
    {
        _settings.BubbleNumbered = true;
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(60, 50));
        Drag(doc, P(180, 140), P(150, 60));

        Assert.Equal(3, _settings.BubbleNextNumber);
        // The badge sits on the body's top-left corner, filled with the primary color.
        var body = _tool.Shape(doc)!.Body;
        Assert.Equal(RedPx, Pixel(doc, (int)body.X - 4, (int)body.Y - 4));
    }

    [Fact]
    public void Escape_finishes_and_typing_then_does_nothing()
    {
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(100, 60));
        Assert.True(_tool.IsTyping(doc));

        Assert.True(_tool.OnKeyDown(doc, ToolKey.Escape, ToolModifiers.None));

        Assert.False(_tool.IsEditing(doc));
        _tool.OnTextInput(doc, "x");
        Assert.Equal(["New Image", "Add New Layer", "Speech Bubble"], Steps(doc));
    }

    [Fact]
    public void Changing_the_style_while_editing_redraws_the_same_bubble()
    {
        _settings.BubbleStyle = BubbleStyle.Square;
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(100, 60));
        var corner = _tool.Shape(doc)!.Body;
        Assert.Equal(RedPx, Pixel(doc, (int)corner.X, (int)corner.Y));

        _settings.BubbleStyle = BubbleStyle.Oval;
        _tool.Refresh(doc);

        var oval = _tool.Shape(doc)!.Body;
        Assert.Equal(Transparent, Pixel(doc, (int)oval.X, (int)oval.Y)); // ellipse corners are empty
        Assert.Equal(["New Image", "Add New Layer", "Speech Bubble"], Steps(doc));
    }

    [Theory]
    [InlineData(BubbleStyle.Square)]
    [InlineData(BubbleStyle.Rounded)]
    [InlineData(BubbleStyle.Oval)]
    [InlineData(BubbleStyle.Thought)]
    public void Every_style_has_an_outline_a_filled_inside_and_a_tail(BubbleStyle style)
    {
        _settings.BubbleStyle = style;
        var doc = NewDoc();
        Drag(doc, P(20, 130), P(120, 50));
        var shape = _tool.Shape(doc)!;

        Assert.Equal(BluePx, Pixel(doc, 120, 50));
        // Coming from the left, the first painted pixel is the outline.
        var x = 0;
        while (Pixel(doc, x, 50)[3] == 0)
            x++;
        Assert.Equal(RedPx, Pixel(doc, x, 50));
        Assert.InRange(x, shape.Extent.X - 1, shape.Body.X + 1);
        // Along the way to the tip, something is drawn outside the body (triangle or trail of circles).
        var drawn = Enumerable.Range(0, 50).Select(i => (X: 120 + (20 - 120) * (0.5 + i * 0.01), Y: 50 + (130 - 50) * (0.5 + i * 0.01)))
            .Count(p => Pixel(doc, (int)p.X, (int)p.Y)[3] != 0);
        Assert.True(drawn >= 2, $"{drawn} tail samples drawn");
    }

    [Fact]
    public void Word_wrap_breaks_between_words_and_inside_too_long_words()
    {
        var style = new TextStyle("", 10, false, false, false, false); // 5 px per character
        var block = new TextBlock(["aaa bbb ccc", "dddddddddd"], style, TextAlignment.Left, new BlockTextRasterizer(),
            new PointD(0, 0), wrapWidth: 40);

        Assert.Equal(["aaa bbb ", "ccc", "dddddddd", "dd"], block.Rows.Select(r => r.Text));
        Assert.Equal(new TextPosition(0, 8), block.PositionAt(new PointD(1, 12))); // start of "ccc"
        Assert.Equal(new PointD(0, 10), block.CaretPoint(new TextPosition(0, 8)));
    }

    /// <summary>Bubbles must produce the same pixels on every OS.</summary>
    [Fact]
    public void Bubbles_are_bit_identical_across_platforms()
    {
        var doc = NewDoc(160, 120);
        _settings.Antialiasing = true;
        _settings.BubbleNumbered = true;
        foreach (var (style, x) in new[] { (BubbleStyle.Square, 30), (BubbleStyle.Rounded, 70), (BubbleStyle.Oval, 110), (BubbleStyle.Thought, 140) })
        {
            _settings.BubbleStyle = style;
            Drag(doc, P(x - 20.3, 110.6), P(x + 0.4, 40.2));
            _tool.OnTextInput(doc, "Hi");
        }

        var hash = Convert.ToHexString(SHA256.HashData(doc.Layers.CurrentUserLayer.Surface.ToBgra()));
        Assert.Equal(ExpectedHash, hash);
    }

    private const string ExpectedHash = "29E7ED4B3BF19D46152A1EE72DE6059EE099468342DA5AB04F03B165BB381B31";
}
