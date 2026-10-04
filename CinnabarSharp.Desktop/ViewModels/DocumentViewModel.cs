using CommunityToolkit.Mvvm.ComponentModel;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Desktop.ViewModels;

public partial class DocumentViewModel(ImageDocument document) : ViewModelBase
{
    public ImageDocument Document { get; } = document;

    public string Title => Document.IsDirty ? Document.DisplayName + " *" : Document.DisplayName;

    /// <summary>The name without the " *" of <see cref="Title"/>: the tab shows a dot instead.</summary>
    public string Name => Document.DisplayName;

    public bool IsDirty => Document.IsDirty;

    public const int ThumbnailSide = 44;

    private Avalonia.Media.Imaging.Bitmap? _thumbnail;

    /// <summary>A small picture for the tab; made when the image is opened and after each edit (see <see cref="RefreshThumbnail"/>).</summary>
    public Avalonia.Media.Imaging.Bitmap? Thumbnail => _thumbnail;

    public void RefreshThumbnail()
    {
        if (Document.Layers.Count() == 0)
            return;
        var (bgra, width, height) = Document.Layers.GetFlattenedThumbnail(ThumbnailSide);
        var previous = _thumbnail;
        _thumbnail = Services.BitmapFactory.FromBgra(bgra, width, height);
        OnPropertyChanged(nameof(Thumbnail));
        previous?.Dispose();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsDirty));
    }
}
