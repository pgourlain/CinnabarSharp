using System.Runtime.CompilerServices;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Photo;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Effects;

/// <summary>
/// The dialog of Photo › Extract Walls: its parameters, and a preview that shows the plan faded with the lines found in
/// red. It is not in <see cref="EffectCatalog"/> (applying it to the layer would only paint that overlay): the dialog's OK
/// button turns the settings into a new layer or a drawing through <see cref="WallExtraction"/> instead.
/// </summary>
public sealed class ExtractWallsEffect : Effect
{
    public override string Name => "Extract Walls";
    public override string Category => EffectCategories.Photo;

    public override IReadOnlyList<EffectParameter> Parameters =>
    [
        new("Keep", 0, 1, 0, Choices: ["Walls (thick lines)", "Thin lines"]),
        new("Lines are", 0, 1, 0, Choices: ["Dark on light", "Light on dark"]),
        new("Threshold (0 = auto)", 0, 255, 0),
        new("Minimum thickness", 1, 100, 5),
        new("Bridge gaps", 0, 50, 0),
        new("Minimum area", 0, 2000, 20),
        new("Output", 0, 1, 0, Choices: ["New layer", "New SVG drawing"]),
        new("Simplify (drawing)", 0, 10, 1.5, 0.1),
        new("Straighten angle (drawing)", 0, 20, 6),
        new("Pixels per meter (drawing, 0 = none)", 0, 10000, 0),
        new("Plan picture (drawing)", 0, 1, 0, Choices: ["Keep as reference", "Leave out"]),
        new("Walls color", 0, 1, 0, Choices: ["Red", "Primary color"]),
    ];

    /// <summary>The color of the extracted walls (layer and drawing): red, so that they show over a dark plan, or the primary color.</summary>
    public static ColorBgra WallColor(IReadOnlyList<double> values, ColorBgra primary) =>
        values[11] >= 0.5 ? primary : ColorBgra.FromBgra(40, 40, 230, 255);

    /// <summary>The settings of a dialog state (the values in the order of <see cref="Parameters"/>).</summary>
    public static WallExtractionSettings Settings(IReadOnlyList<double> values) => new(
        new LineExtractionOptions((LineMode)(int)values[0], (int)values[2], (int)values[3], (int)values[4], (int)values[5], values[1] >= 0.5),
        new TraceOptions(Tolerance: values[7], SnapDegrees: values[8]),
        values[6] >= 0.5 ? WallsOutput.Svg : WallsOutput.Layer,
        values[9],
        values[10] < 0.5);

    /// <summary>The thickness that separates the thin strokes of this picture from the thick ones.</summary>
    public override IReadOnlyList<double>? SuggestValues(EffectContext context)
    {
        var values = Defaults.ToArray();
        values[3] = LineExtraction.SuggestThickness(context.Source, context.Width, context.Height, Settings(values).Lines);
        return values;
    }

    private sealed class Cache
    {
        public readonly object Lock = new();
        public string? Key;
        public byte[]? Mask;
    }

    private static readonly ConditionalWeakTable<EffectContext, Cache> Caches = new();

    /// <summary>The mask for these settings, computed once per context and settings (the preview renders strip by strip).</summary>
    private static byte[] MaskFor(EffectContext context, IReadOnlyList<double> values, CancellationToken cancellation)
    {
        var key = string.Join('|', values.Take(6));
        var cache = Caches.GetOrCreateValue(context);
        lock (cache.Lock)
        {
            if (cache.Key != key || cache.Mask is null)
            {
                cache.Mask = LineExtraction.Extract(context.Source, context.Width, context.Height, Settings(values).Lines,
                    cancellation: cancellation);
                cache.Key = key;
            }
            return cache.Mask;
        }
    }

    public override void Render(EffectContext context, RectangleI region, byte[] destination, IReadOnlyList<double> values,
        CancellationToken cancellation)
    {
        var mask = MaskFor(context, values, cancellation);
        ForEachPixel(region, destination, cancellation, (x, y, px) =>
        {
            var source = context.Index(x, y);
            if (mask[y * context.Width + x] != 0)
            {
                (px[0], px[1], px[2], px[3]) = (40, 40, 230, 255);
                return;
            }
            // The plan, faded towards white so that the red lines stand out.
            for (var c = 0; c < 3; c++)
                px[c] = (byte)((context.Source[source + c] * 45 + 255 * 55 + 50) / 100);
            px[3] = 255;
        });
    }
}
