using System;
using System.Reflection.Metadata;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services
{
	public interface IWorkspaceService
	{
        IDocument ActiveDocument { get; }

        /// <summary>The active document when it is a raster <see cref="ImageDocument"/>; null when the active tab is another kind or nothing is open.</summary>
        ImageDocument? ActiveImageDocument { get; }
        ImageDocumentWorkspace ActiveWorkspace { get; }
        bool HasOpenDocuments { get; }
        ImageDocument CreateAndActivateDocument(ImageFile? file, string? file_type, ImageSize size);
        ImageDocument NewDocument(ImageSize size, ColorBgra background);

        /// <summary>New single-layer document holding the image (Paste Into New Image).</summary>
        ImageDocument NewDocumentFromImage(ClipboardImage image);
        /// <summary>Opens a vector drawing as a new tab: the document is built from DI, gets the root and the file, starts its history and becomes active.</summary>
        CinnabarSharp.Core.Vector.SvgDocument OpenSvgDocument(CinnabarSharp.Vector.SvgRoot root, ImageFile? file, string? fileType);

        /// <summary>A new empty drawing (File › New › SVG drawing): size in the given unit, one empty layer group.</summary>
        CinnabarSharp.Core.Vector.SvgDocument NewSvgDocument(double width, double height, CinnabarSharp.Core.Vector.SvgUnit unit);

        void SetActiveDocument(IDocument document);

        /// <summary>Closes the document without saving; the next document (or the previous one, if it was last) becomes active.</summary>
        void CloseDocument(IDocument document);
        List<IDocument> OpenDocuments { get; }

        /// <summary>Opens an already built document as a new tab and activates it (raises DocumentCreated, then ActiveDocumentChanged).</summary>
        void AddAndActivate(IDocument document);
    }
}

