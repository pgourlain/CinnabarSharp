using CinnabarSharp.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Views;

public partial class NewImageWindow : Window
{
    public NewImageWindow()
    {
        InitializeComponent();
        DialogSizing.FitToScreen(this);
        Opened += (_, _) => WidthInput.Focus();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        var options = (DataContext as NewImageViewModel)?.ToOptions();
        if (options is not null)
            Close(options);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
