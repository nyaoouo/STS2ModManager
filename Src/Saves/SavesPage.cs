using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using STS2ModManager;
using STS2ModManager.Dialogs;
using STS2ModManager.Widgets;

namespace STS2ModManager.Saves;

/// <summary>
/// New saves UI: dual-side picker (Steam users + local default players) with
/// custom Material list rows and drag-drop copy between sides.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SavesPage : UserControl
{
    private int backupRetention = 5;

    /// <summary>Update how many timestamped backup folders to keep per profile (0 = none).</summary>
    public void SetBackupRetention(int count) => backupRetention = Math.Clamp(count, 0, 100);

    private readonly LocalizationService loc;
    private readonly Action<string> reportStatus;

    private readonly SidePanel leftSide;
    private readonly SidePanel rightSide;
    private readonly Label introLabel;

    public SavesPage(LocalizationService loc, Action<string> reportStatus)
    {
        this.loc = loc;
        this.reportStatus = reportStatus;

        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        UpdateStyles();

        introLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = loc.Get("saves.save_manager_intro"),
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 8),
            Padding = new System.Windows.Forms.Padding(12, 8, 12, 0),
        };

        leftSide = new SidePanel(loc, isLeft: true, OnRowSelected, OnDropOnSide, OnCopyClicked);
        rightSide = new SidePanel(loc, isLeft: false, OnRowSelected, OnDropOnSide, OnCopyClicked);

        var sidesLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            RowCount = 1,
            Padding = new System.Windows.Forms.Padding(12, 4, 12, 12),
        };
        sidesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        sidesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        sidesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        sidesLayout.Controls.Add(leftSide, 0, 0);
        sidesLayout.Controls.Add(rightSide, 1, 0);

        var mainLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2,
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainLayout.Controls.Add(introLabel, 0, 0);
        mainLayout.Controls.Add(sidesLayout, 0, 1);

        Controls.Add(mainLayout);

        ReloadLocations(loc.Get("status.ready_save_manager_status"));
    }

    public void RefreshData(string statusText) => ReloadLocations(statusText);

    private void ReloadLocations(string statusText)
    {
        var locations = SaveProfileService.EnumerateLocations();
        leftSide.SetLocations(locations);
        rightSide.SetLocations(locations);

        if (locations.Count == 0)
        {
            reportStatus(loc.Get("saves.save_users_not_found_status"));
            return;
        }

        // Default: left=first, right=second (or same if only one).
        leftSide.SelectLocationIndex(0);
        rightSide.SelectLocationIndex(locations.Count > 1 ? 1 : 0);
        UpdateButtons();
        reportStatus(statusText);
    }

    private void OnRowSelected(SidePanel _, SaveProfileRow __) => UpdateButtons();

    private void UpdateButtons()
    {
        var leftSel = leftSide.GetSelected();
        var rightSel = rightSide.GetSelected();
        // A side can copy if it has a non-empty source AND the other side has any target slot selected.
        leftSide.SetCopyEnabled(leftSel is not null && leftSel.HasData && rightSel is not null);
        rightSide.SetCopyEnabled(rightSel is not null && rightSel.HasData && leftSel is not null);
    }

    private void OnCopyClicked(SidePanel from)
    {
        var to = (from == leftSide) ? rightSide : leftSide;
        var src = from.GetSelected();
        var dst = to.GetSelected();
        TryCopy(src, dst);
    }

    private void OnDropOnSide(SidePanel target, SaveProfileInfo source, SaveProfileRow? hoverTargetRow)
    {
        // If a specific row was hovered, use it; else use the side's current selection.
        var dst = hoverTargetRow?.Profile ?? target.GetSelected();
        TryCopy(source, dst);
    }

    private void TryCopy(SaveProfileInfo? source, SaveProfileInfo? target)
    {
        if (source is null || target is null)
        {
            reportStatus(loc.Get("saves.save_selection_required_status"));
            return;
        }
        if (!source.HasData)
        {
            reportStatus(loc.Get("saves.save_source_empty_status", FormatLabel(source)));
            return;
        }
        if (string.Equals(source.DirectoryPath, target.DirectoryPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var prompt = MessageDialog.Confirm(
            this,
            loc,
            loc.Get("saves.save_transfer_title"),
            loc.Get("saves.save_transfer_prompt", FormatLabel(source), FormatLabel(target)));
        if (!prompt) return;

        try
        {
            var backup = SaveProfileService.CopyProfile(source, target, backupRetention);
            reportStatus(loc.Get("saves.save_transfer_completed_status", FormatLabel(source), FormatLabel(target)));
            if (!string.IsNullOrEmpty(backup))
            {
                // Status already set; show a non-blocking note via status bar — full path is too noisy.
            }
            // Refresh the affected side(s).
            leftSide.RefreshLists();
            rightSide.RefreshLists();
            UpdateButtons();
        }
        catch (Exception ex)
        {
            reportStatus(loc.Get("saves.save_transfer_failed_status", ex.Message));
            MessageDialog.Error(this, loc, loc.Get("saves.save_transfer_error_title"), ex.Message);
        }
    }

    private string FormatLabel(SaveProfileInfo profile)
    {
        var kindLabel = profile.Kind == SaveKind.Vanilla ? loc.Get("saves.vanilla_save_label") : loc.Get("saves.modded_save_label");
        return loc.Get("saves.save_profile_label", kindLabel, profile.ProfileId);
    }

    /// <summary>
    /// One side of the dual picker: location selector + vanilla list + modded list + copy button.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private sealed class SidePanel : UserControl
    {
        private readonly LocalizationService loc;
        private readonly bool isLeft;
        private readonly Action<SidePanel, SaveProfileRow> onRowSelected;
        private readonly Action<SidePanel, SaveProfileInfo, SaveProfileRow?> onDrop;
        private readonly Action<SidePanel> onCopyClick;

        private readonly MaterialComboBox locationCombo;
        private readonly SaveProfileList vanillaList;
        private readonly SaveProfileList moddedList;
        private readonly LinkButton copyButton;
        private SaveProfileRow? selectedRow;
        private IReadOnlyList<SaveLocation> locations = Array.Empty<SaveLocation>();
        private bool suppressLocationEvent;

        public SidePanel(
            LocalizationService loc,
            bool isLeft,
            Action<SidePanel, SaveProfileRow> onRowSelected,
            Action<SidePanel, SaveProfileInfo, SaveProfileRow?> onDrop,
            Action<SidePanel> onCopyClick)
        {
            this.loc = loc;
            this.isLeft = isLeft;
            this.onRowSelected = onRowSelected;
            this.onDrop = onDrop;
            this.onCopyClick = onCopyClick;

            Dock = DockStyle.Fill;
            Margin = new System.Windows.Forms.Padding(isLeft ? 0 : 6, 0, isLeft ? 6 : 0, 0);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();

            locationCombo = new MaterialComboBox
            {
                Dock = DockStyle.Fill,
                Hint = isLeft ? loc.Get("saves.save_source_left_label") : loc.Get("saves.save_source_right_label"),
            };
            locationCombo.MouseWheel += (_, e) =>
            {
                if (e is HandledMouseEventArgs h) h.Handled = true;
            };
            locationCombo.SelectedIndexChanged += (_, _) =>
            {
                if (suppressLocationEvent) return;
                ReloadLists();
                onRowSelected(this, selectedRow!); // notify even if null is OK; just refresh buttons
            };

            vanillaList = new SaveProfileList(loc, OnRowActivated, HandleDrop);
            moddedList = new SaveProfileList(loc, OnRowActivated, HandleDrop);

            var listsLayout = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                RowCount = 2,
            };
            listsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            listsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            listsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            listsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var vanillaHeader = MakeListHeader(loc.Get("saves.vanilla_saves_group"));
            var moddedHeader = MakeListHeader(loc.Get("saves.modded_saves_group"));

            // Stacked layout: 2 list cards top-to-bottom in this side.
            var stack = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                RowCount = 5,
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(vanillaHeader, 0, 0);
            stack.Controls.Add(vanillaList, 0, 1);
            stack.Controls.Add(moddedHeader, 0, 2);
            stack.Controls.Add(moddedList, 0, 3);

            copyButton = new LinkButton
            {
                AutoSize = true,
                Text = isLeft ? loc.Get("saves.save_copy_to_right_button") : loc.Get("saves.save_copy_to_left_button"),
                IsHighlight = true,
                LookDisabled = true,
                Margin = new System.Windows.Forms.Padding(0, 6, 0, 0),
                Anchor = isLeft ? AnchorStyles.Right : AnchorStyles.Left,
            };
            copyButton.Click += (_, _) => onCopyClick(this);
            var buttonHost = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = isLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new System.Windows.Forms.Padding(0),
            };
            buttonHost.Controls.Add(copyButton);
            stack.Controls.Add(buttonHost, 0, 4);

            var outer = new TableLayoutPanel
            {
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                RowCount = 2,
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            outer.Controls.Add(locationCombo, 0, 0);
            outer.Controls.Add(stack, 0, 1);

            Controls.Add(outer);
        }

        public void SetLocations(IReadOnlyList<SaveLocation> all)
        {
            locations = all;
            suppressLocationEvent = true;
            locationCombo.Items.Clear();
            foreach (var loc in all) locationCombo.Items.Add(loc.DisplayName);
            suppressLocationEvent = false;
            if (all.Count == 0)
            {
                locationCombo.Enabled = false;
                vanillaList.SetProfiles(Array.Empty<SaveProfileInfo>());
                moddedList.SetProfiles(Array.Empty<SaveProfileInfo>());
            }
            else
            {
                locationCombo.Enabled = true;
            }
        }

        public void SelectLocationIndex(int index)
        {
            if (index < 0 || index >= locations.Count) return;
            locationCombo.SelectedIndex = index;
        }

        public void RefreshLists() => ReloadLists();

        public SaveProfileInfo? GetSelected() => selectedRow?.Profile;

        public void SetCopyEnabled(bool enabled) => copyButton.LookDisabled = !enabled;

        private void ReloadLists()
        {
            var idx = locationCombo.SelectedIndex;
            if (idx < 0 || idx >= locations.Count) return;
            var loc = locations[idx];
            vanillaList.SetProfiles(SaveProfileService.EnumerateProfiles(loc, SaveKind.Vanilla));
            moddedList.SetProfiles(SaveProfileService.EnumerateProfiles(loc, SaveKind.Modded));
            selectedRow = null;
        }

        private void OnRowActivated(SaveProfileRow row)
        {
            // De-select rows in both lists, then select this one.
            vanillaList.ClearSelection();
            moddedList.ClearSelection();
            row.SetSelected(true);
            selectedRow = row;
            onRowSelected(this, row);
        }

        private void HandleDrop(SaveProfileInfo source, SaveProfileRow? targetRow)
            => onDrop(this, source, targetRow);

        private static Label MakeListHeader(string title)
        {
            return new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 22,
                Text = title,
                Padding = new System.Windows.Forms.Padding(2, 0, 0, 0),
                Font = new Font(FontFamily.GenericSansSerif, 9.5f, FontStyle.Bold),
                Margin = new System.Windows.Forms.Padding(0, 6, 0, 2),
            };
        }
    }

    /// <summary>
    /// Scrollable list of <see cref="SaveProfileRow"/>s with drag-drop accept.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private sealed class SaveProfileList : Panel
    {
        private readonly LocalizationService loc;
        private readonly Action<SaveProfileRow> onRowActivated;
        private readonly Action<SaveProfileInfo, SaveProfileRow?> onDrop;
        private readonly List<SaveProfileRow> rows = new();
        private readonly Panel rowsContainer;

        public SaveProfileList(
            LocalizationService loc,
            Action<SaveProfileRow> onRowActivated,
            Action<SaveProfileInfo, SaveProfileRow?> onDrop)
        {
            this.loc = loc;
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
}
