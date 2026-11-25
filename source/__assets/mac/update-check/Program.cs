using ImageGlass.Common.ServiceProviders.Update;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

const string appVersion = "10.0.6.906";
const string current = appVersion + "-mac-x64.2";
const string newer = appVersion + "-mac-x64.3";
const string highestSequence = appVersion + "-mac-x64.12";
const string higherVersion = "10.0.7.1-mac-x64.1";
const string releases = "https://github.com/FarMC666/ImageGlass-macOS/releases";

var available = await CheckAsync(
    [Page(Release(newer), Release(current), Release(appVersion + "-mac-x64.1"))]);
Require(available is { Status: UpdateCheckStatus.UpdateAvailable } && available.Release?.Version == newer
    && available.DownloadUrl == $"{releases}/download/{newer}/ImageGlass_{appVersion}_mac-x64.dmg",
    "An Intel prerelease with a new sequence supplies its canonical DMG URL.");

await ExpectAsync(UpdateCheckStatus.NoUpdate, [Page(Release(current), Release(appVersion + "-mac-x64.1"))]);
await ExpectAsync(UpdateCheckStatus.NoUpdate, [Page(Release(appVersion + "-mac-x64.1"))]);
await ExpectAsync(UpdateCheckStatus.NoUpdate, [Page(Release(newer))], newer);
await ExpectAsync(UpdateCheckStatus.UpdateAvailable, [Page(Release(newer))], current);
await ExpectAsync(UpdateCheckStatus.NoUpdate,
    [Page(Release(newer, draft: true),
        Release(newer, published: false),
        Release(appVersion + "-mac-x64.003"),
        Release(appVersion + "-mac-x64.2147483648"),
        Release(appVersion + "-mac-x64.0"),
        Release("10.0.7-mac-x64.1"),
        Release(appVersion + "-mac-arm64.3"),
        Release(newer, file: $"ImageGlass_{appVersion}_mac-arm64.dmg"),
        Release(newer, file: "source.zip"),
        Release(newer, host: "https://github.com/d2phap/ImageGlass/releases"),
        Release(newer, host: "https://example.org/releases"),
        Release(newer, state: "new"),
        Release(newer, size: 0))]);

var sorted = await CheckAsync([Page(Release(newer)), Page(Release(highestSequence))]);
Require(sorted.Release?.Version == highestSequence, "Numeric sequence ordering across pages.");
sorted = await CheckAsync([Page(Release(higherVersion)), Page(Release(highestSequence))]);
Require(sorted.Release?.Version == higherVersion, "Application version takes precedence over the sequence.");

const string installer = "ImageGlass_10.0.6.906_mac-x64.dmg";
var missing = new AtomResponse(HttpMethod.Head, Download(highestSequence), HttpStatusCode.NotFound);
var uploaded = new AtomResponse(HttpMethod.Head, Download(newer), HttpStatusCode.OK, FileName: installer);
using (var fallback = new AtomFixtureHandler(
    [
        RateLimit(HttpStatusCode.Forbidden),
        Atom(Entry(newer), Entry(highestSequence), Entry(appVersion + "-mac-x64.20", "https://example.org"),
            Entry(appVersion + "-mac-arm64.20"), Entry(appVersion + "-mac-x64.30?download=1")),
        missing,
        uploaded,
    ]))
{
    using var client = new HttpClient(fallback);
    var result = await IntelGitHubReleases.CheckAsync(client, appVersion, 2, null, CancellationToken.None);
    Require(result.Status == UpdateCheckStatus.UpdateAvailable && result.Release?.Version == newer
        && result.DownloadUrl == Download(newer), "HTTP 403 uses the Atom feed and a verified Intel DMG.");
    Require(fallback.Complete, "The fallback ignores foreign links and checks Intel candidates in version order.");
}

using (var fallback = new AtomFixtureHandler([RateLimit(HttpStatusCode.TooManyRequests), Atom(Entry(newer)), uploaded]))
{
    using var client = new HttpClient(fallback);
    var result = await IntelGitHubReleases.CheckAsync(client, appVersion, 2, newer, CancellationToken.None);
    Require(result.Status == UpdateCheckStatus.NoUpdate && result.DownloadUrl is null && fallback.Complete,
        "HTTP 429 falls back and respects the skipped release.");
}

using (var fallback = new AtomFixtureHandler([RateLimit(HttpStatusCode.Forbidden), Atom(Entry(
    appVersion + "-mac-x64.1"), Entry(appVersion + "-mac-arm64.8"))]))
{
    using var client = new HttpClient(fallback);
    var result = await IntelGitHubReleases.CheckAsync(client, appVersion, 2, null, CancellationToken.None);
    Require(result.Status == UpdateCheckStatus.NoUpdate && result.DownloadUrl is null && fallback.Complete,
        "The fallback offers neither downgrades nor ARM64 installers.");
}

using (var fallback = new AtomFixtureHandler(
    [RateLimit(HttpStatusCode.Forbidden), Atom(Entry(newer)), uploaded with { FileName = "other.dmg" }]))
{
    using var client = new HttpClient(fallback);
    try
    {
        await IntelGitHubReleases.CheckAsync(client, appVersion, 2, null, CancellationToken.None);
        throw new InvalidOperationException("An unexpected asset was accepted.");
    }
    catch (InvalidDataException)
    {
        Require(fallback.Complete, "The fallback rejects a mismatched attachment filename.");
    }
}

