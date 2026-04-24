using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using STS2ModManager.Mods;

namespace STS2ModManager.Dialogs;

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
        var body = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = sb.ToString(),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = dark ? Color.FromArgb(48, 48, 48) : Color.White,
            ForeColor = dark ? Color.FromArgb(230, 230, 230) : Color.FromArgb(33, 33, 33),
            Font = new Font("Segoe UI", 9f),
        };
        ContentHost.Controls.Add(body);

        var keepIncoming = AddButton(loc.Get("archive.keep_incoming_button"), DialogResult.Yes, MaterialButton.MaterialButtonType.Contained, useAccent: true);
        AddButton(loc.Get("archive.keep_existing_button"), DialogResult.No);
        AddButton(loc.Get("common.cancel_button"), DialogResult.Cancel);
        AcceptButton = keepIncoming;
    }
}
