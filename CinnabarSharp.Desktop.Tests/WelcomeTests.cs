using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CinnabarSharp.Core.Models;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class WelcomeTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private Control Panel => _h.Window.FindControl<Control>("WelcomePanel")!;
    private TextBlock Hint => _h.Window.FindControl<TextBlock>("EmptyHint")!;

    [AvaloniaFact]
    public void Welcome_is_shown_with_recent_files_and_gives_way_to_the_image()
    {
        _h.Vm.RecentFiles.Add(TestHarness.SampleImage);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Panel.IsVisible);
        Assert.False(Hint.IsVisible);
        Assert.Equal("sample1.png", Assert.Single(_h.Vm.RecentItems).Name);
        _h.Capture("14-welcome");

        _h.Vm.CreateImage(new NewImageOptions(new ImageSize(40, 30), ColorBgra.White));
        Dispatcher.UIThread.RunJobs();

        Assert.False(Panel.IsVisible);
    }

    [AvaloniaFact]
    public async Task Clicking_a_recent_file_opens_it()
    {
        _h.Vm.RecentFiles.Add(TestHarness.SampleImage);

        await _h.Vm.OpenRecentItemCommand.ExecuteAsync(Assert.Single(_h.Vm.RecentItems));

        Assert.Equal("sample1.png", _h.Vm.ActiveDocument!.Document.DisplayName);
    }

    [AvaloniaFact]
    public void Files_that_no_longer_exist_are_not_listed()
    {
        _h.Vm.RecentFiles.Add(_h.TempPath("gone.png"));

        Assert.Empty(_h.Vm.RecentItems);
        Assert.False(_h.Vm.HasRecentItems);
    }

    [AvaloniaFact]
    public void Turning_the_welcome_off_shows_the_short_hint_instead()
    {
        _h.Vm.ShowWelcomeScreen = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Panel.IsVisible);
        Assert.True(Hint.IsVisible);
        Assert.False(_h.Vm.CaptureSettings(new Services.AppSettings()).ShowWelcome);

        _h.Vm.ApplySettings(new Services.AppSettings { ShowWelcome = true });
        Assert.True(_h.Vm.IsWelcomeVisible);
    }
}
