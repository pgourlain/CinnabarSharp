using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services;

/// <summary>An image file format that can be opened and saved.</summary>
public abstract class ImageFormat : IImageImporter, IImageExporter
{
    protected ImageFormat(string name, string displayName, string[] extensions)
    {
        Name = name;
        DisplayName = displayName;
        SupportedExtensions = extensions;
    }

    /// <summary>The kind of document this format opens and saves (raster formats: <see cref="DocumentKind.Image"/>).</summary>
    public virtual DocumentKind DocumentKind => DocumentKind.Image;

    public string Name { get; }
    public string DisplayName { get; }

    /// <summary>Lower-case extensions without the dot; the first one is the default.</summary>
    public string[] SupportedExtensions { get; }

    /// <summary>True if saving keeps layers; otherwise the saved file is the flattened image.</summary>
    public virtual bool SupportsLayers => false;

    public virtual bool SupportsTransparency => true;

    /// <summary>False for formats that can only be opened (e.g. HEIC: no encoder is available).</summary>
    public virtual bool SupportsSaving => true;

    /// <summary>Whether the file's content is in this format, regardless of its extension.</summary>
    public abstract bool MatchesContent(ImageFile file);

    /// <summary>
    /// The image's pixel size read from the file's header/metadata, without decoding its pixels — cheap even
    /// for a very large file. Null when a format can't tell without a full <see cref="Import"/> (the default;
    /// override where a fast header read is available).
    /// </summary>
    public virtual ImageSize? PeekSize(ImageFile file) => null;

    public abstract void Import(ImageFile file);

    /// <summary>
    /// The slow, thread-safe half of <see cref="Import(ImageFile)"/> (reading and decoding the file): it touches no
    /// document, so a caller can run it on a background thread and then call <see cref="Import(ImageFile, object?)"/>
    /// where events may be raised (the UI thread). Null when the format has nothing to separate (the default).
    /// </summary>
    public virtual object? Decode(ImageFile file) => null;

    /// <summary>Creates the document from what <see cref="Decode"/> returned; decodes itself when that was null.</summary>
    public virtual void Import(ImageFile file, object? decoded) => Import(file);

    public abstract void Export(ImageDocument document, ImageFile file);
}
