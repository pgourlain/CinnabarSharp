using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Mcp;

public sealed record LayerInfo(int Index, string Name, bool Visible, double Opacity, string BlendMode, bool Current);

public sealed record RectInfo(int X, int Y, int Width, int Height)
{
    public static RectInfo From(RectangleI r) => new(r.X, r.Y, r.Width, r.Height);
}

public sealed record DocumentInfo(
    string Id,
    string Name,
    string? File,
    int Width,
    int Height,
    bool Active,
    bool HasUnsavedChanges,
    RectInfo? Selection,
    IReadOnlyList<LayerInfo> Layers,
    bool CanUndo,
    bool CanRedo,
    string Kind = "image");

public sealed record HistoryStep(int Index, string Text, bool Current, bool Undone);

public sealed record HistogramInfo(int Bins, IReadOnlyList<long> Red, IReadOnlyList<long> Green, IReadOnlyList<long> Blue,
    IReadOnlyList<long> Luminosity, double MeanLuminosity, double ShadowsClippedPercent, double HighlightsClippedPercent);

public sealed record ImageInfo(DocumentInfo Document, HistogramInfo? Histogram);

public sealed record SavedFile(string Path, string Format, long Bytes);

public sealed record FolderExportResult(string OutputFolder, int Count);

public sealed record SuggestedValues(string Effect, IReadOnlyDictionary<string, double>? Parameters, IReadOnlyList<double>? Values);

public static class Describe
{
    public static DocumentInfo Document(McpContext context, IDocument any)
    {
        var history = any.Workspace.History;
        if (any is not ImageDocument doc)
            return new DocumentInfo(context.IdOf(any), any.DisplayName, any.File?.FullName, any.ImageSize.Width,
                any.ImageSize.Height, context.Workspace.HasOpenDocuments && context.Workspace.ActiveDocument == any,
                any.IsDirty, null, [], history.CanUndo, history.CanRedo, "svg");
        var layers = doc.Layers;
        return new DocumentInfo(
            context.IdOf(doc),
            doc.DisplayName,
            doc.File?.FullName,
            doc.ImageSize.Width,
            doc.ImageSize.Height,
            context.Workspace.HasOpenDocuments && context.Workspace.ActiveDocument == doc,
            doc.IsDirty,
            doc.Selection is { } s ? RectInfo.From(s.Bounds) : null,
            layers.UserLayers.Select((l, i) => new LayerInfo(i, l.Name, !l.Hidden, Math.Round(l.Opacity, 3),
                l.BlendMode.ToString(), i == layers.CurrentUserLayerIndex)).ToList(),
            history.CanUndo,
            history.CanRedo);
    }

    public static IReadOnlyList<HistoryStep> History(ImageDocument doc)
    {
        var history = doc.Workspace.History;
        return history.Items.Select((item, i) => new HistoryStep(i, item.Text, i == history.Pointer, i > history.Pointer)).ToList();
    }

    public static HistogramInfo Histogram(ImageDocument doc, int bins)
    {
        bins = Math.Clamp(bins, 1, 256);
        var h = Core.Adjustments.Histogram.Compute(doc.Layers.GetFlattenedBgra(includeToolLayer: false));
        long[] Bin(long[] values)
        {
            var result = new long[bins];
            for (var i = 0; i < 256; i++)
                result[i * bins / 256] += values[i];
            return result;
        }
        var total = Math.Max(1, h.Luminosity.Sum());
        var mean = h.Luminosity.Select((count, value) => (double)count * value).Sum() / total;
        return new HistogramInfo(bins, Bin(h.Red), Bin(h.Green), Bin(h.Blue), Bin(h.Luminosity), Math.Round(mean, 2),
            Math.Round(100.0 * h.Luminosity[..3].Sum() / total, 2), Math.Round(100.0 * h.Luminosity[253..].Sum() / total, 2));
    }
}
