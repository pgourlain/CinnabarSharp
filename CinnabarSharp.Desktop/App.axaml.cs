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

    /// <summary>"Open Log Folder" in the macOS application menu (there is no Help menu there).</summary>
    private void OnCheckForUpdatesClick(object? sender, EventArgs e) =>
        Services?.GetRequiredService<MainViewModel>().CheckForUpdatesCommand.Execute(null);

    /// <summary>The usage statistics switch, after "Check for Updates…" (only in builds that have an endpoint).</summary>
    private static void AddUsageStatisticsItem(NativeMenu menu, MainViewModel vm)
    {
        var item = new NativeMenuItem("Send Anonymous Usage Statistics")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = vm.SendUsageStatistics,
            Command = vm.ToggleUsageStatisticsCommand,
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SendUsageStatistics))
                item.IsChecked = vm.SendUsageStatistics;
        };
        var after = menu.Items.OfType<NativeMenuItem>().ToList().FindIndex(i => i.Header == "Check for Updates…");
        menu.Items.Insert(after + 1, item);
    }

    private void OnOpenLogFolderClick(object? sender, EventArgs e) =>
        Services?.GetRequiredService<MainViewModel>().OpenLogFolderCommand.Execute(null);

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
            vm.Updates = Services.GetRequiredService<CinnabarSharp.Core.Services.UpdateChecker>();
            vm.Recovery = Services.GetRequiredService<CinnabarSharp.Core.Services.RecoveryStore>();
            if (TelemetryEndpoint.Resolve() is { } endpoint)
                vm.Telemetry = new CinnabarSharp.Core.Services.TelemetryClient(endpoint,
                    CinnabarSharp.Core.Services.TelemetryContext.ForCurrentProcess(AppVersion.Current));
            if (OperatingSystem.IsMacOS() && vm.HasTelemetry && NativeMenu.GetMenu(this) is { } appMenu)
                AddUsageStatisticsItem(appMenu, vm);
            desktop.Exit += (_, _) =>
            {
                vm.FlushTelemetry(TimeSpan.FromSeconds(2));
                vm.EndRecoverySession();
                vm.Agents.Dispose();
            };
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
                    // Once the window is up, in the background: ask once, then look for a newer release.
                    else if (Environment.GetEnvironmentVariable("CINNABARSHARP_NO_UPDATE_CHECK") is not { Length: > 0 })
                        _ = vm.AfterLaunchAsync();
                }, DispatcherPriority.Background);
                foreach (var file in files)
                    await vm.OpenFileAsync(file);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
