// =============================================================================
// ModView.cs  -  Mods tab UserControl (Phase 5c extraction)
// =============================================================================
//
// Implements IModView. Owns the mods-tab toolbar (search box + filter chips +
// action buttons) and the card scroll panel + diff/refresh state. Built by
// MainForm and inserted into the mods tab page.
//
// Split across three partial files to stay under the ~1000-line guideline:
//   ModView.cs             - this file: fields, ctor, layout, IModView events,
//                            theme/localization glue.
//   ModView.CardDiff.cs    - RefreshCardDisplay + BuildDisplayItems +
//                            ReconcileCardEntries + ReconcileControlOrder +
//                            SyncCardWidths + DisplayItem record +
//                            ModCardEntry + double-buffer helpers.
//   ModView.Operations.cs  - ToggleMod / ApplyLocalToggle / ToggleAllMods /
//                            MoveMod / Open/Export/Conflict + dialog
//                            shims + version comparison helpers.
//
// Phase 5c TODO (cleaned up in 5d/5e):
//   * Dialog ownership uses FindForm() to locate the host MainForm. Cleaner
//     would be to push dialog handling up to the presenter.
//   * Toolbar button handlers still call private methods directly AND raise
//     IModView events. The events drive ModPresenter; the direct calls
//     preserve behaviour. 5d removes the duplication once the presenter
//     fully owns orchestration.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using STS2ModManager.Views.Dialogs;
using STS2ModManager.Services;
using STS2ModManager.Services.UI;
using STS2ModManager.Views.Widgets;

namespace STS2ModManager.Views.Main;

[SupportedOSPlatform("windows")]
internal sealed partial class ModView : UserControl, IModView
{
    // --- callbacks back into MainForm (for things ModView intentionally doesn't own) ---
    private LocalizationService loc;
    private readonly ThemeController themeController;
    private readonly Func<string> getModsDirectory;
    private readonly Func<string> getDisabledDirectory;
    private readonly Func<string> getDisabledDirectoryName;
    private readonly Func<string> getGameDirectory;
    private readonly Action<string> setStatus;
    private readonly Action<string> notify;
    private readonly Action onDirectoriesChanged;
    private readonly Action<string> requestReload;

    // --- IModView events ---
    // Some are not consumed yet (Phase 5d collapses internal handling onto these);
    // CS0067 is suppressed pending that wiring.
#pragma warning disable CS0067
    public event Action? RefreshRequested;
    public event Action<ModInfo>? ToggleModRequested;
    public event Action<ModInfo>? UninstallModRequested;
    public event Action<ModInfo>? OpenModFolderRequested;
    public event Action? OpenModsFolderRequested;
    public event Action? EnableAllRequested;
    public event Action? DisableAllRequested;
    public event Action<string>? SearchTextChanged;
    public event Action<ModFilter>? FilterChanged;
    public event Action<IReadOnlyList<string>>? ArchivesDropped;
    public event Action? RestartGameRequested;
#pragma warning restore CS0067

    // --- toolbar controls ---
    private readonly MaterialTextBox2 searchBox;
    private readonly LinkButton filterAllButton;
    private readonly LinkButton filterEnabledButton;
    private readonly LinkButton filterDisabledButton;
    private readonly LinkButton enableAllButton;
    private readonly LinkButton disableAllButton;
    private readonly LinkButton exportButton;
    private readonly LinkButton openFolderButton;
    private readonly LinkButton refreshButton;
    private readonly LinkButton restartButton;

    // --- scroll + card panel ---
    private readonly Panel cardScrollPanel;
    private readonly ThinScrollBar cardScrollBar;
    private readonly FlowLayoutPanel cardPanel;

    // --- mods state ---
    private List<ModInfo> cachedEnabledMods = new();
    private List<ModInfo> cachedDisabledMods = new();
    private string activeSearchTerm = string.Empty;
    private ModFilter activeFilter = ModFilter.All;
    private readonly HashSet<string> selectedModPaths = new(StringComparer.OrdinalIgnoreCase);
    private int enabledModCount;
    private int disabledModCount;
    private bool splitModList;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool SplitModList
    {
        get => splitModList;
        set
        {
            if (splitModList == value) return;
            splitModList = value;
            if (IsHandleCreated) RefreshCardDisplay();
        }
    }

