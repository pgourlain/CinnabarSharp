using CinnabarSharp.Core.Models;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Vector;

namespace CinnabarSharp.Core.Vector;

/// <summary>Ties a raster document to the <c>&lt;image&gt;</c> of a drawing whose pixels it edits.</summary>
public sealed record BitmapEditLink(SvgDocument Drawing, long ImageId);

/// <summary>Edit Bitmap: the pixels of an SVG image in a raster tab, and back.</summary>
public static class SvgBitmapEditing
{
    /// <summary>Opens the picture of <paramref name="image"/> in a new raster document linked to it.</summary>
    public static ImageDocument Open(IWorkspaceService workspace, SvgDocument drawing, SvgImage image)
    {
        var pixels = drawing.Actions.DecodeImage(image)
            ?? throw new InvalidOperationException("The picture cannot be read: its data is missing, or it is a link outside the folder of the drawing.");
        var document = workspace.NewDocumentFromImage(pixels);
        document.DisplayName = $"{(image.Label is { Length: > 0 } label ? label : image.Id ?? "image")} (bitmap)";
        document.BitmapEdit = new BitmapEditLink(drawing, image.InternalId);
        document.IsDirty = false;
        return document;
    }

    /// <summary>True when <paramref name="document"/> edits an image of a drawing that still has it.</summary>
    public static bool CanUpdate(ImageDocument document) => FindImage(document) is not null;

    private static SvgImage? FindImage(ImageDocument document) =>
        document.BitmapEdit is { } link
            ? link.Drawing.Root.Descendants().OfType<SvgImage>().FirstOrDefault(i => i.InternalId == link.ImageId)
            : null;

    /// <summary>Sends the flattened pixels back to the drawing as one history step; false when the image is gone.</summary>
    public static bool Update(ImageDocument document)
    {
        if (FindImage(document) is not { } image || document.BitmapEdit is not { } link)
            return false;
        var size = document.ImageSize;
        var bgra = document.Layers.GetFlattenedBgra(includeToolLayer: false);
        link.Drawing.Actions.ReplaceImage(image, new ClipboardImage(bgra, size.Width, size.Height));
        return true;
    }
}
