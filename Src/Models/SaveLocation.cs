namespace STS2ModManager.Models;

/// <summary>
/// A single profile root. <see cref="RootPath"/> contains the
/// <c>profile{n}/saves</c> (vanilla) and <c>modded/profile{n}/saves</c> (modded) subtrees.
/// </summary>
internal sealed record SaveLocation(
    SaveLocationKind Kind,
    string Id,
    string DisplayName,
    string RootPath);
