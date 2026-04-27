// =============================================================================
// ModView.Operations.cs  -  Toggle / move / open / export / conflict ops
// =============================================================================
//
// All the "do something with mods" methods that mutate disk + drive dialogs:
//   ToggleMod / ApplyLocalToggle / ToggleAllMods / MoveMod
//   OpenSelectedModFolder / ExportSelectedMods / BuildDefaultExportArchiveName
//   CreateModArchive / AddDirectoryToArchive
//   FindModsById / PromptConflictResolution / DeleteModDirectories
//   FormatPathForDisplay / FormatVersionText / DescribeVersionComparison
//
// Dialogs use FindForm() to obtain the host form for ownership.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using STS2ModManager.Views.Dialogs;
using STS2ModManager.Services;
using STS2ModManager.Services.UI;

namespace STS2ModManager.Views.Main;

[SupportedOSPlatform("windows")]
internal sealed partial class ModView
{
    private void ToggleMod(ModInfo mod, bool enable)
    {
        var targetDirectory = enable ? getModsDirectory() : getDisabledDirectory();
        var operationVerb = enable ? "enable" : "disable";
        var result = MoveMod(
            mod,
            targetDirectory,
            operationVerb,
            $"selected mod at {FormatPathForDisplay(mod.FullPath)}",
            showDialogs: true);

        if (result.Outcome == ModMoveOutcome.Changed)
        {
            if (result.NewFullPath is { } newPath && Directory.Exists(newPath))
            {
                ApplyLocalToggle(mod, enable, newPath);
                setStatus(result.Message);
                return;
            }

            requestReload(result.Message);
            return;
        }

        setStatus(result.Message);
    }

    private void ApplyLocalToggle(ModInfo originalMod, bool nowEnabled, string newFullPath)
    {
        try
        {
            onDirectoriesChanged();

            cachedEnabledMods.RemoveAll(m =>
                string.Equals(m.FullPath, originalMod.FullPath, StringComparison.OrdinalIgnoreCase));
            cachedDisabledMods.RemoveAll(m =>
                string.Equals(m.FullPath, originalMod.FullPath, StringComparison.OrdinalIgnoreCase));

            var newInfo = ModLoader.ReadModInfo(new DirectoryInfo(newFullPath));
            var targetList = nowEnabled ? cachedEnabledMods : cachedDisabledMods;
            targetList.Add(newInfo);
            targetList.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.FolderName, b.FolderName));

            enabledModCount = cachedEnabledMods.Count;
            disabledModCount = cachedDisabledMods.Count;

            if (cardEntries.TryGetValue(originalMod.FullPath, out var entry))
            {
                cardEntries.Remove(originalMod.FullPath);
                cardEntries[newInfo.FullPath] = entry;
            }
            if (selectedModPaths.Remove(originalMod.FullPath))
            {
                selectedModPaths.Add(newInfo.FullPath);
            }

