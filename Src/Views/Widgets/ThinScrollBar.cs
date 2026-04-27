using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;

namespace STS2ModManager.Views.Widgets;

/// <summary>
/// Slim, modern overlay scrollbar. Vertical-only (B1).
/// </summary>
/// <remarks>
/// Layout: a thin rounded track and a thin rounded thumb. Defaults to 8 px wide
/// when idle, 12 px when hovered. Exposes <see cref="Min"/>/<see cref="Max"/>/
/// <see cref="Value"/>/<see cref="LargeChange"/>/<see cref="SmallChange"/> in
/// the same shape as <see cref="System.Windows.Forms.ScrollBar"/> so it can
/// drive any scrollable surface. Raises <see cref="Scroll"/> when
/// <see cref="Value"/> changes through user input.
///
/// The control supports thumb drag, click-page (jumps a <see cref="LargeChange"/>
/// in the click direction), and forwards <see cref="Control.MouseWheel"/>.
///
/// Auto-hide and hover-expand animation are plumbed but conservative; refine
/// in B4.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ThinScrollBar : Control
{
    private int min;
    private int max = 100;
    private int value;
    private int largeChange = 10;
    private int smallChange = 1;

    private int idleWidth = 8;
    private int hoverWidth = 12;
    private int trackPadding = 2;

    private bool isDark;
    private bool isHovered;
    private bool isDragging;
    private int dragStartMouseY;
    private int dragStartValue;

    public event ScrollEventHandler? Scroll;

    public ThinScrollBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Width = idleWidth;
        TabStop = false;
        // Default: pick up dark mode from MaterialSkinManager. Hosts can call
        // SetDarkMode explicitly to override or wire to a richer ThemeController.
        isDark = MaterialSkinManager.Instance.Theme == MaterialSkinManager.Themes.DARK;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Min
    {
        get => min;
        set
        {
            if (min == value) return;
            min = value;
            if (max < min) max = min;
            ClampValue();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Max
    {
        get => max;
        set
        {
            if (max == value) return;
            max = value;
            if (max < min) max = min;
            ClampValue();
            Invalidate();
        }
    }

    /// <summary>
    /// Scrollable range value. Setting through code does NOT raise
    /// <see cref="Scroll"/>; the event fires only for user input.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set
        {
            int v = ClampToRange(value);
            if (this.value == v) return;
            this.value = v;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeChange
    {
        get => largeChange;
        set
        {
            if (value < 1) value = 1;
            if (largeChange == value) return;
            largeChange = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SmallChange
    {
        get => smallChange;
        set
        {
            if (value < 1) value = 1;
            smallChange = value;
        }
    }

    /// <summary>
    /// Width when idle (not hovered). Defaults to 8 px.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int IdleWidth
    {
        get => idleWidth;
        set
        {
            idleWidth = Math.Max(2, value);
            if (!isHovered) Width = idleWidth;
            Invalidate();
        }
    }

    /// <summary>
    /// Width when the pointer is over the bar. Defaults to 12 px.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int HoverWidth
    {
        get => hoverWidth;
        set
        {
            hoverWidth = Math.Max(idleWidth, value);
            if (isHovered) Width = hoverWidth;
            Invalidate();
        }
    }

    /// <summary>
    /// Switch the visible color palette. Call when the host theme changes.
    /// </summary>
    public void SetDarkMode(bool dark)
    {
        if (isDark == dark) return;
        isDark = dark;
        Invalidate();
    }

    /// <summary>
    /// Convenience: configure the bar from the host scrollable in one call.
    /// </summary>
    public void Configure(int contentSize, int viewportSize, int currentOffset)
    {
        if (contentSize < 0) contentSize = 0;
        if (viewportSize < 0) viewportSize = 0;
        Min = 0;
        // Match WinForms VScrollBar semantics: Maximum is content size, LargeChange is viewport.
        // The reachable max value is Max - LargeChange + 1.
        Max = Math.Max(viewportSize, contentSize);
        LargeChange = Math.Max(1, viewportSize);
        SmallChange = Math.Max(1, viewportSize / 10);
        Value = currentOffset;
    }

    private int ClampToRange(int v)
    {
        int reachable = Math.Max(min, max - largeChange + 1);
        if (v < min) return min;
        if (v > reachable) return reachable;
        return v;
    }

    private void ClampValue()
    {
        int v = ClampToRange(value);
        if (v != value)
        {
            value = v;
            Invalidate();
        }
    }

    private bool IsThumbVisible
    {
        get
        {
            int range = max - min;
            return range > largeChange && Height > 0;
        }
    }

    private Rectangle GetTrackRect()
        => new(0, trackPadding, Width, Math.Max(0, Height - trackPadding * 2));

    private Rectangle GetThumbRect()
    {
        var track = GetTrackRect();
        int range = Math.Max(1, max - min);
        int viewport = Math.Min(largeChange, range);
        int thumbHeight = Math.Max(20, (int)((long)track.Height * viewport / range));
        int reachable = Math.Max(1, range - viewport + 1);
        long offset = (long)(value - min) * (track.Height - thumbHeight) / Math.Max(1, reachable - 1);
        int thumbY = track.Y + (int)Math.Max(0, Math.Min(track.Height - thumbHeight, offset));
        return new Rectangle(0, thumbY, Width, thumbHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Pick up theme changes automatically (host can still call SetDarkMode
        // for finer control or richer schemes).
        isDark = MaterialSkinManager.Instance.Theme == MaterialSkinManager.Themes.DARK;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Track
        var trackColor = isDark
            ? Color.FromArgb(36, 255, 255, 255)
            : Color.FromArgb(28, 0, 0, 0);
        using (var brush = new SolidBrush(trackColor))
        using (var path = RoundedRect(GetTrackRect(), Width / 2f))
        {
            g.FillPath(brush, path);
        }

        if (!IsThumbVisible) return;

        // Thumb
        Color thumbColor;
        if (isDragging)
            thumbColor = isDark ? Color.FromArgb(220, 230, 230, 230) : Color.FromArgb(200, 60, 60, 60);
        else if (isHovered)
            thumbColor = isDark ? Color.FromArgb(180, 220, 220, 220) : Color.FromArgb(160, 80, 80, 80);
        else
            thumbColor = isDark ? Color.FromArgb(120, 200, 200, 200) : Color.FromArgb(110, 110, 110, 110);

        using (var brush = new SolidBrush(thumbColor))
        using (var path = RoundedRect(GetThumbRect(), Width / 2f))
        {
            g.FillPath(brush, path);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle rect, float radius)
    {
        var path = new GraphicsPath();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        isHovered = true;
        if (Width != hoverWidth) GrowFromRight(hoverWidth);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (isDragging) return;
        isHovered = false;
        if (Width != idleWidth) GrowFromRight(idleWidth);
        Invalidate();
    }

    private void GrowFromRight(int newWidth)
    {
        int right = Right;
        Width = newWidth;
        Left = right - newWidth;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !IsThumbVisible) return;

        var thumb = GetThumbRect();
        if (thumb.Contains(e.Location))
        {
            isDragging = true;
            dragStartMouseY = e.Y;
            dragStartValue = value;
            Capture = true;
        }
        else
        {
            // Page jump in click direction
            int delta = e.Y < thumb.Y ? -largeChange : largeChange;
            ApplyUserValue(value + delta, e.Y < thumb.Y ? ScrollEventType.LargeDecrement : ScrollEventType.LargeIncrement);
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!isDragging) return;

        var track = GetTrackRect();
        var thumb = GetThumbRect();
        int reachable = Math.Max(1, max - min - largeChange + 1);
        int travel = Math.Max(1, track.Height - thumb.Height);
        long mouseDelta = e.Y - dragStartMouseY;
        long valueDelta = mouseDelta * reachable / travel;
        ApplyUserValue(dragStartValue + (int)valueDelta, ScrollEventType.ThumbTrack);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!isDragging) return;
        isDragging = false;
        Capture = false;
        OnScrollEvent(ScrollEventType.ThumbPosition, value);
        if (!ClientRectangle.Contains(PointToClient(MousePosition)))
        {
            isHovered = false;
            if (Width != idleWidth) GrowFromRight(idleWidth);
        }
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsThumbVisible) return;
        int step = SystemInformation.MouseWheelScrollLines * smallChange;
        if (step <= 0) step = smallChange;
        int delta = e.Delta > 0 ? -step : step;
        ApplyUserValue(value + delta, e.Delta > 0 ? ScrollEventType.SmallDecrement : ScrollEventType.SmallIncrement);
    }

    private void ApplyUserValue(int newValue, ScrollEventType type)
    {
        int clamped = ClampToRange(newValue);
        if (clamped == value) return;
        int oldValue = value;
        value = clamped;
        Invalidate();
        OnScrollEvent(type, oldValue);
    }

    private void OnScrollEvent(ScrollEventType type, int oldValue)
    {
        Scroll?.Invoke(this, new ScrollEventArgs(type, oldValue, value, ScrollOrientation.VerticalScroll));
    }

    protected override bool IsInputKey(Keys keyData) => false;
}
