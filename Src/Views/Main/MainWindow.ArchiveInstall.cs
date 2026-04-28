using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using STS2ModManager.Views.Dialogs;
using STS2ModManager.Services;

[SupportedOSPlatform("windows")]
internal sealed partial class MainWindow
{
    private void EnableArchiveDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += HandleArchiveDragEnter;
        control.DragLeave += HandleArchiveDragLeave;
        control.DragDrop += HandleArchiveDragDrop;

        foreach (Control child in control.Controls)
        {
            EnableArchiveDrop(child);
        }
    }

    private void ShowDropOverlay()
    {
        if (dropOverlay is null) return;
        dropOverlay.Visible = true;
        dropOverlay.BringToFront();
    }

    private void HideDropOverlay()
    {
        if (dropOverlay is null) return;
        dropOverlay.Visible = false;
    }

    private void HandleArchiveDragEnter(object? sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) ?? true)
        {
            eventArgs.Effect = DragDropEffects.None;
            return;
        }

        var droppedPaths = eventArgs.Data?.GetData(DataFormats.FileDrop) as string[];
        var hasFiles = droppedPaths is { Length: > 0 };
        eventArgs.Effect = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        if (hasFiles)
        {
            ShowDropOverlay();
        }
    }

    private void HandleArchiveDragLeave(object? sender, EventArgs eventArgs)
    {
        HideDropOverlay();
    }

    private void HandleArchiveDragDrop(object? sender, DragEventArgs eventArgs)
    {
        HideDropOverlay();
        var droppedPaths = eventArgs.Data?.GetData(DataFormats.FileDrop) as string[];
        if (droppedPaths is null || droppedPaths.Length == 0)
        {
            return;
        }

        // Defer the install work so this OLE drop callback can return immediately.
        // Otherwise the source Explorer window stays frozen until the entire
        // unzip + IO + dialog flow completes (Explorer holds a lock on the drop
        // operation until the target acknowledges it).
        var dialogTitle = loc.Get("archive.archive_import_title");
        BeginInvoke(new Action(() => InstallArchives(droppedPaths, dialogTitle)));
    }

    private void InstallArchives(IEnumerable<string> archivePaths, string dialogTitle)
    {
        var results = new List<OperationResult>();
        foreach (var path in archivePaths)
        {
            if (File.Exists(path))
            {
                results.Add(InstallArchive(path));
            }
        }

        if (results.Count == 0)
        {
            SetStatus(loc.Get("launch.no_files_provided_status"));
            return;
        }

        var summary = string.Join(Environment.NewLine, results.Select(result => result.Message));
        if (results.Any(result => result.RefreshRequired))
        {
            modPresenter?.Reload(summary);
        }
        else
        {
            SetStatus(summary);
        }

        MessageDialog.Info(
            this,
            loc,
            dialogTitle,
            summary);
    }

    private OperationResult InstallArchive(string archivePath)
    {
        if (!ArchiveInstallService.TryReadArchiveInstallPlans(archivePath, loc, out var installPlans, out var errorMessage))
        {
            return new OperationResult(false, $"{Path.GetFileName(archivePath)}: {errorMessage}");
        }

        var results = new List<OperationResult>();
        foreach (var installPlan in installPlans)
        {
            var stepResult = InstallArchivePlan(archivePath, installPlan);
            results.Add(stepResult.Result);
            if (stepResult.StopProcessingArchive)
            {
                break;
            }
        }

        return new OperationResult(
            results.Any(result => result.RefreshRequired),
            string.Join(Environment.NewLine, results.Select(result => result.Message)));
    }

    private ArchiveInstallStepResult InstallArchivePlan(string archivePath, ArchiveInstallPlan installPlan)
    {
        var archiveFileName = Path.GetFileName(archivePath);

        var incomingMod = new ModInfo(
            installPlan.Id,
            installPlan.Name,
            installPlan.Version,
            installPlan.InstallFolderName,
            string.Empty);

        // The new install model: every incoming archive becomes one entry in
        // <archiveDir>/<Id>/<version>.zip. The active install at mods/<Id>/
        // is NEVER touched here -- the user activates a version explicitly via
        // the version chooser. The only conflict prompt fires when the same
        // (id, version) zip already exists in the archive.
        var versionAlreadyArchived = ModArchiveService.HasArchivedVersion(
            disabledDirectory, incomingMod.Id, incomingMod.Version);

        var overwrite = false;
        if (versionAlreadyArchived)
        {
            var existing = FindModsById(incomingMod.Id);
            var choice = PromptConflictResolution(
                incomingMod,
                existing,
                $"archive {archiveFileName}");

            switch (choice)
            {
                case ConflictChoice.Cancel:
                    return new ArchiveInstallStepResult(
                        new OperationResult(false, loc.Get("archive.archive_install_canceled", archiveFileName)),
                        StopProcessingArchive: true);
                case ConflictChoice.KeepExisting:
                    return new ArchiveInstallStepResult(
                        new OperationResult(false, loc.Get("archive.archive_kept_existing", archiveFileName, incomingMod.Id)),
                        StopProcessingArchive: false);
                case ConflictChoice.KeepIncoming:
                    overwrite = true;
                    break;
            }
        }

        string? extractionRoot = null;
        try
        {
            extractionRoot = ArchiveInstallService.ExtractArchiveToTemporaryFolder(archivePath, installPlan);
            var sourceFolder = Path.Combine(extractionRoot, installPlan.InstallFolderName);

            var (zipPath, outcome) = ModArchiveService.InstallVersionFromFolder(
                disabledDirectory,
                incomingMod.Id,
                incomingMod.Version,
                sourceFolder,
                overwriteExisting: overwrite);

            // Auto-apply the freshly installed version: previously-active install
            // (if any) is mirrored into the archive by ActivateVersion before
            // being replaced, so nothing is lost.
            string? activationError = null;
            try
            {
                var currentActive = ModLoader.LoadMods(modsDirectory)
                    .FirstOrDefault(mod => string.Equals(mod.Id, incomingMod.Id, StringComparison.OrdinalIgnoreCase));
                ModArchiveService.ActivateVersion(
                    modsDirectory,
                    disabledDirectory,
                    currentActive,
                    incomingMod.Id,
                    Path.GetFileName(zipPath));
            }
            catch (Exception activationException)
            {
                activationError = activationException.Message;
            }

            var messageKey = outcome switch
            {
                ModArchiveService.InstallOutcome.Replaced => "archive.archive_version_replaced",
                _ => "archive.archive_version_added",
            };
            var baseMessage = loc.Get(
                messageKey,
                archiveFileName,
                incomingMod.Id,
                FormatVersionText(incomingMod.Version),
                Path.GetFileName(zipPath));
            var fullMessage = activationError == null
                ? baseMessage
                : baseMessage + " " + loc.Get("archive.archive_auto_activate_failed", activationError);
            return new ArchiveInstallStepResult(
                new OperationResult(true, fullMessage),
                StopProcessingArchive: false);
        }
        catch (Exception exception)
        {
            return new ArchiveInstallStepResult(
                new OperationResult(false, loc.Get("archive.archive_install_failed", archiveFileName, exception.Message)),
                StopProcessingArchive: false);
        }
        finally
        {
            if (!string.IsNullOrEmpty(extractionRoot))
            {
                TryDeleteDirectory(extractionRoot);
            }
        }
    }

    private List<ModInfo> FindModsById(string modId, params string[] excludedPaths)
        => ModService.FindModsById(modsDirectory, disabledDirectory, modId, excludedPaths);

    private static void DeleteModDirectories(IEnumerable<ModInfo> mods)
        => ModService.DeleteModDirectories(mods);

    private static void MoveDirectory(string source, string destination)
        => ModService.MoveDirectory(source, destination);

    private static void TryDeleteDirectory(string path)
        => ModService.TryDeleteDirectory(path);

    private string FormatPathForDisplay(string path)
    {
        if (path.StartsWith(gameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = path.Substring(gameDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relativePath.Length == 0 ? "." : relativePath;
        }

        return path;
    }

    private string FormatVersionText(string? version)
        => string.IsNullOrWhiteSpace(version) ? loc.Get("ui.unknown_version_label") : version.Trim();

    private string DescribeVersionComparison(ModInfo incomingMod, IReadOnlyList<ModInfo> existingMods)
    {
        if (string.IsNullOrWhiteSpace(incomingMod.Version) || existingMods.Count == 0)
        {
            return string.Empty;
        }

        var comparisonLines = new List<string>();
        foreach (var existingMod in existingMods)
        {
            var comparison = ModVersion.Compare(incomingMod.Version, existingMod.Version);
            if (comparison > 0)
            {
                comparisonLines.Add(loc.Get("archive.incoming_version_newer_message", FormatVersionText(incomingMod.Version), FormatVersionText(existingMod.Version)));
            }
            else if (comparison < 0)
            {
                comparisonLines.Add(loc.Get("archive.incoming_version_older_message", FormatVersionText(incomingMod.Version), FormatVersionText(existingMod.Version)));
            }
            else if (!string.IsNullOrWhiteSpace(existingMod.Version))
            {
                comparisonLines.Add(loc.Get("archive.incoming_version_same_message", FormatVersionText(existingMod.Version)));
            }
        }

        return string.Join(Environment.NewLine, comparisonLines.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private ConflictChoice PromptConflictResolution(ModInfo incomingMod, IReadOnlyList<ModInfo> existingMods, string incomingSourceLabel)
        => ConflictResolutionDialog.Show(
            this,
            loc,
            incomingMod,
            existingMods,
            incomingSourceLabel,
            DescribeVersionComparison(incomingMod, existingMods),
            mod => FormatPathForDisplay(mod.FullPath),
            FormatVersionText);

    private void HandleActivateVersionRequested(ModInfo mod, ModVersionEntry version)
    {
        try
        {
            // Use the real on-disk active mod (if any) so EnsureActiveMirrored
            // never tries to zip a synthesized placeholder path.
            var live = ResolveActiveModOrNull(mod.Id);
            ModArchiveService.ActivateVersion(modsDirectory, disabledDirectory, currentActive: live, mod.Id, version.ZipFileName);
            modPresenter?.Reload(loc.Get("archive.version_activated_status", mod.Id, version.Version));
        }
        catch (Exception ex)
        {
            MessageDialog.Error(this, loc, loc.Get("mods.move_error_title"), ex.Message);
            SetStatus(loc.Get("mods.move_failed_status", mod.Id, ex.Message));
        }
    }

    private void HandleDeleteVersionRequested(ModInfo mod, ModVersionEntry version)
    {
        if (!MessageDialog.Confirm(
                this,
                loc,
                loc.Get("archive.delete_version_title"),
                loc.Get("archive.delete_version_prompt", FormatVersionText(version.Version), mod.Id)))
        {
            return;
        }

        try
        {
            // Only treat the mod as currently active if there really is a live
            // install at mods/<Id>/. Otherwise (mod is disabled) any archived
            // version is fair game to delete.
            var live = ResolveActiveModOrNull(mod.Id);
            ModArchiveService.DeleteVersion(disabledDirectory, currentActive: live, mod.Id, version.ZipFileName, force: false);
            modPresenter?.Reload(loc.Get("archive.version_deleted_status", FormatVersionText(version.Version), mod.Id));
        }
        catch (Exception ex)
        {
            MessageDialog.Error(this, loc, loc.Get("mods.move_error_title"), ex.Message);
            SetStatus(loc.Get("mods.move_failed_status", mod.Id, ex.Message));
        }
    }

    private void HandleDeleteAllVersionsRequested(ModInfo mod)
    {
        if (!MessageDialog.Confirm(
                this,
                loc,
                loc.Get("archive.delete_all_versions_title"),
                loc.Get("archive.delete_all_versions_prompt", mod.Id)))
        {
            return;
        }

        try
        {
            var live = ResolveActiveModOrNull(mod.Id);
            var deleted = ModArchiveService.DeleteAllVersions(disabledDirectory, currentActive: live, mod.Id);
            modPresenter?.Reload(loc.Get("archive.all_versions_deleted_status", deleted, mod.Id));
        }
        catch (Exception ex)
        {
            MessageDialog.Error(this, loc, loc.Get("mods.move_error_title"), ex.Message);
            SetStatus(loc.Get("mods.move_failed_status", mod.Id, ex.Message));
        }
    }

    private ModInfo? ResolveActiveModOrNull(string modId)
    {
        var activePath = Path.Combine(modsDirectory, modId);
        if (!Directory.Exists(activePath)) return null;
        return ModLoader.LoadMods(modsDirectory)
            .FirstOrDefault(mod => string.Equals(mod.Id, modId, StringComparison.OrdinalIgnoreCase));
    }
}
