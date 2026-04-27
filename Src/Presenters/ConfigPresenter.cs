using System;
using System.Runtime.Versioning;
using STS2ModManager.Services;
using STS2ModManager.Services.UI;
using STS2ModManager.Views.Main;

namespace STS2ModManager.Presenters;

/// <summary>
/// Config-tab presenter. Owns the apply-configuration + auto-detect-game
/// flows; later phases will fold launch / restart / force-stop game and the
/// update prompt into here too.
/// </summary>
/// <remarks>
/// Phase 4e introduces this presenter. The legacy
/// <c>autoDetectGameDirectory</c>/<c>applyConfiguration</c> callbacks
/// passed to <see cref="ConfigView"/> remain as constructor parameters
/// for now (kept nullable). When the presenter is wired they take
/// precedence; in tests / future Phase 5 wiring the page will be
/// constructed with all callbacks <c>null</c>.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class ConfigPresenter
{
    private readonly IConfigView view;
    private readonly LocalizationService localization;
    private readonly Func<string> autoDetectGameDirectory;
    private readonly Action<ModManagerConfig> applyConfiguration;

    public ConfigPresenter(
        IConfigView view,
        LocalizationService localization,
        Func<string> autoDetectGameDirectory,
        Action<ModManagerConfig> applyConfiguration)
    {
        this.view = view;
        this.localization = localization;
        this.autoDetectGameDirectory = autoDetectGameDirectory;
        this.applyConfiguration = applyConfiguration;

        view.ApplyConfigurationRequested += OnApplyConfigurationRequested;
        view.AutoDetectGameDirectoryRequested += OnAutoDetectGameDirectoryRequested;
    }

    /// <summary>Refresh the version banner after an update check completes.</summary>
    public void UpdateVersionInfo(string currentVersion, string? latestVersion)
    {
        view.UpdateVersionInfo(currentVersion, latestVersion);
    }

    private void OnApplyConfigurationRequested(ModManagerConfig config)
    {
        applyConfiguration(config);
    }

    private void OnAutoDetectGameDirectoryRequested()
    {
        var detected = autoDetectGameDirectory() ?? string.Empty;
        view.SetGameDirectory(detected);
    }
}
