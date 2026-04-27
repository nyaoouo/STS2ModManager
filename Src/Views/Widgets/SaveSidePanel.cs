using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin.Controls;
using STS2ModManager.Models;
using STS2ModManager.Services;
using STS2ModManager.Services.UI;
using STS2ModManager.Views.Widgets;

namespace STS2ModManager.Views.Widgets;

/// <summary>
/// One side of the dual save-profile picker: location selector + vanilla
/// list + modded list + copy button. Hosted by <see cref="Main.SaveView"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SaveSidePanel : UserControl
{
    private readonly LocalizationService loc;
    private readonly bool isLeft;
    private readonly Action<SaveSidePanel, SaveProfileRow> onRowSelected;
    private readonly Action<SaveSidePanel, SaveProfileInfo, SaveProfileRow?> onDrop;
    private readonly Action<SaveSidePanel> onCopyClick;

    private readonly MaterialComboBox locationCombo;
    private readonly SaveProfileList vanillaList;
    private readonly SaveProfileList moddedList;
    private readonly LinkButton copyButton;
    private SaveProfileRow? selectedRow;
    private IReadOnlyList<SaveLocation> locations = Array.Empty<SaveLocation>();
    private bool suppressLocationEvent;

    public SaveSidePanel(
        LocalizationService loc,
        bool isLeft,
        Action<SaveSidePanel, SaveProfileRow> onRowSelected,
        Action<SaveSidePanel, SaveProfileInfo, SaveProfileRow?> onDrop,
        Action<SaveSidePanel> onCopyClick)
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

        vanillaList = new SaveProfileList(OnRowActivated, HandleDrop);
        moddedList = new SaveProfileList(OnRowActivated, HandleDrop);

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
        foreach (var l in all) locationCombo.Items.Add(l.DisplayName);
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
        var location = locations[idx];
        vanillaList.SetProfiles(SaveProfileService.EnumerateProfiles(location, SaveKind.Vanilla));
        moddedList.SetProfiles(SaveProfileService.EnumerateProfiles(location, SaveKind.Modded));
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
