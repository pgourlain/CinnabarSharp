using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services;

public interface IFormatManager
{
    IReadOnlyList<ImageFormat> Formats { get; }

    /// <summary>Formats that can be written (for Save As).</summary>
    IReadOnlyList<ImageFormat> SaveFormats { get; }

    /// <summary>Extension with or without the leading dot, any case.</summary>
    ImageFormat? GetFormatByExtension(string extension);

    /// <summary>Finds the format from the file extension, or from the file content when the extension is missing or unknown.</summary>
    ImageFormat? GetFormatForFile(ImageFile file);

    /// <summary>Opens the file as a new active document, or activates it if it is already open.</summary>
    ImageDocument Open(ImageFile file);

    /// <summary>The image's pixel size without decoding it, via the matching format's <see cref="ImageFormat.PeekSize"/>;
    /// null if the format isn't recognized or can't tell without a full <see cref="Open"/>.</summary>
    ImageSize? PeekSize(ImageFile file);

    /// <summary>Writes the document to the file and makes it the document's file. The format defaults to the file extension's.</summary>
    void Save(ImageDocument document, ImageFile file, ImageFormat? format = null);

    /// <summary>
    /// Same as <see cref="Save"/>, but encodes on a background thread instead of blocking the caller — for a UI
    /// caller, wrap it in something that disables editing for the duration (e.g. <c>MainViewModel.RunBusyAsync</c>)
    /// so nothing mutates the document while it's being read on that other thread; everything other than the
    /// encode itself (marking the document clean, etc.) still runs on whatever thread awaits this, which for a
    /// caller with a UI SynchronizationContext is back on the UI thread, same as any other <c>await</c>.
    /// </summary>
    Task SaveAsync(ImageDocument document, ImageFile file, ImageFormat? format = null, CancellationToken cancellation = default);
}

public class FormatManager : IFormatManager
{
    private readonly IWorkspaceService _workspace;

    public FormatManager(IEnumerable<IImageImporter> importers, IWorkspaceService workspace)
    {
        Formats = importers.OfType<ImageFormat>().ToList();
        _workspace = workspace;
    }

    public IReadOnlyList<ImageFormat> Formats { get; }

    public IReadOnlyList<ImageFormat> SaveFormats => Formats.Where(f => f.SupportsSaving).ToList();

    public ImageFormat? GetFormatByExtension(string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return Formats.FirstOrDefault(f => f.SupportedExtensions.Contains(ext));
    }

    public ImageFormat? GetFormatForFile(ImageFile file) =>
        GetFormatByExtension(file.Extension) ?? Formats.FirstOrDefault(f => f.MatchesContent(file));

    public ImageDocument Open(ImageFile file)
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

    public ImageSize? PeekSize(ImageFile file) => GetFormatForFile(file)?.PeekSize(file);

    public void Save(ImageDocument document, ImageFile file, ImageFormat? format = null)
    {
        format = ResolveSaveFormat(file, format);
        format.Export(document, file);
        FinishSave(document, file, format);
    }

    public async Task SaveAsync(ImageDocument document, ImageFile file, ImageFormat? format = null, CancellationToken cancellation = default)
    {
        format = ResolveSaveFormat(file, format);
        var resolved = format;
        await Task.Run(() => resolved.Export(document, file), cancellation);
        FinishSave(document, file, resolved);
    }

    private ImageFormat ResolveSaveFormat(ImageFile file, ImageFormat? format) =>
        format ?? GetFormatByExtension(file.Extension)
            ?? throw new NotSupportedException($"No image format matches the extension of '{file.Name}'.");

    private static void FinishSave(ImageDocument document, ImageFile file, ImageFormat format)
    {
        file.Refresh();
        document.File = file;
        document.FileType = format.SupportedExtensions[0];
        document.Workspace.History.SetClean();
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.Ordinal);
}