using (var fallback = new AtomFixtureHandler(
    [RateLimit(HttpStatusCode.Forbidden), Atom() with { Payload = "<html/>" }]))
{
    using var client = new HttpClient(fallback);
    try
    {
        await IntelGitHubReleases.CheckAsync(client, appVersion, 2, null, CancellationToken.None);
        throw new InvalidOperationException("A malformed feed was accepted.");
    }
    catch (InvalidDataException)
    {
        Require(fallback.Complete, "An invalid fallback feed fails the check.");
    }
}

if (args is ["--live"])
{
    using var client = new HttpClient(new LiveTraceHandler());
    var observed = await IntelGitHubReleases.CheckAsync(client, appVersion, 1, null, CancellationToken.None);
    Require(observed.Status == UpdateCheckStatus.UpdateAvailable && observed.DownloadUrl is not null,
        $"Live GitHub lookup found {observed.Release?.Version} at {observed.DownloadUrl}.");
}
else if (args.Length > 0)
{
    var observed = await CheckAsync([await File.ReadAllTextAsync(args[0])]);
    Console.WriteLine($"Recorded public metadata: {observed.Status} ({observed.Release?.Version ?? "no current or newer release"})");
    Require(observed.Status != UpdateCheckStatus.CheckFailed, "Recorded GitHub metadata is readable.");
}

static string Entry(string tag, string root = releases) =>
    $"<entry><link rel=\"alternate\" href=\"{root}/tag/{tag}\"/><title>Intel update</title></entry>";

static string Download(string tag) => $"{releases}/download/{tag}/{installer}";

static AtomResponse Atom(params string[] entries) => new(HttpMethod.Get, releases + ".atom", HttpStatusCode.OK,
    $"<feed xmlns=\"http://www.w3.org/2005/Atom\">{string.Concat(entries)}</feed>");

static AtomResponse RateLimit(HttpStatusCode status) => new(HttpMethod.Get,
    "https://api.github.com/repos/FarMC666/ImageGlass-macOS/releases?per_page=30&page=1", status);

static string Page(params string[] releases) => "[" + string.Join(',', releases) + "]";

static string Release(string tag, bool draft = false, bool published = true, string? file = null, string? host = null,
    string state = "uploaded", int size = 50)
{
    file ??= $"ImageGlass_{tag.Split('-')[0]}_mac-x64.dmg";
    host ??= releases;
    var publishedAt = published ? "\"2026-11-24T13:36:58Z\"" : "null";
    return $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Intel preview",
          "prerelease": true,
          "draft": {{(draft ? "true" : "false")}},
          "published_at": {{publishedAt}},
          "assets": [
            {
              "name": "{{file}}",
              "state": "{{state}}",
              "size": {{size}},
              "browser_download_url": "{{host}}/download/{{tag}}/{{file}}"
            }
          ]
        }
        """;
}

static async Task<UpdateCheckResult> CheckAsync(string[] pages, string? skipped = null)
{
    using var handler = new FixtureHandler(pages);
    using var client = new HttpClient(handler);
    var result = await IntelGitHubReleases.CheckAsync(client, appVersion, 2, skipped, CancellationToken.None);
    Require(handler.Requests == pages.Length, "Every page was requested exactly once.");
    return result;
}

static async Task ExpectAsync(UpdateCheckStatus expected, string[] pages, string? skipped = null)
{
    var actual = await CheckAsync(pages, skipped);
    Require(actual.Status == expected, $"Expected {expected}, got {actual.Status}.");
    if (expected == UpdateCheckStatus.NoUpdate)
    {
        Require(actual.DownloadUrl is null, "An up-to-date build has no recommended installer.");
    }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}

file sealed class FixtureHandler(string[] pages) : HttpMessageHandler
{
    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var index = Requests++;
        var expectedUrl = $"https://api.github.com/repos/FarMC666/ImageGlass-macOS/releases?per_page=30&page={index + 1}";
        if (request.RequestUri?.AbsoluteUri != expectedUrl || request.Headers.Authorization is not null
            || request.Headers.UserAgent.ToString() != "ImageGlass-Intel-Mac")
        {
            throw new InvalidOperationException("Unexpected Intel update request.");
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(pages[index], Encoding.UTF8, "application/json"),
        };
        if (index + 1 < pages.Length)
        {
            response.Headers.Add("Link", "<https://example.org/irrelevant>; rel=\"next\"");
        }
        return Task.FromResult(response);
    }
}

file sealed record AtomResponse(HttpMethod Method, string Url, HttpStatusCode Status, string? Payload = null,
    string? FileName = null);

file sealed class AtomFixtureHandler(AtomResponse[] responses) : HttpMessageHandler
{
    private int _index;
    public bool Complete => _index == responses.Length;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var expected = responses[_index++];
        if (request.Method != expected.Method || request.RequestUri?.AbsoluteUri != expected.Url
            || request.Headers.Authorization is not null)
        {
            throw new InvalidOperationException("Unexpected fallback request.");
        }

        var response = new HttpResponseMessage(expected.Status)
        {
            Content = new StringContent(expected.Payload ?? string.Empty),
        };
        if (expected.FileName is not null)
        {
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = expected.FileName,
            };
            response.Content.Headers.ContentLength = 250;
        }
        return Task.FromResult(response);
    }
}

file sealed class LiveTraceHandler() : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri;
        var response = await base.SendAsync(request, cancellationToken);
        Console.WriteLine($"{request.Method} {url} -> {(int)response.StatusCode}");
        return response;
    }
}
