using System.Collections.Generic;

namespace STS2ModManager.Models;

/// <summary>
/// One archived version of a mod, stored as a single zip under
/// <c>&lt;archiveDir&gt;/&lt;ModId&gt;/&lt;version&gt;.zip</c>.
/// The zip's top-level entry is always a single <c>&lt;ModId&gt;/</c> dir, so
/// it can be re-installed via the same archive-install code path used for
/// freshly-downloaded mods.
/// </summary>
internal sealed record ModVersionEntry(
    string Version,
    string ZipFileName,
    string ZipFullPath,
    ModInfo Info);

/// <summary>
/// Aggregate state for a single mod id: the currently active install (if any)
/// plus every archived version on disk, ordered newest-version-first.
/// The active version (when present) is also mirrored as a zip in the archive
/// so disabling = simply remove the active folder.
/// </summary>
internal sealed record ModArchiveEntry(
    string Id,
    ModInfo? Active,
    IReadOnlyList<ModVersionEntry> ArchivedVersions);
