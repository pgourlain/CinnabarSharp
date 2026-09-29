using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Core.Tests;

/// <summary>Draws every character as a solid block 0.5 × size wide and size tall, so tests don't depend on fonts.</summary>
public sealed class BlockTextRasterizer : ITextRasterizer
{
    public const int Margin = 2;

    public TextRaster RenderLine(string text, TextStyle style)
    {
        var width = (int)Math.Ceiling(MeasureWidth(text, style)) + 2 * Margin;
        var height = (int)Math.Ceiling(LineHeight(style)) + 2 * Margin;
        var coverage = new byte[width * height];
        var charWidth = style.Size / 2;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
                continue;
            for (var y = 0; y < (int)style.Size; y++)
                for (var x = (int)(i * charWidth); x < (int)((i + 1) * charWidth) - 1; x++)
                    coverage[(y + Margin) * width + x + Margin] = 255;
        }
        return new TextRaster(coverage, width, height, Margin, Margin);
    }

    public double MeasureWidth(string text, TextStyle style) => text.Length * style.Size / 2;

    public double LineHeight(TextStyle style) => style.Size;
}

public sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; set; }
    public Task SetImageAsync(ClipboardImage image) => Task.CompletedTask;
    public Task<ClipboardImage?> GetImageAsync() => Task.FromResult<ClipboardImage?>(null);

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync() => Task.FromResult(Text);
}

public sealed class BrushAndTextToolsTests : BaseTests
{
    private static readonly ColorBgra Red = ColorBgra.FromBgra(0, 0, 255, 255);
    private static readonly ColorBgra Blue = ColorBgra.FromBgra(255, 0, 0, 255);
    private static readonly byte[] White = [255, 255, 255, 255];
    private static readonly byte[] RedPx = [0, 0, 255, 255];
    private static readonly byte[] BluePx = [255, 0, 0, 255];

    private readonly IServiceProvider _sp;
    private readonly ToolSettings _settings = new() { BrushWidth = 4, Antialiasing = false };

    public BrushAndTextToolsTests()
    {
        _sp = CinnabarSharpService();
        _settings.PrimaryColor = Red;
        _settings.SecondaryColor = Blue;
    }

