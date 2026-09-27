using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Models;

/// <summary>
/// Edits made by the user. Each one changes the document through <see cref="ImageDocumentLayers"/> and then
/// records a history step that can undo it. The UI must use these instead of the layer methods directly,
/// otherwise the change can't be undone and doesn't mark the document as modified.
/// </summary>
public class DocumentActions(ImageDocument document)
{
    private ImageDocumentLayers Layers => document.Layers;
    private IImageDocumentHistory History => document.Workspace.History;

    public UserLayer AddNewLayer()
    {
        var layer = Layers.AddNewLayer(string.Empty);
        Layers.SetCurrentUserLayer(layer);
        History.PushNewItem(new AddLayerHistoryItem("Add New Layer", Layers, layer, Layers.IndexOf(layer)));
        return layer;
    }

    public UserLayer DuplicateCurrentLayer()
    {
        var layer = Layers.DuplicateCurrentLayer();
        History.PushNewItem(new AddLayerHistoryItem("Duplicate Layer", Layers, layer, Layers.IndexOf(layer)));
        return layer;
    }

    public UserLayer ImportFromFile(ImageFile file)
    {
        var layer = Layers.ImportFromFile(file);
        History.PushNewItem(new AddLayerHistoryItem("Import From File", Layers, layer, Layers.IndexOf(layer)));
        return layer;
    }

    public void DeleteCurrentLayer()
    {
        var index = Layers.CurrentUserLayerIndex;
        var layer = Layers.CurrentUserLayer;
        Layers.DeleteLayer(index);
        History.PushNewItem(new DeleteLayerHistoryItem("Delete Layer", Layers, layer, index));
    }

    public void MoveCurrentLayerUp()
    {
        var from = Layers.CurrentUserLayerIndex;
        var layer = Layers.CurrentUserLayer;
        Layers.MoveCurrentLayerUp();
        History.PushNewItem(new MoveLayerHistoryItem("Move Layer Up", Layers, layer, from, from + 1));
    }

    public void MoveCurrentLayerDown()
    {
        var from = Layers.CurrentUserLayerIndex;
        var layer = Layers.CurrentUserLayer;
        Layers.MoveCurrentLayerDown();
        History.PushNewItem(new MoveLayerHistoryItem("Move Layer Down", Layers, layer, from, from - 1));
    }

    public void FlipCurrentLayerHorizontal()
    {
        var layer = Layers.CurrentUserLayer;
        Layers.FlipCurrentLayerHorizontal();
        History.PushNewItem(new FlipLayerHistoryItem("Flip Layer Horizontal", layer, horizontal: true));
    }

    public void FlipCurrentLayerVertical()
    {
        var layer = Layers.CurrentUserLayer;
        Layers.FlipCurrentLayerVertical();
        History.PushNewItem(new FlipLayerHistoryItem("Flip Layer Vertical", layer, horizontal: false));
    }

    public void MergeCurrentLayerDown()
    {
        var index = Layers.CurrentUserLayerIndex;
        var source = Layers.CurrentUserLayer;
        var dest = Layers[index - 1];
        var before = dest.Surface;

        Layers.MergeCurrentLayerDown();

        var steps = new List<IHistoryItem>();
        if (dest.Surface != before)
            steps.Add(new SwapSurfaceHistoryItem("", dest, before, dest.Surface));
        steps.Add(new DeleteLayerHistoryItem("", Layers, source, index));
        History.PushNewItem(new CompoundHistoryItem("Merge Layer Down", steps));
    }

    public void Flatten()
    {
        var bottom = Layers[0];
        var removed = Layers.UserLayers.Select((layer, index) => (layer, index)).Skip(1).Reverse().ToList();
        var beforeSurface = bottom.Surface;
        var beforeProperties = LayerProperties.From(bottom);

        Layers.FlattenLayers();

        // Same order as FlattenLayers: replace the bottom layer, reset its properties, delete from the top down.
        var steps = new List<IHistoryItem>
        {
            new SwapSurfaceHistoryItem("", bottom, beforeSurface, bottom.Surface),
            new UpdateLayerPropertiesHistoryItem("", bottom, beforeProperties, LayerProperties.From(bottom)),
        };
        steps.AddRange(removed.Select(r => new DeleteLayerHistoryItem("", Layers, r.layer, r.index)));
        History.PushNewItem(new CompoundHistoryItem("Flatten", steps));
    }

    public void SetLayerVisibility(UserLayer layer, bool visible)
    {
        if (layer.Hidden == !visible)
            return;
        var before = LayerProperties.From(layer);
        layer.Hidden = !visible;
        History.PushNewItem(new UpdateLayerPropertiesHistoryItem(
            visible ? "Show Layer" : "Hide Layer", layer, before, LayerProperties.From(layer)));
    }

    // ---- Selection ----

    private int Width => document.ImageSize.Width;
    private int Height => document.ImageSize.Height;

    /// <summary>Records a selection change already applied by a tool.</summary>
    public void RecordSelectionChange(SelectionMask? before, string text)
    {
        if (!ReferenceEquals(before, document.Selection))
            History.PushNewItem(new SelectionHistoryItem(text, document, before, document.Selection));
    }

    public void SelectAll() => ChangeSelection(SelectionMask.All(Width, Height), "Select All");

    public void DeselectAll() => ChangeSelection(null, "Deselect All");

    public void InvertSelection() =>
        ChangeSelection((document.Selection ?? SelectionMask.Empty(Width, Height)).Invert(), "Invert Selection");

    private void ChangeSelection(SelectionMask? selection, string text)
    {
        var before = document.Selection;
        document.SetSelection(selection);
        RecordSelectionChange(before, text);
    }

    // ---- Pixels inside the selection (the whole layer when nothing is selected) ----

