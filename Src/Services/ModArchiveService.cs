using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;

namespace STS2ModManager.Services;

/// <summary>
/// Manages the multi-version mod archive at <c>&lt;game&gt;/&lt;archiveDir&gt;/</c>.
/// Each archived version of a mod is stored as a single zip:
/// <c>&lt;archive&gt;/&lt;ModId&gt;/&lt;version&gt;.zip</c> with internal layout
/// <c>&lt;ModId&gt;/&lt;files&gt;</c>. The active version (folder under
/// <c>&lt;game&gt;/mods/&lt;ModId&gt;/</c>) is always mirrored as a zip in the
/// archive (always-mirror invariant), so disabling a mod is just deleting
/// the active folder.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ModArchiveService
{
    public const string ArchiveDirectoryDefaultName = ".archive-mods";

    /// <summary>
    /// Discover every mod that has either an active install in
    /// <paramref name="modsDirectory"/> or one or more archived zips in
    /// <paramref name="archiveDirectory"/>.
    /// Versions inside each entry are ordered newest-version-first.
    /// </summary>
    public static IReadOnlyList<ModArchiveEntry> LoadAll(
        string modsDirectory,
        string archiveDirectory)
    {
        var entries = new Dictionary<string, (ModInfo? Active, List<ModVersionEntry> Versions)>(
            StringComparer.OrdinalIgnoreCase);

        // Active mods (by folder name, manifest-resolved id wins).
        foreach (var active in ModLoader.LoadMods(modsDirectory))
        {
            entries[active.Id] = (active, new List<ModVersionEntry>());
        }

        // Archived versions.
        if (Directory.Exists(archiveDirectory))
        {
            foreach (var modDir in Directory.EnumerateDirectories(archiveDirectory))
            {
                var modId = Path.GetFileName(modDir);
                if (string.IsNullOrEmpty(modId)) continue;

                if (!entries.TryGetValue(modId, out var bucket))
                {
                    bucket = (null, new List<ModVersionEntry>());
                    entries[modId] = bucket;
                }

                foreach (var zipPath in Directory.EnumerateFiles(modDir, "*.zip"))
                {
                    var version = TryReadVersionFromZip(zipPath, modId, out var info);
                    if (info == null) continue;
                    bucket.Versions.Add(new ModVersionEntry(
                        version,
                        Path.GetFileName(zipPath),
                        zipPath,
                        info));
                }
            }
        }

        return entries
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ModArchiveEntry(
                kv.Key,
                kv.Value.Active,
                kv.Value.Versions
                    .OrderByDescending(v => v.Info.Version, ModVersionComparer.Instance)
                    .ToList()))
            .ToList();
    }

    /// <summary>
    /// Outcome of <see cref="RunStartupMigration"/>.
    /// </summary>
    public sealed record MigrationReport(
        int FlatLayoutsConverted,
        int VersionsRecovered,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<MigrationItem> Items);

    /// <summary>Per-mod status emitted by <see cref="RunStartupMigration"/>.</summary>
    public sealed record MigrationItem(string ModId, MigrationStatus Status, string Detail);

    public enum MigrationStatus
    {
        AlreadyMigrated,
        Converted,
        Failed,
    }

    /// <summary>
    /// One-shot conversion of the legacy "flat" disabled-mods layout
    /// (<c>&lt;archive&gt;/&lt;Id&gt;/&lt;files&gt;</c>) into the new
    /// per-version zip layout (<c>&lt;archive&gt;/&lt;Id&gt;/&lt;v&gt;.zip</c>).
    /// Idempotent: calling on an already-migrated archive is a cheap no-op.
    /// </summary>
    public static MigrationReport RunStartupMigration(string archiveDirectory)
    {
        var warnings = new List<string>();
        var items = new List<MigrationItem>();
        if (!Directory.Exists(archiveDirectory))
        {
            return new MigrationReport(0, 0, warnings, items);
        }

        var converted = 0;
        var recovered = 0;

        foreach (var modDir in Directory.EnumerateDirectories(archiveDirectory))
        {
            var modId = Path.GetFileName(modDir);
            var children = Directory.EnumerateFileSystemEntries(modDir).ToList();
            if (children.Count == 0)
            {
                items.Add(new MigrationItem(modId, MigrationStatus.AlreadyMigrated, "empty"));
                continue;
            }

            // Already-new-layout: every immediate child is a .zip file.
            var allZips = children.All(p => File.Exists(p) &&
                string.Equals(Path.GetExtension(p), ".zip", StringComparison.OrdinalIgnoreCase));
            if (allZips)
            {
                items.Add(new MigrationItem(
                    modId,
                    MigrationStatus.AlreadyMigrated,
                    $"{children.Count} version(s) already zipped"));
                continue;
            }

            try
            {
                var info = ModLoader.ReadModInfo(new DirectoryInfo(modDir));
                var version = SanitiseVersion(info.Version)
                    ?? "unknown-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var zipPath = Path.Combine(modDir, version + ".zip");
                var dedupedZipPath = NextNonCollidingZipPath(zipPath);

                ZipFolderAsModRoot(modDir, modId, dedupedZipPath, excludeChildren: AllChildZips(children));

                // Delete the now-archived flat files (but keep any pre-existing .zip
                // siblings — they are themselves valid archived versions).
                foreach (var entry in Directory.EnumerateFileSystemEntries(modDir))
                {
                    if (string.Equals(entry, dedupedZipPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (File.Exists(entry) &&
                        string.Equals(Path.GetExtension(entry), ".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        recovered++;
                        continue;
                    }
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }
                converted++;
                items.Add(new MigrationItem(
                    modId,
                    MigrationStatus.Converted,
                    $"-> {Path.GetFileName(dedupedZipPath)}"));
            }
            catch (Exception ex)
            {
                warnings.Add($"{modId}: {ex.Message}");
                items.Add(new MigrationItem(modId, MigrationStatus.Failed, ex.Message));
            }
        }

        return new MigrationReport(converted, recovered, warnings, items);
    }

    /// <summary>
    /// Outcome of <see cref="InstallVersionFromFolder"/> so the caller can
    /// report what actually happened (used for the install-status message).
    /// </summary>
    public enum InstallOutcome
    {
        Added,
        Replaced,
        Skipped,
    }

    /// <summary>
    /// Add a new archived version from a freshly-extracted source folder
    /// (typically the temp dir produced by <see cref="ArchiveInstallService.ExtractArchiveToTemporaryFolder"/>).
    /// Writes <c>&lt;archive&gt;/&lt;modId&gt;/&lt;version&gt;.zip</c>.
    /// </summary>
    /// <param name="overwriteExisting">If a zip with that exact version already exists,
    /// <c>true</c> overwrites it, <c>false</c> appends <c>-1</c>, <c>-2</c>, ...</param>
    public static (string ZipPath, InstallOutcome Outcome) InstallVersionFromFolder(
        string archiveDirectory,
        string modId,
        string? version,
        string sourceFolder,
        bool overwriteExisting)
    {
        var safeVersion = SanitiseVersion(version)
            ?? "unknown-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var modArchiveDir = Path.Combine(archiveDirectory, modId);
        Directory.CreateDirectory(modArchiveDir);

        var preferredZip = Path.Combine(modArchiveDir, safeVersion + ".zip");
        var preferredExists = File.Exists(preferredZip);

        var targetZip = preferredExists && !overwriteExisting
            ? NextNonCollidingZipPath(preferredZip)
            : preferredZip;

        ZipFolderAsModRoot(sourceFolder, modId, targetZip, excludeChildren: null);

        var outcome = preferredExists
            ? (overwriteExisting ? InstallOutcome.Replaced : InstallOutcome.Added)
            : InstallOutcome.Added;
        return (targetZip, outcome);
    }

    /// <summary>
    /// Returns true when the archive already contains a zip with this exact
    /// sanitised version for <paramref name="modId"/>.
    /// </summary>
    public static bool HasArchivedVersion(string archiveDirectory, string modId, string? version)
    {
        var safeVersion = SanitiseVersion(version);
        if (safeVersion == null) return false;
        return File.Exists(Path.Combine(archiveDirectory, modId, safeVersion + ".zip"));
    }

    /// <summary>
    /// If the active mod has no matching zip in the archive, write one. Cheap
    /// no-op when the archive is already in sync (matching version + matching
    /// payload size).
    /// </summary>
    public static void EnsureActiveMirrored(
        string modsDirectory,
        string archiveDirectory,
        ModInfo active)
    {
        var version = SanitiseVersion(active.Version)
            ?? "unknown-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var modArchiveDir = Path.Combine(archiveDirectory, active.Id);
        Directory.CreateDirectory(modArchiveDir);

        var targetZip = Path.Combine(modArchiveDir, version + ".zip");
        if (File.Exists(targetZip) && IsZipUpToDate(targetZip, active.FullPath))
        {
            return;
        }

        var dedupedTarget = File.Exists(targetZip)
            ? NextNonCollidingZipPath(targetZip)
            : targetZip;

        ZipFolderAsModRoot(active.FullPath, active.Id, dedupedTarget, excludeChildren: null);
    }

    /// <summary>
    /// Switch the active version of <paramref name="modId"/> to the archived
    /// version with file name <paramref name="zipFileName"/>.
    /// </summary>
    public static void ActivateVersion(
        string modsDirectory,
        string archiveDirectory,
        ModInfo? currentActive,
        string modId,
        string zipFileName)
    {
        var sourceZip = Path.Combine(archiveDirectory, modId, zipFileName);
        if (!File.Exists(sourceZip))
        {
            throw new FileNotFoundException("Archived version not found.", sourceZip);
        }

        // (1) Mirror current active so we never lose the version that is about
        // to be displaced.
        if (currentActive != null)
        {
            EnsureActiveMirrored(modsDirectory, archiveDirectory, currentActive);
        }

        var activePath = Path.Combine(modsDirectory, modId);
        var stagingPath = Path.Combine(modsDirectory,
            "." + modId + ".swap-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        // (2) Extract the chosen version into staging.
        try
        {
            ExtractModZip(sourceZip, modId, stagingPath);
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                try { Directory.Delete(stagingPath, recursive: true); } catch { }
            }
            throw;
        }

        // (3) Replace active with staging (best effort atomic).
        var trashPath = activePath + ".trash-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        try
        {
            if (Directory.Exists(activePath))
            {
                Directory.Move(activePath, trashPath);
            }
            Directory.Move(stagingPath, activePath);
            if (Directory.Exists(trashPath))
            {
                Directory.Delete(trashPath, recursive: true);
            }
        }
        catch
        {
            // Rollback: put the old active back.
            if (Directory.Exists(stagingPath))
            {
                try { Directory.Delete(stagingPath, recursive: true); } catch { }
            }
            if (!Directory.Exists(activePath) && Directory.Exists(trashPath))
            {
                try { Directory.Move(trashPath, activePath); } catch { }
            }
            throw;
        }
    }

    /// <summary>
    /// Delete an archived version. Throws if it is the only mirror of the
    /// currently active version (caller must use <c>force: true</c> to override).
    /// </summary>
    public static void DeleteVersion(
        string archiveDirectory,
        ModInfo? currentActive,
        string modId,
        string zipFileName,
        bool force = false)
    {
        if (!force && currentActive != null &&
            string.Equals(currentActive.Id, modId, StringComparison.OrdinalIgnoreCase))
        {
            var activeVersion = SanitiseVersion(currentActive.Version);
            var stem = Path.GetFileNameWithoutExtension(zipFileName);
            if (string.Equals(activeVersion, stem, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing to delete the only archived copy of the active version.");
            }
        }

        var path = Path.Combine(archiveDirectory, modId, zipFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Delete every archived version of <paramref name="modId"/>. When the mod
    /// is currently active, the zip mirroring the active version is preserved
    /// (so the user can still uninstall + re-archive cleanly later). Returns
    /// the number of zips actually removed.
    /// </summary>
    public static int DeleteAllVersions(
        string archiveDirectory,
        ModInfo? currentActive,
        string modId)
    {
        var modArchiveDir = Path.Combine(archiveDirectory, modId);
        if (!Directory.Exists(modArchiveDir))
        {
            return 0;
        }

        string? protectedStem = null;
        if (currentActive != null &&
            string.Equals(currentActive.Id, modId, StringComparison.OrdinalIgnoreCase))
        {
            protectedStem = SanitiseVersion(currentActive.Version);
        }

        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(modArchiveDir, "*.zip"))
        {
            if (protectedStem != null &&
                string.Equals(Path.GetFileNameWithoutExtension(path), protectedStem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                File.Delete(path);
                deleted++;
            }
            catch
            {
                // Best effort: skip files we can't remove.
            }
        }

        // If nothing remains in the per-mod directory, drop it too.
        if (!Directory.EnumerateFileSystemEntries(modArchiveDir).Any())
        {
            try { Directory.Delete(modArchiveDir); } catch { }
        }

        return deleted;
    }

    // ---------- helpers ----------

    /// <summary>
    /// Write <paramref name="sourceFolder"/> into <paramref name="targetZipPath"/>
    /// using a single <paramref name="rootEntryName"/>/ root inside the zip,
    /// matching the layout we use for distribution archives. Goes through a
    /// <c>.tmp</c> file then atomic-rename for crash safety.
    /// </summary>
    private static void ZipFolderAsModRoot(
        string sourceFolder,
        string rootEntryName,
        string targetZipPath,
        IReadOnlyCollection<string>? excludeChildren)
    {
        var tmp = targetZipPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);

        var excluded = excludeChildren == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(excludeChildren.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

        // Always exclude the .tmp file we are about to write (it lives inside
        // sourceFolder for the migration case) and any final-name target zip
        // that may already sit alongside it.
        excluded.Add(Path.GetFullPath(tmp));
        excluded.Add(Path.GetFullPath(targetZipPath));

        // Snapshot the file list BEFORE creating the .tmp so the enumeration
        // never sees the in-progress zip and tries to read its locked handle.
        var filesToZip = Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories)
            .Where(f => !excluded.Contains(Path.GetFullPath(f)))
            .ToList();

        using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var file in filesToZip)
            {
                var rel = Path.GetRelativePath(sourceFolder, file).Replace('\\', '/');
                var entryName = rootEntryName + "/" + rel;
                var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var src = File.OpenRead(file);
                src.CopyTo(entryStream);
            }
        }

        if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
        File.Move(tmp, targetZipPath);
    }

    /// <summary>
    /// Extract a <c>&lt;modId&gt;/...</c>-rooted zip into
    /// <paramref name="targetFolder"/>, stripping the root prefix.
    /// </summary>
    private static void ExtractModZip(string zipPath, string modId, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

        // Determine root prefix (any non-modId root is treated as "no prefix").
        string? rootPrefix = null;
        foreach (var entry in zip.Entries)
        {
            var slash = entry.FullName.IndexOf('/');
            if (slash <= 0) continue;
            var first = entry.FullName.Substring(0, slash);
            if (!string.Equals(first, modId, StringComparison.OrdinalIgnoreCase))
            {
                rootPrefix = null;
                break;
            }
            rootPrefix = first + "/";
        }

        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (rootPrefix != null && name.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(rootPrefix.Length);
            }
            if (string.IsNullOrEmpty(name)) continue;

            var dest = Path.Combine(targetFolder, name);
            if (name.EndsWith("/", StringComparison.Ordinal) || string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(dest);
                continue;
            }
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    /// <summary>
    /// Cheap up-to-date check: same version + same uncompressed total size.
    /// </summary>
    private static bool IsZipUpToDate(string zipPath, string activeFolder)
    {
        try
        {
            var folderTotal = Directory
                .EnumerateFiles(activeFolder, "*", SearchOption.AllDirectories)
                .Sum(p => new FileInfo(p).Length);

            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var zipTotal = zip.Entries.Sum(e => e.Length);
            return zipTotal == folderTotal;
        }
        catch
        {
            return false;
        }
    }

    private static string TryReadVersionFromZip(string zipPath, string modId, out ModInfo? info)
    {
        info = null;
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var manifestEntry = zip.Entries.FirstOrDefault(e =>
                e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(e.Name, modId + ".json", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(e.Name, ModLoader.ModManifestFileName, StringComparison.OrdinalIgnoreCase)));
            if (manifestEntry == null) return Path.GetFileNameWithoutExtension(zipPath);

            using var stream = manifestEntry.Open();
            using var doc = JsonDocument.Parse(stream);

            var id = modId;
            var name = modId;
            string? version = null;
            ModLoader.ReadManifestMetadata(doc.RootElement, ref id, ref name, ref version);

            var fileStem = Path.GetFileNameWithoutExtension(zipPath);
            info = new ModInfo(id, name, version, modId, zipPath);
            return SanitiseVersion(version) ?? fileStem;
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(zipPath);
        }
    }

    private static string? SanitiseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var trimmed = version.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var sanitised = new string(trimmed.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitised) ? null : sanitised;
    }

    private static string NextNonCollidingZipPath(string preferredPath)
    {
        if (!File.Exists(preferredPath)) return preferredPath;
        var dir = Path.GetDirectoryName(preferredPath)!;
        var stem = Path.GetFileNameWithoutExtension(preferredPath);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}-{i}.zip");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("Could not find a non-colliding archive zip name.");
    }

    private static IReadOnlyCollection<string> AllChildZips(IEnumerable<string> children)
        => children
            .Where(p => File.Exists(p) &&
                string.Equals(Path.GetExtension(p), ".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private sealed class ModVersionComparer : IComparer<string?>
    {
        public static readonly ModVersionComparer Instance = new();
        public int Compare(string? x, string? y) => ModVersion.Compare(x, y);
    }
}
