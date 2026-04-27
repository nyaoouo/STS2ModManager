using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;

namespace STS2ModManager.Views.Widgets;

/// <summary>
/// One row inside <see cref="SaveProfileList"/>. Displays the profile's slot,
/// state, last-modified time and a colored type chip; supports selection and
/// initiates drag-drop on left-button drag.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SaveProfileRow : Control
{
    private const int RowHeight = 44;
    private const int InnerPad = 10;
    private const int ChipWidth = 64;
    private const int ChipHeight = 22;

    public event Action<SaveProfileRow>? Activated;

    public SaveProfileInfo Profile { get; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; private set; }

    private bool hovered;
    private Point dragStartPoint;
    private bool mouseDownLeft;

    public SaveProfileRow(SaveProfileInfo profile)
    {
        Profile = profile;
        Dock = DockStyle.Top;
        Height = RowHeight;
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
    }

    public void SetSelected(bool value)
    {
        if (Selected == value) return;
        Selected = value;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            mouseDownLeft = true;
            dragStartPoint = e.Location;
            Activated?.Invoke(this);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) mouseDownLeft = false;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!mouseDownLeft || !Profile.HasData) return;
        var dx = Math.Abs(e.X - dragStartPoint.X);
        var dy = Math.Abs(e.Y - dragStartPoint.Y);
        if (dx < SystemInformation.DragSize.Width && dy < SystemInformation.DragSize.Height) return;
        mouseDownLeft = false;
        DoDragDrop(Profile, DragDropEffects.Copy);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var skin = MaterialSkinManager.Instance;
        var dark = skin.Theme == MaterialSkinManager.Themes.DARK;

        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Background: selected > hovered > base
        Color baseColor = dark ? Color.FromArgb(48, 48, 48) : Color.White;
        Color hoverColor = dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(245, 245, 245);
        Color selectedColor = skin.ColorScheme.AccentColor;
        Color textColor = Profile.HasData ? skin.TextHighEmphasisColor : skin.TextDisabledOrHintColor;

        Color bg = Selected
            ? Color.FromArgb(48, selectedColor.R, selectedColor.G, selectedColor.B)
            : (hovered ? hoverColor : baseColor);
        using (var brush = new SolidBrush(bg)) g.FillRectangle(brush, ClientRectangle);

        // Bottom divider
        using (var pen = new Pen(dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(230, 230, 230), 1f))
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        // Type chip (left)
        var chipColor = Profile.Kind == SaveKind.Vanilla
            ? (dark ? Color.FromArgb(120, 144, 156) : Color.FromArgb(96, 125, 139))
            : (dark ? Color.FromArgb(0, 172, 193) : Color.FromArgb(0, 151, 167));
        var chipRect = new Rectangle(InnerPad, (Height - ChipHeight) / 2, ChipWidth, ChipHeight);
        using (var brush = new SolidBrush(chipColor))
        using (var path = RoundedRect(chipRect, 11))
        {
            g.FillPath(brush, path);
        }
        var chipText = Profile.Kind == SaveKind.Vanilla ? "Vanilla" : "Modded";
        using (var chipFont = new Font(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var fg = new SolidBrush(Color.White))
        {
            g.DrawString(chipText, chipFont, fg, chipRect, sf);
        }

        // Slot label (center-left)
        var x = InnerPad + ChipWidth + InnerPad;
        using (var titleFont = new Font(FontFamily.GenericSansSerif, 10f, FontStyle.Bold))
        using (var detailFont = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Regular))
        using (var fg = new SolidBrush(textColor))
        using (var fgDim = new SolidBrush(skin.TextMediumEmphasisColor))
        {
            var slotText = $"Slot {Profile.ProfileId}";
            var slotSize = g.MeasureString(slotText, titleFont);
            g.DrawString(slotText, titleFont, fg, x, (Height - slotSize.Height) / 2 - 1);

            x += (int)slotSize.Width + InnerPad * 2;
            var stateText = Profile.HasData ? "Present" : "Empty";
            g.DrawString(stateText, detailFont, fgDim, x, (Height - detailFont.Height) / 2 - 1);

            // Right-aligned: last modified
            if (Profile.LastModified.HasValue)
            {
                var dateText = Profile.LastModified.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                var dateSize = g.MeasureString(dateText, detailFont);
                g.DrawString(dateText, detailFont, fgDim,
                    Width - InnerPad - dateSize.Width,
                    (Height - dateSize.Height) / 2 - 1);
            }
        }

        // Selected accent left bar
        if (Selected)
        {
            using var accentBrush = new SolidBrush(selectedColor);
            g.FillRectangle(accentBrush, 0, 0, 3, Height);
        }
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
