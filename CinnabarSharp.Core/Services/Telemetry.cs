using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CinnabarSharp.Core.Services;

/// <summary>
/// Anonymous usage counts ("tool:Paintbrush" used 12 times). Callers only name what happened; whether anything is ever
/// sent, and where, is the implementation's business. Never throws.
/// </summary>
public interface ITelemetry
{
    void Track(string name);
}

/// <summary>What every batch says about the running app; nothing about the user or their files.</summary>
/// <param name="AppVersion">Like "0.9.5".</param>
/// <param name="Os">"macos", "windows" or "linux".</param>
/// <param name="OsVersion">Major.minor of the system, like "15.2".</param>
/// <param name="Architecture">"arm64", "x64"…</param>
/// <param name="Locale">The UI culture, like "fr-FR".</param>
public sealed record TelemetryContext(string AppVersion, string Os, string OsVersion, string Architecture, string Locale)
{
    public static TelemetryContext ForCurrentProcess(Version appVersion) => new(
        appVersion.ToString(3),
        OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other",
        $"{Environment.OSVersion.Version.Major}.{Math.Max(Environment.OSVersion.Version.Minor, 0)}",
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
        CultureInfo.CurrentUICulture.Name);
}

/// <summary>
/// Counts events in memory and posts them in batches to one HTTPS endpoint (docs/telemetry.md describes the request).
/// <list type="bullet">
/// <item>Off until <see cref="Enable"/> (the user agreed); <see cref="Disable"/> drops what was not sent.</item>
/// <item>Without an endpoint it never sends anything.</item>
/// <item>Only counts by event name are sent: names are checked against <see cref="IsValidName"/>, so a file name, a
/// path or typed text can't slip through by mistake.</item>
/// <item>A batch that could not be sent is kept for the next one (up to <see cref="MaxNames"/> names).</item>
/// </list>
/// </summary>
public sealed partial class TelemetryClient : ITelemetry, IDisposable
{
    public const int MaxNames = 500;
    public const int SchemaVersion = 1;

    private readonly Uri? _endpoint;
    private readonly TelemetryContext _context;
    private readonly HttpClient _http;
    private readonly Func<DateTime> _utcNow;
    private readonly Lock _lock = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private DateTime _since;
    private string? _installId;
    private Timer? _timer;

    public TelemetryClient(Uri? endpoint, TelemetryContext context, HttpMessageHandler? handler = null, Func<DateTime>? utcNow = null)
    {
        _endpoint = endpoint is not null && IsAllowedEndpoint(endpoint) ? endpoint : null;
        _context = context;
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(10);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _since = _utcNow();
    }

    /// <summary>The endpoint given to the constructor, if it is acceptable (HTTPS, or HTTP to this machine for testing).</summary>
    public Uri? Endpoint => _endpoint;

    /// <summary>Whether the user agreed (and so whether <see cref="Track"/> counts anything).</summary>
    public bool IsEnabled => _installId is not null;

