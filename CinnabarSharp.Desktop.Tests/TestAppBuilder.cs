using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using CinnabarSharp.Desktop;
using CinnabarSharp.Desktop.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
[assembly: AvaloniaTestFramework]

namespace CinnabarSharp.Desktop.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
