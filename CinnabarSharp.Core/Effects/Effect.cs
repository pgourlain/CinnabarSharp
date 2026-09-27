using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Effects;

/// <summary>A numeric parameter shown as a slider in the effect/adjustment dialog.</summary>
public sealed record EffectParameter(string Name, double Minimum, double Maximum, double Default, double Step = 1);

/// <summary>What an effect can read: the layer's original pixels (straight-alpha BGRA) and the palette colors.</summary>
public sealed class EffectContext(byte[] source, int width, int height, ColorBgra primary, ColorBgra secondary)
{
    public byte[] Source { get; } = source;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public ColorBgra Primary { get; } = primary;
    public ColorBgra Secondary { get; } = secondary;

    public int Index(int x, int y) => (Math.Clamp(y, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)) * 4;
}

/// <summary>
/// An image operation from the Adjustments or Effects menu. <see cref="Render"/> writes every pixel of
/// <paramref name="region"/> into <paramref name="destination"/> (region-sized BGRA), reading only
/// <see cref="EffectContext.Source"/>, so it can run on a background thread. Pure C#: identical on every OS.
/// </summary>
public abstract class Effect
{
    public abstract string Name { get; }

    /// <summary>Submenu in the Effects menu (Blurs, Distort...); empty for adjustments.</summary>
    public virtual string Category => "";

    /// <summary>Empty for operations applied immediately, without a dialog.</summary>
    public virtual IReadOnlyList<EffectParameter> Parameters => [];

    public IReadOnlyList<double> Defaults => Parameters.Select(p => p.Default).ToList();

    public abstract void Render(EffectContext context, RectangleI region, byte[] destination,
        IReadOnlyList<double> values, CancellationToken cancellation);

    /// <summary>Calls <paramref name="pixel"/> for every pixel of the region with its destination slice.</summary>
    protected static void ForEachPixel(RectangleI region, byte[] destination, CancellationToken cancellation,
        Action<int, int, Span<byte>> pixel)
    {
        for (var y = 0; y < region.Height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < region.Width; x++)
                pixel(region.X + x, region.Y + y, destination.AsSpan((y * region.Width + x) * 4, 4));
        }
    }
}
