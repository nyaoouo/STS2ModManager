using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using STS2ModManager.Mods;

internal static class ModLoader
{
    public const string ModManifestFileName = "mod_manifest.json";

    public static List<ModInfo> LoadMods(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return new List<ModInfo>();
        }

        return Directory
            .EnumerateDirectories(sourceDirectory)
            .Select(path => new DirectoryInfo(path))
            .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ReadModInfo)
            .ToList();
    }

    public static ModInfo ReadModInfo(DirectoryInfo directory)
    {
        var folderName = directory.Name;
        var modId = folderName;
        var modName = folderName;
        string? modVersion = null;
        var manifestPath = GetManifestPath(directory.FullName, folderName);

        if (!string.IsNullOrEmpty(manifestPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                ReadManifestMetadata(document.RootElement, ref modId, ref modName, ref modVersion);
            }
            catch
            {
            }
        }

        return new ModInfo(modId, modName, modVersion, folderName, directory.FullName);
    }

    public static string? GetManifestPath(string directoryPath, string folderName)
    {
        var defaultManifestPath = Path.Combine(directoryPath, folderName + ".json");
        if (File.Exists(defaultManifestPath))
        {
            return defaultManifestPath;
        }

        var specialManifestPath = Path.Combine(directoryPath, ModManifestFileName);
        return File.Exists(specialManifestPath) ? specialManifestPath : null;
    }

    public static void ReadManifestMetadata(JsonElement manifestRoot, ref string modId, ref string modName, ref string? modVersion)
    {
        if (manifestRoot.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            modId = idElement.GetString() ?? modId;
        }

        if (manifestRoot.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
        {
            modName = nameElement.GetString() ?? modName;
        }

        if (manifestRoot.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.String)
        {
            modVersion = versionElement.GetString();
        }
    }
}

internal static class ModVersion
{
    public static int Compare(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(left))
        {
            return -1;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return 1;
        }

        if (Version.TryParse(Normalize(left), out var leftVersion) &&
            Version.TryParse(Normalize(right), out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.Trim(), right.Trim());
    }

    public static string Normalize(string version)
    {
        var cleaned = version.Trim();
        return cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? cleaned.Substring(1)
            : cleaned;
    }
}
