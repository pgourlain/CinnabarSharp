using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Desktop.Tests;

public sealed class SvgToolsUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private SvgDocument Svg => Assert.IsType<SvgDocument>(Vm.ActiveDocument!.Document);

    private void NewDrawing()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(200, 150), ColorBgra.Transparent, new SvgDrawingOptions(200, 150, SvgUnit.Px)));
        Dispatcher.UIThread.RunJobs();
    }

    private void Pick(string name)
    {
        Vm.ShapeStyle = ShapeStyle.OutlineAndFill;
        Vm.SelectedTool = Vm.VectorTools.First(t => t.Name == name);
    }

    private static ToolPointer P(double x, double y, ToolModifiers m = ToolModifiers.None) => new(new PointD(x, y), ToolButton.Left, m);

    private void Drag(double x0, double y0, double x1, double y1)
    {
        Vm.ToolPointerDown(P(x0, y0));
        Vm.ToolPointerMove(P((x0 + x1) / 2, (y0 + y1) / 2));
        Vm.ToolPointerMove(P(x1, y1));
        Vm.ToolPointerUp(P(x1, y1));
        Dispatcher.UIThread.RunJobs();
    }

    private (byte R, byte G, byte B) Pixel(double x, double y) =>
        TestHarness.PixelAt(_h.Capture("svg-tools-probe"), _h.CanvasToWindow((x + 0.5) * Svg.Workspace.Scale, (y + 0.5) * Svg.Workspace.Scale));

    [AvaloniaFact]
    public void A_drawing_gets_the_vector_toolbox_and_images_get_theirs_back()
    {
        Assert.DoesNotContain(Vm.ToolboxTools, t => t.VectorTool is not null);
        NewDrawing();
        Assert.All(Vm.ToolboxTools, t => Assert.True(t.VectorTool is not null || t.Name is "Zoom" or "Pan"));
        Assert.NotNull(Vm.SelectedTool.VectorTool);
        Vm.CreateImage(new NewImageOptions(new ImageSize(20, 20), ColorBgra.White, null));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Vm.ToolboxTools, t => t.Tool is not null);
        Assert.Null(Vm.SelectedTool.VectorTool);
    }

    [AvaloniaFact]
    public void Drawing_a_rectangle_adds_one_object_that_is_painted_and_undoable()
    {
        NewDrawing();
        Vm.PrimaryColor = Avalonia.Media.Color.FromRgb(200, 20, 30);
        Vm.SecondaryColor = Avalonia.Media.Color.FromRgb(10, 120, 240);
        Pick("Rectangle");
        var steps = Svg.History.Items.Count;
        Drag(40, 30, 140, 100);

        Assert.Equal(steps + 1, Svg.History.Items.Count);
        var rect = Assert.Single(Svg.Root.Descendants().OfType<SvgRect>());
        Assert.Equal((40, 30, 100, 70), (rect.X, rect.Y, rect.Width, rect.Height));
        Assert.Contains(rect, Svg.Selection.Nodes);
        var frame = _h.Capture("svg-60-rectangle-drawn");
        var inside = TestHarness.PixelAt(frame, _h.CanvasToWindow(90.5 * Svg.Workspace.Scale, 65.5 * Svg.Workspace.Scale));
        Assert.Equal((10, 120, 240), inside);
        Vm.UndoCommand.Execute(null);
        Assert.Empty(Svg.Root.Descendants().OfType<SvgRect>());
    }

    [AvaloniaFact]
    public void The_select_tool_moves_and_resizes_an_object_in_one_step_each()
    {
        NewDrawing();
        Pick("Rectangle");
        Drag(40, 30, 140, 100);
        var rect = Assert.Single(Svg.Root.Descendants().OfType<SvgRect>());
        Pick("Select");

        var steps = Svg.History.Items.Count;
        Drag(90, 65, 110, 85);                       // move by (20, 20)
        Assert.Equal(steps + 1, Svg.History.Items.Count);
        Assert.Equal((60, 50, 100, 70), (rect.X, rect.Y, rect.Width, rect.Height));

        Drag(160, 120, 180, 140);                    // bottom-right handle: grow by (20, 20)
        Assert.Equal(steps + 2, Svg.History.Items.Count);
        Assert.Equal((60, 50, 120, 90), (rect.X, rect.Y, rect.Width, rect.Height));
        _h.Capture("svg-61-select-resized");

        Vm.UndoCommand.Execute(null);
        Vm.UndoCommand.Execute(null);
        Assert.Equal((40, 30, 100, 70), (rect.X, rect.Y, rect.Width, rect.Height));
    }

    [AvaloniaFact]
    public void The_pen_draws_a_path_finished_with_enter()
    {
        NewDrawing();
        Pick("Pen");
        var steps = Svg.History.Items.Count;
        foreach (var (x, y) in new[] { (30.0, 100.0), (100, 20), (170, 100) })
        {
            Vm.ToolPointerDown(P(x, y));
            Vm.ToolPointerUp(P(x, y));
        }
        Assert.True(Vm.ToolKeyDown(ToolKey.Enter, ToolModifiers.None));
        Dispatcher.UIThread.RunJobs();
        _h.Capture("svg-62-pen-path");

        Assert.Equal(steps + 1, Svg.History.Items.Count);
        var path = Assert.Single(Svg.Root.Descendants().OfType<SvgPath>());
        Assert.Contains("M30", path.GetAttribute("d"));
        Vm.UndoCommand.Execute(null);
        Assert.Empty(Svg.Root.Descendants().OfType<SvgPath>());
    }

    [AvaloniaFact]
    public void Typing_with_the_text_tool_does_not_trigger_tool_shortcuts()
    {
        NewDrawing();
        Pick("Text");
        Vm.ToolPointerDown(P(30, 60));
        Vm.ToolPointerUp(P(30, 60));
        Vm.ToolTextInput("r");
        Assert.True(Vm.IsTyping);
        Vm.ToolKeyDown(ToolKey.Escape, ToolModifiers.None);
        Assert.False(Vm.IsTyping);
        Assert.Equal("r", Assert.Single(Svg.Root.Descendants().OfType<SvgText>()).Content);
    }
}

