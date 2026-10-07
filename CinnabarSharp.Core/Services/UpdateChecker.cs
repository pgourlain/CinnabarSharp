using System.Net.Http.Headers;
using System.Text.Json;

namespace CinnabarSharp.Core.Services;

/// <summary>A release newer than the running app.</summary>
/// <param name="Version">Like "0.9.1" (no leading v).</param>
/// <param name="Url">The release page on GitHub: the only address the app ever opens for an update.</param>
/// <param name="Notes">The release notes (markdown), possibly empty.</param>
public sealed record UpdateInfo(string Version, string Url, string Notes);

/// <summary>The check could not be done (offline, GitHub unreachable or limiting requests, unexpected answer).</summary>
public sealed class UpdateCheckException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Asks GitHub for the latest published release (pre-releases and drafts are not "latest") and compares it with the running
/// version. One GET, no identifier, nothing about the user or the machine but the User-Agent "CinnabarSharp/x.y.z". It never
/// downloads or installs anything: the app opens the release page and the user decides.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/pgourlain/CinnabarSharp/releases/latest";
    public const string ReleasePagePrefix = "https://github.com/pgourlain/CinnabarSharp/";

    private readonly HttpClient _http;
    private readonly string _url;

    public UpdateChecker(HttpMessageHandler? handler = null, string url = LatestReleaseUrl)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(15);
        _url = url;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>The newer release, or null when <paramref name="current"/> is the latest (or newer).</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken cancellation = default)
    {
        string json;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CinnabarSharp", $"{current.Major}.{current.Minor}.{Math.Max(current.Build, 0)}"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new UpdateCheckException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            throw new UpdateCheckException("GitHub could not be reached.", e);
        }
        return Parse(json, current);
    }

    /// <summary>Reads GitHub's "latest release" JSON; public for tests.</summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } name)
                throw new UpdateCheckException("The answer has no release.");
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True
                || root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                return null;
            if (!TryParseVersion(name, out var latest))
                throw new UpdateCheckException($"Unrecognized version '{name}'.");
            if (latest <= Normalize(current))
                return null;
            var url = root.TryGetProperty("html_url", out var html) ? html.GetString() : null;
            // Never follow an address from the answer unless it is this project's release page.
            if (url is null || !url.StartsWith(ReleasePagePrefix, StringComparison.Ordinal))
                url = $"{ReleasePagePrefix}releases/tag/{Uri.EscapeDataString(name)}";
            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            return new UpdateInfo(latest.ToString(3), url, notes);
        }
        catch (JsonException e)
        {
            throw new UpdateCheckException("The answer was not understood.", e);
        }
    }

    /// <summary>"v0.9.1", "0.9.1" or "0.9.1-rc1+abc" → 0.9.1.</summary>
    public static bool TryParseVersion(string text, out Version version)
    {
        text = text.Trim().TrimStart('v', 'V');
        var end = text.IndexOfAny(['-', '+']);
        if (end >= 0)
            text = text[..end];
        if (Version.TryParse(text, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    private static Version Normalize(Version v) => new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
