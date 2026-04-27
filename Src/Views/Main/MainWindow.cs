// =============================================================================
// MainWindow.cs  -  Slay the Spire 2 Mod Manager - top-level window shell
// =============================================================================
//
// This file holds the MainWindow shell: constants, fields, the constructor
// that builds the window-chrome (tab control, header, status bar, drag-drop
// overlay) and the methods that don't fit into a more specific partial. All
// tab-specific UI now lives in dedicated UserControls under Src/Views/Main/.
//
// The class is split across:
//
//   Src/Program.cs                              Entry point (STAThread -> MainWindow)
//   Src/Views/Main/MainWindow.cs                (this file) shell, settings I/O,
//                                               config apply, theme/window chrome,
//                                               launch helpers, directory resolution
//   Src/Views/Main/MainWindow.Updates.cs        GitHub release check + version compare
//   Src/Views/Main/MainWindow.ArchiveInstall.cs Drag-drop + zip install pipeline
//
// Tab UI lives in:
//   Src/Views/Main/ModView.cs (+CardDiff +Operations) Mods tab UserControl
//   Src/Views/Main/ConfigView.cs                      Configuration tab UserControl
//   Src/Views/Main/SaveView.cs                        Saves tab UserControl
//
// Other concerns:
//   Src/Services/UI/ThemeController.cs   Theme mode + Material accent wiring
//   Src/Services/UI/LocalizationService  JSON-backed locale loader
//   Src/Models/...                       Records, enums, AppSettings + JSON ctx
//   Src/Services/ModLoader.cs            Mod manifest discovery + version compare
//   Src/Views/Widgets/WidgetFactory.cs   Section header + button helpers
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
using STS2ModManager.Views.Main;
using STS2ModManager.Views.Dialogs;
using STS2ModManager.Views.Widgets;

[SupportedOSPlatform("windows")]
internal sealed partial class MainWindow : MaterialForm, STS2ModManager.Views.Main.IMainView
{
    public event Action<AppPage>? PageChanged;
    public event Action? RefreshAllRequested;

    void STS2ModManager.Views.Main.IMainView.SetActivePage(AppPage page) => ShowPage(page);

    void STS2ModManager.Views.Main.IMainView.SetStatus(string text) => SetStatus(text);

    void STS2ModManager.Views.Main.IMainView.ShowDropOverlay() => ShowDropOverlay();

    void STS2ModManager.Views.Main.IMainView.HideDropOverlay() => HideDropOverlay();

    private STS2ModManager.Presenters.MainPresenter? mainPresenter;
    private STS2ModManager.Presenters.ModPresenter? modPresenter;
    private STS2ModManager.Presenters.SavePresenter? savePresenter;
    private STS2ModManager.Presenters.ConfigPresenter? configPresenter;

    private string gameDirectory;
    private string modsDirectory;
    private readonly SettingsService settingsService;
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

    private readonly ModView modView;
    private readonly Panel dropOverlay;
    private readonly Label dropOverlayLabel;
    private readonly MaterialButton windowMinButton;
    private readonly MaterialButton windowMaxButton;
    private readonly MaterialButton windowCloseButton;
    private readonly MaterialButton themeToggleButton;
    private readonly Panel windowDragHandle;
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

    private SaveView? savesPage;
    private ConfigView? configPage;
    private AppPage activePage;
    private readonly string[] startupArchivePaths;

    public MainWindow(IReadOnlyList<string>? archivePaths = null)
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

        settingsService = new SettingsService();
        var settings = settingsService.Load();
        configuredGamePath = settings.GamePath;
        disabledDirectoryName = settings.DisabledDirectoryName;
        language = Localization.ParseOrDefault(settings.LanguageCode);
        launchMode = settings.LaunchMode;
        launchArguments = settings.LaunchArguments?.Trim() ?? string.Empty;
        splitModList = settings.SplitModList;
        checkForUpdates = settings.CheckForUpdates;
        skippedUpdateVersion = SettingsService.NormalizeStoredVersion(settings.SkippedUpdateVersion);
        updateRemindAfterUtc = SettingsService.NormalizeUtc(settings.UpdateRemindAfterUtc);
        themeMode = settings.ThemeMode;
        backupRetentionCount = Math.Clamp(settings.BackupRetentionCount, 0, 100);
        if (themeMode != ThemeMode.System)
        {
            themeController.SetMode(themeMode);
        }
        buildVersion = UpdateCheckService.GetBuildVersion();
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

        modView = new ModView(
            loc,
            themeController,
            getModsDirectory: () => modsDirectory,
            getDisabledDirectory: () => disabledDirectory,
            getDisabledDirectoryName: () => disabledDirectoryName,
            getGameDirectory: () => gameDirectory,
            setStatus: SetStatus,
            notify: msg => Notify(msg),
            onDirectoriesChanged: UpdateDirectoryLabels,
            requestReload: text => modPresenter!.Reload(text))
        {
            Dock = DockStyle.Fill,
        };
        modView.SplitModList = splitModList;
        modView.RestartGameRequested += () => RestartGame();
        modView.RefreshRequested += () => RefreshAllRequested?.Invoke();

