using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin.Controls;

namespace STS2ModManager.Views.Dialogs;

[SupportedOSPlatform("windows")]
internal sealed class UpdatePromptDialog : DialogShell
{
    public static UpdatePromptChoice Show(IWin32Window owner, LocalizationService loc, string currentVersion, string latestVersion)
    {
        using var dlg = new UpdatePromptDialog(loc, currentVersion, latestVersion);
        return dlg.ShowDialog(owner) switch
        {
            DialogResult.Yes => UpdatePromptChoice.UpdateNow,
            DialogResult.Ignore => UpdatePromptChoice.SkipThisVersion,
            DialogResult.No => UpdatePromptChoice.NeverCheck,
            _ => UpdatePromptChoice.RemindLater,
        };
    }

    private UpdatePromptDialog(LocalizationService loc, string currentVersion, string latestVersion)
        : base(loc.Get("update.update_available_title"), contentWidth: 540, contentHeight: 140)
    {
        var body = new MaterialLabel
        {
            Dock = DockStyle.Fill,
            Text = loc.Get("update.update_available_message", currentVersion, latestVersion),
            AutoSize = false,
        };
        ContentHost.Controls.Add(body);

        AddButton(loc.Get("update.update_now_button"), DialogResult.Yes, isPrimary: true);
        AddButton(loc.Get("ui.remind_later_button"), DialogResult.Cancel);
        AddButton(loc.Get("ui.skip_this_version_button"), DialogResult.Ignore);
        AddButton(loc.Get("ui.never_check_button"), DialogResult.No);
    }
}
