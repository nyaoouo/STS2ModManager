using System;

namespace STS2ModManager.Models;

internal sealed record SaveProfileInfo(
    SaveLocation Location,
    SaveKind Kind,
    int ProfileId,
    string DirectoryPath,
    bool HasData,
    DateTime? LastModified,
    bool HasCurrentRun,
    int FileCount);
