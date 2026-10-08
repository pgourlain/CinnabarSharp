using System.Net;
using Avalonia.Headless.XUnit;
using CinnabarSharp.Core.Services;
using CinnabarSharp.Desktop.Services;
using CinnabarSharp.Desktop.ViewModels;

namespace CinnabarSharp.Desktop.Tests;

public sealed class TelemetryUiTests : IDisposable
{
    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (Bodies)
                Bodies.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private readonly TestHarness _h = new();
    private readonly Handler _handler = new();
    private readonly TelemetryClient _client;

    public TelemetryUiTests()
    {
        _client = new TelemetryClient(new Uri("https://telemetry.example.com/"),
            new TelemetryContext("0.9.5", "macos", "15.2", "arm64", "fr-FR"), _handler);
        Vm.Telemetry = _client;
    }

    public void Dispose()
    {
        _client.Dispose();
        _h.Dispose();
    }

    private MainViewModel Vm => _h.Vm;

    [AvaloniaFact]
    public async Task Without_an_endpoint_the_user_is_not_asked()
    {
        Vm.Telemetry = null;
        await Vm.StartTelemetryAsync();
        Assert.Empty(_h.Dialogs.Confirmations);
        Assert.Null(Vm.CaptureSettings(new AppSettings()).SendUsageStatistics);
    }

    [AvaloniaFact]
    public async Task A_refusal_sends_nothing_counts_nothing_and_is_remembered()
    {
        _h.Dialogs.ConfirmAnswers.Enqueue(false);
        await Vm.StartTelemetryAsync();
        Assert.Single(_h.Dialogs.Confirmations);
        Vm.SelectToolByShortcut("B");
        Assert.Empty(_client.Pending);
        Assert.Empty(_handler.Bodies);
        var saved = Vm.CaptureSettings(new AppSettings());
        Assert.False(saved.SendUsageStatistics);
        Assert.Null(saved.TelemetryInstallId);

        await Vm.StartTelemetryAsync();             // asked once, not again
        Assert.Single(_h.Dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task Agreeing_sends_the_start_then_counts_tools_and_keeps_the_id()
    {
        _h.Dialogs.ConfirmAnswers.Enqueue(true);
        await Vm.StartTelemetryAsync();
        Assert.True(_client.IsEnabled);
        await WaitForAsync(() => _handler.Bodies.Count == 1);
        Assert.Contains("\"app_start\":1", _handler.Bodies[0]);

        Vm.SelectToolByShortcut("B");
        Assert.Equal(1, _client.Pending["tool:Paintbrush"]);

        var saved = Vm.CaptureSettings(new AppSettings());
        Assert.True(saved.SendUsageStatistics);
        Assert.Equal(32, saved.TelemetryInstallId!.Length);
        Assert.Contains(saved.TelemetryInstallId, _handler.Bodies[0]);
    }

    [AvaloniaFact]
    public void The_id_is_kept_between_sessions_and_dropped_when_turned_off()
    {
        Vm.ApplySettings(new AppSettings { SendUsageStatistics = true, TelemetryInstallId = "abc" });
        Assert.True(Vm.SendUsageStatistics);
        Assert.Equal("abc", Vm.CaptureSettings(new AppSettings()).TelemetryInstallId);

        Vm.ToggleUsageStatisticsCommand.Execute(null);
        Assert.False(Vm.SendUsageStatistics);
        Assert.False(_client.IsEnabled);
        Assert.Null(Vm.CaptureSettings(new AppSettings()).TelemetryInstallId);

        Vm.ToggleUsageStatisticsCommand.Execute(null);
        Assert.True(_client.IsEnabled);
        var id = Vm.CaptureSettings(new AppSettings()).TelemetryInstallId;
        Assert.NotNull(id);
        Assert.NotEqual("abc", id);
    }

    [AvaloniaFact]
    public void Endpoint_comes_from_the_build_unless_overridden_or_turned_off()
    {
        Assert.Null(TelemetryEndpoint.Resolve(null, null, null));
        Assert.Null(TelemetryEndpoint.Resolve(null, null, ""));
        Assert.Equal(new Uri("https://a.example/"), TelemetryEndpoint.Resolve(null, null, "https://a.example/"));
        Assert.Equal(new Uri("http://localhost:8787/"), TelemetryEndpoint.Resolve(null, "http://localhost:8787/", "https://a.example/"));
        Assert.Null(TelemetryEndpoint.Resolve("0", null, "https://a.example/"));
        Assert.Null(TelemetryEndpoint.Resolve(null, null, "http://a.example/"));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }
}
