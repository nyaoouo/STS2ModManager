namespace STS2ModManager.Models;

internal sealed record ModInfo(
    string Id,
    string Name,
    string? Version,
    string FolderName,
    string FullPath);
