using CinnabarSharp.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Views;

public partial class PasteBesideWindow : Window
{
    public PasteBesideWindow()
    {
        InitializeComponent();
        DialogSizing.FitToScreen(this);
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PasteBesideViewModel vm)
            Close(vm.ToOptions());
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
