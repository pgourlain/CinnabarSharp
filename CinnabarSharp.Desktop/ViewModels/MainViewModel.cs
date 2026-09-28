using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Adjustments;
using CinnabarSharp.Core.Effects;
using Effect = CinnabarSharp.Core.Effects.Effect;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Desktop.Services;

namespace CinnabarSharp.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    // Paint.NET zoom presets, in percent.
    private static readonly double[] ZoomLevels =
        [1, 2, 3, 5, 10, 15, 20, 25, 30, 40, 50, 66.67, 75, 100, 125, 150, 200, 300, 400, 500, 600, 700, 800, 1000, 1200, 1400, 1600, 2400, 3200];

    public const double MinZoom = 0.01;
    public const double MaxZoom = 32;

    private readonly IWorkspaceService _workspace;
    private readonly IFormatManager _formats;
    private readonly IDisposable _eventsSubscription;
    private bool _syncingSelection;

    public MainViewModel(IWorkspaceService workspace, IFormatManager formats, IDocumentEventsService events,
        RecentFilesStore recentFiles, ITextRasterizer textRasterizer)
    {
        _workspace = workspace;
        _formats = formats;
        RecentFiles = recentFiles;
        Tools = ToolViewModel.CreatePaintDotNetTools(ToolSettings, textRasterizer);
        ToolSettings.ColorsChanged += OnColorsChanged;
        SelectedTool = Tools.First(t => t.Name == "Rectangle Select");
        _eventsSubscription = events.DocumentEvents.Subscribe(new EventObserver(OnDocumentEvent));
    }

    public IDialogService? Dialogs { get; set; }
    public IViewportService? Viewport { get; set; }
    public RecentFilesStore RecentFiles { get; }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    /// <summary>Top-most layer first, like Paint.NET's Layers panel.</summary>
    public ObservableCollection<LayerViewModel> Layers { get; } = [];

    public ToolSettings ToolSettings { get; } = new();

    public ToolViewModel[] Tools { get; }

    public IClipboardService? Clipboard { get; set; }

    /// <summary>MCP attached mode (File › Allow AI Agents); not set in tests.</summary>
    public AgentConnection? Agents { get; set; }

    /// <summary>Whether AI agents connected with "CinnabarSharp --mcp --attach" can edit the open images.</summary>
    [ObservableProperty]
    public partial bool AllowAgents { get; set; }

    partial void OnAllowAgentsChanged(bool value)
    {
        if (Agents is null)
            return;
        if (!value)
        {
            Agents.Stop();
            return;
        }
        string? error;
        try
        {
            error = Agents.Start() ? null : "Another CinnabarSharp window already accepts AI agents.";
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException)
        {
            error = e.Message;
        }
        if (error is null)
            return;
        AllowAgents = false;
        Dialogs?.ShowErrorAsync("Can't accept AI agents", error);
    }

    [RelayCommand]
    private void ToggleAllowAgents() => AllowAgents = !AllowAgents;

    [ObservableProperty]
    public partial DocumentViewModel? ActiveDocument { get; set; }

    [ObservableProperty]
    public partial LayerViewModel? SelectedLayer { get; set; }

    [ObservableProperty]
    public partial ToolViewModel SelectedTool { get; set; }

    public Color PrimaryColor
    {
        get => ToAvalonia(ToolSettings.PrimaryColor);
        set => ToolSettings.PrimaryColor = ToBgra(value);
    }

    public Color SecondaryColor
    {
        get => ToAvalonia(ToolSettings.SecondaryColor);
        set => ToolSettings.SecondaryColor = ToBgra(value);
    }

    private static Color ToAvalonia(ColorBgra c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    private static ColorBgra ToBgra(Color c) => ColorBgra.FromBgra(c.B, c.G, c.R, c.A);

    private void OnColorsChanged()
    {
        OnPropertyChanged(nameof(PrimaryColor));
        OnPropertyChanged(nameof(SecondaryColor));
        OnPropertyChanged(nameof(PrimaryBrush));
        OnPropertyChanged(nameof(SecondaryBrush));
        RefreshEditingTool();
    }

    [RelayCommand]
    private async Task PickPrimaryColor()
    {
        if (Dialogs is not null && await Dialogs.PickColorAsync("Primary Color", PrimaryColor) is { } c)
            PrimaryColor = c;
    }

    [RelayCommand]
    private async Task PickSecondaryColor()
    {
        if (Dialogs is not null && await Dialogs.PickColorAsync("Secondary Color", SecondaryColor) is { } c)
            SecondaryColor = c;
    }

    [RelayCommand]
    private void ResetColors() => (PrimaryColor, SecondaryColor) = (Colors.Black, Colors.White);

    public IBrush PrimaryBrush => new SolidColorBrush(PrimaryColor);
    public IBrush SecondaryBrush => new SolidColorBrush(SecondaryColor);

    /// <summary>Only this rectangle of the image changed (e.g. during a brush stroke); the view redraws just that.</summary>
    public event Action<RectangleI>? RegionInvalidated;

    /// <summary>Incremented whenever the canvas must be redrawn.</summary>
    [ObservableProperty]
    public partial int RenderVersion { get; set; }

    [ObservableProperty]
    public partial string CursorPositionText { get; set; } = "";

    /// <summary>Size of the visible canvas area, set by the view; used by Best Fit.</summary>
    public Avalonia.Size ViewportSize { get; set; }

    public bool HasDocument => ActiveDocument is not null;

    public string ImageSizeText => ActiveDocument is { } d
        ? $"{d.Document.ImageSize.Width} × {d.Document.ImageSize.Height}"
        : "";

    public string ZoomText => ActiveDocument is { } d ? $"{Math.Round(d.Document.Workspace.Scale * 100)}%" : "";

    public string WindowTitle => ActiveDocument is { } d ? $"{d.Title} - CinnabarSharp" : "CinnabarSharp";

    // ---- File ----

    [RelayCommand]
    private async Task NewImage()
    {
        if (Dialogs is null)
            return;
        var suggested = ActiveDocument?.Document.ImageSize ?? new ImageSize(800, 600);
        var options = await Dialogs.ShowNewImageAsync(suggested);
        if (options is not null)
            CreateImage(options);
    }

    public void CreateImage(NewImageOptions options)
    {
        var doc = _workspace.NewDocument(options.Size, options.Background);
        FitIfLargerThanViewport(doc);
    }

    [RelayCommand]
    private async Task Open()
    {
        if (Dialogs is null)
            return;
        foreach (var path in await Dialogs.PickFilesToOpenAsync(_formats.Formats))
            await OpenFileAsync(path);
    }

    [RelayCommand]
    private Task OpenRecent(string path) => OpenFileAsync(path);

    [RelayCommand]
    private void ClearRecent() => RecentFiles.Clear();

    public async Task<bool> OpenFileAsync(string path)
    {
        var alreadyOpen = Documents.Any(d => d.Document.File?.FullName == Path.GetFullPath(path));
        try
        {
            var doc = _formats.Open(new FileInfo(path));
            RecentFiles.Add(path);
            if (!alreadyOpen)
                FitIfLargerThanViewport(doc);
            return true;
        }
        catch (Exception e) when (e is NotSupportedException or MagickException or IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(path))
                RecentFiles.Remove(path);
            await (Dialogs?.ShowErrorAsync($"Could not open \"{Path.GetFileName(path)}\"", Describe(e)) ?? Task.CompletedTask);
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task Save() => ActiveDocument is { } d ? SaveDocumentAsync(d, saveAs: false) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task SaveAs() => ActiveDocument is { } d ? SaveDocumentAsync(d, saveAs: true) : Task.CompletedTask;

    /// <summary>Returns false if the user cancelled or the save failed.</summary>
    public async Task<bool> SaveDocumentAsync(DocumentViewModel d, bool saveAs)
    {
        var doc = d.Document;
        var file = saveAs ? null : doc.File;
        var format = file is null ? null : _formats.GetFormatByExtension(file.Extension);
        if (format is { SupportsSaving: false })
            format = null;

        if (file is null || format is null)
        {
            if (Dialogs is null)
                return false;
            var suggestedFormat = (doc.FileType is { } t ? _formats.GetFormatByExtension(t) : null) is { SupportsSaving: true } current
                ? current
                : _formats.GetFormatByExtension("png")!;
            var path = await Dialogs.PickFileToSaveAsync(doc.DisplayName, suggestedFormat, _formats.SaveFormats);
            if (path is null)
                return false;
            if (_formats.GetFormatByExtension(Path.GetExtension(path)) is not { SupportsSaving: true })
                path += "." + suggestedFormat.SupportedExtensions[0];
            file = new FileInfo(path);
            format = _formats.GetFormatByExtension(file.Extension)!;
        }

        if (doc.Layers.Count() > 1 && !format.SupportsLayers && Dialogs is not null
            && !await Dialogs.ConfirmAsync("Flatten image?",
                $"{format.DisplayName} files can't store layers, so the saved file will contain the visible layers merged into one. Your layers are kept in CinnabarSharp.",
                "Flatten and Save"))
            return false;

        if (format is JpegFormat jpeg)
        {
            if (Dialogs is not null)
            {
                if (await Dialogs.AskJpegQualityAsync(JpegQuality) is not { } quality)
                    return false;
                JpegQuality = quality;
            }
            jpeg.Quality = JpegQuality;
        }

        try
        {
            _formats.Save(doc, file, format);
            RecentFiles.Add(file.FullName);
            return true;
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
        {
            await (Dialogs?.ShowErrorAsync($"Could not save \"{file.Name}\"", Describe(e)) ?? Task.CompletedTask);
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task Close() => ActiveDocument is { } d ? CloseDocumentAsync(d) : Task.CompletedTask;

    [RelayCommand]
    private Task CloseTab(DocumentViewModel document) => CloseDocumentAsync(document);

    /// <summary>Asks to save unsaved changes first; returns false if the user cancelled.</summary>
    public async Task<bool> CloseDocumentAsync(DocumentViewModel d)
    {
        if (d.Document.IsDirty && Dialogs is not null)
        {
            ActiveDocument = d;
            switch (await Dialogs.AskSaveChangesAsync(d.Document.DisplayName))
            {
                case SaveChangesChoice.Cancel:
                    return false;
                case SaveChangesChoice.Save when !await SaveDocumentAsync(d, saveAs: false):
                    return false;
            }
        }
        _workspace.CloseDocument(d.Document);
        return true;
    }

    /// <summary>Closes every document, asking about unsaved changes; returns false if the user cancelled.</summary>
    public async Task<bool> CloseAllAsync()
    {
        foreach (var d in Documents.ToList())
        {
            if (!await CloseDocumentAsync(d))
                return false;
        }
        return true;
    }

    public bool HasUnsavedChanges => Documents.Any(d => d.Document.IsDirty);

    [RelayCommand]
    private Task About() => Dialogs?.ShowAboutAsync() ?? Task.CompletedTask;

    // ---- View ----

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ZoomIn() => SetZoomPercent(NextZoomIn(CurrentZoomPercent));

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ZoomOut() => SetZoomPercent(NextZoomOut(CurrentZoomPercent));

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void ActualSize() => SetZoomPercent(100);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void BestFit()
    {
        if (ActiveDocument is { } d)
            SetZoomPercent(BestFitPercent(d.Document));
    }

    public static double NextZoomIn(double percent) =>
        ZoomLevels.FirstOrDefault(z => z > percent + 0.01, ZoomLevels[^1]);

    public static double NextZoomOut(double percent) =>
        ZoomLevels.LastOrDefault(z => z < percent - 0.01, ZoomLevels[0]);

    public double CurrentZoomPercent => (ActiveDocument?.Document.Workspace.Scale ?? 1) * 100;

    private void SetZoomPercent(double percent)
    {
        if (ActiveDocument is not { } d)
            return;
        var scale = Math.Clamp(percent / 100, MinZoom, MaxZoom);
        if (Viewport is not null)
            Viewport.ZoomTo(scale);
        else
            d.Document.Workspace.Scale = scale;
    }

    // ---- Layers ----

    private ImageDocumentLayers? CurrentLayers => ActiveDocument?.Document.Layers;

    public bool CanDeleteLayer => CurrentLayers?.Count() > 1;
    public bool CanMergeLayerDown => CurrentLayers?.CurrentUserLayerIndex > 0;
    public bool CanMoveLayerUp => CurrentLayers is { } l && l.CurrentUserLayerIndex < l.Count() - 1;
    public bool CanMoveLayerDown => CurrentLayers?.CurrentUserLayerIndex > 0;
    public bool CanFlatten => CurrentLayers?.Count() > 1;

    private void EditLayers(Action<DocumentActions> edit)
    {
        if (ActiveDocument is not { } d)
            return;
        edit(d.Document.Actions);
        RefreshLayers();
        RefreshThumbnails();
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void AddNewLayer() => EditLayers(a => a.AddNewLayer());

    [RelayCommand(CanExecute = nameof(CanDeleteLayer))]
    private void DeleteLayer() => EditLayers(a => a.DeleteCurrentLayer());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void DuplicateLayer() => EditLayers(a => a.DuplicateCurrentLayer());

    [RelayCommand(CanExecute = nameof(CanMergeLayerDown))]
    private void MergeLayerDown() => EditLayers(a => a.MergeCurrentLayerDown());

    [RelayCommand(CanExecute = nameof(CanMoveLayerUp))]
    private void MoveLayerUp() => EditLayers(a => a.MoveCurrentLayerUp());

    [RelayCommand(CanExecute = nameof(CanMoveLayerDown))]
    private void MoveLayerDown() => EditLayers(a => a.MoveCurrentLayerDown());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FlipLayerHorizontal() => EditLayers(a => a.FlipCurrentLayerHorizontal());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FlipLayerVertical() => EditLayers(a => a.FlipCurrentLayerVertical());

    [RelayCommand(CanExecute = nameof(CanFlatten))]
    private void Flatten() => EditLayers(a => a.Flatten());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task ImportFromFile()
    {
        if (Dialogs is null || ActiveDocument is null)
            return;
        foreach (var path in await Dialogs.PickFilesToOpenAsync(_formats.Formats))
        {
            try
            {
                EditLayers(a => a.ImportFromFile(new FileInfo(path)));
            }
            catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
            {
                await Dialogs.ShowErrorAsync($"Could not import \"{Path.GetFileName(path)}\"", Describe(e));
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task LayerProperties()
    {
        if (Dialogs is null || ActiveDocument is not { } d)
            return;
        var layer = d.Document.Layers.CurrentUserLayer;
        var before = Core.Models.LayerProperties.From(layer);
        var properties = new LayerPropertiesViewModel(layer);
        if (await Dialogs.ShowLayerPropertiesAsync(properties))
            d.Document.Actions.CommitLayerProperties(layer, before);
        else
            properties.Revert();
    }

    // ---- History ----

    /// <summary>Steps of the active document; the selected one is the current state.</summary>
    public ObservableCollection<HistoryItemViewModel> History { get; } = [];

    [ObservableProperty]
    public partial HistoryItemViewModel? SelectedHistoryItem { get; set; }

    private IImageDocumentHistory? CurrentHistory => ActiveDocument?.Document.Workspace.History;

    public bool CanUndo => CurrentHistory?.CanUndo == true;
    public bool CanRedo => CurrentHistory?.CanRedo == true;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => CurrentHistory?.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => CurrentHistory?.Redo();

    partial void OnSelectedHistoryItemChanged(HistoryItemViewModel? value)
    {
        if (value is not null && !_syncingSelection && CurrentHistory is { } history && history.Pointer != value.Index)
            history.JumpTo(value.Index);
    }

    private void RefreshHistory()
    {
        var history = CurrentHistory;
        _syncingSelection = true;
        History.Clear();
        if (history is not null)
        {
            for (var i = 0; i < history.Items.Count; i++)
                History.Add(new HistoryItemViewModel(i, history.Items[i].Text, i > history.Pointer));
            SelectedHistoryItem = history.Pointer >= 0 ? History[history.Pointer] : null;
        }
        _syncingSelection = false;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // ---- Tools ----

    public static IReadOnlyList<SelectionMode> SelectionModes { get; } = Enum.GetValues<SelectionMode>();

    public SelectionMode SelectionMode
    {
        get => ToolSettings.SelectionMode;
        set { ToolSettings.SelectionMode = value; OnPropertyChanged(); }
    }

    public int Tolerance
    {
        get => ToolSettings.Tolerance;
        set { ToolSettings.Tolerance = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
    }

    public bool GlobalFill
    {
        get => ToolSettings.GlobalFill;
        set { ToolSettings.GlobalFill = value; OnPropertyChanged(); }
    }

    public int BrushWidth
    {
        get => ToolSettings.BrushWidth;
        set
        {
            ToolSettings.BrushWidth = Math.Clamp(value, 1, 500);
            OnPropertyChanged();
            OnPropertyChanged(nameof(BrushOutlineSize));
            RefreshEditingTool();
        }
    }

    public bool Antialiasing
    {
        get => ToolSettings.Antialiasing;
        set { ToolSettings.Antialiasing = value; OnPropertyChanged(); RefreshEditingTool(); }
    }

    public int Hardness
    {
        get => ToolSettings.Hardness;
        set { ToolSettings.Hardness = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
    }

    /// <summary>Diameter (image pixels) of the outline drawn under the pointer for round brushes; 0 for none.</summary>
    public double BrushOutlineSize => SelectedTool?.IsBrush == true ? BrushWidth : 0;

    public int CornerRadius
    {
        get => ToolSettings.CornerRadius;
        set { ToolSettings.CornerRadius = Math.Clamp(value, 0, 1000); OnPropertyChanged(); }
    }

    public bool GradientTransparency
    {
        get => ToolSettings.GradientTransparency;
        set { ToolSettings.GradientTransparency = value; OnPropertyChanged(); }
    }

    // ---- Text tool options ----

    private IReadOnlyList<string>? _fontFamilies;

    public IReadOnlyList<string> FontFamilies => _fontFamilies ??= AvaloniaTextRasterizer.FontFamilies;

    public static IReadOnlyList<Core.Models.TextAlignment> TextAlignments { get; } = Enum.GetValues<Core.Models.TextAlignment>();

    /// <summary>Font of the Text tool; the platform default font when none was chosen.</summary>
    public string FontFamily
    {
        get => string.IsNullOrEmpty(ToolSettings.FontFamily) ? AvaloniaTextRasterizer.DefaultFontFamily : ToolSettings.FontFamily;
        set { ToolSettings.FontFamily = value ?? ""; OnPropertyChanged(); RefreshEditingTool(); }
    }

    public double FontSize
    {
        get => ToolSettings.FontSize;
        set { ToolSettings.FontSize = Math.Clamp(value, 1, 1000); OnPropertyChanged(); RefreshEditingTool(); }
    }

    public bool Bold
    {
        get => ToolSettings.Bold;
        set { ToolSettings.Bold = value; OnPropertyChanged(); RefreshEditingTool(); }
    }

    public bool Italic
    {
        get => ToolSettings.Italic;
        set { ToolSettings.Italic = value; OnPropertyChanged(); RefreshEditingTool(); }
    }

    public bool Underline
    {
        get => ToolSettings.Underline;
        set { ToolSettings.Underline = value; OnPropertyChanged(); RefreshEditingTool(); }
    }

    public Core.Models.TextAlignment TextAlignment
    {
        get => ToolSettings.TextAlignment;
        set { ToolSettings.TextAlignment = value; OnPropertyChanged(); RefreshEditingTool(); }
    }

    public static IReadOnlyList<ShapeKind> ShapeKinds { get; } = Enum.GetValues<ShapeKind>();
    public static IReadOnlyList<ShapeStyle> ShapeStyles { get; } = Enum.GetValues<ShapeStyle>();
    public static IReadOnlyList<GradientKind> GradientKinds { get; } = Enum.GetValues<GradientKind>();

    public ShapeKind ShapeKind
    {
        get => ToolSettings.ShapeKind;
        set { ToolSettings.ShapeKind = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowCornerRadiusOptions)); }
    }

    public ShapeStyle ShapeStyle
    {
        get => ToolSettings.ShapeStyle;
        set { ToolSettings.ShapeStyle = value; OnPropertyChanged(); }
    }

    public GradientKind GradientKind
    {
        get => ToolSettings.GradientKind;
        set { ToolSettings.GradientKind = value; OnPropertyChanged(); }
    }

    public bool SampleImage
    {
        get => ToolSettings.SampleImage;
        set { ToolSettings.SampleImage = value; OnPropertyChanged(); }
    }

    /// <summary>Restores tool options, colors and the selected tool saved by <see cref="CaptureSettings"/>.</summary>
    public void ApplySettings(AppSettings settings)
    {
        ToolSettings.PrimaryColor = ColorBgra.FromUInt32(settings.PrimaryColor);
        ToolSettings.SecondaryColor = ColorBgra.FromUInt32(settings.SecondaryColor);
        BrushWidth = settings.BrushWidth;
        Antialiasing = settings.Antialiasing;
        Hardness = settings.Hardness;
        CornerRadius = settings.CornerRadius;
        GradientTransparency = settings.GradientTransparency;
        ToolSettings.FontFamily = settings.FontFamily ?? "";
        FontSize = settings.FontSize;
        Bold = settings.Bold;
        Italic = settings.Italic;
        Underline = settings.Underline;
        TextAlignment = settings.TextAlignment;
        ToolSettings.CropAspect = settings.CropAspect;
        JpegQuality = settings.JpegQuality;
        TvOptions = new TvOptions(settings.TvResolution, settings.TvFit, settings.TvBackground);
        Tolerance = settings.Tolerance;
        GlobalFill = settings.GlobalFill;
        SampleImage = settings.SampleImage;
        SelectionMode = settings.SelectionMode;
        ShapeKind = settings.ShapeKind;
        ShapeStyle = settings.ShapeStyle;
        GradientKind = settings.GradientKind;
        AllowAgents = settings.AllowAgents;
        if (Tools.FirstOrDefault(t => t.Name == settings.SelectedTool) is { } tool)
            SelectedTool = tool;
    }

    public AppSettings CaptureSettings(AppSettings window) => window with
    {
        SelectedTool = SelectedTool.Name,
        PrimaryColor = ToolSettings.PrimaryColor.Bgra,
        SecondaryColor = ToolSettings.SecondaryColor.Bgra,
        BrushWidth = BrushWidth,
        Antialiasing = Antialiasing,
        Hardness = Hardness,
        CornerRadius = CornerRadius,
        GradientTransparency = GradientTransparency,
        FontFamily = ToolSettings.FontFamily,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        TextAlignment = TextAlignment,
        CropAspect = ToolSettings.CropAspect,
        JpegQuality = JpegQuality,
        TvResolution = TvOptions.Resolution,
        TvFit = TvOptions.Fit,
        TvBackground = TvOptions.Background,
        Tolerance = Tolerance,
        GlobalFill = GlobalFill,
        SampleImage = SampleImage,
        SelectionMode = SelectionMode,
        ShapeKind = ShapeKind,
        ShapeStyle = ShapeStyle,
        GradientKind = GradientKind,
        AllowAgents = AllowAgents,
    };

    /// <summary>Selects the next tool with this Paint.NET shortcut letter (pressing S again cycles the select tools).</summary>
    public void SelectToolByShortcut(string letter)
    {
        var matches = Tools.Where(t => t.Shortcut.Equals(letter, StringComparison.OrdinalIgnoreCase) && (t.Tool is not null || t.Name is "Pan" or "Zoom")).ToList();
        if (matches.Count == 0)
            return;
        var index = matches.IndexOf(SelectedTool);
        SelectedTool = matches[(index + 1) % matches.Count];
    }

    public void ToolPointerDown(ToolPointer pointer) => WithTool(t => t.OnPointerDown, pointer);
    public void ToolPointerMove(ToolPointer pointer) => WithTool(t => t.OnPointerMove, pointer);

    public void ToolPointerUp(ToolPointer pointer)
    {
        WithTool(t => t.OnPointerUp, pointer);
        if (SelectedTool.Tool is IEditingTool)
            RefreshThumbnails();
    }

    private void WithTool(Func<ITool, Action<ImageDocument, ToolPointer>> handler, ToolPointer pointer)
    {
        if (ActiveDocument is { } d && SelectedTool.Tool is { } tool)
            handler(tool)(d.Document, pointer);
        UpdateOverlay();
    }

    /// <summary>True while keys typed belong to the selected tool (the Text tool is editing).</summary>
    public bool IsTyping => ActiveDocument is { } d && SelectedTool.Tool is IKeyboardTool k && k.IsTyping(d.Document);

    /// <summary>
    /// Sends a key to the selected tool; returns true if it was used. Escape that no tool uses (to cancel a crop
    /// frame, finish a text...) deselects.
    /// </summary>
    public bool ToolKeyDown(ToolKey key, ToolModifiers modifiers)
    {
        if (ActiveDocument is not { } d)
            return false;
        var handled = SelectedTool.Tool is IKeyboardTool tool && tool.OnKeyDown(d.Document, key, modifiers);
        if (!handled && key == ToolKey.Escape && modifiers == ToolModifiers.None && d.Document.HasSelection)
        {
            d.Document.Actions.DeselectAll();
            handled = true;
        }
        UpdateOverlay();
        return handled;
    }

    public void ToolTextInput(string text)
    {
        if (ActiveDocument is not { } d || SelectedTool.Tool is not IKeyboardTool tool)
            return;
        tool.OnTextInput(d.Document, text);
        UpdateOverlay();
    }

    private (TextTool Tool, ImageDocument Document)? EditingText =>
        ActiveDocument is { } d && SelectedTool.Tool is TextTool t && t.IsEditing(d.Document) ? (t, d.Document) : null;

    /// <summary>What the selected tool draws over the canvas (curve handles, text caret...).</summary>
    [ObservableProperty]
    public partial ToolOverlay? Overlay { get; set; }

    private void UpdateOverlay()
    {
        Overlay = ActiveDocument is { } d && SelectedTool?.Tool is IOverlayTool tool ? tool.GetOverlay(d.Document) : null;
        ApplyCropCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Redraws the curve or text being edited after a color or option change.</summary>
    private void RefreshEditingTool()
    {
        if (ActiveDocument is not { } d || SelectedTool?.Tool is not IEditingTool tool || !tool.IsEditing(d.Document))
            return;
        tool.Refresh(d.Document);
        UpdateOverlay();
    }

    private void FinishEditing(ITool? tool, ImageDocument? document)
    {
        if (document is null || tool is not IEditingTool editing || !editing.IsEditing(document))
            return;
        editing.Finish(document);
        RefreshThumbnails();
    }

    // The generator declares oldValue non-nullable, but it is null on the first assignment.
    partial void OnSelectedToolChanged(ToolViewModel oldValue, ToolViewModel newValue) =>
        FinishEditing(oldValue?.Tool, ActiveDocument?.Document);

    partial void OnActiveDocumentChanging(DocumentViewModel? oldValue, DocumentViewModel? newValue) =>
        FinishEditing(SelectedTool?.Tool, oldValue?.Document);

    partial void OnSelectedToolChanged(ToolViewModel value)
    {
        foreach (var name in new[]
                 {
                     nameof(ShowSelectionOptions), nameof(ShowToleranceOptions), nameof(ShowBrushOptions),
                     nameof(ShowShapeOptions), nameof(ShowGradientOptions), nameof(ShowColorPickerOptions),
                     nameof(ShowHardnessOptions), nameof(ShowCornerRadiusOptions), nameof(ShowTextOptions),
                     nameof(ShowCropOptions),
                     nameof(BrushOutlineSize),
                 })
            OnPropertyChanged(name);
        UpdateOverlay();
    }

    public bool ShowSelectionOptions => SelectedTool.IsSelectionTool;
    public bool ShowToleranceOptions => SelectedTool.HasTolerance;
    public bool ShowBrushOptions => SelectedTool.HasBrushWidth;
    public bool ShowHardnessOptions => SelectedTool.IsBrush;
    public bool ShowShapeOptions => SelectedTool.IsShapes;
    public bool ShowCornerRadiusOptions => SelectedTool.IsShapes && ShapeKind == ShapeKind.RoundedRectangle;
    public bool ShowGradientOptions => SelectedTool.IsGradient;
    public bool ShowColorPickerOptions => SelectedTool.IsColorPicker;
    public bool ShowTextOptions => SelectedTool.IsText;
    public bool ShowCropOptions => SelectedTool.Tool is CropTool;

    // ---- Selection and clipboard ----

    /// <summary>Incremented when the selection changes, to redraw the marching ants.</summary>
    [ObservableProperty]
    public partial int SelectionVersion { get; set; }

    public bool HasSelection => ActiveDocument?.Document.HasSelection == true;

    public string SelectionSizeText => ActiveDocument?.Document.Selection is { } s
        ? $"Selection {s.Bounds.Width} × {s.Bounds.Height}"
        : "";

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void SelectAll()
    {
        if (EditingText is { } text)
        {
            text.Tool.SelectAll(text.Document);
            UpdateOverlay();
            return;
        }
        ActiveDocument?.Document.Actions.SelectAll();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeselectAll() => ActiveDocument?.Document.Actions.DeselectAll();

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void InvertSelection() => ActiveDocument?.Document.Actions.InvertSelection();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CropToSelection() => ActiveDocument?.Document.Actions.CropToSelection();

    // Delete and Backspace are also these menu items' shortcuts: while typing text they edit the text instead.
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void EraseSelection()
    {
        if (!IsTyping || !ToolKeyDown(ToolKey.Delete, ToolModifiers.None))
            EditLayers(a => a.EraseSelection());
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FillSelection()
    {
        if (!IsTyping || !ToolKeyDown(ToolKey.Backspace, ToolModifiers.None))
            EditLayers(a => a.FillSelection(ColorBgra.FromBgra(PrimaryColor.B, PrimaryColor.G, PrimaryColor.R, PrimaryColor.A)));
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task Copy() => CopyAsync(merged: false);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task CopyMerged() => CopyAsync(merged: true);

    private async Task CopyAsync(bool merged)
    {
        if (EditingText is { } text && Clipboard is not null)
        {
            await text.Tool.Copy(Clipboard);
            return;
        }
        if (ActiveDocument is { } d && Clipboard is not null)
            await Clipboard.SetImageAsync(d.Document.Actions.Copy(merged));
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task Cut()
    {
        if (ActiveDocument is not { } d || Clipboard is null)
            return;
        if (EditingText is { } text)
        {
            await text.Tool.Cut(text.Document, Clipboard);
            UpdateOverlay();
            return;
        }
        ClipboardImage? image = null;
        EditLayers(a => image = a.Cut());
        await Clipboard.SetImageAsync(image!);
    }

    /// <summary>Pastes onto the current layer and switches to Move Selected Pixels; with no image open, pastes into a new image.</summary>
    [RelayCommand]
    private async Task Paste()
    {
        if (EditingText is { } text && Clipboard is not null)
        {
            await text.Tool.Paste(text.Document, Clipboard);
            UpdateOverlay();
            return;
        }
        if (await ClipboardImageAsync() is not { } image)
            return;
        if (ActiveDocument is not { } d)
        {
            PasteImage(image);
            return;
        }
        d.Document.Actions.Paste(image, PasteLocation());
        SelectedTool = Tools.First(t => t.Tool is MoveSelectedPixelsTool);
        RefreshThumbnails();
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task PasteIntoNewLayer()
    {
        if (ActiveDocument is not { } d || await ClipboardImageAsync() is not { } image)
            return;
        EditLayers(a => a.PasteIntoNewLayer(image, PasteLocation()));
        SelectedTool = Tools.First(t => t.Tool is MoveSelectedPixelsTool);
    }

    [RelayCommand]
    private async Task PasteIntoNewImage()
    {
        if (await ClipboardImageAsync() is { } image)
            PasteImage(image);
    }

    private void PasteImage(ClipboardImage image)
    {
        var doc = _workspace.NewDocumentFromImage(image);
        FitIfLargerThanViewport(doc);
    }

    private async Task<ClipboardImage?> ClipboardImageAsync()
    {
        if (Clipboard is null)
            return null;
        var image = await Clipboard.GetImageAsync();
        if (image is null && Dialogs is not null)
            await Dialogs.ShowErrorAsync("Nothing to paste", "The clipboard doesn't contain an image.");
        return image;
    }

    /// <summary>Paint.NET pastes at the top-left of the visible part of the canvas.</summary>
    private PointI PasteLocation() => Viewport?.VisibleImageOrigin() ?? PointI.Zero;

    // ---- Adjustments ----

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task AutoLevel() => RunAdjustment(new AutoLevel());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task BlackAndWhite() => RunAdjustment(new BlackAndWhite());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task BrightnessContrast() => RunAdjustment(new BrightnessContrast());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task HueSaturation() => RunAdjustment(new HueSaturation());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task InvertColors() => RunAdjustment(new InvertColors());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task Levels() => RunAdjustment(new Levels());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task Curves() => RunAdjustment(new Core.Adjustments.Curves());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task Posterize() => RunAdjustment(new Posterize());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task Sepia() => RunAdjustment(new Sepia());

    private Task RunAdjustment(ColorAdjustment adjustment) => RunEffect(adjustment);

    // ---- Effects ----

    private (Effect Effect, IReadOnlyList<double> Values)? _lastEffect;

    public string RepeatEffectText => _lastEffect is { } last ? $"Repeat {last.Effect.Name}" : "Repeat Last Effect";

    /// <summary>
    /// Operations without parameters apply at once; others open a live-preview dialog. The final result is
    /// computed in the background, then applied as one history step.
    /// </summary>
    public async Task RunEffect(Effect effect)
    {
        if (ActiveDocument is not { } d)
            return;
        var session = new EffectSession(d.Document, effect, ToolSettings.PrimaryColor, ToolSettings.SecondaryColor);
        if (effect.Parameters.Count == 0 && !effect.HasCustomDialog)
        {
            await ApplyAsync(session, effect.Defaults);
            return;
        }
        if (Dialogs is null)
            return;

        PreviewDialogViewModel dialog = effect switch
        {
            Core.Adjustments.Curves => new CurvesDialogViewModel(session),
            Core.Adjustments.Levels => new LevelsDialogViewModel(session),
            PhotoFilterEffect => new PhotoFilterDialogViewModel(session),
            _ => new EffectDialogViewModel(session),
        };
        dialog.RequestPreview();
        var ok = dialog switch
        {
            CurvesDialogViewModel curves => await Dialogs.ShowCurvesAsync(curves),
            LevelsDialogViewModel levels => await Dialogs.ShowLevelsAsync(levels),
            PhotoFilterDialogViewModel filters => await Dialogs.ShowPhotoFilterAsync(filters),
            _ => await Dialogs.ShowEffectAsync((EffectDialogViewModel)dialog),
        };
        if (ok)
            await dialog.CommitAsync();
        else
            dialog.Cancel();
        if (dialog.Committed && effect is not ColorAdjustment)
            RememberEffect(effect, dialog.Values);
    }

    [RelayCommand(CanExecute = nameof(CanRepeatEffect))]
    private async Task RepeatEffect()
    {
        if (ActiveDocument is not { } d || _lastEffect is not { } last)
            return;
        await ApplyAsync(new EffectSession(d.Document, last.Effect, ToolSettings.PrimaryColor, ToolSettings.SecondaryColor), last.Values);
    }

    public bool CanRepeatEffect => HasDocument && _lastEffect is not null;

    private void RememberEffect(Effect effect, IReadOnlyList<double> values)
    {
        _lastEffect = (effect, values);
        OnPropertyChanged(nameof(RepeatEffectText));
        RepeatEffectCommand.NotifyCanExecuteChanged();
    }

    private static async Task ApplyAsync(EffectSession session, IReadOnlyList<double> values)
    {
        var pixels = await Task.Run(() => session.Compute(values));
        session.Show(pixels);
        session.Commit();
    }

    [RelayCommand]
    private Task ApplyEffect(Effect effect) => RunEffect(effect);

    // ---- Photo: crop and TV ----

    /// <summary>Quality of saved JPEGs, asked on each save and remembered.</summary>
    public int JpegQuality { get; set; } = JpegFormat.DefaultQuality;

    /// <summary>Last choices of the Prepare for TV dialog.</summary>
    public TvOptions TvOptions { get; set; } = new(TvResolution.Uhd4K, TvFit.CropToFill);

    public sealed record CropAspectOption(CropAspect Value, string Label);

    public static IReadOnlyList<CropAspectOption> CropAspects { get; } =
    [
        new(CropAspect.Wide, "16:9 (TV)"),
        new(CropAspect.Tall, "9:16"),
        new(CropAspect.Standard, "4:3"),
        new(CropAspect.Photo, "3:2"),
        new(CropAspect.Square, "1:1"),
        new(CropAspect.Free, "Free"),
    ];

    public CropAspectOption SelectedCropAspect
    {
        get => CropAspects.First(a => a.Value == ToolSettings.CropAspect);
        set
        {
            ToolSettings.CropAspect = value?.Value ?? CropAspect.Free;
            OnPropertyChanged();
            RefreshEditingTool();
        }
    }

    private CropTool? CropTool => Tools.Select(t => t.Tool).OfType<CropTool>().FirstOrDefault();

    public bool HasCropFrame => ActiveDocument is { } d && CropTool?.IsEditing(d.Document) == true;

    [RelayCommand(CanExecute = nameof(HasCropFrame))]
    private void ApplyCrop()
    {
        if (ActiveDocument is { } d)
            CropTool?.Apply(d.Document);
        UpdateOverlay();
    }

    /// <summary>What Crop to fill keeps: the crop frame, else the selection, else the center.</summary>
    private RectangleI? TvCropArea(ImageDocument doc) =>
        CropTool?.Frame(doc) ?? doc.Selection?.Bounds;

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task PrepareForTv()
    {
        if (Dialogs is null || ActiveDocument is not { } d)
            return;
        var doc = d.Document;
        var crop = TvCropArea(doc);
        var options = new PrepareForTvViewModel(TvOptions, doc.ImageSize, crop,
            Documents.Where(o => o != d).ToList());
        if (!await Dialogs.ShowPrepareForTvAsync(options))
            return;
        TvOptions = options.Options;

        var photo = new BgraImage(doc.Layers.GetFlattenedBgra(includeToolLayer: false), doc.ImageSize.Width, doc.ImageSize.Height);
        BgraImage? second = null;
        if (options.SideBySide && options.SecondPhoto?.Document is { } other)
            second = new BgraImage(other.Layers.GetFlattenedBgra(includeToolLayer: false), other.ImageSize.Width, other.ImageSize.Height);
        var tvOptions = options.Options;
        var result = await Task.Run(() => second is null
            ? TvExport.Compose(photo, tvOptions, crop)
            : TvExport.SideBySide(photo, second, tvOptions));

        var name = Path.GetFileNameWithoutExtension(doc.DisplayName);
        var tv = _workspace.NewDocumentFromImage(new ClipboardImage(result.Pixels, result.Width, result.Height));
        tv.DisplayName = name + TvExport.Suffix(tvOptions.Resolution);
        tv.FileType = "jpg";
        FitIfLargerThanViewport(tv);
    }

    [RelayCommand]
    private async Task PrepareFolderForTv()
    {
        if (Dialogs is null || await Dialogs.PickFolderAsync("Choose a folder of photos") is not { } folder)
            return;
        var options = new PrepareForTvViewModel(TvOptions, folder: folder);
        if (!await Dialogs.ShowPrepareForTvAsync(options) || await Dialogs.AskJpegQualityAsync(JpegQuality) is not { } quality)
            return;
        TvOptions = options.Options;
        JpegQuality = quality;
        var tvOptions = options.Options;
        var (output, count) = await Task.Run(() => TvExport.ExportFolder(new DirectoryInfo(folder), tvOptions, quality));
        await Dialogs.ShowMessageAsync("Photos ready for the TV",
            count == 0 ? "No photos were found in this folder." : $"{count} photo{(count > 1 ? "s" : "")} saved in \"{output.FullName}\".");
    }

    // ---- Image ----

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task ResizeImage()
    {
        if (Dialogs is null || ActiveDocument is not { } d)
            return;
        if (await Dialogs.ShowResizeImageAsync(d.Document.ImageSize) is { } options)
            d.Document.Actions.ResizeImage(options.Size, options.Resampling);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task CanvasSize()
    {
        if (Dialogs is null || ActiveDocument is not { } d)
            return;
        if (await Dialogs.ShowCanvasSizeAsync(d.Document.ImageSize) is { } options)
            d.Document.Actions.ResizeCanvas(options.Size, options.Anchor, ToolSettings.SecondaryColor);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FlipImageHorizontal() => ActiveDocument?.Document.Actions.FlipImageHorizontal();

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FlipImageVertical() => ActiveDocument?.Document.Actions.FlipImageVertical();

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void RotateClockwise() => ActiveDocument?.Document.Actions.RotateImage90(clockwise: true);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void RotateCounterClockwise() => ActiveDocument?.Document.Actions.RotateImage90(clockwise: false);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void Rotate180() => ActiveDocument?.Document.Actions.RotateImage180();

    // ---- Colors ----

    [RelayCommand]
    private void SwapColors() => (PrimaryColor, SecondaryColor) = (SecondaryColor, PrimaryColor);

    /// <summary>Disabled placeholder for menu items implemented in later phases.</summary>
    public IRelayCommand NotYetImplemented { get; } = new RelayCommand(() => { }, () => false);

    public void UpdateCursorPosition(Core.Models.PointD? canvasPoint)
    {
        CursorPositionText = canvasPoint is { } p ? $"{(int)Math.Floor(p.X)}, {(int)Math.Floor(p.Y)}" : "";
        HoverCursor = canvasPoint is { } point && ActiveDocument is { } d && SelectedTool.Tool is IOverlayTool tool
            ? tool.CursorAt(d.Document, point)
            : ToolCursor.Default;
    }

    /// <summary>What the selected tool wants the cursor to show under the mouse (e.g. resize arrows over a handle).</summary>
    [ObservableProperty]
    public partial ToolCursor HoverCursor { get; set; }

    partial void OnActiveDocumentChanged(DocumentViewModel? value)
    {
        if (value is not null && !_syncingSelection)
            _workspace.SetActiveDocument(value.Document);
        RefreshLayers();
        RefreshHistory();
        RefreshSelectionState();
        RefreshViewState();
        UpdateOverlay();
        foreach (var command in new IRelayCommand[]
                 {
                     ZoomInCommand, ZoomOutCommand, ActualSizeCommand, BestFitCommand,
                     SaveCommand, SaveAsCommand, CloseCommand,
                     SelectAllCommand, InvertSelectionCommand, EraseSelectionCommand, FillSelectionCommand,
                     CopyCommand, CopyMergedCommand, CutCommand, PasteIntoNewLayerCommand,
                     AutoLevelCommand, BlackAndWhiteCommand, BrightnessContrastCommand, HueSaturationCommand,
                     InvertColorsCommand, LevelsCommand, CurvesCommand, PosterizeCommand, SepiaCommand,
                     RepeatEffectCommand, ApplyEffectCommand,
                     ResizeImageCommand, CanvasSizeCommand, FlipImageHorizontalCommand, FlipImageVerticalCommand, PrepareForTvCommand,
                     RotateClockwiseCommand, RotateCounterClockwiseCommand, Rotate180Command,
                 })
            command.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasDocument));
    }

    partial void OnSelectedLayerChanged(LayerViewModel? value)
    {
        if (value is not null && !_syncingSelection && ActiveDocument is { } d)
            d.Document.Layers.SetCurrentUserLayer(value.Layer);
    }

    private double BestFitPercent(ImageDocument doc)
    {
        if (ViewportSize.Width <= 0 || ViewportSize.Height <= 0)
            return 100;
        var fit = Math.Min(ViewportSize.Width / doc.ImageSize.Width, ViewportSize.Height / doc.ImageSize.Height);
        return Math.Min(100, fit * 100);
    }

    private void FitIfLargerThanViewport(ImageDocument doc)
    {
        var fit = BestFitPercent(doc);
        if (fit < 100)
            doc.Workspace.Scale = Math.Max(MinZoom, fit / 100);
    }

    private static string Describe(Exception e) => e is UnauthorizedAccessException
        ? "You don't have permission to access this file."
        : e.Message;

    private void OnDocumentEvent(EventItem<DocumentEventEnum> e)
    {
        switch (e.State)
        {
            case DocumentEventEnum.DocumentCreated:
                Documents.Add(new DocumentViewModel(e.Document));
                break;

            case DocumentEventEnum.DocumentClosed:
                var closed = Documents.FirstOrDefault(d => d.Document == e.Document);
                if (closed is null)
                    break;
                Documents.Remove(closed);
                if (ActiveDocument == closed)
                {
                    _syncingSelection = true;
                    ActiveDocument = null;
                    _syncingSelection = false;
                }
                OnPropertyChanged(nameof(HasUnsavedChanges));
                break;

            case DocumentEventEnum.ActiveDocumentChanged:
                _syncingSelection = true;
                ActiveDocument = Documents.FirstOrDefault(d => d.Document == e.Document);
                _syncingSelection = false;
                break;

            case DocumentEventEnum.DocumentRenamed:
            case DocumentEventEnum.DirtyChanged:
                Documents.FirstOrDefault(d => d.Document == e.Document)?.Refresh();
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                break;

            case DocumentEventEnum.SelectionChanged:
                if (e.Document == ActiveDocument?.Document)
                    RefreshSelectionState();
                break;

            case DocumentEventEnum.HistoryChanged:
                if (e.Document == ActiveDocument?.Document)
                {
                    RefreshHistory();
                    RefreshThumbnails();
                    UpdateOverlay();
                }
                break;

            case DocumentEventEnum.LayerPropertyChanged:
            case DocumentEventEnum.LayerAdded:
            case DocumentEventEnum.LayerRemoved:
            case DocumentEventEnum.SelectedLayerChanged:
                if (e.Document == ActiveDocument?.Document)
                {
                    RefreshLayers();
                    RenderVersion++;
                }
                break;

            case DocumentEventEnum.CanvasInvalidated:
                if (e.Document != ActiveDocument?.Document)
                    break;
                if (e is CanvasEventItem { Rect.IsEmpty: false } region)
                    RegionInvalidated?.Invoke(region.Rect);
                else
                    RefreshViewState();
                break;

            case DocumentEventEnum.ViewSizeChanged:
                if (e.Document == ActiveDocument?.Document)
                    RefreshViewState();
                break;
        }
    }

    private void RefreshViewState()
    {
        RenderVersion++;
        OnPropertyChanged(nameof(ImageSizeText));
        OnPropertyChanged(nameof(ZoomText));
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void RefreshLayers()
    {
        var layers = ActiveDocument?.Document.Layers;
        var current = layers?.UserLayers.Reverse().ToList() ?? [];

        _syncingSelection = true;
        if (current.SequenceEqual(Layers.Select(l => l.Layer)))
        {
            foreach (var vm in Layers)
                vm.Refresh();
        }
        else
        {
            Layers.Clear();
            foreach (var layer in current)
                Layers.Add(new LayerViewModel(layer, ActiveDocument!.Document.Actions));
        }

        SelectedLayer = layers is { } l && l.CurrentUserLayerIndex >= 0
            ? Layers.FirstOrDefault(vm => vm.Layer == l.CurrentUserLayer)
            : null;
        _syncingSelection = false;

        foreach (var command in new IRelayCommand[]
                 {
                     AddNewLayerCommand, DeleteLayerCommand, DuplicateLayerCommand, MergeLayerDownCommand,
                     MoveLayerUpCommand, MoveLayerDownCommand, FlipLayerHorizontalCommand, FlipLayerVerticalCommand,
                     FlattenCommand, ImportFromFileCommand, LayerPropertiesCommand,
                 })
            command.NotifyCanExecuteChanged();
    }

    private void RefreshSelectionState()
    {
        SelectionVersion++;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSizeText));
        DeselectAllCommand.NotifyCanExecuteChanged();
        CropToSelectionCommand.NotifyCanExecuteChanged();
    }

    private void RefreshThumbnails()
    {
        foreach (var layer in Layers)
            layer.RefreshThumbnail();
    }

    public void Dispose() => _eventsSubscription.Dispose();

    private sealed class EventObserver(Action<EventItem<DocumentEventEnum>> onNext) : IObserver<EventItem<DocumentEventEnum>>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(EventItem<DocumentEventEnum> value) => onNext(value);
    }
}
