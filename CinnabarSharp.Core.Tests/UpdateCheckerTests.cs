using System.Net;
using CinnabarSharp.Core.Services;

namespace CinnabarSharp.Core.Tests;

public class UpdateCheckerTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static string Release(string tag, bool prerelease = false, bool draft = false, string? url = null) =>
        $$"""{"tag_name":"{{tag}}","html_url":"{{url ?? "https://github.com/pgourlain/CinnabarSharp/releases/tag/" + tag}}","prerelease":{{prerelease.ToString().ToLowerInvariant()}},"draft":{{draft.ToString().ToLowerInvariant()}},"body":"Notes"}""";

    private static Task<UpdateInfo?> Check(string json, string current = "0.9.0", HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new Handler(_ => new HttpResponseMessage(status) { Content = new StringContent(json) });
        return new UpdateChecker(handler).CheckAsync(Version.Parse(current));
    }

    [Fact]
    public async Task A_newer_release_is_reported_with_its_page_and_notes()
    {
        var update = await Check(Release("v0.9.1"));
        Assert.Equal("0.9.1", update!.Version);
        Assert.Equal("https://github.com/pgourlain/CinnabarSharp/releases/tag/v0.9.1", update.Url);
        Assert.Equal("Notes", update.Notes);
    }

    [Theory]
    [InlineData("v0.9.0", "0.9.0")]
    [InlineData("v0.8.1", "0.9.0")]
    [InlineData("v0.9.0", "0.9.0.0")]
    [InlineData("v1.0.0", "1.0.0")]
    public async Task The_same_or_an_older_release_is_not_an_update(string tag, string current) =>
        Assert.Null(await Check(Release(tag), current));

    [Theory]
    [InlineData("v0.10.0", "0.9.9", true)]
    [InlineData("v0.9.10", "0.9.9", true)]
    [InlineData("v1.0.0-rc1", "0.9.9", true)]   // a tag suffix is ignored (GitHub's own prerelease flag decides)
    [InlineData("0.9.1", "0.9.0", true)]
    public async Task Versions_are_compared_by_number(string tag, string current, bool newer) =>
        Assert.Equal(newer, await Check(Release(tag), current) is not null);

    [Fact]
    public async Task Pre_releases_and_drafts_are_ignored()
    {
        Assert.Null(await Check(Release("v0.9.1", prerelease: true)));
        Assert.Null(await Check(Release("v0.9.1", draft: true)));
    }

    [Fact]
    public async Task An_address_outside_the_project_is_never_returned()
    {
        var update = await Check(Release("v0.9.1", url: "https://evil.example/download.exe"));
        Assert.StartsWith(UpdateChecker.ReleasePagePrefix, update!.Url);
    }

    [Fact]
    public async Task Failures_are_reported_as_update_check_exceptions()
    {
        await Assert.ThrowsAsync<UpdateCheckException>(() => Check("{}", status: HttpStatusCode.Forbidden));
        await Assert.ThrowsAsync<UpdateCheckException>(() => Check("not json"));
        await Assert.ThrowsAsync<UpdateCheckException>(() => Check("{\"nothing\":1}"));
        await Assert.ThrowsAsync<UpdateCheckException>(() => Check(Release("latest")));
        var offline = new Handler(_ => throw new HttpRequestException("offline"));
        await Assert.ThrowsAsync<UpdateCheckException>(() => new UpdateChecker(offline).CheckAsync(new Version(0, 9, 0)));
    }

    [Fact]
    public async Task The_request_is_a_plain_get_that_says_only_the_app_and_its_version()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release("v0.9.0")) });
        await new UpdateChecker(handler).CheckAsync(new Version(0, 9, 0));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(UpdateChecker.LatestReleaseUrl, request.RequestUri!.ToString());
        Assert.Equal("CinnabarSharp/0.9.0", request.Headers.UserAgent.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.Empty(request.Headers.GetValues("Accept").Where(a => !a.Contains("github")));
    }
}
