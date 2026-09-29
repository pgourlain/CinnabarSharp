namespace CinnabarSharp.Core.Models;

public record LayerProperties(string Name, bool Hidden, double Opacity, BlendMode BlendMode)
{
    public static LayerProperties From(Layer layer) => new(layer.Name, layer.Hidden, layer.Opacity, layer.BlendMode);

    public void ApplyTo(Layer layer)
    {
        layer.Name = Name;
        layer.Hidden = Hidden;
        layer.Opacity = Opacity;
        layer.BlendMode = BlendMode;
    }
}

/// <summary>A layer was inserted at <paramref name="index"/>.</summary>
public sealed class AddLayerHistoryItem(string text, ImageDocumentLayers layers, UserLayer layer, int index)
    : HistoryItem(text)
{
    public override long Bytes => layer.Surface.Width * (long)layer.Surface.Height * 4;

    protected override void OnUndo() => layers.DeleteLayer(index);

    protected override void OnRedo()
    {
        layers.Insert(layer, index);
        layers.SetCurrentUserLayer(layer);
    }

    protected override void OnDispose()
    {
        if (IsUndone)
            layer.Surface.Dispose();
    }
}

/// <summary>The layer at <paramref name="index"/> was removed.</summary>
public sealed class DeleteLayerHistoryItem(string text, ImageDocumentLayers layers, UserLayer layer, int index)
    : HistoryItem(text)
{
    public override long Bytes => layer.Surface.Width * (long)layer.Surface.Height * 4;

    protected override void OnUndo()
    {
        layers.Insert(layer, index);
        layers.SetCurrentUserLayer(layer);
    }

    protected override void OnRedo() => layers.DeleteLayer(index);

    protected override void OnDispose()
    {
        if (!IsUndone)
            layer.Surface.Dispose();
    }
}

/// <summary>The layer moved from index <paramref name="from"/> to the adjacent index <paramref name="to"/>.</summary>
public sealed class MoveLayerHistoryItem(string text, ImageDocumentLayers layers, UserLayer layer, int from, int to)
    : HistoryItem(text)
{
    protected override void OnUndo() => Move(to, from);
    protected override void OnRedo() => Move(from, to);

    private void Move(int current, int target)
    {
        layers.SetCurrentUserLayer(current);
        if (target > current)
            layers.MoveCurrentLayerUp();
        else
            layers.MoveCurrentLayerDown();
    }
}

/// <summary>The layer's pixels were replaced: <paramref name="before"/> → <paramref name="after"/>.</summary>
public sealed class SwapSurfaceHistoryItem(string text, Layer layer, IImageBuf before, IImageBuf after)
    : HistoryItem(text)
{
    public override long Bytes =>
        before.Width * (long)before.Height * 4 + after.Width * (long)after.Height * 4;

    protected override void OnUndo() => layer.Surface = before;
    protected override void OnRedo() => layer.Surface = after;

    protected override void OnDispose() => (IsUndone ? after : before).Dispose();
}

public sealed class FlipLayerHistoryItem(string text, Layer layer, bool horizontal) : HistoryItem(text)
{
    protected override void OnUndo() => Flip();
    protected override void OnRedo() => Flip();

    private void Flip()
    {
        if (horizontal)
            layer.FlipHorizontal();
        else
            layer.FlipVertical();
    }
}

public sealed class UpdateLayerPropertiesHistoryItem(string text, Layer layer, LayerProperties before, LayerProperties after)
    : HistoryItem(text)
{
    protected override void OnUndo() => before.ApplyTo(layer);
    protected override void OnRedo() => after.ApplyTo(layer);
}
