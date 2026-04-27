using System;
using System.Runtime.Versioning;
using System.Windows.Forms;
using STS2ModManager.Views.Dialogs;
using STS2ModManager.Models;
using STS2ModManager.Services;
using STS2ModManager.Services.UI;
using STS2ModManager.Views.Widgets;

namespace STS2ModManager.Views.Main;

/// <summary>
/// Saves UI: dual-side picker (Steam users + local default players) with
/// custom Material list rows and drag-drop copy between sides. Implements
/// <see cref="ISaveView"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SaveView : UserControl, ISaveView
{
    private int backupRetention = 5;

    // ----- ISaveView events ---------------------------------------------
    // Phase 4d declared the contract; per-profile events (Restore /
    // Delete / Rename / LaunchWithProfile) and the bulk
    // CreateBackupRequested are still un-raised \u2014 the per-card
    // operations stay inside this view until Phase 5d/5e wires them.
#pragma warning disable CS0067
    public event Action? RefreshRequested;
    public event Action<SaveLocation>? CreateBackupRequested;
    public event Action<SaveProfileInfo>? RestoreProfileRequested;
    public event Action<SaveProfileInfo>? DeleteProfileRequested;
    public event Action<SaveProfileInfo>? RenameProfileRequested;
    public event Action<SaveProfileInfo>? LaunchWithProfileRequested;
#pragma warning restore CS0067

    /// <summary>Update how many timestamped backup folders to keep per profile (0 = none).</summary>
    public void SetBackupRetention(int count) => backupRetention = Math.Clamp(count, 0, 100);

    private readonly LocalizationService loc;
    private readonly Action<string> reportStatus;

    private readonly SaveSidePanel leftSide;
    private readonly SaveSidePanel rightSide;
    private readonly Label introLabel;

    public SaveView(LocalizationService loc, Action<string> reportStatus)
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

        leftSide = new SaveSidePanel(loc, isLeft: true, OnRowSelected, OnDropOnSide, OnCopyClicked);
        rightSide = new SaveSidePanel(loc, isLeft: false, OnRowSelected, OnDropOnSide, OnCopyClicked);

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

    private void OnRowSelected(SaveSidePanel _, SaveProfileRow __) => UpdateButtons();

    private void UpdateButtons()
    {
        var leftSel = leftSide.GetSelected();
        var rightSel = rightSide.GetSelected();
        // A side can copy if it has a non-empty source AND the other side has any target slot selected.
        leftSide.SetCopyEnabled(leftSel is not null && leftSel.HasData && rightSel is not null);
        rightSide.SetCopyEnabled(rightSel is not null && rightSel.HasData && leftSel is not null);
    }

    private void OnCopyClicked(SaveSidePanel from)
    {
        var to = (from == leftSide) ? rightSide : leftSide;
        var src = from.GetSelected();
        var dst = to.GetSelected();
        TryCopy(src, dst);
    }

    private void OnDropOnSide(SaveSidePanel target, SaveProfileInfo source, SaveProfileRow? hoverTargetRow)
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
                // Status already set; full backup path is too noisy for the status bar.
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
}
