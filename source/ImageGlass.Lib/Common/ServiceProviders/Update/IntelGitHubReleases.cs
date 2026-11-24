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
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ImageGlass.Common.ServiceProviders.Update;

internal static class IntelGitHubReleases
{
    public const string Channel = "github-intel";

    private const string Repository = "FarMC666/ImageGlass-macOS";
    private const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
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

        (Version Version, int Sequence) latest = current;
        UpdateReleaseInfo? release = null;
        var page = 1;
        bool hasNextPage;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(UpdateConstants.MetadataTimeout);
        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                ApiUrl + "?per_page=30&page=" + page.ToString(CultureInfo.InvariantCulture));
            request.Headers.UserAgent.ParseAdd("ImageGlass-Intel-Mac");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(UpdateConstants.MaxMetadataSize, timeout.Token).ConfigureAwait(false);
            using var content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync(content, IntelGitHubJsonContext.Default.GitHubReleaseArray,
                timeout.Token).ConfigureAwait(false);
            if (releases is null) return UpdateCheckResult.Failed("Invalid GitHub Releases response.");

            foreach (var item in releases)
            {
                if (item is not { Draft: false, PublishedAt: { } publishedAt, Tag: { } tag, Assets: { } assets }
                    || !TryParseTag(tag, out var version)
                    || version.CompareTo(latest) < 0)
                {
                    continue;
                }

                var fileName = $"ImageGlass_{version.Version}_mac-x64.dmg";
                var downloadUrl = $"{ReleasesUrl}/download/{tag}/{fileName}";
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
                    ChangelogUrl = $"{ReleasesUrl}/tag/{tag}",
                    UpdateUrl = downloadUrl,
                };
            }

            // Request the next page from the same API endpoint without following a URL from the response.
            hasNextPage = response.Headers.TryGetValues("Link", out var links)
                && links.SelectMany(link => link.Split(',')).Any(link => link.TrimEnd().EndsWith("; rel=\"next\"", StringComparison.Ordinal));
            page++;
        }
        while (hasNextPage);

        if (release is null || latest == current) return UpdateCheckResult.NoUpdate(release);
        if (release.Version == skippedVersion) return UpdateCheckResult.NoUpdate();

        return UpdateCheckResult.ForIntelMac(release, release.UpdateUrl);
    }

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
