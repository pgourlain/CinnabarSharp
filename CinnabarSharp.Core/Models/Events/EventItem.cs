using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Models
{
    public record EventItem<T>
    {
        public EventItem(IDocument document, T state)
        {
            this.Document = document;
            this.State = state;

        }
        public IDocument Document { get; }
        public T State { get; }
    }

}

