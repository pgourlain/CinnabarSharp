using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using CinnabarSharp.Core.Effects;
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
        bool Literal = false)
    {
        public static readonly MenuSpec Separator = new("-");
    }

    private double _wheelZoomAccumulator;
    private bool _spaceHeld;
    private Point? _panStart;
    private Vector _panStartOffset;
    private bool _closeConfirmed;
    private NativeMenu? _nativeRecentMenu;
    private MenuItem? _recentMenuItem;

    public MainWindow()
    {
        InitializeComponent();
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
        foreach (var letter in vm.Tools.Select(t => t.Shortcut).Distinct())
        {
            if (Enum.TryParse<Key>(letter, out var key))
                AddLetterShortcut(key, () => vm.SelectToolByShortcut(letter));
        }
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTool))
                Canvas.Cursor = vm.SelectedTool.IsText ? new Cursor(StandardCursorType.Ibeam)
                    : vm.SelectedTool.IsPaintingTool ? new Cursor(StandardCursorType.Cross)
                    : null;
        };
        BuildMenu(vm);
    }

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
        CanvasScroller.Offset = new Vector(
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
        return props.IsMiddleButtonPressed
               || (props.IsLeftButtonPressed && (_spaceHeld || Vm?.SelectedTool.Name == "Pan"));
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is { HasDocument: true } zoomVm && zoomVm.SelectedTool.Name == "Zoom" && !_spaceHeld)
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
            new("Open _Recent", Children: []),
            MenuSpec.Separator,
            new("_Save", vm.SaveCommand, G(Key.S)),
            new("Save _As…", vm.SaveAsCommand, G(Key.S, KeyModifiers.Shift)),
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
            ]),
        ];
        if (!isMac)
            menus = [.. menus, new("_Help", Children: [new("_About CinnabarSharp", vm.AboutCommand)])];

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
        return item;
    }

    private static Control ToMenuItem(MenuSpec spec)
    {
        if (spec == MenuSpec.Separator)
            return new Separator();
        return new MenuItem
        {
            Header = spec.Literal ? spec.Header.Replace("_", "__") : spec.Header,
            Command = spec.Command,
            CommandParameter = spec.CommandParameter,
            InputGesture = spec.Gesture,
            HotKey = spec.Gesture,
            ItemsSource = spec.Children?.Select(ToMenuItem).ToList(),
        };
    }
}
