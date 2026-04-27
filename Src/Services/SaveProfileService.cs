using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace STS2ModManager.Services;

/// <summary>
/// Filesystem operations for save profile discovery and copying.
/// Steam saves live at  <c>%AppData%/SlayTheSpire2/steam/{steamId}/...</c>.
/// Local-default players live at <c>%AppData%/SlayTheSpire2/default/{playerId}/...</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SaveProfileService
{
    private const int ProfileSlotCount = 3;

    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2");

    public static string SteamRoot { get; } = Path.Combine(AppDataRoot, "steam");

    public static string LocalDefaultRoot { get; } = Path.Combine(AppDataRoot, "default");

    public static IReadOnlyList<SaveLocation> EnumerateLocations()
    {
        var list = new List<SaveLocation>();

        if (Directory.Exists(SteamRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(SteamRoot)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(dir)!;
                list.Add(new SaveLocation(
                    SaveLocationKind.SteamUser,
                    id,
                    BuildSteamDisplayName(id),
                    dir));
            }
        }

        if (Directory.Exists(LocalDefaultRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(LocalDefaultRoot)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(dir)!;
                list.Add(new SaveLocation(
                    SaveLocationKind.LocalDefault,
                    id,
                    BuildLocalDisplayName(id),
                    dir));
            }
        }

        return list;
    }

    public static IReadOnlyList<SaveProfileInfo> EnumerateProfiles(SaveLocation location, SaveKind kind)
    {
        var profiles = new List<SaveProfileInfo>(ProfileSlotCount);
        for (var slot = 1; slot <= ProfileSlotCount; slot++)
        {
            profiles.Add(ReadProfile(location, kind, slot));
        }
        return profiles;
    }

    public static SaveProfileInfo ReadProfile(SaveLocation location, SaveKind kind, int profileId)
    {
        var directoryPath = ProfileDirectory(location, kind, profileId);
        var progressSavePath = Path.Combine(directoryPath, "progress.save");
        var currentRunPath = Path.Combine(directoryPath, "current_run.save");
        var hasData = File.Exists(progressSavePath);
        var fileCount = Directory.Exists(directoryPath)
            ? Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).Count()
            : 0;

        return new SaveProfileInfo(
            location,
            kind,
            profileId,
            directoryPath,
            hasData,
            hasData ? File.GetLastWriteTime(progressSavePath) : null,
            File.Exists(currentRunPath),
            fileCount);
    }

    /// <summary>
    /// Copy <paramref name="source"/> into <paramref name="target"/>, backing up the
    /// destination if it has data. Returns the backup path or <c>null</c> if no
    /// backup was made.
    /// </summary>
    public static string? CopyProfile(SaveProfileInfo source, SaveProfileInfo target, int backupRetention)
    {
        if (!Directory.Exists(source.DirectoryPath))
        {
            throw new DirectoryNotFoundException($"Source save directory does not exist: {source.DirectoryPath}");
        }

        string? backupPath = null;
        if (target.HasData)
        {
            backupPath = BackupProfile(target, backupRetention);
        }

        Directory.CreateDirectory(target.DirectoryPath);
        ClearDirectoryContents(target.DirectoryPath);
        CopyDirectoryContents(source.DirectoryPath, target.DirectoryPath);
        return backupPath;
    }

    private static string ProfileDirectory(SaveLocation location, SaveKind kind, int profileId)
    {
        return kind == SaveKind.Vanilla
            ? Path.Combine(location.RootPath, $"profile{profileId}", "saves")
            : Path.Combine(location.RootPath, "modded", $"profile{profileId}", "saves");
    }

    private static string? BackupProfile(SaveProfileInfo profile, int backupRetention)
    {
        var typeCode = profile.Kind == SaveKind.Vanilla ? "normal" : "modded";
        var backupRoot = Path.Combine(profile.Location.RootPath, "backups");
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{typeCode}_p{profile.ProfileId}_auto_before_transfer_{timestamp}");

        Directory.CreateDirectory(backupPath);
        CopyDirectoryContents(profile.DirectoryPath, backupPath);
        CleanupOldBackups(backupRoot, typeCode, backupRetention);
        return backupPath;
    }

    private static void CleanupOldBackups(string backupRoot, string typeCode, int keepCount)
    {
        if (!Directory.Exists(backupRoot)) return;

        var stale = Directory.EnumerateDirectories(backupRoot)
            .Where(p => Path.GetFileName(p).StartsWith(typeCode + "_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Skip(Math.Max(1, keepCount))
            .ToList();
        foreach (var path in stale)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private static void ClearDirectoryContents(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;
        foreach (var file in Directory.EnumerateFiles(directoryPath)) File.Delete(file);
        foreach (var child in Directory.EnumerateDirectories(directoryPath)) Directory.Delete(child, recursive: true);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var child in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectoryContents(child, Path.Combine(destinationDirectory, Path.GetFileName(child)));
        }
    }

    private static string BuildSteamDisplayName(string steamId)
    {
        // Abbreviate long ids in the middle so the combobox stays readable.
        var pretty = steamId.Length > 14
            ? $"{steamId.Substring(0, 6)}\u2026{steamId.Substring(steamId.Length - 6)}"
            : steamId;
        return $"Steam \u00b7 {pretty}";
    }

    private static string BuildLocalDisplayName(string playerId)
    {
        return $"Local \u00b7 {playerId}";
    }
}
