using System.IO.Compression;

namespace STS2ModManager.Models;

internal sealed record ArchiveEntryInfo(ZipArchiveEntry Entry, string NormalizedPath);
