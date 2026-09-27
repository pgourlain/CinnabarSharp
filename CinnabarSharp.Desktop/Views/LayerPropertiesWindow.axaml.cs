using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CinnabarSharp.Desktop.Views;

/// <summary>Dialog result: true for OK. Closing any other way returns null (treated as Cancel).</summary>
public partial class LayerPropertiesWindow : Window
{
    public LayerPropertiesWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            NameInput.Focus();
            NameInput.SelectAll();
        };
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
