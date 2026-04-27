using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace STS2ModManager.Services;

/// <summary>
/// Pure-logic helpers for checking the GitHub releases feed and comparing
/// version strings. UI orchestration (prompting the user, updating state)
/// remains in the form layer.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UpdateCheckService
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/nyaoouo/STS2ModManager/releases/latest";
    public const string LatestReleaseUrl = "https://github.com/nyaoouo/STS2ModManager/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/nyaoouo/STS2ModManager/releases";

    public static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan UpdateReminderDelay = TimeSpan.FromDays(1);

    public static string GetBuildVersion()
    {
        var attribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attribute?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? "dev" : version.Trim();
    }

    public static bool IsDevelopmentBuildVersion(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ||
            string.Equals(version.Trim(), "dev", StringComparison.OrdinalIgnoreCase);
    }

    public static bool VersionsMatch(string? left, string? right)
    {
        var normalizedLeft = SettingsService.NormalizeStoredVersion(left);
        var normalizedRight = SettingsService.NormalizeStoredVersion(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft) &&
            string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryCompareVersions(string currentVersionText, string latestVersionText, out int comparisonResult)
    {
        comparisonResult = 0;
        if (!TryParseComparableVersion(currentVersionText, out var currentVersion) ||
            !TryParseComparableVersion(latestVersionText, out var latestVersion))
        {
            return false;
        }

        comparisonResult = currentVersion.CompareTo(latestVersion);
        return true;
    }

    public static async Task<LatestReleaseInfo?> TryGetLatestReleaseInfoAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"STS2ModManager/{GetBuildVersion()}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var latestRelease = await TryGetLatestReleaseInfoFromApiAsync(client, cancellationToken);
        if (latestRelease is not null)
        {
            return latestRelease;
        }

        return await TryGetLatestReleaseInfoFromReleasePageAsync(client, cancellationToken);
    }

    private static async Task<LatestReleaseInfo?> TryGetLatestReleaseInfoFromApiAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagNameElement) || tagNameElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var version = SettingsService.NormalizeStoredVersion(tagNameElement.GetString());
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var releaseUrl = root.TryGetProperty("html_url", out var htmlUrlElement) && htmlUrlElement.ValueKind == JsonValueKind.String
            ? htmlUrlElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(releaseUrl))
        {
            releaseUrl = ReleasesPageUrl;
        }

        return new LatestReleaseInfo(version, releaseUrl);
    }

    private static async Task<LatestReleaseInfo?> TryGetLatestReleaseInfoFromReleasePageAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null ||
            !finalUri.AbsolutePath.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tag = finalUri.Segments.LastOrDefault();
        var version = SettingsService.NormalizeStoredVersion(tag is null ? null : Uri.UnescapeDataString(tag));
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return new LatestReleaseInfo(version, finalUri.AbsoluteUri);
    }

    private static bool TryParseComparableVersion(string versionText, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (IsDevelopmentBuildVersion(versionText))
        {
            return false;
        }

        var normalizedVersion = SettingsService.NormalizeStoredVersion(versionText);
        if (string.IsNullOrWhiteSpace(normalizedVersion))
        {
            return false;
        }

        var match = Regex.Match(normalizedVersion, @"^\d+(?:\.\d+){0,3}");
        if (!match.Success)
        {
            return false;
        }

        var parts = match.Value.Split('.').ToList();
        while (parts.Count < 4)
        {
            parts.Add("0");
        }

        if (!Version.TryParse(string.Join(".", parts), out var parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }
}
