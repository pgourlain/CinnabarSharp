using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CinnabarSharp.Desktop.Views;

/// <summary>Dialog result: true for OK. The caller commits or cancels the adjustment session.</summary>
public partial class PhotoFilterWindow : Window
{
    public PhotoFilterWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
