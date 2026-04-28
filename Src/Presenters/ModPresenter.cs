using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using STS2ModManager.Models;
using STS2ModManager.Views.Dialogs;
using STS2ModManager.Services;
using STS2ModManager.Services.UI;
using STS2ModManager.Views.Main;

namespace STS2ModManager.Presenters;

/// <summary>
/// Mods-tab presenter. Owns the directory paths the view needs to operate
/// on and exposes <see cref="ModService"/> + <see cref="ArchiveInstallService"/>
/// behind clean intent events.
/// </summary>
/// <remarks>
/// Phase 5d: the presenter now owns the load-from-disk path
/// (<see cref="Reload"/>); the view is a passive renderer fed via
/// <see cref="IModView.SetMods"/> / <see cref="IModView.SetFilterCounts"/> /
/// <see cref="IModView.SetSelection"/>. Per-mod mutations still live in
/// the view but request a reload via the <c>requestReload</c> callback the
/// host wires to <see cref="Reload"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class ModPresenter
{
    private readonly IModView view;
    private readonly LocalizationService localization;
    private readonly Func<string> getModsDirectory;
    private readonly Func<string> getDisabledDirectory;
    private readonly Action<string> setStatus;
    private readonly Action onDirectoriesChanged;
    private readonly Func<System.Windows.Forms.IWin32Window?> getDialogOwner;
    private readonly Action<bool> toggleAll;
    private readonly Action openSelectedModFolder;
    private readonly Action<IReadOnlyList<string>> installArchives;
    private readonly Action exportSelected;
    private readonly Action openModsFolder;

    public ModPresenter(
        IModView view,
        LocalizationService localization,
        Func<string> getModsDirectory,
        Func<string> getDisabledDirectory,
        Action<string> setStatus,
        Action onDirectoriesChanged,
        Func<System.Windows.Forms.IWin32Window?> getDialogOwner,
        Action<bool> toggleAll,
        Action openSelectedModFolder,
        Action<IReadOnlyList<string>> installArchives,
        Action exportSelected,
        Action openModsFolder)
    {
        this.view = view;
        this.localization = localization;
        this.getModsDirectory = getModsDirectory;
        this.getDisabledDirectory = getDisabledDirectory;
        this.setStatus = setStatus;
        this.onDirectoriesChanged = onDirectoriesChanged;
        this.getDialogOwner = getDialogOwner;
        this.toggleAll = toggleAll;
        this.openSelectedModFolder = openSelectedModFolder;
        this.installArchives = installArchives;
        this.exportSelected = exportSelected;
        this.openModsFolder = openModsFolder;

        view.RefreshRequested += OnRefreshRequested;
        view.EnableAllRequested += OnEnableAll;
        view.DisableAllRequested += OnDisableAll;
        view.OpenModFolderRequested += OnOpenSelectedFolder;
        view.OpenModsFolderRequested += OnOpenModsFolder;
        view.ArchivesDropped += OnArchivesDropped;
    }

    /// <summary>Loads the mod lists from disk and pushes the result into
    /// the view via <see cref="IModView.SetMods"/> / <see cref="IModView.SetFilterCounts"/>.
    /// </summary>
    public void Reload(string statusText)
    {
        try
        {
            onDirectoriesChanged();

            var modsDir = getModsDirectory();
            var archiveDir = getDisabledDirectory();

            var archives = ModArchiveService.LoadAll(modsDir, archiveDir);

            var enabled = new List<ModInfo>();
            var disabled = new List<ModInfo>();
            var versionMap = new Dictionary<string, IReadOnlyList<ModVersionEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in archives)
            {
                versionMap[entry.Id] = entry.ArchivedVersions;

                if (entry.Active is { } active)
                {
                    // Mirror the active install into the archive so the
                    // version chooser always sees it. Best-effort -- a
                    // stale mirror is corrected on the next reload.
                    try
                    {
                        ModArchiveService.EnsureActiveMirrored(modsDir, archiveDir, active);
                    }
                    catch
                    {
                        // Swallow: mirror failure shouldn't block the UI.
                    }

                    enabled.Add(active);
                }
                else if (entry.ArchivedVersions.Count > 0)
                {
                    // Synthesize a placeholder mod that points at the per-mod
                    // archive directory so "open folder" surfaces the zips.
                    var newest = entry.ArchivedVersions[0];
                    var placeholderFolder = System.IO.Path.Combine(archiveDir, entry.Id);
                    disabled.Add(newest.Info with
                    {
                        FullPath = placeholderFolder,
                        FolderName = entry.Id,
                    });
                }
            }

            view.SetFilterCounts(enabled.Count, disabled.Count);
            view.SetMods(enabled, disabled);
            view.SetArchiveData(versionMap);
            view.SetSelection(Array.Empty<string>());
            setStatus(statusText);
        }
        catch (Exception exception)
        {
            setStatus(localization.Get("mods.load_failed_status", exception.Message));
            var owner = getDialogOwner();
            if (owner is not null)
            {
                MessageDialog.Error(
                    owner,
                    localization,
                    localization.Get("mods.load_error_title"),
                    exception.Message);
            }
        }
    }

    private void OnRefreshRequested()
        => Reload(localization.Get("status.reloaded_mod_list_status"));

    private void OnEnableAll() => toggleAll(true);

    private void OnDisableAll() => toggleAll(false);

    /// <summary>The form fires this event without a payload today; the
    /// per-mod argument is unused until Phase 5.</summary>
    private void OnOpenSelectedFolder(ModInfo _) => openSelectedModFolder();

    private void OnOpenModsFolder() => openModsFolder();

    private void OnArchivesDropped(IReadOnlyList<string> paths) => installArchives(paths);

    /// <summary>Allows the form to ask the presenter to handle export
    /// without exposing it as a view event (Phase 5 will move it).</summary>
    public void RequestExportSelected() => exportSelected();
}
