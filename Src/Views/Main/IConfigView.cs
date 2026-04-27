using System;
using System.Runtime.Versioning;

namespace STS2ModManager.Views.Main;

/// <summary>
/// Config tab contract. The view owns the form fields; the presenter
/// validates the snapshot, persists settings, and pushes back the
/// version-info banner once the GitHub release check completes.
/// </summary>
/// <remarks>
/// Today's <c>ConfigView</c> takes three callback delegates
/// (autoDetectGameDirectory, applyConfiguration, setStatus) plus a
/// <c>UpdateVersionInfo</c> sink. Phase 4e formalizes those as the
/// events / methods below and lets <c>ConfigPresenter</c> wire them to
/// services.
/// </remarks>
[SupportedOSPlatform("windows")]
internal interface IConfigView
{
    /// <summary>Fired when the user submits the configuration form.</summary>
    event Action<ModManagerConfig>? ApplyConfigurationRequested;

    /// <summary>
    /// Fired when the user clicks Auto-Detect on the game path field. The
    /// presenter resolves a path via <c>LaunchService</c> and pushes it back
    /// through <see cref="SetGameDirectory"/>.
    /// </summary>
    event Action? AutoDetectGameDirectoryRequested;

    /// <summary>Replace the value of the game-path field (post auto-detect).</summary>
    void SetGameDirectory(string gameDirectory);

    /// <summary>Update the current/latest version banner.</summary>
    void UpdateVersionInfo(string currentVersion, string? latestVersion);
}
