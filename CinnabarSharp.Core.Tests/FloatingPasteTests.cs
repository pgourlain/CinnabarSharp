using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Tests;

public class FloatingPasteTests : BaseTests
{
    private readonly IServiceProvider _sp;

    public FloatingPasteTests() => _sp = CinnabarSharpService();

    // Six pixels with a different color each, so a moved or lost pixel can't go unnoticed.
    private static readonly byte[][] Colors =
    [
        [10, 20, 200, 255], [30, 180, 40, 255], [200, 60, 50, 255],
        [90, 90, 10, 255], [120, 30, 160, 255], [20, 140, 140, 255],
    ];

    private static readonly ClipboardImage Pasted = new([1, 2, 3, 255, 4, 5, 6, 255], 2, 1);

    private ImageDocument NewDoc()
    {
        var doc = _sp.GetRequiredService<IWorkspaceService>().NewDocument(new ImageSize(6, 1), ColorBgra.White);
        doc.Layers[0].Surface = Utility.FromBgra(Colors.SelectMany(c => c).ToArray(), 6, 1);
        return doc;
    }

    private static byte[] Expected(params byte[][] pixels) => pixels.SelectMany(p => p).ToArray();

    private static byte[] X => Pasted.Bgra[..4];
    private static byte[] Y => Pasted.Bgra[4..];

    private static void Drag(ImageDocument doc, double from, double to)
    {
        var tool = new MoveSelectedPixelsTool();
        tool.OnPointerDown(doc, new ToolPointer(new PointD(from, 0.5), ToolButton.Left, ToolModifiers.None));
        tool.OnPointerMove(doc, new ToolPointer(new PointD(to, 0.5), ToolButton.Left, ToolModifiers.None));
        tool.OnPointerUp(doc, new ToolPointer(new PointD(to, 0.5), ToolButton.Left, ToolModifiers.None));
    }

    private static byte[] Pixels(ImageDocument doc) => doc.Layers[0].Surface.ToBgra();

    [Fact]
    public void Moving_a_fresh_paste_puts_back_the_pixels_it_covered()
    {
        var doc = NewDoc();
        doc.Actions.Paste(Pasted, new PointI(1, 0));
        Assert.Equal(Expected(Colors[0], X, Y, Colors[3], Colors[4], Colors[5]), Pixels(doc));

        Drag(doc, 1.5, 4.5);

        Assert.Equal(Expected(Colors[0], Colors[1], Colors[2], Colors[3], X, Y), Pixels(doc));
        Assert.Equal(new RectangleI(4, 0, 2, 1), doc.Selection!.Bounds);
    }

    [Fact]
    public void A_floating_paste_can_be_moved_again_and_each_move_undone()
    {
        var doc = NewDoc();
        doc.Actions.Paste(Pasted, new PointI(1, 0));
        Drag(doc, 1.5, 3.5); // to x = 3
        Drag(doc, 3.5, 2.5); // to x = 2

        Assert.Equal(Expected(Colors[0], Colors[1], X, Y, Colors[4], Colors[5]), Pixels(doc));

        doc.Workspace.History.Undo();
        Assert.Equal(Expected(Colors[0], Colors[1], Colors[2], X, Y, Colors[5]), Pixels(doc));
        doc.Workspace.History.Undo();
        Assert.Equal(Expected(Colors[0], X, Y, Colors[3], Colors[4], Colors[5]), Pixels(doc));
        doc.Workspace.History.Redo();
        doc.Workspace.History.Redo();
        Assert.Equal(Expected(Colors[0], Colors[1], X, Y, Colors[4], Colors[5]), Pixels(doc));
    }

    [Fact]
    public void Another_edit_ends_the_floating_paste()
    {
        var doc = NewDoc();
        doc.Actions.Paste(Pasted, new PointI(1, 0));
        Assert.NotNull(doc.Floating);

        doc.Actions.AddNewLayer();
        doc.Layers.SetCurrentUserLayer(0);

        Assert.Null(doc.Floating);
    }

    [Fact]
    public void Undo_then_moving_does_not_use_a_stale_floating_paste()
    {
        var doc = NewDoc();
        doc.Actions.Paste(Pasted, new PointI(1, 0));
        Drag(doc, 1.5, 3.5);
        doc.Workspace.History.Undo();
        doc.Workspace.History.Undo();
        Assert.Equal(Colors.SelectMany(c => c).ToArray(), Pixels(doc));

        // Redo brings the paste back; moving it again must still leave the layer intact.
        doc.Workspace.History.Redo();
        Drag(doc, 1.5, 4.5);

        Assert.Equal(Expected(Colors[0], Colors[1], Colors[2], Colors[3], X, Y), Pixels(doc));
    }

    [Fact]
    public void Selected_pixels_that_were_not_pasted_still_leave_transparency()
    {
        var doc = NewDoc();
        doc.SetSelection(SelectionMask.Rectangle(6, 1, new PointD(0, 0), new PointD(1, 1)));

        Drag(doc, 0.5, 2.5);

        Assert.Equal(Expected([0, 0, 0, 0], Colors[1], Colors[0], Colors[3], Colors[4], Colors[5]), Pixels(doc));
    }
}
