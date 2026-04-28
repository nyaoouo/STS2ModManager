using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using STS2ModManager.Services;
using STS2ModManager.Views.Dialogs;

[SupportedOSPlatform("windows")]
internal sealed partial class MainWindow
{
    private bool archiveMigrationDone;

    private async void HandleShown(object? sender, EventArgs eventArgs)
    {
        Shown -= HandleShown;

        // Re-apply theme once after the form is fully visible so MaterialSkin
        // controls inside non-active tab pages (Saves/Config) pick up the right
        // background. Without this, a colored rect can appear under fields
        // until the user toggles theme — see UI redesign sub-goal 5 notes.
        themeController.Refresh();

        TryRunArchiveMigrationOnce();

        if (startupArchivePaths.Length > 0)
        {
            InstallArchives(startupArchivePaths, loc.Get("archive.archive_import_title"));
        }

        await CheckForUpdatesOnStartupAsync();
    }

    private void TryRunArchiveMigrationOnce()
    {
        if (archiveMigrationDone) return;
        archiveMigrationDone = true;

        try
        {
            // Step 1: rename the legacy ".mods" folder to the new default
            // ".archive-mods" when the user has never customised the setting.
            // Persist the new value so subsequent launches see it directly.
            TryRenameLegacyArchiveDirectory();

            var report = ModArchiveService.RunStartupMigration(disabledDirectory);

            // Only surface the dialog if something actually happened (or failed).
            // An idempotent re-run on a fully-migrated archive is silent.
            var hasNoise = report.FlatLayoutsConverted > 0
                || report.VersionsRecovered > 0
                || report.Warnings.Count > 0;
            if (!hasNoise)
            {
                return;
            }

            var summary = loc.Get(
                "archive.startup_migration_summary",
                report.FlatLayoutsConverted,
                report.VersionsRecovered,
                report.Warnings.Count);
            SetStatus(summary);

            var lines = new System.Text.StringBuilder();
            lines.AppendLine(summary);
            lines.AppendLine();
            lines.AppendLine($"Archive: {disabledDirectory}");
            lines.AppendLine();

            var converted = report.Items.Where(i => i.Status == ModArchiveService.MigrationStatus.Converted).ToList();
            var failed = report.Items.Where(i => i.Status == ModArchiveService.MigrationStatus.Failed).ToList();
            var skipped = report.Items.Where(i => i.Status == ModArchiveService.MigrationStatus.AlreadyMigrated).ToList();

            if (converted.Count > 0)
            {
                lines.AppendLine($"Converted ({converted.Count}):");
                foreach (var item in converted)
                {
                    lines.AppendLine($"  + {item.ModId}  {item.Detail}");
                }
                lines.AppendLine();
            }

            if (failed.Count > 0)
            {
                lines.AppendLine($"Failed ({failed.Count}):");
                foreach (var item in failed)
                {
                    lines.AppendLine($"  ! {item.ModId}  {item.Detail}");
                }
                lines.AppendLine();
            }

            if (skipped.Count > 0)
            {
                lines.AppendLine($"Already migrated ({skipped.Count}):");
                foreach (var item in skipped)
                {
                    lines.AppendLine($"  . {item.ModId}  {item.Detail}");
                }
            }

            MessageDialog.Info(
                this,
                loc,
                loc.Get("archive.startup_migration_title"),
                lines.ToString().TrimEnd());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Archive migration failed: {exception}");
            MessageDialog.Error(
                this,
                loc,
                loc.Get("archive.startup_migration_title"),
                exception.Message);
        }
    }

    /// <summary>
    /// Renames the legacy <c>.mods</c> archive folder to <c>.archive-mods</c>
    /// when the user is still on the old default. Persists the new name
    /// to settings and refreshes derived paths so the rest of the migration
    /// works against the new directory.
    /// </summary>
    private void TryRenameLegacyArchiveDirectory()
    {
        const string legacyName = ".mods";
        const string newName = ".archive-mods";

        if (!string.Equals(disabledDirectoryName, legacyName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrEmpty(gameDirectory))
        {
            return;
        }

        var legacyPath = System.IO.Path.Combine(gameDirectory, legacyName);
        var newPath = System.IO.Path.Combine(gameDirectory, newName);

        try
        {
            if (System.IO.Directory.Exists(legacyPath) && !System.IO.Directory.Exists(newPath))
            {
                System.IO.Directory.Move(legacyPath, newPath);
            }
            else if (!System.IO.Directory.Exists(legacyPath) && !System.IO.Directory.Exists(newPath))
            {
                // Neither exists -- nothing to rename, but still flip the setting.
            }
            else if (System.IO.Directory.Exists(legacyPath) && System.IO.Directory.Exists(newPath))
            {
                // Both exist (unusual). Don't overwrite -- bail with a warning
                // so the user can resolve manually.
                MessageDialog.Warn(
                    this,
                    loc,
                    loc.Get("archive.startup_migration_title"),
                    $"Both '{legacyPath}' and '{newPath}' exist. Please merge them manually; keeping '{legacyName}' for now.");
                return;
            }

            disabledDirectoryName = newName;
            UpdateDirectoryLabels();
            SaveSettings(CurrentSettings());
        }
        catch (Exception ex)
        {
            MessageDialog.Warn(
                this,
                loc,
                loc.Get("archive.startup_migration_title"),
                $"Could not rename '{legacyName}' to '{newName}': {ex.Message}");
        }
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
