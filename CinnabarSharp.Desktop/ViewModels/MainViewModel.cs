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
using CinnabarSharp.Core.Tools;
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
        RecentFilesStore recentFiles)
    {
        _workspace = workspace;
        _formats = formats;
        RecentFiles = recentFiles;
        Tools = ToolViewModel.CreatePaintDotNetTools(ToolSettings);
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

    [ObservableProperty]
    public partial DocumentViewModel? ActiveDocument { get; set; }

    [ObservableProperty]
    public partial LayerViewModel? SelectedLayer { get; set; }

    [ObservableProperty]
    public partial ToolViewModel SelectedTool { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryBrush))]
    public partial Color PrimaryColor { get; set; } = Colors.Black;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecondaryBrush))]
    public partial Color SecondaryColor { get; set; } = Colors.White;

    public IBrush PrimaryBrush => new SolidColorBrush(PrimaryColor);
    public IBrush SecondaryBrush => new SolidColorBrush(SecondaryColor);

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

        if (file is null || format is null)
        {
            if (Dialogs is null)
                return false;
            var suggestedFormat = (doc.FileType is { } t ? _formats.GetFormatByExtension(t) : null)
                ?? _formats.GetFormatByExtension("png")!;
            var path = await Dialogs.PickFileToSaveAsync(doc.DisplayName, suggestedFormat, _formats.Formats);
            if (path is null)
                return false;
            if (_formats.GetFormatByExtension(Path.GetExtension(path)) is null)
                path += "." + suggestedFormat.SupportedExtensions[0];
            file = new FileInfo(path);
            format = _formats.GetFormatByExtension(file.Extension)!;
        }

        if (doc.Layers.Count() > 1 && !format.SupportsLayers && Dialogs is not null
            && !await Dialogs.ConfirmAsync("Flatten image?",
                $"{format.DisplayName} files can't store layers, so the saved file will contain the visible layers merged into one. Your layers are kept in CinnabarSharp.",
                "Flatten and Save"))
            return false;

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

    public void ToolPointerDown(ToolPointer pointer) => WithTool(t => t.OnPointerDown, pointer);
    public void ToolPointerMove(ToolPointer pointer) => WithTool(t => t.OnPointerMove, pointer);
    public void ToolPointerUp(ToolPointer pointer) => WithTool(t => t.OnPointerUp, pointer);

    private void WithTool(Func<ITool, Action<ImageDocument, ToolPointer>> handler, ToolPointer pointer)
    {
        if (ActiveDocument is { } d && SelectedTool.Tool is { } tool)
            handler(tool)(d.Document, pointer);
    }

    partial void OnSelectedToolChanged(ToolViewModel value)
    {
        OnPropertyChanged(nameof(ShowSelectionOptions));
        OnPropertyChanged(nameof(ShowMagicWandOptions));
    }

    public bool ShowSelectionOptions => SelectedTool.IsSelectionTool;
    public bool ShowMagicWandOptions => SelectedTool.IsMagicWand;

    // ---- Selection and clipboard ----

    /// <summary>Incremented when the selection changes, to redraw the marching ants.</summary>
    [ObservableProperty]
    public partial int SelectionVersion { get; set; }

    public bool HasSelection => ActiveDocument?.Document.HasSelection == true;

    public string SelectionSizeText => ActiveDocument?.Document.Selection is { } s
        ? $"Selection {s.Bounds.Width} × {s.Bounds.Height}"
        : "";

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void SelectAll() => ActiveDocument?.Document.Actions.SelectAll();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeselectAll() => ActiveDocument?.Document.Actions.DeselectAll();

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void InvertSelection() => ActiveDocument?.Document.Actions.InvertSelection();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CropToSelection() => ActiveDocument?.Document.Actions.CropToSelection();

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void EraseSelection() => EditLayers(a => a.EraseSelection());

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private void FillSelection() => EditLayers(a =>
        a.FillSelection(ColorBgra.FromBgra(PrimaryColor.B, PrimaryColor.G, PrimaryColor.R, PrimaryColor.A)));

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task Copy() => CopyAsync(merged: false);

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private Task CopyMerged() => CopyAsync(merged: true);

    private async Task CopyAsync(bool merged)
    {
        if (ActiveDocument is { } d && Clipboard is not null)
            await Clipboard.SetImageAsync(d.Document.Actions.Copy(merged));
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task Cut()
    {
        if (ActiveDocument is not { } d || Clipboard is null)
            return;
        ClipboardImage? image = null;
        EditLayers(a => image = a.Cut());
        await Clipboard.SetImageAsync(image!);
    }

    /// <summary>Pastes onto the current layer and switches to Move Selected Pixels; with no image open, pastes into a new image.</summary>
    [RelayCommand]
    private async Task Paste()
    {
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

    // ---- Colors ----

    [RelayCommand]
    private void SwapColors() => (PrimaryColor, SecondaryColor) = (SecondaryColor, PrimaryColor);

    /// <summary>Disabled placeholder for menu items implemented in later phases.</summary>
    public IRelayCommand NotYetImplemented { get; } = new RelayCommand(() => { }, () => false);

    public void UpdateCursorPosition(Core.Models.PointD? canvasPoint)
    {
        CursorPositionText = canvasPoint is { } p ? $"{(int)Math.Floor(p.X)}, {(int)Math.Floor(p.Y)}" : "";
    }

    partial void OnActiveDocumentChanged(DocumentViewModel? value)
    {
        if (value is not null && !_syncingSelection)
            _workspace.SetActiveDocument(value.Document);
        RefreshLayers();
        RefreshHistory();
        RefreshSelectionState();
        RefreshViewState();
        foreach (var command in new IRelayCommand[]
                 {
                     ZoomInCommand, ZoomOutCommand, ActualSizeCommand, BestFitCommand,
                     SaveCommand, SaveAsCommand, CloseCommand,
                     SelectAllCommand, InvertSelectionCommand, EraseSelectionCommand, FillSelectionCommand,
                     CopyCommand, CopyMergedCommand, CutCommand, PasteIntoNewLayerCommand,
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
                    RefreshHistory();
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
                if (e.Document == ActiveDocument?.Document)
                {
                    RefreshViewState();
                    RefreshThumbnails();
                }
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
