namespace STS2ModManager.Saves;

internal enum SaveKind
{
    Vanilla,
    Modded
}

internal enum SaveLocationKind
{
    SteamUser,
    LocalDefault,
}

/// <summary>
/// A single profile root. <see cref="RootPath"/> contains the
/// <c>profile{n}/saves</c> (vanilla) and <c>modded/profile{n}/saves</c> (modded)
/// subtrees.
/// </summary>
internal sealed record SaveLocation(
    SaveLocationKind Kind,
    string Id,
    string DisplayName,
    string RootPath);

internal sealed record SaveProfileInfo(
    SaveLocation Location,
    SaveKind Kind,
    int ProfileId,
    string DirectoryPath,
    bool HasData,
    System.DateTime? LastModified,
    bool HasCurrentRun,
    int FileCount);
