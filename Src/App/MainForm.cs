// =============================================================================
// MainForm.cs  -  Slay the Spire 2 Mod Manager - main form shell
// =============================================================================
//
// This file holds the ModManagerForm shell: constants, fields, the constructor
// that builds the toolbar/card panel/nav/status UI, and the methods that don't
// fit into a more specific partial. The class is split across:
//
//   Src/App/Program.cs                  Entry point (STAThread -> ModManagerForm)
//   Src/App/MainForm.cs                 (this file) shell, settings I/O,
//                                       config apply, theme/window chrome,
//                                       launch helpers, directory resolution
//   Src/App/MainForm.Updates.cs         GitHub release check + version compare
//   Src/App/MainForm.ModsPage.cs        Mods tab UI + mod move/toggle/select/
//                                       export/conflict resolution
//   Src/App/MainForm.ArchiveInstall.cs  Drag-drop + zip install pipeline
//
// Other concerns live in their own files:
//   Src/Pages/ConfigPage.cs             Configuration tab UserControl
//   Src/Saves/SavesPage.cs              Saves tab UserControl
//   Src/App/ThemeController.cs          Theme mode + Material accent wiring
//   Src/App/Localization/...            JSON-backed LocalizationService
//   Src/App/Models/...                  Records, enums, AppSettings + JSON ctx
//   Src/Infrastructure/ModLoader.cs     Mod manifest discovery + version compare
//   Src/Widgets/WidgetFactory.cs        Section header + button helpers
//
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using Microsoft.Win32;
using System.Text.Json.Serialization;
using STS2ModManager.App;
using STS2ModManager.Mods;
using STS2ModManager.Saves;
using STS2ModManager.Dialogs;
using STS2ModManager.Widgets;

[SupportedOSPlatform("windows")]
internal sealed partial class ModManagerForm : MaterialForm
{
    private const string GameExecutableName = "SlayTheSpire2.exe";
    private const string SteamExecutableName = "steam.exe";
    private const int SlayTheSpire2AppId = 2868840;
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/nyaoouo/STS2ModManager/releases/latest";
    private const string LatestReleaseUrl = "https://github.com/nyaoouo/STS2ModManager/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/nyaoouo/STS2ModManager/releases";
    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UpdateReminderDelay = TimeSpan.FromDays(1);

    private string gameDirectory;
    private string modsDirectory;
    private readonly string settingsFilePath;
    private readonly string buildVersion;

    private string? configuredGamePath;
    private string disabledDirectoryName;
    private string disabledDirectory;
    private AppLanguage language;
    private LaunchMode launchMode;
    private string launchArguments;
    private bool splitModList;
    private bool checkForUpdates;
    private string? skippedUpdateVersion;
    private DateTime? updateRemindAfterUtc;
    private ThemeMode themeMode = ThemeMode.System;
    private int backupRetentionCount = 5;
    private string? latestReleaseVersion;
    private LocalizationService loc;
    private readonly ThemeController themeController;

    private readonly Panel cardScrollPanel;
    private readonly ThinScrollBar cardScrollBar;
    private readonly FlowLayoutPanel cardPanel;
    private readonly MaterialTextBox2 searchBox;
    private readonly LinkButton filterAllButton;
    private readonly LinkButton filterEnabledButton;
    private readonly LinkButton filterDisabledButton;
    private readonly Panel dropOverlay;
    private readonly Label dropOverlayLabel;
    private List<ModInfo> cachedEnabledMods = new();
    private List<ModInfo> cachedDisabledMods = new();
    private string activeSearchTerm = string.Empty;
    private ModFilter activeFilter = ModFilter.All;
    private readonly LinkButton disableAllButton;
    private readonly LinkButton enableAllButton;
    private readonly LinkButton exportButton;
    private readonly LinkButton openFolderButton;
    private readonly LinkButton refreshButton;
    private readonly LinkButton restartButton;
    private readonly MaterialButton windowMinButton;
    private readonly MaterialButton windowMaxButton;
    private readonly MaterialButton windowCloseButton;
    private readonly MaterialButton themeToggleButton;
    private readonly Panel windowDragHandle;
    private readonly HashSet<string> selectedModPaths = new(StringComparer.OrdinalIgnoreCase);
    private int enabledModCount;
    private int disabledModCount;
    private readonly MaterialTabSelector tabSelector;
    private readonly MaterialTabControl tabControl;
    private readonly TabPage modsTabPage;
    private readonly TabPage savesTabPage;
    private readonly TabPage configTabPage;
    private readonly Label disabledFolderLabel;
    private readonly Label rootLabel;
    private readonly Panel statusBar;
    private readonly Label statusLabel;
    private readonly LinkButton infoButton;
    private readonly ToolTip pathsTooltip = new() { AutoPopDelay = 15000, InitialDelay = 200, ReshowDelay = 100 };
    private readonly Control modsPage;