public sealed class SvgObjectMenuUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private SvgDocument Svg => Assert.IsType<SvgDocument>(Vm.ActiveDocument!.Document);

    private static SvgRect Rect(string id, double x, double y, string fill)
    {
        var rect = new SvgRect { Id = id };
        rect.X = x;
        rect.Y = y;
        rect.Width = 60;
        rect.Height = 60;
        rect.SetAttribute("fill", fill);
        return rect;
    }

    private void Setup()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(200, 150), ColorBgra.Transparent, new SvgDrawingOptions(200, 150, SvgUnit.Px)));
        var root = Svg.Root;
        Svg.Actions.AddNode(Rect("a", 20, 20, "#cc2200"));
        Svg.Actions.AddNode(Rect("b", 50, 40, "#0033cc"));
        Dispatcher.UIThread.RunJobs();
        Svg.Selection.Set(root.Descendants().OfType<SvgRect>());
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Union_from_the_path_menu_merges_the_selection()
    {
        Setup();
        Assert.True(Vm.ApplyPathOperationCommand.CanExecute(PathOperation.Union));
        Vm.ApplyPathOperationCommand.Execute(PathOperation.Union);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(Svg.Root.Descendants().OfType<SvgShape>());
        Assert.IsType<SvgPath>(Svg.Root.Descendants().OfType<SvgShape>().Single());
        _h.Capture("svg-70-union");
        Vm.UndoCommand.Execute(null);
        Assert.Equal(2, Svg.Root.Descendants().OfType<SvgRect>().Count());
    }

    [AvaloniaFact]
    public void Align_follows_the_chosen_reference_and_flip_and_rotate_work()
    {
        Setup();
        Vm.SetAlignRelativeToCommand.Execute(AlignRelativeTo.Page);
        Vm.AlignObjectsCommand.Execute(AlignEdge.Right);
        var rects = Svg.Root.Descendants().OfType<SvgRect>().ToList();
        Assert.All(rects, r => Assert.Equal(200, r.X + r.Width, 1e-6));
        Vm.RotateObjectsClockwiseCommand.Execute(null);
        Vm.FlipObjectsHorizontalCommand.Execute(null);
        Assert.Equal("Flip Horizontal", Svg.History.Items[^1].Text);
        Assert.Equal("Rotate 90° Clockwise", Svg.History.Items[^2].Text);
    }
}

public sealed class SvgBitmapUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private SvgDocument Svg => Assert.IsType<SvgDocument>(Vm.Documents.Select(d => d.Document).OfType<SvgDocument>().Single());

    [AvaloniaFact]
    public async Task Import_then_edit_bitmap_in_a_tab_and_send_the_pixels_back()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(200, 150), ColorBgra.Transparent, new SvgDrawingOptions(200, 150, SvgUnit.Px)));
        Dispatcher.UIThread.RunJobs();
        _h.Dialogs.FilesToOpen.Enqueue([TestHarness.SampleImage]);
        Assert.True(Vm.ImportPictureCommand.CanExecute(null));
        await Vm.ImportPictureCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var image = Assert.Single(Svg.Root.Descendants().OfType<SvgImage>());
        Assert.Contains(image, Svg.Selection.Nodes);
        Assert.True(Vm.HasImageObjectSelected);
        _h.Capture("svg-80-imported-picture");

        await Vm.EditBitmapCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var raster = Assert.IsType<ImageDocument>(Vm.ActiveDocument!.Document);
        Assert.NotNull(raster.BitmapEdit);
        Assert.True(Vm.CanUpdateDrawing);

        var href = image.Href;
        raster.Actions.FillSelection(ColorBgra.FromBgra(10, 200, 30, 255));
        Vm.UpdateDrawingCommand.Execute(null);
        Assert.NotEqual(href, image.Href);
        Assert.Equal("Edit Bitmap", Svg.History.Items[^1].Text);
    }
}
