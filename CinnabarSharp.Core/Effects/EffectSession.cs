using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Effects;

/// <summary>
/// Runs an effect or adjustment on the current layer (inside the selection) with live preview:
/// <see cref="Compute"/> is thread-safe (reads the original pixels only) and can run in the background;
/// <see cref="Show"/> writes a result into the layer on the UI thread; <see cref="Commit"/> records one
/// history step, <see cref="Cancel"/> restores the layer.
/// </summary>
public sealed class EffectSession
{
    private readonly PaintSession _session;
    private readonly EffectContext _context;
    private readonly RectangleI _area;
    private readonly SelectionMask? _selection;

    public EffectSession(ImageDocument document, Effect effect, ColorBgra primary, ColorBgra secondary)
    {
        Effect = effect;
        _session = new PaintSession(document, effect.Name);
        _context = new EffectContext(_session.BasePixels, document.ImageSize.Width, document.ImageSize.Height, primary, secondary);
        _area = document.Selection?.Bounds ?? new RectangleI(0, 0, document.ImageSize.Width, document.ImageSize.Height);
        _selection = document.Selection;
    }

    /// <summary>What the effect reads (the layer before the effect); never modify it.</summary>
    public EffectContext Context => _context;

    /// <summary>The effect's suggested values for this layer (Auto button), or null.</summary>
    public IReadOnlyList<double>? SuggestValues() => Effect.SuggestValues(_context);

    /// <summary>Histogram of the pixels the effect applies to (the selection, or the whole layer), before the effect.</summary>
    public Adjustments.Histogram Histogram() => Adjustments.Histogram.Compute(_session.BasePixels, _selection);

    public EffectSession(ImageDocument document, Effect effect)
        : this(document, effect, ColorBgra.Black, ColorBgra.White)
    {
    }

    public Effect Effect { get; }

    /// <summary>Rows below this, a region is rendered as one block: thread hand-off would cost more than it saves.</summary>
    private const int MinStripRows = 64;

    /// <summary>Result pixels of the affected area; pixels outside the selection keep their original value.</summary>
    public byte[] Compute(IReadOnlyList<double> values, CancellationToken cancellation = default) =>
        _session.ComputeRegion(_area, buffer => RenderInStrips(_area, buffer, values, cancellation));

    /// <summary>
    /// Runs <see cref="Effect.Render"/> over horizontal strips of <paramref name="area"/> in parallel instead of
    /// one call for the whole region (performance-tasks.md P3). This is safe because the effect contract already
    /// requires <c>Render</c> to read only <see cref="EffectContext.Source"/> (the layer as it was, shared and
    /// read-only across strips) and to write every pixel of whatever region it's given — splitting the region
    /// into independent strips and running them concurrently produces the same bytes as one call, only faster.
    /// (An effect that analyzes the whole image, e.g. Auto-Enhance, repeats that analysis once per strip today —
    /// still correct, since it reads <see cref="EffectContext.Source"/> rather than the region it's passed, just
    /// not yet the "compute it once" optimization performance-tasks.md also lists.)
    /// <c>EffectParallelismTests</c> (Core.Tests) pins bit-identical results against a single block for every
    /// effect in <c>EffectCatalog</c>; the existing per-effect checksum/determinism tests are untouched.
    /// </summary>
    private void RenderInStrips(RectangleI area, byte[] destination, IReadOnlyList<double> values, CancellationToken cancellation)
    {
        var stripCount = Math.Clamp(area.Height / MinStripRows, 1, Environment.ProcessorCount);
        if (stripCount <= 1)
        {
            Effect.Render(_context, area, destination, values, cancellation);
            return;
        }

        var rowsPerStrip = (area.Height + stripCount - 1) / stripCount;
        var rowBytes = area.Width * 4;
        Parallel.For(0, stripCount, new ParallelOptions { CancellationToken = cancellation }, i =>
        {
            var y0 = i * rowsPerStrip;
            var rows = Math.Min(rowsPerStrip, area.Height - y0);
            if (rows <= 0)
                return;
            var strip = new RectangleI(area.X, area.Y + y0, area.Width, rows);
            var stripBuffer = new byte[rows * rowBytes];
            Effect.Render(_context, strip, stripBuffer, values, cancellation);
            Array.Copy(stripBuffer, 0, destination, y0 * rowBytes, stripBuffer.Length);
        });
    }

    public void Show(byte[] pixels) => _session.Write(_area, pixels);

    public void Preview(IReadOnlyList<double> values) => Show(Compute(values));

    public void Commit() => _session.Commit();

    public void Cancel() => _session.Reset();

    /// <summary>Applies with the given (or default) values and records it, e.g. for operations without a dialog.</summary>
    public static void ApplyNow(ImageDocument document, Effect effect, IReadOnlyList<double>? values = null,
        ColorBgra? primary = null, ColorBgra? secondary = null)
    {
        var session = new EffectSession(document, effect, primary ?? ColorBgra.Black, secondary ?? ColorBgra.White);
        session.Preview(values ?? effect.Defaults);
        session.Commit();
    }
}
