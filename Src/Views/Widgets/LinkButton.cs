using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;

namespace STS2ModManager.Views.Widgets;

/// <summary>
/// Link-style button: transparent background, theme-colored underlined text,
/// 1 px theme-colored border on hover, hand cursor, deeper-contrast text on press.
/// When <see cref="IsHighlight"/> is true the text uses the blue accent.
/// Theme is read from <see cref="MaterialSkinManager.Instance.Theme"/> on every
/// paint, so the control automatically follows theme switches.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LinkButton : Control
{
    private static readonly ToolTip SharedTooltip = new() { AutoPopDelay = 8000, InitialDelay = 350, ReshowDelay = 100, ShowAlways = true };

    private bool isHighlight;
    private bool isHover;
    private bool isPressed;
    private bool lookDisabled;
    private string? tooltipText;
    private Font? underlineFont;

    public LinkButton()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.AllPaintingInWmPaint,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Padding = new Padding(8, 4, 8, 4);
        Font = SystemFonts.MessageBoxFont ?? new Font("Segoe UI", 9f);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsHighlight
    {
        get => isHighlight;
        set
        {
            if (isHighlight == value) return;
            isHighlight = value;
            Invalidate();
        }
    }

    /// <summary>
    /// Visually disabled but still receives mouse events so <see cref="Tooltip"/> can
    /// display (typically used to explain why an action is unavailable). Click events
    /// are suppressed while this is true. Prefer this over <see cref="Control.Enabled"/>
    /// when you want the user to be able to hover and learn why.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool LookDisabled
    {
        get => lookDisabled;
        set
        {
            if (lookDisabled == value) return;
            lookDisabled = value;
            Cursor = value ? Cursors.Default : Cursors.Hand;
            if (value) { isHover = false; isPressed = false; }
            Invalidate();
        }
    }

    /// <summary>
    /// Hover tooltip text. Set to null/empty to clear. Works on both normal and
    /// <see cref="LookDisabled"/> states (does NOT work when <see cref="Control.Enabled"/>
    /// is false because Windows suppresses mouse events on disabled controls).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Tooltip
    {
        get => tooltipText;
        set
        {
            if (tooltipText == value) return;
            tooltipText = value;
            SharedTooltip.SetToolTip(this, value ?? string.Empty);
        }
    }

    protected override Size DefaultSize => new(80, 26);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = string.IsNullOrEmpty(Text) ? " " : Text;
        var size = TextRenderer.MeasureText(text, Font);
        return new Size(size.Width + Padding.Horizontal, size.Height + Padding.Vertical);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        underlineFont?.Dispose();
        underlineFont = null;
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        if (!lookDisabled)
        {
            isHover = true;
            Invalidate();
        }
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        isHover = false;
        isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!lookDisabled && e.Button == MouseButtons.Left)
        {
            isPressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (isPressed)
        {
            isPressed = false;
            Invalidate();
        }
        base.OnMouseUp(e);
    }

    protected override void OnClick(EventArgs e)
    {
        if (lookDisabled) return; // Suppress click while soft-disabled.
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var dark = MaterialSkinManager.Instance?.Theme == MaterialSkinManager.Themes.DARK;
        var idleText  = dark ? AppPalette.DarkIdleText  : AppPalette.LightIdleText;
        var pressText = dark ? AppPalette.DarkPressText : AppPalette.LightPressText;
        var highlight = dark ? AppPalette.DarkHighlight : AppPalette.LightHighlight;
        var border    = dark ? AppPalette.DarkBorder    : AppPalette.LightBorder;
        var disabled  = dark ? AppPalette.DarkDisabledText : AppPalette.LightDisabledText;

        Color fg;
        if (!Enabled || lookDisabled)
        {
            fg = disabled;
        }
        else if (isHighlight)
        {
            fg = highlight;
        }
        else
        {
            fg = isPressed ? pressText : idleText;
        }

        underlineFont ??= new Font(Font, FontStyle.Underline);

        var flags = TextFormatFlags.HorizontalCenter
                  | TextFormatFlags.VerticalCenter
                  | TextFormatFlags.SingleLine
                  | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(e.Graphics, Text ?? string.Empty, underlineFont, ClientRectangle, fg, flags);

        if (isHover && Enabled && !lookDisabled)
        {
            using var pen = new Pen(border, 1f);
            var box = ClientRectangle;
            box.Width  -= 1;
            box.Height -= 1;
            e.Graphics.DrawRectangle(pen, box);
        }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        if (!Enabled)
        {
            isHover = false;
            isPressed = false;
        }
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            underlineFont?.Dispose();
            underlineFont = null;
        }
        base.Dispose(disposing);
    }
}
