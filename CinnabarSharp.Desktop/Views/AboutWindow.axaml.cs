using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CinnabarSharp.Desktop.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3)}";
        var avalonia = typeof(Window).Assembly.GetName().Version?.ToString(3);
        RuntimeText.Text = $"{RuntimeInformation.FrameworkDescription} · Avalonia {avalonia} · {RuntimeInformation.OSDescription}";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
