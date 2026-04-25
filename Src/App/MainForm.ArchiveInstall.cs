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
            ReloadLists(summary);
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
        if (!TryReadArchiveInstallPlans(archivePath, out var installPlans, out var errorMessage))
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
            extractionRoot = ExtractArchiveToTemporaryFolder(archivePath, installPlan);
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

    private bool TryReadArchiveInstallPlans(string archivePath, out IReadOnlyList<ArchiveInstallPlan> installPlans, out string errorMessage)
    {
        installPlans = Array.Empty<ArchiveInstallPlan>();
        errorMessage = string.Empty;

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var archiveEntries = archive.Entries
                .Select(entry => new ArchiveEntryInfo(entry, NormalizeArchivePath(entry.FullName)))
                .Where(entry => !string.IsNullOrEmpty(entry.NormalizedPath))
                .ToList();

            var manifestEntries = archiveEntries
                .Where(entry => !string.IsNullOrEmpty(entry.Entry.Name) && IsManifestCandidatePath(entry.NormalizedPath))
                .ToList();

            var manifestCountsByDirectory = manifestEntries
                .GroupBy(entry => GetArchiveDirectory(entry.NormalizedPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            var candidates = manifestEntries
                .Select(entry => TryReadArchiveManifestCandidate(entry, archiveEntries, manifestCountsByDirectory, out var candidate) ? candidate : (ArchiveInstallPlan?)null)
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .OrderBy(candidate => candidate.ManifestDepth)
                .ThenBy(candidate => candidate.EntryPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
            {
                errorMessage = loc.Get("ui.invalid_archive_manifest_message");
                return false;
            }

            installPlans = candidates;
            return true;
        }
        catch (InvalidDataException)
        {
            errorMessage = loc.Get("ui.unsupported_zip_archive_message");
            return false;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    private static bool TryReadArchiveManifestCandidate(
        ArchiveEntryInfo entry,
        IReadOnlyList<ArchiveEntryInfo> archiveEntries,
        IReadOnlyDictionary<string, int> manifestCountsByDirectory,
        out ArchiveInstallPlan installPlan)
    {
        installPlan = null!;

        var entryPath = entry.NormalizedPath;

        try
        {
            using var stream = entry.Entry.Open();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // A real mod manifest must declare a non-empty string "id". Without
            // this guard, any random JSON file in the archive (translation
            // tables, sample data, asset descriptors, etc.) would be picked up
            // as an install candidate.
            if (!document.RootElement.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                return false;
            }

            var segments = entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var directoryPath = GetArchiveDirectory(entryPath);
            var fileName = Path.GetFileName(entryPath);
            var fileStem = Path.GetFileNameWithoutExtension(fileName);
            var isSpecialManifestFile = string.Equals(fileName, ModLoader.ModManifestFileName, StringComparison.OrdinalIgnoreCase);

            var directoryName = string.IsNullOrEmpty(directoryPath)
                ? string.Empty
                : directoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
            var manifestCountInDirectory = manifestCountsByDirectory.TryGetValue(directoryPath, out var count)
                ? count
                : 0;
            var totalManifestCount = manifestCountsByDirectory.Values.Sum();

            var modId = fileStem;
            var modName = fileStem;
            string? modVersion = null;

            ModLoader.ReadManifestMetadata(document.RootElement, ref modId, ref modName, ref modVersion);

            var archiveFolderName = ResolveArchiveFolderName(directoryName, fileStem, modId, isSpecialManifestFile);
            if (string.IsNullOrWhiteSpace(archiveFolderName))
            {
                return false;
            }

            var installFolderName = modId.Trim();
            if (string.IsNullOrWhiteSpace(installFolderName))
            {
                return false;
            }

            var extractFullDirectory = manifestCountInDirectory == 1 &&
                (!string.IsNullOrEmpty(directoryPath) || isSpecialManifestFile || totalManifestCount == 1);

            var sourceEntries = extractFullDirectory
                ? GetArchiveDirectoryEntries(archiveEntries, directoryPath)
                : archiveEntries
                    .Where(candidate => IsMatchingModPayload(candidate.NormalizedPath, directoryPath, fileStem))
                    .Select(candidate => candidate.NormalizedPath)
                    .ToArray();

            if (sourceEntries.Length == 0)
            {
                return false;
            }

            installPlan = new ArchiveInstallPlan(
                modId,
                modName,
                modVersion,
                archiveFolderName,
                installFolderName,
                entryPath,
                segments.Length - 1,
                extractFullDirectory && !string.IsNullOrEmpty(directoryPath) ? directoryPath + "/" : string.Empty,
                extractFullDirectory,
                sourceEntries);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractArchiveToTemporaryFolder(string archivePath, ArchiveInstallPlan installPlan)
    {
        var extractionRoot = Path.Combine(Path.GetTempPath(), "sts2-mod-manager", Guid.NewGuid().ToString("N"));
        var destinationRoot = Path.Combine(extractionRoot, installPlan.InstallFolderName);
        Directory.CreateDirectory(destinationRoot);
        var selectedEntries = new HashSet<string>(installPlan.SourceEntries, StringComparer.OrdinalIgnoreCase);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var entryPath = NormalizeArchivePath(entry.FullName);
            if (string.IsNullOrEmpty(entryPath) || !selectedEntries.Contains(entryPath))
            {
                continue;
            }

            if (installPlan.ExtractFullDirectory)
            {
                if (!string.IsNullOrEmpty(installPlan.RootPrefix))
                {
                    entryPath = entryPath.Substring(installPlan.RootPrefix.Length);
                }

                if (string.IsNullOrEmpty(entryPath))
                {
                    continue;
                }
            }
            else
            {
                entryPath = Path.GetFileName(entryPath);
            }

            if (!TryGetSafeRelativePath(entryPath, out var relativePath))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationRoot, relativePath);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        return extractionRoot;
    }

    private static bool IsManifestCandidatePath(string entryPath)
    {
        var segments = entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 2)
        {
            return false;
        }

        return segments[^1].EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetArchiveDirectory(string entryPath)
    {
        var lastSeparatorIndex = entryPath.LastIndexOf('/');
        return lastSeparatorIndex < 0 ? string.Empty : entryPath.Substring(0, lastSeparatorIndex);
    }

    private static string[] GetArchiveDirectoryEntries(IReadOnlyList<ArchiveEntryInfo> archiveEntries, string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            return archiveEntries
                .Select(candidate => candidate.NormalizedPath)
                .ToArray();
        }

        return archiveEntries
            .Where(candidate => candidate.NormalizedPath.StartsWith(directoryPath + "/", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.NormalizedPath)
            .ToArray();
    }

    private static string ResolveArchiveFolderName(string directoryName, string fileStem, string modId, bool isSpecialManifestFile)
    {
        if (!string.IsNullOrWhiteSpace(directoryName))
        {
            return directoryName;
        }

        if (isSpecialManifestFile && !string.IsNullOrWhiteSpace(modId))
        {
            return modId.Trim();
        }

        return fileStem;
    }

    private static bool IsMatchingModPayload(string entryPath, string directoryPath, string fileStem)
    {
        if (!string.Equals(GetArchiveDirectory(entryPath), directoryPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(entryPath);
        if (!string.Equals(Path.GetFileNameWithoutExtension(fileName), fileStem, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".pck", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeArchivePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static bool TryGetSafeRelativePath(string archivePath, out string relativePath)
    {
        var segments = archivePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToArray();

        if (segments.Length == 0 || segments.Any(segment => segment == ".."))
        {
            relativePath = string.Empty;
            return false;
        }

        relativePath = Path.Combine(segments);
        return true;
    }
}