    public ModView(
        LocalizationService loc,
        ThemeController themeController,
        Func<string> getModsDirectory,
        Func<string> getDisabledDirectory,
        Func<string> getDisabledDirectoryName,
        Func<string> getGameDirectory,
        Action<string> setStatus,
        Action<string> notify,
        Action onDirectoriesChanged,
        Action<string> requestReload)
    {
        this.loc = loc;
        this.themeController = themeController;
        this.getModsDirectory = getModsDirectory;
        this.getDisabledDirectory = getDisabledDirectory;
        this.getDisabledDirectoryName = getDisabledDirectoryName;
        this.getGameDirectory = getGameDirectory;
        this.setStatus = setStatus;
        this.notify = notify;
        this.onDirectoriesChanged = onDirectoriesChanged;
        this.requestReload = requestReload;

        // Card scroll panel + flow layout.
        cardScrollPanel = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
        cardPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(4)
        };
        cardScrollPanel.Controls.Add(cardPanel);
        EnableDoubleBuffered(cardScrollPanel);
        EnableDoubleBuffered(cardPanel);
        cardScrollBar = ThinScrollBarHost.Attach(cardScrollPanel, cardPanel, manageContentWidth: false);
        themeController.EffectiveThemeChanged += () => cardScrollBar.Invalidate();
        cardScrollPanel.Resize += (_, _) => SyncCardWidths();

        // Toolbar buttons.
        disableAllButton = WidgetFactory.MakeButton();
        enableAllButton = WidgetFactory.MakeButton();
        exportButton = WidgetFactory.MakeButton();
        openFolderButton = WidgetFactory.MakeButton();
        refreshButton = WidgetFactory.MakeButton();
        restartButton = WidgetFactory.MakeButton();
        restartButton.IsHighlight = true;

        searchBox = new MaterialTextBox2
        {
            Width = 240,
            Margin = new Padding(0, 0, 12, 0),
            Hint = string.Empty,
        };
        searchBox.TextChanged += (_, _) =>
        {
            activeSearchTerm = searchBox.Text ?? string.Empty;
            SearchTextChanged?.Invoke(activeSearchTerm);
            RefreshCardDisplay();
        };

        filterAllButton = WidgetFactory.MakeButton();
        filterEnabledButton = WidgetFactory.MakeButton();
        filterDisabledButton = WidgetFactory.MakeButton();
        filterAllButton.Click += (_, _) => SetActiveFilter(ModFilter.All);
        filterEnabledButton.Click += (_, _) => SetActiveFilter(ModFilter.Enabled);
        filterDisabledButton.Click += (_, _) => SetActiveFilter(ModFilter.Disabled);

