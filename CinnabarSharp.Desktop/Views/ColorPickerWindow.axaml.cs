using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CinnabarSharp.Desktop.Views;

/// <summary>Dialog result: the chosen color, or null when cancelled.</summary>
public partial class ColorPickerWindow : Window
{
    public ColorPickerWindow()
    {
        InitializeComponent();
        Opened += (_, _) => FixTabIcons();
    }

    /// <summary>
    /// The icons of the picker's tabs (spectrum, palette, components) are geometries shared through the theme's resources,
    /// and drawn from there they come out blank in this app. Each icon gets a copy of its own geometry.
    /// </summary>
    private void FixTabIcons()
    {
        foreach (var icon in this.GetVisualDescendants().OfType<PathIcon>().Where(i => i.Data is not null))
            icon.Data = Geometry.Parse(icon.Data!.ToString()!);
    }

    public ColorPickerWindow(string title, Color initial) : this()
    {
        Title = title;
        Picker.Color = initial;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(Picker.Color);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
