using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace STS2ModManager.Widgets;

/// <summary>
/// Replaces a <see cref="Panel"/>'s native scrollbar with a <see cref="ThinScrollBar"/>
/// overlay and drives a single inner control's <c>Top</c> from the bar's value.
/// </summary>
/// <remarks>
/// The viewport panel must contain exactly one logical scroll content control
/// (passed as <paramref name="content"/>). The host:
/// 1. Disables the panel's native AutoScroll.
/// 2. Adds a <see cref="ThinScrollBar"/> docked on the right edge.
/// 3. Resizes the content's width to match the viewport.
/// 4. Updates the bar Min/Max/LargeChange whenever the viewport or content
///    resizes, and translates the content vertically based on bar value.
/// 5. Forwards mouse wheel from the viewport, the content, and any descendant
///    that bubbles wheel events.
///
/// The scrollbar is overlaid (no extra reserved column) so the content keeps
/// its full width.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ThinScrollBarHost
{
    /// <summary>
    /// Attach a <see cref="ThinScrollBar"/> overlay to <paramref name="viewport"/>
    /// scrolling its single child <paramref name="content"/>.
    /// </summary>
    /// <param name="manageContentWidth">
    /// When true (default), the host stretches the content to the viewport width.
    /// Pass false when the caller manages the content's width themselves
    /// (e.g. a FlowLayoutPanel sized via MaximumSize).
    /// </param>
    /// <param name="autoHideAfterMs">
    /// When &gt; 0, the bar fades back to hidden after this many milliseconds
    /// of inactivity (no wheel, no mouse over the viewport, no thumb drag).
    /// Pass 0 to keep the bar visible whenever it's needed.
    /// </param>
    public static ThinScrollBar Attach(
        ScrollableControl viewport,
        Control content,
        bool manageContentWidth = true,
        int autoHideAfterMs = 1500)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(content);

        viewport.AutoScroll = false;

        var bar = new ThinScrollBar
        {
            // Docked manually so it can overlay the right edge.
            Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom,
            Visible = false,
        };
        viewport.Controls.Add(bar);
        bar.BringToFront();

        // The content control is owned by viewport (or some scrolling parent).
        // Force position so we control it.
        content.Location = new Point(0, 0);
        if (manageContentWidth) content.Width = viewport.ClientSize.Width;

        // Auto-hide state: needsBar tracks whether the content overflows the
        // viewport (and the bar would be shown), separately from the actual
        // Visible flag which the timer toggles.
        bool needsBar = false;
        System.Windows.Forms.Timer? hideTimer = null;
        if (autoHideAfterMs > 0)
        {
            hideTimer = new System.Windows.Forms.Timer { Interval = autoHideAfterMs };
            hideTimer.Tick += (_, _) =>
            {
                hideTimer.Stop();
                if (!needsBar) return;
                // Don't hide while the user is interacting with the bar.
                if (bar.Capture) return;
                if (bar.ClientRectangle.Contains(bar.PointToClient(Control.MousePosition))) return;
                bar.Visible = false;
            };
            viewport.Disposed += (_, _) => hideTimer.Dispose();
        }

        void Wake()
        {
            if (!needsBar) return;
            if (!bar.Visible) bar.Visible = true;
            if (hideTimer != null)
            {
                hideTimer.Stop();
                hideTimer.Start();
            }
        }

        void UpdateLayout()
        {
            int viewportH = viewport.ClientSize.Height;
            int contentH = content.PreferredSize.Height;
            if (contentH <= 0) contentH = content.Height;

            // Bar geometry: full height, hugging the right edge.
            bar.Width = bar.IdleWidth;
            bar.Height = viewportH;
            bar.Left = viewport.ClientSize.Width - bar.Width;
            bar.Top = 0;

            if (manageContentWidth && content.Width != viewport.ClientSize.Width)
                content.Width = viewport.ClientSize.Width;

            bool needBar = contentH > viewportH;
            needsBar = needBar;
            if (!needBar)
            {
                bar.Visible = false;
            }
            else if (hideTimer == null)
            {
                bar.Visible = true;
            }
            // If autoHide is on and the bar is currently hidden, leave it hidden;
            // Wake() will surface it on the next user activity.

            if (needBar)
            {
                bar.Configure(contentSize: contentH, viewportSize: viewportH, currentOffset: -content.Top);
                if (-content.Top > bar.Max - bar.LargeChange + 1)
                {
                    int newTop = -(bar.Max - bar.LargeChange + 1);
                    if (newTop > 0) newTop = 0;
                    content.Top = newTop;
                    bar.Value = -content.Top;
                }
            }
            else
            {
                content.Top = 0;
            }
        }

        bar.Scroll += (_, e) =>
        {
            content.Top = -e.NewValue;
            Wake();
        };

        viewport.Resize += (_, _) => UpdateLayout();
        content.SizeChanged += (_, _) => UpdateLayout();
        viewport.MouseMove += (_, _) => Wake();
        bar.MouseMove += (_, _) => Wake();
        bar.MouseEnter += (_, _) => Wake();

        // Mouse-wheel forwarding: viewport, content, and any descendants.
        void OnWheel(object? sender, MouseEventArgs e)
        {
            if (!needsBar) return;
            Wake();
            // Forward by sending a synthesized wheel-style update through the bar API.
            int notch = e.Delta / SystemInformation.MouseWheelScrollDelta;
            if (notch == 0) return;
            int linesPerNotch = SystemInformation.MouseWheelScrollLines;
            if (linesPerNotch <= 0) linesPerNotch = 3;
            // Approximate one line as 24 px (matches MaterialCard line height roughly).
            const int pixelsPerLine = 24;
            int delta = -notch * linesPerNotch * pixelsPerLine;
            int oldValue = bar.Value;
            bar.Value = bar.Value + delta;
            if (bar.Value != oldValue)
            {
                content.Top = -bar.Value;
            }
        }

        viewport.MouseWheel += OnWheel;
        HookWheelRecursive(content, OnWheel);
        content.ControlAdded += (_, args) => { if (args.Control is { } c) HookWheelRecursive(c, OnWheel); };

        UpdateLayout();
        return bar;
    }

    private static void HookWheelRecursive(Control root, MouseEventHandler handler)
    {
        root.MouseWheel += handler;
        foreach (Control c in root.Controls)
        {
            HookWheelRecursive(c, handler);
        }
        root.ControlAdded += (_, args) => { if (args.Control is { } c) HookWheelRecursive(c, handler); };
    }
}