    private SavesPage? savesPage;
    private ConfigPage? configPage;
    private AppPage activePage;
    private readonly string[] startupArchivePaths;

    public ModManagerForm(IReadOnlyList<string>? archivePaths = null)
    {
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 600);
        Size = new Size(1200, 720);
        Sizable = true;
        FormStyle = FormStyles.StatusAndActionBar_None;
        themeController = new ThemeController(this);
        startupArchivePaths = archivePaths?
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .ToArray() ?? Array.Empty<string>();

        settingsFilePath = Path.Combine(AppContext.BaseDirectory, "ModManager.settings.json");
        var settings = LoadSettings();
        configuredGamePath = settings.GamePath;
        disabledDirectoryName = settings.DisabledDirectoryName;
        language = Localization.ParseOrDefault(settings.LanguageCode);
        launchMode = settings.LaunchMode;
        launchArguments = settings.LaunchArguments?.Trim() ?? string.Empty;
        splitModList = settings.SplitModList;
        checkForUpdates = settings.CheckForUpdates;
        skippedUpdateVersion = NormalizeStoredVersion(settings.SkippedUpdateVersion);
        updateRemindAfterUtc = NormalizeUtc(settings.UpdateRemindAfterUtc);
        themeMode = settings.ThemeMode;
        backupRetentionCount = Math.Clamp(settings.BackupRetentionCount, 0, 100);
        if (themeMode != ThemeMode.System)
        {
            themeController.SetMode(themeMode);
        }
        buildVersion = GetBuildVersion();
        loc = new LocalizationService(Localization.ToCode(language));

        try
        {
            gameDirectory = ResolveGameDirectory(configuredGamePath);
        }
        catch (Exception exception)
        {
            MessageDialog.Error(
                this,
                loc,
                loc.Get("game.game_not_found_title"),
                LocalizedFormats.GameNotFoundMessage(loc, exception.Message));
            Environment.Exit(1);
            return;
        }

        modsDirectory = string.Empty;
        disabledDirectory = string.Empty;
        RefreshManagedDirectories(createDirectories: true);

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
        // Double-buffer both scroll host and inner flow panel so diff-based refreshes
        // never paint an intermediate "blank" frame while controls are being reordered.
        EnableDoubleBuffered(cardScrollPanel);
        EnableDoubleBuffered(cardPanel);
        cardScrollBar = ThinScrollBarHost.Attach(cardScrollPanel, cardPanel, manageContentWidth: false);
        themeController.EffectiveThemeChanged += () => cardScrollBar.Invalidate();
        cardScrollPanel.Resize += (_, _) => SyncCardWidths();

        disableAllButton = WidgetFactory.MakeButton();
        enableAllButton = WidgetFactory.MakeButton();
        exportButton = WidgetFactory.MakeButton();
        openFolderButton = WidgetFactory.MakeButton();
        refreshButton = WidgetFactory.MakeButton();
        restartButton = WidgetFactory.MakeButton();
        restartButton.IsHighlight = true;
        infoButton = new LinkButton
        {
            AutoSize = true,
            Text = "\u24D8",
            Margin = new Padding(0),
        };

        searchBox = new MaterialTextBox2
        {
            Width = 240,
            Margin = new Padding(0, 0, 12, 0),
            Hint = string.Empty,
        };
        searchBox.TextChanged += (_, _) =>
        {
            activeSearchTerm = searchBox.Text ?? string.Empty;
            RefreshCardDisplay();
        };

        filterAllButton = WidgetFactory.MakeButton();
        filterEnabledButton = WidgetFactory.MakeButton();
        filterDisabledButton = WidgetFactory.MakeButton();
        filterAllButton.Click += (_, _) => SetActiveFilter(ModFilter.All);
        filterEnabledButton.Click += (_, _) => SetActiveFilter(ModFilter.Enabled);
        filterDisabledButton.Click += (_, _) => SetActiveFilter(ModFilter.Disabled);

