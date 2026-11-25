/*
ImageGlass - A Fast, Seamless Photo Viewer
Copyright (C) 2010 - 2026 DUONG DIEU PHAP
Project homepage: https://imageglass.org

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace ImageGlass.Common.ServiceProviders.Update;

internal static class IntelGitHubReleases
{
    public const string Channel = "github-intel";

    private const string Repository = "FarMC666/ImageGlass-macOS";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    private const string ReleaseTagUrl = ReleasesUrl + "/tag/";
    private const string ApiUrl = "https://api.github.com/repos/" + Repository + "/releases";
    private const string TagSeparator = "-mac-x64.";
    public const string IssuesUrl = "https://github.com/" + Repository + "/issues";

    public static string FormatTag(string version, int sequence) =>
        version + TagSeparator + sequence.ToString(CultureInfo.InvariantCulture);

    public static async Task<UpdateCheckResult> CheckAsync(HttpClient client, string appVersion, int sequence,
        string? skippedVersion, CancellationToken ct)
    {
        var current = (Version.Parse(appVersion), sequence);
        if (sequence <= 0) return UpdateCheckResult.Failed("Invalid Intel release sequence.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(UpdateConstants.MetadataTimeout);
        UpdateReleaseInfo? release;
        try
        {
            release = await CheckRestApiAsync(client, current, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            release = await CheckAtomFeedAsync(client, current, timeout.Token).ConfigureAwait(false);
        }

        if (release is null || release.Version == FormatTag(appVersion, sequence))
        {
            return UpdateCheckResult.NoUpdate(release);
        }
        if (release.Version == skippedVersion) return UpdateCheckResult.NoUpdate();

        return UpdateCheckResult.ForIntelMac(release, release.UpdateUrl);
    }

    private static async Task<UpdateReleaseInfo?> CheckRestApiAsync(HttpClient client,
        (Version Version, int Sequence) current, CancellationToken ct)
    {
        var latest = current;
        UpdateReleaseInfo? release = null;
        var page = 1;
        bool hasNextPage;
        do
        {
            using var request = CreateRequest(HttpMethod.Get,
                ApiUrl + "?per_page=30&page=" + page.ToString(CultureInfo.InvariantCulture));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(UpdateConstants.MaxMetadataSize, ct).ConfigureAwait(false);
            using var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync(content, IntelGitHubJsonContext.Default.GitHubReleaseArray,
                ct).ConfigureAwait(false);
            if (releases is null) throw new JsonException("Invalid GitHub Releases response.");

            foreach (var item in releases)
            {
                if (item is not { Draft: false, PublishedAt: { } publishedAt, Tag: { } tag, Assets: { } assets }
                    || !TryParseTag(tag, out var version)
                    || version.CompareTo(latest) < 0)
                {
                    continue;
                }

                var fileName = GetInstallerFileName(version.Version);
                var downloadUrl = GetDownloadUrl(tag, fileName);
                if (!assets.Any(asset => asset is { State: "uploaded", Size: > 0 }
                    && asset.Name == fileName && asset.DownloadUrl == downloadUrl))
                {
                    continue;
                }

                latest = version;
                release = new UpdateReleaseInfo
                {
                    Version = tag,
                    Title = item.Name ?? string.Empty,
                    PublishedDate = publishedAt.ToLocalTime().ToString("d"),
                    ChangelogUrl = ReleaseTagUrl + tag,
                    UpdateUrl = downloadUrl,
                };
            }

            // Request the next page from the same API endpoint without following a URL from the response.
            hasNextPage = response.Headers.TryGetValues("Link", out var links)
                && links.SelectMany(link => link.Split(',')).Any(link => link.TrimEnd().EndsWith("; rel=\"next\"", StringComparison.Ordinal));
            page++;
        }
        while (hasNextPage);

        return release;
    }

    private static async Task<UpdateReleaseInfo?> CheckAtomFeedAsync(HttpClient client,
        (Version Version, int Sequence) current, CancellationToken ct)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        using var request = CreateRequest(HttpMethod.Get, ReleasesUrl + ".atom");
        request.Headers.Accept.ParseAdd("application/atom+xml");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await response.Content.LoadIntoBufferAsync(UpdateConstants.MaxMetadataSize, ct).ConfigureAwait(false);
        using var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = XmlReader.Create(content, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = UpdateConstants.MaxMetadataSize,
        });
        var doc = await XDocument.LoadAsync(reader, LoadOptions.None, ct).ConfigureAwait(false);
        if (doc.Root is not { } root || root.Name != atom + "feed")
        {
            throw new InvalidDataException("Invalid GitHub Releases feed.");
        }

        var candidates = root.Elements(atom + "entry").SelectMany(entry => entry.Elements(atom + "link")
            .Where(link => (string?)link.Attribute("rel") == "alternate")
            .Select(link => (Href: (string?)link.Attribute("href"), Title: (string?)entry.Element(atom + "title"))));

        var releases = new SortedDictionary<(Version Version, int Sequence), UpdateReleaseInfo>();
        foreach (var entry in candidates)
        {
            if (entry.Href is not { } href || !href.StartsWith(ReleaseTagUrl, StringComparison.Ordinal))
            {
                continue;
            }

            var tag = href[ReleaseTagUrl.Length..];
            if (!TryParseTag(tag, out var version) || version.CompareTo(current) < 0)
            {
                continue;
            }

            releases.TryAdd(version, new UpdateReleaseInfo
            {
                Version = tag,
                Title = entry.Title ?? string.Empty,
                ChangelogUrl = ReleaseTagUrl + tag,
                UpdateUrl = GetDownloadUrl(tag, GetInstallerFileName(version.Version)),
            });
        }

        foreach (var (version, release) in releases.Reverse())
        {
            using var assetRequest = CreateRequest(HttpMethod.Head, release.UpdateUrl);
            using var assetResponse = await client.SendAsync(assetRequest, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (assetResponse.StatusCode == HttpStatusCode.NotFound) continue;
            assetResponse.EnsureSuccessStatusCode();

            var disposition = assetResponse.Content.Headers.ContentDisposition;
            var fileName = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
            if (assetResponse.StatusCode != HttpStatusCode.OK || assetResponse.Content.Headers.ContentLength is not > 0
                || disposition?.DispositionType != "attachment" || fileName != GetInstallerFileName(version.Version))
            {
                throw new InvalidDataException("Invalid response for the GitHub Intel DMG.");
            }

            return release;
        }

        return null;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd("ImageGlass-Intel-Mac");
        return request;
    }

    private static string GetInstallerFileName(Version version) => $"ImageGlass_{version}_mac-x64.dmg";

    private static string GetDownloadUrl(string tag, string fileName) => $"{ReleasesUrl}/download/{tag}/{fileName}";

    private static bool TryParseTag(string tag, out (Version Version, int Sequence) value)
    {
        value = default;
        var separator = tag.IndexOf(TagSeparator, StringComparison.Ordinal);
        if (separator < 0 || !Version.TryParse(tag.AsSpan(0, separator), out var version) || version.Revision < 0
            || !int.TryParse(tag.AsSpan(separator + TagSeparator.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            || sequence <= 0 || tag != FormatTag(version.ToString(), sequence))
        {
            return false;
        }

        value = (version, sequence);
        return true;
    }
}

[JsonSerializable(typeof(GitHubRelease[]))]
internal partial class IntelGitHubJsonContext : JsonSerializerContext;

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? Tag { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("draft")]
    public bool? Draft { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public GitHubAsset?[]? Assets { get; set; }
}

internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? DownloadUrl { get; set; }
}
