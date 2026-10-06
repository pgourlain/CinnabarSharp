
using ImageMagick;
using Microsoft.Extensions.Logging;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Models
{

    public class ImageDocument : IDocument
	{
        private bool is_dirty;
        private readonly IDocumentEventsService _documentEventsService;
        private readonly ILogger<ImageDocument> logger;
        private ImageFile? file;
        private string display_name = string.Empty;

        public ImageDocument(IDocumentEventsService documentEventsService, ILogger<ImageDocument> logger,
            IHistoryStorage? historyStorage = null)
		{
            is_dirty = false;
            _documentEventsService = documentEventsService;
            this.logger = logger;
            Layers = new (this, _documentEventsService, logger);
            Workspace = new(this, new ImageDocumentHistory(this, _documentEventsService, storage: historyStorage),
                _documentEventsService, logger);
            Actions = new DocumentActions(this);
        }

        public Guid Id { get; } = Guid.NewGuid();

        public DocumentKind Kind => DocumentKind.Image;

        public ImageSize ImageSize { get; set; }
        public ImageDocumentLayers Layers { get; }
        public ImageDocumentWorkspace Workspace { get; }

        public IImageDocumentHistory History => Workspace.History;

        /// <summary>Selected pixels, or null when nothing is selected (tools then act on the whole layer).</summary>
        public SelectionMask? Selection { get; private set; }

        public bool HasSelection => Selection is not null;

        /// <summary>The last paste while it can still be moved without leaving a hole (see <see cref="FloatingPaste"/>).</summary>
        public FloatingPaste? Floating { get; set; }

        /// <summary>Replaces the selection without recording history; an empty mask means no selection.</summary>
        public void SetSelection(SelectionMask? selection)
        {
            selection = selection is { IsEmpty: true } ? null : selection;
            if (ReferenceEquals(selection, Selection))
                return;
            Selection = selection;
            _documentEventsService.PushEvent(new DocumentEventItem(this, DocumentEventEnum.SelectionChanged));
        }

        /// <summary>Changes the image size (layers must be resized by the caller), keeping the zoom level.</summary>
        public void Resize(ImageSize size)
        {
            ImageSize = size;
            Workspace.UpdateViewSize();
            Workspace.Invalidate();
        }

        /// <summary>User-level edits that are recorded in the undo history.</summary>
        public DocumentActions Actions { get; }

        public ImageFile? File
        {
            get => file;
            set
            {
                file = value;
                DisplayName = file?.GetDisplayName() ?? String.Empty;
            }
        }

        public string DisplayName
        {
            get => display_name;
            set
            {
                display_name = value;
                OnRenamed();
            }
        }

        /// <summary>
        /// File type of the image, if File is not null. This is an extensions, e.g. "png" or "ora".
        /// This cannot necessarily be derived from the file name's extension (e.g. when opening
        /// a file with no extension or an incorrect extension) so it's easiest to record
        /// the file type used when opening the image.
        /// </summary>
        public string? FileType { get; set; }

        public bool IsDirty
        {
            get { return is_dirty; }
            set
            {
                if (is_dirty != value)
                {
                    is_dirty = value;
                    OnIsDirtyChanged();
                }
            }
        }

        protected void OnIsDirtyChanged()
        {
            var evt = new DocumentEventItem(this, DocumentEventEnum.DirtyChanged);
            _documentEventsService.PushEvent(evt);
        }

        protected void OnRenamed()
        {
            var evt = new DocumentEventItem(this, DocumentEventEnum.DocumentRenamed);
            _documentEventsService.PushEvent(evt);
        }

        public void Close() => Layers.Close();

        public (byte[] Bgra, int Width, int Height) GetThumbnail(int maxSide) => Layers.GetFlattenedThumbnail(maxSide);

        public IImageBuf GetFlattenedImage(bool clip_to_selection = false) => Layers.GetFlattenedImage(clip_to_selection);
        
    }
}

