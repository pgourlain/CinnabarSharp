using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CinnabarSharp.Desktop.Views;

/// <summary>Dialog result: true for OK. The caller reads the quality.</summary>
public partial class JpegQualityWindow : Window
{
    public JpegQualityWindow()
    {
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
