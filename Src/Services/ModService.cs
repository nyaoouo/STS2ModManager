using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace STS2ModManager.Services;

/// <summary>
/// Pure-IO helpers for moving, deleting, and locating mod folders. The form
/// layer keeps the UI orchestration (conflict prompts, status reporting); this
/// service just touches the file system and the mod directories.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ModService
{
    /// <summary>
    /// Returns every mod across the enabled and disabled directories whose id
    /// matches <paramref name="modId"/>, excluding any mods rooted at the given
    /// full paths.
    /// </summary>
    public static List<ModInfo> FindModsById(
        string modsDirectory,
        string disabledDirectory,
        string modId,
        params string[] excludedPaths)
    {
        var excluded = new HashSet<string>(
            excludedPaths.Select(path => Path.GetFullPath(path)),
            StringComparer.OrdinalIgnoreCase);

        return ModLoader.LoadMods(modsDirectory)
            .Concat(ModLoader.LoadMods(disabledDirectory))
            .Where(mod =>
                string.Equals(mod.Id, modId, StringComparison.OrdinalIgnoreCase) &&
                !excluded.Contains(Path.GetFullPath(mod.FullPath)))
            .ToList();
    }

    /// <summary>
    /// Recursively deletes the on-disk folder for each supplied mod (no-op for
    /// missing folders).
    /// </summary>
    public static void DeleteModDirectories(IEnumerable<ModInfo> mods)
    {
        foreach (var mod in mods)
        {
            TryDeleteDirectory(mod.FullPath);
        }
    }

    /// <summary>
    /// Deletes <paramref name="path"/> recursively if it exists; otherwise
    /// does nothing.
    /// </summary>
    public static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    /// Moves a directory; falls back to copy + delete when source and target
    /// live on different volumes (Windows <see cref="Directory.Move"/> cannot
    /// cross drive roots).
    /// </summary>
    public static void MoveDirectory(string sourcePath, string destinationPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destinationPath = Path.GetFullPath(destinationPath);

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: {sourcePath}");
        }

        if (Directory.Exists(destinationPath))
        {
            throw new IOException($"Destination directory already exists: {destinationPath}");
        }

        if (string.Equals(Path.GetPathRoot(sourcePath), Path.GetPathRoot(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        CopyDirectory(sourcePath, destinationPath);
        Directory.Delete(sourcePath, recursive: true);
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var filePath in Directory.EnumerateFiles(sourcePath))
        {
            var targetFilePath = Path.Combine(destinationPath, Path.GetFileName(filePath));
            File.Copy(filePath, targetFilePath, overwrite: false);
        }

        foreach (var childDirectoryPath in Directory.EnumerateDirectories(sourcePath))
        {
            var childDirectoryName = Path.GetFileName(childDirectoryPath);
            var targetChildDirectoryPath = Path.Combine(destinationPath, childDirectoryName);
            CopyDirectory(childDirectoryPath, targetChildDirectoryPath);
        }
    }
}
