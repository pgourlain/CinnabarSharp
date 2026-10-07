using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Effects;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Views;

public partial class MainWindow : Window, IViewportService
{
    private sealed record MenuSpec(
        string Header,
        ICommand? Command = null,
        KeyGesture? Gesture = null,
        MenuSpec[]? Children = null,
        object? CommandParameter = null,
        bool Literal = false,
        (System.ComponentModel.INotifyPropertyChanged Source, string Property, Func<bool> Value)? Checked = null)
    {
        public static readonly MenuSpec Separator = new("-");
    }

    private double _wheelZoomAccumulator;
    private bool _spaceHeld;
    private Point? _panStart;
    private Avalonia.Vector _panStartOffset;
    private bool _closeConfirmed;
    private NativeMenu? _nativeRecentMenu;
    private MenuItem? _recentMenuItem;

    public MainWindow()
    {
        StartupTrace.Mark("MainWindow ctor start");
        InitializeComponent();
        StartupTrace.Mark("MainWindow XAML loaded");
        Canvas.CanvasPointerMoved += p => Vm?.UpdateCursorPosition(p);
        Canvas.ToolPointerPressed += p => Vm?.ToolPointerDown(p);
        Canvas.ToolPointerMoved += p => Vm?.ToolPointerMove(p);
        Canvas.ToolPointerReleased += p => Vm?.ToolPointerUp(p);
        CanvasScroller.SizeChanged += (_, e) =>
        {
            if (Vm is { } vm)
                vm.ViewportSize = new Size(
                    Math.Max(0, e.NewSize.Width - Canvas.Margin.Left - Canvas.Margin.Right),
                    Math.Max(0, e.NewSize.Height - Canvas.Margin.Top - Canvas.Margin.Bottom));
        };

        CanvasScroller.AddHandler(PointerWheelChangedEvent, OnCanvasWheel, RoutingStrategies.Tunnel);
        CanvasScroller.AddHandler(PointerTouchPadGestureMagnifyEvent, OnCanvasMagnify);
        CanvasScroller.AddHandler(PointerPressedEvent, OnCanvasPointerPressed, RoutingStrategies.Tunnel);
        CanvasScroller.AddHandler(PointerMovedEvent, OnCanvasPointerMoved, RoutingStrategies.Tunnel);
        CanvasScroller.AddHandler(PointerReleasedEvent, OnCanvasPointerReleased, RoutingStrategies.Tunnel);
        CanvasScroller.AddHandler(PointerCaptureLostEvent, (_, _) => EndPan());
        AddHandler(KeyDownEvent, OnToolKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnToolTextInput, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        LayersList.DoubleTapped += (_, _) => Vm?.LayerPropertiesCommand.Execute(null);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private KeyModifiers CommandModifier =>
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is not { } vm)
            return;
        vm.Dialogs = new DialogService(this);
        vm.Clipboard = new AvaloniaClipboardService(this);
        vm.Viewport = this;
        vm.RegionInvalidated += region => Canvas.UpdateRegion(region);
        vm.RecentFiles.Changed += () => RefreshRecentMenu(vm);
        // Single-letter shortcuts are disabled while typing (text boxes, the Text tool), so they don't fire and
        // don't swallow the key: a handled key produces no text input on some platforms.
        void AddLetterShortcut(Key key, Action action) => KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(key),
            Command = new RelayCommand(action, () => !IsTyping),
        });
        AddLetterShortcut(Key.X, () => vm.SwapColorsCommand.Execute(null));
        AddLetterShortcut(Key.D, () => vm.ResetColorsCommand.Execute(null));
        foreach (var letter in vm.Tools.Concat(vm.VectorTools).Select(t => t.Shortcut).Distinct())
        {
            if (Enum.TryParse<Key>(letter, out var key))
                AddLetterShortcut(key, () => vm.SelectToolByShortcut(letter));
        }
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SelectedTool) or nameof(MainViewModel.HoverCursor) or nameof(MainViewModel.IsComicMode))
                Canvas.Cursor = CanvasCursor(vm);
            if (e.PropertyName == nameof(MainViewModel.IsBusy))
                Cursor = vm.IsBusy ? new Cursor(StandardCursorType.Wait) : null;
            if (e.PropertyName == nameof(MainViewModel.ActiveDocument) && vm.ActiveDocument is { } active)
                Dispatcher.UIThread.Post(() => TabsList.ScrollIntoView(active), DispatcherPriority.Loaded);
        };
        StartupTrace.Mark("DataContext handlers wired");
        BuildMenu(vm);
        StartupTrace.Mark("Menu built");
    }

    private static MenuSpec[] AlignTargets(MainViewModel vm) =>
    [
        AlignTarget(vm, "Relative to _First Selected", AlignRelativeTo.FirstSelected),
        AlignTarget(vm, "Relative to _Last Selected", AlignRelativeTo.LastSelected),
        AlignTarget(vm, "Relative to _Biggest", AlignRelativeTo.Biggest),
        AlignTarget(vm, "Relative to _Page", AlignRelativeTo.Page),
        AlignTarget(vm, "Relative to _Selection", AlignRelativeTo.Selection),
    ];

    private static MenuSpec AlignTarget(MainViewModel vm, string header, AlignRelativeTo target) =>
        new(header, vm.SetAlignRelativeToCommand, CommandParameter: target,
            Checked: (vm, nameof(MainViewModel.AlignRelativeTo), () => vm.AlignRelativeTo == target));

    private static Cursor? CanvasCursor(MainViewModel vm) => vm.HoverCursor switch
    {
        CinnabarSharp.Core.Tools.ToolCursor.Move => new Cursor(StandardCursorType.SizeAll),
        CinnabarSharp.Core.Tools.ToolCursor.ResizeHorizontal => new Cursor(StandardCursorType.SizeWestEast),
        CinnabarSharp.Core.Tools.ToolCursor.ResizeVertical => new Cursor(StandardCursorType.SizeNorthSouth),
        CinnabarSharp.Core.Tools.ToolCursor.ResizeDiagonal => new Cursor(StandardCursorType.TopLeftCorner),
        CinnabarSharp.Core.Tools.ToolCursor.ResizeAntiDiagonal => new Cursor(StandardCursorType.TopRightCorner),
        CinnabarSharp.Core.Tools.ToolCursor.Rotate => new Cursor(StandardCursorType.Hand),
        CinnabarSharp.Core.Tools.ToolCursor.Text => new Cursor(StandardCursorType.Ibeam),
        // The comic page gets the mouse, not the selected tool: no text or drawing cursor.
        _ when vm.IsComicMode => null,
        _ when vm.SelectedTool.IsText => new Cursor(StandardCursorType.Ibeam),
        _ when vm.SelectedTool.IsPaintingTool => new Cursor(StandardCursorType.Cross),
        _ => null,
    };

    // ---- Settings ----

    private SettingsStore? _settings;

    /// <summary>Restores window placement and tool options, and saves them again when the window closes.</summary>
    public void RestoreSettings(SettingsStore store)
    {
        _settings = store;
        var saved = store.Load();
        if (saved.WindowWidth is { } w && saved.WindowHeight is { } h && w >= MinWidth && h >= MinHeight)
            (Width, Height) = (w, h);
        if (saved.WindowX is { } x && saved.WindowY is { } y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
        if (saved.WindowMaximized)
            WindowState = WindowState.Maximized;
        Vm?.ApplySettings(saved);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_settings is not null && Vm is { } vm)
        {
            var normal = WindowState == WindowState.Normal;
            var previous = _settings.Load();
            _settings.Save(vm.CaptureSettings(previous with
            {
                WindowWidth = normal ? Width : previous.WindowWidth,
                WindowHeight = normal ? Height : previous.WindowHeight,
                WindowX = normal ? Position.X : previous.WindowX,
                WindowY = normal ? Position.Y : previous.WindowY,
                WindowMaximized = WindowState == WindowState.Maximized,
            }));
        }
        base.OnClosed(e);
    }

    // ---- Closing with unsaved changes ----

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || Vm is not { HasUnsavedChanges: true } vm)
            return;

        e.Cancel = true;
        if (await vm.CloseAllAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ---- Objects panel ----

    private void OnObjectDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ViewModels.ObjectViewModel row && !row.IsEditing)
            row.BeginEdit();
    }

    private void OnObjectNameKeyDown(object? sender, KeyEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ViewModels.ObjectViewModel row)
            return;
        if (e.Key == Key.Enter)
        {
            row.CommitEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.CancelEdit();
            e.Handled = true;
        }
    }

    private void OnObjectNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ViewModels.ObjectViewModel row)
            row.CommitEdit();
    }

    private void OnSliderGestureEnd(object? sender, PointerCaptureLostEventArgs e) => Vm?.Properties.EndGesture();

    // ---- Zoom ----

    public void ZoomTo(double scale, Point? anchor = null)
    {
        if (Vm?.ActiveDocument?.Document is not { } doc)
            return;

        scale = Math.Clamp(scale, MainViewModel.MinZoom, MainViewModel.MaxZoom);
        // A previous zoom may have changed the offset without re-arranging yet; positions must be current.
        CanvasScroller.UpdateLayout();
        var viewport = CanvasScroller.Viewport;
        var a = anchor ?? new Point(viewport.Width / 2, viewport.Height / 2);
        var oldScale = doc.Workspace.Scale;
        var canvasOrigin = Canvas.TranslatePoint(default, CanvasScroller) ?? default;
        var imagePoint = (a - canvasOrigin) / oldScale;

        doc.Workspace.Scale = scale;
        CanvasScroller.UpdateLayout();

        var offset = CanvasScroller.Offset;
        var newOrigin = (Canvas.TranslatePoint(default, CanvasScroller) ?? default) + offset;
        var desired = newOrigin + imagePoint * doc.Workspace.Scale - a;
        var extent = CanvasScroller.Extent;
        CanvasScroller.Offset = new Avalonia.Vector(
            Math.Clamp(desired.X, 0, Math.Max(0, extent.Width - viewport.Width)),
            Math.Clamp(desired.Y, 0, Math.Max(0, extent.Height - viewport.Height)));
        CanvasScroller.UpdateLayout();
    }

    public CinnabarSharp.Core.Models.PointI VisibleImageOrigin()
    {
        if (Vm?.ActiveDocument?.Document is not { } doc)
            return default;
        var topLeft = CanvasScroller.TranslatePoint(default, Canvas) ?? default;
        var scale = doc.Workspace.Scale;
        return new CinnabarSharp.Core.Models.PointI(
            Math.Max(0, (int)Math.Ceiling(topLeft.X / scale)), Math.Max(0, (int)Math.Ceiling(topLeft.Y / scale)));
    }

    private void OnCanvasWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Vm is not { HasDocument: true } vm)
            return;
        if ((e.KeyModifiers & (CommandModifier | KeyModifiers.Control)) == 0)
            return;

        e.Handled = true;
        _wheelZoomAccumulator += e.Delta.Y;
        var anchor = e.GetPosition(CanvasScroller);
        while (Math.Abs(_wheelZoomAccumulator) >= 1)
        {
            var zoomIn = _wheelZoomAccumulator > 0;
            _wheelZoomAccumulator -= zoomIn ? 1 : -1;
            var percent = zoomIn
                ? MainViewModel.NextZoomIn(vm.CurrentZoomPercent)
                : MainViewModel.NextZoomOut(vm.CurrentZoomPercent);
            ZoomTo(percent / 100, anchor);
        }
    }

    private void OnCanvasMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (Vm?.ActiveDocument?.Document is not { } doc)
            return;
        e.Handled = true;
        ZoomTo(doc.Workspace.Scale * (1 + e.Delta.X), e.GetPosition(CanvasScroller));
    }

    // ---- Keys for tools (Text tool typing, Enter/Escape to finish a curve) ----

    private bool TextBoxHasFocus => FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>Keys are text: in a text box, or while the Text tool is editing.</summary>
    public bool IsTyping => TextBoxHasFocus || Vm?.IsTyping == true;

    private void OnToolKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || TextBoxHasFocus || ToToolKey(e.Key) is not { } key)
            return;
        var modifiers = CinnabarSharp.Core.Tools.ToolModifiers.None;
        if ((e.KeyModifiers & (KeyModifiers.Meta | KeyModifiers.Control)) != 0)
            modifiers |= CinnabarSharp.Core.Tools.ToolModifiers.Command;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            modifiers |= CinnabarSharp.Core.Tools.ToolModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            modifiers |= CinnabarSharp.Core.Tools.ToolModifiers.Shift;
        if (vm.ToolKeyDown(key, modifiers))
            e.Handled = true;
    }

    private void OnToolTextInput(object? sender, TextInputEventArgs e)
    {
        if (Vm is not { IsTyping: true } vm || TextBoxHasFocus || string.IsNullOrEmpty(e.Text))
            return;
        vm.ToolTextInput(e.Text);
        e.Handled = true;
    }

    private static CinnabarSharp.Core.Tools.ToolKey? ToToolKey(Key key) => key switch
    {
        Key.Enter => CinnabarSharp.Core.Tools.ToolKey.Enter,
        Key.Escape => CinnabarSharp.Core.Tools.ToolKey.Escape,
        Key.Back => CinnabarSharp.Core.Tools.ToolKey.Backspace,
        Key.Delete => CinnabarSharp.Core.Tools.ToolKey.Delete,
        Key.Left => CinnabarSharp.Core.Tools.ToolKey.Left,
        Key.Right => CinnabarSharp.Core.Tools.ToolKey.Right,
        Key.Up => CinnabarSharp.Core.Tools.ToolKey.Up,
        Key.Down => CinnabarSharp.Core.Tools.ToolKey.Down,
        Key.Home => CinnabarSharp.Core.Tools.ToolKey.Home,
        Key.End => CinnabarSharp.Core.Tools.ToolKey.End,
        _ => null,
    };

    // ---- Pan: middle button, Space + drag, or the Pan tool ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space && Vm?.HasDocument == true && !IsTyping)
        {
            _spaceHeld = true;
            CanvasScroller.Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            if (_panStart is null)
                CanvasScroller.Cursor = null;
        }
        base.OnKeyUp(e);
    }

    private bool IsPanGesture(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(CanvasScroller).Properties;
        // In comic page mode the page gets the mouse whatever tool was selected; Space still pans.
        return props.IsMiddleButtonPressed
               || (props.IsLeftButtonPressed && (_spaceHeld || Vm is { IsComicMode: false, SelectedTool.Name: "Pan" }));
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is { HasDocument: true, IsComicMode: false } zoomVm && zoomVm.SelectedTool.Name == "Zoom" && !_spaceHeld)
        {
            var props = e.GetCurrentPoint(CanvasScroller).Properties;
            if (props.IsLeftButtonPressed || props.IsRightButtonPressed)
            {
                var percent = props.IsRightButtonPressed
                    ? MainViewModel.NextZoomOut(zoomVm.CurrentZoomPercent)
                    : MainViewModel.NextZoomIn(zoomVm.CurrentZoomPercent);
                ZoomTo(percent / 100, e.GetPosition(CanvasScroller));
                e.Handled = true;
                return;
            }
        }
        if (Vm?.HasDocument != true || !IsPanGesture(e))
            return;
        _panStart = e.GetPosition(CanvasScroller);
        _panStartOffset = CanvasScroller.Offset;
        CanvasScroller.Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(CanvasScroller);
        e.Handled = true;
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_panStart is not { } start)
            return;
        var delta = e.GetPosition(CanvasScroller) - start;
        CanvasScroller.Offset = _panStartOffset - delta;
        e.Handled = true;
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panStart is null)
            return;
        e.Pointer.Capture(null);
        EndPan();
        e.Handled = true;
    }

    private void EndPan()
    {
        _panStart = null;
        CanvasScroller.Cursor = _spaceHeld ? new Cursor(StandardCursorType.Hand) : null;
    }

    // ---- Drag and drop files ----

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (Vm is not { } vm || e.DataTransfer.TryGetFiles() is not { } files)
            return;
        foreach (var path in files.Select(f => f.TryGetLocalPath()).OfType<string>())
            await vm.OpenFileAsync(path);
    }

    // ---- Menu ----

    private void BuildMenu(MainViewModel vm)
    {
        var isMac = OperatingSystem.IsMacOS();
        var cmd = CommandModifier;
        KeyGesture G(Key key, KeyModifiers extra = KeyModifiers.None) => new(key, cmd | extra);
        var notYet = vm.NotYetImplemented;

        var fileItems = new MenuSpec[]
        {
            new("_New…", vm.NewImageCommand, G(Key.N)),
            new("_Open…", vm.OpenCommand, G(Key.O)),
            new("Open as _Image…", vm.OpenAsImageCommand),
            new("Open _Recent", Children: []),
            MenuSpec.Separator,
            new("_Save", vm.SaveCommand, G(Key.S)),
            new("Save _As…", vm.SaveAsCommand, G(Key.S, KeyModifiers.Shift)),
            new("E_xport As…", vm.ExportAsCommand, G(Key.S, KeyModifiers.Alt)),
            MenuSpec.Separator,
            new("Import _Picture…", vm.ImportPictureCommand),
            new("Import _Linked Picture…", vm.ImportLinkedPictureCommand),
            new("_Update Drawing From Bitmap", vm.UpdateDrawingCommand),
            MenuSpec.Separator,
            new("Allow AI Ag_ents (MCP)", vm.ToggleAllowAgentsCommand, Checked: (vm, nameof(MainViewModel.AllowAgents), () => vm.AllowAgents)),
            new("Connect an AI A_gent…", vm.ShowAgentConnectionCommand),
            MenuSpec.Separator,
            new("_Close", vm.CloseCommand, G(Key.W)),
        };
        if (!isMac)
            fileItems = [.. fileItems, new("E_xit", new RelayCommand(Close), G(Key.Q))];

        MenuSpec[] menus =
        [
            new("_File", Children: fileItems),
            new("_Edit", Children:
            [
                new("_Undo", vm.UndoCommand, G(Key.Z)),
                new("_Redo", vm.RedoCommand, isMac ? G(Key.Z, KeyModifiers.Shift) : G(Key.Y)),
                MenuSpec.Separator,
                new("Cu_t", vm.CutCommand, G(Key.X)),
                new("_Copy", vm.CopyCommand, G(Key.C)),
                new("Copy _Merged", vm.CopyMergedCommand, G(Key.C, KeyModifiers.Shift)),
                new("_Paste", vm.PasteCommand, G(Key.V)),
                new("Paste Into New _Layer", vm.PasteIntoNewLayerCommand, G(Key.V, KeyModifiers.Shift)),
                new("Paste Into New _Image", vm.PasteIntoNewImageCommand, G(Key.V, KeyModifiers.Alt)),
                new("Paste _Beside…", vm.PasteBesideCommand),
                MenuSpec.Separator,
                new("_Erase Selection", vm.EraseSelectionCommand, new KeyGesture(Key.Delete)),
                new("_Fill Selection", vm.FillSelectionCommand, new KeyGesture(Key.Back)),
                new("_Invert Selection", vm.InvertSelectionCommand, G(Key.I)),
                new("Select _All", vm.SelectAllCommand, G(Key.A)),
                new("_Deselect All", vm.DeselectAllCommand, G(Key.D)),
            ]),
            new("_View", Children:
            [
                new("Zoom _In", vm.ZoomInCommand, G(Key.OemPlus)),
                new("Zoom _Out", vm.ZoomOutCommand, G(Key.OemMinus)),
                new("_Best Fit", vm.BestFitCommand, G(Key.B)),
                new("_Actual Size", vm.ActualSizeCommand, G(Key.D0)),
                MenuSpec.Separator,
                new("Show _Grid", vm.ToggleGridCommand, G(Key.OemQuotes), Checked: (vm, nameof(MainViewModel.ShowGrid), () => vm.ShowGrid)),
                new("S_nap to Grid", vm.ToggleSnapToGridCommand, G(Key.OemSemicolon), Checked: (vm, nameof(MainViewModel.SnapToGrid), () => vm.SnapToGrid)),
            ]),
            new("_Object", Children:
            [
                new("_Duplicate", vm.DuplicateObjectsCommand, G(Key.D)),
                new("_Delete", vm.DeleteObjectsCommand),
                MenuSpec.Separator,
                new("_Group", vm.GroupObjectsCommand, G(Key.G)),
                new("_Ungroup", vm.UngroupObjectsCommand, G(Key.G, KeyModifiers.Shift)),
                MenuSpec.Separator,
                new("Raise to _Top", vm.RaiseToTopCommand, new KeyGesture(Key.Home)),
                new("_Raise", vm.RaiseObjectsCommand, new KeyGesture(Key.PageUp)),
                new("_Lower", vm.LowerObjectsCommand, new KeyGesture(Key.PageDown)),
                new("Lower to _Bottom", vm.LowerToBottomCommand, new KeyGesture(Key.End)),
                MenuSpec.Separator,
                new("Flip _Horizontal", vm.FlipObjectsHorizontalCommand),
                new("Flip _Vertical", vm.FlipObjectsVerticalCommand),
                new("Rotate 90° _Clockwise", vm.RotateObjectsClockwiseCommand),
                new("Rotate 90° Counter-C_lockwise", vm.RotateObjectsCounterClockwiseCommand),
                MenuSpec.Separator,
                new("_Align", Children:
                [
                    new("_Left", vm.AlignObjectsCommand, CommandParameter: AlignEdge.Left),
                    new("Center _Horizontally", vm.AlignObjectsCommand, CommandParameter: AlignEdge.CenterHorizontal),
                    new("_Right", vm.AlignObjectsCommand, CommandParameter: AlignEdge.Right),
                    new("_Top", vm.AlignObjectsCommand, CommandParameter: AlignEdge.Top),
                    new("Center _Vertically", vm.AlignObjectsCommand, CommandParameter: AlignEdge.CenterVertical),
                    new("_Bottom", vm.AlignObjectsCommand, CommandParameter: AlignEdge.Bottom),
                    MenuSpec.Separator,
                    .. AlignTargets(vm),
                ]),
                new("Dis_tribute", Children:
                [
                    new("_Left Edges", vm.DistributeObjectsCommand, CommandParameter: DistributeMode.Left),
                    new("Centers _Horizontally", vm.DistributeObjectsCommand, CommandParameter: DistributeMode.CenterHorizontal),
                    new("_Right Edges", vm.DistributeObjectsCommand, CommandParameter: DistributeMode.Right),
                    new("Horizontal _Gaps", vm.DistributeObjectsCommand, CommandParameter: DistributeMode.GapHorizontal),
                    MenuSpec.Separator,
                    new("_Top Edges", vm.DistributeObjectsCommand, CommandParameter: DistributeMode.Top),
                    new("Centers _Vertically", vm.DistributeObjectsCommand, CommandParameter: DistributeMode.CenterVertical),
                    new("_Bottom Edges", vm.DistributeObjectsCommand, CommandParameter: DistributeMode.Bottom),
                    new("Vertical G_aps", vm.DistributeObjectsCommand, CommandParameter: DistributeMode.GapVertical),
                ]),
                MenuSpec.Separator,
                new("Cl_ip", Children:
                [
                    new("_Set Clip", vm.SetClipCommand),
                    new("_Release Clip", vm.ReleaseClipCommand),
                ]),
                new("Edit _Bitmap", vm.EditBitmapCommand),
                MenuSpec.Separator,
                new("D_eselect Objects", vm.DeselectObjectsCommand, G(Key.A, KeyModifiers.Shift)),
            ]),
            new("_Path", Children:
            [
                new("_Object to Path", vm.ObjectToPathCommand, G(Key.O, KeyModifiers.Shift)),
                new("_Stroke to Path", vm.StrokeToPathCommand, G(Key.O, KeyModifiers.Shift | KeyModifiers.Alt)),
                MenuSpec.Separator,
                new("_Union", vm.ApplyPathOperationCommand, G(Key.OemPlus, KeyModifiers.Shift), CommandParameter: PathOperation.Union),
                new("_Difference", vm.ApplyPathOperationCommand, G(Key.OemMinus, KeyModifiers.Shift), CommandParameter: PathOperation.Difference),
                new("_Intersection", vm.ApplyPathOperationCommand, CommandParameter: PathOperation.Intersection),
                new("E_xclusion", vm.ApplyPathOperationCommand, CommandParameter: PathOperation.Exclusion),
                new("D_ivision", vm.ApplyPathOperationCommand, CommandParameter: PathOperation.Division),
                MenuSpec.Separator,
                new("_Combine", vm.ApplyPathOperationCommand, G(Key.K), CommandParameter: PathOperation.Combine),
                new("_Break Apart", vm.BreakApartPathsCommand, G(Key.K, KeyModifiers.Shift)),
                MenuSpec.Separator,
                new("Sim_plify", vm.SimplifyPathsCommand, G(Key.L)),
                new("_Reverse", vm.ReversePathsCommand),
            ]),
            new("_Image", Children:
            [
                new("_Crop to Selection", vm.CropToSelectionCommand, G(Key.X, KeyModifiers.Shift)),
                new("_Resize…", vm.ResizeImageCommand, G(Key.R)),
                new("Canvas _Size…", vm.CanvasSizeCommand, G(Key.R, KeyModifiers.Shift)),
                MenuSpec.Separator,
                new("Flip _Horizontal", vm.FlipImageHorizontalCommand),
                new("Flip _Vertical", vm.FlipImageVerticalCommand),
                MenuSpec.Separator,
                new("Rotate 90° Clockwise", vm.RotateClockwiseCommand, G(Key.H)),
                new("Rotate 90° Counter-Clockwise", vm.RotateCounterClockwiseCommand, G(Key.G)),
                new("Rotate 180°", vm.Rotate180Command, G(Key.J)),
                MenuSpec.Separator,
                new("_Flatten", vm.FlattenCommand, G(Key.F, KeyModifiers.Shift)),
                MenuSpec.Separator,
                new("Ras_terize…", vm.RasterizeCommand),
            ]),
            new("_Layers", Children:
            [
                new("_Add New Layer", vm.AddNewLayerCommand, G(Key.N, KeyModifiers.Shift)),
                new("_Delete Layer", vm.DeleteLayerCommand, G(Key.Delete, KeyModifiers.Shift)),
                new("D_uplicate Layer", vm.DuplicateLayerCommand, G(Key.D, KeyModifiers.Shift)),
                new("_Merge Layer Down", vm.MergeLayerDownCommand, G(Key.M)),
                new("_Import From File…", vm.ImportFromFileCommand),
                MenuSpec.Separator,
                new("Flip Layer _Horizontal", vm.FlipLayerHorizontalCommand),
                new("Flip Layer _Vertical", vm.FlipLayerVerticalCommand),
                MenuSpec.Separator,
                new("Move Layer _Up", vm.MoveLayerUpCommand),
                new("Move Layer Do_wn", vm.MoveLayerDownCommand),
                MenuSpec.Separator,
                new("Layer _Properties…", vm.LayerPropertiesCommand, new KeyGesture(Key.F4)),
            ]),
            new("_Adjustments", Children:
            [
                new("_Auto-Level", vm.AutoLevelCommand, G(Key.L, KeyModifiers.Shift)),
                new("_Black and White", vm.BlackAndWhiteCommand, G(Key.G, KeyModifiers.Shift)),
                new("Brightness / _Contrast…", vm.BrightnessContrastCommand),
                new("C_urves…", vm.CurvesCommand, G(Key.M, KeyModifiers.Shift)),
                new("_Hue / Saturation…", vm.HueSaturationCommand, G(Key.U, KeyModifiers.Shift)),
                new("_Invert Colors", vm.InvertColorsCommand, G(Key.I, KeyModifiers.Shift)),
                new("_Levels…", vm.LevelsCommand, G(Key.L)),
                new("_Posterize…", vm.PosterizeCommand, G(Key.P, KeyModifiers.Shift)),
                new("_Sepia", vm.SepiaCommand, G(Key.E, KeyModifiers.Shift)),
            ]),
            new("Effe_cts", Children:
            [
                new("_Repeat Last Effect", vm.RepeatEffectCommand, G(Key.F)),
                MenuSpec.Separator,
                .. EffectCatalog.Effects.GroupBy(e => e.Category).Select(group => new MenuSpec(group.Key,
                    Children: group.Select(e => new MenuSpec(
                        e.Parameters.Count > 0 ? e.Name + "…" : e.Name, vm.ApplyEffectCommand,
                        CommandParameter: e, Literal: true)).ToArray())),
            ]),
        ];
        menus =
        [
            .. menus,
            new("_Photo", Children:
            [
                .. EffectCatalog.PhotoTools.Select(e => new MenuSpec(
                    e.Parameters.Count > 0 ? e.Name + "…" : e.Name, vm.ApplyEffectCommand, CommandParameter: e, Literal: true,
                    Gesture: e is AutoEnhanceEffect ? G(Key.E, KeyModifiers.Alt) : null)),
                MenuSpec.Separator,
                new("Prepare for _TV…", vm.PrepareForTvCommand),
                new("Prepare _Folder for TV…", vm.PrepareFolderForTvCommand),
                MenuSpec.Separator,
                new("_Comic Page…", vm.ComicPageCommand),
            ]),
        ];
        if (!isMac)
            menus = [.. menus, new("_Help", Children: [new("Check for _Updates…", vm.CheckForUpdatesCommand), new("Open _Log Folder", vm.OpenLogFolderCommand), MenuSpec.Separator, new("_About CinnabarSharp", vm.AboutCommand)])];

        // Built once: the macOS native menu can't be replaced while the window is shown, only mutated.
        if (isMac)
        {
            var nativeMenu = ToNativeMenu(menus);
            _nativeRecentMenu = FindNative(nativeMenu, "File", "Open Recent").Menu;
            NativeMenu.SetMenu(this, nativeMenu);
        }
        else
        {
            var items = menus.Select(ToMenuItem).Cast<MenuItem>().ToList();
            _recentMenuItem = items.First(i => (string)i.Header! == "_File")
                .ItemsSource!.OfType<MenuItem>().First(i => (string)i.Header! == "Open _Recent");
            MenuHost.Content = new Menu { ItemsSource = items };
        }
        RefreshRecentMenu(vm);

        EmptyHint.Text = $"File › New ({G(Key.N).ToString("p", null)}) or drop an image here";
    }

    private void RefreshRecentMenu(MainViewModel vm)
    {
        MenuSpec[] recent = vm.RecentFiles.Files.Count == 0
            ? [new("No recent files", vm.NotYetImplemented)]
            :
            [
                .. vm.RecentFiles.Files.Select(path =>
                    new MenuSpec(path, vm.OpenRecentCommand, CommandParameter: path, Literal: true)),
                MenuSpec.Separator,
                new("Clear Recent", vm.ClearRecentCommand),
            ];

        if (_nativeRecentMenu is not null)
        {
            _nativeRecentMenu.Items.Clear();
            foreach (var spec in recent)
                _nativeRecentMenu.Items.Add(ToNativeMenuItem(spec));
        }
        if (_recentMenuItem is not null)
            _recentMenuItem.ItemsSource = recent.Select(ToMenuItem).ToList();
    }

    private static NativeMenuItem FindNative(NativeMenu menu, params string[] path)
    {
        var item = menu.Items.OfType<NativeMenuItem>().First(i => i.Header == path[0]);
        return path.Length == 1 ? item : FindNative(item.Menu!, path[1..]);
    }

    private static NativeMenu ToNativeMenu(MenuSpec[] specs)
    {
        var menu = new NativeMenu();
        foreach (var spec in specs)
            menu.Add(ToNativeMenuItem(spec));
        return menu;
    }

    private static NativeMenuItemBase ToNativeMenuItem(MenuSpec spec)
    {
        if (spec == MenuSpec.Separator)
            return new NativeMenuItemSeparator();
        var item = new NativeMenuItem(spec.Literal ? spec.Header : spec.Header.Replace("_", ""))
        {
            Command = spec.Command,
            CommandParameter = spec.CommandParameter,
            Gesture = spec.Gesture,
        };
        if (spec.Children is not null)
            item.Menu = ToNativeMenu(spec.Children);
        if (spec.Checked is { } check)
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = check.Value();
            check.Source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == check.Property)
                    item.IsChecked = check.Value();
            };
        }
        return item;
    }

    private static Control ToMenuItem(MenuSpec spec)
    {
        if (spec == MenuSpec.Separator)
            return new Separator();
        var item = new MenuItem
        {
            Header = spec.Literal ? spec.Header.Replace("_", "__") : spec.Header,
            Command = spec.Command,
            CommandParameter = spec.CommandParameter,
            InputGesture = spec.Gesture,
            HotKey = spec.Gesture,
            ItemsSource = spec.Children?.Select(ToMenuItem).ToList(),
        };
        if (spec.Checked is { } check)
        {
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = check.Value();
            check.Source.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == check.Property)
                    item.IsChecked = check.Value();
            };
        }
        return item;
    }
}
