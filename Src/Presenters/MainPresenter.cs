using System;
using System.Runtime.Versioning;
using STS2ModManager.Services;
using STS2ModManager.Services.UI;
using STS2ModManager.Views.Main;

namespace STS2ModManager.Presenters;

/// <summary>
/// Top-level presenter. Owns references to the cross-cutting services
/// (<see cref="LocalizationService"/>, <see cref="ThemeController"/>,
/// <see cref="SettingsService"/>) and forwards window-chrome events
/// (<see cref="IMainView.RefreshAllRequested"/>,
/// <see cref="IMainView.PageChanged"/>) to host-supplied callbacks.
/// </summary>
/// <remarks>
/// Phase 4b introduces this class as a thin coordinator. The form still
/// owns the page-level handlers and passes their entry points in via the
/// constructor callbacks; later sub-phases (4c\u20134e) replace those
/// callbacks with dedicated child presenters.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class MainPresenter
{
    private readonly IMainView view;
    private readonly LocalizationService localization;
    private readonly Action<string> reloadLists;
    private readonly Action<AppPage> applyActivePageSideEffects;

    public MainPresenter(
        IMainView view,
        LocalizationService localization,
        ThemeController themeController,
        SettingsService settingsService,
        Action<string> reloadLists,
        Action<AppPage> applyActivePageSideEffects)
    {
        this.view = view;
        this.localization = localization;
        Theme = themeController;
        Settings = settingsService;
        this.reloadLists = reloadLists;
        this.applyActivePageSideEffects = applyActivePageSideEffects;

        view.RefreshAllRequested += OnRefreshAllRequested;
        view.PageChanged += OnPageChanged;
    }

    /// <summary>Cross-cutting theme controller (settings dialog reads it).</summary>
    public ThemeController Theme { get; }

    /// <summary>Settings IO (used by future ConfigPresenter to persist changes).</summary>
    public SettingsService Settings { get; }

    /// <summary>Localization access for child presenters.</summary>
    public LocalizationService Localization => localization;

    private void OnRefreshAllRequested()
    {
        reloadLists(localization.Get("status.reloaded_mod_list_status"));
    }

    private void OnPageChanged(AppPage page)
    {
        applyActivePageSideEffects(page);
    }
}
