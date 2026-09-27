using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Models
{
    public record LayerEventItem : EventItem<DocumentEventEnum>
    {
        public LayerEventItem(ImageDocument document, DocumentEventEnum state, Layer layer, int index)
            : base(document, state)
        {
            this.Layer = layer;
            Index = index;
        }
        public Layer Layer { get;}
        public int Index { get; }
    }

}

