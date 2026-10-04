using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public sealed class HistoryTests : BaseTests, IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("cinnabarsharp-history-");
    private readonly IServiceProvider _sp;
    private readonly IWorkspaceService _workspace;

    public HistoryTests()
    {
        _sp = CinnabarSharpService();
        _workspace = _sp.GetRequiredService<IWorkspaceService>();
    }

    public void Dispose() => _dir.Delete(recursive: true);

    /// <summary>Background (white), "Red" at 50%, "Blue" hidden; "Blue" is current.</summary>
    private ImageDocument ThreeLayers()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);
        var red = doc.Layers.AddNewLayer("Red");
        red.Surface = Solid(0, 0, 255);
        red.Opacity = 0.5;
        doc.Layers.SetCurrentUserLayer(red);
        var blue = doc.Layers.AddNewLayer("Blue");
        blue.Surface = Solid(255, 0, 0);
        blue.Hidden = true;
        doc.Layers.SetCurrentUserLayer(blue);
        return doc;
    }

    private static ImageMagick.IMagickImage<byte> Solid(byte b, byte g, byte r)
    {
        var px = new byte[4 * 3 * 4];
        for (var i = 0; i < px.Length; i += 4)
            (px[i], px[i + 1], px[i + 2], px[i + 3]) = (b, g, r, 255);
        (px[0], px[1], px[2]) = (0, 0, 0); // top-left pixel differs so flips are visible
        return Utility.FromBgra(px, 4, 3);
    }

    /// <summary>Everything undo must restore: order, names, properties, pixels, current layer.</summary>
    private static string Snapshot(ImageDocument doc) =>
        string.Join(" | ", doc.Layers.UserLayers.Select(l =>
            $"{l.Name},{l.Hidden},{l.Opacity},{l.BlendMode},{Convert.ToHexString(l.Surface.ToBgra())}"))
        + $" | current={doc.Layers.CurrentUserLayerIndex} size={doc.ImageSize}"
        + $" selection={(doc.Selection is { } s ? Convert.ToHexString(s.Data) : "none")}";

    private static IImageDocumentHistory History(ImageDocument doc) => doc.Workspace.History;

    [Fact]
    public void New_document_starts_clean_with_one_step()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);

        Assert.Equal(["New Image"], History(doc).Items.Select(i => i.Text));
        Assert.False(History(doc).CanUndo);
        Assert.False(History(doc).CanRedo);
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void Opened_document_starts_with_open_image_step()
    {
        var doc = _sp.GetRequiredService<IFormatManager>().Open(ImageSample1());

        Assert.Equal(["Open Image"], History(doc).Items.Select(i => i.Text));
        Assert.False(doc.IsDirty);
    }

    public static TheoryData<string, Action<DocumentActions>> Actions => new()
    {
        { "Add New Layer", a => a.AddNewLayer() },
        { "Duplicate Layer", a => a.DuplicateCurrentLayer() },
        { "Delete Layer", a => a.DeleteCurrentLayer() },
        { "Move Layer Down", a => a.MoveCurrentLayerDown() },
        { "Flip Layer Horizontal", a => a.FlipCurrentLayerHorizontal() },
        { "Flip Layer Vertical", a => a.FlipCurrentLayerVertical() },
        { "Merge Layer Down", a => a.MergeCurrentLayerDown() },
        { "Flatten", a => a.Flatten() },
        { "Select All", a => a.SelectAll() },
        { "Invert Selection", a => a.InvertSelection() },
        { "Erase Selection", a => a.EraseSelection() },
        { "Fill Selection", a => a.FillSelection(ColorBgra.FromBgra(0, 255, 0, 255)) },
        { "Cut", a => a.Cut() },
        { "Paste", a => a.Paste(new ClipboardImage([9, 9, 9, 255, 9, 9, 9, 255], 2, 1), new PointI(1, 1)) },
        { "Paste Into New Layer", a => a.PasteIntoNewLayer(new ClipboardImage([9, 9, 9, 255], 1, 1), new PointI(3, 2)) },
        { "Flip Image Horizontal", a => a.FlipImageHorizontal() },
        { "Flip Image Vertical", a => a.FlipImageVertical() },
        { "Rotate 90° Clockwise", a => a.RotateImage90(clockwise: true) },
        { "Rotate 90° Counter-Clockwise", a => a.RotateImage90(clockwise: false) },
        { "Rotate 180°", a => a.RotateImage180() },
        { "Canvas Size", a => a.ResizeCanvas(new ImageSize(6, 5), Anchor.Center, ColorBgra.Black) },
        { "Paste Beside", a => a.PasteBeside(new ClipboardImage([9, 9, 9, 255, 9, 9, 9, 255], 1, 2), PasteSide.Left, EdgeAlignment.End, ColorBgra.Black) },
        { "Resize Image", a => a.ResizeImage(new ImageSize(8, 6), ResamplingMode.NearestNeighbor) },
        { "Comic Page", a => a.ReplaceLayerPixels("Comic Page", Enumerable.Repeat((byte)77, 4 * 3 * 4).ToArray()) },
    };

    [Fact]
    public void Deselect_and_crop_undo_exactly()
    {
        var doc = ThreeLayers();
        doc.Actions.SelectAll();
        doc.SetSelection(SelectionMask.Rectangle(4, 3, new PointD(1, 0), new PointD(3, 2)));
        doc.Actions.RecordSelectionChange(null, "Rectangle Select");
        var before = Snapshot(doc);

        doc.Actions.CropToSelection();
        Assert.NotEqual(before, Snapshot(doc));
        History(doc).Undo();
        Assert.Equal(before, Snapshot(doc));

        doc.Actions.DeselectAll();
        History(doc).Undo();
        Assert.Equal(before, Snapshot(doc));
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void Every_action_undoes_and_redoes_exactly(string text, Action<DocumentActions> action)
    {
        var doc = ThreeLayers();
        var before = Snapshot(doc);

        action(doc.Actions);
        var after = Snapshot(doc);
        Assert.NotEqual(before, after);
        Assert.Equal(text, History(doc).Items[^1].Text);
        Assert.True(doc.IsDirty);

        History(doc).Undo();
        Assert.Equal(before, Snapshot(doc));
        Assert.False(doc.IsDirty);

        History(doc).Redo();
        Assert.Equal(after, Snapshot(doc));
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Merge_down_of_visible_layer_restores_pixels_below()
    {
        var doc = ThreeLayers();
        doc.Layers.SetCurrentUserLayer(1);
        var before = Snapshot(doc);

        doc.Actions.MergeCurrentLayerDown();
        History(doc).Undo();

        Assert.Equal(before, Snapshot(doc));
    }

    [Fact]
    public void Move_up_and_import_undo()
    {
        var doc = ThreeLayers();
        doc.Layers.SetCurrentUserLayer(0);
        var before = Snapshot(doc);

        doc.Actions.MoveCurrentLayerUp();
        doc.Actions.ImportFromFile(ImageSample1());
        Assert.Equal(["New Image", "Move Layer Up", "Import From File"], History(doc).Items.Select(i => i.Text));

        History(doc).JumpTo(0);
        Assert.Equal(before, Snapshot(doc));
    }

    [Fact]
    public void Visibility_and_properties_are_recorded()
    {
        var doc = ThreeLayers();
        var red = doc.Layers[1];

        doc.Actions.SetLayerVisibility(red, false);
        var before = LayerProperties.From(red);
        red.Name = "Shadow";
        red.BlendMode = BlendMode.Multiply;
        doc.Actions.CommitLayerProperties(red, before);
        doc.Actions.CommitLayerProperties(red, LayerProperties.From(red));

        Assert.Equal(["New Image", "Hide Layer", "Layer Properties"], History(doc).Items.Select(i => i.Text));
        History(doc).Undo();
        Assert.Equal(("Red", BlendMode.Normal), (red.Name, red.BlendMode));
        History(doc).Undo();
        Assert.False(red.Hidden);
    }

    [Fact]
    public void Saving_marks_clean_and_undo_after_save_is_dirty()
    {
        var doc = ThreeLayers();
        doc.Actions.AddNewLayer();
        _sp.GetRequiredService<IFormatManager>().Save(doc, new FileInfo(Path.Combine(_dir.FullName, "a.ora")));
        Assert.False(doc.IsDirty);

        History(doc).Undo();
        Assert.True(doc.IsDirty);

        History(doc).Redo();
        Assert.False(doc.IsDirty);
    }

    [Fact]
    public void New_step_after_undo_discards_redo_and_saved_state()
    {
        var doc = ThreeLayers();
        doc.Actions.AddNewLayer();
        _sp.GetRequiredService<IFormatManager>().Save(doc, new FileInfo(Path.Combine(_dir.FullName, "b.ora")));
        History(doc).Undo();

        doc.Actions.FlipCurrentLayerVertical();
        History(doc).Undo();

        Assert.Equal(["New Image", "Flip Layer Vertical"], History(doc).Items.Select(i => i.Text));
        Assert.True(doc.IsDirty);
        Assert.True(History(doc).CanRedo);
    }

    [Fact]
    public void Discarded_steps_release_layers_no_longer_in_the_document()
    {
        var doc = ThreeLayers();
        var added = doc.Actions.AddNewLayer();
        History(doc).Undo();

        doc.Actions.FlipCurrentLayerHorizontal();

        Assert.Throws<ObjectDisposedException>(() => added.Surface.Width);
        Assert.Equal(3u, doc.Layers[0].Surface.Height);
    }

    [Fact]
    public void Closing_releases_history_and_layers()
    {
        var doc = ThreeLayers();
        var bottom = doc.Layers[0];
        doc.Actions.Flatten();
        var flattened = bottom.Surface;

        _workspace.CloseDocument(doc);

        Assert.Empty(History(doc).Items);
        Assert.Throws<ObjectDisposedException>(() => flattened.Width);
    }

    [Fact]
    public void Undo_and_redo_fire_history_and_canvas_events()
    {
        var doc = ThreeLayers();
        doc.Actions.AddNewLayer();
        var received = new List<DocumentEventEnum>();
        using var sub = _sp.GetRequiredService<IDocumentEventsService>().DocumentEvents
            .Subscribe(new Observer(e => received.Add(e.State)));

        History(doc).Undo();

        Assert.Contains(DocumentEventEnum.LayerRemoved, received);
        Assert.Contains(DocumentEventEnum.CanvasInvalidated, received);
        Assert.Equal(DocumentEventEnum.HistoryChanged, received[^1]);
    }

    // Zoom must not invalidate the canvas: CanvasInvalidated makes the view re-flatten every layer.
    [Fact]
    public void Zoom_fires_view_size_changed_only()
    {
        var doc = ThreeLayers();
        var received = new List<DocumentEventEnum>();
        using var sub = _sp.GetRequiredService<IDocumentEventsService>().DocumentEvents
            .Subscribe(new Observer(e => received.Add(e.State)));

        doc.Workspace.Scale = 2;
        Assert.Equal([DocumentEventEnum.ViewSizeChanged], received);

        received.Clear();
        doc.Workspace.Scale = 2.0000001; // same view size after rounding, but the zoom text changes
        Assert.Equal([DocumentEventEnum.ViewSizeChanged], received);

        received.Clear();
        doc.Workspace.Scale = 2.0000001;
        Assert.Empty(received);
    }

    [Fact]
    public void Fill_selection_records_only_the_selection_bounds()
    {
        var doc = ThreeLayers();
        var before = Snapshot(doc);
        doc.SetSelection(SelectionMask.Rectangle(4, 3, new PointD(1, 0), new PointD(3, 2))); // 2x2
        doc.Actions.RecordSelectionChange(null, "Rectangle Select");

        doc.Actions.FillSelection(ColorBgra.FromBgra(0, 255, 0, 255));

        var item = Assert.IsType<PixelRegionHistoryItem>(History(doc).Items[^1]);
        Assert.Equal(new RectangleI(1, 0, 2, 2), item.Rect); // the 2x2 selection bounds, not the whole 4x3 layer

        // Pixel (0,0) is outside the selection ([1,3) x [0,2)) and Solid() makes it black; it must stay untouched.
        Assert.Equal<byte[]>([0, 0, 0, 255], doc.Layers.CurrentUserLayer.Surface.ReadRegion(new RectangleI(0, 0, 1, 1)));

        History(doc).Undo();
        History(doc).Undo();
        Assert.Equal(before, Snapshot(doc));
    }

    [Theory]
    [InlineData("Erase Selection")]
    [InlineData("Fill Selection")]
    public async Task Pixel_edit_undoes_and_redoes_exactly_after_background_compression_finishes(string text)
    {
        var doc = ThreeLayers();
        var before = Snapshot(doc);

        if (text == "Erase Selection")
            doc.Actions.EraseSelection();
        else
            doc.Actions.FillSelection(ColorBgra.FromBgra(0, 255, 0, 255));
        var after = Snapshot(doc);

        var item = Assert.IsType<PixelRegionHistoryItem>(History(doc).Items[^1]);
        await item.PendingCompression; // undo/redo must still be correct once the diff is compressed, not just raw

        History(doc).Undo();
        Assert.Equal(before, Snapshot(doc));
        History(doc).Redo();
        Assert.Equal(after, Snapshot(doc));
    }

    [Fact]
    public void No_op_pixel_edit_records_nothing()
    {
        var doc = ThreeLayers();
        doc.Actions.EraseSelection(); // whole layer becomes transparent
        var afterFirstErase = History(doc).Items.Count;

        doc.Actions.EraseSelection(); // already fully transparent: nothing changes, nothing recorded

        Assert.Equal(afterFirstErase, History(doc).Items.Count);
    }

    [Fact]
    public void History_drops_oldest_steps_over_the_step_limit()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);
        var events = _sp.GetRequiredService<IDocumentEventsService>();
        var history = new ImageDocumentHistory(doc, events, byteBudget: long.MaxValue, maxSteps: 3);
        var received = new List<DocumentEventEnum>();
        using var sub = events.DocumentEvents.Subscribe(
            new Observer(e => { if (e.Document == doc) received.Add(e.State); }));

        history.PushNewItem(new DummyHistoryItem("Base", 0));
        history.PushNewItem(new DummyHistoryItem("A", 0));
        history.PushNewItem(new DummyHistoryItem("B", 0));
        history.PushNewItem(new DummyHistoryItem("C", 0)); // 4th step: over the limit of 3

        Assert.Equal(["A", "B", "C"], history.Items.Select(i => i.Text));
        Assert.Equal(2, history.Pointer);
        Assert.False(history.CanUndo && history.Pointer == 0);
        Assert.Contains(DocumentEventEnum.HistoryTrimmed, received);
    }

    [Fact]
    public void History_drops_oldest_steps_over_the_byte_budget()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);
        var events = _sp.GetRequiredService<IDocumentEventsService>();
        var history = new ImageDocumentHistory(doc, events, byteBudget: 250, maxSteps: 1000);

        history.PushNewItem(new DummyHistoryItem("Base", 100));
        history.PushNewItem(new DummyHistoryItem("A", 100));
        history.PushNewItem(new DummyHistoryItem("B", 100)); // 300 > 250: drop "Base"

        Assert.Equal(["A", "B"], history.Items.Select(i => i.Text));
        Assert.Equal(200, history.Bytes);
    }

    [Fact]
    public void History_trim_warning_fires_only_once()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);
        var events = _sp.GetRequiredService<IDocumentEventsService>();
        var history = new ImageDocumentHistory(doc, events, byteBudget: long.MaxValue, maxSteps: 2);
        var received = new List<DocumentEventEnum>();
        using var sub = events.DocumentEvents.Subscribe(
            new Observer(e => { if (e.Document == doc) received.Add(e.State); }));

        for (var i = 0; i < 5; i++)
            history.PushNewItem(new DummyHistoryItem($"Step{i}", 0));

        Assert.Equal(1, received.Count(e => e == DocumentEventEnum.HistoryTrimmed));
    }

    [Fact]
    public void History_never_trims_below_the_current_step()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);
        var events = _sp.GetRequiredService<IDocumentEventsService>();
        // A single huge step must never be dropped just because it's the only one over budget.
        var history = new ImageDocumentHistory(doc, events, byteBudget: 10, maxSteps: 1000);

        history.PushNewItem(new DummyHistoryItem("Base", 1_000_000));

        Assert.Single(history.Items);
        Assert.Equal(0, history.Pointer);
    }

    [Fact]
    public void History_spills_steps_far_from_the_pointer_to_disk()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);
        var events = _sp.GetRequiredService<IDocumentEventsService>();
        var storage = new FileHistoryStorage(_dir);
        var history = new ImageDocumentHistory(doc, events, byteBudget: long.MaxValue, maxSteps: 1000,
            storage: storage, spillDistance: 2);
        var items = new List<DummySpillableHistoryItem>();
        for (var i = 0; i < 5; i++)
        {
            var item = new DummySpillableHistoryItem($"Step{i}");
            items.Add(item);
            history.PushNewItem(item);
        }
        // Pointer is at the last item (index 4); items at distance >= 2 (index 0,1,2) must be spilled.
        Assert.All(items.Take(3), i => Assert.True(i.Spilled));
        Assert.All(items.Skip(3), i => Assert.False(i.Spilled));
    }

    [Fact]
    public void History_with_no_storage_never_spills()
    {
        var doc = _workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);
        var events = _sp.GetRequiredService<IDocumentEventsService>();
        var history = new ImageDocumentHistory(doc, events, maxSteps: 1000, storage: null, spillDistance: 1);
        var items = new List<DummySpillableHistoryItem>();
        for (var i = 0; i < 5; i++)
        {
            var item = new DummySpillableHistoryItem($"Step{i}");
            items.Add(item);
            history.PushNewItem(item);
        }

        Assert.All(items, i => Assert.False(i.Spilled));
    }

    [Fact]
    public async Task Fill_selection_pixel_step_can_be_spilled_and_read_back_from_disk()
    {
        // A document created through DI with IHistoryStorage registered: the real wiring an app would use to
        // opt in, not a hand-built ImageDocumentHistory.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCinnabarSharpServices();
        services.AddSingleton<IHistoryStorage>(new FileHistoryStorage(_dir));
        var workspace = services.BuildServiceProvider().GetRequiredService<IWorkspaceService>();
        var doc = workspace.NewDocument(new ImageSize(4, 3), ColorBgra.White);
        var before = Snapshot(doc);

        doc.SetSelection(SelectionMask.Rectangle(4, 3, new PointD(1, 0), new PointD(3, 2)));
        doc.Actions.RecordSelectionChange(null, "Rectangle Select");
        doc.Actions.FillSelection(ColorBgra.FromBgra(0, 255, 0, 255));
        var after = Snapshot(doc);

        var item = Assert.IsType<PixelRegionHistoryItem>(doc.Workspace.History.Items[^1]);
        await item.PendingCompression;

        // Push enough steps after it to cross the default spill distance, then wait for the write to land.
        var padCount = ImageDocumentHistory.DefaultSpillDistance + 1;
        for (var i = 0; i < padCount; i++)
            doc.Workspace.History.PushNewItem(new DummyHistoryItem($"Pad{i}", 0));
        await item.PendingSpill;
        Assert.True(item.IsSpilled);

        var stepsSinceBefore = padCount + 2; // the pads, then the fill step, then the selection change
        for (var i = 0; i < stepsSinceBefore; i++)
            doc.Workspace.History.Undo();
        Assert.Equal(before, Snapshot(doc)); // read the diff back from disk

        for (var i = 0; i < stepsSinceBefore; i++)
            doc.Workspace.History.Redo();
        Assert.Equal(after, Snapshot(doc));
    }

    private sealed class DummySpillableHistoryItem(string text) : HistoryItem(text), ISpillableHistoryItem
    {
        public bool Spilled { get; private set; }
        public override long Bytes => Spilled ? 0 : 1;
        public void Spill(IHistoryDocumentStorage storage) => Spilled = true;
        protected override void OnUndo() { }
        protected override void OnRedo() { }
    }

    private sealed class DummyHistoryItem(string text, long bytes) : HistoryItem(text)
    {
        public override long Bytes => bytes;
        protected override void OnUndo() { }
        protected override void OnRedo() { }
    }

    [Fact]
    public void Undo_of_a_pixel_edit_invalidates_only_its_own_rectangle()
    {
        var doc = ThreeLayers();
        doc.SetSelection(SelectionMask.Rectangle(4, 3, new PointD(1, 0), new PointD(3, 2))); // 2x2
        doc.Actions.RecordSelectionChange(null, "Rectangle Select");
        doc.Actions.FillSelection(ColorBgra.FromBgra(0, 255, 0, 255));

        var received = new List<EventItem<DocumentEventEnum>>();
        using var sub = _sp.GetRequiredService<IDocumentEventsService>().DocumentEvents.Subscribe(new Observer(received.Add));

        History(doc).Undo(); // undoes the fill

        var canvasEvents = received.OfType<CanvasEventItem>().ToList();
        Assert.NotEmpty(canvasEvents);
        Assert.All(canvasEvents, e => Assert.Equal(new RectangleI(1, 0, 2, 2), e.Rect));
    }

    [Fact]
    public void Undo_of_a_structural_step_still_invalidates_the_whole_image()
    {
        var doc = ThreeLayers();
        doc.Actions.AddNewLayer();

        var received = new List<EventItem<DocumentEventEnum>>();
        using var sub = _sp.GetRequiredService<IDocumentEventsService>().DocumentEvents.Subscribe(new Observer(received.Add));

        History(doc).Undo();

        var canvasEvents = received.OfType<CanvasEventItem>().ToList();
        Assert.NotEmpty(canvasEvents);
        Assert.All(canvasEvents, e => Assert.True(e.Rect.IsEmpty)); // empty rect means "the whole image" (see Workspace.Invalidate())
    }

    private sealed class Observer(Action<EventItem<DocumentEventEnum>> onNext) : IObserver<EventItem<DocumentEventEnum>>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(EventItem<DocumentEventEnum> value) => onNext(value);
    }
}
