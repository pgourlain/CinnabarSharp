using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Adjustments;

/// <summary>
/// Runs an adjustment on the current layer (inside the selection) with live preview: <see cref="Compute"/> is
/// thread-safe (reads the original pixels only) and can run in the background; <see cref="Show"/> writes a result
/// into the layer on the UI thread; <see cref="Commit"/> records one history step, <see cref="Cancel"/> restores.
/// </summary>
public sealed class AdjustmentSession
{
    private readonly ColorAdjustment _adjustment;
    private readonly PaintSession _session;
    private readonly RectangleI _area;

    public AdjustmentSession(ImageDocument document, ColorAdjustment adjustment)
    {
        _adjustment = adjustment;
        _session = new PaintSession(document, adjustment.Name);
        _area = document.Selection?.Bounds ?? new RectangleI(0, 0, document.ImageSize.Width, document.ImageSize.Height);
    }

    public ColorAdjustment Adjustment => _adjustment;

    /// <summary>Adjusted pixels of the affected area, computed from the original pixels.</summary>
    public byte[] Compute(IReadOnlyList<double> values, CancellationToken cancellation = default)
    {
        var function = _adjustment.Create(values, _session.Base);
        return _session.Compute(_area, (_, _, pixel) => function(pixel), cancellation);
    }

    public void Show(byte[] pixels) => _session.Write(_area, pixels);

    public void Preview(IReadOnlyList<double> values) => Show(Compute(values));

    public void Commit() => _session.Commit();

    public void Cancel() => _session.Reset();

    /// <summary>Applies with the given (or default) values and records it, for adjustments without a dialog.</summary>
    public static void ApplyNow(ImageDocument document, ColorAdjustment adjustment, IReadOnlyList<double>? values = null)
    {
        var session = new AdjustmentSession(document, adjustment);
        session.Preview(values ?? adjustment.Defaults);
        session.Commit();
    }
}
