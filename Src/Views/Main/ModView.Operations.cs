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
        var modsDir = getModsDirectory();
        var archiveDir = getDisabledDirectory();

        try
        {
            if (enable)
            {
                if (!archiveVersionsByModId.TryGetValue(mod.Id, out var versions) || versions.Count == 0)
                {
                    setStatus(loc.Get("archive.no_archive_to_enable_status", mod.Id));
                    requestReload(string.Empty);
                    return;
                }

                var latest = versions[0]; // service returns newest-first
                ModArchiveService.ActivateVersion(modsDir, archiveDir, currentActive: null, mod.Id, latest.ZipFileName);
                requestReload(loc.Get("archive.version_activated_status", mod.Id, FormatVersionText(latest.Version)));
            }
            else
            {
                ModArchiveService.EnsureActiveMirrored(modsDir, archiveDir, mod);
                if (Directory.Exists(mod.FullPath))
                {
                    Directory.Delete(mod.FullPath, recursive: true);
                }
                requestReload(loc.Get("archive.disabled_status", mod.Id));
            }
        }
        catch (Exception exception)
        {
            MessageDialog.Error(
                FindForm()!,
                loc,
                loc.Get("mods.move_error_title"),
                exception.Message);
            setStatus(loc.Get("mods.move_failed_status", mod.Id, exception.Message));
            requestReload(string.Empty);
        }
    }

    private void ApplyLocalToggle(ModInfo originalMod, bool nowEnabled, string newFullPath)
    {
        // No longer used: toggling now always triggers a full reload via ModArchiveService.
        _ = originalMod; _ = nowEnabled; _ = newFullPath;
    }

    private void ToggleAllMods(bool enable)
    {
        var operationVerb = enable ? "enable" : "disable";
        var modsDir = getModsDirectory();
        var archiveDir = getDisabledDirectory();

        // Snapshot the source list because the cache mutates after each op.
        var sourceList = (enable ? cachedDisabledMods : cachedEnabledMods).ToList();
        if (sourceList.Count == 0)
        {
            return;
        }

        if (!MessageDialog.Confirm(
                FindForm()!,
                loc,
                loc.Get("mods.bulk_move_title"),
                LocalizedFormats.BulkMovePrompt(loc, operationVerb, sourceList.Count)))
        {
            setStatus(LocalizedFormats.BulkMoveCanceledStatus(loc, operationVerb));
            return;
        }

        var changedCount = 0;
        var unchangedCount = 0;
        var failedCount = 0;

        foreach (var mod in sourceList)
        {
            try
            {
                if (enable)
                {
                    if (!archiveVersionsByModId.TryGetValue(mod.Id, out var versions) || versions.Count == 0)
                    {
                        unchangedCount++;
                        continue;
                    }
                    var latest = versions[0];
                    ModArchiveService.ActivateVersion(modsDir, archiveDir, currentActive: null, mod.Id, latest.ZipFileName);
                    changedCount++;
                }
                else
                {
                    ModArchiveService.EnsureActiveMirrored(modsDir, archiveDir, mod);
                    if (Directory.Exists(mod.FullPath))
                    {
                        Directory.Delete(mod.FullPath, recursive: true);
                    }
                    changedCount++;
                }
            }
            catch
            {
                failedCount++;
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

    private void CreateModArchive(string archivePath, IReadOnlyList<ModInfo> mods)
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
            if (Directory.Exists(mod.FullPath))
            {
                AddDirectoryToArchive(archive, mod.FullPath, mod.FolderName);
                continue;
            }

            // Disabled / archive-only mod: there is no live folder, so pull
            // the newest archived zip and copy its entries under the mod folder
            // name in the export archive.
            if (archiveVersionsByModId.TryGetValue(mod.Id, out var versions) && versions.Count > 0)
            {
                var newest = versions[0];
                AddZipContentsToArchive(archive, newest.ZipFullPath, mod.FolderName);
                continue;
            }

            throw new DirectoryNotFoundException(mod.FullPath);
        }
    }

    private static void AddZipContentsToArchive(ZipArchive destination, string sourceZipPath, string archiveRoot)
    {
        using var sourceArchive = ZipFile.OpenRead(sourceZipPath);
        var rootPrefix = ArchiveInstallService.NormalizeArchivePath(archiveRoot).TrimEnd('/') + "/";
        var copiedAny = false;

        foreach (var entry in sourceArchive.Entries)
        {
            // Source archives are stored as <Id>/<files...>; strip the first
            // segment so we can re-root under the export's chosen folder name.
            var normalized = entry.FullName.Replace('\\', '/');
            var slash = normalized.IndexOf('/');
            var relative = slash < 0 ? string.Empty : normalized.Substring(slash + 1);

            if (string.IsNullOrEmpty(relative))
            {
                continue;
            }

            var entryName = rootPrefix + relative;

            if (entryName.EndsWith("/", StringComparison.Ordinal))
            {
                destination.CreateEntry(entryName);
                continue;
            }

            var newEntry = destination.CreateEntry(entryName, CompressionLevel.Optimal);
            using var sourceStream = entry.Open();
            using var destinationStream = newEntry.Open();
            sourceStream.CopyTo(destinationStream);
            copiedAny = true;
        }

        if (!copiedAny)
        {
            destination.CreateEntry(rootPrefix);
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

    /// <summary>
    /// Rebuilds the right-click version-chooser menu for the given mod.
    /// Lists every archived version with the active one ticked; selecting
    /// a non-active version raises <see cref="ActivateVersionRequested"/>.
    /// "Delete" submenu offers per-version removal.
    /// </summary>
    private void PopulateVersionMenu(ContextMenuStrip menu, ModInfo mod, bool isActive)
    {
        menu.Items.Clear();
        if (!archiveVersionsByModId.TryGetValue(mod.Id, out var versions) || versions.Count == 0)
        {
            return;
        }

        // Active version is whatever currently sits in mods/<Id>/ -- match by version string.
        var activeVersionStr = isActive ? (mod.Version ?? string.Empty) : null;

        var header = new ToolStripLabel(loc.Get("archive.version_menu_header", mod.Name ?? mod.Id))
        {
            Enabled = false
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        foreach (var version in versions)
        {
            var label = FormatVersionText(version.Version);
            var isThisActive = activeVersionStr is not null
                && string.Equals(activeVersionStr, version.Version, StringComparison.OrdinalIgnoreCase);
            var item = new ToolStripMenuItem(label)
            {
                Checked = isThisActive,
                Enabled = !isThisActive,
                Tag = version,
            };
            item.Click += (_, _) => ActivateVersionRequested?.Invoke(mod, version);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var deleteRoot = new ToolStripMenuItem(loc.Get("archive.delete_version_menu"));
        foreach (var version in versions)
        {
            var label = FormatVersionText(version.Version);
            var isThisActive = activeVersionStr is not null
                && string.Equals(activeVersionStr, version.Version, StringComparison.OrdinalIgnoreCase);
            var item = new ToolStripMenuItem(label)
            {
                Tag = version,
                Enabled = !isThisActive,
            };
            item.Click += (_, _) => DeleteVersionRequested?.Invoke(mod, version);
            deleteRoot.DropDownItems.Add(item);
        }

        // Delete-all entry: when the mod is enabled this leaves the active
        // mirror intact; when disabled it wipes every archived copy.
        if (versions.Count > 0)
        {
            deleteRoot.DropDownItems.Add(new ToolStripSeparator());
            var deleteAll = new ToolStripMenuItem(loc.Get("archive.delete_all_versions_menu"));
            deleteAll.Click += (_, _) => DeleteAllVersionsRequested?.Invoke(mod);
            deleteRoot.DropDownItems.Add(deleteAll);
        }

        menu.Items.Add(deleteRoot);
    }
}
