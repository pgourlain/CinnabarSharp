using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Vector;

namespace CinnabarSharp.Core.Services;

public interface IFormatManager
{
    IReadOnlyList<ImageFormat> Formats { get; }

    /// <summary>Raster formats that can be written (for Save As of an image).</summary>
    IReadOnlyList<ImageFormat> SaveFormats { get; }

    /// <summary>
    /// Formats a document of this kind can be saved to (Save As lists only these). An SVG drawing lists SVG first, then the
    /// raster formats: those are exports, the drawing stays an SVG document.
    /// </summary>
    IReadOnlyList<ImageFormat> GetSaveFormats(DocumentKind kind);

    /// <summary>Extension with or without the leading dot, any case.</summary>
    ImageFormat? GetFormatByExtension(string extension);

    /// <summary>Finds the format from the file extension, or from the file content when the extension is missing or unknown.</summary>
    ImageFormat? GetFormatForFile(ImageFile file);

    /// <summary>Opens the file as a new active document, or activates it if it is already open.</summary>
    IDocument Open(ImageFile file);

    /// <summary>
    /// Same as <see cref="Open"/>, but reads and decodes the file on a background thread (a 12 MP HEIC takes
    /// seconds) and creates the document where it is awaited, so the events it raises come from the caller's thread.
    /// For a UI caller, wrap it in something that disables editing meanwhile (<c>MainViewModel.RunBusyAsync</c>).
    /// </summary>
    Task<IDocument> OpenAsync(ImageFile file, CancellationToken cancellation = default);

    /// <summary>Opens an SVG file as a raster image (File › Open as Image), so it can be painted on. The image has no file: Save asks for one.</summary>
    IDocument OpenAsImage(ImageFile file);

    /// <summary>Writes an SVG drawing as a picture (PNG, JPEG, WebP, BMP, TIFF, ORA). The drawing keeps its file and its unsaved-changes state.</summary>
    void Export(SvgDocument document, ImageFile file, SvgExportOptions options, ImageFormat? format = null);

    Task ExportAsync(SvgDocument document, ImageFile file, SvgExportOptions options, ImageFormat? format = null, CancellationToken cancellation = default);

    /// <summary>The image's pixel size without decoding it, via the matching format's <see cref="ImageFormat.PeekSize"/>;
    /// null if the format isn't recognized or can't tell without a full <see cref="Open"/>.</summary>
    ImageSize? PeekSize(ImageFile file);

    /// <summary>Writes the document to the file and makes it the document's file. The format defaults to the file extension's.</summary>
    void Save(IDocument document, ImageFile file, ImageFormat? format = null);

    /// <summary>
    /// Same as <see cref="Save"/>, but encodes on a background thread instead of blocking the caller — for a UI
    /// caller, wrap it in something that disables editing for the duration (e.g. <c>MainViewModel.RunBusyAsync</c>)
    /// so nothing mutates the document while it's being read on that other thread; everything other than the
    /// encode itself (marking the document clean, etc.) still runs on whatever thread awaits this, which for a
    /// caller with a UI SynchronizationContext is back on the UI thread, same as any other <c>await</c>.
    /// </summary>
    Task SaveAsync(IDocument document, ImageFile file, ImageFormat? format = null, CancellationToken cancellation = default);
}

public class FormatManager : IFormatManager
{
    private readonly IWorkspaceService _workspace;

    private readonly SvgRasterFormat? _svgRaster;

    public FormatManager(IEnumerable<IImageImporter> importers, IWorkspaceService workspace, SvgRasterFormat? svgRaster = null)
    {
        Formats = importers.OfType<ImageFormat>().ToList();
        _workspace = workspace;
        _svgRaster = svgRaster;
    }

    public IReadOnlyList<ImageFormat> Formats { get; }

    public IReadOnlyList<ImageFormat> SaveFormats =>
        Formats.Where(f => f.SupportsSaving && f.DocumentKind == DocumentKind.Image).ToList();

    public IReadOnlyList<ImageFormat> GetSaveFormats(DocumentKind kind) => kind switch
    {
        DocumentKind.Image => SaveFormats,
        DocumentKind.Svg => [.. Formats.Where(f => f.DocumentKind == DocumentKind.Svg), .. SaveFormats],
        _ => [],
    };

    public ImageFormat? GetFormatByExtension(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return Formats.FirstOrDefault(f => f.SupportedExtensions.Contains(ext));
    }

    public ImageFormat? GetFormatForFile(ImageFile file) =>
        GetFormatByExtension(file.Extension) ?? Formats.FirstOrDefault(f => f.MatchesContent(file));