    private ImageDocument NewDoc(int w = 40, int h = 20) =>
        _sp.GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(w, h), ColorBgra.White);

    private static byte[] Pixel(ImageDocument doc, int x, int y) =>
        doc.Layers.CurrentUserLayer.Surface.ReadRegion(new RectangleI(x, y, 1, 1));

    private static ToolPointer P(double x, double y, ToolModifiers mods = ToolModifiers.None, double pressure = 1,
        ToolButton button = ToolButton.Left) => new(new PointD(x, y), button, mods, pressure);

    private static void Drag(ITool tool, ImageDocument doc, params ToolPointer[] points)
    {
        tool.OnPointerDown(doc, points[0]);
        foreach (var p in points.Skip(1))
            tool.OnPointerMove(doc, p);
        tool.OnPointerUp(doc, points[^1]);
    }

    private static IEnumerable<string> Steps(ImageDocument doc) =>
        doc.Workspace.History.Items.Take(doc.Workspace.History.Pointer + 1).Select(i => i.Text);

    // ---- Brushes ----

    [Fact]
    public void Soft_brush_fades_towards_the_edge()
    {
        var hard = new CoverageMask(21, 21);
        hard.Disc(10.5, 10.5, 8, antialias: true, hardness: 1);
        var soft = new CoverageMask(21, 21);
        soft.Disc(10.5, 10.5, 8, antialias: true, hardness: 0);

        Assert.Equal(255, soft[10, 10]);
        Assert.Equal(255, hard[15, 10]);
        Assert.InRange(soft[15, 10], 1, 200);
        Assert.True(soft[16, 10] < soft[14, 10]);
        Assert.Equal(0, soft[19, 10]);
    }

    [Fact]
    public void Pen_pressure_scales_the_brush_width()
    {
        _settings.BrushWidth = 10;
        var full = NewDoc();
        Drag(new PaintbrushTool(_settings), full, P(5, 10), P(35, 10));
        var light = NewDoc();
        Drag(new PaintbrushTool(_settings), light, P(5, 10, pressure: 0.3), P(35, 10, pressure: 0.3));

        Assert.Equal(RedPx, Pixel(full, 20, 14));
        Assert.Equal(White, Pixel(light, 20, 14));
        Assert.Equal(RedPx, Pixel(light, 20, 10));
    }

    [Fact]
    public void Clone_stamp_copies_from_the_source_point()
    {
        var doc = NewDoc();
        _settings.BrushWidth = 4;
        Drag(new PaintbrushTool(_settings), doc, P(5, 5), P(5, 5));
        Assert.Equal(RedPx, Pixel(doc, 5, 5));
        var tool = new CloneStampTool(_settings);

        Drag(tool, doc, P(30, 5));
        Assert.Equal(White, Pixel(doc, 30, 5)); // No source yet: nothing painted.
        Assert.Equal(["New Image", "Paintbrush"], Steps(doc));

        Drag(tool, doc, P(5, 5, ToolModifiers.Command));
        Drag(tool, doc, P(30, 10), P(31, 10));

        Assert.Equal(RedPx, Pixel(doc, 30, 10));
        Assert.Equal(White, Pixel(doc, 30, 5));
        Assert.Equal(["New Image", "Paintbrush", "Clone Stamp"], Steps(doc));
        Assert.Equal(new PointD(5, 5), tool.GetOverlay(doc)!.Handles[0]);
    }

    [Fact]
    public void Recolor_replaces_only_colors_close_to_the_secondary_color()
    {
        var doc = NewDoc();
        _settings.BrushWidth = 6;
        Drag(new PaintbrushTool(_settings), doc, P(10, 10, button: ToolButton.Right), P(10, 10, button: ToolButton.Right));
        Assert.Equal(BluePx, Pixel(doc, 10, 10));
        _settings.Tolerance = 10;

        Drag(new RecolorTool(_settings), doc, P(4, 10), P(20, 10));

        Assert.Equal(RedPx, Pixel(doc, 10, 10)); // Blue (secondary) became red (primary).
        Assert.Equal(White, Pixel(doc, 16, 10)); // White is not close to blue: untouched.
    }

    [Fact]
    public void Gradient_transparency_mode_fades_alpha_and_keeps_colors()
    {
        var doc = NewDoc(40, 4);
        _settings.GradientTransparency = true;

        Drag(new GradientTool(_settings), doc, P(0, 2), P(40, 2));

        var start = Pixel(doc, 0, 2);
        var middle = Pixel(doc, 20, 2);
        var end = Pixel(doc, 39, 2);
        Assert.Equal(new byte[] { 255, 255, 255 }, middle[..3]);
        Assert.True(start[3] > 245);
        Assert.InRange(middle[3], 110, 145);
        Assert.True(end[3] < 10);
    }

    // ---- Shapes ----

    [Fact]
    public void Rounded_rectangle_has_empty_corners()
    {
        var fill = new CoverageMask(20, 20);
        fill.FillRoundedRectangle(new PointD(0, 0), new PointD(20, 20), 6, antialias: false);
        Assert.Equal(0, fill[0, 0]);
        Assert.Equal(255, fill[10, 0]);
        Assert.Equal(255, fill[10, 10]);

        var square = new CoverageMask(20, 20);
        square.FillRoundedRectangle(new PointD(0, 0), new PointD(20, 20), 0, antialias: false);
        Assert.Equal(255, square[0, 0]);

        var stroke = new CoverageMask(20, 20);
        stroke.StrokeRoundedRectangle(new PointD(0, 0), new PointD(20, 20), 6, 1, antialias: false);
        Assert.Equal(0, stroke[0, 0]);
        Assert.Equal(255, stroke[10, 0]);
        Assert.Equal(0, stroke[10, 10]);
    }

    // ---- Line / Curve ----

    [Fact]
    public void Line_stays_editable_and_bends_into_a_curve_in_one_history_step()
    {
        var doc = NewDoc();
        var tool = new LineTool(_settings);
        Drag(tool, doc, P(2, 10), P(38, 10));
        Assert.True(tool.IsEditing(doc));
        Assert.Equal(RedPx, Pixel(doc, 20, 10));
        var control1 = tool.Points[1];
        var control2 = tool.Points[2];

        // Pull both control points up: the middle of the curve moves up.
        Drag(tool, doc, P(control1.X, control1.Y), P(control1.X, 0));
        Drag(tool, doc, P(control2.X, control2.Y), P(control2.X, 0));

        Assert.Equal(White, Pixel(doc, 20, 10));
        Assert.Equal(RedPx, Pixel(doc, 20, 2));
        Assert.Equal(["New Image", "Line / Curve"], Steps(doc));
        Assert.Equal(4, tool.GetOverlay(doc)!.Handles.Count);

        doc.Workspace.History.Undo();
        Assert.Equal(White, Pixel(doc, 20, 2));
        Assert.False(tool.IsEditing(doc));
        doc.Workspace.History.Redo();
        Assert.Equal(RedPx, Pixel(doc, 20, 2));
    }

    [Fact]
    public void Enter_finishes_the_line_and_the_next_drag_starts_a_new_one()
    {
        var doc = NewDoc();
        var tool = new LineTool(_settings);
        Drag(tool, doc, P(2, 5), P(38, 5));

        Assert.True(tool.OnKeyDown(doc, ToolKey.Enter, ToolModifiers.None));
        Assert.False(tool.IsEditing(doc));
        Assert.Null(tool.GetOverlay(doc));

        Drag(tool, doc, P(2, 5), P(2, 15)); // Starts at the old end point: a new line, not a handle.
        Assert.Equal(["New Image", "Line / Curve", "Line / Curve"], Steps(doc));
        Assert.Equal(RedPx, Pixel(doc, 20, 5));
        Assert.Equal(RedPx, Pixel(doc, 2, 12));
    }

    [Fact]
    public void Any_other_edit_ends_line_editing()
    {
        var doc = NewDoc();
        var tool = new LineTool(_settings);
        Drag(tool, doc, P(2, 5), P(38, 5));

        doc.Actions.SelectAll();

        Assert.False(tool.IsEditing(doc));
    }

    [Fact]
    public void Changing_the_color_redraws_the_line_being_edited()
    {
        var doc = NewDoc();
        var tool = new LineTool(_settings);
        Drag(tool, doc, P(2, 5), P(38, 5));

        _settings.PrimaryColor = Blue;
        tool.Refresh(doc);

        Assert.Equal(BluePx, Pixel(doc, 20, 5));
        Assert.Equal(["New Image", "Line / Curve"], Steps(doc));
        doc.Workspace.History.Undo();
        Assert.Equal(White, Pixel(doc, 20, 5));
    }

    // ---- Text ----

    private (TextTool Tool, ImageDocument Doc) StartText(double x = 2, double y = 2)
    {
        _settings.FontSize = 8;
        var doc = NewDoc(60, 30);
        var tool = new TextTool(_settings, new BlockTextRasterizer());
        Drag(tool, doc, P(x, y));
        return (tool, doc);
    }

    [Fact]
    public void Typing_paints_text_as_one_history_step_updated_while_editing()
    {
        var (tool, doc) = StartText();
        Assert.True(tool.IsTyping(doc));
        Assert.Equal(["New Image"], Steps(doc));

        tool.OnTextInput(doc, "A");
        tool.OnTextInput(doc, "B");

        // Each block is 4 px wide (3 painted) and 8 px tall, starting at the click point.
        Assert.Equal(RedPx, Pixel(doc, 2, 2));
        Assert.Equal(RedPx, Pixel(doc, 7, 9));
        Assert.Equal(White, Pixel(doc, 5, 2));
        Assert.Equal(White, Pixel(doc, 2, 10));
        Assert.Equal(["New Image", "Text"], Steps(doc));
        Assert.Equal("AB", tool.Engine.ToString());

        tool.OnKeyDown(doc, ToolKey.Backspace, ToolModifiers.None);
        Assert.Equal(White, Pixel(doc, 7, 2));
        Assert.Equal(["New Image", "Text"], Steps(doc));

        doc.Workspace.History.Undo();
        Assert.Equal(White, Pixel(doc, 2, 2));
    }

    [Fact]
    public void Enter_adds_a_line_and_alignment_moves_lines()
    {
        var (tool, doc) = StartText();
        tool.OnTextInput(doc, "AAAA");
        tool.OnKeyDown(doc, ToolKey.Enter, ToolModifiers.None);
        tool.OnTextInput(doc, "B");

        Assert.Equal(RedPx, Pixel(doc, 2, 10)); // Second line, left-aligned.

        _settings.TextAlignment = TextAlignment.Right;
        tool.Refresh(doc);

        Assert.Equal(White, Pixel(doc, 2, 10));
        Assert.Equal(RedPx, Pixel(doc, 14, 10)); // Right edge of the 16-px-wide block.
        Assert.Equal(["New Image", "Text"], Steps(doc));
    }

    [Fact]
    public void Escape_or_click_outside_finishes_the_text()
    {
        var (tool, doc) = StartText();
        tool.OnTextInput(doc, "A");

        Assert.True(tool.OnKeyDown(doc, ToolKey.Escape, ToolModifiers.None));
        Assert.False(tool.IsEditing(doc));
        tool.OnTextInput(doc, "B");
        Assert.Equal(White, Pixel(doc, 6, 2));

        Drag(tool, doc, P(30, 15));
        tool.OnTextInput(doc, "C");
        Assert.Equal(RedPx, Pixel(doc, 30, 15));
        Assert.Equal(["New Image", "Text", "Text"], Steps(doc));
    }

    [Fact]
    public void Clicking_inside_the_text_moves_the_caret()
    {
        var (tool, doc) = StartText();
        tool.OnTextInput(doc, "ABCD");

        Drag(tool, doc, P(2 + 8.2, 5)); // Between "AB" and "CD".
        tool.OnTextInput(doc, "x");

        Assert.Equal("ABxCD", tool.Engine.ToString());
        Assert.Equal(["New Image", "Text"], Steps(doc));
    }

    [Fact]
    public void Dragging_the_handle_moves_the_text()
    {
        var (tool, doc) = StartText();
        tool.OnTextInput(doc, "A");
        var handle = tool.GetOverlay(doc)!.Handles[0];
        Assert.Equal(new PointD(8, 12), handle);

        Drag(tool, doc, P(handle.X, handle.Y), P(handle.X + 20, handle.Y + 10));

        Assert.Equal(White, Pixel(doc, 2, 2));
        Assert.Equal(RedPx, Pixel(doc, 22, 12));
        Assert.Equal("A", tool.Engine.ToString());
        Assert.Equal(["New Image", "Text"], Steps(doc));
    }

    [Fact]
    public async Task Text_copy_cut_and_paste_use_the_clipboard_service()
    {
        var (tool, doc) = StartText();
        var clipboard = new FakeClipboard();
        tool.OnTextInput(doc, "Hello");
        tool.SelectAll(doc);

        await tool.Cut(doc, clipboard);
        Assert.Equal("Hello", clipboard.Text);
        Assert.Equal("", tool.Engine.ToString());

        clipboard.Text = "one\r\ntwo";
        await tool.Paste(doc, clipboard);
        Assert.Equal(["one", "two"], tool.Engine.Lines);

        tool.OnKeyDown(doc, ToolKey.Left, ToolModifiers.Shift);
        await tool.Copy(clipboard);
        Assert.Equal("o", clipboard.Text);
    }

    [Fact]
    public void Text_overlay_shows_frame_caret_and_selection()
    {
        var (tool, doc) = StartText();
        tool.OnTextInput(doc, "AB");
        tool.OnKeyDown(doc, ToolKey.Left, ToolModifiers.Shift);

        var overlay = tool.GetOverlay(doc)!;

        Assert.Equal(new RectangleD(0, 0, 12, 12), overlay.Frame);
        Assert.Equal((new PointD(6, 2), new PointD(6, 10)), overlay.Lines[0]);
        Assert.Equal(new RectangleD(6, 2, 4, 8), Assert.Single(overlay.Highlights));
    }

    [Fact]
    public void Rotate_handle_sits_at_the_top_right_corner_unrotated_at_first()
    {
        var (tool, doc) = StartText();
        tool.OnTextInput(doc, "A");

        var overlay = tool.GetOverlay(doc)!;

        // Bounds are (2, 2, 4, 8) for one block-glyph; the handle sits just outside its top-right corner.
        Assert.Equal(new PointD(12, -6), overlay.RotateHandle);
        Assert.Equal((0.0, new PointD(4, 6)), overlay.Rotation); // unrotated; pivot is the bounds' center
    }

    [Fact]
    public void Dragging_the_rotate_handle_sets_the_overlay_angle()
    {
        var (tool, doc) = StartText();
        tool.OnTextInput(doc, "A");
        var pivot = tool.GetOverlay(doc)!.Rotation!.Value.Pivot; // (4, 6)
        var handle = tool.GetOverlay(doc)!.RotateHandle!.Value;  // (12, -6): pointer angle atan2(-12, 8) from pivot

        // Drag the handle a quarter turn clockwise (90°) around the pivot.
        var startAngle = Math.Atan2(handle.Y - pivot.Y, handle.X - pivot.X);
        var quarterTurn = new PointD(pivot.X + (handle.Y - pivot.Y), pivot.Y - (handle.X - pivot.X));
        tool.OnPointerDown(doc, P(handle.X, handle.Y));
        tool.OnPointerMove(doc, P(quarterTurn.X, quarterTurn.Y));
        tool.OnPointerUp(doc, P(quarterTurn.X, quarterTurn.Y));

        var angle = tool.GetOverlay(doc)!.Rotation!.Value.Angle;
        Assert.Equal(-Math.PI / 2, angle, 1e-9);
    }

    [Fact]
    public void Rotating_180_degrees_flips_the_glyph_to_the_opposite_side_of_its_bounds()
    {
        var (tool, doc) = StartText();
        tool.OnTextInput(doc, "A");
        // Before rotating: the block-glyph paints image columns 2..4, rows 2..9 (see the plain typing test).
        Assert.Equal(RedPx, Pixel(doc, 2, 2));
        Assert.Equal(White, Pixel(doc, 5, 2));

        var pivot = tool.GetOverlay(doc)!.Rotation!.Value.Pivot; // (4, 6)
        var handle = tool.GetOverlay(doc)!.RotateHandle!.Value;
        var opposite = new PointD(2 * pivot.X - handle.X, 2 * pivot.Y - handle.Y); // 180° around the pivot
        tool.OnPointerDown(doc, P(handle.X, handle.Y));
        tool.OnPointerMove(doc, P(opposite.X, opposite.Y));
        tool.OnPointerUp(doc, P(opposite.X, opposite.Y));

        Assert.Equal(Math.PI, Math.Abs(tool.GetOverlay(doc)!.Rotation!.Value.Angle), 1e-9);
        // The bounds' center (the pivot) doesn't move; a 4-px-wide box reflected through its own center swaps
        // which side the 3-px glyph sits on: it was columns 2-4, it's now columns 3-5.
        Assert.Equal(White, Pixel(doc, 2, 2));
        Assert.Equal(RedPx, Pixel(doc, 3, 2));
        Assert.Equal(RedPx, Pixel(doc, 5, 9));
    }

    [Fact]
    public void Rotated_text_stays_editable_and_undoable()
    {
        var (tool, doc) = StartText();
        tool.OnTextInput(doc, "A");
        var pivot = tool.GetOverlay(doc)!.Rotation!.Value.Pivot;
        var handle = tool.GetOverlay(doc)!.RotateHandle!.Value;
        var opposite = new PointD(2 * pivot.X - handle.X, 2 * pivot.Y - handle.Y);
        Drag(tool, doc, P(handle.X, handle.Y), P(opposite.X, opposite.Y));

        // Still editable: typing more text keeps updating the same one history step.
        Assert.True(tool.IsTyping(doc));
        tool.OnTextInput(doc, "B");
        Assert.Equal("AB", tool.Engine.ToString());
        Assert.Equal(["New Image", "Text"], Steps(doc));

        doc.Workspace.History.Undo();
        Assert.Equal(White, Pixel(doc, 3, 2));
        Assert.Equal(White, Pixel(doc, 5, 9));
    }

    // ---- Cross-platform ----

    /// <summary>New brush, shape and curve features must produce the same pixels on every OS.</summary>
    [Fact]
    public void New_painting_features_are_bit_identical_across_platforms()
    {
        var doc = NewDoc(64, 48);
        _settings.Antialiasing = true;
        _settings.BrushWidth = 9;
        _settings.Hardness = 30;
        Drag(new PaintbrushTool(_settings), doc, P(3.3, 4.1, pressure: 0.4), P(20.7, 30.2, pressure: 0.9), P(60.1, 10.9));
        _settings.ShapeKind = ShapeKind.RoundedRectangle;
        _settings.ShapeStyle = ShapeStyle.OutlineAndFill;
        _settings.CornerRadius = 7;
        _settings.BrushWidth = 3;
        Drag(new ShapesTool(_settings), doc, P(10.2, 10.6), P(50.4, 40.3));
        var line = new LineTool(_settings);
        Drag(line, doc, P(2, 44), P(62, 44));
        Drag(line, doc, P(line.Points[1].X, line.Points[1].Y), P(20, 20));
        _settings.Tolerance = 40;
        Drag(new RecolorTool(_settings), doc, P(30, 0, button: ToolButton.Right), P(30, 48, button: ToolButton.Right));
        var clone = new CloneStampTool(_settings);
        Drag(clone, doc, P(20, 20, ToolModifiers.Command));
        Drag(clone, doc, P(50, 5), P(60, 30));
        _settings.GradientTransparency = true;
        _settings.GradientKind = GradientKind.Diamond;
        Drag(new GradientTool(_settings), doc, P(32, 24), P(50, 40));

        var hash = Convert.ToHexString(SHA256.HashData(doc.Layers[0].Surface.ToBgra()));
        Assert.Equal(ExpectedHash, hash);
    }

    private const string ExpectedHash = "F40FD7A7681D0E8B7CC0840FB160E0D394FA689B6353B6A49364A5712BE5D033";
}
