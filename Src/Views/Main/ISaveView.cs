using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace STS2ModManager.Views.Main;

/// <summary>
/// Saves tab contract. Mirrors today's <c>SavesPage</c> public surface:
/// the view manages its own location/profile rendering; the presenter
/// configures retention and asks for refreshes after settings changes.
/// </summary>
/// <remarks>
/// Phase 4d will route create-backup / restore / delete / launch-with-profile
/// requests through <c>SavePresenter</c> instead of the page handling them
/// inline.
/// </remarks>
[SupportedOSPlatform("windows")]
internal interface ISaveView
{
    /// <summary>Fired when the user clicks Refresh on the saves tab.</summary>
    event Action? RefreshRequested;

    /// <summary>Fired when the user requests creation of a new backup for a save location.</summary>
    event Action<SaveLocation>? CreateBackupRequested;

    /// <summary>Fired when the user requests restoration of a backup profile.</summary>
    event Action<SaveProfileInfo>? RestoreProfileRequested;

    /// <summary>Fired when the user deletes a backup profile.</summary>
    event Action<SaveProfileInfo>? DeleteProfileRequested;

    /// <summary>Fired when the user renames a backup profile.</summary>
    event Action<SaveProfileInfo>? RenameProfileRequested;

    /// <summary>Fired when the user launches the game with a specific save profile.</summary>
    event Action<SaveProfileInfo>? LaunchWithProfileRequested;

    /// <summary>Apply a new backup retention setting (changed in the config tab).</summary>
    void SetBackupRetention(int count);

    /// <summary>Force the saves tab to re-enumerate locations + profiles, then report status.</summary>
    void RefreshData(string statusText);
}
