using CommunityToolkit.Mvvm.ComponentModel;
using CinnabarSharp.Core.Models;

namespace CinnabarSharp.Desktop.ViewModels;

public partial class DocumentViewModel(ImageDocument document) : ViewModelBase
{
    public ImageDocument Document { get; } = document;

    public string Title => Document.IsDirty ? Document.DisplayName + " *" : Document.DisplayName;

    public void Refresh() => OnPropertyChanged(nameof(Title));
}
