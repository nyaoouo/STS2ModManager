// =============================================================================
// STS2ModManager.cs  –  Slay the Spire 2 Mod Manager
// =============================================================================
//
// INDEX
// ─────────────────────────────────────────────────────────────────────────────
//  Program                     Entry point (STAThread → ModManagerForm)
//
//    Constants                 GameExecutableName, AppId, etc.
//    Fields                    Form state: directories, UI controls, selection
//    Constructor               Full UI layout (toolbar, card panel, nav, status)
//    Page management           ShowPage, RebuildPages, UpdateNavigationButtons
//    Version/update check      HandleShown, CheckForUpdatesOnStartupAsync,
//                              PromptForUpdate, OpenReleasePage,
//                              TryGetLatestReleaseInfoAsync,
//                              TryGetLatestReleaseInfoFromApiAsync,
//                              TryGetLatestReleaseInfoFromReleasePageAsync,
//                              GetBuildVersion, TryCompareVersions
//    Localization apply        ApplyLocalizedText
//    Mod card UI               SyncCardWidths, CreateSectionHeader,
//                              CreateModCard, SelectCard
//    Mod list                  ReloadLists, UpdateButtons,
//                              GetSelectedMods, GetActiveSelectedMod
//    Mod toggle                ToggleMod, ToggleAllMods
//    Mod move engine           MoveMod, MoveDirectory, CopyDirectory
//    Conflict resolution       FindModsById, PromptConflictResolution,
//                              DeleteModDirectories, DescribeVersionComparison
//    Folder actions            OpenSelectedModFolder, ExportSelectedMods
//    Drag-and-drop             EnableArchiveDrop, HandleArchiveDragEnter/Drop
//    Archive install           InstallArchives, InstallArchive,
//                              InstallArchivePlan, ExtractArchiveToTemporaryFolder
//    Archive parsing           TryReadArchiveInstallPlans,
//                              TryReadArchiveManifestCandidate,
//                              IsManifestCandidatePath, NormalizeArchivePath
//    Game launch               RestartGame, ForceStopGame,
//                              LaunchConfiguredGame, LaunchGameDirectly/ViaSteam
//    Configuration             ApplyConfiguration, CurrentConfiguration,
//                              MigrateDisabledDirectory
//    Settings I/O              LoadSettings, SaveSettings, CurrentSettings
//    Directory helpers         ResolveGameDirectory, FindGameDirectory,
//                              RefreshManagedDirectories,
//                              EnumerateGameDirectoryCandidates,
//                              TryFindSteamPath, EnumerateSteamLibraryCandidates
//    Manifest parsing          LoadMods, ReadModInfo, GetManifestPath,
//                              ReadManifestMetadata, FormatVersionText,
//                              CompareVersionStrings
//    Validation                TryValidateDirectoryName, TryGetSafeRelativePath
//    Utility                   FormatPathForDisplay, TryDeleteDirectory,
//                              UpdateDirectoryLabels
//
//  ConfigPage : UserControl    Configuration tab (game path, general, launch)
//    Constructor               UI layout and control initialization
//    Browse/Auto-detect        BrowseForGamePath, AutoDetectGamePath
//    Launch argument builder   BuildLaunchArguments, ParseLaunchArguments,
//                              TokenizeArguments, AppendOption
//    ParsedLaunchArguments     Inner class for parsed launch flag state
//
//  SaveManagerPage : UserControl   Save file copy/backup tab
//    Constructor               UI layout
//    Data loading              RefreshData, LoadSteamIds, ReadSaveProfileInfo
//    Transfer                  TransferSave, BackupAndReplace
//    Formatting                FormatSaveNotes, FormatSaveProfileLabel
//
//  Data records
//    ModInfo                   Id, Name, Version, FolderName, FullPath
//    SaveProfileInfo           Steam save slot metadata
//    ArchiveInstallPlan        Extraction plan derived from a zip manifest
//    ArchiveInstallStepResult  Per-step install outcome
//    ArchiveEntryInfo          Zip entry + normalized path pair
//    ModManagerConfig          Transient configuration passed to ApplyConfiguration
//    AppSettings               Persisted settings (JSON)
//    ModManagerJsonContext      Source-generation context for AppSettings JSON
//    OperationResult           success + message pair
//    ModMoveResult             ModMoveOutcome + message pair
//    LatestReleaseInfo         Remote release version + page URL
//    LanguageOption            ComboBox display wrapper for AppLanguage
//    LaunchModeOption          ComboBox display wrapper for LaunchMode
//
//  Enums
//    ModMoveOutcome            Changed / Unchanged / Failed
//    UpdatePromptChoice        UpdateNow / RemindLater / Skip / NeverCheck
//    ConflictChoice            KeepIncoming / KeepExisting / Cancel
//    AppLanguage               English / ChineseSimplified
//    LaunchMode                Steam / Direct
//    AppPage                   Mods / Saves / Config
//
//  Localization                Language code ↔ AppLanguage helpers
//  UiText                      All user-facing strings (English / 简体中文)
//
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
using Microsoft.Win32;
using System.Text.Json.Serialization;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ModManagerForm(args));
    }
}

[SupportedOSPlatform("windows")]
internal sealed class ModManagerForm : Form
{
    private const string GameExecutableName = "SlayTheSpire2.exe";
    private const string SteamExecutableName = "steam.exe";
    private const int SlayTheSpire2AppId = 2868840;
    private const string ModManifestFileName = "mod_manifest.json";
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
    private UiText text;

    private readonly Panel cardScrollPanel;
    private readonly FlowLayoutPanel cardPanel;
    private readonly Button disableAllButton;
    private readonly Button enableAllButton;
    private readonly Button exportButton;
    private readonly Button openFolderButton;
    private readonly Button refreshButton;
    private readonly Button restartButton;
    private readonly HashSet<string> selectedModPaths = new(StringComparer.OrdinalIgnoreCase);
    private int enabledModCount;
    private int disabledModCount;
    private readonly MenuStrip navigationMenu;
    private readonly ToolStripMenuItem modsPageMenuItem;
    private readonly ToolStripMenuItem savesPageMenuItem;
    private readonly ToolStripMenuItem configPageMenuItem;
    private readonly ToolStripMenuItem restartMenuItem;
    private readonly Label disabledFolderLabel;
    private readonly Label rootLabel;
    private readonly Label titleLabel;
    private readonly StatusStrip statusStrip;
    private readonly ToolStripStatusLabel statusLabel;
    private readonly Panel pageHost;
    private readonly Control modsPage;

    private SaveManagerPage? savesPage;
    private ConfigPage? configPage;
    private AppPage activePage;
    private readonly string[] startupArchivePaths;

    public ModManagerForm(IReadOnlyList<string>? archivePaths = null)
    {
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 560);
        Size = new Size(1200, 680);
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
        buildVersion = GetBuildVersion();
        text = new UiText(language);

