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

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsDirty));
    }
}