        infoButton = new LinkButton
        {
            AutoSize = true,
            Text = "\u24D8",
            Margin = new Padding(0),
        };

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

        // Mods tab page hosts the ModView (toolbar + card scroll). Saves /
        // Config tab pages are populated by RebuildPages() once their
        // UserControls exist.
        modsTabPage.Controls.Add(modView);

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

        tabControl.SelectedIndexChanged += (_, _) => HandleTabChanged();
        Shown += HandleShown;

        EnableArchiveDrop(this);

        ResumeLayout(performLayout: true);

        ApplyLocalizedText();
        RebuildPages();
        ShowPage(AppPage.Mods);
        UpdateDirectoryLabels();

        // Phase 5d: ModPresenter owns the load-from-disk path. Created
        // before MainPresenter so the latter can route reloads through it.
        modPresenter = new STS2ModManager.Presenters.ModPresenter(
            modView,
            loc,
            getModsDirectory: () => modsDirectory,
            getDisabledDirectory: () => disabledDirectory,
            setStatus: SetStatus,
            onDirectoriesChanged: UpdateDirectoryLabels,
            getDialogOwner: () => this,
            toggleAll: _ => { /* handled inside ModView (operations partial) */ },
            openSelectedModFolder: () => { /* handled inside ModView (operations partial) */ },
            installArchives: paths => InstallArchives(paths, loc.Get("archive.archive_import_title")),
            exportSelected: () => { /* handled inside ModView (operations partial) */ },
            openModsFolder: () => { /* Phase 5: dedicated open-mods-folder button. */ });

        // Phase 4b: top-level presenter wires window-chrome events to the
        // services + form callbacks. Child presenters (Phase 4c–4e) will be
        // hung off this same instance.
        mainPresenter = new STS2ModManager.Presenters.MainPresenter(
            this,
            loc,
            themeController,
            settingsService,
            reloadLists: modPresenter.Reload,
            applyActivePageSideEffects: page =>
            {
                activePage = page;
                ApplyActivePageSideEffects();
            });

        modPresenter.Reload(loc.Get("status.ready_status", disabledDirectoryName));
    }

    private void RebuildPages()
    {
        savesTabPage.SuspendLayout();
        configTabPage.SuspendLayout();
        savesTabPage.Controls.Clear();
        configTabPage.Controls.Clear();

        savesPage = new SaveView(loc, SetStatus)
        {
            Dock = DockStyle.Fill,
        };
        savePresenter = new STS2ModManager.Presenters.SavePresenter(
            savesPage,
            loc,
            requestRefresh: () => savesPage.RefreshData(loc.Get("saves.save_profiles_reloaded_status")));
        savePresenter.ApplyBackupRetention(backupRetentionCount);
        configPage = new ConfigView(
            loc,
            CurrentConfiguration(),
            gameDirectory,
            buildVersion,
            latestReleaseVersion,
            autoDetectGameDirectory: null,
            applyConfiguration: null,
            setStatus: SetStatus)
        {
            Dock = DockStyle.Fill,
        };
        configPresenter = new STS2ModManager.Presenters.ConfigPresenter(
            configPage,
            loc,
            autoDetectGameDirectory: () => LaunchService.FindGameDirectory(AppContext.BaseDirectory),
            applyConfiguration: ApplyConfiguration);

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
        if (PageChanged is { } handler)
        {
            handler(activePage);
        }
        else
        {
            ApplyActivePageSideEffects();
        }
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
        dropOverlayLabel.Text = loc.Get("mods.drop_overlay_label");
        modView?.ApplyLocalization(loc);
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
        => LaunchService.ResolveGameDirectory(preferredPath);

    private void ApplyConfiguration(ModManagerConfig updatedConfiguration)
    {
        if (!SettingsService.TryValidateDirectoryName(updatedConfiguration.DisabledDirectoryName, loc, out var validationError))
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
            modView.SplitModList = splitModList;
            themeMode = updatedConfiguration.ThemeMode;
            backupRetentionCount = Math.Clamp(updatedConfiguration.BackupRetentionCount, 0, 100);
            themeController.SetMode(themeMode);
            savePresenter?.ApplyBackupRetention(backupRetentionCount);
            RefreshManagedDirectories(createDirectories: true);
            ApplyLocalizedText();
            SaveSettings(CurrentSettings());
            RebuildPages();
            // Newly created controls were added after SetMode applied the theme; re-apply
            // so MaterialSkin paints their backgrounds correctly (otherwise inputs render
            // with black rectangles until the theme is toggled).
            themeController.Refresh();
            ShowPage(AppPage.Config);
            modPresenter?.Reload(loc.Get("config.configuration_updated_status"));
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

    private void ForceStopGame() => LaunchService.ForceStopGame();

    private void LaunchConfiguredGame()
    {
        LaunchService.Launch(
            launchMode,
            gameDirectory,
            launchArguments,
            executablePath => loc.Get("game.game_executable_missing_message", executablePath),
            loc.Get("saves.steam_not_found_message"));
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

    private AppSettings LoadSettings() => settingsService.Load();

    private void SaveSettings(AppSettings settings) => settingsService.Save(settings);

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

}

