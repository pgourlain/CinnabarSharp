using BenchmarkDotNet.Attributes;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Benchmarks;

/// <summary>
/// Undo, then redo, of one step of each concrete <see cref="IHistoryItem"/> type, on a 24 MP, 3-layer document —
/// performance-tasks.md's own target: "Undo/redo of any step: under 200 ms". Each benchmark method is a single
/// Undo()+Redo() pair, which leaves the document exactly as <see cref="IterationSetupAttribute"/> found it, so
/// it's safe to call repeatedly across iterations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 1, iterationCount: 5)]
public class HistoryUndoRedoBenchmarks
{
    private ImageDocument _doc = null!;

    private ImageDocument CreateDocument()
    {
        var (width, height) = BenchmarkSizes.Large;
        var sp = BenchmarkHelpers.BuildServices();
        return BenchmarkHelpers.CreateDocument(sp, width, height, layerCount: 3);
    }

    private void UndoRedo()
    {
        _doc.Workspace.History.Undo();
        _doc.Workspace.History.Redo();
    }

    // ---- AddLayerHistoryItem ----
    [GlobalSetup(Target = nameof(AddLayer))]
    public void SetupAddLayer()
    {
        _doc = CreateDocument();
        _doc.Actions.AddNewLayer();
    }

    [Benchmark]
    public void AddLayer() => UndoRedo();

    // ---- DeleteLayerHistoryItem ----
    [GlobalSetup(Target = nameof(DeleteLayer))]
    public void SetupDeleteLayer()
    {
        _doc = CreateDocument();
        _doc.Actions.DeleteCurrentLayer();
    }

    [Benchmark]
    public void DeleteLayer() => UndoRedo();

    // ---- MoveLayerHistoryItem ----
    [GlobalSetup(Target = nameof(MoveLayer))]
    public void SetupMoveLayer()
    {
        _doc = CreateDocument();
        _doc.Layers.SetCurrentUserLayer(0);
        _doc.Actions.MoveCurrentLayerUp();
    }

    [Benchmark]
    public void MoveLayer() => UndoRedo();

    // ---- FlipLayerHistoryItem ----
    [GlobalSetup(Target = nameof(FlipLayer))]
    public void SetupFlipLayer()
    {
        _doc = CreateDocument();
        _doc.Actions.FlipCurrentLayerHorizontal();
    }

    [Benchmark]
    public void FlipLayer() => UndoRedo();

    // ---- UpdateLayerPropertiesHistoryItem ----
    [GlobalSetup(Target = nameof(LayerVisibility))]
    public void SetupLayerVisibility()
    {
        _doc = CreateDocument();
        _doc.Actions.SetLayerVisibility(_doc.Layers.CurrentUserLayer, visible: false);
    }

    [Benchmark]
    public void LayerVisibility() => UndoRedo();

    // ---- SelectionHistoryItem ----
    [GlobalSetup(Target = nameof(Selection))]
    public void SetupSelection()
    {
        _doc = CreateDocument();
        _doc.Actions.SelectAll();
    }

    [Benchmark]
    public void Selection() => UndoRedo();

    // ---- PixelRegionHistoryItem, whole layer (Erase Selection with nothing selected) ----
    [GlobalSetup(Target = nameof(PixelEditWholeLayer))]
    public void SetupPixelEditWholeLayer()
    {
        _doc = CreateDocument();
        _doc.Actions.EraseSelection();
    }

    [Benchmark]
    public void PixelEditWholeLayer() => UndoRedo();

    // ---- PixelRegionHistoryItem, a small rectangle (the common case: a brush dab, a small fill) ----
    [GlobalSetup(Target = nameof(PixelEditSmallRegion))]
    public void SetupPixelEditSmallRegion()
    {
        _doc = CreateDocument();
        var (width, height) = BenchmarkSizes.Large;
        _doc.SetSelection(SelectionMask.Rectangle(width, height,
            new PointD(width / 2.0, height / 2.0), new PointD(width / 2.0 + 200, height / 2.0 + 200)));
        _doc.Actions.FillSelection(ColorBgra.FromBgra(0, 255, 0, 255));
    }

    [Benchmark]
    public void PixelEditSmallRegion() => UndoRedo();

    // ---- ResizeImageHistoryItem ----
    [GlobalSetup(Target = nameof(Crop))]
    public void SetupCrop()
    {
        _doc = CreateDocument();
        var (width, height) = BenchmarkSizes.Large;
        _doc.Actions.CropToRectangle(new RectangleI(0, 0, width / 2, height / 2));
    }

    [Benchmark]
    public void Crop() => UndoRedo();

    // ---- CompoundHistoryItem (wraps SwapSurfaceHistoryItem + DeleteLayerHistoryItem) ----
    [GlobalSetup(Target = nameof(MergeDown))]
    public void SetupMergeDown()
    {
        _doc = CreateDocument();
        _doc.Layers.SetCurrentUserLayer(1);
        _doc.Actions.MergeCurrentLayerDown();
    }

    [Benchmark]
    public void MergeDown() => UndoRedo();
}
