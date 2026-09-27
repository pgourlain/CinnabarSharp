using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Models
{
    public record EventItem<T>
    {
        public EventItem(ImageDocument document, T state)
        {
            this.Document = document;
            this.State = state;

        }
        public ImageDocument Document { get; }
        public T State { get; }
    }

}

