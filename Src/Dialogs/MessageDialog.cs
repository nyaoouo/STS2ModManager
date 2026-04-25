using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;
using STS2ModManager.App;

namespace STS2ModManager.Dialogs;

/// <summary>
/// Themed replacement for <see cref="MessageBox"/>. All variants follow
/// <see cref="DialogShell"/>'s X/Esc=Cancel safety guarantees.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MessageDialog : DialogShell
{
    // Soft text wrap target for body content.
    private const int MaxBodyWidth = 560;
    private const int MinBodyWidth = 300;

    private MessageDialog(LocalizationService loc, string title, string message, bool includeCancel, string? confirmText, string? cancelText)
        : base(title, contentWidth: MeasureBody(message).Width, contentHeight: MeasureBody(message).Height)
    {
        var dark = MaterialSkinManager.Instance?.Theme == MaterialSkinManager.Themes.DARK;
        var fg = dark ? AppPalette.DarkIdleText : AppPalette.LightIdleText;

        var body = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(MaxBodyWidth, 0),
            Text = message,
            Font = SystemFonts.MessageBoxFont ?? new Font("Segoe UI", 9f),
            ForeColor = fg,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
            UseCompatibleTextRendering = false,
        };
        ContentHost.Padding = new Padding(0, 0, 0, 8);
        ContentHost.Controls.Add(body);

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
        var font = SystemFonts.MessageBoxFont ?? new Font("Segoe UI", 9f);
        var maxArea = new Size(MaxBodyWidth, int.MaxValue);
        var measured = TextRenderer.MeasureText(
            message,
            font,
            maxArea,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.LeftAndRightPadding);
        var width = Math.Max(MinBodyWidth, Math.Min(MaxBodyWidth, measured.Width));
        var height = Math.Max(40, measured.Height);
        return new Size(width, height);
    }
}
