using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using STS2ModManager.Models;
using STS2ModManager.Services.UI;

namespace STS2ModManager.Views.Main;

/// <summary>
/// Mods tab contract. The view raises events for user intent and the
/// presenter pushes back the lists, selection, and counters.
/// </summary>
/// <remarks>
/// Phase 4a is a skeleton; Phase 4c moves the toggle/install/uninstall
/// orchestration out of <c>MainForm.ModsPage.cs</c> into <c>ModPresenter</c>
/// and grows this interface as needed.
/// </remarks>
[SupportedOSPlatform("windows")]
internal interface IModView
{
    /// <summary>Fired when the user clicks the Refresh button.</summary>
    event Action? RefreshRequested;

    /// <summary>Fired when the user toggles a mod's enable/disable switch.</summary>
    event Action<ModInfo>? ToggleModRequested;

    /// <summary>Fired when the user requests uninstall of a mod.</summary>
    event Action<ModInfo>? UninstallModRequested;

    /// <summary>Fired when the user opens a mod's folder.</summary>
    event Action<ModInfo>? OpenModFolderRequested;

    /// <summary>Fired when the user clicks Open Mods Folder.</summary>
    event Action? OpenModsFolderRequested;

    /// <summary>Fired when the user clicks Enable All.</summary>
    event Action? EnableAllRequested;

    /// <summary>Fired when the user clicks Disable All.</summary>
    event Action? DisableAllRequested;

    /// <summary>Fired when the search box text changes (debounced by the view).</summary>
    event Action<string>? SearchTextChanged;

    /// <summary>Fired when the active filter chip changes.</summary>
    event Action<ModFilter>? FilterChanged;

    /// <summary>Fired when one or more archive paths are dropped on the mods page.</summary>
    event Action<IReadOnlyList<string>>? ArchivesDropped;

    /// <summary>Fired when the user clicks the Restart Game button on the mods toolbar.</summary>
    event Action? RestartGameRequested;

    /// <summary>Fired when the user picks a different archived version for a mod.</summary>
    event Action<ModInfo, ModVersionEntry>? ActivateVersionRequested;

    /// <summary>Fired when the user requests deletion of an archived version.</summary>
    event Action<ModInfo, ModVersionEntry>? DeleteVersionRequested;

    /// <summary>Fired when the user requests deletion of every archived version of a mod.</summary>
    event Action<ModInfo>? DeleteAllVersionsRequested;

    /// <summary>Push the current enabled/disabled mod lists into the view.</summary>
    void SetMods(IReadOnlyList<ModInfo> enabled, IReadOnlyList<ModInfo> disabled);

    /// <summary>Push the per-mod archived-version lists used by the version chooser.</summary>
    void SetArchiveData(IReadOnlyDictionary<string, IReadOnlyList<ModVersionEntry>> versionsByModId);

    /// <summary>Update the count badges next to each filter chip.</summary>
    void SetFilterCounts(int enabledCount, int disabledCount);

    /// <summary>Restore the mods that should appear selected after a list refresh.</summary>
    void SetSelection(IReadOnlyList<string> selectedFullPaths);

    /// <summary>Read the currently selected mods (whatever the user has highlighted).</summary>
    IReadOnlyList<ModInfo> GetSelectedMods();

    /// <summary>Mirrors the user's "split mod list" preference; controls the all/enabled/disabled headers.</summary>
    bool SplitModList { get; set; }

    /// <summary>Re-runs card diff + repaint without reloading from disk (theme/lang change).</summary>
    void RefreshDisplay();

    /// <summary>Re-applies localized text to toolbar controls (search hint, chip labels, button text).</summary>
    void ApplyLocalization(LocalizationService loc);
}
