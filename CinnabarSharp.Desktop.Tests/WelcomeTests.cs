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

        Assert.Equal("sample1.png", _h.Vm.ActiveDocument!.Image.DisplayName);
    }

    [AvaloniaFact]
    public async Task A_recent_file_gets_its_thumbnail_in_the_background()
    {
        _h.Vm.RecentFiles.Add(TestHarness.SampleImage);
        var item = Assert.Single(_h.Vm.RecentItems);
        Assert.Null(item.Thumbnail); // asked for: read in the background

        for (var i = 0; i < 100 && !item.HasThumbnail; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.True(item.HasThumbnail);
        Assert.Equal(CinnabarSharp.Desktop.Services.RecentThumbnails.Width, item.Thumbnail!.PixelSize.Width);
        Assert.Same(item, Assert.Single(_h.Vm.RecentItems)); // kept, so the picture is not read again
        _h.Capture("14-welcome-thumbnail");
    }

    [AvaloniaFact]
    public void Tabs_show_a_thumbnail_that_follows_the_edits()
    {
        _h.Vm.CreateImage(new NewImageOptions(new ImageSize(120, 80), ColorBgra.White));
        Dispatcher.UIThread.RunJobs();
        var tab = _h.Vm.ActiveDocument!;
        var first = tab.Thumbnail;
        Assert.NotNull(first);
        Assert.Equal((44, 29), (first.PixelSize.Width, first.PixelSize.Height));

        tab.Image.Actions.AddNewLayer();
        Dispatcher.UIThread.RunJobs();

        Assert.NotSame(first, tab.Thumbnail);
        _h.Capture("07-tab-thumbnail");
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
