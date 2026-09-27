namespace CinnabarSharp.Core.Models;

public record DocumentEventItem : EventItem<DocumentEventEnum>
{
    public DocumentEventItem(ImageDocument document, DocumentEventEnum state) : base(document, state)
    {
    }
}