    /// <summary>Starts counting; <paramref name="installId"/> is a random id kept by the app, not derived from the machine.</summary>
    public void Enable(string installId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installId);
        lock (_lock)
            _installId = installId;
    }

    /// <summary>Stops counting and forgets what was not sent.</summary>
    public void Disable()
    {
        lock (_lock)
        {
            _installId = null;
            _counts.Clear();
        }
        StopPeriodicFlush();
    }

    public void Track(string name)
    {
        if (!IsValidName(name))
            return;
        lock (_lock)
        {
            if (_installId is null)
                return;
            if (_counts.TryGetValue(name, out var count))
                _counts[name] = count + 1;
            else if (_counts.Count < MaxNames)
                _counts[name] = 1;
        }
    }

    /// <summary>The counts not sent yet (for tests and the documentation).</summary>
    public IReadOnlyDictionary<string, int> Pending
    {
        get
        {
            lock (_lock)
                return new Dictionary<string, int>(_counts, StringComparer.Ordinal);
        }
    }

    /// <summary>Sends <see cref="FlushAsync"/> every <paramref name="interval"/> in the background until disabled or disposed.</summary>
    public void StartPeriodicFlush(TimeSpan interval)
    {
        StopPeriodicFlush();
        _timer = new Timer(_ => _ = FlushAsync(), null, interval, interval);
    }

    private void StopPeriodicFlush()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Posts the pending counts as one batch. True when a batch was accepted, false when there was nothing to send, no
    /// endpoint, no consent, or the request failed (the counts are then kept for the next batch). Never throws.
    /// </summary>
    public async Task<bool> FlushAsync(CancellationToken cancellation = default)
    {
        Dictionary<string, int> batch;
        string installId;
        DateTime from;
        var to = _utcNow();
        lock (_lock)
        {
            if (_endpoint is null || _installId is null || _counts.Count == 0)
                return false;
            (batch, _counts) = (_counts, new Dictionary<string, int>(StringComparer.Ordinal));
            (from, _since) = (_since, to);
            installId = _installId;
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(BuildPayload(installId, _sessionId, _context, from, to, batch), Encoding.UTF8, "application/json"),
            };
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CinnabarSharp", _context.AppVersion));
            using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
        }
        lock (_lock)
        {
            // Put the batch back unless the user turned statistics off meanwhile.
            if (_installId is not null)
            {
                foreach (var (name, count) in batch)
                {
                    if (_counts.TryGetValue(name, out var existing))
                        _counts[name] = existing + count;
                    else if (_counts.Count < MaxNames)
                        _counts[name] = count;
                }
                if (from < _since)
                    _since = from;
            }
        }
        return false;
    }

    /// <summary>The JSON body of one batch (docs/telemetry.md); public for tests.</summary>
    public static string BuildPayload(string installId, string sessionId, TelemetryContext context, DateTime from, DateTime to,
        IReadOnlyDictionary<string, int> counts)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", SchemaVersion);
            json.WriteString("installId", installId);
            json.WriteString("sessionId", sessionId);
            json.WriteString("appVersion", context.AppVersion);
            json.WriteString("os", context.Os);
            json.WriteString("osVersion", context.OsVersion);
            json.WriteString("arch", context.Architecture);
            json.WriteString("locale", context.Locale);
            json.WriteString("from", from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            json.WriteString("to", to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            json.WriteStartObject("events");
            foreach (var (name, count) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
                json.WriteNumber(name, count);
            json.WriteEndObject();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// "kind" or "kind:detail": a lowercase kind, then letters, digits, spaces and <c>. _ - / &amp;</c>, at most 80
    /// characters. No colon or backslash in the detail and no leading slash, so an absolute path can't pass; names are
    /// built from the app's own labels (menus, tools, effects, format names), never from the user's data.
    /// </summary>
    public static bool IsValidName(string? name) => name is { Length: > 0 and <= 80 } && NamePattern().IsMatch(name);

    [GeneratedRegex(@"^[a-z][a-z_]*(:[A-Za-z0-9][A-Za-z0-9 ._&/-]*)?$")]
    private static partial Regex NamePattern();

    /// <summary>
    /// "kind:detail" from an app label: characters a name can't hold are dropped ("Allow AI Agents (MCP)…" →
    /// "Allow AI Agents MCP"), spaces collapsed, cut to fit.
    /// </summary>
    public static string Name(string kind, string detail)
    {
        var text = new StringBuilder(detail.Length);
        foreach (var c in detail)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '&' or '/' or '-')
                text.Append(c);
            else if (char.IsWhiteSpace(c) && text.Length > 0 && text[^1] != ' ')
                text.Append(' ');
        }
        var cleaned = text.ToString().TrimStart('.', '_', '&', '/', '-', ' ').TrimEnd();
        var name = cleaned.Length == 0 ? kind : $"{kind}:{cleaned}";
        return name.Length <= 80 ? name : name[..80].TrimEnd();
    }

    /// <summary>HTTPS anywhere, plain HTTP only to this machine (a local relay while testing).</summary>
    public static bool IsAllowedEndpoint(Uri endpoint) =>
        endpoint.IsAbsoluteUri && (endpoint.Scheme == Uri.UriSchemeHttps || endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback);

    public void Dispose()
    {
        StopPeriodicFlush();
        _http.Dispose();
    }
}