    public IDocument Open(ImageFile file)
    {
        var existing = _workspace.OpenDocuments.FirstOrDefault(d =>
            d.File is not null && PathsEqual(d.File.FullName, file.FullName));
        if (existing is not null)
        {
            _workspace.SetActiveDocument(existing);
            return existing;
        }

        var format = GetFormatForFile(file)
            ?? throw new NotSupportedException($"'{file.Name}' is not a supported image file.");
        format.Import(file);
        return _workspace.ActiveDocument;
    }

    public async Task<IDocument> OpenAsync(ImageFile file, CancellationToken cancellation = default)
    {
        var existing = _workspace.OpenDocuments.FirstOrDefault(d =>
            d.File is not null && PathsEqual(d.File.FullName, file.FullName));
        if (existing is not null)
        {
            _workspace.SetActiveDocument(existing);
            return existing;
        }

        var format = GetFormatForFile(file)
            ?? throw new NotSupportedException($"'{file.Name}' is not a supported image file.");
        var decoded = await Task.Run(() => format.Decode(file), cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            format.Import(file, decoded);
        }
        catch
        {
            (decoded as IDisposable)?.Dispose();
            throw;
        }
        return _workspace.ActiveDocument;
    }

    public ImageSize? PeekSize(ImageFile file) => GetFormatForFile(file)?.PeekSize(file);

    public IDocument OpenAsImage(ImageFile file)
    {
        var raster = _svgRaster ?? throw new NotSupportedException("Opening an SVG as an image is not available.");
        raster.Import(file);
        var image = _workspace.ActiveImageDocument!;
        // Not the SVG's own file: saving would try to write a raster back as SVG.
        image.File = null;
        image.FileType = null;
        image.DisplayName = file.Name;
        return image;
    }

    public void Save(IDocument document, ImageFile file, ImageFormat? format = null)
    {
        format = ResolveSaveFormat(document.Kind, file, format);
        if (document is SvgDocument svg && format.DocumentKind != DocumentKind.Svg)
        {
            Export(svg, file, SvgExportOptions.Default, format);
            return;
        }
        format.ExportDocument(document, file);
        FinishSave(document, file, format);
    }

    public async Task SaveAsync(IDocument document, ImageFile file, ImageFormat? format = null, CancellationToken cancellation = default)
    {
        var resolved = ResolveSaveFormat(document.Kind, file, format);
        if (document is SvgDocument svg && resolved.DocumentKind != DocumentKind.Svg)
        {
            await ExportAsync(svg, file, SvgExportOptions.Default, resolved, cancellation);
            return;
        }
        await Task.Run(() => resolved.ExportDocument(document, file), cancellation);
        FinishSave(document, file, resolved);
    }

    public void Export(SvgDocument document, ImageFile file, SvgExportOptions options, ImageFormat? format = null)
    {
        var resolved = ResolveExportFormat(file, format);
        var (pixels, width, height) = options.Render(document);
        resolved.ExportPixels(pixels, width, height, file);
        file.Refresh();
    }

    public async Task ExportAsync(SvgDocument document, ImageFile file, SvgExportOptions options, ImageFormat? format = null,
        CancellationToken cancellation = default)
    {
        var resolved = ResolveExportFormat(file, format);
        await Task.Run(() =>
        {
            var (pixels, width, height) = options.Render(document, cancellation);
            resolved.ExportPixels(pixels, width, height, file);
        }, cancellation);
        file.Refresh();
    }

    private ImageFormat ResolveExportFormat(ImageFile file, ImageFormat? format)
    {
        var resolved = format ?? GetFormatByExtension(file.Extension)
            ?? throw new NotSupportedException($"No image format matches the extension of '{file.Name}'.");
        if (resolved.DocumentKind != DocumentKind.Image || !resolved.SupportsSaving)
            throw new NotSupportedException($"{resolved.DisplayName} cannot be used to export a picture.");
        return resolved;
    }

    private ImageFormat ResolveSaveFormat(DocumentKind kind, ImageFile file, ImageFormat? format)
    {
        var resolved = format ?? GetFormatByExtension(file.Extension)
            ?? throw new NotSupportedException($"No image format matches the extension of '{file.Name}'.");
        // An image is saved in a raster format; an SVG drawing in SVG, or exported to a raster format.
        var allowed = resolved.SupportsSaving && (kind == DocumentKind.Svg || resolved.DocumentKind == kind);
        if (!allowed)
            throw new NotSupportedException($"A {kind} document cannot be saved as {resolved.DisplayName}.");
        return resolved;
    }

    private static void FinishSave(IDocument document, ImageFile file, ImageFormat format)
    {
        file.Refresh();
        document.File = file;
        document.FileType = document.Kind == DocumentKind.Svg && file.Extension.Equals(".svgz", StringComparison.OrdinalIgnoreCase)
            ? "svgz"
            : format.SupportedExtensions[0];
        document.History.SetClean();
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.Ordinal);
}
