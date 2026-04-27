using System;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using STS2ModManager.Views.Dialogs;

[SupportedOSPlatform("windows")]
internal sealed partial class MainWindow
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
        if (!checkForUpdates || UpdateCheckService.IsDevelopmentBuildVersion(buildVersion))
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
            using var cancellationSource = new CancellationTokenSource(UpdateCheckService.UpdateCheckTimeout);
            latestRelease = await UpdateCheckService.TryGetLatestReleaseInfoAsync(cancellationSource.Token);
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

        if (UpdateCheckService.VersionsMatch(skippedUpdateVersion, latestRelease.Version))
        {
            return;
        }

        if (!UpdateCheckService.TryCompareVersions(buildVersion, latestRelease.Version, out var comparisonResult) || comparisonResult >= 0)
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
                updateRemindAfterUtc = DateTime.UtcNow.Add(UpdateCheckService.UpdateReminderDelay);
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
            FileName = string.IsNullOrWhiteSpace(releasePageUrl) ? UpdateCheckService.ReleasesPageUrl : releasePageUrl,
            UseShellExecute = true,
        };

        Process.Start(startInfo);
    }
}
