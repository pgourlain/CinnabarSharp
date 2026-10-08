using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Adjustments;
using CinnabarSharp.Core.Effects;
using Effect = CinnabarSharp.Core.Effects.Effect;
using CinnabarSharp.Core.Tools;
using CinnabarSharp.Core.Vector.Tools;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Core.Vector;
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
        CreateVectorTools();
        Properties = new SvgPropertiesViewModel(this);
        ToolSettings.ColorsChanged += OnColorsChanged;
        ToolSettings.BubbleNumberChanged += () => OnPropertyChanged(nameof(BubbleNextNumber));
        SelectedTool = Tools.First(t => t.Name == "Rectangle Select");
        _eventsSubscription = events.DocumentEvents.Subscribe(new EventObserver(OnDocumentEvent));
        RecentFiles.Changed += RefreshWelcome;
    }

    public IDialogService? Dialogs { get; set; }
    public IViewportService? Viewport { get; set; }
    public RecentFilesStore RecentFiles { get; }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    /// <summary>Top-most layer first, like Paint.NET's Layers panel.</summary>
    public ObservableCollection<LayerViewModel> Layers { get; } = [];

    public ToolSettings ToolSettings { get; } = new();

    public ToolViewModel[] Tools { get; }

    /// <summary>The Properties panel of drawings: fill, stroke, opacity and geometry of the selected objects.</summary>
    public SvgPropertiesViewModel Properties { get; }

    public IClipboardService? Clipboard { get; set; }

    /// <summary>MCP attached mode (File › Allow AI Agents); not set in tests.</summary>
    public AgentConnection? Agents
    {
        get => _agents;
        set
        {
            if (_agents is not null)
                _agents.Changed -= OnAgentsChanged;
            _agents = value;
            if (_agents is not null)
                _agents.Changed += OnAgentsChanged;
        }
    }

    private AgentConnection? _agents;

    // Turning the option on shows how to connect, but not when it comes back on with the saved settings.
    private bool _restoringSettings;

    private void OnAgentsChanged() => OnPropertyChanged(nameof(AgentStatusText));

    /// <summary>Status bar text while AI agents are allowed ("Waiting for an AI agent", "1 AI agent connected").</summary>
    public string AgentStatusText => Agents is { } agents && AllowAgents ? AgentConnectionViewModel.StatusFor(agents) : "";

    /// <summary>Whether AI agents connected with "CinnabarSharp --mcp --attach" can edit the open images.</summary>
    [ObservableProperty]
    public partial bool AllowAgents { get; set; }

    partial void OnAllowAgentsChanged(bool value)
    {
        OnPropertyChanged(nameof(AgentStatusText));
        ShowAgentConnectionCommand.NotifyCanExecuteChanged();
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
        {
            if (!_restoringSettings)
                _ = ShowAgentConnection();
            return;
        }
        AllowAgents = false;
        Dialogs?.ShowErrorAsync("Can't accept AI agents", error);
    }

    [RelayCommand]
    private void ToggleAllowAgents() => AllowAgents = !AllowAgents;

    /// <summary>Shows the commands that connect Claude Code or Claude Desktop to this window.</summary>
    [RelayCommand(CanExecute = nameof(AllowAgents))]
    private async Task ShowAgentConnection()
    {
        if (Dialogs is null || Agents is null)
            return;
        using var connection = new AgentConnectionViewModel(Agents, Clipboard);
        await Dialogs.ShowAgentConnectionAsync(connection);
    }

    [ObservableProperty]
    public partial DocumentViewModel? ActiveDocument { get; set; }

    [ObservableProperty]
    public partial LayerViewModel? SelectedLayer { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusToolName))]
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
        OnPropertyChanged(nameof(PrimaryColorHex));
        OnPropertyChanged(nameof(SecondaryColorHex));
        OnPropertyChanged(nameof(PrimaryColorRgbText));
        OnPropertyChanged(nameof(SecondaryColorRgbText));
        OnPropertyChanged(nameof(PrimaryColorDetails));
        OnPropertyChanged(nameof(SecondaryColorDetails));
        OnPropertyChanged(nameof(PrimaryColorFormats));
        OnPropertyChanged(nameof(SecondaryColorFormats));
        RefreshEditingTool();
    }

    // ---- Color panel text: hex ("html syntax"), RGB, and a tooltip with HSV and alpha ----

    public string PrimaryColorHex => HexText(ToolSettings.PrimaryColor);
    public string SecondaryColorHex => HexText(ToolSettings.SecondaryColor);
    public string PrimaryColorRgbText => RgbText(ToolSettings.PrimaryColor);
    public string SecondaryColorRgbText => RgbText(ToolSettings.SecondaryColor);
    public string PrimaryColorDetails => ColorDetails(ToolSettings.PrimaryColor);
    public string SecondaryColorDetails => ColorDetails(ToolSettings.SecondaryColor);

    /// <summary>"#RRGGBB", or "#RRGGBBAA" when the color isn't fully opaque.</summary>
    private static string HexText(ColorBgra c) =>
        $"#{c.R:X2}{c.G:X2}{c.B:X2}{(c.A == 255 ? "" : c.A.ToString("X2"))}";

    private static string RgbText(ColorBgra c) => $"RGB {c.R}, {c.G}, {c.B}";

    private static string ColorDetails(ColorBgra c)
    {
        var hsv = ToAvalonia(c).ToHsv();
        var details = $"Hex {HexText(c)}\n{RgbText(c)}\nHSV {hsv.H:0}°, {hsv.S:P0}, {hsv.V:P0}";
        return c.A == 255 ? details : details + $"\nAlpha {c.A} ({c.A / 255.0:P0})";
    }

    public IReadOnlyList<ColorFormatLine> PrimaryColorFormats => ColorFormats(ToolSettings.PrimaryColor);
    public IReadOnlyList<ColorFormatLine> SecondaryColorFormats => ColorFormats(ToolSettings.SecondaryColor);

    /// <summary>One line per text format; plain text so any other application can paste it.</summary>
    private static ColorFormatLine[] ColorFormats(ColorBgra c)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d % 6 + 6) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        double l = (max + min) / 2;
        double sl = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        double sv = max == 0 ? 0 : d / max;
        var a = (c.A / 255.0).ToString("0.###", inv);
        bool opaque = c.A == 255;
        string hex = $"{c.R:X2}{c.G:X2}{c.B:X2}";
        return
        [
            new("Hex", HexText(c)),
            new("Hex (no #)", hex + (opaque ? "" : c.A.ToString("X2"))),
            new("0x", $"0x{(opaque ? "" : c.A.ToString("X2"))}{hex}"),
            new("CSS rgb", opaque ? $"rgb({c.R}, {c.G}, {c.B})" : $"rgba({c.R}, {c.G}, {c.B}, {a})"),
            new("CSS hsl", opaque
                ? FormattableString.Invariant($"hsl({h:0}, {sl * 100:0}%, {l * 100:0}%)")
                : FormattableString.Invariant($"hsla({h:0}, {sl * 100:0}%, {l * 100:0}%, {a})")),
            new("RGB", $"{c.R}, {c.G}, {c.B}"),
            new("HSV", FormattableString.Invariant($"{h:0}, {sv * 100:0}%, {max * 100:0}%")),
            new("RGB 0–1", FormattableString.Invariant($"{r:0.###}, {g:0.###}, {b:0.###}")),
        ];
    }

    [RelayCommand]
    private async Task CopyColorText(string? text)
    {
        if (!string.IsNullOrEmpty(text) && Clipboard is not null)
            await Clipboard.SetTextAsync(text);
    }

    [RelayCommand]
    private async Task PickPrimaryColor()
    {
        if (Dialogs is not null && await Dialogs.PickColorAsync("Primary Color", PrimaryColor) is { } c)
        {
            PrimaryColor = c;
            ApplyPaletteColor(stroke: true);
        }
    }

    [RelayCommand]
    private async Task PickSecondaryColor()
    {
        if (Dialogs is not null && await Dialogs.PickColorAsync("Secondary Color", SecondaryColor) is { } c)
        {
            SecondaryColor = c;
            ApplyPaletteColor(stroke: false);
        }
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

    /// <summary>Incremented when the zoom changes: the canvas is resized and redrawn from the pixels it already has.</summary>
    [ObservableProperty]
    public partial int ViewVersion { get; set; }

    [ObservableProperty]
    public partial string CursorPositionText { get; set; } = "";

    /// <summary>Size of the visible canvas area, set by the view; used by Best Fit.</summary>
    public Avalonia.Size ViewportSize { get; set; }

    public bool HasDocument => ActiveDocument is not null;

    /// <summary>The active tab when it holds a raster image; null for other kinds (raster-only commands then do nothing).</summary>
    private DocumentViewModel? ActiveImageTab => ActiveDocument is { IsImage: true } tab ? tab : null;

    /// <summary>True when the active tab is a raster image: the CanExecute of every pixel-only command.</summary>
    public bool HasImage => ActiveImageTab is not null;

    private DocumentViewModel? ActiveSvgTab => ActiveDocument is { IsSvg: true } tab ? tab : null;

    /// <summary>True when the active tab is an SVG drawing: the CanExecute of the vector-only commands.</summary>
    public bool HasSvg => ActiveSvgTab is not null;

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
        IDocument doc = options.Svg is { } svg
            ? _workspace.NewSvgDocument(svg.Width, svg.Height, svg.Unit)
            : _workspace.NewDocument(options.Size, options.Background);
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

    /// <summary>File › Open as Image: an SVG file is rasterized into a raster image (to paint on); other files open as usual.</summary>
    [RelayCommand]
    private async Task OpenAsImage()
    {
        if (Dialogs is null)
            return;
        foreach (var path in await Dialogs.PickFilesToOpenAsync(_formats.Formats))
            await OpenFileAsImageAsync(path);
    }

    public async Task<bool> OpenFileAsImageAsync(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".svgz", StringComparison.OrdinalIgnoreCase))
            return await OpenFileAsync(path);
        var file = new FileInfo(path);
        try
        {
            // Created where the events are raised (the UI thread); rasterizing an SVG is quick.
            var image = await RunBusyAsync($"Opening {file.Name}", _ => Task.FromResult(_formats.OpenAsImage(file)));
            FitIfLargerThanViewport(image);
            return true;
        }
        catch (Exception e) when (e is NotSupportedException or MagickException or IOException or UnauthorizedAccessException)
        {
            await (Dialogs?.ShowErrorAsync($"Could not open \"{file.Name}\"", Describe(e)) ?? Task.CompletedTask);
            return false;
        }
    }

    [RelayCommand]
    private Task OpenRecent(string path) => OpenFileAsync(path);

    [RelayCommand]
    private void ClearRecent() => RecentFiles.Clear();

    public async Task<bool> OpenFileAsync(string path)
    {
        var file = new FileInfo(path);
        var alreadyOpen = Documents.Any(d => d.Document.File?.FullName == Path.GetFullPath(path));
        // A cheap header read (performance-tasks.md P5): warn before decoding something this big. No "open
        // downscaled" option yet, just proceed-or-cancel.
        if (!alreadyOpen && Dialogs is not null && _formats.PeekSize(file) is { } size && size.IsRiskyToOpen())
        {
            var megapixels = size.Width * (long)size.Height / 1_000_000.0;
            if (!await Dialogs.ConfirmAsync("Large Image",
                    $"This image is {size.Width} × {size.Height} (~{megapixels:0.#} MP) and may need more memory " +
                    "than is comfortably available. Opening it could be slow.",
                    "Open Anyway"))
                return false;
        }
        try
        {
            // Decoded on a background thread (performance-tasks.md P5): the window stays alive, with a busy status.
            var doc = alreadyOpen
                ? _formats.Open(file)
                : await RunBusyAsync($"Opening {file.Name}", _ => _formats.OpenAsync(file));
            RecentFiles.Add(path);
            if (!alreadyOpen)
            {
                TrackUsage(TelemetryClient.Name("open", doc.FileType ?? "unknown"));
                FitIfLargerThanViewport(doc);
            }
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

    /// <summary>The format last picked in Export As…, suggested next time.</summary>
    private ImageFormat? _lastExportFormat;

    /// <summary>
    /// File › Export As…: writes a picture of the image (flattened) or of the drawing (rendered) to a file of a flat format,
    /// without changing the document's file, name or unsaved-changes state.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasContent))]
    private async Task ExportAs()
    {
        if (Dialogs is null || ActiveDocument is not { } d)
            return;
        var formats = _formats.SaveFormats.Where(f => !f.SupportsLayers).ToList();
        var suggested = _lastExportFormat ?? _formats.GetFormatByExtension("png")!;
        var path = await Dialogs.PickFileToSaveAsync(d.Document.DisplayName, suggested, formats, "Export As");
        if (path is null)
            return;
        if (_formats.GetFormatByExtension(Path.GetExtension(path)) is not { SupportsSaving: true, SupportsLayers: false })
            path += "." + suggested.SupportedExtensions[0];
        var file = new FileInfo(path);
        var format = _formats.GetFormatByExtension(file.Extension)!;
        try
        {
            if (d.Document is SvgDocument drawing)
            {
                if (!await ExportDrawingAsync(drawing, file, format) && !drawing.IsDirty)
                    return;
            }
            else if (d.Document is ImageDocument doc)
            {
                if (format is JpegFormat jpeg)
                {
                    if (await Dialogs.AskJpegQualityAsync(JpegQuality) is not { } quality)
                        return;
                    JpegQuality = quality;
                    jpeg.Quality = quality;
                }
                await RunBusyAsync($"Exporting {file.Name}", async _ =>
                {
                    await Task.Run(() => format.Export(doc, file));
                    return true;
                });
            }
            _lastExportFormat = format;
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await Dialogs.ShowErrorAsync($"Could not export \"{file.Name}\"", Describe(e));
        }
    }

    /// <summary>Returns false if the user cancelled or the save failed.</summary>
    public async Task<bool> SaveDocumentAsync(DocumentViewModel d, bool saveAs)
    {
        if (d.Document is SvgDocument drawing)
            return await SaveDrawingAsync(drawing, saveAs);
        if (d.Document is not ImageDocument doc)
            return false;
        var layered = doc.Layers.Count() > 1;
        var file = saveAs ? null : doc.File;
        var format = file is null ? null : _formats.GetFormatByExtension(file.Extension);
        if (format is { SupportsSaving: false })
            format = null;
        // Save never flattens by itself: an image with layers whose file is a flat format asks where to put a layered
        // copy (an .ora). File › Export As… writes a flat picture and leaves the image and its file alone.
        if (layered && format is { SupportsLayers: false })
            (file, format) = (null, null);

        if (file is null || format is null)
        {
            if (Dialogs is null)
                return false;
            var current = (doc.FileType is { } t ? _formats.GetFormatByExtension(t) : null) is { SupportsSaving: true } known ? known : null;
            var suggestedFormat = layered && current is not { SupportsLayers: true }
                ? _formats.GetFormatByExtension("ora")!
                : current ?? _formats.GetFormatByExtension("png")!;
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
                $"{format.DisplayName} files can't store layers, so the saved file will contain the visible layers merged into one. Your layers are kept in CinnabarSharp. To keep them in a file, save as OpenRaster (.ora); File › Export As… writes a flat copy without changing this image's file.",
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
            // Encodes on a background thread (performance-tasks.md P5); RunBusyAsync disables editing for the
            // duration, so nothing mutates the document while it's being read on that other thread.
            await RunBusyAsync($"Saving {file.Name}", async _ =>
            {
                await _formats.SaveAsync(doc, file, format);
                return true;
            });
            RecentFiles.Add(file.FullName);
            TrackUsage(TelemetryClient.Name("save", format.SupportedExtensions[0]));
            PushBitmapToDrawing(doc);
            return true;
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
        {
            await (Dialogs?.ShowErrorAsync($"Could not save \"{file.Name}\"", Describe(e)) ?? Task.CompletedTask);
            return false;
        }
    }

    /// <summary>
    /// Saves an SVG drawing as SVG, or, when the user picks a raster format in Save As, exports it as a picture: the drawing
    /// then stays an SVG document with its own file and unsaved state, so this returns false (not saved) while it is dirty.
    /// </summary>
    private async Task<bool> SaveDrawingAsync(SvgDocument drawing, bool saveAs)
    {
        var svgFormat = _formats.GetSaveFormats(DocumentKind.Svg)[0];
        var file = saveAs ? null : drawing.File;
        var format = file is null ? null : _formats.GetFormatByExtension(file.Extension);
        if (format is not { DocumentKind: DocumentKind.Svg })
            (file, format) = (null, null);

        if (file is null)
        {
            if (Dialogs is null)
                return false;
            var path = await Dialogs.PickFileToSaveAsync(drawing.DisplayName, svgFormat, _formats.GetSaveFormats(DocumentKind.Svg));
            if (path is null)
                return false;
            format = _formats.GetFormatByExtension(Path.GetExtension(path));
            if (format is not { SupportsSaving: true })
            {
                path += "." + svgFormat.SupportedExtensions[0];
                format = svgFormat;
            }
            file = new FileInfo(path);
        }

        try
        {
            if (format!.DocumentKind != DocumentKind.Svg)
                return await ExportDrawingAsync(drawing, file, format);
            await RunBusyAsync($"Saving {file.Name}", async _ =>
            {
                await _formats.SaveAsync(drawing, file, format);
                return true;
            });
            RecentFiles.Add(file.FullName);
            TrackUsage(TelemetryClient.Name("save", format.SupportedExtensions[0]));
            return true;
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await (Dialogs?.ShowErrorAsync($"Could not save \"{file.Name}\"", Describe(e)) ?? Task.CompletedTask);
            return false;
        }
    }

    private async Task<bool> ExportDrawingAsync(SvgDocument drawing, FileInfo file, ImageFormat format)
    {
        if (Dialogs is null)
            return false;
        var options = await Dialogs.ShowSvgExportAsync(new SvgExportViewModel(drawing.ImageSize, $"Export as {format.DisplayName}", "Export"));
        if (options is null)
            return false;
        if (format is JpegFormat jpeg)
        {
            if (await Dialogs.AskJpegQualityAsync(JpegQuality) is not { } quality)
                return false;
            JpegQuality = quality;
            jpeg.Quality = quality;
        }
        await RunBusyAsync($"Exporting {file.Name}", async _ =>
        {
            await _formats.ExportAsync(drawing, file, options, format);
            return true;
        });
        return !drawing.IsDirty;
    }

    /// <summary>Image › Rasterize: a new raster image from the drawing at the chosen size; the drawing stays open.</summary>
    [RelayCommand(CanExecute = nameof(HasSvg))]
    private async Task Rasterize()
    {
        if (ActiveSvgTab is not { } tab || Dialogs is null || IsBusy)
            return;
        var drawing = tab.Svg;
        var options = await Dialogs.ShowSvgExportAsync(new SvgExportViewModel(drawing.ImageSize, "Rasterize", "Rasterize"));
        if (options is null)
            return;
        var (pixels, width, height) = await RunBusyAsync("Rasterizing", _ => Task.Run(() => options.Render(drawing)));
        var image = _workspace.NewDocumentFromImage(new ClipboardImage(pixels, width, height));
        image.DisplayName = Path.GetFileNameWithoutExtension(drawing.DisplayName) + " (raster)";
        FitIfLargerThanViewport(image);
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

    /// <summary>Help › Open Log Folder: the folder with log.txt, to send after a problem.</summary>
    [RelayCommand]
    private Task OpenLogFolder() => Dialogs?.OpenFolderAsync(AppLog.Folder ?? AppLog.DefaultFolder) ?? Task.CompletedTask;

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

    private ImageDocumentLayers? CurrentLayers => ActiveImageTab?.Image.Layers;

    public bool CanDeleteLayer => CurrentLayers?.Count() > 1;
    public bool CanMergeLayerDown => CurrentLayers?.CurrentUserLayerIndex > 0;
    public bool CanMoveLayerUp => CurrentLayers is { } l && l.CurrentUserLayerIndex < l.Count() - 1;
    public bool CanMoveLayerDown => CurrentLayers?.CurrentUserLayerIndex > 0;
    public bool CanFlatten => CurrentLayers?.Count() > 1;

    private void EditLayers(Action<DocumentActions> edit)
    {
        if (ActiveImageTab is not { } d)
            return;
        edit(d.Image.Actions);
        RefreshLayers();
        RefreshThumbnails();
    }

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void AddNewLayer() => EditLayers(a => a.AddNewLayer());

    [RelayCommand(CanExecute = nameof(CanDeleteLayer))]
    private void DeleteLayer() => EditLayers(a => a.DeleteCurrentLayer());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void DuplicateLayer() => EditLayers(a => a.DuplicateCurrentLayer());

    [RelayCommand(CanExecute = nameof(CanMergeLayerDown))]
    private void MergeLayerDown() => EditLayers(a => a.MergeCurrentLayerDown());

    [RelayCommand(CanExecute = nameof(CanMoveLayerUp))]
    private void MoveLayerUp() => EditLayers(a => a.MoveCurrentLayerUp());

    [RelayCommand(CanExecute = nameof(CanMoveLayerDown))]
    private void MoveLayerDown() => EditLayers(a => a.MoveCurrentLayerDown());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void FlipLayerHorizontal() => EditLayers(a => a.FlipCurrentLayerHorizontal());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void FlipLayerVertical() => EditLayers(a => a.FlipCurrentLayerVertical());

    [RelayCommand(CanExecute = nameof(CanFlatten))]
    private void Flatten() => EditLayers(a => a.Flatten());

    [RelayCommand(CanExecute = nameof(HasImage))]
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

    [RelayCommand(CanExecute = nameof(HasImage))]
    private async Task LayerProperties()
    {
        if (Dialogs is null || ActiveImageTab is not { } d)
            return;
        var layer = d.Image.Layers.CurrentUserLayer;
        var before = Core.Models.LayerProperties.From(layer);
        var properties = new LayerPropertiesViewModel(layer);
        if (await Dialogs.ShowLayerPropertiesAsync(properties))
            d.Image.Actions.CommitLayerProperties(layer, before);
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
        set { ToolSettings.CornerRadius = Math.Clamp(value, 0, 1000); OnPropertyChanged(); RefreshEditingTool(); }
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

    public static IReadOnlyList<BubbleStyle> BubbleStyles { get; } = Enum.GetValues<BubbleStyle>();

    public BubbleStyle BubbleStyle
    {
        get => ToolSettings.BubbleStyle;
        set
        {
            ToolSettings.BubbleStyle = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowBubbleRadius));
            RefreshEditingTool();
        }
    }

    public bool BubbleNumbered
    {
        get => ToolSettings.BubbleNumbered;
        set { ToolSettings.BubbleNumbered = value; OnPropertyChanged(); RefreshEditingTool(); }
    }

    /// <summary>Number the next numbered bubble gets; set it back to 1 to start a new series.</summary>
    public int BubbleNextNumber
    {
        get => ToolSettings.BubbleNextNumber;
        set => ToolSettings.BubbleNextNumber = value;
    }

    public bool BubbleOwnLayer
    {
        get => ToolSettings.BubbleOwnLayer;
        set { ToolSettings.BubbleOwnLayer = value; OnPropertyChanged(); }
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
        ComicDefaults = settings.ComicPage ?? new();
        Tolerance = settings.Tolerance;
        GlobalFill = settings.GlobalFill;
        SampleImage = settings.SampleImage;
        SelectionMode = settings.SelectionMode;
        ShapeKind = settings.ShapeKind;
        ShapeStyle = settings.ShapeStyle;
        GradientKind = settings.GradientKind;
        BubbleStyle = settings.BubbleStyle;
        BubbleNumbered = settings.BubbleNumbered;
        BubbleOwnLayer = settings.BubbleOwnLayer;
        ShowWelcomeScreen = settings.ShowWelcome;
        GridSize = settings.GridSize;
        SnapToGuides = settings.SnapToGuides;
        ShowRulers = settings.ShowRulers;
        if (CinnabarSharp.Core.Vector.ShapeLibrary.Find(settings.LibraryShape) is { } libraryShape)
            ToolSettings.LibraryShape = libraryShape.Id;
        SnapToGrid = settings.SnapToGrid;
        ShowGrid = settings.ShowGrid;
        _checkForUpdates = settings.CheckForUpdates;
        _skippedUpdate = settings.SkippedUpdate;
        _lastUpdateCheck = settings.LastUpdateCheckUtc;
        _sendUsageStatistics = settings.SendUsageStatistics;
        _telemetryInstallId = settings.SendUsageStatistics == true ? settings.TelemetryInstallId : null;
        _restoringSettings = true;
        try
        {
            AllowAgents = settings.AllowAgents;
        }
        finally
        {
            _restoringSettings = false;
        }
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
        BubbleStyle = BubbleStyle,
        BubbleNumbered = BubbleNumbered,
        BubbleOwnLayer = BubbleOwnLayer,
        AllowAgents = AllowAgents,
        ShowWelcome = ShowWelcomeScreen,
        ShowGrid = ShowGrid,
        ShowRulers = ShowRulers,
        SnapToGuides = SnapToGuides,
        LibraryShape = ToolSettings.LibraryShape,
        SnapToGrid = SnapToGrid,
        GridSize = GridSize,
        CheckForUpdates = _checkForUpdates,
        SkippedUpdate = _skippedUpdate,
        LastUpdateCheckUtc = _lastUpdateCheck,
        SendUsageStatistics = _sendUsageStatistics,
        TelemetryInstallId = _telemetryInstallId,
        ComicPage = ComicDefaults,
    };

    /// <summary>Selects the next tool with this Paint.NET shortcut letter (pressing S again cycles the select tools).</summary>
    public void SelectToolByShortcut(string letter)
    {
        var matches = ToolboxTools.Where(t => t.Shortcut.Equals(letter, StringComparison.OrdinalIgnoreCase) && (t.Tool is not null || t.VectorTool is not null || t.Name is "Pan" or "Zoom")).ToList();
        if (matches.Count == 0)
            return;
        var index = matches.IndexOf(SelectedTool);
        SelectedTool = matches[(index + 1) % matches.Count];
    }

    public void ToolPointerDown(ToolPointer pointer)
    {
        // Prepare for TV without Crop to fill has no frame to move.
        if (ActiveSvg is { } drawing)
        {
            // A double click on a text with the Select tool edits it again: the Text tool takes over at that click.
            if (!IsBusy && pointer.ClickCount >= 2 && ActiveVectorTool is VectorSelectTool && TextAt(drawing, pointer) is not null
                && VectorTools.FirstOrDefault(t => t.VectorTool is VectorTextTool) is { } textTool)
                SelectedTool = textTool;
            if (!IsBusy)
                WithVectorTool(drawing, pointer, (t, d, p) => t.OnPointerDown(d, p));
            UpdateOverlay();
            return;
        }
        if (!IsBusy && Tv is not { ShowsFrame: false })
            WithTool(t => t.OnPointerDown, pointer);
    }

    public void ToolPointerMove(ToolPointer pointer)
    {
        if (ActiveSvg is { } drawing)
        {
            if (!IsBusy)
                WithVectorTool(drawing, pointer, (t, d, p) => t.OnPointerMove(d, p));
            UpdateOverlay();
            return;
        }
        WithTool(t => t.OnPointerMove, pointer);
    }

    public void ToolPointerUp(ToolPointer pointer)
    {
        if (ActiveSvg is { } drawing)
        {
            if (!IsBusy)
                WithVectorTool(drawing, pointer, (t, d, p) => t.OnPointerUp(d, p));
            UpdateOverlay();
            RefreshThumbnails();
            return;
        }
        WithTool(t => t.OnPointerUp, pointer);
        if (SelectedTool.Tool is IEditingTool)
            RefreshThumbnails();
    }

    private void WithTool(Func<ITool, Action<ImageDocument, ToolPointer>> handler, ToolPointer pointer)
    {
        if (ActiveImageTab is { } d && ActiveTool is { } tool)
        {
            if (tool is IGridSnappingTool)
            {
                var guided = SnapToGuidesImage(pointer.Position);
                var position = ToolSettings.SnapToGrid ? GridSnapping.Snap(guided.Point, ToolSettings.GridSize, default) : guided.Point;
                pointer = pointer with { Position = new Core.Models.PointD(guided.X ? guided.Point.X : position.X, guided.Y ? guided.Point.Y : position.Y) };
            }
            handler(tool)(d.Image, pointer);
        }
        UpdateOverlay();
    }

    /// <summary>The tool that gets the mouse: the page's while a comic page is edited, else the selected tool.</summary>
    private ITool? ActiveTool => Comic?.Tool ?? SelectedTool?.Tool;

    /// <summary>True while keys typed belong to the selected tool (the Text tool is editing).</summary>
    public bool IsTyping => IsVectorTyping || ActiveImageTab is { } d && SelectedTool.Tool is IKeyboardTool k && k.IsTyping(d.Image);

    /// <summary>
    /// Sends a key to the selected tool; returns true if it was used. Escape that no tool uses (to cancel a crop
    /// frame, finish a text...) deselects.
    /// </summary>
    public bool ToolKeyDown(ToolKey key, ToolModifiers modifiers)
    {
        if (ActiveSvg is { } drawing && !IsBusy)
        {
            // Escape deselects objects (a vector tool that uses Escape handles it first).
            return VectorToolKeyDown(drawing, key, modifiers);
        }
        if (ActiveImageTab is not { } d || IsBusy)
            return false;
        if (IsComicMode && modifiers == ToolModifiers.None && key is ToolKey.Enter or ToolKey.Escape)
        {
            if (key == ToolKey.Enter)
                ApplyComicCommand.Execute(null);
            else
                ExitComic(closeDocument: true);
            return true;
        }
        if (IsTvMode && modifiers == ToolModifiers.None && key is ToolKey.Enter or ToolKey.Escape)
        {
            if (key == ToolKey.Enter)
                ApplyTvCommand.Execute(null);
            else
                ExitTv();
            return true;
        }
        var handled = SelectedTool.Tool is IKeyboardTool tool && tool.OnKeyDown(d.Image, key, modifiers);
        if (!handled && key == ToolKey.Escape && modifiers == ToolModifiers.None && d.Image.HasSelection)
        {
            d.Image.Actions.DeselectAll();
            handled = true;
        }
        UpdateOverlay();
        return handled;
    }

    public void ToolTextInput(string text)
    {
        if (ActiveSvg is { } drawing)
        {
            if (ActiveVectorTool is IVectorKeyboardTool vectorTool)
            {
                vectorTool.OnTextInput(drawing, text);
                UpdateOverlay();
            }
            return;
        }
        if (ActiveImageTab is not { } d || SelectedTool.Tool is not IKeyboardTool tool)
            return;
        tool.OnTextInput(d.Image, text);
        UpdateOverlay();
    }

    private (ITextEditingTool Tool, ImageDocument Document)? EditingText =>
        ActiveImageTab is { } d && SelectedTool.Tool is ITextEditingTool t && t.IsEditing(d.Image) ? (t, d.Image) : null;

    /// <summary>What the selected tool draws over the canvas (curve handles, text caret...).</summary>
    [ObservableProperty]
    public partial ToolOverlay? Overlay { get; set; }

    private void UpdateOverlay()
    {
        OnPropertyChanged(nameof(StatusToolName));
        UpdateGrid();
        RefreshNodeCommands();
        if (ActiveSvg is { } drawing)
        {
            Overlay = VectorToolOverlay(drawing);
            return;
        }
        Overlay = ActiveImageTab is not { } d ? null
            : Comic?.Tool is { } comic ? comic.GetOverlay(d.Image)
            : Tv is { ShowsFrame: false } tvOptions ? TvOverlay(tvOptions, d.Image)
            : SelectedTool?.Tool is IOverlayTool tool ? tool.GetOverlay(d.Image)
            : null;
        if (Tv is { } tv && ActiveImageTab is { } doc)
            tv.Crop = CropTool?.Frame(doc.Image);
        ApplyCropCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Redraws the curve or text being edited after a color or option change.</summary>
    private void RefreshEditingTool()
    {
        RefreshEditingVectorTool();
        if (ActiveImageTab is not { } d || SelectedTool?.Tool is not IEditingTool tool || !tool.IsEditing(d.Image))
            return;
        tool.Refresh(d.Image);
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
    partial void OnSelectedToolChanged(ToolViewModel oldValue, ToolViewModel newValue)
    {
        // Picking another tool leaves Prepare for TV, keeping the tool picked.
        if (IsTvMode && newValue?.Tool is not Core.Tools.CropTool)
        {
            _toolBeforeTv = null;
            ExitTv();
        }
        FinishEditing(oldValue?.Tool, ActiveDocument?.ImageOrNull);
        FinishVectorEditing(oldValue?.VectorTool, ActiveDocument?.SvgOrNull);
        if (newValue is not null && oldValue is not null)
            TrackUsage(TelemetryClient.Name(newValue.VectorTool is not null ? "vector_tool" : "tool", newValue.Name));
    }

    partial void OnActiveDocumentChanging(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        // Another image (e.g. opened from the menu) ends editing the page, which stays as it is.
        if (IsComicMode && newValue?.Document != (IDocument?)_comicDocument)
            ExitComic(closeDocument: false);
        ExitTv();
        FinishEditing(SelectedTool?.Tool, oldValue?.ImageOrNull);
        FinishVectorEditing(SelectedTool?.VectorTool, oldValue?.SvgOrNull);
    }

    partial void OnSelectedToolChanged(ToolViewModel value)
    {
        foreach (var name in new[]
                 {
                     nameof(ShowSelectionOptions), nameof(ShowToleranceOptions), nameof(ShowBrushOptions),
                     nameof(ShowShapeOptions), nameof(ShowGradientOptions), nameof(ShowColorPickerOptions),
                     nameof(ShowHardnessOptions), nameof(ShowCornerRadiusOptions), nameof(ShowTextOptions),
                     nameof(ShowCropOptions), nameof(ShowBubbleOptions),
                     nameof(BrushOutlineSize), nameof(ToolboxSelection),
                 })
            OnPropertyChanged(name);
        RaiseVectorOptionFlags();
        UpdateOverlay();
    }

    public bool ShowSelectionOptions => SelectedTool.IsSelectionTool;
    public bool ShowToleranceOptions => SelectedTool.HasTolerance;
    public bool ShowBrushOptions => SelectedTool.HasBrushWidth || SelectedTool.HasVectorStroke;
    public bool ShowHardnessOptions => SelectedTool.IsBrush;
    public bool ShowShapeOptions => SelectedTool.IsShapes;
    public bool ShowCornerRadiusOptions => SelectedTool.IsShapes && ShapeKind == ShapeKind.RoundedRectangle;
    public bool ShowBubbleRadius => BubbleStyle == BubbleStyle.Rounded;
    public bool ShowGradientOptions => SelectedTool.IsGradient;
    public bool ShowColorPickerOptions => SelectedTool.IsColorPicker;
    public bool ShowTextOptions => SelectedTool.IsText || SelectedTool.IsBubble;
    public bool ShowBubbleOptions => SelectedTool.IsBubble;
    public bool ShowCropOptions => SelectedTool.Tool is CropTool && !IsTvMode;

    // ---- Selection and clipboard ----

    /// <summary>Incremented when the selection changes, to redraw the marching ants.</summary>
    [ObservableProperty]
    public partial int SelectionVersion { get; set; }

    public bool HasSelection => ActiveImageTab?.Image.HasSelection == true;

    public string SelectionSizeText => ActiveImageTab?.Image.Selection is { } s
        ? $"Selection {s.Bounds.Width} × {s.Bounds.Height}"
        : "";

    [RelayCommand(CanExecute = nameof(HasContent))]
    private void SelectAll()
    {
        if (EditingVectorText is { } vectorText && ActiveSvg is { } svgText)
        {
            vectorText.SelectAll(svgText);
            UpdateOverlay();
            return;
        }
        if (ActiveSvg is { } drawing)
        {
            drawing.Selection.Set(SvgDocumentFactory.DefaultParent(drawing.Root).Elements.Where(IsObject));
            return;
        }
        if (EditingText is { } text)
        {
            text.Tool.SelectAll(text.Document);
            UpdateOverlay();
            return;
        }
        ActiveImageTab?.Image.Actions.SelectAll();
    }

    /// <summary>Something is selected: pixels of an image, or objects of a drawing.</summary>
    public bool HasAnySelection => HasSelection || HasObjectSelection;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeselectAll() => ActiveImageTab?.Image.Actions.DeselectAll();

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void InvertSelection() => ActiveImageTab?.Image.Actions.InvertSelection();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CropToSelection() => ActiveImageTab?.Image.Actions.CropToSelection();

    // Delete and Backspace are also these menu items' shortcuts: while typing text they edit the text instead.
    [RelayCommand(CanExecute = nameof(HasContent))]
    private void EraseSelection()
    {
        if (ActiveSvg is { } drawing)
        {
            if (!IsTyping || !ToolKeyDown(ToolKey.Delete, ToolModifiers.None))
                drawing.Actions.DeleteSelection();
            return;
        }
        if (!IsTyping || !ToolKeyDown(ToolKey.Delete, ToolModifiers.None))
            EditLayers(a => a.EraseSelection());
    }

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void FillSelection()
    {
        if (!IsTyping || !ToolKeyDown(ToolKey.Backspace, ToolModifiers.None))
            EditLayers(a => a.FillSelection(ColorBgra.FromBgra(PrimaryColor.B, PrimaryColor.G, PrimaryColor.R, PrimaryColor.A)));
    }

    [RelayCommand(CanExecute = nameof(HasContent))]
    private Task Copy() => CopyAsync(merged: false);

    [RelayCommand(CanExecute = nameof(HasContent))]
    private Task CopyMerged() => CopyAsync(merged: true);

    private async Task CopyAsync(bool merged)
    {
        if (EditingVectorText is { } vectorText && Clipboard is not null)
        {
            await vectorText.Copy(Clipboard);
            return;
        }
        if (ActiveSvg is { } drawing)
        {
            await CopyObjectsAsync(drawing, cut: false);
            return;
        }
        if (EditingText is { } text && Clipboard is not null)
        {
            await text.Tool.Copy(Clipboard);
            return;
        }
        if (ActiveImageTab is { } d && Clipboard is not null)
            await Clipboard.SetImageAsync(d.Image.Actions.Copy(merged));
    }

    [RelayCommand(CanExecute = nameof(HasContent))]
    private async Task Cut()
    {
        if (EditingVectorText is { } vectorText && ActiveSvg is { } svgText && Clipboard is not null)
        {
            await vectorText.Cut(svgText, Clipboard);
            UpdateOverlay();
            return;
        }
        if (ActiveSvg is { } drawing)
        {
            await CopyObjectsAsync(drawing, cut: true);
            return;
        }
        if (ActiveImageTab is not { } d || Clipboard is null)
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
        if (EditingVectorText is { } vectorText && ActiveSvg is { } svgText && Clipboard is not null)
        {
            await vectorText.Paste(svgText, Clipboard);
            UpdateOverlay();
            return;
        }
        if (ActiveSvg is { } drawing)
        {
            await PasteIntoDrawingAsync(drawing);
            return;
        }
        if (EditingText is { } text && Clipboard is not null)
        {
            await text.Tool.Paste(text.Document, Clipboard);
            UpdateOverlay();
            return;
        }
        if (await ClipboardImageAsync() is not { } image)
            return;
        if (ActiveImageTab is not { } d)
        {
            PasteImage(image);
            return;
        }
        d.Image.Actions.Paste(image, PasteLocation());
        SelectedTool = Tools.First(t => t.Tool is MoveSelectedPixelsTool);
        RefreshThumbnails();
    }

    [RelayCommand(CanExecute = nameof(HasImage))]
    private async Task PasteIntoNewLayer()
    {
        if (ActiveImageTab is not { } d || await ClipboardImageAsync() is not { } image)
            return;
        EditLayers(a => a.PasteIntoNewLayer(image, PasteLocation()));
        SelectedTool = Tools.First(t => t.Tool is MoveSelectedPixelsTool);
    }

    /// <summary>Puts the clipboard image next to the image (side and alignment from a dialog), growing the canvas.</summary>
    [RelayCommand(CanExecute = nameof(HasImage))]
    private async Task PasteBeside()
    {
        if (ActiveImageTab is not { } d || Dialogs is null || await ClipboardImageAsync() is not { } image)
            return;
        if (await Dialogs.ShowPasteBesideAsync(d.Image.ImageSize, new ImageSize(image.Width, image.Height)) is not { } options)
            return;
        EditLayers(a => a.PasteBeside(image, options.Side, options.Alignment, ToolSettings.SecondaryColor));
        SelectedTool = Tools.First(t => t.Tool is MoveSelectedPixelsTool);
        FitIfLargerThanViewport(d.Image);
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

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task AutoLevel() => RunAdjustment(new AutoLevel());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task BlackAndWhite() => RunAdjustment(new BlackAndWhite());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task BrightnessContrast() => RunAdjustment(new BrightnessContrast());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task HueSaturation() => RunAdjustment(new HueSaturation());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task InvertColors() => RunAdjustment(new InvertColors());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task Levels() => RunAdjustment(new Levels());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task Curves() => RunAdjustment(new Core.Adjustments.Curves());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task Posterize() => RunAdjustment(new Posterize());

    [RelayCommand(CanExecute = nameof(HasImage))]
    private Task Sepia() => RunAdjustment(new Sepia());

    private Task RunAdjustment(ColorAdjustment adjustment) => RunEffect(adjustment);

    // ---- Effects ----

    // ---- Long operations: feedback in the status bar ----

    /// <summary>Name of the long operation in progress ("Auto-Enhance…"), shown in the status bar; null when idle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle))]
    public partial string? BusyText { get; set; }

    /// <summary>Progress in percent, or null when the operation can't tell (the bar then just animates).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BusyIndeterminate))]
    public partial double? BusyProgress { get; set; }

    public bool IsBusy => BusyText is not null;
    public bool IsIdle => !IsBusy;
    public bool BusyIndeterminate => BusyProgress is null;

    /// <summary>
    /// Runs a long operation while the status bar shows <paramref name="text"/> and a progress bar, and the window's
    /// editing areas are disabled so nothing else changes the image meanwhile.
    /// </summary>
    public async Task<T> RunBusyAsync<T>(string text, Func<IProgress<double>, Task<T>> work)
    {
        BusyText = text + "…";
        BusyProgress = null;
        try
        {
            return await work(new Progress<double>(p => BusyProgress = p));
        }
        finally
        {
            BusyText = null;
            BusyProgress = null;
        }
    }

    private (Effect Effect, IReadOnlyList<double> Values)? _lastEffect;

    public string RepeatEffectText => _lastEffect is { } last ? $"Repeat {last.Effect.Name}" : "Repeat Last Effect";

    /// <summary>
    /// Operations without parameters apply at once; others open a live-preview dialog. The final result is
    /// computed in the background, then applied as one history step.
    /// </summary>
    public async Task RunEffect(Effect effect)
    {
        if (ActiveImageTab is not { } d || IsBusy)
            return;
        var session = new EffectSession(d.Image, effect, ToolSettings.PrimaryColor, ToolSettings.SecondaryColor);
        if (effect.Parameters.Count == 0 && !effect.HasCustomDialog)
        {
            TrackUsage(TelemetryClient.Name("effect", effect.Name));
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
            await RunBusyAsync(effect.Name, async _ =>
            {
                await dialog.CommitAsync();
                return true;
            });
        else
            dialog.Cancel();
        if (dialog.Committed)
            TrackUsage(TelemetryClient.Name("effect", effect.Name));
        if (dialog.Committed && effect is not ColorAdjustment)
            RememberEffect(effect, dialog.Values);
    }

    [RelayCommand(CanExecute = nameof(CanRepeatEffect))]
    private async Task RepeatEffect()
    {
        if (ActiveImageTab is not { } d || _lastEffect is not { } last || IsBusy)
            return;
        await ApplyAsync(new EffectSession(d.Image, last.Effect, ToolSettings.PrimaryColor, ToolSettings.SecondaryColor), last.Values);
    }

    public bool CanRepeatEffect => HasImage && _lastEffect is not null;

    private void RememberEffect(Effect effect, IReadOnlyList<double> values)
    {
        _lastEffect = (effect, values);
        OnPropertyChanged(nameof(RepeatEffectText));
        RepeatEffectCommand.NotifyCanExecuteChanged();
    }

    private async Task ApplyAsync(EffectSession session, IReadOnlyList<double> values)
    {
        var history = ActiveImageTab!.Image.Workspace.History;
        var (pointer, count) = (history.Pointer, history.Items.Count);
        var pixels = await RunBusyAsync(session.Effect.Name, _ => Task.Run(() => session.Compute(values)));
        // The macOS menu stays usable: if something changed the image meanwhile (e.g. Undo), drop the result.
        if (history.Pointer != pointer || history.Items.Count != count)
            return;
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

    public bool HasCropFrame => ActiveImageTab is { } d && CropTool?.IsEditing(d.Image) == true;

    [RelayCommand(CanExecute = nameof(HasCropFrame))]
    private void ApplyCrop()
    {
        if (ActiveImageTab is { } d)
            CropTool?.Apply(d.Image);
        UpdateOverlay();
    }

    // ---- Page de BD: photos assembled like a comic page, framed on the canvas ----

    /// <summary>
    /// Set while a comic page is edited: the canvas shows the page (a preview) on its new document, a click selects a
    /// panel and a drag moves the photo in it; the options bar has the page and panel options. Apply (Enter) writes
    /// the page into the document as one step; Cancel (Escape) closes it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComicMode), nameof(IsNotComicMode), nameof(StatusToolName))]
    [NotifyCanExecuteChangedFor(nameof(ApplyComicCommand), nameof(CancelComicCommand))]
    public partial ComicPageViewModel? Comic { get; set; }

    public bool IsComicMode => Comic is not null;
    public bool IsNotComicMode => !IsComicMode;

    /// <summary>Status bar: the selected tool, or the comic page while it gets the mouse instead.</summary>
    public string StatusToolName => IsComicMode ? "Comic page"
        : ActiveSvg is { } drawing && VectorHint(drawing) is { Length: > 0 } hint ? $"{SelectedTool?.Name} — {hint}"
        : SelectedTool?.Name ?? "";

    private ImageDocument? _comicDocument;

    /// <summary>Format, gutter, border, background and recently used layouts of the last comic page.</summary>
    public ComicSettings ComicDefaults { get; private set; } = new();

    [RelayCommand]
    private async Task ComicPage()
    {
        if (Dialogs is null || IsBusy || IsTvMode || IsComicMode)
            return;
        // Open images are flattened only when a panel needs them (a proxy, a thumbnail, Apply), not all at once here.
        var sources = Documents.Where(d => d.IsImage).Select(d =>
        {
            var document = d.Image;
            var (width, height) = (document.ImageSize.Width, document.ImageSize.Height);
            return new ComicSource(document.DisplayName, width, height,
                () => new BgraImage(document.Layers.GetFlattenedBgra(includeToolLayer: false), width, height));
        });
        var comic = new ComicPageViewModel(sources, ComicDefaults);
        if (!await Dialogs.ShowComicPageAsync(comic))
            return;
        ComicDefaults = comic.ToSettings(rememberLayout: false);

        var options = comic.Options;
        var page = _workspace.NewDocument(options.Page, options.Background);
        page.DisplayName = "Comic page";
        FitIfLargerThanViewport(page);
        var tool = comic.CreateTool();
        tool.Changed += () => _ = RefreshComicPreviewAsync();
        tool.SelectionChanged += UpdateOverlay;
        tool.PanelActivated += panel => _ = ReplacePanelPhotoAsync(comic, tool, panel);
        _comicDocument = page;
        Comic = comic;
        UpdateOverlay();
        await RefreshComicPreviewAsync();
    }

    /// <summary>Double click on a panel: pick another image file and put it in that panel.</summary>
    private async Task ReplacePanelPhotoAsync(ComicPageViewModel comic, ComicPageTool tool, int panel)
    {
        if (Dialogs is null || IsBusy)
            return;
        var files = await Dialogs.PickFilesToOpenAsync(_formats.Formats);
        if (files.Count == 0 || Comic != comic || comic.AddFiles(files.Take(1)) == 0)
            return;
        var added = comic.Sources[^1].Source;
        tool.SetPhoto(panel, added.Proxy ?? added.Placeholder(), added, added.LoadFull);
    }

    [RelayCommand(CanExecute = nameof(IsComicMode))]
    private async Task ApplyComic()
    {
        if (Comic?.Tool is not { } tool || _comicDocument is not { } page)
            return;
        var (layout, options, contents) = (tool.Layout, tool.Options, tool.Contents.ToList());
        // The page is composed from the photos at full resolution, read one panel at a time (the editing used proxies). A
        // file that was moved fails here, and the page stays open to put another photo in that panel.
        BgraImage result;
        try
        {
            result = await RunBusyAsync("Composing the comic page", _ =>
                Task.Run(() => Core.Photo.ComicPage.Compose(layout, options, contents, quality: Core.Photo.ComicQuality.Full)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ImageMagick.MagickException)
        {
            if (Dialogs is not null)
                await Dialogs.ShowErrorAsync("Could not create the comic page", Describe(e));
            return;
        }
        if (Comic?.Tool != tool)
            return;
        ExitComic(closeDocument: false);
        page.Actions.ReplaceLayerPixels("Comic Page", result.Pixels);
    }

    [RelayCommand(CanExecute = nameof(IsComicMode))]
    private void CancelComic() => ExitComic(closeDocument: true);

    private void ExitComic(bool closeDocument)
    {
        if (Comic is null)
            return;
        var page = _comicDocument;
        if (!closeDocument)
            ComicDefaults = Comic.ToSettings(rememberLayout: true);
        Comic = null;
        _comicDocument = null;
        UpdateOverlay();
        if (closeDocument && page is not null && _workspace.OpenDocuments.Contains(page))
            _workspace.CloseDocument(page);
    }

    private bool _comicPreviewRunning;
    private CancellationTokenSource? _comicPreviewCts;
    private bool _comicPreviewDirty;

    /// <summary>
    /// Recomputes the page preview in the background. While one is computing, further changes (a drag) are merged into
    /// one more computation when it ends, so dragging stays responsive.
    /// </summary>
    private async Task RefreshComicPreviewAsync()
    {
        if (_comicPreviewRunning)
        {
            // A newer change makes the preview being computed out of date: stop it and start again.
            _comicPreviewDirty = true;
            _comicPreviewCts?.Cancel();
            return;
        }
        _comicPreviewRunning = true;
        try
        {
            do
            {
                _comicPreviewDirty = false;
                if (Comic?.Tool is not { } tool || _comicDocument is not { } page)
                    return;
                using var cts = _comicPreviewCts = new CancellationTokenSource();
                try
                {
                    var (layout, options) = (tool.Layout, tool.Options);
                    var size = options.Page;
                    // As many pixels as the screen shows (twice for high-DPI displays), never more than the page has.
                    var width = (int)Math.Clamp(size.Width * page.Workspace.Scale * 2, 64, Math.Min(size.Width, 2560));
                    var preview = new ImageSize(width, Math.Max(1, (int)Math.Round((double)width * size.Height / size.Width)));
                    // Panels draw from proxies sized for this preview (a bigger one is made in the background if needed).
                    await ComicProxies.EnsureAsync(tool, (double)width / size.Width, cts.Token);
                    var contents = tool.Contents.ToList();
                    var result = await Task.Run(() => Core.Photo.ComicPage.Compose(layout, options, contents, preview, cts.Token, Core.Photo.ComicQuality.Preview));
                    if (Comic?.Tool != tool)
                        return;
                    tool.Preview = new OverlayPicture(result.Pixels, result.Width, result.Height,
                        new RectangleD(0, 0, size.Width, size.Height));
                    UpdateOverlay();
                }
                catch (OperationCanceledException)
                {
                    // Replaced by a newer change: the loop starts again with it.
                }
            }
            while (_comicPreviewDirty);
        }
        finally
        {
            _comicPreviewRunning = false;
        }
    }

    // ---- Prepare for TV: options in the options bar, a 16:9 frame on the canvas ----

    /// <summary>
    /// Set while Prepare for TV is in progress: the options bar shows these options and, for Crop to fill, the canvas
    /// shows a 16:9 frame (the Crop tool) to move or resize. Apply (Enter) creates the TV image, Cancel (Escape) leaves.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTvMode), nameof(ShowCropOptions))]
    [NotifyCanExecuteChangedFor(nameof(ApplyTvCommand), nameof(CancelTvCommand))]
    public partial PrepareForTvViewModel? Tv { get; set; }

    public bool IsTvMode => Tv is not null;

    private ToolViewModel? _toolBeforeTv;

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void PrepareForTv()
    {
        if (ActiveImageTab is not { } d || IsTvMode || IsComicMode || IsBusy || CropTool is not { } crop)
            return;
        var doc = d.Image;
        // The frame is centered on the crop frame, the selection, or the photo.
        var area = crop.Frame(doc) ?? doc.Selection?.Bounds ?? new RectangleI(0, 0, doc.ImageSize.Width, doc.ImageSize.Height);

        _toolBeforeTv = SelectedTool;
        SelectedTool = Tools.First(t => t.Tool == crop);
        crop.ForcedRatio = 16 / 9.0;
        crop.CanDrawNewFrame = false;

        var tv = new PrepareForTvViewModel(TvOptions, doc.ImageSize, null, Documents.Where(o => o != d && o.IsImage).ToList());
        tv.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PrepareForTvViewModel.Resolution))
                ProposeTvFrame(CropTool?.Frame(doc) ?? area);
            else if (e.PropertyName is nameof(PrepareForTvViewModel.Fit) or nameof(PrepareForTvViewModel.SideBySide)
                     or nameof(PrepareForTvViewModel.Background) or nameof(PrepareForTvViewModel.SecondPhoto))
            {
                UpdateOverlay();
                if (Overlay?.Frame is { } screen)
                    ZoomToShow(doc, screen);
                _ = RefreshTvPreviewAsync();
            }
        };
        Tv = tv;
        ProposeTvFrame(area);
    }

    /// <summary>
    /// Puts the TV frame at the resolution's size (one image pixel per TV pixel), centered on <paramref name="around"/>
    /// and on the photo (covering it when larger), and zooms out if needed so the whole frame is visible: a frame larger
    /// than the photo shows that the photo will be enlarged. Resizing the frame is undone by choosing a resolution again.
    /// </summary>
    private void ProposeTvFrame(RectangleI around)
    {
        if (Tv is not { } tv || ActiveImageTab is not { } d || CropTool is not { } crop)
            return;
        var doc = d.Image;
        var (iw, ih) = (doc.ImageSize.Width, doc.ImageSize.Height);
        var size = TvExport.SizeOf(tv.Resolution);
        var (cx, cy) = (around.X + around.Width / 2.0, around.Y + around.Height / 2.0);
        var frame = new RectangleD(
            Math.Round(Core.Tools.CropTool.KeepOnImage(cx - size.Width / 2.0, size.Width, iw)),
            Math.Round(Core.Tools.CropTool.KeepOnImage(cy - size.Height / 2.0, size.Height, ih)),
            size.Width, size.Height);
        crop.Propose(doc, frame);
        UpdateOverlay();
        ZoomToShow(doc, tv.ShowsFrame ? frame : TvScreen(doc, tv.Resolution));
        _ = RefreshTvPreviewAsync();
    }

    /// <summary>
    /// Zooms out (never in) so <paramref name="area"/> (image pixels, possibly beyond the image) is visible: the canvas
    /// is centered in the view, so the image and the area must fit around its center.
    /// </summary>
    private void ZoomToShow(ImageDocument doc, RectangleD area)
    {
        if (ViewportSize.Width <= 0 || ViewportSize.Height <= 0)
            return;
        var (iw, ih) = (doc.ImageSize.Width, doc.ImageSize.Height);
        var halfWidth = Math.Max(Math.Max(iw / 2.0, iw / 2.0 - area.X), area.X + area.Width - iw / 2.0);
        var halfHeight = Math.Max(Math.Max(ih / 2.0, ih / 2.0 - area.Y), area.Y + area.Height - ih / 2.0);
        var fit = 0.9 * Math.Min(ViewportSize.Width / (2 * halfWidth), ViewportSize.Height / (2 * halfHeight));
        if (fit < doc.Workspace.Scale)
            SetZoomPercent(fit * 100);
    }

    /// <summary>The TV screen at one image pixel per TV pixel, centered on the photo.</summary>
    private static RectangleD TvScreen(ImageDocument doc, TvResolution resolution)
    {
        var size = TvExport.SizeOf(resolution);
        return new RectangleD(Math.Round((doc.ImageSize.Width - size.Width) / 2.0),
            Math.Round((doc.ImageSize.Height - size.Height) / 2.0), size.Width, size.Height);
    }

    // Preview of the TV image for Fit with borders, Stretch and Side by side (computed in the background).
    private OverlayPicture? _tvPreview;
    private int _tvPreviewVersion;

    /// <summary>
    /// What the TV shows when there is no frame to place: a preview of the TV image in a screen of the TV's size
    /// (one image pixel per TV pixel) centered on the photo, which is shaded around it.
    /// </summary>
    private ToolOverlay TvOverlay(PrepareForTvViewModel tv, ImageDocument doc)
    {
        var screen = TvScreen(doc, tv.Resolution);
        return new ToolOverlay
        {
            Frame = screen,
            Shade = screen,
            Picture = _tvPreview is { } preview && preview.Area == screen ? preview : null,
        };
    }

    /// <summary>Recomputes the preview when an option changes; the latest request wins.</summary>
    private async Task RefreshTvPreviewAsync()
    {
        var version = ++_tvPreviewVersion;
        if (Tv is not { ShowsFrame: false } tv || ActiveImageTab is not { } d)
        {
            _tvPreview = null;
            return;
        }
        var doc = d.Image;
        var screen = TvScreen(doc, tv.Resolution);
        var options = tv.Options;
        var photo = new BgraImage(doc.Layers.GetFlattenedBgra(includeToolLayer: false), doc.ImageSize.Width, doc.ImageSize.Height);
        var other = tv.SideBySide ? tv.SecondPhoto?.Image : null;
        var second = other is null
            ? null
            : new BgraImage(other.Layers.GetFlattenedBgra(includeToolLayer: false), other.ImageSize.Width, other.ImageSize.Height);
        // As many pixels as the screen shows (twice for high-DPI displays), never more than the TV has.
        var width = (int)Math.Clamp(screen.Width * doc.Workspace.Scale * 2, 64, Math.Min(screen.Width, 2560));
        var size = new ImageSize(width, (int)Math.Round(width * 9 / 16.0));

        var result = await Task.Run(() => second is null
            ? TvExport.Compose(photo, options, size: size)
            : TvExport.SideBySide(photo, second, options, size));
        if (version != _tvPreviewVersion || Tv != tv)
            return;
        _tvPreview = new OverlayPicture(result.Pixels, result.Width, result.Height, screen);
        UpdateOverlay();
    }

    [RelayCommand(CanExecute = nameof(IsTvMode))]
    private async Task ApplyTv()
    {
        if (Tv is not { } tv || ActiveImageTab is not { } d)
            return;
        var doc = d.Image;
        var crop = tv.ShowsFrame ? CropTool?.Frame(doc) : null;
        var tvOptions = tv.Options;
        var other = tv.SideBySide ? tv.SecondPhoto?.Image : null;
        TvOptions = tvOptions;
        ExitTv();

        var photo = new BgraImage(doc.Layers.GetFlattenedBgra(includeToolLayer: false), doc.ImageSize.Width, doc.ImageSize.Height);
        var second = other is null
            ? null
            : new BgraImage(other.Layers.GetFlattenedBgra(includeToolLayer: false), other.ImageSize.Width, other.ImageSize.Height);
        var result = await RunBusyAsync("Preparing for TV", _ => Task.Run(() => second is null
            ? TvExport.Compose(photo, tvOptions, crop)
            : TvExport.SideBySide(photo, second, tvOptions)));

        var name = Path.GetFileNameWithoutExtension(doc.DisplayName);
        var image = _workspace.NewDocumentFromImage(new ClipboardImage(result.Pixels, result.Width, result.Height));
        image.DisplayName = name + TvExport.Suffix(tvOptions.Resolution);
        image.FileType = "jpg";
        FitIfLargerThanViewport(image);
    }

    [RelayCommand(CanExecute = nameof(IsTvMode))]
    private void CancelTv() => ExitTv();

    /// <summary>Removes the TV frame and goes back to the tool used before (unless the user picked another one).</summary>
    private void ExitTv()
    {
        if (Tv is null)
            return;
        Tv = null;
        _tvPreview = null;
        _tvPreviewVersion++;
        if (CropTool is { } crop)
        {
            crop.ForcedRatio = null;
            crop.CanDrawNewFrame = true;
            if (ActiveImageTab is { } d)
                crop.Finish(d.Image);
        }
        if (_toolBeforeTv is { } tool)
            SelectedTool = tool;
        _toolBeforeTv = null;
        UpdateOverlay();
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
        var (output, count) = await RunBusyAsync("Preparing photos for TV", percent =>
        {
            var photos = new Progress<(int Done, int Total)>(p =>
            {
                BusyText = $"Preparing photos for TV… {p.Done} / {p.Total}";
                percent.Report(100.0 * p.Done / p.Total);
            });
            return Task.Run(() => TvExport.ExportFolder(new DirectoryInfo(folder), tvOptions, quality, photos));
        });
        await Dialogs.ShowMessageAsync("Photos ready for the TV",
            count == 0 ? "No photos were found in this folder." : $"{count} photo{(count > 1 ? "s" : "")} saved in \"{output.FullName}\".");
    }

    // ---- Image ----

    [RelayCommand(CanExecute = nameof(HasContent))]
    private async Task ResizeImage()
    {
        if (Dialogs is not null && ActiveSvg is { } drawing)
        {
            // A drawing is scaled with its page: the objects keep their place and proportions on the page.
            if (await Dialogs.ShowResizeImageAsync(drawing.ImageSize, resampling: false) is { } size)
                drawing.Actions.ResizePage(size.Size, keepContentAt: null);
            return;
        }
        if (Dialogs is null || ActiveImageTab is not { } d)
            return;
        if (await Dialogs.ShowResizeImageAsync(d.Image.ImageSize) is { } options)
            d.Image.Actions.ResizeImage(options.Size, options.Resampling);
    }

    [RelayCommand(CanExecute = nameof(HasContent))]
    private async Task CanvasSize()
    {
        if (Dialogs is not null && ActiveSvg is { } drawing)
        {
            // The page grows or shrinks around the anchor; the objects keep their size and place on it.
            if (await Dialogs.ShowCanvasSizeAsync(drawing.ImageSize) is { } size)
                drawing.Actions.ResizePage(size.Size, size.Anchor);
            return;
        }
        if (Dialogs is null || ActiveImageTab is not { } d)
            return;
        if (await Dialogs.ShowCanvasSizeAsync(d.Image.ImageSize) is { } options)
            d.Image.Actions.ResizeCanvas(options.Size, options.Anchor, ToolSettings.SecondaryColor);
    }

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void FlipImageHorizontal() => ActiveImageTab?.Image.Actions.FlipImageHorizontal();

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void FlipImageVertical() => ActiveImageTab?.Image.Actions.FlipImageVertical();

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void RotateClockwise() => ActiveImageTab?.Image.Actions.RotateImage90(clockwise: true);

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void RotateCounterClockwise() => ActiveImageTab?.Image.Actions.RotateImage90(clockwise: false);

    [RelayCommand(CanExecute = nameof(HasImage))]
    private void Rotate180() => ActiveImageTab?.Image.Actions.RotateImage180();

    // ---- Colors ----

    [RelayCommand]
    private void SwapColors() => (PrimaryColor, SecondaryColor) = (SecondaryColor, PrimaryColor);

    /// <summary>Disabled placeholder for menu items implemented in later phases.</summary>
    public IRelayCommand NotYetImplemented { get; } = new RelayCommand(() => { }, () => false);

    public void UpdateCursorPosition(Core.Models.PointD? canvasPoint)
    {
        CursorPositionText = canvasPoint is { } p ? $"{(int)Math.Floor(p.X)}, {(int)Math.Floor(p.Y)}" : "";
        if (canvasPoint is { } hoverPoint && ActiveSvg is { } hoverDrawing && !IsBusy && ActiveVectorTool is IVectorHoverTool hoverTool
            && hoverTool.OnHover(hoverDrawing, ImageToUser(hoverDrawing, hoverPoint)))
            UpdateOverlay();
        HoverCursor = canvasPoint is { } point && ActiveSvg is { } drawing && ActiveVectorTool is { } vectorTool
            ? vectorTool.CursorAt(drawing, ImageToUser(drawing, point))
            : canvasPoint is { } imagePoint && ActiveImageTab is { } d && ActiveTool is IOverlayTool tool
                ? tool.CursorAt(d.Image, imagePoint)
                : ToolCursor.Default;
    }

    /// <summary>What the selected tool wants the cursor to show under the mouse (e.g. resize arrows over a handle).</summary>
    [ObservableProperty]
    public partial ToolCursor HoverCursor { get; set; }

    partial void OnActiveDocumentChanged(DocumentViewModel? value)
    {
        SyncToolbox();
        RefreshGuides();
        if (value is not null && !_syncingSelection)
            _workspace.SetActiveDocument(value.Document);
        RefreshLayers();
        RefreshHistory();
        RefreshSelectionState();
        RefreshViewState();
        UpdateOverlay();
        if (value is { Thumbnail: null })
            value.RefreshThumbnail();
        foreach (var command in new IRelayCommand[]
                 {
                     ZoomInCommand, ZoomOutCommand, ActualSizeCommand, BestFitCommand,
                     SaveCommand, SaveAsCommand, ExportAsCommand, CloseCommand,
                     SelectAllCommand, InvertSelectionCommand, EraseSelectionCommand, FillSelectionCommand,
                     CopyCommand, CopyMergedCommand, CutCommand, PasteIntoNewLayerCommand, PasteBesideCommand,
                     AutoLevelCommand, BlackAndWhiteCommand, BrightnessContrastCommand, HueSaturationCommand,
                     InvertColorsCommand, LevelsCommand, CurvesCommand, PosterizeCommand, SepiaCommand,
                     RepeatEffectCommand, ApplyEffectCommand,
                     ResizeImageCommand, CanvasSizeCommand, FlipImageHorizontalCommand, FlipImageVerticalCommand, PrepareForTvCommand,
                     RotateClockwiseCommand, RotateCounterClockwiseCommand, Rotate180Command,
                 })
            command.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(HasSvg));
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(ShowGridToggle));
        OnPropertyChanged(nameof(ShowGridOptions));
        OnPropertyChanged(nameof(IsNotSvg));
        OnPropertyChanged(nameof(HasAnySelection));
        RasterizeCommand.NotifyCanExecuteChanged();
        RefreshObjects();
        Properties.Refresh();
        RefreshWelcome();
    }

    partial void OnSelectedLayerChanged(LayerViewModel? value)
    {
        if (value is not null && !_syncingSelection && ActiveImageTab is { } d)
            d.Image.Layers.SetCurrentUserLayer(value.Layer);
    }

    private double BestFitPercent(IDocument doc)
    {
        if (ViewportSize.Width <= 0 || ViewportSize.Height <= 0)
            return 100;
        var fit = Math.Min(ViewportSize.Width / doc.ImageSize.Width, ViewportSize.Height / doc.ImageSize.Height);
        return Math.Min(100, fit * 100);
    }

    private void FitIfLargerThanViewport(IDocument doc)
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
                    // A new document is activated before its first layer exists: its tab picture is made now.
                    if (ActiveDocument is { Thumbnail: null } tab)
                        tab.RefreshThumbnail();
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

            case DocumentEventEnum.VectorTreeChanged:
                if (e.Document == ActiveDocument?.Document)
                {
                    RenderVersion++;
                    ActiveDocument?.RefreshThumbnail();
                    RefreshObjects();
                    Properties.Refresh();
                }
                break;

            case DocumentEventEnum.VectorNodeChanged:
                if (e.Document == ActiveDocument?.Document && e is VectorNodeEventItem node)
                {
                    if (node.DirtyBounds is { } dirty && node.Document is SvgDocument drawing)
                        RegionInvalidated?.Invoke(drawing.UserToImage.TransformBounds(dirty).ToOuterPixels());
                    else
                        RenderVersion++;
                    // A name, visibility or lock may have changed.
                    Objects.FirstOrDefault(o => o.Node == node.Node)?.Refresh();
                    UpdateOverlay();
                    if (node.Document is SvgDocument changed && changed.Selection.Nodes.Contains(node.Node))
                        Properties.Refresh();
                }
                break;

            case DocumentEventEnum.VectorSelectionChanged:
                if (e.Document == ActiveDocument?.Document)
                {
                    SelectionVersion++;
                    SyncObjectSelection();
                    Properties.Refresh();
                    OnPropertyChanged(nameof(HasAnySelection));
                    UpdateOverlay();
                }
                break;

            case DocumentEventEnum.ViewSizeChanged:
                if (e.Document == ActiveDocument?.Document)
                {
                    ViewVersion++;
                    RefreshViewText();
                }
                break;
        }
    }

    private void RefreshViewState()
    {
        RenderVersion++;
        RefreshViewText();
    }

    private void RefreshViewText()
    {
        OnPropertyChanged(nameof(ImageSizeText));
        OnPropertyChanged(nameof(ZoomText));
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void RefreshLayers()
    {
        var layers = ActiveImageTab?.Image.Layers;
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
                Layers.Add(new LayerViewModel(layer, ActiveImageTab!.Image.Actions));
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
        ActiveDocument?.RefreshThumbnail();
    }

    public void Dispose() => _eventsSubscription.Dispose();

    private sealed class EventObserver(Action<EventItem<DocumentEventEnum>> onNext) : IObserver<EventItem<DocumentEventEnum>>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(EventItem<DocumentEventEnum> value) => onNext(value);
    }
}
