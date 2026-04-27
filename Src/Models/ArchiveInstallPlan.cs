using System.Collections.Generic;

namespace STS2ModManager.Models;

internal sealed record ArchiveInstallPlan(
    string Id,
    string Name,
    string? Version,
    string ArchiveFolderName,
    string InstallFolderName,
    string EntryPath,
    int ManifestDepth,
    string RootPrefix,
    bool ExtractFullDirectory,
    IReadOnlyList<string> SourceEntries);