            RefreshCardDisplay();
            UpdateButtons();
        }
        catch
        {
            requestReload(string.Empty);
        }
    }

    private void ToggleAllMods(bool enable)
    {
        var operationVerb = enable ? "enable" : "disable";
        var sourceDirectory = enable ? getDisabledDirectory() : getModsDirectory();
        var targetDirectory = enable ? getModsDirectory() : getDisabledDirectory();
        var mods = ModLoader.LoadMods(sourceDirectory);

        if (mods.Count == 0)
        {
            return;
        }

        if (!MessageDialog.Confirm(
                FindForm()!,
                loc,
                loc.Get("mods.bulk_move_title"),
                LocalizedFormats.BulkMovePrompt(loc, operationVerb, mods.Count)))
        {
            setStatus(LocalizedFormats.BulkMoveCanceledStatus(loc, operationVerb));
            return;
        }

        var changedCount = 0;
        var unchangedCount = 0;
        var failedCount = 0;

        foreach (var mod in mods)
        {
            var result = MoveMod(
                mod,
                targetDirectory,
                operationVerb,
                $"bulk action for {mod.Id} at {FormatPathForDisplay(mod.FullPath)}",
                showDialogs: false);

            switch (result.Outcome)
            {
                case ModMoveOutcome.Changed:
                    changedCount++;
                    break;
                case ModMoveOutcome.Failed:
                    failedCount++;
                    break;
                default:
                    unchangedCount++;
                    break;
            }
        }

        var statusMessage = LocalizedFormats.BulkMoveCompletedStatus(loc, operationVerb, changedCount, unchangedCount, failedCount);
        if (changedCount > 0)
        {
            requestReload(statusMessage);
            return;
        }

        setStatus(statusMessage);
    }

    private ModMoveResult MoveMod(
        ModInfo selectedMod,
        string targetDirectory,
        string operationVerb,
        string incomingSourceLabel,
        bool showDialogs)
    {
        var conflicts = FindModsById(selectedMod.Id, selectedMod.FullPath);
        if (conflicts.Count > 0)
        {
            var choice = PromptConflictResolution(
                selectedMod,
                conflicts,
                incomingSourceLabel);

            if (choice == ConflictChoice.Cancel)
            {
                return new ModMoveResult(ModMoveOutcome.Unchanged, LocalizedFormats.OperationCanceledStatus(loc, operationVerb, selectedMod.Id));
            }

            if (choice == ConflictChoice.KeepExisting)
            {
                DeleteModDirectories(new[] { selectedMod });
                return new ModMoveResult(ModMoveOutcome.Changed, loc.Get("archive.kept_existing_status", selectedMod.Id));
            }

            DeleteModDirectories(conflicts);
        }

        var targetPath = Path.Combine(targetDirectory, selectedMod.FolderName);
        if (Directory.Exists(targetPath))
        {
            if (showDialogs)
            {
                MessageDialog.Warn(
                    FindForm()!,
                    loc,
                    loc.Get("mods.move_skipped_title"),
                    loc.Get("archive.target_folder_already_exists_message", selectedMod.FolderName));
            }

            return new ModMoveResult(ModMoveOutcome.Unchanged, loc.Get("mods.move_skipped_status", selectedMod.Id));
        }

        try
        {
            ModService.MoveDirectory(selectedMod.FullPath, targetPath);
            return new ModMoveResult(
                ModMoveOutcome.Changed,
                LocalizedFormats.MoveCompletedStatus(loc, operationVerb, selectedMod.Id, selectedMod.Name),
                NewFullPath: targetPath);
        }
        catch (Exception exception)
        {
            if (showDialogs)
            {
                MessageDialog.Error(
                    FindForm()!,
                    loc,
                    loc.Get("mods.move_error_title"),
                    exception.Message);
            }

            return new ModMoveResult(ModMoveOutcome.Failed, loc.Get("mods.move_failed_status", selectedMod.Id, exception.Message));
        }
    }

    private void OpenSelectedModFolder()
    {
        var selectedMods = GetSelectedMods();
        if (selectedMods.Count == 0)
        {
            setStatus(loc.Get("saves.select_mod_folder_status"));
            return;
        }

        if (selectedMods.Count > 1)
        {
            setStatus(loc.Get("saves.select_single_mod_folder_status"));
            return;
        }

        var selectedMod = selectedMods[0];

        if (!Directory.Exists(selectedMod.FullPath))
        {
            var message = loc.Get("mods.mod_folder_missing_message", selectedMod.FullPath);
            setStatus(loc.Get("common.open_folder_failed_status", message));
            MessageDialog.Warn(
                FindForm()!,
                loc,
                loc.Get("common.open_folder_error_title"),
                message);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{selectedMod.FullPath}\"",
                UseShellExecute = true
            });
            setStatus(loc.Get("common.open_folder_opened_status", selectedMod.Id));
        }
        catch (Exception exception)
        {
            setStatus(loc.Get("common.open_folder_failed_status", exception.Message));
            MessageDialog.Error(
                FindForm()!,
                loc,
                loc.Get("common.open_folder_error_title"),
                exception.Message);
        }
    }

    private void ExportSelectedMods()
    {
        var selectedMods = GetSelectedMods();
        if (selectedMods.Count == 0)
        {
            setStatus(loc.Get("saves.select_mods_export_status"));
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "zip",
            FileName = BuildDefaultExportArchiveName(selectedMods),
            Filter = loc.Get("ui.export_archive_filter"),
            OverwritePrompt = true,
            Title = loc.Get("ui.export_archive_dialog_title")
        };

        if (dialog.ShowDialog(FindForm()!) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            setStatus(loc.Get("ui.export_canceled_status"));
            return;
        }

        try
        {
            CreateModArchive(dialog.FileName, selectedMods);
            var message = loc.Get("ui.export_completed_status", selectedMods.Count, dialog.FileName);
            setStatus(message);
            notify(loc.Get("ui.export_completed_message", selectedMods.Count, dialog.FileName));
        }
        catch (Exception exception)
        {
            setStatus(loc.Get("ui.export_failed_status", exception.Message));
            MessageDialog.Error(
                FindForm()!,
                loc,
                loc.Get("ui.export_failed_title"),
                exception.Message);
        }
    }

    private static string BuildDefaultExportArchiveName(IReadOnlyList<ModInfo> selectedMods)
    {
        return selectedMods.Count == 1
            ? $"{selectedMods[0].FolderName}.zip"
            : $"mods-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    }

    private static void CreateModArchive(string archivePath, IReadOnlyList<ModInfo> mods)
    {
        var directory = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var mod in mods.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(mod.FullPath))
            {
                throw new DirectoryNotFoundException(mod.FullPath);
            }

            AddDirectoryToArchive(archive, mod.FullPath, mod.FolderName);
        }
    }

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDirectory, string archiveRoot)
    {
        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            archive.CreateEntry($"{ArchiveInstallService.NormalizeArchivePath(archiveRoot)}/");
            return;
        }

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var entryName = ArchiveInstallService.NormalizeArchivePath(Path.Combine(archiveRoot, relativePath));
            archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
        }
    }

    private List<ModInfo> FindModsById(string modId, params string[] excludedPaths)
        => ModService.FindModsById(getModsDirectory(), getDisabledDirectory(), modId, excludedPaths);

    private ConflictChoice PromptConflictResolution(ModInfo incomingMod, IReadOnlyList<ModInfo> existingMods, string incomingSourceLabel)
    {
        return ConflictResolutionDialog.Show(
            FindForm()!,
            loc,
            incomingMod,
            existingMods,
            incomingSourceLabel,
            DescribeVersionComparison(incomingMod, existingMods),
            mod => FormatPathForDisplay(mod.FullPath),
            FormatVersionText);
    }

    private static void DeleteModDirectories(IEnumerable<ModInfo> mods)
        => ModService.DeleteModDirectories(mods);

    private string FormatPathForDisplay(string path)
    {
        var gameDir = getGameDirectory();
        if (path.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = path.Substring(gameDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relativePath.Length == 0 ? "." : relativePath;
        }

        return path;
    }

    private string FormatVersionText(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ? loc.Get("ui.unknown_version_label") : version.Trim();
    }

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
}
