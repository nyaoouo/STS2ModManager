using System.Collections.Generic;
using System.IO.Compression;

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

internal sealed record ArchiveInstallStepResult(OperationResult Result, bool StopProcessingArchive);

internal sealed record ArchiveEntryInfo(ZipArchiveEntry Entry, string NormalizedPath);

internal sealed record OperationResult(bool RefreshRequired, string Message);

internal sealed record ModMoveResult(ModMoveOutcome Outcome, string Message, string? NewFullPath = null);

internal sealed record LatestReleaseInfo(string Version, string ReleasePageUrl);
