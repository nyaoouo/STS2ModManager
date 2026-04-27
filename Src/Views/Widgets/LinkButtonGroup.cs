using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;

namespace STS2ModManager.Views.Widgets;

/// <summary>
/// Visual grouping container for related <see cref="LinkButton"/>s
/// (e.g. a set of mutually-exclusive filter chips). Lays children out
/// horizontally and paints a 1 px rounded theme-colored border so users
/// understand the items belong together. Theme is re-read on every paint.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LinkButtonGroup : FlowLayoutPanel
{
    public LinkButtonGroup()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor
            | ControlStyles.AllPaintingInWmPaint,
            true);
        BackColor = Color.Transparent;
        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(4, 2, 4, 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var dark = MaterialSkinManager.Instance?.Theme == MaterialSkinManager.Themes.DARK;
        var border = dark ? AppPalette.DarkBorder : AppPalette.LightBorder;

        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var prevMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = RoundedRect(rect, 6))
        using (var pen = new Pen(border, 1f))
        {
            e.Graphics.DrawPath(pen, path);
        }
        e.Graphics.SmoothingMode = prevMode;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        var path = new GraphicsPath();

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
