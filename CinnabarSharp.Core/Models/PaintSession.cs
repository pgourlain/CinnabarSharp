using CinnabarSharp.Core.Extensions;

namespace CinnabarSharp.Core.Models;

/// <summary>
/// One painting operation on a layer (a brush stroke, a shape, a gradient...). Keeps a copy of the layer as it was,
/// recomputes touched rectangles from that copy (so previews can be redrawn freely), writes them into the layer
/// in place, and records a single history step holding only the changed rectangle.
/// Pixels outside the selection are never changed.
/// </summary>
public sealed class PaintSession
{
    private readonly ImageDocument _document;
    private readonly Layer _layer;
    private readonly string _text;
    private readonly byte[] _base;
    private readonly SelectionMask? _selection;
    private readonly IHistoryItem? _startStep;
    private RectangleI _touched = RectangleI.Zero;
    private PixelRegionHistoryItem? _step;

    public PaintSession(ImageDocument document, string text)
    {
        _document = document;
        _layer = document.Layers.CurrentUserLayer;
        _text = text;
        _base = _layer.Surface.ToBgra();
        _selection = document.Selection;
        _startStep = CurrentStep;
    }

    public ImageDocument Document => _document;

    private IHistoryItem? CurrentStep
    {
        get
        {
            var history = _document.Workspace.History;
            return history.Pointer >= 0 ? history.Items[history.Pointer] : null;
        }
    }

    /// <summary>
    /// True while nothing else changed the document since this session started (or since its step was recorded):
    /// its result can still be edited, and <see cref="Commit"/> updates its step instead of adding one.
    /// </summary>
    public bool IsLive => CurrentStep == (_step ?? _startStep);

    public int Width => _document.ImageSize.Width;
    public int Height => _document.ImageSize.Height;

    /// <summary>The layer pixels as they were when the session started.</summary>
    public ReadOnlySpan<byte> Base => _base;

    /// <summary>Same as <see cref="Base"/>, as an array for code that runs on other threads. Never modify it.</summary>
    public byte[] BasePixels => _base;

    /// <summary>Computes a pixel from its value before the session (<paramref name="pixel"/>, BGRA, edited in place).</summary>
    public delegate void PixelFunction(int x, int y, Span<byte> pixel);

    /// <summary>Recomputes <paramref name="region"/> from the original pixels and writes it into the layer.</summary>
    public void Apply(RectangleI region, PixelFunction function)
    {
        if (!region.IsEmpty)
            Write(region, Compute(region, function));
    }

    /// <summary>
    /// Pixels of <paramref name="region"/> computed from the original pixels (unselected pixels unchanged).
    /// Reads only immutable state, so it may run on a background thread.
    /// </summary>
    public byte[] Compute(RectangleI region, PixelFunction function, CancellationToken cancellation = default)
    {
        var buffer = PixelRegion.Extract(_base, Width, region);
        for (var y = 0; y < region.Height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < region.Width; x++)
            {
                var ix = region.X + x;
                var iy = region.Y + y;
                if (_selection is { } s && !s.Contains(ix, iy))
                    continue;
                function(ix, iy, buffer.AsSpan((y * region.Width + x) * 4, 4));
            }
        }
        return buffer;
    }

    /// <summary>
    /// Pixels of <paramref name="region"/> rendered by <paramref name="render"/> into a region-sized buffer, with
    /// pixels outside the selection reset to their original value. Thread-safe like <see cref="Compute"/>.
    /// </summary>
    public byte[] ComputeRegion(RectangleI region, Action<byte[]> render)
    {
        var buffer = new byte[region.Width * region.Height * 4];
        render(buffer);
        if (_selection is { } s)
        {
            for (var y = 0; y < region.Height; y++)
                for (var x = 0; x < region.Width; x++)
                    if (!s.Contains(region.X + x, region.Y + y))
                        _base.AsSpan(((region.Y + y) * Width + region.X + x) * 4, 4).CopyTo(buffer.AsSpan((y * region.Width + x) * 4, 4));
        }
        return buffer;
    }

    /// <summary>Writes computed pixels of <paramref name="region"/> into the layer and redraws it.</summary>
    public void Write(RectangleI region, byte[] pixels)
    {
        if (region.IsEmpty)
            return;
        _layer.Surface.WriteRegion(region, pixels);
        _touched = CoverageMask.Union(_touched, region);
        _document.Workspace.Invalidate(region);
    }

    /// <summary>Paints <paramref name="color"/> where the mask covers <paramref name="region"/>.</summary>
    public void ApplyColor(RectangleI region, CoverageMask coverage, ColorBgra color) =>
        Apply(region, (x, y, pixel) => BlendCoverage(pixel, color, coverage[x, y]));

    /// <summary>Makes pixels transparent where the mask covers <paramref name="region"/>.</summary>
    public void ApplyErase(RectangleI region, CoverageMask coverage) =>
        Apply(region, (x, y, pixel) => pixel[3] = (byte)(pixel[3] * (255 - coverage[x, y]) / 255));

    /// <summary>Restores everything changed so far (used before redrawing a shape preview).</summary>
    public void Reset()
    {
        if (_touched.IsEmpty)
            return;
        _layer.Surface.WriteRegion(_touched, PixelRegion.Extract(_base, Width, _touched));
        _document.Workspace.Invalidate(_touched);
    }

    /// <summary>
    /// Records one history step for everything changed; does nothing if nothing changed. Committing again while
    /// the session <see cref="IsLive"/> updates that step, so an editable result (curve, text) stays one step.
    /// </summary>
    public void Commit()
    {
        if (_touched.IsEmpty)
            return;
        var before = PixelRegion.Extract(_base, Width, _touched);
        var after = _layer.Surface.ReadRegion(_touched);
        if (_step is not null && IsLive)
        {
            _step.Update(_touched, before, after);
            return;
        }
        if (before.AsSpan().SequenceEqual(after))
            return;
        _step = new PixelRegionHistoryItem(_text, _layer, _touched, before, after);
        _document.Workspace.History.PushNewItem(_step);
    }

    public static void BlendCoverage(Span<byte> pixel, ColorBgra color, byte coverage)
    {
        if (coverage == 0)
            return;
        Span<byte> top = [color.B, color.G, color.R, color.A];
        BlendOps.Composite(pixel, top, BlendMode.Normal, coverage / 255.0);
    }
}

/// <summary>Pixels of a rectangle of a layer changed; only that rectangle is stored.</summary>
public sealed class PixelRegionHistoryItem(string text, Layer layer, RectangleI rect, byte[] before, byte[] after)
    : HistoryItem(text)
{
    public RectangleI Rect => rect;

    /// <summary>Replaces the stored change while the step is still being edited (it must be done, not undone).</summary>
    internal void Update(RectangleI newRect, byte[] newBefore, byte[] newAfter) =>
        (rect, before, after) = (newRect, newBefore, newAfter);

    protected override void OnUndo() => layer.Surface.WriteRegion(rect, before);
    protected override void OnRedo() => layer.Surface.WriteRegion(rect, after);
}
