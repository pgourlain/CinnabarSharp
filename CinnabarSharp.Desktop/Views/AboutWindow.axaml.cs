using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CinnabarSharp.Desktop.Services;

namespace CinnabarSharp.Desktop.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        NoticesButton.IsVisible = File.Exists(NoticesPath);
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3)}";
        var avalonia = typeof(Window).Assembly.GetName().Version?.ToString(3);
        RuntimeText.Text = $"{RuntimeInformation.FrameworkDescription} · Avalonia {avalonia} · {RuntimeInformation.OSDescription}";
    }

    // Shipped next to the executable by packaging/package.sh; absent when running from source.
    private static readonly string NoticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");

    private async void OnNotices(object? sender, RoutedEventArgs e)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(NoticesPath));
        }
        catch (Exception ex)
        {
            AppLog.Error(nameof(AboutWindow), "Could not open the third-party notices", ex);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
