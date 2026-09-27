using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace CinnabarSharp.Desktop.Views;

/// <summary>Dialog result: the chosen color, or null when cancelled.</summary>
public partial class ColorPickerWindow : Window
{
    public ColorPickerWindow()
    {
        InitializeComponent();
    }

    public ColorPickerWindow(string title, Color initial) : this()
    {
        Title = title;
        Picker.Color = initial;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(Picker.Color);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
