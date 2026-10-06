namespace CinnabarSharp.Core.Models;

/// <summary>What a tab holds: pixels in layers, or a vector (SVG) drawing.</summary>
public enum DocumentKind
{
    Image,
    Svg,
}

/// <summary>
/// An open document: what the workspace, the tabs, the events and the history know about. <see cref="ImageDocument"/>
/// (raster) and the SVG document implement it; code that needs layers or pixels pattern-matches on
/// <see cref="ImageDocument"/>.
/// </summary>
public interface IDocument
{
    /// <summary>Identity that stays the same for the life of the document.</summary>
    Guid Id { get; }

    DocumentKind Kind { get; }

    string DisplayName { get; set; }

    /// <summary>The file the document was opened from or last saved to.</summary>
    ImageFile? File { get; set; }

    /// <summary>Extension of the format of <see cref="File"/>, e.g. "png" or "ora" (see <see cref="ImageDocument.FileType"/>).</summary>
    string? FileType { get; set; }

    bool HasFile => File is not null;

    bool IsDirty { get; set; }

    /// <summary>Pixel size at 100 % zoom.</summary>
    ImageSize ImageSize { get; set; }

    ImageDocumentWorkspace Workspace { get; }

    IImageDocumentHistory History => Workspace.History;

    /// <summary>Releases what the document holds (pixel data). Called by <c>IWorkspaceService.CloseDocument</c>.</summary>
    void Close();

    /// <summary>A small picture of the document (longest side <paramref name="maxSide"/>, never enlarged), straight-alpha BGRA.</summary>
    (byte[] Bgra, int Width, int Height) GetThumbnail(int maxSide);
}
