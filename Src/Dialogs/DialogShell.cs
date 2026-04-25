using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using STS2ModManager.Widgets;

namespace STS2ModManager.Dialogs;

/// <summary>
/// Shared base for all custom dialogs in the app.
/// Provides a Material-themed form with a padded content host and a
/// right-aligned button bar of <see cref="LinkButton"/>s.
///
/// Safety: closing the window via the title-bar X button or the Escape key
/// always resolves to <see cref="DialogResult.Cancel"/> rather than the first
/// available "OK"-style button.
/// </summary>
[SupportedOSPlatform("windows")]
internal class DialogShell : MaterialForm
{
    /// <summary>Content host. Add your dialog widgets here.</summary>
    protected readonly Panel ContentHost;
    private readonly FlowLayoutPanel buttonBar;

    protected DialogShell(string title, int contentWidth, int contentHeight)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Sizable = false;
        KeyPreview = true; // Required so OnKeyDown sees Escape before child controls.

        // Apply same skin as the main form so dialogs match the theme.
        try { MaterialSkinManager.Instance.AddFormToManage(this); }
        catch { /* manager may not yet be initialized when used standalone */ }

        var outer = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 70, 20, 16),
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        ContentHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 12) };
        outer.Controls.Add(ContentHost, 0, 0);

        buttonBar = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
        };
        outer.Controls.Add(buttonBar, 0, 1);

        Controls.Add(outer);
        ClientSize = new Size(
            Math.Max(contentWidth + 40, 320),
            contentHeight + 70 + 60);
    }

    /// <summary>
    /// Add a button to the right-aligned button bar. First call appears rightmost.
    /// Set <paramref name="isPrimary"/> for the recommended action (highlighted blue).
    /// </summary>
    protected LinkButton AddButton(string text, DialogResult dialogResult, bool isPrimary = false)
    {
        var btn = new LinkButton
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Text = text,
            IsHighlight = isPrimary,
            Margin = new Padding(8, 0, 0, 0),
        };
        btn.Click += (_, _) =>
        {
            DialogResult = dialogResult;
            if (dialogResult != DialogResult.None)
            {
                Close();
            }
        };
        buttonBar.Controls.Add(btn);
        return btn;
    }

    /// <summary>Escape always cancels the dialog.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            e.Handled = true;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>
    /// User-driven close (title-bar X, Alt+F4, system menu) defaults to Cancel
    /// rather than whatever DialogResult happens to be set on the form.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && DialogResult == DialogResult.None)
        {
            DialogResult = DialogResult.Cancel;
        }
        base.OnFormClosing(e);
    }
}