        dropOverlay = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            BackColor = Color.FromArgb(220, 235, 250),
        };
        dropOverlayLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(33, 50, 70),
            BackColor = Color.Transparent,
            AutoEllipsis = false,
        };
        dropOverlay.Controls.Add(dropOverlayLabel);

        windowMinButton = new MaterialButton
        {
            AutoSize = false,
            Size = new Size(36, 28),
            Type = MaterialButton.MaterialButtonType.Text,
            Text = string.Empty,
            UseAccentColor = false,
            HighEmphasis = true,
            Margin = new Padding(0),
            Icon = RenderGlyphIcon("\u2014"),
        };
        windowMaxButton = new MaterialButton
        {
            AutoSize = false,
            Size = new Size(36, 28),
            Type = MaterialButton.MaterialButtonType.Text,
            Text = string.Empty,
            UseAccentColor = false,
            HighEmphasis = true,
            Margin = new Padding(0),
            Icon = RenderGlyphIcon("\u25A1"),
        };
        windowCloseButton = new MaterialButton
        {
            AutoSize = false,
            Size = new Size(36, 28),
            Type = MaterialButton.MaterialButtonType.Text,
            Text = string.Empty,
            UseAccentColor = false,
            HighEmphasis = true,
            Margin = new Padding(0),
            Icon = RenderGlyphIcon("\u2716"),
        };
        themeToggleButton = new MaterialButton
        {
            AutoSize = false,
            Size = new Size(36, 28),
            Type = MaterialButton.MaterialButtonType.Text,
            Text = string.Empty,
            UseAccentColor = false,
            HighEmphasis = true,
            Margin = new Padding(0),
            Icon = RenderGlyphIcon("\u263E"),
        };
        themeToggleButton.Click += (_, _) =>
        {
            themeController.SetMode(themeController.IsEffectiveDark ? ThemeMode.Light : ThemeMode.Dark);
        };
        themeController.EffectiveThemeChanged += UpdateThemeToggleIcon;
        themeController.EffectiveThemeChanged += UpdateWindowButtonColors;
        // Re-render mod cards so cached dark/light foreground/background reflect the new theme.
        themeController.EffectiveThemeChanged += () =>
        {
            if (IsDisposed) return;
            try { RefreshCardDisplay(); } catch { /* during early init cardPanel may be null */ }
        };
        UpdateThemeToggleIcon();
        UpdateWindowButtonColors();
        windowMinButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        windowMaxButton.Click += (_, _) =>
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        windowCloseButton.Click += (_, _) => Close();

        windowDragHandle = new Panel
        {
            Dock = DockStyle.Fill,
            Cursor = Cursors.SizeAll,
            BackColor = Color.Transparent,
        };
        windowDragHandle.MouseDown += HandleDragHandleMouseDown;
        windowDragHandle.MouseDoubleClick += (_, _) =>
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;

        tabSelector = new MaterialTabSelector
        {
            Dock = DockStyle.Top,
            Height = 48,
        };
        tabControl = new MaterialTabControl
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        modsTabPage = new TabPage { UseVisualStyleBackColor = true };
        savesTabPage = new TabPage { UseVisualStyleBackColor = true };
        configTabPage = new TabPage { UseVisualStyleBackColor = true };
        tabControl.TabPages.Add(modsTabPage);
        tabControl.TabPages.Add(savesTabPage);
        tabControl.TabPages.Add(configTabPage);
        tabSelector.BaseTabControl = tabControl;

        disabledFolderLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        rootLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            Padding = new Padding(12, 4, 12, 4),
        };
        statusLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        statusBar.Controls.Add(statusLabel);

        SuspendLayout();

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

        var bottomPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2,
            Visible = false,
        };
        bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottomPanel.Controls.Add(rootLabel, 0, 0);
        bottomPanel.Controls.Add(disabledFolderLabel, 0, 1);

        var modsPageLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        modsPageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        modsPageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        modsPageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        modsPageLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        modsPageLayout.Controls.Add(toolbarPanel, 0, 0);
        modsPageLayout.Controls.Add(cardScrollPanel, 0, 1);
        // bottomPanel kept invisible: paths are shown via the info icon tooltip
        modsPageLayout.Controls.Add(bottomPanel, 0, 2);
        modsPage = modsPageLayout;

        // Mods tab page hosts the existing modsPageLayout. Saves / Config tab pages
        // are populated by RebuildPages() once their UserControls exist.
        modsTabPage.Controls.Add(modsPage);

        var windowControls = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        windowControls.Controls.Add(infoButton);
        windowControls.Controls.Add(themeToggleButton);
        windowControls.Controls.Add(windowMinButton);
        windowControls.Controls.Add(windowMaxButton);
        windowControls.Controls.Add(windowCloseButton);

        var headerRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Height = 28,
            Margin = new Padding(0),
        };
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        headerRow.Controls.Add(windowDragHandle, 0, 0);
        headerRow.Controls.Add(windowControls, 1, 0);

        var mainLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(4, 0, 4, 0),
            RowCount = 3,
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainLayout.Controls.Add(headerRow, 0, 0);
        mainLayout.Controls.Add(tabSelector, 0, 1);
        mainLayout.Controls.Add(tabControl, 0, 2);

        Controls.Add(mainLayout);
        Controls.Add(statusBar);
        Controls.Add(dropOverlay);
        dropOverlay.BringToFront();
        themeController.Refresh();

        disableAllButton.Click += (_, _) => ToggleAllMods(enable: false);
        enableAllButton.Click += (_, _) => ToggleAllMods(enable: true);
        exportButton.Click += (_, _) => ExportSelectedMods();
        openFolderButton.Click += (_, _) => OpenSelectedModFolder();
        refreshButton.Click += (_, _) => ReloadLists(loc.Get("status.reloaded_mod_list_status"));
        restartButton.Click += (_, _) => RestartGame();
        tabControl.SelectedIndexChanged += (_, _) => HandleTabChanged();
        Shown += HandleShown;

        EnableArchiveDrop(this);

        ResumeLayout(performLayout: true);

        ApplyLocalizedText();
        RebuildPages();
        ShowPage(AppPage.Mods);
        UpdateDirectoryLabels();
        ReloadLists(loc.Get("status.ready_status", disabledDirectoryName));
    }

    private void RebuildPages()
    {
        savesTabPage.SuspendLayout();
        configTabPage.SuspendLayout();
        savesTabPage.Controls.Clear();
        configTabPage.Controls.Clear();

        savesPage = new SavesPage(loc, SetStatus)
        {
            Dock = DockStyle.Fill,
        };
        savesPage.SetBackupRetention(backupRetentionCount);
        configPage = new ConfigPage(
            loc,
            CurrentConfiguration(),
            gameDirectory,
            buildVersion,
            latestReleaseVersion,
            () => FindGameDirectory(AppContext.BaseDirectory),
            ApplyConfiguration,
            SetStatus)
        {
            Dock = DockStyle.Fill,
        };

        savesTabPage.Controls.Add(savesPage);
        configTabPage.Controls.Add(configPage);
        EnableArchiveDrop(savesPage);
        EnableArchiveDrop(configPage);

        savesTabPage.ResumeLayout(performLayout: true);
        configTabPage.ResumeLayout(performLayout: true);
    }

    private ModManagerConfig CurrentConfiguration()
    {
        return new ModManagerConfig(
            configuredGamePath,
            disabledDirectoryName,
            language,
            launchMode,
            launchArguments,
            splitModList,
            themeMode,
            backupRetentionCount);
    }

    private void ShowPage(AppPage page)
    {
        activePage = page;
        var targetIndex = (int)page;
        if (tabControl.SelectedIndex != targetIndex)
        {
            tabControl.SelectedIndex = targetIndex;
            // SelectedIndexChanged will fire HandleTabChanged for side-effects.
            return;
        }

        ApplyActivePageSideEffects();
    }

    private void HandleTabChanged()
    {
        var index = tabControl.SelectedIndex;
        if (index < 0 || index > 2)
        {
            return;
        }

        activePage = (AppPage)index;
        ApplyActivePageSideEffects();
    }

    private void ApplyActivePageSideEffects()
    {
        switch (activePage)
        {
            case AppPage.Saves:
                savesPage?.RefreshData(loc.Get("saves.save_profiles_reloaded_status"));
                break;
        }
    }

    private void UpdateNavigationButtons()
    {
        if (modsTabPage is not null) modsTabPage.Text = loc.Get("common.mods_page_button");
        if (savesTabPage is not null) savesTabPage.Text = loc.Get("common.saves_page_button");
        if (configTabPage is not null) configTabPage.Text = loc.Get("common.config_page_button");
        tabSelector.Invalidate();
    }

    private void ApplyLocalizedText()
    {
        loc = new LocalizationService(Localization.ToCode(language));
        Text = string.Empty;
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
        dropOverlayLabel.Text = loc.Get("mods.drop_overlay_label");
        UpdateFilterChipStyles();
        UpdateDirectoryLabels();
        UpdateNavigationButtons();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    private void HandleDragHandleMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        // WM_NCLBUTTONDOWN = 0x00A1, HTCAPTION = 2
        SendMessage(Handle, 0x00A1, 2, 0);
    }

    private void UpdateThemeToggleIcon()
    {
        if (themeToggleButton is null) return;
        // Show the icon for the theme you'll switch INTO (sun = "click for light", moon = "click for dark").
        var glyph = themeController.IsEffectiveDark ? "\u263C" : "\u263E";
        themeToggleButton.Icon = RenderGlyphIcon(glyph);
        themeToggleButton.Invalidate();
    }

    private static Bitmap RenderGlyphIcon(string glyph, int size = 24, float fontSize = 18f)
    {
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using var font = new Font("Segoe UI Symbol", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.White);
            using var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), fmt);
        }
        return bmp;
    }

    private void UpdateWindowButtonColors()
    {
        if (windowMinButton is null) return;
        var fg = MaterialSkinManager.Instance.TextHighEmphasisColor;
        windowMinButton.NoAccentTextColor = fg;
        windowMaxButton.NoAccentTextColor = fg;
        windowCloseButton.NoAccentTextColor = fg;
        themeToggleButton.NoAccentTextColor = fg;
        windowMinButton.Invalidate();
        windowMaxButton.Invalidate();
        windowCloseButton.Invalidate();
        themeToggleButton.Invalidate();
        infoButton.Invalidate();
        // Tint the nav bar slightly so it stands out from the page background.
        if (tabSelector is not null)
        {
            tabSelector.BackColor = themeController.IsEffectiveDark
                ? Color.FromArgb(45, 56, 65)   // a touch darker than BlueGrey900 page bg
                : Color.FromArgb(236, 239, 241); // BlueGrey50, contrasts with white page bg
        }
    }

    private void SetStatus(string loc)
    {
        statusLabel.Text = loc;
    }

    private void Notify(string message, int durationMs = 3000)
    {
        try
        {
            new MaterialSkin.Controls.MaterialSnackBar(message, durationMs).Show(this);
        }
        catch
        {
            // Snackbar can fail if the form is not yet shown; fall back silently.
        }
    }

    private void UpdateDirectoryLabels()
    {
        rootLabel.Text = loc.Get("game.game_root_label", gameDirectory);
        disabledFolderLabel.Text = loc.Get("config.disabled_folder_label", disabledDirectoryName, disabledDirectory);
        var tip = rootLabel.Text + Environment.NewLine + disabledFolderLabel.Text;
        pathsTooltip.SetToolTip(infoButton, tip);
    }

    private void RefreshManagedDirectories(bool createDirectories)
    {
        modsDirectory = Path.Combine(gameDirectory, "mods");
        disabledDirectory = Path.Combine(gameDirectory, disabledDirectoryName);

        if (!createDirectories)
        {
            return;
        }

        Directory.CreateDirectory(modsDirectory);
        Directory.CreateDirectory(disabledDirectory);
    }

    private string ResolveGameDirectory(string? preferredPath)
    {
        if (TryNormalizeGameDirectoryPath(preferredPath, out var normalizedGameDirectory))
        {
            return normalizedGameDirectory;
        }

        return FindGameDirectory(AppContext.BaseDirectory);
    }

    private static bool TryNormalizeGameDirectoryPath(string? candidatePath, out string normalizedGameDirectory)
    {
        normalizedGameDirectory = string.Empty;
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var trimmedPath = candidatePath.Trim().Trim('"');
        if (trimmedPath.EndsWith(GameExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            trimmedPath = Path.GetDirectoryName(trimmedPath) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            return false;
        }

        try
        {
            trimmedPath = Path.GetFullPath(trimmedPath);
        }
        catch
        {
            return false;
        }

        if (!ContainsGameExecutable(trimmedPath))
        {
            return false;
        }

        normalizedGameDirectory = trimmedPath;
        return true;
    }

    private void ApplyConfiguration(ModManagerConfig updatedConfiguration)
    {
        if (!TryValidateDirectoryName(updatedConfiguration.DisabledDirectoryName, out var validationError))
        {
            MessageDialog.Warn(
                this,
                loc,
                loc.Get("ui.invalid_directory_name_title"),
                validationError);
            return;
        }

        try
        {
            var resolvedGameDirectory = ResolveGameDirectory(updatedConfiguration.GamePath);
            var normalizedConfiguredGamePath = string.IsNullOrWhiteSpace(updatedConfiguration.GamePath)
                ? null
                : resolvedGameDirectory;
            var newModsDirectory = Path.Combine(resolvedGameDirectory, "mods");
            var newDisabledDirectory = Path.Combine(resolvedGameDirectory, updatedConfiguration.DisabledDirectoryName);
            if (string.Equals(newDisabledDirectory, newModsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                MessageDialog.Warn(
                    this,
                    loc,
                    loc.Get("ui.invalid_directory_title"),
                    loc.Get("config.disabled_folder_matches_mods_message"));
                return;
            }

            var previousGameDirectory = gameDirectory;
            var previousDisabledDirectory = disabledDirectory;
            var sameGameDirectory = string.Equals(previousGameDirectory, resolvedGameDirectory, StringComparison.OrdinalIgnoreCase);
            if (sameGameDirectory &&
                !string.Equals(previousDisabledDirectory, newDisabledDirectory, StringComparison.OrdinalIgnoreCase))
            {
                MigrateDisabledDirectory(previousDisabledDirectory, newDisabledDirectory);
            }

            configuredGamePath = normalizedConfiguredGamePath;
            gameDirectory = resolvedGameDirectory;
            disabledDirectoryName = updatedConfiguration.DisabledDirectoryName;
            language = updatedConfiguration.Language;
            launchMode = updatedConfiguration.LaunchMode;
            launchArguments = updatedConfiguration.LaunchArguments.Trim();
            splitModList = updatedConfiguration.SplitModList;
            themeMode = updatedConfiguration.ThemeMode;
            backupRetentionCount = Math.Clamp(updatedConfiguration.BackupRetentionCount, 0, 100);
            themeController.SetMode(themeMode);
            savesPage?.SetBackupRetention(backupRetentionCount);
            RefreshManagedDirectories(createDirectories: true);
            ApplyLocalizedText();
            SaveSettings(CurrentSettings());
            RebuildPages();
            // Newly created controls were added after SetMode applied the theme; re-apply
            // so MaterialSkin paints their backgrounds correctly (otherwise inputs render
            // with black rectangles until the theme is toggled).
            themeController.Refresh();
            ShowPage(AppPage.Config);
            ReloadLists(loc.Get("config.configuration_updated_status"));
        }
        catch (Exception exception)
        {
            MessageDialog.Error(
                this,
                loc,
                loc.Get("update.update_failed_title"),
                exception.Message);
            SetStatus(loc.Get("config.configuration_update_failed_status", exception.Message));
        }
    }

    private void ConfigureSaves()
    {
        ShowPage(AppPage.Saves);
    }

    private void RestartGame()
    {
        try
        {
            ForceStopGame();
            LaunchConfiguredGame();
            SetStatus(loc.Get("game.game_restarted_status"));
        }
        catch (Exception exception)
        {
            SetStatus(loc.Get("game.game_restart_failed_status", exception.Message));
            MessageDialog.Error(
                this,
                loc,
                loc.Get("game.game_restart_error_title"),
                exception.Message);
        }
    }

    private void ForceStopGame()
    {
        var processName = Path.GetFileNameWithoutExtension(GameExecutableName);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10000);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void LaunchConfiguredGame()
    {
        if (launchMode == LaunchMode.Direct)
        {
            LaunchGameDirectly();
            return;
        }

        LaunchGameViaSteam();
    }

    private void LaunchGameDirectly()
    {
        var gameExecutablePath = Path.Combine(gameDirectory, GameExecutableName);
        if (!File.Exists(gameExecutablePath))
        {
            throw new InvalidOperationException(loc.Get("game.game_executable_missing_message", gameExecutablePath));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = gameExecutablePath,
            Arguments = launchArguments,
            UseShellExecute = true,
            WorkingDirectory = gameDirectory
        };

        Process.Start(startInfo);
    }

    private void LaunchGameViaSteam()
    {
        var steamPath = TryFindSteamExecutablePath();
        if (string.IsNullOrWhiteSpace(steamPath) || !File.Exists(steamPath))
        {
            throw new InvalidOperationException(loc.Get("saves.steam_not_found_message"));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = steamPath,
            Arguments = BuildSteamLaunchArguments(),
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(steamPath) ?? AppContext.BaseDirectory
        };

        Process.Start(startInfo);
    }

    private string BuildSteamLaunchArguments()
    {
        if (string.IsNullOrWhiteSpace(launchArguments))
        {
            return $"-applaunch {SlayTheSpire2AppId}";
        }

        return $"-applaunch {SlayTheSpire2AppId} {launchArguments.Trim()}";
    }

    private void MigrateDisabledDirectory(string oldDirectory, string newDirectory)
    {
        if (string.Equals(oldDirectory, newDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(newDirectory);
        if (!Directory.Exists(oldDirectory))
        {
            return;
        }

        var oldMods = ModLoader.LoadMods(oldDirectory);
        if (oldMods.Count == 0)
        {
            return;
        }

        var shouldMove = MessageDialog.Confirm(
            this,
            loc,
            loc.Get("mods.move_existing_disabled_mods_title"),
            loc.Get("mods.move_existing_disabled_mods_prompt", oldMods.Count, Path.GetFileName(oldDirectory), Path.GetFileName(newDirectory)));

        if (!shouldMove)
        {
            return;
        }

        foreach (var mod in oldMods)
        {
            var targetPath = Path.Combine(newDirectory, mod.FolderName);
            if (Directory.Exists(targetPath))
            {
                throw new InvalidOperationException(loc.Get("ui.cannot_move_existing_disabled_mod_message", mod.FolderName, Path.GetFileName(newDirectory)));
            }

            MoveDirectory(mod.FullPath, targetPath);
        }
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsFilePath))
            {
                return AppSettings.Default;
            }

            var settings = JsonSerializer.Deserialize(
                File.ReadAllText(settingsFilePath),
                ModManagerJsonContext.Default.AppSettings);
            if (settings is null)
            {
                return AppSettings.Default;
            }

            var disabledDirectoryValue = settings.DisabledDirectoryName;
            if (string.IsNullOrWhiteSpace(disabledDirectoryValue) || !TryValidateDirectoryName(disabledDirectoryValue, out _))
            {
                disabledDirectoryValue = AppSettings.Default.DisabledDirectoryName;
            }

            var languageCode = Localization.IsSupported(settings.LanguageCode)
                ? settings.LanguageCode
                : AppSettings.Default.LanguageCode;

            var gamePath = TryNormalizeGameDirectoryPath(settings.GamePath, out var normalizedGameDirectory)
                ? normalizedGameDirectory
                : null;
            var savedLaunchMode = Enum.IsDefined(typeof(LaunchMode), settings.LaunchMode)
                ? settings.LaunchMode
                : AppSettings.Default.LaunchMode;
            var savedLaunchArguments = settings.LaunchArguments?.Trim() ?? string.Empty;
            var savedSkippedUpdateVersion = NormalizeStoredVersion(settings.SkippedUpdateVersion);
            var savedUpdateRemindAfterUtc = NormalizeUtc(settings.UpdateRemindAfterUtc);

            return new AppSettings(
                disabledDirectoryValue,
                languageCode,
                gamePath,
                savedLaunchMode,
                savedLaunchArguments,
                settings.SplitModList,
                settings.CheckForUpdates,
                savedSkippedUpdateVersion,
                savedUpdateRemindAfterUtc,
                Enum.IsDefined(typeof(ThemeMode), settings.ThemeMode) ? settings.ThemeMode : ThemeMode.System,
                Math.Clamp(settings.BackupRetentionCount, 0, 100));
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    private void SaveSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, ModManagerJsonContext.Default.AppSettings);
        File.WriteAllText(settingsFilePath, json);
    }

    private AppSettings CurrentSettings()
    {
        return new AppSettings(
            disabledDirectoryName,
            Localization.ToCode(language),
            configuredGamePath,
            launchMode,
            launchArguments,
            splitModList,
            checkForUpdates,
            skippedUpdateVersion,
            updateRemindAfterUtc,
            themeMode,
            backupRetentionCount);
    }

    private static string FindGameDirectory(string startingDirectory)
    {
        foreach (var candidate in EnumerateGameDirectoryCandidates(startingDirectory))
        {
            if (ContainsGameExecutable(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException(
            $"Could not find {GameExecutableName}. Checked parent directories, Steam libraries, and common install paths.");
    }

    private static IEnumerable<string> EnumerateGameDirectoryCandidates(string startingDirectory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateParentDirectories(startingDirectory))
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }

        var steamPath = TryFindSteamPath();
        if (!string.IsNullOrEmpty(steamPath))
        {
            var defaultLibraryCandidate = Path.Combine(steamPath, "steamapps", "common", "Slay the Spire 2");
            if (seen.Add(defaultLibraryCandidate))
            {
                yield return defaultLibraryCandidate;
            }

            foreach (var candidate in EnumerateSteamLibraryCandidates(steamPath))
            {
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        foreach (var candidate in EnumerateCommonInstallPathCandidates())
        {
            if (seen.Add(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<string> EnumerateParentDirectories(string startingDirectory)
    {
        var currentDirectory = new DirectoryInfo(Path.GetFullPath(startingDirectory));
        while (currentDirectory is not null)
        {
            yield return currentDirectory.FullName;
            currentDirectory = currentDirectory.Parent;
        }
    }

    private static string? TryFindSteamPath()
    {
        foreach (var registryLocation in new[]
        {
            (RegistryHive.CurrentUser, @"SOFTWARE\Valve\Steam"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam")
        })
        {
            foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32, RegistryView.Default })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(registryLocation.Item1, registryView);
                    using var steamKey = baseKey.OpenSubKey(registryLocation.Item2);
                    if (steamKey is null)
                    {
                        continue;
                    }

                    foreach (var valueName in new[] { "SteamPath", "InstallPath" })
                    {
                        if (steamKey.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value))
                        {
                            return value.Replace('/', Path.DirectorySeparatorChar).Trim();
                        }
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static string? TryFindSteamExecutablePath()
    {
        var steamPath = TryFindSteamPath();
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return null;
        }

        if (steamPath.EndsWith(SteamExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            return steamPath;
        }

        return Path.Combine(steamPath, SteamExecutableName);
    }

    private static IEnumerable<string> EnumerateSteamLibraryCandidates(string steamPath)
    {
        foreach (var vdfPath in new[]
        {
            Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamPath, "config", "libraryfolders.vdf")
        })
        {
            string content;
            try
            {
                if (!File.Exists(vdfPath))
                {
                    continue;
                }

                content = File.ReadAllText(vdfPath);
            }
            catch
            {
                continue;
            }

            foreach (Match match in Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\""))
            {
                if (!match.Success)
                {
                    continue;
                }

                var libraryPath = match.Groups[1].Value.Replace("\\\\", "\\");
                if (string.IsNullOrWhiteSpace(libraryPath))
                {
                    continue;
                }

                yield return Path.Combine(libraryPath, "steamapps", "common", "Slay the Spire 2");
            }

            yield break;
        }
    }

    private static IEnumerable<string> EnumerateCommonInstallPathCandidates()
    {
        var relativePaths = new[]
        {
            Path.Combine("SteamLibrary", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Program Files (x86)", "Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Program Files", "Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Games", "Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Games", "SteamLibrary", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Game", "Steam", "steamapps", "common", "Slay the Spire 2"),
            Path.Combine("Game", "SteamLibrary", "steamapps", "common", "Slay the Spire 2")
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
            {
                continue;
            }

            foreach (var relativePath in relativePaths)
            {
                yield return Path.Combine(drive.RootDirectory.FullName, relativePath);
            }
        }
    }

    private static bool ContainsGameExecutable(string directoryPath)
    {
        try
        {
            return File.Exists(Path.Combine(directoryPath, "SlayTheSpire2.exe"));
        }
        catch
        {
            return false;
        }
    }
}

