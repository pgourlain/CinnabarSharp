using Avalonia.Controls;
using Avalonia.Interactivity;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Views;

public partial class ResizeImageWindow : Window
{
    public ResizeImageWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WidthInput.Focus();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as ResizeImageViewModel)?.ToOptions() is { } options)
            Close(options);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