        try
        {
            gameDirectory = ResolveGameDirectory(configuredGamePath);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                text.GameNotFoundMessage(exception.Message),
                text.GameNotFoundTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
        cardScrollPanel.Resize += (_, _) => SyncCardWidths();
        disableAllButton = new Button { AutoSize = true };
        enableAllButton = new Button { AutoSize = true };
        exportButton = new Button { AutoSize = true };
        openFolderButton = new Button { AutoSize = true };
        refreshButton = new Button { AutoSize = true };
        restartButton = new Button { AutoSize = true };
        navigationMenu = new MenuStrip
        {
            AutoSize = false,
            BackColor = SystemColors.Control,
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(2),
            RenderMode = ToolStripRenderMode.System,
            Stretch = true,
            Height = 30
        };
        modsPageMenuItem = new ToolStripMenuItem();
        savesPageMenuItem = new ToolStripMenuItem();
        configPageMenuItem = new ToolStripMenuItem();
        restartMenuItem = new ToolStripMenuItem();
        modsPageMenuItem.Padding = new Padding(8, 4, 8, 4);
        savesPageMenuItem.Padding = new Padding(8, 4, 8, 4);
        configPageMenuItem.Padding = new Padding(8, 4, 8, 4);
        restartMenuItem.Alignment = ToolStripItemAlignment.Right;
        restartMenuItem.Padding = new Padding(8, 4, 8, 4);
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
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        pageHost = new Panel { Dock = DockStyle.Fill };

        SuspendLayout();

        titleLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 12)
        };

        var toolbarPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 6),
            WrapContents = false
        };
        toolbarPanel.Controls.Add(enableAllButton);
        toolbarPanel.Controls.Add(disableAllButton);
        toolbarPanel.Controls.Add(exportButton);
        toolbarPanel.Controls.Add(openFolderButton);
        toolbarPanel.Controls.Add(refreshButton);

        var bottomPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
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
        modsPageLayout.Controls.Add(bottomPanel, 0, 2);
        modsPage = modsPageLayout;

        var navPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            RowCount = 1
        };
        navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        navPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        navigationMenu.Items.Add(modsPageMenuItem);
        navigationMenu.Items.Add(savesPageMenuItem);
        navigationMenu.Items.Add(configPageMenuItem);
        navigationMenu.Items.Add(new ToolStripSeparator());
        navigationMenu.Items.Add(restartMenuItem);
        navPanel.Controls.Add(navigationMenu, 0, 0);

        var mainLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 3
        };

        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainLayout.Controls.Add(titleLabel, 0, 0);
        mainLayout.Controls.Add(navPanel, 0, 1);
        mainLayout.Controls.Add(pageHost, 0, 2);

        statusStrip.Items.Add(statusLabel);

        Controls.Add(mainLayout);
        Controls.Add(statusStrip);

        disableAllButton.Click += (_, _) => ToggleAllMods(enable: false);
        enableAllButton.Click += (_, _) => ToggleAllMods(enable: true);
        exportButton.Click += (_, _) => ExportSelectedMods();
        openFolderButton.Click += (_, _) => OpenSelectedModFolder();
        refreshButton.Click += (_, _) => ReloadLists(text.ReloadedModListStatus);
        restartButton.Click += (_, _) => RestartGame();
        restartMenuItem.Click += (_, _) => RestartGame();
        modsPageMenuItem.Click += (_, _) => ShowPage(AppPage.Mods);
        savesPageMenuItem.Click += (_, _) => ShowPage(AppPage.Saves);
        configPageMenuItem.Click += (_, _) => ShowPage(AppPage.Config);
        Shown += HandleShown;

        EnableArchiveDrop(this);

        ResumeLayout(performLayout: true);

        ApplyLocalizedText();
        RebuildPages();
        ShowPage(AppPage.Mods);
        UpdateDirectoryLabels();
        ReloadLists(text.ReadyStatus(disabledDirectoryName));
    }

    private async void HandleShown(object? sender, EventArgs eventArgs)
    {
        Shown -= HandleShown;

        if (startupArchivePaths.Length > 0)
        {
            InstallArchives(startupArchivePaths, text.ArchiveImportTitle);
        }

        await CheckForUpdatesOnStartupAsync();
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!checkForUpdates || IsDevelopmentBuildVersion(buildVersion))
        {
            return;
        }

        if (updateRemindAfterUtc.HasValue && updateRemindAfterUtc.Value > DateTime.UtcNow)
        {
            return;
        }

        LatestReleaseInfo? latestRelease;
        try
        {
            using var cancellationSource = new CancellationTokenSource(UpdateCheckTimeout);
            latestRelease = await TryGetLatestReleaseInfoAsync(cancellationSource.Token);
        }
        catch (HttpRequestException)
        {
            latestRelease = null;
        }
        catch (OperationCanceledException)
        {
            latestRelease = null;
        }

        if (latestRelease is null)
        {
            SetStatus(text.UpdateCheckUnavailableStatus);
            return;
        }

        if (VersionsMatch(skippedUpdateVersion, latestRelease.Version))
        {
            return;
        }

        if (!TryCompareVersions(buildVersion, latestRelease.Version, out var comparisonResult) || comparisonResult >= 0)
        {
            return;
        }

        var choice = PromptForUpdate(latestRelease);
        switch (choice)
        {
            case UpdatePromptChoice.UpdateNow:
                skippedUpdateVersion = null;
                updateRemindAfterUtc = null;
                SaveSettings(CurrentSettings());
                try
                {
                    OpenReleasePage(latestRelease.ReleasePageUrl);
                    SetStatus(text.UpdatePageOpenedStatus(latestRelease.Version));
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        text.OpenReleasePageFailedMessage(exception.Message),
                        text.UpdateFailedTitle,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    SetStatus(text.UpdateOpenFailedStatus(exception.Message));
                }
                break;

            case UpdatePromptChoice.RemindLater:
                skippedUpdateVersion = null;
                updateRemindAfterUtc = DateTime.UtcNow.Add(UpdateReminderDelay);
                SaveSettings(CurrentSettings());
                SetStatus(text.UpdateReminderScheduledStatus(latestRelease.Version));
                break;

            case UpdatePromptChoice.SkipThisVersion:
                skippedUpdateVersion = latestRelease.Version;
                updateRemindAfterUtc = null;
                SaveSettings(CurrentSettings());
                SetStatus(text.UpdateSkippedStatus(latestRelease.Version));
                break;

            case UpdatePromptChoice.NeverCheck:
                checkForUpdates = false;
                skippedUpdateVersion = latestRelease.Version;
                updateRemindAfterUtc = null;
                SaveSettings(CurrentSettings());
                SetStatus(text.UpdateChecksDisabledStatus);
                break;
        }
    }

    private UpdatePromptChoice PromptForUpdate(LatestReleaseInfo latestRelease)
    {
        using var dialog = new Form
        {
            Text = text.UpdateAvailableTitle,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(560, 220),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var messageBox = new TextBox
        {
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            TabStop = false,
            Text = text.UpdateAvailableMessage(buildVersion, latestRelease.Version)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var updateButton = new Button { AutoSize = true, DialogResult = DialogResult.Yes, Text = text.UpdateNowButton };
        var remindButton = new Button { AutoSize = true, DialogResult = DialogResult.Cancel, Text = text.RemindLaterButton };
        var skipButton = new Button { AutoSize = true, DialogResult = DialogResult.Ignore, Text = text.SkipThisVersionButton };
        var neverCheckButton = new Button { AutoSize = true, DialogResult = DialogResult.No, Text = text.NeverCheckButton };
        buttonPanel.Controls.Add(updateButton);
        buttonPanel.Controls.Add(remindButton);
        buttonPanel.Controls.Add(skipButton);
        buttonPanel.Controls.Add(neverCheckButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(messageBox, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);

        dialog.Controls.Add(layout);
        dialog.AcceptButton = updateButton;
        dialog.CancelButton = remindButton;

        return dialog.ShowDialog(this) switch
        {
            DialogResult.Yes => UpdatePromptChoice.UpdateNow,
            DialogResult.Ignore => UpdatePromptChoice.SkipThisVersion,
            DialogResult.No => UpdatePromptChoice.NeverCheck,
            _ => UpdatePromptChoice.RemindLater
        };
    }

    private void OpenReleasePage(string releasePageUrl)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(releasePageUrl) ? ReleasesPageUrl : releasePageUrl,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    private static async Task<LatestReleaseInfo?> TryGetLatestReleaseInfoAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"STS2ModManager/{GetBuildVersion()}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var latestRelease = await TryGetLatestReleaseInfoFromApiAsync(client, cancellationToken);
        if (latestRelease is not null)
        {
            return latestRelease;
        }

        return await TryGetLatestReleaseInfoFromReleasePageAsync(client, cancellationToken);
    }

    private static async Task<LatestReleaseInfo?> TryGetLatestReleaseInfoFromApiAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagNameElement) || tagNameElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var version = NormalizeStoredVersion(tagNameElement.GetString());
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var releaseUrl = root.TryGetProperty("html_url", out var htmlUrlElement) && htmlUrlElement.ValueKind == JsonValueKind.String
            ? htmlUrlElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(releaseUrl))
        {
            releaseUrl = ReleasesPageUrl;
        }

        return new LatestReleaseInfo(version, releaseUrl);
    }

    private static async Task<LatestReleaseInfo?> TryGetLatestReleaseInfoFromReleasePageAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null ||
            !finalUri.AbsolutePath.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tag = finalUri.Segments.LastOrDefault();
        var version = NormalizeStoredVersion(tag is null ? null : Uri.UnescapeDataString(tag));
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return new LatestReleaseInfo(version, finalUri.AbsoluteUri);
    }

    private static string GetBuildVersion()
    {
        var attribute = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attribute?.InformationalVersion;
        return string.IsNullOrWhiteSpace(version) ? "dev" : version.Trim();
    }

    private static bool IsDevelopmentBuildVersion(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ||
            string.Equals(version.Trim(), "dev", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeStoredVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        return trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(1)
            : trimmed;
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var normalized = value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value;
        return normalized.ToUniversalTime();
    }

    private static bool VersionsMatch(string? left, string? right)
    {
        var normalizedLeft = NormalizeStoredVersion(left);
        var normalizedRight = NormalizeStoredVersion(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft) &&
            string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCompareVersions(string currentVersionText, string latestVersionText, out int comparisonResult)
    {
        comparisonResult = 0;
        if (!TryParseComparableVersion(currentVersionText, out var currentVersion) ||
            !TryParseComparableVersion(latestVersionText, out var latestVersion))
        {
            return false;
        }

        comparisonResult = currentVersion.CompareTo(latestVersion);
        return true;
    }

    private static bool TryParseComparableVersion(string versionText, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (IsDevelopmentBuildVersion(versionText))
        {
            return false;
        }

        var normalizedVersion = NormalizeStoredVersion(versionText);
        if (string.IsNullOrWhiteSpace(normalizedVersion))
        {
            return false;
        }

        var match = Regex.Match(normalizedVersion, @"^\d+(?:\.\d+){0,3}");
        if (!match.Success)
        {
            return false;
        }

        var parts = match.Value.Split('.').ToList();
        while (parts.Count < 4)
        {
            parts.Add("0");
        }

        if (!Version.TryParse(string.Join(".", parts), out var parsedVersion) || parsedVersion is null)
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    private void RebuildPages()
    {
        pageHost.SuspendLayout();
        pageHost.Controls.Clear();

        savesPage = new SaveManagerPage(text, SetStatus)
        {
            Dock = DockStyle.Fill,
            Visible = false
        };
        configPage = new ConfigPage(
            text,
            CurrentConfiguration(),
            gameDirectory,
            () => FindGameDirectory(AppContext.BaseDirectory),
            ApplyConfiguration,
            SetStatus)
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        modsPage.Dock = DockStyle.Fill;
        modsPage.Visible = false;

        pageHost.Controls.Add(modsPage);
        pageHost.Controls.Add(savesPage);
        pageHost.Controls.Add(configPage);
        EnableArchiveDrop(savesPage);
        EnableArchiveDrop(configPage);
        pageHost.ResumeLayout(performLayout: true);
    }

    private ModManagerConfig CurrentConfiguration()
    {
        return new ModManagerConfig(
            configuredGamePath,
            disabledDirectoryName,
            language,
            launchMode,
            launchArguments,
            splitModList);
    }

    private void ShowPage(AppPage page)
    {
        activePage = page;
        modsPage.Visible = page == AppPage.Mods;
        if (savesPage is not null)
        {
            savesPage.Visible = page == AppPage.Saves;
        }

        if (configPage is not null)
        {
            configPage.Visible = page == AppPage.Config;
        }

        switch (page)
        {
            case AppPage.Mods:
                modsPage.BringToFront();
                break;
            case AppPage.Saves:
                savesPage?.BringToFront();
                savesPage?.RefreshData(text.SaveProfilesReloadedStatus);
                break;
            case AppPage.Config:
                configPage?.BringToFront();
                break;
        }

        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        modsPageMenuItem.Checked = activePage == AppPage.Mods;
        savesPageMenuItem.Checked = activePage == AppPage.Saves;
        configPageMenuItem.Checked = activePage == AppPage.Config;
    }

    private void ApplyLocalizedText()
    {
        text = new UiText(language);
        Text = text.AppTitle;
        titleLabel.Text = text.TitleText;
        disableAllButton.Text = text.DisableAllButton;
        enableAllButton.Text = text.EnableAllButton;
        exportButton.Text = text.ExportButton;
        openFolderButton.Text = text.OpenFolderButton;
        refreshButton.Text = text.RefreshButton;
        restartButton.Text = text.RestartGameButton;
        modsPageMenuItem.Text = text.ModsPageButton;
        savesPageMenuItem.Text = text.SavesPageButton;
        configPageMenuItem.Text = text.ConfigPageButton;
        restartMenuItem.Text = text.RestartGameButton;
        UpdateDirectoryLabels();
        UpdateNavigationButtons();
    }

    private void ReloadLists(string statusText)
    {
        try
        {
            UpdateDirectoryLabels();
            var enabledMods = LoadMods(modsDirectory);
            var disabledMods = LoadMods(disabledDirectory);
            enabledModCount = enabledMods.Count;
            disabledModCount = disabledMods.Count;

            cardPanel.SuspendLayout();
            cardPanel.Controls.Clear();

            if (splitModList)
            {
                if (enabledMods.Count > 0)
                {
                    cardPanel.Controls.Add(CreateSectionHeader(text.EnabledGroup(enabledMods.Count)));
                    foreach (var mod in enabledMods)
                    {
                        cardPanel.Controls.Add(CreateModCard(mod, isEnabled: true));
                    }
                }

                if (disabledMods.Count > 0)
                {
                    cardPanel.Controls.Add(CreateSectionHeader(text.DisabledGroup(disabledDirectoryName, disabledMods.Count)));
                    foreach (var mod in disabledMods)
                    {
                        cardPanel.Controls.Add(CreateModCard(mod, isEnabled: false));
                    }
                }
            }
            else
            {
                // Merged view: all mods alphabetically, colored indicator distinguishes state
                var allMods = enabledMods.Select(m => (Mod: m, Enabled: true))
                    .Concat(disabledMods.Select(m => (Mod: m, Enabled: false)))
                    .OrderBy(pair => pair.Mod.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (allMods.Count > 0)
                {
                    cardPanel.Controls.Add(CreateSectionHeader(text.AllModsGroup(allMods.Count)));
                    foreach (var (mod, enabled) in allMods)
                    {
                        cardPanel.Controls.Add(CreateModCard(mod, isEnabled: enabled));
                    }
                }
            }

            if (enabledMods.Count == 0 && disabledMods.Count == 0)
            {
                cardPanel.Controls.Add(CreateSectionHeader(text.NoModsFoundLabel));
            }

            cardPanel.ResumeLayout(performLayout: true);
            selectedModPaths.Clear();
            SyncCardWidths();
            SetStatus(statusText);
            UpdateButtons();
        }
        catch (Exception exception)
        {
            SetStatus(text.LoadFailedStatus(exception.Message));
            MessageBox.Show(
                exception.Message,
                text.LoadErrorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ToggleMod(ModInfo mod, bool enable)
    {
        var targetDirectory = enable ? modsDirectory : disabledDirectory;
        var operationVerb = enable ? "enable" : "disable";
        var result = MoveMod(
            mod,
            targetDirectory,
            operationVerb,
            $"selected mod at {FormatPathForDisplay(mod.FullPath)}",
            showDialogs: true);

        if (result.Outcome == ModMoveOutcome.Changed)
        {
            ReloadLists(result.Message);
            return;
        }

        SetStatus(result.Message);
    }

    private void ToggleAllMods(bool enable)
    {
        var operationVerb = enable ? "enable" : "disable";
        var sourceDirectory = enable ? disabledDirectory : modsDirectory;
        var targetDirectory = enable ? modsDirectory : disabledDirectory;
        var mods = LoadMods(sourceDirectory);

        if (mods.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(
                text.BulkMovePrompt(operationVerb, mods.Count),
                text.BulkMoveTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            SetStatus(text.BulkMoveCanceledStatus(operationVerb));
            return;
        }

        var changedCount = 0;
        var unchangedCount = 0;
        var failedCount = 0;

        foreach (var mod in mods)
        {
            var result = MoveMod(
                mod,
                targetDirectory,
                operationVerb,
                $"bulk action for {mod.Id} at {FormatPathForDisplay(mod.FullPath)}",
                showDialogs: false);

            switch (result.Outcome)
            {
                case ModMoveOutcome.Changed:
                    changedCount++;
                    break;
                case ModMoveOutcome.Failed:
                    failedCount++;
                    break;
                default:
                    unchangedCount++;
                    break;
            }
        }

        var statusMessage = text.BulkMoveCompletedStatus(operationVerb, changedCount, unchangedCount, failedCount);
        if (changedCount > 0)
        {
            ReloadLists(statusMessage);
            return;
        }

        SetStatus(statusMessage);
    }

    private ModMoveResult MoveMod(
        ModInfo selectedMod,
        string targetDirectory,
        string operationVerb,
        string incomingSourceLabel,
        bool showDialogs)
    {

        var conflicts = FindModsById(selectedMod.Id, selectedMod.FullPath);
        if (conflicts.Count > 0)
        {
            var choice = PromptConflictResolution(
                selectedMod,
                conflicts,
                incomingSourceLabel);

            if (choice == ConflictChoice.Cancel)
            {
                return new ModMoveResult(ModMoveOutcome.Unchanged, text.OperationCanceledStatus(operationVerb, selectedMod.Id));
            }

            if (choice == ConflictChoice.KeepExisting)
            {
                DeleteModDirectories(new[] { selectedMod });
                return new ModMoveResult(ModMoveOutcome.Changed, text.KeptExistingStatus(selectedMod.Id));
            }

            DeleteModDirectories(conflicts);
        }

        var targetPath = Path.Combine(targetDirectory, selectedMod.FolderName);
        if (Directory.Exists(targetPath))
        {
            if (showDialogs)
            {
                MessageBox.Show(
                    text.TargetFolderAlreadyExistsMessage(selectedMod.FolderName),
                    text.MoveSkippedTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return new ModMoveResult(ModMoveOutcome.Unchanged, text.MoveSkippedStatus(selectedMod.Id));
        }

        try
        {
            MoveDirectory(selectedMod.FullPath, targetPath);
            return new ModMoveResult(ModMoveOutcome.Changed, text.MoveCompletedStatus(operationVerb, selectedMod.Id, selectedMod.Name));
        }
        catch (Exception exception)
        {
            if (showDialogs)
            {
                MessageBox.Show(
                    exception.Message,
                    text.MoveErrorTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            return new ModMoveResult(ModMoveOutcome.Failed, text.MoveFailedStatus(selectedMod.Id, exception.Message));
        }
    }

    private void OpenSelectedModFolder()
    {
        var selectedMods = GetSelectedMods();
        if (selectedMods.Count == 0)
        {
            SetStatus(text.SelectModFolderStatus);
            return;
        }

        if (selectedMods.Count > 1)
        {
            SetStatus(text.SelectSingleModFolderStatus);
            return;
        }

        var selectedMod = selectedMods[0];

        if (!Directory.Exists(selectedMod.FullPath))
        {
            var message = text.ModFolderMissingMessage(selectedMod.FullPath);
            SetStatus(text.OpenFolderFailedStatus(message));
            MessageBox.Show(
                message,
                text.OpenFolderErrorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{selectedMod.FullPath}\"",
                UseShellExecute = true
            });
            SetStatus(text.OpenFolderOpenedStatus(selectedMod.Id));
        }
        catch (Exception exception)
        {
            SetStatus(text.OpenFolderFailedStatus(exception.Message));
            MessageBox.Show(
                exception.Message,
                text.OpenFolderErrorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ExportSelectedMods()
    {
        var selectedMods = GetSelectedMods();
        if (selectedMods.Count == 0)
        {
            SetStatus(text.SelectModsExportStatus);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "zip",
            FileName = BuildDefaultExportArchiveName(selectedMods),
            Filter = text.ExportArchiveFilter,
            OverwritePrompt = true,
            Title = text.ExportArchiveDialogTitle
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            SetStatus(text.ExportCanceledStatus);
            return;
        }

        try
        {
            CreateModArchive(dialog.FileName, selectedMods);
            var message = text.ExportCompletedStatus(selectedMods.Count, dialog.FileName);
            SetStatus(message);
            MessageBox.Show(
                text.ExportCompletedMessage(selectedMods.Count, dialog.FileName),
                text.ExportArchiveDialogTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            SetStatus(text.ExportFailedStatus(exception.Message));
            MessageBox.Show(
                exception.Message,
                text.ExportFailedTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private List<ModInfo> GetSelectedMods()
    {
        var selectedMods = new List<ModInfo>();
        foreach (Control control in cardPanel.Controls)
        {
            if (control is Panel panel &&
                panel.BorderStyle == BorderStyle.FixedSingle &&
                panel.Tag is ModInfo mod &&
                selectedModPaths.Contains(mod.FullPath))
            {
                selectedMods.Add(mod);
            }
        }

        return selectedMods;
    }

    private ModInfo? GetActiveSelectedMod()
    {
        var selectedMods = GetSelectedMods();
        return selectedMods.Count == 1 ? selectedMods[0] : null;
    }

    private string BuildDefaultExportArchiveName(IReadOnlyList<ModInfo> selectedMods)
    {
        return selectedMods.Count == 1
            ? $"{selectedMods[0].FolderName}.zip"
            : $"mods-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    }

    private static void CreateModArchive(string archivePath, IReadOnlyList<ModInfo> mods)
    {
        var directory = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var mod in mods.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(mod.FullPath))
            {
                throw new DirectoryNotFoundException(mod.FullPath);
            }

            AddDirectoryToArchive(archive, mod.FullPath, mod.FolderName);
        }
    }

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDirectory, string archiveRoot)
    {
        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            archive.CreateEntry($"{NormalizeArchivePath(archiveRoot)}/");
            return;
        }

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
            var entryName = NormalizeArchivePath(Path.Combine(archiveRoot, relativePath));
            archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
        }
    }


    private void EnableArchiveDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += HandleArchiveDragEnter;
        control.DragDrop += HandleArchiveDragDrop;

        foreach (Control child in control.Controls)
        {
            EnableArchiveDrop(child);
        }
    }

    private void HandleArchiveDragEnter(object? sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.Data?.GetDataPresent(DataFormats.FileDrop) ?? true)
        {
            eventArgs.Effect = DragDropEffects.None;
            return;
        }

        var droppedPaths = eventArgs.Data?.GetData(DataFormats.FileDrop) as string[];
        eventArgs.Effect = droppedPaths is { Length: > 0 }
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void HandleArchiveDragDrop(object? sender, DragEventArgs eventArgs)
    {
        var droppedPaths = eventArgs.Data?.GetData(DataFormats.FileDrop) as string[];
        if (droppedPaths is null || droppedPaths.Length == 0)
        {
            return;
        }

        InstallArchives(droppedPaths, text.ArchiveImportTitle);
    }

    private void InstallArchives(IEnumerable<string> archivePaths, string dialogTitle)
    {
        var results = new List<OperationResult>();
        foreach (var path in archivePaths)
        {
            if (File.Exists(path))
            {
                results.Add(InstallArchive(path));
            }
        }

        if (results.Count == 0)
        {
            SetStatus(text.NoFilesProvidedStatus);
            return;
        }

        var summary = string.Join(Environment.NewLine, results.Select(result => result.Message));
        if (results.Any(result => result.RefreshRequired))
        {
            ReloadLists(summary);
        }
        else
        {
            SetStatus(summary);
        }

        MessageBox.Show(
            summary,
            dialogTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private OperationResult InstallArchive(string archivePath)
    {
        if (!TryReadArchiveInstallPlans(archivePath, out var installPlans, out var errorMessage))
        {
            return new OperationResult(false, $"{Path.GetFileName(archivePath)}: {errorMessage}");
        }

        var results = new List<OperationResult>();
        foreach (var installPlan in installPlans)
        {
            var stepResult = InstallArchivePlan(archivePath, installPlan);
            results.Add(stepResult.Result);
            if (stepResult.StopProcessingArchive)
            {
                break;
            }
        }

        return new OperationResult(
            results.Any(result => result.RefreshRequired),
            string.Join(Environment.NewLine, results.Select(result => result.Message)));
    }

    private ArchiveInstallStepResult InstallArchivePlan(string archivePath, ArchiveInstallPlan installPlan)
    {
        var archiveFileName = Path.GetFileName(archivePath);

        var incomingMod = new ModInfo(
            installPlan.Id,
            installPlan.Name,
            installPlan.Version,
                installPlan.InstallFolderName,
                Path.Combine(disabledDirectory, installPlan.InstallFolderName));

        var conflicts = FindModsById(incomingMod.Id);
        if (conflicts.Count > 0)
        {
            var choice = PromptConflictResolution(
                incomingMod,
                conflicts,
                $"archive {archiveFileName}");

            if (choice == ConflictChoice.Cancel)
            {
                return new ArchiveInstallStepResult(
                    new OperationResult(false, text.ArchiveInstallCanceled(archiveFileName)),
                    StopProcessingArchive: true);
            }

            if (choice == ConflictChoice.KeepExisting)
            {
                return new ArchiveInstallStepResult(
                    new OperationResult(false, text.ArchiveKeptExisting(archiveFileName, incomingMod.Id)),
                    StopProcessingArchive: false);
            }

            DeleteModDirectories(conflicts);
        }

        var targetPath = Path.Combine(disabledDirectory, installPlan.InstallFolderName);
        if (Directory.Exists(targetPath))
        {
            return new ArchiveInstallStepResult(
                new OperationResult(false, text.ArchiveTargetFolderExists(archiveFileName, installPlan.InstallFolderName)),
                StopProcessingArchive: false);
        }

        string? extractionRoot = null;
        try
        {
            extractionRoot = ExtractArchiveToTemporaryFolder(archivePath, installPlan);
            MoveDirectory(Path.Combine(extractionRoot, installPlan.InstallFolderName), targetPath);
            return new ArchiveInstallStepResult(
                new OperationResult(true, text.ArchiveInstalled(
                    archiveFileName,
                    installPlan.Id,
                    disabledDirectoryName,
                    installPlan.ArchiveFolderName,
                    installPlan.InstallFolderName)),
                StopProcessingArchive: false);
        }
        catch (Exception exception)
        {
            return new ArchiveInstallStepResult(
                new OperationResult(false, text.ArchiveInstallFailed(archiveFileName, exception.Message)),
                StopProcessingArchive: false);
        }
        finally
        {
            if (!string.IsNullOrEmpty(extractionRoot))
            {
                TryDeleteDirectory(extractionRoot);
            }
        }
    }

    private const int CardMinWidth = 250;
    private const int CardSpacing = 6;
    private const int CardHeight = 90;

    private void SyncCardWidths()
    {
        var scrollWidth = cardScrollPanel.ClientSize.Width;
        if (scrollWidth <= 0)
        {
            return;
        }

        // Subtract scrollbar width so the panel never triggers a horizontal scrollbar.
        // Pinning both Min and Max width breaks the AutoSize circular dependency:
        // AutoSize=true still manages height, but width is locked to exactly panelWidth.
        var panelWidth = Math.Max(CardMinWidth + 24, scrollWidth - SystemInformation.VerticalScrollBarWidth);
        cardPanel.MinimumSize = new Size(panelWidth, 0);
        cardPanel.MaximumSize = new Size(panelWidth, 0);

        var available = panelWidth - cardPanel.Padding.Horizontal;
        var cardsPerRow = Math.Max(1, available / (CardMinWidth + CardSpacing * 2));
        var cardWidth = (available - cardsPerRow * CardSpacing * 2) / cardsPerRow;
        var headerWidth = available - CardSpacing * 2;

        foreach (Control c in cardPanel.Controls)
        {
            // Section headers span the full row; mod cards share the row
            if (c is Panel p && p.BorderStyle == BorderStyle.FixedSingle)
            {
                c.Width = Math.Max(CardMinWidth, cardWidth);
            }
            else
            {
                c.Width = Math.Max(CardMinWidth, headerWidth);
            }
        }
    }

    private Panel CreateSectionHeader(string label)
    {
        var header = new Panel
        {
            Height = 26,
            Margin = new Padding(CardSpacing, CardSpacing + 2, CardSpacing, 2)
        };
        var headerLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = SystemColors.GrayText,
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0)
        };
        header.Controls.Add(headerLabel);
        return header;
    }

    private Panel CreateModCard(ModInfo mod, bool isEnabled)
    {
        var statusColor = isEnabled
            ? Color.FromArgb(76, 175, 80)
            : Color.FromArgb(180, 180, 180);

        var card = new Panel
        {
            Height = CardHeight,
            Margin = new Padding(CardSpacing),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Cursor = Cursors.Hand,
            Tag = mod
        };

        var indicator = new Panel
        {
            Dock = DockStyle.Left,
            Width = 5,
            BackColor = statusColor
        };

        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(8, 6, 8, 6)
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // Keep enough room for a wrapped two-line title without leaving a large dead zone above it.
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var nameLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = isEnabled ? SystemColors.ControlText : SystemColors.GrayText,
            Text = mod.Name,
            // No AutoEllipsis: text wraps naturally within the fixed-width label
            TextAlign = ContentAlignment.TopLeft
        };

        var detailLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = $"ID: {mod.Id}  \u2022  {FormatVersionText(mod.Version)}  \u2022  {mod.FolderName}",
            TextAlign = ContentAlignment.TopLeft
        };

        var toggleButton = new Button
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Text = isEnabled ? text.DisableModButton : text.EnableModButton
        };
        toggleButton.Click += (_, _) => ToggleMod(mod, enable: !isEnabled);

        contentLayout.Controls.Add(nameLabel, 0, 0);
        contentLayout.Controls.Add(toggleButton, 1, 0);
        contentLayout.SetRowSpan(toggleButton, 2);
        contentLayout.Controls.Add(detailLabel, 0, 1);

        card.Controls.Add(contentLayout);
        card.Controls.Add(indicator);

        void SelectThis(object? s, EventArgs e) => SelectCard(card, mod);
        card.Click += SelectThis;
        contentLayout.Click += SelectThis;
        nameLabel.Click += SelectThis;
        detailLabel.Click += SelectThis;
        indicator.Click += SelectThis;

        return card;
    }

    private void SelectCard(Panel card, ModInfo mod)
    {
        var toggleSelection = (ModifierKeys & Keys.Control) == Keys.Control;
        if (toggleSelection)
        {
            if (!selectedModPaths.Add(mod.FullPath))
            {
                selectedModPaths.Remove(mod.FullPath);
            }
        }
        else
        {
            selectedModPaths.Clear();
            selectedModPaths.Add(mod.FullPath);
        }

        foreach (Control c in cardPanel.Controls)
        {
            if (c is Panel p && p.BorderStyle == BorderStyle.FixedSingle && p.Tag is ModInfo currentMod)
            {
                p.BackColor = selectedModPaths.Contains(currentMod.FullPath)
                    ? Color.FromArgb(219, 234, 249)
                    : SystemColors.Window;
            }
        }

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var selectedCount = selectedModPaths.Count;
        disableAllButton.Enabled = enabledModCount > 0;
        enableAllButton.Enabled = disabledModCount > 0;
        exportButton.Enabled = selectedCount > 0;
        openFolderButton.Enabled = selectedCount == 1;
    }

    private void SetStatus(string text)
    {
        statusLabel.Text = text;
    }

    private void UpdateDirectoryLabels()
    {
        rootLabel.Text = text.GameRootLabel(gameDirectory);
        disabledFolderLabel.Text = text.DisabledFolderLabel(disabledDirectoryName, disabledDirectory);
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
            MessageBox.Show(
                validationError,
                text.InvalidDirectoryNameTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
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
                MessageBox.Show(
                    text.DisabledFolderMatchesModsMessage,
                    text.InvalidDirectoryTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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
            RefreshManagedDirectories(createDirectories: true);
            ApplyLocalizedText();
            SaveSettings(CurrentSettings());
            RebuildPages();
            ShowPage(AppPage.Config);
            ReloadLists(text.ConfigurationUpdatedStatus);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                text.UpdateFailedTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus(text.ConfigurationUpdateFailedStatus(exception.Message));
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
            SetStatus(text.GameRestartedStatus);
        }
        catch (Exception exception)
        {
            SetStatus(text.GameRestartFailedStatus(exception.Message));
            MessageBox.Show(
                exception.Message,
                text.GameRestartErrorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
            throw new InvalidOperationException(text.GameExecutableMissingMessage(gameExecutablePath));
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
            throw new InvalidOperationException(text.SteamNotFoundMessage);
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

    private string? PromptForDirectoryName(string currentName)
    {
        using var dialog = new Form
        {
            Text = text.DisabledFolderDialogTitle,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(520, 170),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var promptLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.DisabledFolderPrompt
        };

        var nameTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = currentName
        };

        var hintLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = text.DisabledFolderHint
        };

        var okButton = new Button { AutoSize = true, Text = text.SaveButton };
        var cancelButton = new Button { AutoSize = true, Text = text.CancelButton };

        okButton.Click += (_, _) => dialog.DialogResult = DialogResult.OK;
        cancelButton.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(promptLabel, 0, 0);
        layout.Controls.Add(nameTextBox, 0, 1);
        layout.Controls.Add(hintLabel, 0, 2);
        layout.Controls.Add(buttons, 0, 3);

        dialog.Controls.Add(layout);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var candidate = nameTextBox.Text.Trim();
            if (TryValidateDirectoryName(candidate, out var validationError))
            {
                return candidate;
            }

            MessageBox.Show(
                validationError,
                text.InvalidDirectoryNameTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            nameTextBox.Focus();
            nameTextBox.SelectAll();
        }

        return null;
    }

    private AppLanguage? PromptForLanguage(AppLanguage currentLanguage)
    {
        using var dialog = new Form
        {
            Text = text.LanguageDialogTitle,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(360, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var promptLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.LanguagePrompt
        };

        var languageOptions = new[]
        {
            new LanguageOption(AppLanguage.English, "English"),
            new LanguageOption(AppLanguage.ChineseSimplified, "简体中文")
        };

        var languageComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };
        languageComboBox.Items.AddRange(languageOptions.Cast<object>().ToArray());
        languageComboBox.SelectedItem = languageOptions.First(option => option.Language == currentLanguage);

        var okButton = new Button { AutoSize = true, Text = text.SaveButton };
        var cancelButton = new Button { AutoSize = true, Text = text.CancelButton };

        okButton.Click += (_, _) => dialog.DialogResult = DialogResult.OK;
        cancelButton.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(promptLabel, 0, 0);
        layout.Controls.Add(languageComboBox, 0, 1);
        layout.Controls.Add(buttons, 0, 2);

        dialog.Controls.Add(layout);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog(this) == DialogResult.OK
            ? ((LanguageOption)languageComboBox.SelectedItem!).Language
            : null;
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

        var oldMods = LoadMods(oldDirectory);
        if (oldMods.Count == 0)
        {
            return;
        }

        var shouldMove = MessageBox.Show(
            text.MoveExistingDisabledModsPrompt(oldMods.Count, Path.GetFileName(oldDirectory), Path.GetFileName(newDirectory)),
            text.MoveExistingDisabledModsTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;

        if (!shouldMove)
        {
            return;
        }

        foreach (var mod in oldMods)
        {
            var targetPath = Path.Combine(newDirectory, mod.FolderName);
            if (Directory.Exists(targetPath))
            {
                throw new InvalidOperationException(text.CannotMoveExistingDisabledModMessage(mod.FolderName, Path.GetFileName(newDirectory)));
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
                savedUpdateRemindAfterUtc);
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
            updateRemindAfterUtc);
    }

    private bool TryValidateDirectoryName(string value, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = text.EmptyFolderNameMessage;
            return false;
        }

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errorMessage = text.InvalidFolderCharactersMessage;
            return false;
        }

        if (value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
        {
            errorMessage = text.SingleFolderNameMessage;
            return false;
        }

        if (value is "." or "..")
        {
            errorMessage = text.DotFolderNameMessage;
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private List<ModInfo> FindModsById(string modId, params string[] excludedPaths)
    {
        var excluded = new HashSet<string>(
            excludedPaths.Select(path => Path.GetFullPath(path)),
            StringComparer.OrdinalIgnoreCase);

        return LoadMods(modsDirectory)
            .Concat(LoadMods(disabledDirectory))
            .Where(mod =>
                string.Equals(mod.Id, modId, StringComparison.OrdinalIgnoreCase) &&
                !excluded.Contains(Path.GetFullPath(mod.FullPath)))
            .ToList();
    }

    private ConflictChoice PromptConflictResolution(ModInfo incomingMod, IReadOnlyList<ModInfo> existingMods, string incomingSourceLabel)
    {
        using var dialog = new Form
        {
            Text = text.DuplicateModIdTitle,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(760, 360),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };

        var messageBuilder = new StringBuilder();
        messageBuilder.AppendLine(text.DuplicateModIdMessage(incomingMod.Id));
        var comparisonMessage = DescribeVersionComparison(incomingMod, existingMods);
        if (!string.IsNullOrEmpty(comparisonMessage))
        {
            messageBuilder.AppendLine(comparisonMessage);
        }
        messageBuilder.AppendLine();
        messageBuilder.AppendLine(text.IncomingVersionLabel);
        messageBuilder.AppendLine(text.IncomingVersionLine(incomingMod.Name, FormatVersionText(incomingMod.Version), incomingMod.FolderName, incomingSourceLabel));
        messageBuilder.AppendLine();
        messageBuilder.AppendLine(text.ExistingVersionsLabel);

        foreach (var mod in existingMods)
        {
            messageBuilder.AppendLine(text.ExistingVersionLine(mod.Name, FormatVersionText(mod.Version), mod.FolderName, FormatPathForDisplay(mod.FullPath)));
        }

        messageBuilder.AppendLine();
        messageBuilder.AppendLine(text.KeepIncomingHelpText);
        messageBuilder.AppendLine(text.KeepExistingHelpText);

        var messageBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = messageBuilder.ToString()
        };

        var keepIncomingButton = new Button { AutoSize = true, Text = text.KeepIncomingButton };
        var keepExistingButton = new Button { AutoSize = true, Text = text.KeepExistingButton };
        var cancelButton = new Button { AutoSize = true, Text = text.CancelButton };

        keepIncomingButton.Click += (_, _) => dialog.DialogResult = DialogResult.Yes;
        keepExistingButton.Click += (_, _) => dialog.DialogResult = DialogResult.No;
        cancelButton.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(keepExistingButton);
        buttonPanel.Controls.Add(keepIncomingButton);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(messageBox, 0, 0);
        layout.Controls.Add(buttonPanel, 0, 1);

        dialog.Controls.Add(layout);
        dialog.AcceptButton = keepIncomingButton;
        dialog.CancelButton = cancelButton;

        return dialog.ShowDialog(this) switch
        {
            DialogResult.Yes => ConflictChoice.KeepIncoming,
            DialogResult.No => ConflictChoice.KeepExisting,
            _ => ConflictChoice.Cancel
        };
    }

    private void DeleteModDirectories(IEnumerable<ModInfo> mods)
    {
        foreach (var mod in mods)
        {
            TryDeleteDirectory(mod.FullPath);
        }
    }

    private string FormatPathForDisplay(string path)
    {
        if (path.StartsWith(gameDirectory, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = path.Substring(gameDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relativePath.Length == 0 ? "." : relativePath;
        }

        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void MoveDirectory(string sourcePath, string destinationPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destinationPath = Path.GetFullPath(destinationPath);

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: {sourcePath}");
        }

        if (Directory.Exists(destinationPath))
        {
            throw new IOException($"Destination directory already exists: {destinationPath}");
        }

        if (string.Equals(Path.GetPathRoot(sourcePath), Path.GetPathRoot(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(sourcePath, destinationPath);
            return;
        }

        CopyDirectory(sourcePath, destinationPath);
        Directory.Delete(sourcePath, recursive: true);
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var filePath in Directory.EnumerateFiles(sourcePath))
        {
            var targetFilePath = Path.Combine(destinationPath, Path.GetFileName(filePath));
            File.Copy(filePath, targetFilePath, overwrite: false);
        }

        foreach (var childDirectoryPath in Directory.EnumerateDirectories(sourcePath))
        {
            var childDirectoryName = Path.GetFileName(childDirectoryPath);
            var targetChildDirectoryPath = Path.Combine(destinationPath, childDirectoryName);
            CopyDirectory(childDirectoryPath, targetChildDirectoryPath);
        }
    }

    private bool TryReadArchiveInstallPlans(string archivePath, out IReadOnlyList<ArchiveInstallPlan> installPlans, out string errorMessage)
    {
        installPlans = Array.Empty<ArchiveInstallPlan>();
        errorMessage = string.Empty;

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var archiveEntries = archive.Entries
                .Select(entry => new ArchiveEntryInfo(entry, NormalizeArchivePath(entry.FullName)))
                .Where(entry => !string.IsNullOrEmpty(entry.NormalizedPath))
                .ToList();

            var manifestEntries = archiveEntries
                .Where(entry => !string.IsNullOrEmpty(entry.Entry.Name) && IsManifestCandidatePath(entry.NormalizedPath))
                .ToList();

            var manifestCountsByDirectory = manifestEntries
                .GroupBy(entry => GetArchiveDirectory(entry.NormalizedPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            var candidates = manifestEntries
                .Select(entry => TryReadArchiveManifestCandidate(entry, archiveEntries, manifestCountsByDirectory, out var candidate) ? candidate : (ArchiveInstallPlan?)null)
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!)
                .OrderBy(candidate => candidate.ManifestDepth)
                .ThenBy(candidate => candidate.EntryPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
            {
                errorMessage = text.InvalidArchiveManifestMessage;
                return false;
            }

            installPlans = candidates;
            return true;
        }
        catch (InvalidDataException)
        {
            errorMessage = text.UnsupportedZipArchiveMessage;
            return false;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    private static bool TryReadArchiveManifestCandidate(
        ArchiveEntryInfo entry,
        IReadOnlyList<ArchiveEntryInfo> archiveEntries,
        IReadOnlyDictionary<string, int> manifestCountsByDirectory,
        out ArchiveInstallPlan installPlan)
    {
        installPlan = null!;

        var entryPath = entry.NormalizedPath;

        try
        {
            using var stream = entry.Entry.Open();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var segments = entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var directoryPath = GetArchiveDirectory(entryPath);
            var fileName = Path.GetFileName(entryPath);
            var fileStem = Path.GetFileNameWithoutExtension(fileName);
            var isSpecialManifestFile = string.Equals(fileName, ModManifestFileName, StringComparison.OrdinalIgnoreCase);

            var directoryName = string.IsNullOrEmpty(directoryPath)
                ? string.Empty
                : directoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];
            var manifestCountInDirectory = manifestCountsByDirectory.TryGetValue(directoryPath, out var count)
                ? count
                : 0;
            var totalManifestCount = manifestCountsByDirectory.Values.Sum();

            var modId = fileStem;
            var modName = fileStem;
            string? modVersion = null;

            ReadManifestMetadata(document.RootElement, ref modId, ref modName, ref modVersion);

            var archiveFolderName = ResolveArchiveFolderName(directoryName, fileStem, modId, isSpecialManifestFile);
            if (string.IsNullOrWhiteSpace(archiveFolderName))
            {
                return false;
            }

            var installFolderName = modId.Trim();
            if (string.IsNullOrWhiteSpace(installFolderName))
            {
                return false;
            }

            var extractFullDirectory = manifestCountInDirectory == 1 &&
                (!string.IsNullOrEmpty(directoryPath) || isSpecialManifestFile || totalManifestCount == 1);

            var sourceEntries = extractFullDirectory
                ? GetArchiveDirectoryEntries(archiveEntries, directoryPath)
                : archiveEntries
                    .Where(candidate => IsMatchingModPayload(candidate.NormalizedPath, directoryPath, fileStem))
                    .Select(candidate => candidate.NormalizedPath)
                    .ToArray();

            if (sourceEntries.Length == 0)
            {
                return false;
            }

            installPlan = new ArchiveInstallPlan(
                modId,
                modName,
                modVersion,
                archiveFolderName,
                installFolderName,
                entryPath,
                segments.Length - 1,
                extractFullDirectory && !string.IsNullOrEmpty(directoryPath) ? directoryPath + "/" : string.Empty,
                extractFullDirectory,
                sourceEntries);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractArchiveToTemporaryFolder(string archivePath, ArchiveInstallPlan installPlan)
    {
        var extractionRoot = Path.Combine(Path.GetTempPath(), "sts2-mod-manager", Guid.NewGuid().ToString("N"));
        var destinationRoot = Path.Combine(extractionRoot, installPlan.InstallFolderName);
        Directory.CreateDirectory(destinationRoot);
        var selectedEntries = new HashSet<string>(installPlan.SourceEntries, StringComparer.OrdinalIgnoreCase);

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var entryPath = NormalizeArchivePath(entry.FullName);
            if (string.IsNullOrEmpty(entryPath) || !selectedEntries.Contains(entryPath))
            {
                continue;
            }

            if (installPlan.ExtractFullDirectory)
            {
                if (!string.IsNullOrEmpty(installPlan.RootPrefix))
                {
                    entryPath = entryPath.Substring(installPlan.RootPrefix.Length);
                }

                if (string.IsNullOrEmpty(entryPath))
                {
                    continue;
                }
            }
            else
            {
                entryPath = Path.GetFileName(entryPath);
            }

            if (!TryGetSafeRelativePath(entryPath, out var relativePath))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationRoot, relativePath);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        return extractionRoot;
    }

    private static bool IsManifestCandidatePath(string entryPath)
    {
        var segments = entryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 2)
        {
            return false;
        }

        return segments[^1].EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetArchiveDirectory(string entryPath)
    {
        var lastSeparatorIndex = entryPath.LastIndexOf('/');
        return lastSeparatorIndex < 0 ? string.Empty : entryPath.Substring(0, lastSeparatorIndex);
    }

    private static string[] GetArchiveDirectoryEntries(IReadOnlyList<ArchiveEntryInfo> archiveEntries, string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            return archiveEntries
                .Select(candidate => candidate.NormalizedPath)
                .ToArray();
        }

        return archiveEntries
            .Where(candidate => candidate.NormalizedPath.StartsWith(directoryPath + "/", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.NormalizedPath)
            .ToArray();
    }

    private static string ResolveArchiveFolderName(string directoryName, string fileStem, string modId, bool isSpecialManifestFile)
    {
        if (!string.IsNullOrWhiteSpace(directoryName))
        {
            return directoryName;
        }

        if (isSpecialManifestFile && !string.IsNullOrWhiteSpace(modId))
        {
            return modId.Trim();
        }

        return fileStem;
    }

    private static bool IsMatchingModPayload(string entryPath, string directoryPath, string fileStem)
    {
        if (!string.Equals(GetArchiveDirectory(entryPath), directoryPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(entryPath);
        if (!string.Equals(Path.GetFileNameWithoutExtension(fileName), fileStem, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".pck", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeArchivePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static bool TryGetSafeRelativePath(string archivePath, out string relativePath)
    {
        var segments = archivePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToArray();

        if (segments.Length == 0 || segments.Any(segment => segment == ".."))
        {
            relativePath = string.Empty;
            return false;
        }

        relativePath = Path.Combine(segments);
        return true;
    }

    private static List<ModInfo> LoadMods(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return new List<ModInfo>();
        }

        return Directory
            .EnumerateDirectories(sourceDirectory)
            .Select(path => new DirectoryInfo(path))
            .OrderBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
            .Select(directory => ReadModInfo(directory))
            .ToList();
    }

    private static ModInfo ReadModInfo(DirectoryInfo directory)
    {
        var folderName = directory.Name;
        var modId = folderName;
        var modName = folderName;
        string? modVersion = null;
        var manifestPath = GetManifestPath(directory.FullName, folderName);

        if (!string.IsNullOrEmpty(manifestPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                ReadManifestMetadata(document.RootElement, ref modId, ref modName, ref modVersion);
            }
            catch
            {
            }
        }

        return new ModInfo(modId, modName, modVersion, folderName, directory.FullName);
    }

    private static string? GetManifestPath(string directoryPath, string folderName)
    {
        var defaultManifestPath = Path.Combine(directoryPath, folderName + ".json");
        if (File.Exists(defaultManifestPath))
        {
            return defaultManifestPath;
        }

        var specialManifestPath = Path.Combine(directoryPath, ModManifestFileName);
        return File.Exists(specialManifestPath)
            ? specialManifestPath
            : null;
    }

    private static void ReadManifestMetadata(JsonElement manifestRoot, ref string modId, ref string modName, ref string? modVersion)
    {
        if (manifestRoot.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            modId = idElement.GetString() ?? modId;
        }

        if (manifestRoot.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
        {
            modName = nameElement.GetString() ?? modName;
        }

        if (manifestRoot.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.String)
        {
            modVersion = versionElement.GetString();
        }
    }

    private string FormatVersionText(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ? text.UnknownVersionLabel : version.Trim();
    }

    private string DescribeVersionComparison(ModInfo incomingMod, IReadOnlyList<ModInfo> existingMods)
    {
        if (string.IsNullOrWhiteSpace(incomingMod.Version) || existingMods.Count == 0)
        {
            return string.Empty;
        }

        var comparisonLines = new List<string>();
        foreach (var existingMod in existingMods)
        {
            var comparison = CompareVersionStrings(incomingMod.Version, existingMod.Version);
            if (comparison > 0)
            {
                comparisonLines.Add(text.IncomingVersionNewerMessage(FormatVersionText(incomingMod.Version), FormatVersionText(existingMod.Version)));
            }
            else if (comparison < 0)
            {
                comparisonLines.Add(text.IncomingVersionOlderMessage(FormatVersionText(incomingMod.Version), FormatVersionText(existingMod.Version)));
            }
            else if (!string.IsNullOrWhiteSpace(existingMod.Version))
            {
                comparisonLines.Add(text.IncomingVersionSameMessage(FormatVersionText(existingMod.Version)));
            }
        }

        return string.Join(Environment.NewLine, comparisonLines.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static int CompareVersionStrings(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(left))
        {
            return -1;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return 1;
        }

        if (Version.TryParse(NormalizeVersionString(left), out var leftVersion) &&
            Version.TryParse(NormalizeVersionString(right), out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.Trim(), right.Trim());
    }

    private static string NormalizeVersionString(string version)
    {
        var cleaned = version.Trim();
        return cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? cleaned.Substring(1)
            : cleaned;
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

[SupportedOSPlatform("windows")]
internal sealed class ConfigPage : UserControl
{
    private readonly UiText text;
    private readonly Func<string> autoDetectGameDirectory;
    private readonly Action<ModManagerConfig> applyConfiguration;
    private readonly Action<string> setStatus;
    private readonly TextBox gamePathTextBox;
    private readonly TextBox disabledFolderTextBox;
    private readonly ComboBox languageComboBox;
    private readonly ComboBox launchModeComboBox;
    private readonly ComboBox forceSteamComboBox;
    private readonly CheckBox autoslayCheckBox;
    private readonly TextBox seedTextBox;
    private readonly TextBox logFileTextBox;
    private readonly CheckBox bootstrapCheckBox;
    private readonly ComboBox fastMpComboBox;
    private readonly TextBox clientIdTextBox;
    private readonly CheckBox noModsCheckBox;
    private readonly TextBox connectLobbyTextBox;
    private readonly TextBox extraLaunchArgumentsTextBox;
    private readonly CheckBox splitModListCheckBox;

    public ConfigPage(
        UiText text,
        ModManagerConfig currentConfig,
        string resolvedGameDirectory,
        Func<string> autoDetectGameDirectory,
        Action<ModManagerConfig> applyConfiguration,
        Action<string> setStatus)
    {
        this.text = text;
        this.autoDetectGameDirectory = autoDetectGameDirectory;
        this.applyConfiguration = applyConfiguration;
        this.setStatus = setStatus;

        AutoScroll = true;
        Dock = DockStyle.Fill;

        var gamePathGroup = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Text = text.GamePathGroupTitle
        };
        var currentGamePathLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Text = text.CurrentGamePathLabel(resolvedGameDirectory),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var gamePathLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.GamePathLabel
        };
        gamePathTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = currentConfig.GamePath ?? string.Empty
        };
        var browseButton = new Button { AutoSize = true, Text = text.BrowseButton };
        var autoDetectButton = new Button { AutoSize = true, Text = text.AutoDetectButton };
        var clearButton = new Button { AutoSize = true, Text = text.ClearButton };
        var gamePathHintBox = new TextBox
        {
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            TabStop = false,
            Text = text.GamePathHint
        };

        browseButton.Click += (_, _) => BrowseForGamePath();
        autoDetectButton.Click += (_, _) => AutoDetectGamePath();
        clearButton.Click += (_, _) => gamePathTextBox.Text = string.Empty;

        var gamePathButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        gamePathButtons.Controls.Add(browseButton);
        gamePathButtons.Controls.Add(autoDetectButton);
        gamePathButtons.Controls.Add(clearButton);

        var gamePathLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Padding = new Padding(12),
            RowCount = 4
        };
        gamePathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gamePathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        gamePathLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gamePathLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gamePathLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gamePathLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gamePathLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gamePathLayout.Controls.Add(currentGamePathLabel, 0, 0);
        gamePathLayout.SetColumnSpan(currentGamePathLabel, 3);
        gamePathLayout.Controls.Add(gamePathLabel, 0, 1);
        gamePathLayout.Controls.Add(gamePathTextBox, 1, 1);
        gamePathLayout.Controls.Add(gamePathButtons, 2, 1);
        gamePathLayout.Controls.Add(gamePathHintBox, 1, 2);
        gamePathLayout.SetColumnSpan(gamePathHintBox, 2);
        gamePathGroup.Controls.Add(gamePathLayout);

        var generalGroup = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Text = text.GeneralSettingsGroupTitle
        };
        var disabledFolderLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.DisabledFolderNameLabel
        };
        disabledFolderTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = currentConfig.DisabledDirectoryName
        };
        var disabledFolderHintLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = text.DisabledFolderHint
        };
        var languageLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.InterfaceLanguageLabel
        };
        var languageOptions = new[]
        {
            new LanguageOption(AppLanguage.English, "English"),
            new LanguageOption(AppLanguage.ChineseSimplified, "简体中文")
        };
        languageComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };
        languageComboBox.Items.AddRange(languageOptions.Cast<object>().ToArray());
        languageComboBox.SelectedItem = languageOptions.First(option => option.Language == currentConfig.Language);

        splitModListCheckBox = new CheckBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.SplitModListLabel,
            Checked = currentConfig.SplitModList
        };

        var generalLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Padding = new Padding(12),
            RowCount = 5
        };
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.Controls.Add(disabledFolderLabel, 0, 0);
        generalLayout.Controls.Add(disabledFolderTextBox, 1, 0);
        generalLayout.Controls.Add(disabledFolderHintLabel, 1, 1);
        generalLayout.Controls.Add(languageLabel, 0, 2);
        generalLayout.Controls.Add(languageComboBox, 1, 2);
        generalLayout.Controls.Add(splitModListCheckBox, 0, 3);
        generalLayout.SetColumnSpan(splitModListCheckBox, 2);
        generalGroup.Controls.Add(generalLayout);

        var launchGroup = new GroupBox
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Text = text.LaunchSettingsGroupTitle
        };
        var launchModeLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.LaunchModeLabel
        };
        var launchOptions = new[]
        {
            new LaunchModeOption(LaunchMode.Steam, text.LaunchViaSteamOption),
            new LaunchModeOption(LaunchMode.Direct, text.LaunchDirectOption)
        };
        launchModeComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };
        launchModeComboBox.Items.AddRange(launchOptions.Cast<object>().ToArray());
        launchModeComboBox.SelectedItem = launchOptions.First(option => option.LaunchMode == currentConfig.LaunchMode);

        var parsedLaunchArguments = ParseLaunchArguments(currentConfig.LaunchArguments);
        var lastSteamForceSelectionIndex = parsedLaunchArguments.ForceSteam switch
        {
            true => 1,
            false => 0,
            _ => 0
        };

        var forceSteamLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.ForceSteamLabel
        };
        forceSteamComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };
        forceSteamComboBox.Items.AddRange(new object[]
        {
            text.LaunchArgumentUnsetOption,
            text.ForceSteamOnOption,
            text.ForceSteamOffOption
        });
        forceSteamComboBox.SelectedIndex = parsedLaunchArguments.ForceSteam switch
        {
            true => 1,
            false => 2,
            _ => 0
        };
        launchModeComboBox.SelectedIndexChanged += (_, _) =>
        {
            var selectedMode = ((LaunchModeOption)launchModeComboBox.SelectedItem!).LaunchMode;
            if (selectedMode == LaunchMode.Direct)
            {
                if (forceSteamComboBox.SelectedIndex != 2)
                {
                    lastSteamForceSelectionIndex = forceSteamComboBox.SelectedIndex;
                }

                forceSteamComboBox.SelectedIndex = 2;
                forceSteamComboBox.Enabled = false;
                return;
            }

            forceSteamComboBox.Enabled = true;
            forceSteamComboBox.SelectedIndex = lastSteamForceSelectionIndex;
        };
        forceSteamComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (forceSteamComboBox.Enabled && forceSteamComboBox.SelectedIndex != 2)
            {
                lastSteamForceSelectionIndex = forceSteamComboBox.SelectedIndex;
            }
        };

        if (((LaunchModeOption)launchModeComboBox.SelectedItem!).LaunchMode == LaunchMode.Direct)
        {
            forceSteamComboBox.SelectedIndex = 2;
            forceSteamComboBox.Enabled = false;
        }

        autoslayCheckBox = new CheckBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.AutoslayLabel,
            Checked = parsedLaunchArguments.AutoSlay
        };

        var seedLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.SeedLabel
        };
        seedTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = parsedLaunchArguments.Seed ?? string.Empty
        };

        var logFileLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.LogFileLabel
        };
        logFileTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = parsedLaunchArguments.LogFilePath ?? string.Empty
        };

        bootstrapCheckBox = new CheckBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.BootstrapLabel,
            Checked = parsedLaunchArguments.Bootstrap
        };

        var fastMpLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.FastMpLabel
        };
        fastMpComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };
        fastMpComboBox.Items.AddRange(new object[]
        {
            text.LaunchArgumentUnsetOption,
            "host",
            "host_standard",
            "host_daily",
            "host_custom",
            "load",
            "join"
        });
        fastMpComboBox.SelectedItem = string.IsNullOrWhiteSpace(parsedLaunchArguments.FastMpMode)
            ? text.LaunchArgumentUnsetOption
            : parsedLaunchArguments.FastMpMode;

        var clientIdLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.ClientIdLabel
        };
        clientIdTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = parsedLaunchArguments.ClientId ?? string.Empty
        };

        noModsCheckBox = new CheckBox
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.NoModsLabel,
            Checked = parsedLaunchArguments.NoMods
        };

        var connectLobbyLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.ConnectLobbyLabel
        };
        connectLobbyTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = parsedLaunchArguments.ConnectLobbyId ?? string.Empty
        };

        var extraLaunchArgumentsLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.ExtraLaunchArgumentsLabel
        };
        extraLaunchArgumentsTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = parsedLaunchArguments.ExtraArguments
        };

        var launchHintBox = new TextBox
        {
            BackColor = SystemColors.Control,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            TabStop = false,
            Text = text.LaunchArgumentsHint
        };

        var launchLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            Dock = DockStyle.Top,
            Padding = new Padding(12),
            RowCount = 7
        };
        launchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        launchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        launchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        launchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.Controls.Add(launchModeLabel, 0, 0);
        launchLayout.Controls.Add(launchModeComboBox, 1, 0);
        launchLayout.SetColumnSpan(launchModeComboBox, 3);
        launchLayout.Controls.Add(forceSteamLabel, 0, 1);
        launchLayout.Controls.Add(forceSteamComboBox, 1, 1);
        launchLayout.Controls.Add(seedLabel, 2, 1);
        launchLayout.Controls.Add(seedTextBox, 3, 1);
        launchLayout.Controls.Add(logFileLabel, 0, 2);
        launchLayout.Controls.Add(logFileTextBox, 1, 2);
        launchLayout.Controls.Add(clientIdLabel, 2, 2);
        launchLayout.Controls.Add(clientIdTextBox, 3, 2);
        launchLayout.Controls.Add(fastMpLabel, 0, 3);
        launchLayout.Controls.Add(fastMpComboBox, 1, 3);
        launchLayout.Controls.Add(connectLobbyLabel, 2, 3);
        launchLayout.Controls.Add(connectLobbyTextBox, 3, 3);
        launchLayout.Controls.Add(autoslayCheckBox, 0, 4);
        launchLayout.SetColumnSpan(autoslayCheckBox, 2);
        launchLayout.Controls.Add(bootstrapCheckBox, 2, 4);
        launchLayout.SetColumnSpan(bootstrapCheckBox, 1);
        launchLayout.Controls.Add(noModsCheckBox, 3, 4);
        launchLayout.Controls.Add(extraLaunchArgumentsLabel, 0, 5);
        launchLayout.Controls.Add(extraLaunchArgumentsTextBox, 1, 5);
        launchLayout.SetColumnSpan(extraLaunchArgumentsTextBox, 3);
        launchLayout.Controls.Add(launchHintBox, 0, 6);
        launchLayout.SetColumnSpan(launchHintBox, 4);
        launchGroup.Controls.Add(launchLayout);

        var saveButton = new Button { AutoSize = true, Text = text.SaveButton };
        saveButton.Click += (_, _) =>
        {
            applyConfiguration(new ModManagerConfig(
                NormalizeOptionalText(gamePathTextBox.Text),
                disabledFolderTextBox.Text.Trim(),
                ((LanguageOption)languageComboBox.SelectedItem!).Language,
                ((LaunchModeOption)launchModeComboBox.SelectedItem!).LaunchMode,
                BuildLaunchArguments(),
                splitModListCheckBox.Checked));
        };

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttonPanel.Controls.Add(saveButton);

        var mainLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(12),
            RowCount = 4
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.Controls.Add(gamePathGroup, 0, 0);
        mainLayout.Controls.Add(generalGroup, 0, 1);
        mainLayout.Controls.Add(launchGroup, 0, 2);
        mainLayout.Controls.Add(buttonPanel, 0, 3);

        Controls.Add(mainLayout);
    }

    private void BrowseForGamePath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = text.BrowseGameFolderDescription,
            SelectedPath = Directory.Exists(gamePathTextBox.Text) ? gamePathTextBox.Text : string.Empty,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            gamePathTextBox.Text = dialog.SelectedPath;
            setStatus(text.GamePathBrowsedStatus(dialog.SelectedPath));
        }
    }

    private void AutoDetectGamePath()
    {
        try
        {
            gamePathTextBox.Text = autoDetectGameDirectory();
            setStatus(text.GamePathDetectedStatus(gamePathTextBox.Text));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                text.GameNotFoundMessage(exception.Message),
                text.GameNotFoundTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static string? NormalizeOptionalText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private string BuildLaunchArguments()
    {
        var parts = new List<string>();

        if (forceSteamComboBox.SelectedIndex == 1)
        {
            parts.Add("--force-steam=on");
        }
        else if (forceSteamComboBox.SelectedIndex == 2)
        {
            parts.Add("--force-steam=off");
        }

        if (autoslayCheckBox.Checked)
        {
            parts.Add("--autoslay");
        }

        AppendOption(parts, "--seed", seedTextBox.Text);
        AppendOption(parts, "--log-file", logFileTextBox.Text, quoteValue: true);

        if (bootstrapCheckBox.Checked)
        {
            parts.Add("--bootstrap");
        }

        var fastMpMode = fastMpComboBox.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(fastMpMode) && !string.Equals(fastMpMode, text.LaunchArgumentUnsetOption, StringComparison.Ordinal))
        {
            parts.Add($"--fastmp {fastMpMode}");
        }

        AppendOption(parts, "--clientId", clientIdTextBox.Text);

        if (noModsCheckBox.Checked)
        {
            parts.Add("--nomods");
        }

        AppendOption(parts, "+connect_lobby", connectLobbyTextBox.Text);

        var extraArguments = extraLaunchArgumentsTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(extraArguments))
        {
            parts.Add(extraArguments);
        }

        return string.Join(" ", parts);
    }

    private static void AppendOption(List<string> parts, string optionName, string value, bool quoteValue = false)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (quoteValue && trimmed.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0)
        {
            trimmed = "\"" + trimmed.Replace("\"", "\\\"") + "\"";
        }

        parts.Add($"{optionName} {trimmed}");
    }

    private static ParsedLaunchArguments ParseLaunchArguments(string launchArguments)
    {
        var parsed = new ParsedLaunchArguments();
        var tokens = TokenizeArguments(launchArguments);

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            switch (token)
            {
                case "--autoslay":
                    parsed.AutoSlay = true;
                    break;
                case "--bootstrap":
                    parsed.Bootstrap = true;
                    break;
                case "--nomods":
                    parsed.NoMods = true;
                    break;
                case var _ when token.StartsWith("--force-steam=", StringComparison.OrdinalIgnoreCase):
                    parsed.ForceSteam = token.EndsWith("=on", StringComparison.OrdinalIgnoreCase)
                        ? true
                        : token.EndsWith("=off", StringComparison.OrdinalIgnoreCase)
                            ? false
                            : parsed.ForceSteam;
                    break;
                case "--seed":
                    if (TryTakeValue(tokens, ref index, out var seedValue))
                    {
                        parsed.Seed = seedValue;
                    }
                    break;
                case "--log-file":
                    if (TryTakeValue(tokens, ref index, out var logFileValue))
                    {
                        parsed.LogFilePath = logFileValue;
                    }
                    break;
                case "--fastmp":
                    if (TryTakeValue(tokens, ref index, out var fastMpValue))
                    {
                        parsed.FastMpMode = fastMpValue;
                    }
                    else
                    {
                        parsed.FastMpMode = "host";
                    }
                    break;
                case "--clientId":
                    if (TryTakeValue(tokens, ref index, out var clientIdValue))
                    {
                        parsed.ClientId = clientIdValue;
                    }
                    break;
                case "+connect_lobby":
                    if (TryTakeValue(tokens, ref index, out var lobbyValue))
                    {
                        parsed.ConnectLobbyId = lobbyValue;
                    }
                    break;
                default:
                    parsed.ExtraTokens.Add(token);
                    break;
            }
        }

        parsed.ExtraArguments = string.Join(" ", parsed.ExtraTokens);
        return parsed;
    }

    private static bool TryTakeValue(IReadOnlyList<string> tokens, ref int index, out string value)
    {
        if (index + 1 < tokens.Count)
        {
            value = tokens[++index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static List<string> TokenizeArguments(string arguments)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return tokens;
        }

        foreach (Match match in Regex.Matches(arguments, @"""(?:\\.|[^""])*""|\S+"))
        {
            var value = match.Value;
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value.Substring(1, value.Length - 2).Replace("\\\"", "\"");
            }

            tokens.Add(value);
        }

        return tokens;
    }

    private sealed class ParsedLaunchArguments
    {
        public bool? ForceSteam { get; set; }

        public bool AutoSlay { get; set; }

        public string? Seed { get; set; }

        public string? LogFilePath { get; set; }

        public bool Bootstrap { get; set; }

        public string? FastMpMode { get; set; }

        public string? ClientId { get; set; }

        public bool NoMods { get; set; }

        public string? ConnectLobbyId { get; set; }

        public List<string> ExtraTokens { get; } = new();

        public string ExtraArguments { get; set; } = string.Empty;
    }
}

[SupportedOSPlatform("windows")]
internal sealed class SaveManagerPage : UserControl
{
    private readonly UiText text;
    private readonly Action<string> reportStatus;
    private readonly string saveRoot;
    private readonly Label introLabel;
    private readonly Label saveRootLabel;
    private readonly Label steamUserLabel;
    private readonly ComboBox steamUserComboBox;
    private readonly GroupBox vanillaGroup;
    private readonly GroupBox moddedGroup;
    private readonly ListView vanillaList;
    private readonly ListView moddedList;
    private readonly Button transferToModdedButton;
    private readonly Button transferToVanillaButton;
    private readonly Button refreshButton;

    private bool suppressSelectionEvents;

    public SaveManagerPage(UiText text, Action<string> reportStatus)
    {
        this.text = text;
        this.reportStatus = reportStatus;
        saveRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlayTheSpire2", "steam");

        Dock = DockStyle.Fill;

        introLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = text.SaveManagerIntro
        };

        saveRootLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        steamUserLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = text.SteamUserLabel
        };

        steamUserComboBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FormattingEnabled = true
        };

        vanillaGroup = new GroupBox { Dock = DockStyle.Fill, Text = text.VanillaSavesGroup };
        moddedGroup = new GroupBox { Dock = DockStyle.Fill, Text = text.ModdedSavesGroup };
        vanillaList = CreateSaveListView(text);
        moddedList = CreateSaveListView(text);
        transferToModdedButton = new Button { AutoSize = true, Text = text.TransferToModdedButton };
        transferToVanillaButton = new Button { AutoSize = true, Text = text.TransferToVanillaButton };
        refreshButton = new Button { AutoSize = true, Text = text.RefreshButton };

        vanillaGroup.Controls.Add(vanillaList);
        moddedGroup.Controls.Add(moddedList);

        var selectorLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        selectorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        selectorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        selectorLayout.Controls.Add(saveRootLabel, 0, 0);
        selectorLayout.SetColumnSpan(saveRootLabel, 2);
        selectorLayout.Controls.Add(steamUserLabel, 0, 1);
        selectorLayout.Controls.Add(steamUserComboBox, 1, 1);

        var transferButtonsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.None,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(12, 0, 12, 0),
            Padding = new Padding(0, 24, 0, 0),
            WrapContents = false
        };
        transferButtonsPanel.Controls.Add(transferToModdedButton);
        transferButtonsPanel.Controls.Add(transferToVanillaButton);
        transferButtonsPanel.Controls.Add(refreshButton);

        var listsLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            RowCount = 1
        };
        listsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        listsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        listsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        listsLayout.Controls.Add(vanillaGroup, 0, 0);
        listsLayout.Controls.Add(transferButtonsPanel, 1, 0);
        listsLayout.Controls.Add(moddedGroup, 2, 0);

        var mainLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 3
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainLayout.Controls.Add(introLabel, 0, 0);
        mainLayout.Controls.Add(selectorLayout, 0, 1);
        mainLayout.Controls.Add(listsLayout, 0, 2);

        Controls.Add(mainLayout);

        steamUserComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (!suppressSelectionEvents)
            {
                ReloadProfiles(text.SaveProfilesReloadedStatus);
            }
        };
        vanillaList.SelectedIndexChanged += (_, _) => UpdateButtons();
        moddedList.SelectedIndexChanged += (_, _) => UpdateButtons();
        vanillaList.Resize += (_, _) => ResizeSaveListColumns(vanillaList);
        moddedList.Resize += (_, _) => ResizeSaveListColumns(moddedList);
        transferToModdedButton.Click += (_, _) => TransferSelectedSave(SaveKind.Vanilla, SaveKind.Modded);
        transferToVanillaButton.Click += (_, _) => TransferSelectedSave(SaveKind.Modded, SaveKind.Vanilla);
        refreshButton.Click += (_, _) => ReloadSteamUsers(text.SaveProfilesReloadedStatus);

        saveRootLabel.Text = text.SaveRootLabel(saveRoot);
        ResizeSaveListColumns(vanillaList);
        ResizeSaveListColumns(moddedList);
        ReloadSteamUsers(text.ReadySaveManagerStatus);
    }

    public void RefreshData(string statusText)
    {
        ReloadSteamUsers(statusText);
    }

    private static ListView CreateSaveListView(UiText text)
    {
        var listView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = false,
            ShowItemToolTips = true,
            View = View.Details
        };

        listView.Columns.Add(text.SaveSlotColumn, 110);
        listView.Columns.Add(text.SaveStateColumn, 100);
        listView.Columns.Add(text.SaveModifiedColumn, 170);
        listView.Columns.Add(text.SaveNotesColumn, 260);
        return listView;
    }

    private void ReloadSteamUsers(string statusText)
    {
        var previousSteamId = steamUserComboBox.SelectedItem as string;
        var previousVanillaSlot = GetSelectedProfileId(vanillaList);
        var previousModdedSlot = GetSelectedProfileId(moddedList);
        var steamIds = GetSteamIds(saveRoot);

        suppressSelectionEvents = true;
        steamUserComboBox.BeginUpdate();
        steamUserComboBox.Items.Clear();
        foreach (var steamId in steamIds)
        {
            steamUserComboBox.Items.Add(steamId);
        }

        if (steamIds.Count > 0)
        {
            steamUserComboBox.Enabled = true;
            var targetSteamId = !string.IsNullOrEmpty(previousSteamId) && steamIds.Contains(previousSteamId, StringComparer.OrdinalIgnoreCase)
                ? previousSteamId
                : steamIds[0];
            steamUserComboBox.SelectedItem = targetSteamId;
        }
        else
        {
            steamUserComboBox.Enabled = false;
            steamUserComboBox.SelectedIndex = -1;
        }

        steamUserComboBox.EndUpdate();
        suppressSelectionEvents = false;

        if (steamIds.Count == 0)
        {
            PopulateSaveList(vanillaList, Array.Empty<SaveProfileInfo>());
            PopulateSaveList(moddedList, Array.Empty<SaveProfileInfo>());
            UpdateButtons();

            SetStatus(Directory.Exists(saveRoot)
                ? text.SaveUsersNotFoundStatus
                : text.SaveRootMissingStatus(saveRoot));
            return;
        }

        ReloadProfiles(statusText, previousVanillaSlot, previousModdedSlot);
    }

    private void ReloadProfiles(string statusText, int? vanillaProfileId = null, int? moddedProfileId = null)
    {
        var steamId = steamUserComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(steamId))
        {
            PopulateSaveList(vanillaList, Array.Empty<SaveProfileInfo>());
            PopulateSaveList(moddedList, Array.Empty<SaveProfileInfo>());
            UpdateButtons();
            SetStatus(text.SelectSteamUserStatus);
            return;
        }

        var vanillaProfiles = Enumerable.Range(1, 3)
            .Select(profileId => ReadSaveProfileInfo(saveRoot, steamId, SaveKind.Vanilla, profileId))
            .ToList();
        var moddedProfiles = Enumerable.Range(1, 3)
            .Select(profileId => ReadSaveProfileInfo(saveRoot, steamId, SaveKind.Modded, profileId))
            .ToList();

        PopulateSaveList(vanillaList, vanillaProfiles);
        PopulateSaveList(moddedList, moddedProfiles);
        RestoreSaveSelection(vanillaList, vanillaProfileId);
        RestoreSaveSelection(moddedList, moddedProfileId);
        ResizeSaveListColumns(vanillaList);
        ResizeSaveListColumns(moddedList);
        UpdateButtons();
        SetStatus(statusText);
    }

    private void PopulateSaveList(ListView listView, IReadOnlyList<SaveProfileInfo> profiles)
    {
        listView.BeginUpdate();
        listView.Items.Clear();

        foreach (var profile in profiles)
        {
            var item = new ListViewItem(text.SaveSlotLabel(profile.ProfileId));
            item.SubItems.Add(profile.HasData ? text.SavePresentState : text.SaveEmptyState);
            item.SubItems.Add(profile.LastModified?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? text.SaveNoTimestamp);
            item.SubItems.Add(FormatSaveNotes(profile));
            item.Tag = profile;
            item.ToolTipText = text.SaveSlotTooltip(
                FormatSaveProfileLabel(profile),
                profile.DirectoryPath,
                profile.FileCount,
                profile.HasCurrentRun);

            if (!profile.HasData)
            {
                item.ForeColor = SystemColors.GrayText;
            }

            listView.Items.Add(item);
        }

        listView.EndUpdate();
    }

    private void ResizeSaveListColumns(ListView listView)
    {
        if (listView.Columns.Count != 4 || listView.ClientSize.Width <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(380, listView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        listView.Columns[0].Width = Math.Max(90, (int)(availableWidth * 0.16f));
        listView.Columns[1].Width = Math.Max(90, (int)(availableWidth * 0.18f));
        listView.Columns[2].Width = Math.Max(150, (int)(availableWidth * 0.28f));
        listView.Columns[3].Width = Math.Max(150, availableWidth - listView.Columns[0].Width - listView.Columns[1].Width - listView.Columns[2].Width);
    }

    private void UpdateButtons()
    {
        var hasVanillaSelection = vanillaList.SelectedItems.Count > 0;
        var hasModdedSelection = moddedList.SelectedItems.Count > 0;
        transferToModdedButton.Enabled = hasVanillaSelection && hasModdedSelection;
        transferToVanillaButton.Enabled = hasVanillaSelection && hasModdedSelection;
    }

    private void SetStatus(string message)
    {
        reportStatus(message);
    }

    private void TransferSelectedSave(SaveKind sourceKind, SaveKind targetKind)
    {
        var sourceList = sourceKind == SaveKind.Vanilla ? vanillaList : moddedList;
        var targetList = targetKind == SaveKind.Vanilla ? vanillaList : moddedList;
        var sourceProfile = GetSelectedProfile(sourceList);
        var targetProfile = GetSelectedProfile(targetList);
        if (sourceProfile is null || targetProfile is null)
        {
            SetStatus(text.SaveSelectionRequiredStatus);
            MessageBox.Show(
                text.SaveSelectionRequiredMessage,
                text.SaveTransferTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (!sourceProfile.HasData)
        {
            SetStatus(text.SaveSourceEmptyStatus(FormatSaveProfileLabel(sourceProfile)));
            MessageBox.Show(
                text.SaveSourceEmptyMessage(FormatSaveProfileLabel(sourceProfile)),
                text.SaveTransferTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var promptAccepted = MessageBox.Show(
            text.SaveTransferPrompt(FormatSaveProfileLabel(sourceProfile), FormatSaveProfileLabel(targetProfile)),
            text.SaveTransferTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes;

        if (!promptAccepted)
        {
            return;
        }

        try
        {
            string? backupPath = null;
            if (targetProfile.HasData)
            {
                backupPath = BackupSaveProfile(targetProfile);
            }

            ReplaceDirectoryContents(sourceProfile.DirectoryPath, targetProfile.DirectoryPath);
            ReloadProfiles(
                text.SaveTransferCompletedStatus(FormatSaveProfileLabel(sourceProfile), FormatSaveProfileLabel(targetProfile)),
                sourceKind == SaveKind.Vanilla ? sourceProfile.ProfileId : targetProfile.ProfileId,
                sourceKind == SaveKind.Modded ? sourceProfile.ProfileId : targetProfile.ProfileId);

            MessageBox.Show(
                text.SaveTransferCompletedMessage(
                    FormatSaveProfileLabel(sourceProfile),
                    FormatSaveProfileLabel(targetProfile),
                    backupPath),
                text.SaveTransferTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            SetStatus(text.SaveTransferFailedStatus(exception.Message));
            MessageBox.Show(
                exception.Message,
                text.SaveTransferErrorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private string? BackupSaveProfile(SaveProfileInfo profile)
    {
        if (!Directory.Exists(profile.DirectoryPath))
        {
            return null;
        }

        var typeCode = profile.Kind == SaveKind.Vanilla ? "normal" : "modded";
        var backupRoot = Path.Combine(saveRoot, profile.SteamId, "backups");
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupRoot, $"{typeCode}_p{profile.ProfileId}_auto_before_transfer_{timestamp}");

        Directory.CreateDirectory(backupPath);
        CopyDirectoryContents(profile.DirectoryPath, backupPath);
        CleanupOldBackups(backupRoot, typeCode, 20);
        return backupPath;
    }

    private static void CleanupOldBackups(string backupRoot, string typeCode, int keepCount)
    {
        if (!Directory.Exists(backupRoot))
        {
            return;
        }

        var backups = Directory
            .EnumerateDirectories(backupRoot)
            .Where(path => Path.GetFileName(path).StartsWith(typeCode + "_", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Skip(keepCount)
            .ToList();

        foreach (var backupPath in backups)
        {
            TryDeleteBackupDirectory(backupPath);
        }
    }

    private static void TryDeleteBackupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static void ReplaceDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source save directory does not exist: {sourceDirectory}");
        }

        Directory.CreateDirectory(destinationDirectory);
        ClearDirectoryContents(destinationDirectory);
        CopyDirectoryContents(sourceDirectory, destinationDirectory);
    }

    private static void ClearDirectoryContents(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(directoryPath))
        {
            File.Delete(filePath);
        }

        foreach (var childDirectoryPath in Directory.EnumerateDirectories(directoryPath))
        {
            Directory.Delete(childDirectoryPath, recursive: true);
        }
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            var targetFilePath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, targetFilePath, overwrite: true);
        }

        foreach (var childDirectoryPath in Directory.EnumerateDirectories(sourceDirectory))
        {
            var childDirectoryName = Path.GetFileName(childDirectoryPath);
            var targetChildDirectoryPath = Path.Combine(destinationDirectory, childDirectoryName);
            CopyDirectoryContents(childDirectoryPath, targetChildDirectoryPath);
        }
    }

    private static List<string> GetSteamIds(string saveRoot)
    {
        if (!Directory.Exists(saveRoot))
        {
            return new List<string>();
        }

        return Directory
            .EnumerateDirectories(saveRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private static SaveProfileInfo ReadSaveProfileInfo(string saveRoot, string steamId, SaveKind kind, int profileId)
    {
        var directoryPath = kind == SaveKind.Vanilla
            ? Path.Combine(saveRoot, steamId, $"profile{profileId}", "saves")
            : Path.Combine(saveRoot, steamId, "modded", $"profile{profileId}", "saves");
        var progressSavePath = Path.Combine(directoryPath, "progress.save");
        var currentRunPath = Path.Combine(directoryPath, "current_run.save");
        var hasData = File.Exists(progressSavePath);
        var fileCount = Directory.Exists(directoryPath)
            ? Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).Count()
            : 0;

        return new SaveProfileInfo(
            steamId,
            kind,
            profileId,
            directoryPath,
            hasData,
            hasData ? File.GetLastWriteTime(progressSavePath) : null,
            File.Exists(currentRunPath),
            fileCount);
    }

    private string FormatSaveNotes(SaveProfileInfo profile)
    {
        if (!profile.HasData)
        {
            return text.SaveNoNotes;
        }

        var notes = new List<string> { text.SaveFileCountNote(profile.FileCount) };
        if (profile.HasCurrentRun)
        {
            notes.Add(text.SaveCurrentRunNote);
        }

        return string.Join(", ", notes);
    }

    private string FormatSaveProfileLabel(SaveProfileInfo profile)
    {
        var kindLabel = profile.Kind == SaveKind.Vanilla ? text.VanillaSaveLabel : text.ModdedSaveLabel;
        return text.SaveProfileLabel(kindLabel, profile.ProfileId);
    }

    private static SaveProfileInfo? GetSelectedProfile(ListView listView)
    {
        return listView.SelectedItems.Count == 0
            ? null
            : listView.SelectedItems[0].Tag as SaveProfileInfo;
    }

    private static int? GetSelectedProfileId(ListView listView)
    {
        return GetSelectedProfile(listView)?.ProfileId;
    }

    private static void RestoreSaveSelection(ListView listView, int? profileId)
    {
        if (!profileId.HasValue)
        {
            return;
        }

        foreach (ListViewItem item in listView.Items)
        {
            if (item.Tag is SaveProfileInfo profile && profile.ProfileId == profileId.Value)
            {
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
                break;
            }
        }
    }
}

internal sealed record ModInfo(string Id, string Name, string? Version, string FolderName, string FullPath);

internal enum SaveKind
{
    Vanilla,
    Modded
}

internal sealed record SaveProfileInfo(
    string SteamId,
    SaveKind Kind,
    int ProfileId,
    string DirectoryPath,
    bool HasData,
    DateTime? LastModified,
    bool HasCurrentRun,
    int FileCount);

internal sealed record ArchiveInstallPlan(
    string Id,
    string Name,
    string? Version,
    string ArchiveFolderName,
    string InstallFolderName,
    string EntryPath,
    int ManifestDepth,
    string RootPrefix,
    bool ExtractFullDirectory,
    IReadOnlyList<string> SourceEntries);

internal sealed record ArchiveInstallStepResult(OperationResult Result, bool StopProcessingArchive);

internal sealed record ArchiveEntryInfo(ZipArchiveEntry Entry, string NormalizedPath);

internal sealed record ModManagerConfig(
    string? GamePath,
    string DisabledDirectoryName,
    AppLanguage Language,
    LaunchMode LaunchMode,
    string LaunchArguments,
    bool SplitModList = true);

internal sealed record AppSettings(
    string DisabledDirectoryName,
    string? LanguageCode,
    string? GamePath,
    LaunchMode LaunchMode,
    string? LaunchArguments,
    bool SplitModList = true,
    bool CheckForUpdates = true,
    string? SkippedUpdateVersion = null,
    DateTime? UpdateRemindAfterUtc = null)
{
    public static AppSettings Default { get; } = new(".mods", null, null, LaunchMode.Steam, null);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class ModManagerJsonContext : JsonSerializerContext
{
}

internal sealed record OperationResult(bool RefreshRequired, string Message);

internal sealed record ModMoveResult(ModMoveOutcome Outcome, string Message);

internal sealed record LatestReleaseInfo(string Version, string ReleasePageUrl);

internal enum ModMoveOutcome
{
    Changed,
    Unchanged,
    Failed
}

internal enum UpdatePromptChoice
{
    UpdateNow,
    RemindLater,
    SkipThisVersion,
    NeverCheck
}

internal sealed record LanguageOption(AppLanguage Language, string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

internal sealed record LaunchModeOption(LaunchMode LaunchMode, string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

internal enum ConflictChoice
{
    KeepIncoming,
    KeepExisting,
    Cancel
}

internal enum AppLanguage
{
    English,
    ChineseSimplified
}

internal enum LaunchMode
{
    Steam,
    Direct
}

internal enum AppPage
{
    Mods,
    Saves,
    Config
}

internal static class Localization
{
    public static bool IsSupported(string? languageCode)
    {
        return !string.IsNullOrWhiteSpace(languageCode) &&
            (string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(languageCode, "chs", StringComparison.OrdinalIgnoreCase));
    }

    public static AppLanguage ParseOrDefault(string? languageCode)
    {
        if (string.Equals(languageCode, "chs", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.ChineseSimplified;
        }

        if (string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.English;
        }

        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.ChineseSimplified
            : AppLanguage.English;
    }

    public static string ToCode(AppLanguage language)
    {
        return language == AppLanguage.ChineseSimplified ? "chs" : "en";
    }
}

internal sealed class UiText
{
    private readonly bool isChinese;

    public UiText(AppLanguage language)
    {
        isChinese = language == AppLanguage.ChineseSimplified;
    }

    public string AppTitle => isChinese ? "杀戮尖塔2 模组管理器" : "Slay the Spire 2 Mod Manager";
    public string TitleText => isChinese ? "管理已启用和已禁用的模组，或将 zip 压缩包拖进窗口以安装为禁用状态" : "Manage enabled and disabled mods, or drop a zip archive to install it as disabled";
    public string DisableSelectedButton => isChinese ? "禁用所选 ->" : "Disable selected ->";
    public string DisableAllButton => isChinese ? "全部禁用" : "Disable all";
    public string EnableSelectedButton => isChinese ? "<- 启用所选" : "<- Enable selected";
    public string EnableAllButton => isChinese ? "全部启用" : "Enable all";
    public string ExportButton => isChinese ? "导出..." : "Export...";
    public string DisableModButton => isChinese ? "禁用" : "Disable";
    public string EnableModButton => isChinese ? "启用" : "Enable";
    public string NoModsFoundLabel => isChinese ? "未找到任何模组" : "No mods found";
    public string SplitModListLabel => isChinese ? "将已启用和已禁用的模组分开显示" : "Show enabled and disabled mods in separate sections";

    public string AllModsGroup(int count)
    {
        return isChinese ? $"全部模组 ({count})" : $"All mods ({count})";
    }
    public string ModsPageButton => isChinese ? "模组" : "Mods";
    public string SavesPageButton => isChinese ? "存档" : "Saves";
    public string ConfigPageButton => isChinese ? "配置" : "Config";
    public string OpenFolderButton => isChinese ? "打开文件夹" : "Open Folder";
    public string RefreshButton => isChinese ? "刷新" : "Refresh";
    public string RestartGameButton => isChinese ? "重启游戏" : "Restart Game";
    public string SavesButton => isChinese ? "存档..." : "Saves...";
    public string ConfigButton => isChinese ? "配置..." : "Config...";
    public string IdColumn => "ID";
    public string NameColumn => isChinese ? "名称" : "Name";
    public string VersionColumn => isChinese ? "版本" : "Version";
    public string FolderColumn => isChinese ? "文件夹" : "Folder";
    public string ArchiveImportTitle => isChinese ? "导入压缩包" : "Archive Import";
    public string ExportArchiveDialogTitle => isChinese ? "导出模组压缩包" : "Export Mods Archive";
    public string ExportArchiveFilter => isChinese ? "Zip 压缩包 (*.zip)|*.zip" : "Zip archive (*.zip)|*.zip";
    public string ReloadedModListStatus => isChinese ? "已重新加载模组列表。" : "Reloaded mod list.";
    public string GameNotFoundTitle => isChinese ? "未找到游戏" : "Game Not Found";
    public string LoadErrorTitle => isChinese ? "加载错误" : "Load Error";
    public string SelectedModNotResolvedStatus => isChinese ? "无法解析当前选中的模组。" : "The selected mod could not be resolved.";
    public string MoveSkippedTitle => isChinese ? "已跳过移动" : "Move Skipped";
    public string MoveErrorTitle => isChinese ? "移动错误" : "Move Error";
    public string ExportFailedTitle => isChinese ? "导出失败" : "Export Failed";
    public string OpenFolderErrorTitle => isChinese ? "打开文件夹失败" : "Open Folder Failed";
    public string NoFilesProvidedStatus => isChinese ? "未提供任何文件。" : "No files were provided.";
    public string DisabledFolderMatchesModsMessage => isChinese ? "禁用模组目录不能与启用模组目录相同。" : "The disabled mods folder cannot be the same as the enabled mods folder.";
    public string InvalidDirectoryTitle => isChinese ? "无效目录" : "Invalid Directory";
    public string UpdateFailedTitle => isChinese ? "更新失败" : "Update Failed";
    public string UpdateAvailableTitle => isChinese ? "发现新版本" : "Update Available";
    public string UpdateNowButton => isChinese ? "立即更新" : "Update Now";
    public string RemindLaterButton => isChinese ? "稍后提醒" : "Remind Later";
    public string SkipThisVersionButton => isChinese ? "跳过此版本" : "Skip This Version";
    public string NeverCheckButton => isChinese ? "不再检查" : "Never Check";
    public string ConfigurationDialogTitle => isChinese ? "配置" : "Configuration";
    public string ConfigurationUpdatedStatus => isChinese ? "配置已更新。" : "Configuration updated.";
    public string GamePathGroupTitle => isChinese ? "游戏路径" : "Game Path";
    public string GeneralSettingsGroupTitle => isChinese ? "常规设置" : "General";
    public string LaunchSettingsGroupTitle => isChinese ? "启动设置" : "Launch";
    public string GamePathLabel => isChinese ? "手动路径:" : "Manual path:";
    public string GamePathHint => isChinese ? "留空表示自动检测。可填写游戏根目录，或直接填写 SlayTheSpire2.exe 的路径。" : "Leave blank to auto-detect. You can enter the game root or the full path to SlayTheSpire2.exe.";
    public string CurrentGamePathLabel(string path)
    {
        return isChinese ? $"当前使用: {path}" : $"Currently using: {path}";
    }

    public string BrowseButton => isChinese ? "浏览..." : "Browse...";
    public string AutoDetectButton => isChinese ? "自动检测" : "Auto detect";
    public string ClearButton => isChinese ? "清空" : "Clear";
    public string BrowseGameFolderDescription => isChinese ? "选择包含 SlayTheSpire2.exe 的文件夹" : "Select the folder that contains SlayTheSpire2.exe";
    public string GamePathBrowsedStatus(string path)
    {
        return isChinese ? $"已选择游戏路径: {path}" : $"Selected game path: {path}";
    }

    public string GamePathDetectedStatus(string path)
    {
        return isChinese ? $"已自动检测到游戏路径: {path}" : $"Auto-detected game path: {path}";
    }
    public string DisabledFolderDialogTitle => isChinese ? "禁用模组目录" : "Disabled Mods Folder";
    public string DisabledFolderPrompt => isChinese ? "选择游戏根目录下用于存放已禁用模组的文件夹名称。" : "Choose the folder name under the game root used to store disabled mods.";
    public string DisabledFolderHint => isChinese ? "示例: .mods, disabled-mods, archived-mods" : "Examples: .mods, disabled-mods, archived-mods";
    public string DisabledFolderNameLabel => isChinese ? "禁用目录名:" : "Disabled folder name:";
    public string InterfaceLanguageLabel => isChinese ? "界面语言:" : "Interface language:";
    public string SaveButton => isChinese ? "保存" : "Save";
    public string CancelButton => isChinese ? "取消" : "Cancel";
    public string InvalidDirectoryNameTitle => isChinese ? "无效目录名" : "Invalid Directory Name";
    public string EmptyFolderNameMessage => isChinese ? "文件夹名称不能为空。" : "The folder name cannot be empty.";
    public string InvalidFolderCharactersMessage => isChinese ? "文件夹名称包含无效字符。" : "The folder name contains invalid characters.";
    public string SingleFolderNameMessage => isChinese ? "请输入单个文件夹名称，不要填写路径。" : "Use a single folder name, not a path.";
    public string DotFolderNameMessage => isChinese ? "文件夹名称不能是 '.' 或 '..'。" : "The folder name cannot be '.' or '..'.";
    public string LaunchModeLabel => isChinese ? "启动方式:" : "Launch mode:";
    public string ForceSteamLabel => isChinese ? "Steam 集成:" : "Steam integration:";
    public string AutoslayLabel => isChinese ? "自动开始新游戏 (--autoslay)" : "Auto-start new run (--autoslay)";
    public string SeedLabel => isChinese ? "Seed:" : "Seed:";
    public string LogFileLabel => isChinese ? "日志文件:" : "Log file:";
    public string BootstrapLabel => isChinese ? "引导模式 (--bootstrap)" : "Bootstrap mode (--bootstrap)";
    public string FastMpLabel => isChinese ? "快速多人模式:" : "Fast multiplayer:";
    public string ClientIdLabel => isChinese ? "客户端 ID:" : "Client ID:";
    public string NoModsLabel => isChinese ? "禁用所有模组 (--nomods)" : "Disable all mods (--nomods)";
    public string ConnectLobbyLabel => isChinese ? "加入大厅 ID:" : "Connect lobby ID:";
    public string ExtraLaunchArgumentsLabel => isChinese ? "额外参数:" : "Extra arguments:";
    public string LaunchArgumentUnsetOption => isChinese ? "未设置" : "Not set";
    public string ForceSteamOnOption => isChinese ? "强制启用 (--force-steam=on)" : "Force on (--force-steam=on)";
    public string ForceSteamOffOption => isChinese ? "强制禁用 (--force-steam=off)" : "Force off (--force-steam=off)";
    public string LaunchViaSteamOption => isChinese ? "通过 Steam 启动" : "Launch via Steam";
    public string LaunchDirectOption => isChinese ? "直接启动游戏" : "Launch game directly";
    public string LaunchArgumentsHint => isChinese
        ? "常用参数已经拆成上面的控件。未覆盖的内容可以继续写到“额外参数”。\r\n\r\n直接启动通常需要设置 --force-steam=off。\r\n多人模式中 host 的 clientId 必须为 1，客户端建议使用 1000-1002。"
        : "Common flags are available above. Use Extra arguments for anything not covered there.\r\n\r\nDirect launch usually needs --force-steam=off.\r\nFor fastmp host, clientId must be 1. Clients should use 1000-1002.";
    public string DuplicateModIdTitle => isChinese ? "重复的模组 ID" : "Duplicate Mod Id";
    public string IncomingVersionLabel => isChinese ? "导入版本:" : "Incoming version:";
    public string ExistingVersionsLabel => isChinese ? "现有版本:" : "Existing version(s):";
    public string KeepIncomingHelpText => isChinese ? "保留导入版本会删除现有版本。" : "Keep Incoming removes the existing version(s).";
    public string KeepExistingHelpText => isChinese ? "保留现有版本会丢弃导入版本。" : "Keep Existing discards the incoming version.";
    public string KeepIncomingButton => isChinese ? "保留导入版本" : "Keep Incoming";
    public string KeepExistingButton => isChinese ? "保留现有版本" : "Keep Existing";
    public string InvalidArchiveManifestMessage => isChinese ? "zip 中未找到有效模组清单，路径需匹配 *.json 或 */*.json。" : "zip does not contain a valid mod manifest at *.json or */*.json.";
    public string UnsupportedZipArchiveMessage => isChinese ? "该文件不是受支持的 zip 压缩包。" : "file is not a supported zip archive.";
    public string UnknownVersionLabel => isChinese ? "未知" : "unknown";
    public string MoveExistingDisabledModsTitle => isChinese ? "移动现有已禁用模组" : "Move Existing Disabled Mods";
    public string LanguageDialogTitle => isChinese ? "界面语言" : "Interface Language";
    public string LanguagePrompt => isChinese ? "选择要使用的界面语言。" : "Choose the interface language to use.";
    public string LanguageUpdatedStatus => isChinese ? "界面语言已更新。" : "Updated interface language.";
    public string SavesDialogTitle => isChinese ? "存档管理" : "Save Manager";
    public string SaveManagerIntro => isChinese ? "原版存档与模组存档是分开的。选择左侧或右侧来源槽位，再选择目标槽位进行复制。目标槽位在覆盖前会自动备份。" : "Vanilla and modded saves are stored separately. Select a source slot and a target slot, then copy between them. The destination slot is backed up automatically before overwrite.";
    public string SteamUserLabel => isChinese ? "Steam 账号:" : "Steam User:";
    public string VanillaSavesGroup => isChinese ? "原版存档" : "Vanilla Saves";
    public string ModdedSavesGroup => isChinese ? "模组存档" : "Modded Saves";
    public string TransferToModdedButton => isChinese ? "原版 -> 模组" : "Vanilla -> Modded";
    public string TransferToVanillaButton => isChinese ? "原版 <- 模组" : "Vanilla <- Modded";
    public string CloseButton => isChinese ? "关闭" : "Close";
    public string SaveSlotColumn => isChinese ? "槽位" : "Slot";
    public string SaveStateColumn => isChinese ? "状态" : "State";
    public string SaveModifiedColumn => isChinese ? "最后修改" : "Last Modified";
    public string SaveNotesColumn => isChinese ? "备注" : "Notes";
    public string SavePresentState => isChinese ? "有存档" : "Present";
    public string SaveEmptyState => isChinese ? "空" : "Empty";
    public string SaveNoTimestamp => isChinese ? "-" : "-";
    public string SaveNoNotes => isChinese ? "无" : "none";
    public string SaveCurrentRunNote => isChinese ? "有进行中的一局" : "current run in progress";
    public string ReadySaveManagerStatus => isChinese ? "存档管理器已就绪。" : "Save manager is ready.";
    public string SaveProfilesReloadedStatus => isChinese ? "已重新加载存档状态。" : "Reloaded save status.";
    public string SaveUsersNotFoundStatus => isChinese ? "未找到任何 Steam 存档。请先运行一次游戏。" : "No Steam save data was found. Run the game once first.";
    public string SelectSteamUserStatus => isChinese ? "请选择一个 Steam 账号。" : "Select a Steam user.";
    public string SaveSelectionRequiredStatus => isChinese ? "请选择来源槽位和目标槽位。" : "Select a source slot and a target slot.";
    public string SaveSelectionRequiredMessage => isChinese ? "请先选择来源槽位和目标槽位。" : "Select a source slot and a target slot first.";
    public string SaveTransferTitle => isChinese ? "复制存档" : "Transfer Save";
    public string SaveTransferErrorTitle => isChinese ? "复制失败" : "Transfer Failed";
    public string VanillaSaveLabel => isChinese ? "原版" : "Vanilla";
    public string ModdedSaveLabel => isChinese ? "模组" : "Modded";
    public string GameRestartErrorTitle => isChinese ? "重启失败" : "Restart Failed";
    public string SteamNotFoundMessage => isChinese ? "未找到 steam.exe。请确认 Steam 已安装。" : "Could not find steam.exe. Make sure Steam is installed.";
    public string GameRestartedStatus => isChinese ? "已强制关闭游戏并按当前配置重新启动。" : "Force-stopped the game and relaunched it with the configured launcher.";
    public string BulkMoveTitle => isChinese ? "批量切换模组" : "Bulk Toggle Mods";

    public string GameNotFoundMessage(string details)
    {
        return isChinese
            ? $"未能在父级目录中找到 SlayTheSpire2.exe。{Environment.NewLine}{Environment.NewLine}{details}"
            : details;
    }

    public string ReadyStatus(string disabledDirectoryName)
    {
        return isChinese
            ? $"就绪。将 zip 压缩包拖到窗口任意位置即可安装到 {disabledDirectoryName}。"
            : $"Ready. Drop a zip archive anywhere in the window to install it into {disabledDirectoryName}.";
    }

    public string UpdateAvailableMessage(string currentVersion, string latestVersion)
    {
        return isChinese
            ? $"当前版本: {currentVersion}{Environment.NewLine}最新版本: {latestVersion}{Environment.NewLine}{Environment.NewLine}是否打开 GitHub 发布页面下载更新?"
            : $"Current version: {currentVersion}{Environment.NewLine}Latest version: {latestVersion}{Environment.NewLine}{Environment.NewLine}Open the GitHub releases page to download the update?";
    }

    public string UpdateCheckUnavailableStatus => isChinese
        ? "无法连接 GitHub 检查更新。在某些地区这可能是网络限制导致的；稍后可手动查看发布页。"
        : "Could not reach GitHub to check for updates. In some regions this can fail because of network restrictions; you can check the releases page manually later.";

    public string UpdatePageOpenedStatus(string version)
    {
        return isChinese ? $"已打开版本 {version} 的发布页。" : $"Opened the release page for version {version}.";
    }

    public string UpdateOpenFailedStatus(string message)
    {
        return isChinese ? $"打开发布页失败: {message}" : $"Failed to open the release page: {message}";
    }

    public string OpenReleasePageFailedMessage(string message)
    {
        return isChinese ? $"无法打开 GitHub 发布页。{Environment.NewLine}{Environment.NewLine}{message}" : $"Could not open the GitHub releases page.{Environment.NewLine}{Environment.NewLine}{message}";
    }

    public string UpdateReminderScheduledStatus(string version)
    {
        return isChinese ? $"稍后会再次提醒版本 {version}。" : $"Will remind you about version {version} later.";
    }

    public string UpdateSkippedStatus(string version)
    {
        return isChinese ? $"将跳过版本 {version} 的提醒。" : $"Will skip update reminders for version {version}.";
    }

    public string UpdateChecksDisabledStatus => isChinese ? "已关闭自动更新检查。" : "Automatic update checks are disabled.";

    public string EnabledGroup(int count)
    {
        return isChinese ? $"已启用 ({count})" : $"Enabled ({count})";
    }

    public string DisabledGroup(string disabledDirectoryName, int count)
    {
        return isChinese ? $"已禁用于 {disabledDirectoryName} ({count})" : $"Disabled in {disabledDirectoryName} ({count})";
    }

    public string LoadFailedStatus(string message)
    {
        return isChinese ? $"加载模组失败: {message}" : $"Failed to load mods: {message}";
    }

    public string SelectModFolderStatus => isChinese ? "请选择一个模组以打开其文件夹。" : "Select a mod to open its folder.";
    public string SelectSingleModFolderStatus => isChinese ? "一次只能打开一个模组文件夹。" : "Select exactly one mod to open its folder.";
    public string SelectModsExportStatus => isChinese ? "请选择一个或多个模组以导出。" : "Select one or more mods to export.";
    public string ExportCanceledStatus => isChinese ? "已取消导出模组。" : "Canceled mod export.";

    public string GameRestartFailedStatus(string message)
    {
        return isChinese ? $"重启游戏失败: {message}" : $"Failed to restart the game: {message}";
    }

    public string GameExecutableMissingMessage(string path)
    {
        return isChinese ? $"未找到游戏可执行文件: {path}" : $"Game executable was not found: {path}";
    }

    public string BulkMovePrompt(string operationVerb, int count)
    {
        if (isChinese)
        {
            var action = operationVerb == "enable" ? "启用" : "禁用";
            return $"要{action}全部 {count} 个模组吗?";
        }

        var actionText = operationVerb == "enable" ? "enable" : "disable";
        return $"{char.ToUpperInvariant(actionText[0]) + actionText.Substring(1)} all {count} mod(s)?";
    }

    public string BulkMoveCanceledStatus(string operationVerb)
    {
        if (isChinese)
        {
            return operationVerb == "enable" ? "已取消全部启用。" : "已取消全部禁用。";
        }

        return operationVerb == "enable" ? "Canceled enable all." : "Canceled disable all.";
    }

    public string BulkMoveCompletedStatus(string operationVerb, int changedCount, int unchangedCount, int failedCount)
    {
        if (isChinese)
        {
            var action = operationVerb == "enable" ? "启用" : "禁用";
            return $"批量{action}完成: 已处理 {changedCount}，未变更 {unchangedCount}，失败 {failedCount}。";
        }

        var actionText = operationVerb == "enable" ? "Enable all" : "Disable all";
        return $"{actionText} complete: changed {changedCount}, unchanged {unchangedCount}, failed {failedCount}.";
    }

    public string OperationCanceledStatus(string operationVerb, string modId)
    {
        var action = isChinese
            ? operationVerb switch
            {
                "enable" => "启用",
                "disable" => "禁用",
                _ => operationVerb
            }
            : operationVerb;

        return isChinese ? $"已取消{action} {modId}。" : $"Canceled {action} for {modId}.";
    }

    public string KeptExistingStatus(string modId)
    {
        return isChinese ? $"已保留 {modId} 的现有版本。" : $"Kept existing version of {modId}.";
    }

    public string TargetFolderAlreadyExistsMessage(string folderName)
    {
        return isChinese
            ? $"目标目录中已存在文件夹 {folderName}，且其模组 ID 不同。"
            : $"{folderName} already exists in the target folder with a different mod id.";
    }

    public string MoveSkippedStatus(string modId)
    {
        return isChinese ? $"已跳过 {modId}: 目标文件夹已存在。" : $"Skipped {modId}: target folder already exists.";
    }

    public string MoveCompletedStatus(string operationVerb, string modId, string modName)
    {
        return isChinese
            ? $"{(operationVerb == "enable" ? "已启用" : operationVerb == "disable" ? "已禁用" : operationVerb)} {modId} - {modName}"
            : $"{(operationVerb == "enable" ? "Enabled" : operationVerb == "disable" ? "Disabled" : operationVerb)} {modId} - {modName}";
    }

    public string MoveFailedStatus(string modId, string message)
    {
        return isChinese ? $"移动 {modId} 失败: {message}" : $"Failed to move {modId}: {message}";
    }

    public string ModFolderMissingMessage(string path)
    {
        return isChinese ? $"未找到模组文件夹: {path}" : $"Mod folder was not found: {path}";
    }

    public string OpenFolderOpenedStatus(string modId)
    {
        return isChinese ? $"已打开 {modId} 的文件夹。" : $"Opened folder for {modId}.";
    }

    public string OpenFolderFailedStatus(string message)
    {
        return isChinese ? $"打开文件夹失败: {message}" : $"Failed to open folder: {message}";
    }

    public string ExportCompletedStatus(int count, string path)
    {
        return isChinese
            ? $"已将 {count} 个模组导出到 {path}。"
            : $"Exported {count} mod(s) to {path}.";
    }

    public string ExportCompletedMessage(int count, string path)
    {
        return isChinese
            ? $"已将 {count} 个模组导出到:{Environment.NewLine}{path}"
            : $"Exported {count} mod(s) to:{Environment.NewLine}{path}";
    }

    public string ExportFailedStatus(string message)
    {
        return isChinese ? $"导出模组失败: {message}" : $"Failed to export mods: {message}";
    }

    public string ModTooltip(string id, string name, string version, string folderName)
    {
        return isChinese
            ? $"ID: {id}{Environment.NewLine}名称: {name}{Environment.NewLine}版本: {version}{Environment.NewLine}文件夹: {folderName}"
            : $"ID: {id}{Environment.NewLine}Name: {name}{Environment.NewLine}Version: {version}{Environment.NewLine}Folder: {folderName}";
    }

    public string GameRootLabel(string gameDirectory)
    {
        return isChinese ? $"游戏根目录: {gameDirectory}" : $"Game root: {gameDirectory}";
    }

    public string DisabledFolderLabel(string disabledDirectoryName, string disabledDirectory)
    {
        return isChinese
            ? $"已禁用模组目录: {disabledDirectoryName} ({disabledDirectory})"
            : $"Disabled mods folder: {disabledDirectoryName} ({disabledDirectory})";
    }

    public string DisabledFolderUpdatedStatus(string disabledDirectoryName)
    {
        return isChinese ? $"已将禁用模组目录更新为 {disabledDirectoryName}。" : $"Disabled mods folder updated to {disabledDirectoryName}.";
    }

    public string ConfigurationUpdateFailedStatus(string message)
    {
        return isChinese ? $"更新配置失败: {message}" : $"Failed to update configuration: {message}";
    }

    public string SaveRootLabel(string saveRoot)
    {
        return isChinese ? $"存档目录: {saveRoot}" : $"Save root: {saveRoot}";
    }

    public string SaveRootMissingStatus(string saveRoot)
    {
        return isChinese ? $"未找到存档目录: {saveRoot}" : $"Save root was not found: {saveRoot}";
    }

    public string SaveSlotLabel(int profileId)
    {
        return isChinese ? $"槽位 {profileId}" : $"Slot {profileId}";
    }

    public string SaveProfileLabel(string kindLabel, int profileId)
    {
        return isChinese ? $"{kindLabel}槽位 {profileId}" : $"{kindLabel} slot {profileId}";
    }

    public string SaveFileCountNote(int fileCount)
    {
        return isChinese ? $"{fileCount} 个文件" : $"{fileCount} files";
    }

    public string SaveSlotTooltip(string slotLabel, string directoryPath, int fileCount, bool hasCurrentRun)
    {
        var currentRunLine = hasCurrentRun
            ? (isChinese ? "有进行中的一局" : "Current run in progress")
            : (isChinese ? "无进行中的一局" : "No current run");

        return isChinese
            ? $"{slotLabel}{Environment.NewLine}路径: {directoryPath}{Environment.NewLine}文件数: {fileCount}{Environment.NewLine}{currentRunLine}"
            : $"{slotLabel}{Environment.NewLine}Path: {directoryPath}{Environment.NewLine}Files: {fileCount}{Environment.NewLine}{currentRunLine}";
    }

    public string SaveSourceEmptyStatus(string sourceLabel)
    {
        return isChinese ? $"{sourceLabel} 为空，无法复制。" : $"{sourceLabel} is empty and cannot be copied.";
    }

    public string SaveSourceEmptyMessage(string sourceLabel)
    {
        return isChinese ? $"{sourceLabel} 没有可复制的存档。" : $"{sourceLabel} does not contain a save to copy.";
    }

    public string SaveTransferPrompt(string sourceLabel, string targetLabel)
    {
        return isChinese
            ? $"要将 {sourceLabel} 复制到 {targetLabel} 吗?{Environment.NewLine}{Environment.NewLine}如果目标槽位已有存档，会先自动备份再覆盖。"
            : $"Copy {sourceLabel} to {targetLabel}?{Environment.NewLine}{Environment.NewLine}If the destination already has a save, it will be backed up automatically before overwrite.";
    }

    public string SaveTransferCompletedStatus(string sourceLabel, string targetLabel)
    {
        return isChinese ? $"已将 {sourceLabel} 复制到 {targetLabel}。" : $"Copied {sourceLabel} to {targetLabel}.";
    }

    public string SaveTransferCompletedMessage(string sourceLabel, string targetLabel, string? backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return isChinese
                ? $"已将 {sourceLabel} 复制到 {targetLabel}。"
                : $"Copied {sourceLabel} to {targetLabel}.";
        }

        return isChinese
            ? $"已将 {sourceLabel} 复制到 {targetLabel}。{Environment.NewLine}{Environment.NewLine}目标槽位原有存档已备份到:{Environment.NewLine}{backupPath}"
            : $"Copied {sourceLabel} to {targetLabel}.{Environment.NewLine}{Environment.NewLine}The previous destination save was backed up to:{Environment.NewLine}{backupPath}";
    }

    public string SaveTransferFailedStatus(string message)
    {
        return isChinese ? $"复制存档失败: {message}" : $"Failed to transfer save: {message}";
    }

    public string MoveExistingDisabledModsPrompt(int count, string oldName, string newName)
    {
        return isChinese
            ? $"要将 {count} 个现有已禁用模组从 {oldName} 移动到 {newName} 吗?"
            : $"Move {count} existing disabled mod(s) from {oldName} to {newName}?";
    }

    public string CannotMoveExistingDisabledModMessage(string folderName, string newDirectoryName)
    {
        return isChinese
            ? $"无法移动 {folderName}，因为它已存在于 {newDirectoryName} 中。"
            : $"Cannot move {folderName} because it already exists in {newDirectoryName}.";
    }

    public string DuplicateModIdMessage(string modId)
    {
        return isChinese ? $"已存在 ID 为 '{modId}' 的模组。" : $"A mod with id '{modId}' already exists.";
    }

    public string IncomingVersionLine(string name, string version, string folderName, string source)
    {
        return isChinese ? $"- {name} v{version} [{folderName}] 来源: {source}" : $"- {name} v{version} [{folderName}] from {source}";
    }

    public string ExistingVersionLine(string name, string version, string folderName, string path)
    {
        return isChinese ? $"- {name} v{version} [{folderName}] 位置: {path}" : $"- {name} v{version} [{folderName}] at {path}";
    }

    public string ArchiveInstallCanceled(string archiveFileName)
    {
        return isChinese ? $"{archiveFileName}: 已取消安装。" : $"{archiveFileName}: install canceled.";
    }

    public string ArchiveKeptExisting(string archiveFileName, string modId)
    {
        return isChinese ? $"{archiveFileName}: 已保留 {modId} 的现有版本。" : $"{archiveFileName}: kept existing version of {modId}.";
    }

    public string ArchiveTargetFolderExists(string archiveFileName, string folderName)
    {
        return isChinese
            ? $"{archiveFileName}: 目标文件夹 {folderName} 已存在，且其模组 ID 不同。"
            : $"{archiveFileName}: target folder {folderName} already exists with a different mod id.";
    }

    public string ArchiveInstalled(
        string archiveFileName,
        string modId,
        string disabledDirectoryName,
        string archiveFolderName,
        string installFolderName)
    {
        var renameHint = string.Equals(archiveFolderName, installFolderName, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : isChinese
                ? $" 已将文件夹名从 {archiveFolderName} 改为 {installFolderName} 以匹配模组 ID。"
                : $" Renamed folder from {archiveFolderName} to {installFolderName} to match the mod id.";

        return isChinese
            ? $"{archiveFileName}: 已将 {modId} 安装到 {disabledDirectoryName}。{renameHint}"
            : $"{archiveFileName}: installed {modId} to {disabledDirectoryName}.{renameHint}";
    }

    public string ArchiveInstallFailed(string archiveFileName, string message)
    {
        return isChinese ? $"{archiveFileName}: 安装失败: {message}" : $"{archiveFileName}: install failed: {message}";
    }

    public string IncomingVersionNewerMessage(string incomingVersion, string existingVersion)
    {
        return isChinese
            ? $"导入版本 {incomingVersion} 新于现有版本 {existingVersion}。"
            : $"Incoming version {incomingVersion} is newer than existing version {existingVersion}.";
    }

    public string IncomingVersionOlderMessage(string incomingVersion, string existingVersion)
    {
        return isChinese
            ? $"导入版本 {incomingVersion} 旧于现有版本 {existingVersion}。"
            : $"Incoming version {incomingVersion} is older than existing version {existingVersion}.";
    }

    public string IncomingVersionSameMessage(string existingVersion)
    {
        return isChinese
            ? $"导入版本与现有版本 {existingVersion} 相同。"
            : $"Incoming version matches existing version {existingVersion}.";
    }
}