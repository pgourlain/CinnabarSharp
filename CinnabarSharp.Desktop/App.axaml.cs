using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using CinnabarSharp.Desktop.ViewModels;
using CinnabarSharp.Desktop.Views;

namespace CinnabarSharp.Desktop;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = AppServices.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainViewModel>();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            var files = desktop.Args ?? [];
            window.Opened += async (_, _) =>
            {
                foreach (var file in files)
                    await vm.OpenFileAsync(file);
            };

            // macOS application menu (next to the Apple menu).
            NativeMenu.SetMenu(this, new NativeMenu
            {
                new NativeMenuItem("About CinnabarSharp") { Command = vm.AboutCommand },
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