        // Toolbar layout.
        var toolbarPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 6),
            WrapContents = true
        };
        toolbarPanel.Controls.Add(searchBox);

        var filterGroup = new LinkButtonGroup
        {
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 12, 0),
        };
        filterGroup.Controls.Add(filterAllButton);
        filterGroup.Controls.Add(filterEnabledButton);
        filterGroup.Controls.Add(filterDisabledButton);
        toolbarPanel.Controls.Add(filterGroup);

        toolbarPanel.Controls.Add(enableAllButton);
        toolbarPanel.Controls.Add(disableAllButton);
        toolbarPanel.Controls.Add(exportButton);
        toolbarPanel.Controls.Add(openFolderButton);
        toolbarPanel.Controls.Add(refreshButton);
        toolbarPanel.Controls.Add(restartButton);

        // Toolbar button click handlers: raise events AND call internal methods
        // (preserves Phase 4c behaviour while feeding the presenter; 5d collapses).
        disableAllButton.Click += (_, _) =>
        {
            DisableAllRequested?.Invoke();
            ToggleAllMods(enable: false);
        };
        enableAllButton.Click += (_, _) =>
        {
            EnableAllRequested?.Invoke();
            ToggleAllMods(enable: true);
        };
        exportButton.Click += (_, _) => ExportSelectedMods();
        openFolderButton.Click += (_, _) =>
        {
            OpenModFolderRequested?.Invoke(null!);
            OpenSelectedModFolder();
        };
        refreshButton.Click += (_, _) => RefreshRequested?.Invoke();
        restartButton.Click += (_, _) => RestartGameRequested?.Invoke();

        // Outer 2-row layout: toolbar (auto) + card scroll (fill).
        var rootLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2,
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rootLayout.Controls.Add(toolbarPanel, 0, 0);
        rootLayout.Controls.Add(cardScrollPanel, 0, 1);

        Dock = DockStyle.Fill;
        Controls.Add(rootLayout);

        // Re-render cards on theme change so cached colours flip.
        themeController.EffectiveThemeChanged += HandleEffectiveThemeChanged;

        ApplyLocalization(loc);
    }

    private void HandleEffectiveThemeChanged()
    {
        if (IsDisposed) return;
        try { RefreshCardDisplay(); } catch { /* during early init cardPanel may be null */ }
    }

    public void ApplyLocalization(LocalizationService newLoc)
    {
        loc = newLoc;
        disableAllButton.Text = loc.Get("ui.disable_all_button");
        enableAllButton.Text = loc.Get("ui.enable_all_button");
        exportButton.Text = loc.Get("ui.export_button");
        openFolderButton.Text = loc.Get("common.open_folder_button");
        refreshButton.Text = loc.Get("common.refresh_button");
        restartButton.Text = loc.Get("common.restart_game_button");
        searchBox.Hint = loc.Get("mods.search_mods_placeholder");
        filterAllButton.Text = loc.Get("mods.filter_all_chip");
        filterEnabledButton.Text = loc.Get("mods.filter_enabled_chip");
        filterDisabledButton.Text = loc.Get("mods.filter_disabled_chip");
        UpdateFilterChipStyles();
    }

    public void RefreshDisplay() => RefreshCardDisplay();

    public void SetMods(IReadOnlyList<ModInfo> enabled, IReadOnlyList<ModInfo> disabled)
    {
        cachedEnabledMods = enabled.ToList();
        cachedDisabledMods = disabled.ToList();
        enabledModCount = cachedEnabledMods.Count;
        disabledModCount = cachedDisabledMods.Count;
        RefreshCardDisplay();
        UpdateButtons();
    }

    public void SetFilterCounts(int enabledCount, int disabledCount)
    {
        enabledModCount = enabledCount;
        disabledModCount = disabledCount;
        UpdateButtons();
    }

    public void SetSelection(IReadOnlyList<string> selectedFullPaths)
    {
        selectedModPaths.Clear();
        foreach (var path in selectedFullPaths)
        {
            selectedModPaths.Add(path);
        }
        RefreshCardDisplay();
        UpdateButtons();
    }

    IReadOnlyList<ModInfo> IModView.GetSelectedMods() => GetSelectedMods();

    private void SetActiveFilter(ModFilter filter)
    {
        activeFilter = filter;
        UpdateFilterChipStyles();
        FilterChanged?.Invoke(filter);
        RefreshCardDisplay();
    }

    private void UpdateFilterChipStyles()
    {
        filterAllButton.IsHighlight      = activeFilter == ModFilter.All;
        filterEnabledButton.IsHighlight  = activeFilter == ModFilter.Enabled;
        filterDisabledButton.IsHighlight = activeFilter == ModFilter.Disabled;
    }

    private void UpdateButtons()
    {
        var selectedCount = selectedModPaths.Count;

        var disableAllOff = enabledModCount == 0;
        disableAllButton.LookDisabled = disableAllOff;
        disableAllButton.Tooltip = disableAllOff ? loc.Get("ui.disable_all_disabled_tooltip") : null;

        var enableAllOff = disabledModCount == 0;
        enableAllButton.LookDisabled = enableAllOff;
        enableAllButton.Tooltip = enableAllOff ? loc.Get("ui.enable_all_disabled_tooltip") : null;

        var exportOff = selectedCount == 0;
        exportButton.LookDisabled = exportOff;
        exportButton.Tooltip = exportOff ? loc.Get("ui.export_disabled_tooltip") : null;

        var openFolderOff = selectedCount != 1;
        openFolderButton.LookDisabled = openFolderOff;
        openFolderButton.Tooltip = openFolderOff ? loc.Get("ui.open_folder_disabled_tooltip") : null;
    }
}
