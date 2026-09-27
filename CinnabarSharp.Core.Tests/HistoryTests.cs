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
        { "Resize Image", a => a.ResizeImage(new ImageSize(8, 6), ResamplingMode.NearestNeighbor) },
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

    private sealed class Observer(Action<EventItem<DocumentEventEnum>> onNext) : IObserver<EventItem<DocumentEventEnum>>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(EventItem<DocumentEventEnum> value) => onNext(value);
    }
}
