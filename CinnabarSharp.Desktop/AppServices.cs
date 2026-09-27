using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop;

public static class AppServices
{
    public static IServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddCinnabarSharpServices();
        services.AddSingleton(_ => new RecentFilesStore(RecentFilesStore.DefaultPath));
        services.AddSingleton(_ => new SettingsStore(SettingsStore.DefaultPath));
        services.AddSingleton<MainViewModel>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
