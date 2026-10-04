using CinnabarSharp.Desktop.Services;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Views;

/// <summary>Dialog result: true for OK; the caller then edits the page on the canvas.</summary>
public partial class ComicPageWindow : Window
{
    public ComicPageWindow()
    {
        InitializeComponent();
        DialogSizing.FitToScreen(this);
    }

    private async void OnAddImages(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ComicPageViewModel vm)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add images",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.ImageAll, FilePickerFileTypes.All],
        });
        vm.AddFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
