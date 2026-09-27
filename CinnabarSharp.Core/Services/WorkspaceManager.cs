using System;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Models;
using System.Reflection.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace CinnabarSharp.Core.Services
{
	public class WorkspaceManager : IWorkspaceService
    {
        private int active_document_index = -1;
        private int new_file_name = 1;
        private readonly IServiceProvider serviceProvider;
        private readonly IDocumentEventsService _documentEventsService;

        public bool HasOpenDocuments { get { return OpenDocuments.Count > 0; } }

        public ImageDocument ActiveDocument
        {
            get
            {
                if (HasOpenDocuments)
                    return OpenDocuments[active_document_index];

                throw new InvalidOperationException("Tried to get WorkspaceManager.ActiveDocument when there are no open Documents.  Check HasOpenDocuments first.");
            }
        }

        public ImageDocumentWorkspace ActiveWorkspace
        {
            get
            {
                if (HasOpenDocuments)
                    return OpenDocuments[active_document_index].Workspace;

                throw new InvalidOperationException("Tried to get WorkspaceManager.ActiveWorkspace when there are no open Documents.  Check HasOpenDocuments first.");
            }
        }

        public List<ImageDocument> OpenDocuments { get; }

        public WorkspaceManager(IServiceProvider serviceProvider,
            IDocumentEventsService documentEventsService)
		{
            OpenDocuments = new();
            this.serviceProvider = serviceProvider;
            _documentEventsService = documentEventsService;
        }

        public ImageDocument NewDocument(ImageSize size, ColorBgra background)
        {
            var doc = CreateAndActivateDocument(null, null, size);
            doc.Workspace.ViewSize = size;
            var layer = doc.Layers.AddNewLayer(Translations.GetString("Background"));
            var transparent = layer.Surface;
            layer.Surface = Utility.CreateImage(size.Width, size.Height, background);
            transparent.Dispose();
            doc.Workspace.Invalidate();
            return doc;
        }

        public ImageDocument NewDocumentFromImage(ClipboardImage image)
        {
            var doc = NewDocument(new ImageSize(image.Width, image.Height), ColorBgra.Transparent);
            var layer = doc.Layers[0];
            var transparent = layer.Surface;
            layer.Surface = Utility.FromBgra(image.Bgra, image.Width, image.Height);
            transparent.Dispose();
            doc.Workspace.Invalidate();
            return doc;
        }

        public ImageDocument CreateAndActivateDocument(ImageFile? file, string? file_type, ImageSize size)
        {
            ImageDocument doc = serviceProvider.GetService<ImageDocument>()!;
            doc.ImageSize = size;

            if (file is not null)
            {
                ArgumentNullException.ThrowIfNullOrEmpty(file_type);
                doc.File = file;
                doc.FileType = file_type;
            }
            else
                doc.DisplayName = Translations.GetString("Unsaved Image {0}", new_file_name++);

            doc.Workspace.History.PushNewItem(new BaseHistoryItem(file is null ? "New Image" : "Open Image"));
            OpenDocuments.Add(doc);
            var ev = new DocumentEventItem(doc, DocumentEventEnum.DocumentCreated);
            _documentEventsService.PushEvent(ev);

            SetActiveDocument(OpenDocuments.Count-1);

            return doc;
        }

        public void SetActiveDocument(int index)
        {
            if (index >= OpenDocuments.Count)
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    $"Tried to {nameof(WorkspaceManager)}.{nameof(SetActiveDocument)} greater than {nameof(OpenDocuments)}."
                );
            if (index < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    $"Tried to {nameof(WorkspaceManager)}.{nameof(SetActiveDocument)} less that zero."
                );
            active_document_index = index;
            var evt = new DocumentEventItem(OpenDocuments[index], DocumentEventEnum.ActiveDocumentChanged);
            _documentEventsService.PushEvent(evt);
        }

        public void CloseDocument(ImageDocument document)
        {
            var index = OpenDocuments.IndexOf(document);
            if (index < 0)
                throw new ArgumentException("Document is not open in this workspace.", nameof(document));

            var wasActive = index == active_document_index;
            OpenDocuments.RemoveAt(index);
            document.Workspace.History.Clear();
            document.Layers.Close();
            _documentEventsService.PushEvent(new DocumentEventItem(document, DocumentEventEnum.DocumentClosed));

            if (OpenDocuments.Count == 0)
                active_document_index = -1;
            else if (wasActive)
                SetActiveDocument(Math.Min(index, OpenDocuments.Count - 1));
            else if (index < active_document_index)
                active_document_index--;
        }

        public void SetActiveDocument(ImageDocument document)
        {
            var index = OpenDocuments.IndexOf(document);
            if (index < 0)
                throw new ArgumentException("Document is not open in this workspace.", nameof(document));
            if (index != active_document_index)
                SetActiveDocument(index);
        }
    }
}

