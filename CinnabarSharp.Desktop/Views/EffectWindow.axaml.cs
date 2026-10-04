using CinnabarSharp.Desktop.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CinnabarSharp.Desktop.Views;

/// <summary>Dialog result: true for OK. The caller commits or cancels the adjustment session.</summary>
public partial class EffectWindow : Window
{
    public EffectWindow()
    {
        InitializeComponent();
        DialogSizing.FitToScreen(this);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
