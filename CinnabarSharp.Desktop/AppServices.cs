using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CinnabarSharp.Core.Extensions;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop;

public static class AppServices
{
    public static IServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        StartupTrace.Mark("AppServices.Build start");
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddCinnabarSharpServices();
        services.AddSingleton(_ => new RecentFilesStore(RecentFilesStore.DefaultPath));
        services.AddSingleton(_ => new SettingsStore(SettingsStore.DefaultPath));
        // Far-back undo/redo steps spill to disk (performance-tasks.md P1.4); a test overrides this with a
        // throwaway temp folder (see TestHarness) so it never touches the real user's app data.
        services.AddSingleton<IHistoryStorage>(_ => FileHistoryStorage.CreateDefault());
        services.AddSingleton<CinnabarSharp.Core.Tools.ITextRasterizer, AvaloniaTextRasterizer>();
        // Text in SVG drawings is drawn from the platform fonts (SvgDocument takes this from the container).
        services.AddSingleton<CinnabarSharp.Vector.IGlyphOutlineProvider, AvaloniaGlyphOutlineProvider>();
        services.AddSingleton<AgentConnection>();
        services.AddSingleton(_ => new UpdateChecker());
        services.AddSingleton(sp => new RecoveryStore(RecoveryStore.DefaultFolder, sp.GetRequiredService<IFormatManager>()));
        services.AddSingleton<MainViewModel>();
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();
        StartupTrace.Mark("AppServices.Build end");
        return provider;
    }
}
