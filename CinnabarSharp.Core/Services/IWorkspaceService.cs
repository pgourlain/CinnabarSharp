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
        void SetActiveDocument(IDocument document);

        /// <summary>Closes the document without saving; the next document (or the previous one, if it was last) becomes active.</summary>
        void CloseDocument(IDocument document);
        List<IDocument> OpenDocuments { get; }

        /// <summary>Opens an already built document as a new tab and activates it (raises DocumentCreated, then ActiveDocumentChanged).</summary>
        void AddAndActivate(IDocument document);
    }
}

