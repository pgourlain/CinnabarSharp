using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;

namespace CinnabarSharp.Core.Tests;

public class SelectionMaskTests
{
    private static string Draw(SelectionMask m) =>
        string.Join("/", Enumerable.Range(0, m.Height).Select(y =>
            new string(Enumerable.Range(0, m.Width).Select(x => m.Contains(x, y) ? '#' : '.').ToArray())));

    [Fact]
    public void Rectangle_from_any_corner_order_and_clipped()
    {
        var a = SelectionMask.Rectangle(5, 4, new PointD(1, 1), new PointD(3, 3));
        var b = SelectionMask.Rectangle(5, 4, new PointD(3, 3), new PointD(1, 1));
        var clipped = SelectionMask.Rectangle(5, 4, new PointD(-2, 2), new PointD(9, 9));

        Assert.Equal("...../.##../.##../.....", Draw(a));
        Assert.Equal(Draw(a), Draw(b));
        Assert.Equal(new RectangleI(1, 1, 2, 2), a.Bounds);
        Assert.Equal("...../...../#####/#####", Draw(clipped));
    }

    [Fact]
    public void Ellipse_is_symmetric_and_inside_its_box()
    {
        var e = SelectionMask.Ellipse(7, 7, new PointD(0, 0), new PointD(7, 7));

        Assert.Equal("..###../.#####./#######/#######/#######/.#####./..###..", Draw(e));
    }

    [Fact]
    public void Polygon_uses_pixel_centers()
    {
        var triangle = SelectionMask.Polygon(4, 4, [new PointD(0, 0), new PointD(4, 0), new PointD(0, 4)]);

        Assert.Equal("###./##../#.../....", Draw(triangle));
    }

    [Fact]
    public void Combine_modes()
    {
        var a = SelectionMask.Rectangle(4, 1, new PointD(0, 0), new PointD(2, 1));
        var b = SelectionMask.Rectangle(4, 1, new PointD(1, 0), new PointD(3, 1));

        Assert.Equal(".##.", Draw(a.Combine(b, SelectionMode.Replace)));
        Assert.Equal("###.", Draw(a.Combine(b, SelectionMode.Union)));
        Assert.Equal("#...", Draw(a.Combine(b, SelectionMode.Exclude)));
        Assert.Equal("#.#.", Draw(a.Combine(b, SelectionMode.Xor)));
        Assert.Equal(".#..", Draw(a.Combine(b, SelectionMode.Intersect)));
        Assert.Equal("..##", Draw(a.Invert()));
    }

    [Fact]
    public void Offset_moves_and_drops_outside_pixels()
    {
        var a = SelectionMask.Rectangle(4, 2, new PointD(0, 0), new PointD(2, 1));

        Assert.Equal("..../.##.", Draw(a.Offset(1, 1)));
        Assert.Equal("#.../....", Draw(a.Offset(-1, 0)));
    }

    [Fact]
    public void Magic_wand_contiguous_vs_global_and_tolerance()
    {
        // red red blue red ; red is 255,0,0 ; a darker red 200,0,0 at the end
        byte[] px =
        [
            0, 0, 255, 255, 0, 0, 255, 255, 255, 0, 0, 255, 0, 0, 255, 255, 0, 0, 200, 255,
        ];

        Assert.Equal("##...", Draw(SelectionMask.MagicWand(px, 5, 1, new PointI(0, 0), 0, global: false)));
        Assert.Equal("##.#.", Draw(SelectionMask.MagicWand(px, 5, 1, new PointI(0, 0), 0, global: true)));
        Assert.Equal("##.##", Draw(SelectionMask.MagicWand(px, 5, 1, new PointI(0, 0), 20, global: true)));
    }

    [Fact]
    public void Outline_of_a_rectangle_is_four_edges()
    {
        var a = SelectionMask.Rectangle(5, 5, new PointD(1, 1), new PointD(4, 3));

        var outline = a.GetOutline().OrderBy(s => s).ToList();

        Assert.Equal([(1, 1, 1, 3), (1, 1, 4, 1), (1, 3, 4, 3), (4, 1, 4, 3)], outline);
    }
}

public sealed class SelectionToolsTests : BaseTests
{
    private readonly IServiceProvider _sp;
    private readonly ToolSettings _settings = new();

    public SelectionToolsTests() => _sp = CinnabarSharpService();

