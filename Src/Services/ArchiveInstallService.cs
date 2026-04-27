using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using STS2ModManager.Services.UI;

namespace STS2ModManager.Services;

/// <summary>
/// Pure (no WinForms) helpers for inspecting and unpacking mod archives.
/// The form layer keeps the orchestration that touches UI state (conflict
/// dialogs, status updates, list reloads); this service just plans and
/// extracts.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ArchiveInstallService
{
    /// <summary>
    /// Inspects a .zip archive and returns one install plan per detected mod
    /// manifest. Localized error messages are produced via <paramref name="loc"/>.
    /// </summary>
    public static bool TryReadArchiveInstallPlans(
        string archivePath,
        LocalizationService loc,
        out IReadOnlyList<ArchiveInstallPlan> installPlans,
        out string errorMessage)
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

    /// <summary>
    /// Extracts the entries selected by <paramref name="installPlan"/> into a
    /// fresh subdirectory of the system temp folder, returning the root path
    /// (the caller is responsible for cleanup).
    /// </summary>
    public static string ExtractArchiveToTemporaryFolder(string archivePath, ArchiveInstallPlan installPlan)
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

    public static string NormalizeArchivePath(string path)
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
