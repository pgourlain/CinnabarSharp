using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Views;

public partial class AgentConnectionWindow : Window
{
    public AgentConnectionWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private async void OnDocs(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AgentConnectionViewModel vm)
            await Launcher.LaunchUriAsync(new Uri(vm.DocsUrl));
    }
}