    private ImageDocument NewDoc(int w = 10, int h = 8) =>
        _sp.GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(w, h), ColorBgra.White);

    private static void Drag(ITool tool, ImageDocument doc, PointD from, PointD to,
        ToolModifiers mods = ToolModifiers.None, ToolButton button = ToolButton.Left)
    {
        tool.OnPointerDown(doc, new ToolPointer(from, button, mods));
        tool.OnPointerMove(doc, new ToolPointer(new PointD((from.X + to.X) / 2, (from.Y + to.Y) / 2), button, mods));
        tool.OnPointerMove(doc, new ToolPointer(to, button, mods));
        tool.OnPointerUp(doc, new ToolPointer(to, button, mods));
    }

    [Fact]
    public void Rectangle_select_records_one_history_step()
    {
        var doc = NewDoc();

        Drag(new RectangleSelectTool(_settings), doc, new PointD(1, 1), new PointD(5, 4));

        Assert.Equal(new RectangleI(1, 1, 4, 3), doc.Selection!.Bounds);
        Assert.Equal(["New Image", "Rectangle Select"], doc.Workspace.History.Items.Select(i => i.Text));
        doc.Workspace.History.Undo();
        Assert.Null(doc.Selection);
    }

    [Fact]
    public void Modifiers_change_the_mode_and_click_deselects()
    {
        var doc = NewDoc(40, 30);
        var tool = new RectangleSelectTool(_settings);
        Drag(tool, doc, new PointD(0, 0), new PointD(4, 4));

        Drag(tool, doc, new PointD(2, 2), new PointD(6, 6), ToolModifiers.Command);
        Assert.Equal(new RectangleI(0, 0, 6, 6), doc.Selection!.Bounds);

        Drag(tool, doc, new PointD(0, 0), new PointD(6, 3), button: ToolButton.Right);
        Assert.Equal(new RectangleI(0, 3, 6, 3), doc.Selection!.Bounds);

        // Far from the last shape's resize handles.
        Drag(tool, doc, new PointD(30, 25), new PointD(30, 25));
        Assert.Null(doc.Selection);
    }

    [Fact]
    public void Handles_resize_the_last_shape_combined_with_the_previous_selection()
    {
        var doc = NewDoc(100, 80);
        var tool = new RectangleSelectTool(_settings);
        Drag(tool, doc, new PointD(10, 10), new PointD(30, 30));
        Drag(tool, doc, new PointD(50, 20), new PointD(70, 40), ToolModifiers.Command);
        Assert.Equal(8, tool.GetOverlay(doc)!.Handles.Count);
        Assert.True(tool.GetOverlay(doc)!.SquareHandles);
        Assert.Equal(ToolCursor.ResizeHorizontal, tool.CursorAt(doc, new PointD(70, 30)));
        Assert.Equal(ToolCursor.Default, tool.CursorAt(doc, new PointD(60, 30)));

        // Right edge of the second rectangle, from x = 70 to 90: the first rectangle stays selected.
        Drag(tool, doc, new PointD(70, 30), new PointD(90, 30));
        Assert.Equal(new RectangleI(10, 10, 80, 30), doc.Selection!.Bounds);
        Assert.True(doc.Selection.Contains(15, 15));
        Assert.True(doc.Selection.Contains(85, 25));
        Assert.False(doc.Selection.Contains(40, 25));

        // Top-left corner moved past the bottom-right one: the box flips.
        Drag(tool, doc, new PointD(50, 20), new PointD(95, 45));
        Assert.Equal(new RectangleI(10, 10, 85, 35), doc.Selection!.Bounds);
        Assert.True(doc.Selection.Contains(92, 42));

        Assert.Equal(["New Image", "Rectangle Select", "Rectangle Select", "Rectangle Select", "Rectangle Select"],
            doc.Workspace.History.Items.Select(i => i.Text));
        doc.Workspace.History.Undo();
        Assert.Equal(new RectangleI(10, 10, 80, 30), doc.Selection!.Bounds);
        Assert.Null(tool.GetOverlay(doc)); // the undone selection is no longer the tool's shape

        doc.Actions.SelectAll();
        Assert.Null(tool.GetOverlay(doc));
    }

    [Fact]
    public void Magic_wand_selects_similar_pixels_of_current_layer()
    {
        var doc = NewDoc(4, 1);
        doc.Layers[0].Surface = Utility.FromBgra([0, 0, 255, 255, 0, 0, 255, 255, 255, 0, 0, 255, 0, 0, 255, 255], 4, 1);

        new MagicWandTool(_settings).OnPointerDown(doc, new ToolPointer(new PointD(0.5, 0.5), ToolButton.Left, ToolModifiers.None));
        Assert.Equal(new RectangleI(0, 0, 2, 1), doc.Selection!.Bounds);

        new MagicWandTool(_settings).OnPointerDown(doc, new ToolPointer(new PointD(0.5, 0.5), ToolButton.Left, ToolModifiers.Shift));
        Assert.Equal(new RectangleI(0, 0, 4, 1), doc.Selection!.Bounds);
        Assert.False(doc.Selection.Contains(2, 0));
    }

    [Fact]
    public void Move_selection_shifts_outline_only()
    {
        var doc = NewDoc();
        doc.Actions.SelectAll();
        Drag(new RectangleSelectTool(_settings), doc, new PointD(1, 1), new PointD(3, 3));
        var pixels = doc.Layers[0].Surface.ToBgra();

        Drag(new MoveSelectionTool(), doc, new PointD(2, 2), new PointD(5, 4));

        Assert.Equal(new RectangleI(4, 3, 2, 2), doc.Selection!.Bounds);
        Assert.Equal(pixels, doc.Layers[0].Surface.ToBgra());
    }

    [Fact]
    public void Move_selected_pixels_moves_pixels_and_leaves_transparency()
    {
        var doc = NewDoc(4, 1);
        doc.Layers[0].Surface = Utility.FromBgra([0, 0, 255, 255, 0, 255, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0], 4, 1);
        Drag(new RectangleSelectTool(_settings), doc, new PointD(0, 0), new PointD(1, 1));

        Drag(new MoveSelectedPixelsTool(), doc, new PointD(0.5, 0.5), new PointD(2.5, 0.5));

        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0 }, doc.Layers[0].Surface.ToBgra());
        Assert.Equal(new RectangleI(2, 0, 1, 1), doc.Selection!.Bounds);
        Assert.Equal("Move Selected Pixels", doc.Workspace.History.Items[^1].Text);

        doc.Workspace.History.Undo();
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0 }, doc.Layers[0].Surface.ToBgra());
        Assert.Equal(new RectangleI(0, 0, 1, 1), doc.Selection!.Bounds);
    }

    [Fact]
    public void Clicking_move_tool_without_dragging_changes_nothing()
    {
        var doc = NewDoc();
        var before = doc.Layers[0].Surface;

        Drag(new MoveSelectedPixelsTool(), doc, new PointD(2, 2), new PointD(2, 2));

        Assert.Same(before, doc.Layers[0].Surface);
        Assert.Single(doc.Workspace.History.Items);
    }
}

