using System;
using System.Runtime.Versioning;
using STS2ModManager.Services.UI;
using STS2ModManager.Views.Main;

namespace STS2ModManager.Presenters;

/// <summary>
/// Saves-tab presenter. Coordinates the save view's lifecycle hooks
/// (retention changed, refresh requested) and \u2014 in later phases \u2014 the
/// per-profile actions (restore, delete, rename, launch with profile).
/// </summary>
/// <remarks>
/// Today's <c>SavesPage</c> already calls <c>SaveProfileService</c>
/// directly. Phase 4d formalizes the contract via <see cref="ISaveView"/>
/// and pipes the host\u2019s "settings changed \u2192 retention update" event
/// through this presenter so future presenters can listen too. Phase 5
/// will move the per-profile orchestration here.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class SavePresenter
{
    private readonly ISaveView view;
    private readonly LocalizationService localization;
    private readonly Action requestRefresh;

    public SavePresenter(
        ISaveView view,
        LocalizationService localization,
        Action requestRefresh)
    {
        this.view = view;
        this.localization = localization;
        this.requestRefresh = requestRefresh;

        view.RefreshRequested += OnRefreshRequested;
    }

    /// <summary>Push a new retention setting from the config tab into the view.</summary>
    public void ApplyBackupRetention(int count)
    {
        view.SetBackupRetention(count);
    }

    /// <summary>Force the saves view to re-enumerate locations + profiles.</summary>
    public void RefreshNow()
    {
        view.RefreshData(localization.Get("saves.save_profiles_reloaded_status"));
    }

    private void OnRefreshRequested() => requestRefresh();
}
