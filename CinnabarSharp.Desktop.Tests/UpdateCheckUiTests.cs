using System.Net;
using Avalonia.Headless.XUnit;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class UpdateCheckUiTests : IDisposable
{
    private sealed class Handler : HttpMessageHandler
    {
        public string Tag = "v0.9.1";
        public bool Offline;
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Offline)
                throw new HttpRequestException("offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"tag_name":"{{Tag}}","html_url":"https://github.com/pgourlain/CinnabarSharp/releases/tag/{{Tag}}","prerelease":false,"draft":false,"body":"x"}"""),
            });
        }
    }

    private readonly TestHarness _h = new();
    private readonly Handler _handler = new();
    private DateTime _now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    public UpdateCheckUiTests()
    {
        Vm.Updates = new UpdateChecker(_handler);
        Vm.CurrentVersion = new Version(0, 9, 0);
        Vm.UtcNow = () => _now;
    }

    public void Dispose() => _h.Dispose();

    private MainViewModel Vm => _h.Vm;

    [AvaloniaFact]
    public async Task Nothing_is_sent_before_the_user_answers_and_a_refusal_is_remembered()
    {
        _h.Dialogs.ConfirmAnswers.Enqueue(false);
        await Vm.StartUpdateCheckAsync();
        Assert.Equal(0, _handler.Requests);
        Assert.Single(_h.Dialogs.Confirmations);
        Assert.False(Vm.HasUpdate);
        Assert.False(Vm.CaptureSettings(new AppSettings()).CheckForUpdates);

        await Vm.StartUpdateCheckAsync();           // asked once, not again
        Assert.Single(_h.Dialogs.Confirmations);
        Assert.Equal(0, _handler.Requests);
    }

    [AvaloniaFact]
    public async Task Agreeing_looks_for_a_release_shows_the_banner_and_the_page_opens()
    {
        _h.Dialogs.ConfirmAnswers.Enqueue(true);
        await Vm.StartUpdateCheckAsync();

        Assert.Equal(1, _handler.Requests);
        Assert.True(Vm.HasUpdate);
        Assert.Contains("0.9.1", Vm.UpdateText);
        Assert.True(Vm.CaptureSettings(new AppSettings()).CheckForUpdates);
        Vm.OpenUpdatePageCommand.Execute(null);
        await Task.Yield();
        Assert.Equal(["https://github.com/pgourlain/CinnabarSharp/releases/tag/v0.9.1"], _h.Dialogs.UrlsOpened);
        Vm.DismissUpdateCommand.Execute(null);
        Assert.False(Vm.HasUpdate);
    }

    [AvaloniaFact]
    public async Task It_looks_at_most_once_a_day_and_a_skipped_version_stays_quiet()
    {
        _h.Dialogs.ConfirmAnswers.Enqueue(true);
        await Vm.StartUpdateCheckAsync();
        Vm.SkipUpdateCommand.Execute(null);
        Assert.False(Vm.HasUpdate);
        Assert.Equal("0.9.1", Vm.CaptureSettings(new AppSettings()).SkippedUpdate);

        _now = _now.AddHours(2);
        await Vm.StartUpdateCheckAsync();
        Assert.Equal(1, _handler.Requests);          // too soon

        _now = _now.AddHours(24);
        await Vm.StartUpdateCheckAsync();
        Assert.Equal(2, _handler.Requests);
        Assert.False(Vm.HasUpdate);                  // the same version was skipped

        _handler.Tag = "v0.9.2";
        _now = _now.AddHours(25);
        await Vm.StartUpdateCheckAsync();
        Assert.True(Vm.HasUpdate);
        Assert.Contains("0.9.2", Vm.UpdateText);
    }

    [AvaloniaFact]
    public async Task Being_offline_is_silent_at_startup_and_reported_when_asked_for()
    {
        _handler.Offline = true;
        _h.Dialogs.ConfirmAnswers.Enqueue(true);
        await Vm.StartUpdateCheckAsync();
        Assert.Empty(_h.Dialogs.Errors);
        Assert.False(Vm.HasUpdate);

        await Vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Single(_h.Dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task The_menu_command_checks_at_once_without_asking_and_says_when_up_to_date()
    {
        _handler.Tag = "v0.9.0";
        await Vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(1, _handler.Requests);
        Assert.Empty(_h.Dialogs.Confirmations);
        Assert.Contains(_h.Dialogs.Errors, e => e.Contains("up to date"));

        _handler.Tag = "v0.9.5";
        _h.Dialogs.ConfirmAnswers.Enqueue(true);
        await Vm.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(["https://github.com/pgourlain/CinnabarSharp/releases/tag/v0.9.5"], _h.Dialogs.UrlsOpened);
    }
}
