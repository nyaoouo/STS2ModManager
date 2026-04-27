using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using STS2ModManager.Models;
using STS2ModManager.Views.Widgets;

namespace STS2ModManager.Views.Widgets;

/// <summary>
/// Scrollable list of <see cref="SaveProfileRow"/>s with drag-drop accept.
/// Used by <see cref="SaveSidePanel"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SaveProfileList : Panel
{
    private readonly Action<SaveProfileRow> onRowActivated;
    private readonly Action<SaveProfileInfo, SaveProfileRow?> onDrop;
    private readonly List<SaveProfileRow> rows = new();
    private readonly Panel rowsContainer;

    public SaveProfileList(
        Action<SaveProfileRow> onRowActivated,
        Action<SaveProfileInfo, SaveProfileRow?> onDrop)
    {
        this.onRowActivated = onRowActivated;
        this.onDrop = onDrop;

        Dock = DockStyle.Fill;
        BorderStyle = BorderStyle.FixedSingle;
        AllowDrop = true;
        DoubleBuffered = true;

        // Single content panel that the overlay scrollbar drives. Rows
        // dock-stack into it; we translate its Top to scroll.
        rowsContainer = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Location = new Point(0, 0),
            AllowDrop = true,
        };
        // Forward drag events from the inner container to this panel so the
        // existing OnDragEnter/OnDragOver/OnDragDrop logic still runs.
        rowsContainer.DragEnter += (_, e) => OnDragEnter(e);
        rowsContainer.DragOver += (_, e) => OnDragOver(e);
        rowsContainer.DragDrop += (_, e) => OnDragDrop(e);
        Controls.Add(rowsContainer);
        ThinScrollBarHost.Attach(this, rowsContainer, manageContentWidth: true);
    }

    public void SetProfiles(IReadOnlyList<SaveProfileInfo> profiles)
    {
        rowsContainer.SuspendLayout();
        foreach (var existing in rows) rowsContainer.Controls.Remove(existing);
        rows.Clear();

        // Add rows in reverse order because Dock=Top stacks last-added on top.
        for (var i = profiles.Count - 1; i >= 0; i--)
        {
            var row = new SaveProfileRow(profiles[i]);
            row.Activated += r => onRowActivated(r);
            rowsContainer.Controls.Add(row);
            rows.Add(row);
        }

        rowsContainer.ResumeLayout();
    }

    public void ClearSelection()
    {
        foreach (var r in rows) r.SetSelected(false);
    }

    protected override void OnDragEnter(DragEventArgs drgevent)
    {
        base.OnDragEnter(drgevent);
        drgevent.Effect = drgevent.Data!.GetDataPresent(typeof(SaveProfileInfo))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        drgevent.Effect = drgevent.Data!.GetDataPresent(typeof(SaveProfileInfo))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);
        if (drgevent.Data?.GetData(typeof(SaveProfileInfo)) is not SaveProfileInfo source) return;
        // Find which row the cursor is over (if any). Row bounds are
        // expressed in rowsContainer coordinates.
        var clientPoint = rowsContainer.PointToClient(new Point(drgevent.X, drgevent.Y));
        SaveProfileRow? hovered = null;
        foreach (var r in rows)
        {
            if (r.Bounds.Contains(clientPoint)) { hovered = r; break; }
        }
        onDrop(source, hovered);
    }
}
