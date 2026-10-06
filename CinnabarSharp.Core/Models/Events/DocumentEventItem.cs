namespace CinnabarSharp.Core.Models;

public record DocumentEventItem : EventItem<DocumentEventEnum>
{
    public DocumentEventItem(IDocument document, DocumentEventEnum state) : base(document, state)
    {
    }
}