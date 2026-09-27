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

    public string Name { get; }
    public string DisplayName { get; }

    /// <summary>Lower-case extensions without the dot; the first one is the default.</summary>
    public string[] SupportedExtensions { get; }

    /// <summary>True if saving keeps layers; otherwise the saved file is the flattened image.</summary>
    public virtual bool SupportsLayers => false;

    public virtual bool SupportsTransparency => true;

    /// <summary>Whether the file's content is in this format, regardless of its extension.</summary>
    public abstract bool MatchesContent(ImageFile file);

    public abstract void Import(ImageFile file);

    public abstract void Export(ImageDocument document, ImageFile file);
}