    private SelectionMask EffectiveSelection => document.Selection ?? SelectionMask.All(Width, Height);

    public void EraseSelection(string text = "Erase Selection") =>
        EditCurrentLayerPixels(text, (bgra, mask) =>
        {
            for (var i = 0; i < mask.Length; i++)
                if (mask[i] != 0)
                    bgra.Slice(i * 4, 4).Clear();
        });

    public void FillSelection(ColorBgra color) =>
        EditCurrentLayerPixels("Fill Selection", (bgra, mask) =>
        {
            for (var i = 0; i < mask.Length; i++)
                if (mask[i] != 0)
                    (bgra[i * 4], bgra[i * 4 + 1], bgra[i * 4 + 2], bgra[i * 4 + 3]) = (color.B, color.G, color.R, color.A);
        });

    private delegate void PixelEdit(Span<byte> bgra, ReadOnlySpan<byte> mask);

    private void EditCurrentLayerPixels(string text, PixelEdit edit)
    {
        var layer = Layers.CurrentUserLayer;
        var before = layer.Surface;
        var bgra = before.ToBgra();
        edit(bgra, EffectiveSelection.Data);
        layer.Surface = Utility.FromBgra(bgra, Width, Height);
        document.Workspace.Invalidate();
        History.PushNewItem(new SwapSurfaceHistoryItem(text, layer, before, layer.Surface));
    }

    /// <summary>Selected pixels of the current layer (or of the whole image when <paramref name="merged"/>), cropped to the selection.</summary>
    public ClipboardImage Copy(bool merged = false)
    {
        var bgra = merged ? Layers.GetFlattenedBgra(includeToolLayer: false) : Layers.CurrentUserLayer.Surface.ToBgra();
        var selection = EffectiveSelection;
        PixelRegion.ClearOutside(bgra, selection);
        var bounds = selection.Bounds;
        return new ClipboardImage(PixelRegion.Extract(bgra, Width, bounds), bounds.Width, bounds.Height);
    }

    public ClipboardImage Cut()
    {
        var image = Copy();
        EraseSelection("Cut");
        return image;
    }

    /// <summary>Pastes onto the current layer at (x, y) and selects the pasted area.</summary>
    public void Paste(ClipboardImage image, PointI at)
    {
        var layer = Layers.CurrentUserLayer;
        var before = layer.Surface;
        var bgra = before.ToBgra();
        var area = PixelRegion.Place(bgra, Width, Height, image.Bgra, image.Width, image.Height, at.X, at.Y, composite: true);
        if (area.IsEmpty)
            return;
        layer.Surface = Utility.FromBgra(bgra, Width, Height);
        var selectionBefore = document.Selection;
        document.SetSelection(RectangleSelection(area));
        document.Workspace.Invalidate();
        History.PushNewItem(new CompoundHistoryItem("Paste",
        [
            new SwapSurfaceHistoryItem("", layer, before, layer.Surface),
            new SelectionHistoryItem("", document, selectionBefore, document.Selection),
        ]));
    }

    /// <summary>Pastes into a new layer above the current one at (x, y) and selects the pasted area.</summary>
    public UserLayer PasteIntoNewLayer(ClipboardImage image, PointI at)
    {
        var bgra = new byte[Width * Height * 4];
        var area = PixelRegion.Place(bgra, Width, Height, image.Bgra, image.Width, image.Height, at.X, at.Y, composite: false);
        var layer = Layers.CreateLayer();
        layer.Surface.Dispose();
        layer.Surface = Utility.FromBgra(bgra, Width, Height);
        var index = Layers.CurrentUserLayerIndex + 1;
        Layers.Insert(layer, index);
        Layers.SetCurrentUserLayer(layer);

        var selectionBefore = document.Selection;
        document.SetSelection(area.IsEmpty ? null : RectangleSelection(area));
        document.Workspace.Invalidate();
        History.PushNewItem(new CompoundHistoryItem("Paste Into New Layer",
        [
            new AddLayerHistoryItem("", Layers, layer, index),
            new SelectionHistoryItem("", document, selectionBefore, document.Selection),
        ]));
        return layer;
    }

    /// <summary>Crops the image to the selection's bounds; pixels outside a non-rectangular selection become transparent.</summary>
    public void CropToSelection()
    {
        if (document.Selection is not { } selection)
            return;
        var bounds = selection.Bounds;
        var mask = selection.Crop(bounds);
        var sizeBefore = document.ImageSize;

        var surfaces = new List<(Layer, IImageBuf, IImageBuf)>();
        foreach (var layer in Layers.UserLayers)
        {
            var region = PixelRegion.Extract(layer.Surface.ToBgra(), Width, bounds);
            PixelRegion.ClearOutside(region, mask);
            surfaces.Add((layer, layer.Surface, Utility.FromBgra(region, bounds.Width, bounds.Height)));
        }

        document.SetSelection(null);
        foreach (var (layer, _, after) in surfaces)
            layer.Surface = after;
        document.Resize(new ImageSize(bounds.Width, bounds.Height));
        History.PushNewItem(new ResizeImageHistoryItem("Crop to Selection", document, sizeBefore,
            document.ImageSize, surfaces, selection, null));
    }

    private SelectionMask RectangleSelection(RectangleI area) =>
        SelectionMask.Rectangle(Width, Height, new PointD(area.X, area.Y), new PointD(area.X + area.Width, area.Y + area.Height));

    /// <summary>Records property changes already applied to the layer (e.g. by a live-preview dialog).</summary>
    public void CommitLayerProperties(UserLayer layer, LayerProperties before)
    {
        var after = LayerProperties.From(layer);
        if (after != before)
            History.PushNewItem(new UpdateLayerPropertiesHistoryItem("Layer Properties", layer, before, after));
    }
}
