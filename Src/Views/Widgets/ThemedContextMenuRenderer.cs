// =============================================================================
// ThemedContextMenuRenderer.cs  -  ContextMenuStrip skin matching MaterialSkin
// =============================================================================
//
// WinForms' default ToolStripProfessionalRenderer paints menus with a
// fixed grey margin column and black text -- ugly in dark theme. This
// renderer reads the current MaterialSkinManager theme and paints with
// AppPalette colours so context menus blend into the rest of the UI.
//
// Apply with `myStrip.Renderer = new ThemedContextMenuRenderer();`.
// =============================================================================

using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;
using STS2ModManager.Services.UI;

namespace STS2ModManager.Views.Widgets;

[SupportedOSPlatform("windows")]
internal sealed class ThemedContextMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedContextMenuRenderer() : base(new ThemedColorTable()) { }

    private static bool IsDark => MaterialSkinManager.Instance?.Theme == MaterialSkinManager.Themes.DARK;

    private static Color Background => IsDark ? Color.FromArgb(48, 48, 48) : Color.White;
    private static Color Text => IsDark ? AppPalette.DarkIdleText : AppPalette.LightIdleText;
    private static Color DisabledText => IsDark ? AppPalette.DarkDisabledText : AppPalette.LightDisabledText;
    private static Color Hover => IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(232, 240, 252);
    private static Color Border => IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(210, 210, 210);
    private static Color Separator => IsDark ? Color.FromArgb(80, 80, 80) : Color.FromArgb(220, 220, 220);
    private static Color Accent => MaterialSkinManager.Instance?.ColorScheme?.AccentColor ?? Color.DodgerBlue;

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : DisabledText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        if (e.Item.Selected && e.Item.Enabled)
        {
            using var brush = new SolidBrush(Hover);
            e.Graphics.FillRectangle(brush, bounds);
        }
        else
        {
            using var brush = new SolidBrush(Background);
            e.Graphics.FillRectangle(brush, bounds);
        }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        var r = e.AffectedBounds;
        r.Width -= 1; r.Height -= 1;
        e.Graphics.DrawRectangle(pen, r);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        using var pen = new Pen(Separator);
        var y = bounds.Height / 2;
        e.Graphics.DrawLine(pen, bounds.Left + 4, y, bounds.Right - 4, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        using var pen = new Pen(Accent, 2f);
        var r = e.ImageRectangle;
        // Simple checkmark
        e.Graphics.DrawLines(pen, new[]
        {
            new Point(r.Left + 2, r.Top + r.Height / 2),
            new Point(r.Left + r.Width / 3, r.Bottom - 3),
            new Point(r.Right - 2, r.Top + 2),
        });
    }

    private sealed class ThemedColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(232, 240, 252);
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
    }
}
