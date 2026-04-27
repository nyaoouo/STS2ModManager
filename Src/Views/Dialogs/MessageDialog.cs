using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;
using STS2ModManager.Views.Widgets;

namespace STS2ModManager.Views.Dialogs;

/// <summary>
/// Themed replacement for <see cref="MessageBox"/>. All variants follow
/// <see cref="DialogShell"/>'s X/Esc=Cancel safety guarantees.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MessageDialog : DialogShell
{
    // Soft text wrap target for body content.
    private const int MaxBodyWidth = 640;
    private const int MinBodyWidth = 480;
    // Floor on body height so short single-line messages still get a comfortable dialog.
    private const int MinBodyHeight = 120;

    private MessageDialog(LocalizationService loc, string title, string message, bool includeCancel, string? confirmText, string? cancelText)
        : base(title, contentWidth: MeasureBody(message).Width, contentHeight: MeasureBody(message).Height)
    {
        var dark = MaterialSkinManager.Instance?.Theme == MaterialSkinManager.Themes.DARK;
        var fg = dark ? AppPalette.DarkIdleText : AppPalette.LightIdleText;
        var bg = dark ? Color.FromArgb(48, 48, 48) : Color.White;

        // Panel viewport + AutoSize Label inside so we can attach the custom thin
        // scrollbar (ThinScrollBarHost). Unlike a Label-only approach, the viewport
        // gracefully degrades to a scrollable view if the measured height ends up
        // smaller than the rendered text, so the last line is never silently clipped.
        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = bg,
            BorderStyle = BorderStyle.None,
        };

        var body = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(MaxBodyWidth, 0),
            Text = message,
            Font = SystemFonts.MessageBoxFont ?? new Font("Segoe UI", 9f),
            ForeColor = fg,
            BackColor = bg,
            Margin = new Padding(0),
            Padding = new Padding(0),
            UseCompatibleTextRendering = false,
            Location = new Point(0, 0),
        };
        viewport.Controls.Add(body);

        ContentHost.BackColor = bg;
        ContentHost.Padding = new Padding(0, 0, 0, 8);
        ContentHost.Controls.Add(viewport);

        ThinScrollBarHost.Attach(viewport, body, manageContentWidth: true, autoHideAfterMs: 0);

        // First call appears RIGHTMOST in the right-to-left button bar — that's the primary action.
        AddButton(confirmText ?? loc.Get("common.ok_button"), DialogResult.OK, isPrimary: true);
        if (includeCancel)
        {
            AddButton(cancelText ?? loc.Get("common.cancel_button"), DialogResult.Cancel);
        }
    }

    /// <summary>Show an informational message with a single OK button.</summary>
    public static void Info(IWin32Window owner, LocalizationService loc, string title, string message)
        => ShowInternal(owner, loc, title, message, includeCancel: false, confirmText: null, cancelText: null);

    /// <summary>Show a warning message with a single OK button.</summary>
    public static void Warn(IWin32Window owner, LocalizationService loc, string title, string message)
        => ShowInternal(owner, loc, title, message, includeCancel: false, confirmText: null, cancelText: null);

    /// <summary>Show an error message with a single OK button.</summary>
    public static void Error(IWin32Window owner, LocalizationService loc, string title, string message)
        => ShowInternal(owner, loc, title, message, includeCancel: false, confirmText: null, cancelText: null);

    /// <summary>
    /// Ask the user for confirmation. Returns <c>true</c> if the user clicked the
    /// primary action; <c>false</c> if they cancelled (including via X / Esc).
    /// </summary>
    public static bool Confirm(
        IWin32Window owner,
        LocalizationService loc,
        string title,
        string message,
        string? confirmText = null,
        string? cancelText = null)
    {
        return ShowInternal(owner, loc, title, message, includeCancel: true, confirmText, cancelText) == DialogResult.OK;
    }

    private static DialogResult ShowInternal(
        IWin32Window owner,
        LocalizationService loc,
        string title,
        string message,
        bool includeCancel,
        string? confirmText,
        string? cancelText)
    {
        using var dlg = new MessageDialog(loc, title, message, includeCancel, confirmText, cancelText);
        return dlg.ShowDialog(owner);
    }

    private static Size MeasureBody(string message)
    {
        // Use a real Label probe with GetPreferredSize so the measurement matches
        // exactly how the runtime Label will lay out the text (including wrapping,
        // padding, and line spacing). TextRenderer.MeasureText routinely under-
        // estimates the rendered height by a few pixels, which clips the last line.
        using var probe = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(MaxBodyWidth, 0),
            Font = SystemFonts.MessageBoxFont ?? new Font("Segoe UI", 9f),
            UseCompatibleTextRendering = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Text = message,
        };
        var preferred = probe.GetPreferredSize(new Size(MaxBodyWidth, 0));
        var width = Math.Max(MinBodyWidth, Math.Min(MaxBodyWidth, preferred.Width));
        var height = Math.Max(MinBodyHeight, preferred.Height + 8);
        return new Size(width, height);
    }
}
