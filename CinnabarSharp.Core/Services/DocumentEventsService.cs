using System;
using System.Linq;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services
{

    public interface IDocumentEventsService
    {
        IObservable<EventItem<DocumentEventEnum>> DocumentEvents { get; }

        void PushEvent(EventItem<DocumentEventEnum> item);

    }

    public class DocumentEventsService : IDocumentEventsService
    {
        DocumentEventsTracker _documentEvents = new();
        public DocumentEventsService()
        {
        }

        public IObservable<EventItem<DocumentEventEnum>> DocumentEvents => _documentEvents;

        public void PushEvent(EventItem<DocumentEventEnum> item)
        {
            _documentEvents.Push(item);
        }
    }

    class ObservableEvents<T> :  IObservable<T>
    {
        private List<IObserver<T>> observers;
        public ObservableEvents()
        {
            observers = new ();
        }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            if (!observers.Contains(observer))
                observers.Add(observer);
            return new Unsubscriber<T>(observers, observer);
        }

        public void Push(T item)
        {
            foreach (var observer in observers)
            {
                observer.OnNext(item);
            }
        }

    }


    class DocumentEventsTracker : ObservableEvents<EventItem<DocumentEventEnum>>
    {
    }


    class Unsubscriber<T> : IDisposable
    {
        private List<IObserver<T>> _observers;
        private IObserver<T> _observer;

        public Unsubscriber(List<IObserver<T>> observers, IObserver<T> observer)
        {
            _observer = observer;
            _observers = observers;

        }
        public void Dispose()
        {
            if (_observer != null && _observers.Contains(_observer))
                _observers.Remove(_observer);
        }
    }

}

