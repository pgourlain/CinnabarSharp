using System;
namespace CinnabarSharp.Core.Models
{
    public record CanvasEventItem : EventItem<DocumentEventEnum>
    {
        public CanvasEventItem(ImageDocument document, DocumentEventEnum state, ImageDocumentWorkspace workspace, RectangleI winRect)
            : base(document, state)
        {
            Workspace = workspace;
            this.Rect = winRect;
        }

        public ImageDocumentWorkspace Workspace { get; }
        public RectangleI Rect { get; }
    }
}

