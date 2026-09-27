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

    /// <summary>Result pixels of the affected area; pixels outside the selection keep their original value.</summary>
    public byte[] Compute(IReadOnlyList<double> values, CancellationToken cancellation = default) =>
        _session.ComputeRegion(_area, buffer => Effect.Render(_context, _area, buffer, values, cancellation));

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
