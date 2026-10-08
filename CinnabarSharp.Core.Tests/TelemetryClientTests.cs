using System.Net;
using System.Text.Json;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public class TelemetryClientTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool Offline;
        public List<string> Bodies { get; } = [];
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (Offline)
                throw new HttpRequestException("offline");
            return new HttpResponseMessage(Status);
        }
    }

    private static readonly TelemetryContext Context = new("0.9.5", "macos", "15.2", "arm64", "fr-FR");
    private static readonly Uri Endpoint = new("https://telemetry.example.com/v1/batch");

    private readonly Handler _handler = new();
    private DateTime _now = new(2026, 10, 8, 10, 0, 0, DateTimeKind.Utc);

    private TelemetryClient Client(Uri? endpoint = null) => new(endpoint ?? Endpoint, Context, _handler, () => _now);

    [Fact]
    public async Task Nothing_is_counted_or_sent_before_consent()
    {
        using var client = Client();
        client.Track("app_start");
        Assert.Empty(client.Pending);
        Assert.False(await client.FlushAsync());
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Counts_are_sent_as_one_batch_with_the_context_and_then_cleared()
    {
        using var client = Client();
        client.Enable("install-1");
        client.Track("app_start");
        client.Track("tool:Paintbrush");
        client.Track("tool:Paintbrush");
        _now = _now.AddMinutes(15);

        Assert.True(await client.FlushAsync());
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Endpoint, request.RequestUri);
        Assert.Equal("CinnabarSharp/0.9.5", request.Headers.UserAgent.ToString());

        using var json = JsonDocument.Parse(_handler.Bodies[0]);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal("install-1", root.GetProperty("installId").GetString());
        Assert.Equal(32, root.GetProperty("sessionId").GetString()!.Length);
        Assert.Equal("0.9.5", root.GetProperty("appVersion").GetString());
        Assert.Equal("macos", root.GetProperty("os").GetString());
        Assert.Equal("15.2", root.GetProperty("osVersion").GetString());
        Assert.Equal("arm64", root.GetProperty("arch").GetString());
        Assert.Equal("fr-FR", root.GetProperty("locale").GetString());
        Assert.Equal("2026-10-08T10:00:00Z", root.GetProperty("from").GetString());
        Assert.Equal("2026-10-08T10:15:00Z", root.GetProperty("to").GetString());
        var events = root.GetProperty("events");
        Assert.Equal(1, events.GetProperty("app_start").GetInt32());
        Assert.Equal(2, events.GetProperty("tool:Paintbrush").GetInt32());

        Assert.Empty(client.Pending);
        Assert.False(await client.FlushAsync());        // nothing new: no request
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task A_failed_batch_is_kept_for_the_next_one()
    {
        using var client = Client();
        client.Enable("install-1");
        client.Track("menu:File/Save");
        _handler.Offline = true;
        Assert.False(await client.FlushAsync());
        _handler.Offline = false;
        _handler.Status = HttpStatusCode.ServiceUnavailable;
        client.Track("menu:File/Save");
        Assert.False(await client.FlushAsync());
        Assert.Equal(2, client.Pending["menu:File/Save"]);

        _handler.Status = HttpStatusCode.OK;
        _now = _now.AddHours(1);
        Assert.True(await client.FlushAsync());
        using var json = JsonDocument.Parse(_handler.Bodies[^1]);
        Assert.Equal(2, json.RootElement.GetProperty("events").GetProperty("menu:File/Save").GetInt32());
        Assert.Equal("2026-10-08T10:00:00Z", json.RootElement.GetProperty("from").GetString());
    }

    [Fact]
    public async Task Disabling_forgets_what_was_not_sent()
    {
        using var client = Client();
        client.Enable("install-1");
        client.Track("app_start");
        client.Disable();
        Assert.False(client.IsEnabled);
        Assert.Empty(client.Pending);
        client.Track("app_start");
        Assert.False(await client.FlushAsync());
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Without_an_acceptable_endpoint_nothing_is_sent()
    {
        using var client = new TelemetryClient(new Uri("http://telemetry.example.com/"), Context, _handler);
        Assert.Null(client.Endpoint);
        client.Enable("install-1");
        client.Track("app_start");
        Assert.False(await client.FlushAsync());
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData("https://telemetry.example.com/", true)]
    [InlineData("http://localhost:8787/", true)]
    [InlineData("http://127.0.0.1:8787/", true)]
    [InlineData("http://telemetry.example.com/", false)]
    [InlineData("ftp://telemetry.example.com/", false)]
    public void Only_https_or_a_local_relay_is_accepted(string url, bool allowed) =>
        Assert.Equal(allowed, TelemetryClient.IsAllowedEndpoint(new Uri(url)));

    [Theory]
    [InlineData("app_start", true)]
    [InlineData("tool:Paintbrush", true)]
    [InlineData("menu:Effects/Blurs/Gaussian Blur", true)]
    [InlineData("menu:Edit/Copy & Paste", true)]
    [InlineData("open:jpg", true)]
    [InlineData("", false)]
    [InlineData("Tool:Paintbrush", false)]
    [InlineData("open:/Users/me/holiday.jpg", false)]
    [InlineData("open:C:\\Users\\me\\holiday.jpg", false)]
    [InlineData("text:hello: world", false)]
    [InlineData("menu:", false)]
    public void Names_that_could_carry_user_data_are_refused(string name, bool valid) =>
        Assert.Equal(valid, TelemetryClient.IsValidName(name));

    [Fact]
    public void Invalid_names_are_never_counted()
    {
        using var client = Client();
        client.Enable("install-1");
        client.Track("open:/Users/me/holiday.jpg");
        client.Track(new string('a', 81));
        Assert.Empty(client.Pending);
    }

    [Theory]
    [InlineData("menu", "File/Save As…", "menu:File/Save As")]
    [InlineData("menu", "View/Allow AI Agents (MCP)", "menu:View/Allow AI Agents MCP")]
    [InlineData("tool", "Rectangle Select", "tool:Rectangle Select")]
    [InlineData("effect", "  ", "effect")]
    [InlineData("menu", "/Zoom: 100 %", "menu:Zoom 100")]
    public void Names_are_built_from_labels(string kind, string label, string expected)
    {
        var name = TelemetryClient.Name(kind, label);
        Assert.Equal(expected, name);
        Assert.True(TelemetryClient.IsValidName(name));
    }

    [Fact]
    public void The_number_of_distinct_names_is_bounded()
    {
        using var client = Client();
        client.Enable("install-1");
        for (var i = 0; i < TelemetryClient.MaxNames + 10; i++)
            client.Track($"tool:T{i}");
        Assert.Equal(TelemetryClient.MaxNames, client.Pending.Count);
    }
}
