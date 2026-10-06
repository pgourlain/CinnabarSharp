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

        public IDocument ActiveDocument
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

        public ImageDocument? ActiveImageDocument => HasOpenDocuments ? OpenDocuments[active_document_index] as ImageDocument : null;

        public List<IDocument> OpenDocuments { get; }

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
            AddAndActivate(doc);
            return doc;
        }

        private int new_svg_name = 1;

        public CinnabarSharp.Core.Vector.SvgDocument NewSvgDocument(double width, double height, CinnabarSharp.Core.Vector.SvgUnit unit) =>
            OpenSvgDocument(CinnabarSharp.Core.Vector.SvgDocumentFactory.Create(width, height, unit), null, null);

        public CinnabarSharp.Core.Vector.SvgDocument OpenSvgDocument(CinnabarSharp.Vector.SvgRoot root, ImageFile? file, string? fileType)
        {
            var doc = serviceProvider.GetService<CinnabarSharp.Core.Vector.SvgDocument>()!;
            doc.Attach(root);
            if (file is not null)
            {
                doc.File = file;
                doc.FileType = fileType ?? file.Extension.TrimStart('.').ToLowerInvariant();
            }
            else
                doc.DisplayName = Translations.GetString("Unsaved Drawing {0}", new_svg_name++);
            doc.Workspace.ViewSize = doc.ImageSize;
            doc.Workspace.History.PushNewItem(new BaseHistoryItem(file is null ? "New Drawing" : "Open Drawing"));
            AddAndActivate(doc);
            return doc;
        }

        public void AddAndActivate(IDocument document)
        {
            OpenDocuments.Add(document);
            _documentEventsService.PushEvent(new DocumentEventItem(document, DocumentEventEnum.DocumentCreated));
            SetActiveDocument(OpenDocuments.Count - 1);
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

        public void CloseDocument(IDocument document)
        {
            var index = OpenDocuments.IndexOf(document);
            if (index < 0)
                throw new ArgumentException("Document is not open in this workspace.", nameof(document));

            var wasActive = index == active_document_index;
            OpenDocuments.RemoveAt(index);
            document.Workspace.History.Clear();
            document.Close();
            _documentEventsService.PushEvent(new DocumentEventItem(document, DocumentEventEnum.DocumentClosed));

            if (OpenDocuments.Count == 0)
                active_document_index = -1;
            else if (wasActive)
                SetActiveDocument(Math.Min(index, OpenDocuments.Count - 1));
            else if (index < active_document_index)
                active_document_index--;
        }

        public void SetActiveDocument(IDocument document)
        {
            var index = OpenDocuments.IndexOf(document);
            if (index < 0)
                throw new ArgumentException("Document is not open in this workspace.", nameof(document));
            if (index != active_document_index)
                SetActiveDocument(index);
        }
    }
}

