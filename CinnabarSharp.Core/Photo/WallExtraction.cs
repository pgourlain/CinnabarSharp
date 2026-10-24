using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Core.Vector;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Photo;

/// <summary>Where the extracted lines go.</summary>
public enum WallsOutput
{
    /// <summary>A new layer above the current one, with the lines as opaque pixels.</summary>
    Layer,

    /// <summary>A new SVG drawing with the lines traced as filled outlines.</summary>
    Svg,
}

/// <summary>
/// Everything the Extract Walls dialog and the MCP tool choose. <paramref name="PixelsPerMeter"/> above 0 gives the
/// drawing real units (millimetres) so that it measures true; <paramref name="IncludePlan"/> keeps the source picture as
/// a locked reference layer of the drawing.
/// </summary>
public sealed record WallExtractionSettings(LineExtractionOptions Lines, TraceOptions Trace, WallsOutput Output = WallsOutput.Layer,
    double PixelsPerMeter = 0, bool IncludePlan = true)
{
    public static WallExtractionSettings Default { get; } = new(new LineExtractionOptions(), new TraceOptions());
}

/// <summary>The result of the computation, before it is applied to the document.</summary>
public sealed class WallExtractionData(byte[] mask, byte[]? plan, IReadOnlyList<VectorPath>? outlines)
{
    /// <summary>0/255, image-sized.</summary>
    public byte[] Mask { get; } = mask;

    /// <summary>The picture the lines come from (flattened image), when the output is a drawing that keeps it.</summary>
    public byte[]? Plan { get; } = plan;

    /// <summary>The traced outlines in drawing units; null for a layer output.</summary>
    public IReadOnlyList<VectorPath>? Outlines { get; } = outlines;
}

/// <summary>What <see cref="WallExtraction.Apply"/> made.</summary>
public sealed record WallExtractionResult(UserLayer? Layer, SvgDocument? Drawing, int OutlineCount);

/// <summary>
/// Extract the lines of a picture (the walls of a floor plan) as a new layer or as a new SVG drawing. <see cref="Compute"/>
/// reads the document and can run in the background; <see cref="Apply"/> changes it and must run where the document is edited.
/// </summary>
public static class WallExtraction
{
    public const string LayerName = "Walls";

    /// <summary>
    /// The mask of the lines of the current layer inside the selection, and for a drawing output their outlines.
    /// With <paramref name="fromLayerAlpha"/> the current layer is taken as the mask itself (opaque pixels are lines, e.g. a
    /// Walls layer that was fixed by hand) instead of being analyzed.
    /// </summary>
    public static WallExtractionData Compute(ImageDocument document, WallExtractionSettings settings, bool fromLayerAlpha = false,
        CancellationToken cancellation = default)
    {
        var (width, height) = (document.ImageSize.Width, document.ImageSize.Height);
        var pixels = document.Layers.CurrentUserLayer.Surface.ToBgra();
        byte[] mask;
        if (fromLayerAlpha)
        {
            mask = new byte[width * height];
            for (var i = 0; i < mask.Length; i++)
                mask[i] = (byte)(pixels[i * 4 + 3] >= 128 ? 255 : 0);
            if (document.Selection is { } selection)
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                        if (selection[x, y] == 0)
                            mask[y * width + x] = 0;
        }
        else
            mask = LineExtraction.Extract(pixels, width, height, settings.Lines, document.Selection, cancellation);

        if (settings.Output == WallsOutput.Layer)
            return new WallExtractionData(mask, null, null);

        var scale = MillimetresPerPixel(settings);
        var outlines = MaskTracer.Trace(mask, width, height, settings.Trace with { Scale = scale }, cancellation);
        var plan = settings.IncludePlan ? document.Layers.GetFlattenedBgra(includeToolLayer: false) : null;
        return new WallExtractionData(mask, plan, outlines);
    }

    /// <summary>Drawing units per pixel: 1 for a pixel drawing, millimetres when a scale is given.</summary>
    public static double MillimetresPerPixel(WallExtractionSettings settings) =>
        settings.PixelsPerMeter > 0 ? 1000.0 / settings.PixelsPerMeter : 1;

    /// <summary>The mask as layer pixels: <paramref name="color"/> where there are lines, transparent elsewhere.</summary>
    public static byte[] LayerPixels(byte[] mask, ColorBgra color)
    {
        var bgra = new byte[mask.Length * 4];
        for (var i = 0; i < mask.Length; i++)
            if (mask[i] != 0)
                (bgra[i * 4], bgra[i * 4 + 1], bgra[i * 4 + 2], bgra[i * 4 + 3]) = (color.B, color.G, color.R, 255);
        return bgra;
    }

    /// <summary>
    /// Adds the layer (one history step) or opens the drawing as a new tab. An empty result still creates what was asked for,
    /// so the user sees that nothing was found and can change the settings.
    /// </summary>
    public static WallExtractionResult Apply(ImageDocument document, IWorkspaceService workspace, WallExtractionData data,
        WallExtractionSettings settings, ColorBgra color)
    {
        if (data.Outlines is null)
        {
            var layer = document.Actions.AddLayerFromPixels(LayerName, LayerPixels(data.Mask, color), "Extract Walls");
            return new WallExtractionResult(layer, null, 0);
        }
        var (width, height) = (document.ImageSize.Width, document.ImageSize.Height);
        var root = WallsDrawing.Build(width, height, data.Outlines, color, MillimetresPerPixel(settings), settings.PixelsPerMeter > 0, data.Plan);
        var drawing = workspace.OpenSvgDocument(root, null, null);
        return new WallExtractionResult(null, drawing, data.Outlines.Count);
    }
}
