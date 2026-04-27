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
            Path.Combine(disabledDirectory, installPlan.InstallFolderName));

        var conflicts = FindModsById(incomingMod.Id);
        if (conflicts.Count > 0)
        {
            var choice = PromptConflictResolution(
                incomingMod,
                conflicts,
                $"archive {archiveFileName}");

            if (choice == ConflictChoice.Cancel)
            {
                return new ArchiveInstallStepResult(
                    new OperationResult(false, loc.Get("archive.archive_install_canceled", archiveFileName)),
                    StopProcessingArchive: true);
            }

            if (choice == ConflictChoice.KeepExisting)
            {
                return new ArchiveInstallStepResult(
                    new OperationResult(false, loc.Get("archive.archive_kept_existing", archiveFileName, incomingMod.Id)),
                    StopProcessingArchive: false);
            }

            DeleteModDirectories(conflicts);
        }

        var targetPath = Path.Combine(disabledDirectory, installPlan.InstallFolderName);
        if (Directory.Exists(targetPath))
        {
            return new ArchiveInstallStepResult(
                new OperationResult(false, loc.Get("archive.archive_target_folder_exists", archiveFileName, installPlan.InstallFolderName)),
                StopProcessingArchive: false);
        }

        string? extractionRoot = null;
        try
        {
            extractionRoot = ArchiveInstallService.ExtractArchiveToTemporaryFolder(archivePath, installPlan);
            MoveDirectory(Path.Combine(extractionRoot, installPlan.InstallFolderName), targetPath);
            return new ArchiveInstallStepResult(
                new OperationResult(true, LocalizedFormats.ArchiveInstalled(
                    loc,
                    archiveFileName,
                    installPlan.Id,
                    disabledDirectoryName,
                    installPlan.ArchiveFolderName,
                    installPlan.InstallFolderName)),
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
}
