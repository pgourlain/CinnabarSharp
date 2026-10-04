using CinnabarSharp.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Views;

public partial class CanvasSizeWindow : Window
{
    public CanvasSizeWindow()
    {
        InitializeComponent();
        DialogSizing.FitToScreen(this);
        Opened += (_, _) => WidthInput.Focus();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if ((DataContext as CanvasSizeViewModel)?.ToOptions() is { } options)
            Close(options);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
