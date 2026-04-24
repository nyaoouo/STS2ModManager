using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace STS2ModManager.Dialogs;

/// <summary>
/// Shared base for all custom dialogs in the app.
/// Provides a Material-themed form with a padded content host
/// and a right-aligned button bar.
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

    /// <summary>Add a button to the right-aligned button bar. First call appears rightmost.</summary>
    protected MaterialButton AddButton(
        string text,
        DialogResult dialogResult,
        MaterialButton.MaterialButtonType type = MaterialButton.MaterialButtonType.Outlined,
        bool useAccent = false)
    {
        var btn = new MaterialButton
        {
            AutoSize = false,
            Size = new Size(Math.Max(110, TextRenderer.MeasureText(text, Font).Width + 32), 36),
            Text = text,
            Type = type,
            UseAccentColor = useAccent,
            DialogResult = dialogResult,
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
}
