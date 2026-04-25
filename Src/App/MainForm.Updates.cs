using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using Microsoft.Win32;
using System.Text.Json.Serialization;
using STS2ModManager.App;
using STS2ModManager.Mods;
using STS2ModManager.Saves;
using STS2ModManager.Dialogs;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed partial class ModManagerForm
{
    private async void HandleShown(object? sender, EventArgs eventArgs)
    {
        Shown -= HandleShown;

        // Re-apply theme once after the form is fully visible so MaterialSkin
        // controls inside non-active tab pages (Saves/Config) pick up the right
        // background. Without this, a colored rect can appear under fields
        // until the user toggles theme — see UI redesign sub-goal 5 notes.
        themeController.Refresh();

        if (startupArchivePaths.Length > 0)
        {
            InstallArchives(startupArchivePaths, loc.Get("archive.archive_import_title"));
        }

        await CheckForUpdatesOnStartupAsync();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!checkForUpdates || IsDevelopmentBuildVersion(buildVersion))
        {
            return;
        }

        if (updateRemindAfterUtc.HasValue && updateRemindAfterUtc.Value > DateTime.UtcNow)
        {
            return;
        }

        LatestReleaseInfo? latestRelease;
        try
        {
            using var cancellationSource = new CancellationTokenSource(UpdateCheckTimeout);
            latestRelease = await TryGetLatestReleaseInfoAsync(cancellationSource.Token);
        }
        catch (HttpRequestException)
        {
            latestRelease = null;
        }
        catch (OperationCanceledException)
        {
            latestRelease = null;
        }

        if (latestRelease is null)
        {
            latestReleaseVersion = null;
            configPage?.UpdateVersionInfo(buildVersion, latestReleaseVersion);
            SetStatus(loc.Get("update.update_check_unavailable_status"));
            return;
        }

        latestReleaseVersion = latestRelease.Version;
        configPage?.UpdateVersionInfo(buildVersion, latestReleaseVersion);

        if (VersionsMatch(skippedUpdateVersion, latestRelease.Version))
        {
            return;
        }

        if (!TryCompareVersions(buildVersion, latestRelease.Version, out var comparisonResult) || comparisonResult >= 0)
        {
            return;
        }

        var choice = PromptForUpdate(latestRelease);
        switch (choice)
        {
            case UpdatePromptChoice.UpdateNow:
                skippedUpdateVersion = null;
                updateRemindAfterUtc = null;
                SaveSettings(CurrentSettings());
                try
                {
                    OpenReleasePage(latestRelease.ReleasePageUrl);
                    SetStatus(loc.Get("update.update_page_opened_status", latestRelease.Version));
                }
                catch (Exception exception)
                {
                    MessageDialog.Error(
                        this,
                        loc,
                        loc.Get("update.update_failed_title"),
                        loc.Get("common.open_release_page_failed_message", exception.Message));
                    SetStatus(loc.Get("update.update_open_failed_status", exception.Message));
                }
                break;

            case UpdatePromptChoice.RemindLater:
                skippedUpdateVersion = null;
                updateRemindAfterUtc = DateTime.UtcNow.Add(UpdateReminderDelay);
                SaveSettings(CurrentSettings());
                SetStatus(loc.Get("update.update_reminder_scheduled_status", latestRelease.Version));
                break;

            case UpdatePromptChoice.SkipThisVersion:
                skippedUpdateVersion = latestRelease.Version;
                updateRemindAfterUtc = null;
                SaveSettings(CurrentSettings());
                SetStatus(loc.Get("update.update_skipped_status", latestRelease.Version));
                break;

            case UpdatePromptChoice.NeverCheck:
                checkForUpdates = false;
                skippedUpdateVersion = latestRelease.Version;
                updateRemindAfterUtc = null;
                SaveSettings(CurrentSettings());
                SetStatus(loc.Get("update.update_checks_disabled_status"));
                break;
        }
    }

    private UpdatePromptChoice PromptForUpdate(LatestReleaseInfo latestRelease)
    {
        return UpdatePromptDialog.Show(this, loc, buildVersion, latestRelease.Version);
    }

    private void OpenReleasePage(string releasePageUrl)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(releasePageUrl) ? ReleasesPageUrl : releasePageUrl,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    private static async Task<LatestReleaseInfo?> TryGetLatestReleaseInfoAsync(CancellationToken cancellationToken)
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

        var version = NormalizeStoredVersion(tagNameElement.GetString());
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
        var version = NormalizeStoredVersion(tag is null ? null : Uri.UnescapeDataString(tag));
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return new LatestReleaseInfo(version, finalUri.AbsoluteUri);
    }

    private static string GetBuildVersion()
    {
        var attribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attribute?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? "dev" : version.Trim();
    }

    private static bool IsDevelopmentBuildVersion(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ||
            string.Equals(version.Trim(), "dev", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeStoredVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(1)
            : trimmed;
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var normalized = value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value;
        return normalized.ToUniversalTime();
    }

    private static bool VersionsMatch(string? left, string? right)
    {
        var normalizedLeft = NormalizeStoredVersion(left);
        var normalizedRight = NormalizeStoredVersion(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft) &&
            string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCompareVersions(string currentVersionText, string latestVersionText, out int comparisonResult)
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

    private static bool TryParseComparableVersion(string versionText, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (IsDevelopmentBuildVersion(versionText))
        {
            return false;
        }

        var normalizedVersion = NormalizeStoredVersion(versionText);
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
