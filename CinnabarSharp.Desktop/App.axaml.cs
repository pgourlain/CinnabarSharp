using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;
using System.Linq;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>"About CinnabarSharp" in the macOS application menu (declared in App.axaml).</summary>
    private void OnAboutClick(object? sender, EventArgs e) =>
        Services?.GetRequiredService<MainViewModel>().AboutCommand.Execute(null);

    public override void OnFrameworkInitializationCompleted()
    {
        StartupTrace.Mark("Framework initialized");
        Services = AppServices.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainViewModel>();
            StartupTrace.Mark("MainViewModel created");
            var settings = Services.GetRequiredService<SettingsStore>();
            // Before RestoreSettings: it turns attached mode back on if the user left it on.
            vm.Agents = Services.GetRequiredService<AgentConnection>();
            desktop.Exit += (_, _) => vm.Agents.Dispose();
            var window = new MainWindow { DataContext = vm };
            StartupTrace.Mark("MainWindow created");
            window.RestoreSettings(settings);
            desktop.MainWindow = window;

            // macOS delivers "Open With" / double-clicked files as activation events, not command-line arguments.
            if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            {
                activatable.Activated += async (_, e) =>
                {
                    if (e is FileActivatedEventArgs files)
                        foreach (var path in files.Files.Select(f => f.TryGetLocalPath()).OfType<string>())
                            await vm.OpenFileAsync(path);
                };
            }
            var files = desktop.Args ?? [];
            window.Opened += async (_, _) =>
            {
                StartupTrace.Mark("Window opened");
                // Render priority runs after the first layout and render pass: the window is on screen.
                Dispatcher.UIThread.Post(() =>
                {
                    StartupTrace.Mark("First frame");
                    if (StartupTrace.ExitWhenVisible && files.Length == 0)
                        desktop.Shutdown();
                }, DispatcherPriority.Background);
                foreach (var file in files)
                    await vm.OpenFileAsync(file);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
