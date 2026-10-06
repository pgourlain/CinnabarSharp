using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Desktop.Tests;

public sealed class SvgPanelsUiTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    private SvgDocument Svg => Assert.IsType<SvgDocument>(Vm.ActiveDocument!.Document);

    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Data", "svg", name);

    private async Task OpenShapes()
    {
        await Vm.OpenFileAsync(Sample("shapes.svg"));
        Dispatcher.UIThread.RunJobs();
    }

    private ObjectViewModel Row(string id) => Vm.Objects.Single(o => o.Node.Id == id);

    private (byte R, byte G, byte B) CanvasPixel(Avalonia.Media.Imaging.WriteableBitmap frame, double x, double y)
    {
        var scale = Svg.Workspace.Scale;
        return TestHarness.PixelAt(frame, _h.CanvasToWindow((x + 0.5) * scale, (y + 0.5) * scale));
    }

    private ListBox ObjectsList => _h.Window.FindControl<ListBox>("ObjectsList")!;

    [AvaloniaFact]
    public async Task The_objects_panel_shows_the_tree_of_the_drawing_top_first()
    {
        await OpenShapes();
        Dispatcher.UIThread.RunJobs();
        _h.Capture("svg-50-objects-panel");

        Assert.Equal(["pg1", "pl1", "l1", "e1", "c1", "r1"], Vm.Objects.Select(o => o.Node.Id));
        Assert.All(Vm.Objects, o => Assert.Equal(0, o.Depth));
        Assert.Equal("rectangle", Row("r1").Kind);
        Assert.Equal("polygon", Row("pg1").Kind);
        Assert.True(Row("r1").IsVisible);
        Assert.False(Row("r1").IsLocked);
        // The panel replaces Layers for drawings.
        Assert.True(Vm.HasSvg);
        Assert.False(Vm.IsNotSvg);
        Assert.NotNull(ObjectsList);
        Assert.Equal(6, ObjectsList.ItemCount);
    }

    [AvaloniaFact]
    public async Task Groups_expand_and_collapse_and_nest_their_children()
    {
        await Vm.OpenFileAsync(Sample("transforms.svg"));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["g1", "e", "d", "g2", "g3", "c", "b", "a"], Vm.Objects.Select(o => o.Node.Id));
        Assert.Equal([0, 1, 1, 1, 2, 3, 2, 1], Vm.Objects.Select(o => o.Depth));
        Row("g2").IsExpanded = false;
        Assert.Equal(["g1", "e", "d", "g2", "a"], Vm.Objects.Select(o => o.Node.Id));
        Row("g2").IsExpanded = true;
        Assert.Equal(8, Vm.Objects.Count);
        Assert.True(Row("g1").HasChildren);
        Assert.False(Row("a").HasChildren);
    }

    [AvaloniaFact]
    public async Task Selecting_in_the_panel_selects_in_the_drawing_and_draws_handles()
    {
        await OpenShapes();
        Row("c1").IsSelected = true;
        Row("r1").IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        var frame = _h.Capture("svg-51-selected");

        Assert.Equal(["c1", "r1"], Svg.Selection.Nodes.Select(n => n.Id));
        Assert.True(Vm.HasObjectSelection);
        Assert.Equal("2 objects", Vm.ObjectSelectionText);
        var overlay = Assert.IsType<CinnabarSharp.Core.Tools.ToolOverlay>(Vm.Overlay);
        Assert.Equal(8, overlay.Handles.Count);
        // The frame is the box around both objects (10,10)-(130,50) in image pixels at 100 %.
        Assert.Equal((10, 10, 120, 40), ((int)overlay.Frame!.Value.X, (int)overlay.Frame.Value.Y, (int)overlay.Frame.Value.Width, (int)overlay.Frame.Value.Height));
        // A handle is drawn: white squares with a dark outline at the top left corner.
        Assert.Equal((255, 255, 255), CanvasPixel(frame, 10, 10));

        Row("c1").IsSelected = false;
        Assert.Equal(["r1"], Svg.Selection.Nodes.Select(n => n.Id));
    }

    [AvaloniaFact]
    public async Task Selection_changes_from_the_drawing_update_the_panel_rows()
    {
        await OpenShapes();
        Svg.Selection.Set(new[] { Svg.Root.FindById("e1")!, Svg.Root.FindById("l1")! }.OfType<SvgElement>());
        Assert.True(Row("e1").IsSelected);
        Assert.True(Row("l1").IsSelected);
        Assert.False(Row("r1").IsSelected);
        Vm.SelectAllCommand.Execute(null);
        Assert.All(Vm.Objects, o => Assert.True(o.IsSelected));
        Svg.Selection.Clear();
        Assert.All(Vm.Objects, o => Assert.False(o.IsSelected));
    }

    [AvaloniaFact]
    public async Task Panel_buttons_edit_through_actions_and_undo()
    {
        await OpenShapes();
        Row("r1").IsSelected = true;
        Vm.DuplicateObjectsCommand.Execute(null);
        Assert.Equal(7, Vm.Objects.Count);
        Assert.Equal(1, Svg.Selection.Count);
        Vm.UndoCommand.Execute(null);
        Assert.Equal(6, Vm.Objects.Count);
        Assert.Equal(["r1"], Svg.Selection.Nodes.Select(n => n.Id));

        Vm.GroupObjectsCommand.Execute(null);
        Assert.Equal("group", Vm.Objects[^2].Kind);                  // above r1, which is now inside it
        Assert.Equal(1, Vm.Objects[^1].Depth);
        Vm.UngroupObjectsCommand.Execute(null);
        Assert.Equal(6, Vm.Objects.Count);

        Vm.RaiseToTopCommand.Execute(null);
        Assert.Equal("r1", Vm.Objects[0].Node.Id);
        Vm.LowerToBottomCommand.Execute(null);
        Assert.Equal("r1", Vm.Objects[^1].Node.Id);

        Vm.DeleteObjectsCommand.Execute(null);
        Assert.Equal(5, Vm.Objects.Count);
        Vm.UndoCommand.Execute(null);
        Assert.Equal(6, Vm.Objects.Count);
        Assert.True(Vm.History.Count > 5);
    }

    [AvaloniaFact]
    public async Task Visibility_and_lock_toggles_are_undoable_steps()
    {
        await OpenShapes();
        var frame = _h.Capture("svg-52-before-hide");
        Assert.Equal((0xe0, 0x30, 0x20), CanvasPixel(frame, 30, 30));

        Row("r1").IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, CanvasPixelAlphaAt(30, 30));
        Assert.False(Row("r1").IsVisible);
        Assert.Equal("Hide", Vm.History[^1].Text);

        Vm.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0xe0, 0x30, 0x20), CanvasPixel(_h.Capture("svg-53-after-undo"), 30, 30));
        Assert.True(Row("r1").IsVisible);

        Row("c1").IsLocked = true;
        Assert.True(Svg.Root.FindById("c1") is SvgElement { IsLocked: true });
        Assert.True(Row("c1").IsLocked);
    }

    private int CanvasPixelAlphaAt(double x, double y)
    {
        // Drawing the checkerboard where nothing is drawn: the pixel is grey, not the red of the rectangle.
        var frame = _h.Capture("svg-alpha");
        var (r, g, b) = CanvasPixel(frame, x, y);
        return r == g && g == b ? 0 : 255;
    }

    [AvaloniaFact]
    public async Task Renaming_sets_the_label_and_the_id_is_checked()
    {
        await OpenShapes();
        Row("r1").BeginEdit();
        Row("r1").EditText = "Red box";
        Row("r1").CommitEdit();
        Assert.Equal("Red box", Row("r1").Name);
        Assert.Equal("Red box", ((SvgElement)Svg.Root.FindById("r1")!).Label);

        Assert.Null(Vm.SetObjectId(Row("r1"), "box"));
        Assert.NotNull(Svg.Root.FindById("box"));
        Assert.NotNull(Vm.SetObjectId(Row("c1"), "box"));          // already used
        Assert.NotNull(Vm.SetObjectId(Row("c1"), "bad id"));
        Row("c1").BeginEdit();
        Row("c1").CancelEdit();
        Assert.Equal("c1", Row("c1").Name);
    }

    [AvaloniaFact]
    public async Task Drag_and_drop_reorders_and_moves_into_groups_in_one_step()
    {
        await Vm.OpenFileAsync(Sample("transforms.svg"));
        Dispatcher.UIThread.RunJobs();
        var before = Vm.History.Count;
        // Drop "e" into group g2 (on top).
        Vm.MoveObject(Row("e"), Row("g2"), MainViewModel.DropPosition.Into);
        Assert.Equal(before + 1, Vm.History.Count);
        Assert.Equal("g2", Svg.Root.FindById("e")!.Parent is SvgElement p ? p.Id : null);
        // The place on the page is kept.
        Assert.Equal(2, Row("g2").Node.Parent is SvgGroup ? 2 : 0);
        Vm.UndoCommand.Execute(null);
        Assert.Equal("g1", ((SvgElement)Svg.Root.FindById("e")!.Parent!).Id);

        // Above/below a sibling changes the z-order only.
        Vm.MoveObject(Row("a"), Row("e"), MainViewModel.DropPosition.Above);
        var g1 = (SvgGroup)Svg.Root.FindById("g1")!;
        Assert.Equal(["g2", "d", "e", "a"], g1.Elements.Select(x => x.Id));
        // Cannot drop a group into itself.
        Vm.MoveObject(Row("g1"), Row("g3"), MainViewModel.DropPosition.Into);
        Assert.Equal("g1", Svg.Root.FindById("g1")!.Id);
        Assert.Same(Svg.Root, Svg.Root.FindById("g1")!.Parent);
    }

    // ---- Properties panel ----

    [AvaloniaFact]
    public async Task Properties_show_what_the_selection_has()
    {
        await OpenShapes();
        Assert.False(Vm.Properties.HasSelection);
        Row("r1").IsSelected = true;

        var p = Vm.Properties;
        Assert.True(p.HasSelection);
        Assert.Equal(PaintMode.Flat, p.Fill.Mode);
        Assert.Equal(Color.FromRgb(0xe0, 0x30, 0x20), p.Fill.Color);
        Assert.Equal(100, p.Fill.Opacity);
        Assert.Equal(PaintMode.None, p.Stroke.Mode);
        Assert.Equal((10m, 10m, 60m, 40m), (p.X, p.Y, p.Width, p.Height));
        Assert.Equal(0m, p.Rotation);

        Row("l1").IsSelected = true;
        Row("r1").IsSelected = false;
        Assert.Equal(PaintMode.Flat, p.Stroke.Mode);
        Assert.Equal(Color.FromRgb(0x80, 0x40, 0), p.Stroke.Color);
        Assert.Equal(4, p.StrokeWidth);
        _h.Capture("svg-54-properties");
    }

    [AvaloniaFact]
    public async Task Changing_the_fill_color_in_the_panel_changes_the_canvas_and_undo_restores_it()
    {
        await OpenShapes();
        Row("r1").IsSelected = true;
        Vm.Properties.Fill.Color = Color.FromRgb(0x11, 0x88, 0x33);
        Dispatcher.UIThread.RunJobs();
        var after = _h.Capture("svg-55-fill-changed");

        Assert.Equal((0x11, 0x88, 0x33), CanvasPixel(after, 30, 30));
        Assert.Equal("Set Fill", Vm.History[^1].Text);
        Assert.True(Vm.ActiveDocument!.IsDirty);

        Vm.UndoCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((0xe0, 0x30, 0x20), CanvasPixel(_h.Capture("svg-56-fill-undone"), 30, 30));
        Assert.Equal(Color.FromRgb(0xe0, 0x30, 0x20), Vm.Properties.Fill.Color);
        Assert.False(Vm.ActiveDocument.IsDirty);
    }

    [AvaloniaFact]
    public async Task A_slider_drag_is_one_history_step()
    {
        await OpenShapes();
        Row("r1").IsSelected = true;
        var before = Vm.History.Count;

        foreach (var v in new[] { 90.0, 80, 70, 60, 50, 40 })
            Vm.Properties.Fill.Opacity = v;
        Vm.Properties.EndGesture();
        Vm.Properties.ObjectOpacity = 50;
        Vm.Properties.EndGesture();
        Vm.Properties.ObjectOpacity = 25;

        Assert.Equal(before + 3, Vm.History.Count);                   // one for the drag, then two for opacity
        Assert.Equal(["Set Fill Opacity", "Set Opacity", "Set Opacity"], Vm.History.Skip(before).Select(h => h.Text));
        var rect = (SvgRect)Svg.Root.FindById("r1")!;
        Assert.Equal(0.4, rect.Style.FillOpacity!.Value, 6);
        Vm.UndoCommand.Execute(null);
        Vm.UndoCommand.Execute(null);
        Vm.UndoCommand.Execute(null);
        Assert.Null(rect.Style.FillOpacity);                          // the whole drag undone at once
        Assert.Null(rect.GetAttribute("opacity"));
    }

    [AvaloniaFact]
    public async Task Stroke_settings_apply_to_all_selected_objects()
    {
        await OpenShapes();
        Svg.Selection.Set(new[] { Svg.Root.FindById("r1")!, Svg.Root.FindById("c1")! }.OfType<SvgElement>());
        Vm.Properties.Stroke.Mode = PaintMode.Flat;
        Vm.Properties.Stroke.Color = Color.FromRgb(0, 0, 255);
        Vm.Properties.StrokeWidth = 5;
        Vm.Properties.Cap = LineCap.Round;
        Vm.Properties.Join = LineJoin.Bevel;
        Vm.Properties.SelectedDash = SvgPropertiesViewModel.DashPresets[1];
        Dispatcher.UIThread.RunJobs();
        _h.Capture("svg-57-stroke");

        foreach (var id in new[] { "r1", "c1" })
        {
            var style = StyleResolver.ComputeFor((SvgElement)Svg.Root.FindById(id)!);
            Assert.Equal(VColor.FromRgb(0, 0, 255), style.Stroke.Color);
            Assert.Equal(5, style.StrokeWidth);
            Assert.Equal((LineCap.Round, LineJoin.Bevel), (style.LineCap, style.LineJoin));
            Assert.Equal([6.0, 4.0], style.DashArray!);
        }
    }

    [AvaloniaFact]
    public async Task Geometry_fields_move_and_resize_the_selection()
    {
        await OpenShapes();
        Row("r1").IsSelected = true;
        Vm.Properties.X = 20;
        Vm.Properties.Y = 30;
        Vm.Properties.Width = 120;
        var rect = (SvgRect)Svg.Root.FindById("r1")!;
        Assert.Equal((20, 30, 120), (rect.X, rect.Y, rect.Width));
        Assert.Equal(40, rect.Height);
        Assert.Null(rect.GetAttribute("transform"));               // natural form kept
        Vm.Properties.Rotation = 45;
        Assert.Equal(45, rect.Transform.Decompose().Rotation, 3);
        Assert.Equal(45m, Vm.Properties.Rotation);
        Vm.UndoCommand.Execute(null);
        Assert.True(rect.Transform.IsIdentity);
    }

    [AvaloniaFact]
    public async Task Fill_can_become_a_gradient_whose_stops_are_edited_in_the_panel()
    {
        await OpenShapes();
        Row("r1").IsSelected = true;
        var fill = Vm.Properties.Fill;

        fill.Mode = PaintMode.Linear;
        Dispatcher.UIThread.RunJobs();
        _h.Capture("svg-58-gradient");
        var rect = (SvgRect)Svg.Root.FindById("r1")!;
        var gradient = Assert.IsType<SvgLinearGradient>(Svg.Root.FindById(StyleResolver.ComputeFor(rect).Fill.Id));
        Assert.Equal(2, gradient.ResolvedStops().Count);
        Assert.Equal(2, fill.Stops.Count);
        Assert.Equal(VColor.FromRgb(0xe0, 0x30, 0x20), gradient.ResolvedStops()[0].Color);   // starts as the old color
        Assert.Equal(0, gradient.ResolvedStops()[1].Color.A);                              // fading out

        fill.Stops[1].Color = Color.FromRgb(0, 0, 255);
        Assert.Equal(VColor.FromRgb(0, 0, 255), gradient.ResolvedStops()[1].Color.WithAlpha(255));
        fill.AddStopCommand.Execute(null);
        Assert.Equal(3, gradient.ResolvedStops().Count);
        Assert.Equal(0.5, gradient.ResolvedStops()[1].Offset, 6);
        fill.SelectedStop = fill.Stops.First(s => s.Offset == 0.5);
        fill.RemoveStopCommand.Execute(null);
        Assert.Equal(2, gradient.ResolvedStops().Count);

        fill.Mode = PaintMode.Radial;
        var radial = StyleResolver.ComputeFor(rect).Fill;
        Assert.IsType<SvgRadialGradient>(Svg.Root.FindById(radial.Id));
        fill.Mode = PaintMode.None;
        Assert.Equal("none", rect.GetAttribute("fill"));
        fill.Mode = PaintMode.Flat;
        Assert.Equal(PaintKind.Color, StyleResolver.ComputeFor(rect).Fill.Kind);
    }

    [AvaloniaFact]
    public async Task Palette_colors_apply_to_the_selection_on_drawings_only()
    {
        await OpenShapes();
        Row("r1").IsSelected = true;
        _h.Dialogs.ColorAnswers.Enqueue(Color.FromRgb(1, 2, 3));
        await Vm.PickSecondaryColorCommand.ExecuteAsync(null);
        Assert.Equal(VColor.FromRgb(1, 2, 3), StyleResolver.ComputeFor((SvgElement)Svg.Root.FindById("r1")!).Fill.Color);

        _h.Dialogs.ColorAnswers.Enqueue(Color.FromRgb(4, 5, 6));
        await Vm.PickPrimaryColorCommand.ExecuteAsync(null);
        Assert.Equal(VColor.FromRgb(4, 5, 6), StyleResolver.ComputeFor((SvgElement)Svg.Root.FindById("r1")!).Stroke.Color);

        Vm.CreateImage(new NewImageOptions(new ImageSize(10, 10), ColorBgra.White));
        var historyBefore = Vm.History.Count;
        _h.Dialogs.ColorAnswers.Enqueue(Color.FromRgb(7, 8, 9));
        await Vm.PickPrimaryColorCommand.ExecuteAsync(null);
        Assert.Equal(historyBefore, Vm.History.Count);               // an image's history is untouched by the palette
    }

    [AvaloniaFact]
    public async Task Edit_commands_work_on_objects_cut_copy_paste_select_all_delete()
    {
        await OpenShapes();
        Row("r1").IsSelected = true;
        Assert.True(Vm.CopyCommand.CanExecute(null));
        await Vm.CopyCommand.ExecuteAsync(null);
        Assert.NotNull(_h.Clipboard.Svg);
        Assert.NotNull(_h.Clipboard.Image);

        await Vm.PasteCommand.ExecuteAsync(null);
        Assert.Equal(7, Vm.Objects.Count);
        Assert.Equal(1, Svg.Selection.Count);

        await Vm.CutCommand.ExecuteAsync(null);
        Assert.Equal(6, Vm.Objects.Count);
        Vm.UndoCommand.Execute(null);
        Assert.Equal(7, Vm.Objects.Count);

        Vm.SelectAllCommand.Execute(null);
        Assert.Equal(7, Svg.Selection.Count);
        Vm.EraseSelectionCommand.Execute(null);
        Assert.Empty(Vm.Objects);
        Assert.True(Vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Escape_deselects_the_objects()
    {
        await OpenShapes();
        Row("r1").IsSelected = true;
        Assert.True(Vm.ToolKeyDown(CinnabarSharp.Core.Tools.ToolKey.Escape, CinnabarSharp.Core.Tools.ToolModifiers.None));
        Assert.True(Svg.Selection.IsEmpty);
        Assert.False(Vm.ToolKeyDown(CinnabarSharp.Core.Tools.ToolKey.Escape, CinnabarSharp.Core.Tools.ToolModifiers.None));
    }

    [AvaloniaFact]
    public async Task Switching_tabs_swaps_between_layers_and_objects()
    {
        Vm.CreateImage(new NewImageOptions(new ImageSize(20, 20), ColorBgra.White));
        Assert.Single(Vm.Layers);
        Assert.Empty(Vm.Objects);
        await OpenShapes();
        Assert.Empty(Vm.Layers);
        Assert.Equal(6, Vm.Objects.Count);
        Vm.ActiveDocument = Vm.Documents[0];
        Assert.Single(Vm.Layers);
        Assert.Empty(Vm.Objects);
        Assert.False(Vm.Properties.HasSelection);
    }
}
