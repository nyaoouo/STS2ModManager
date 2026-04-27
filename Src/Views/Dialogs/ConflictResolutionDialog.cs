using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using STS2ModManager.Views.Widgets;

namespace STS2ModManager.Views.Dialogs;

[SupportedOSPlatform("windows")]
internal sealed class ConflictResolutionDialog : DialogShell
{
    public static ConflictChoice Show(
        IWin32Window owner,
        LocalizationService loc,
        ModInfo incoming,
        IReadOnlyList<ModInfo> existing,
        string incomingSourceLabel,
        string comparisonText,
        Func<ModInfo, string> formatPath,
        Func<string?, string> formatVersion)
    {
        using var dlg = new ConflictResolutionDialog(loc, incoming, existing, incomingSourceLabel, comparisonText, formatPath, formatVersion);
        return dlg.ShowDialog(owner) switch
        {
            DialogResult.Yes => ConflictChoice.KeepIncoming,
            DialogResult.No => ConflictChoice.KeepExisting,
            _ => ConflictChoice.Cancel,
        };
    }

    private ConflictResolutionDialog(
        LocalizationService loc,
        ModInfo incoming,
        IReadOnlyList<ModInfo> existing,
        string incomingSourceLabel,
        string comparisonText,
        Func<ModInfo, string> formatPath,
        Func<string?, string> formatVersion)
        : base(loc.Get("archive.duplicate_mod_id_title"), contentWidth: 720, contentHeight: 320)
    {
        var sb = new StringBuilder();
        sb.AppendLine(loc.Get("archive.duplicate_mod_id_message", incoming.Id));
        if (!string.IsNullOrEmpty(comparisonText))
        {
            sb.AppendLine(comparisonText);
        }
        sb.AppendLine();
        sb.AppendLine(loc.Get("archive.incoming_version_label"));
        sb.AppendLine(loc.Get("archive.incoming_version_line", incoming.Name, formatVersion(incoming.Version), incoming.FolderName, incomingSourceLabel));
        sb.AppendLine();
        sb.AppendLine(loc.Get("archive.existing_versions_label"));
        foreach (var mod in existing)
        {
            sb.AppendLine(loc.Get("archive.existing_version_line", mod.Name, formatVersion(mod.Version), mod.FolderName, formatPath(mod)));
        }
        sb.AppendLine();
        sb.AppendLine(loc.Get("archive.keep_incoming_help_text"));
        sb.AppendLine(loc.Get("archive.keep_existing_help_text"));

        var skin = MaterialSkinManager.Instance;
        var dark = skin.Theme == MaterialSkinManager.Themes.DARK;
        var bg = dark ? Color.FromArgb(48, 48, 48) : Color.White;
        var fg = dark ? Color.FromArgb(230, 230, 230) : Color.FromArgb(33, 33, 33);

        // Panel viewport + AutoSize Label inside, scrolled by ThinScrollBar so the
        // dialog matches the visual style used elsewhere (mod list, MessageDialog).
        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = bg,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8, 6, 8, 6),
        };
        var body = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(0, 0),
            Text = sb.ToString(),
            BackColor = bg,
            ForeColor = fg,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0),
            Padding = new Padding(0),
            UseCompatibleTextRendering = false,
            Location = new Point(0, 0),
        };
        viewport.Controls.Add(body);
        ContentHost.Controls.Add(viewport);
        ThinScrollBarHost.Attach(viewport, body, manageContentWidth: true, autoHideAfterMs: 0);

        AddButton(loc.Get("archive.keep_incoming_button"), DialogResult.Yes, isPrimary: true);
        AddButton(loc.Get("archive.keep_existing_button"), DialogResult.No);
        AddButton(loc.Get("common.cancel_button"), DialogResult.Cancel);
    }
}
