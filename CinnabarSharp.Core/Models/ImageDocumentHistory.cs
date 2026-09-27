using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Core.Services;

class ImageDocumentHistory : IImageDocumentHistory
{
    private readonly ImageDocument document;

    public ImageDocumentHistory(ImageDocument document)
    {
        this.document = document;
    }

    public int Pointer => throw new NotImplementedException();

    public bool CanRedo => throw new NotImplementedException();

    public bool CanUndo => throw new NotImplementedException();

    public IEnumerable<IHistoryItem> Items => throw new NotImplementedException();

    public void Clear()
    {
        throw new NotImplementedException();
    }

    public void PushNewItem(IHistoryItem newItem)
    {
        throw new NotImplementedException();
    }

    public void Redo()
    {
        throw new NotImplementedException();
    }

    public void SetClean()
    {
        throw new NotImplementedException();
    }

    public void SetDirty()
    {
        throw new NotImplementedException();
    }

    public void Undo()
    {
        throw new NotImplementedException();
    }
}