public sealed class SelectionActionsTests : BaseTests
{
    private readonly IServiceProvider _sp;

    public SelectionActionsTests() => _sp = CinnabarSharpService();

    private ImageDocument NewDoc(int w, int h) =>
        _sp.GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(w, h), ColorBgra.FromBgra(0, 0, 255, 255));

    [Fact]
    public void Copy_crops_to_selection_and_clears_unselected_pixels()
    {
        var doc = NewDoc(6, 6);
        doc.SetSelection(SelectionMask.Polygon(6, 6, [new PointD(1, 1), new PointD(5, 1), new PointD(1, 5)]));

        var image = doc.Actions.Copy();

        Assert.Equal((3, 3), (image.Width, image.Height));
        Assert.Equal(255, image.Bgra[3]);
        Assert.Equal(0, image.Bgra[(2 * 3 + 2) * 4 + 3]);
    }

    [Fact]
    public void Cut_then_paste_into_new_layer_restores_pixels()
    {
        var doc = NewDoc(6, 4);
        doc.SetSelection(SelectionMask.Rectangle(6, 4, new PointD(1, 1), new PointD(3, 3)));

        var image = doc.Actions.Cut();
        Assert.Equal(0, doc.Layers[0].Surface.ToBgra()[(1 * 6 + 1) * 4 + 3]);

        var layer = doc.Actions.PasteIntoNewLayer(image, new PointI(1, 1));
        using var flat = doc.GetFlattenedImage();
        Assert.Equal(255, flat.ToBgra()[(1 * 6 + 1) * 4 + 3]);
        Assert.Equal(new RectangleI(1, 1, 2, 2), doc.Selection!.Bounds);
        Assert.Same(layer, doc.Layers.CurrentUserLayer);
    }

    [Fact]
    public void Crop_to_selection_resizes_image_and_undo_restores()
    {
        var doc = NewDoc(8, 6);
        doc.Actions.AddNewLayer();
        doc.SetSelection(SelectionMask.Rectangle(8, 6, new PointD(2, 1), new PointD(7, 4)));

        doc.Actions.CropToSelection();

        Assert.Equal(new ImageSize(5, 3), doc.ImageSize);
        Assert.All(doc.Layers.UserLayers, l => Assert.Equal((5u, 3u), (l.Surface.Width, l.Surface.Height)));
        Assert.Null(doc.Selection);

        doc.Workspace.History.Undo();
        Assert.Equal(new ImageSize(8, 6), doc.ImageSize);
        Assert.Equal(new RectangleI(2, 1, 5, 3), doc.Selection!.Bounds);
    }

    [Fact]
    public void Fill_and_erase_only_touch_selected_pixels()
    {
        var doc = NewDoc(4, 1);
        doc.SetSelection(SelectionMask.Rectangle(4, 1, new PointD(0, 0), new PointD(2, 1)));

        doc.Actions.FillSelection(ColorBgra.FromBgra(255, 0, 0, 255));
        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255, 0, 0, 255, 255 }, doc.Layers[0].Surface.ToBgra());

        doc.Actions.EraseSelection();
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 0, 0, 255, 255 }, doc.Layers[0].Surface.ToBgra());
    }

    [Fact]
    public void Invert_with_no_selection_selects_everything()
    {
        var doc = NewDoc(3, 2);

        doc.Actions.InvertSelection();

        Assert.Equal(new RectangleI(0, 0, 3, 2), doc.Selection!.Bounds);
        doc.Actions.DeselectAll();
        Assert.Null(doc.Selection);
        Assert.Equal(["New Image", "Invert Selection", "Deselect All"], doc.Workspace.History.Items.Select(i => i.Text));
    }
}
