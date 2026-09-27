using System;
using System.Reflection.Metadata;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services
{
	public interface IWorkspaceService
	{
        ImageDocument ActiveDocument { get; }
        ImageDocumentWorkspace ActiveWorkspace { get; }
        bool HasOpenDocuments { get; }
        ImageDocument CreateAndActivateDocument(ImageFile? file, string? file_type, ImageSize size);
        ImageDocument NewDocument(ImageSize size, ColorBgra background);
        void SetActiveDocument(ImageDocument document);

        /// <summary>Closes the document without saving; the next document (or the previous one, if it was last) becomes active.</summary>
        void CloseDocument(ImageDocument document);
        List<ImageDocument> OpenDocuments { get; }
    }
}

