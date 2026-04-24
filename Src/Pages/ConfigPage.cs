using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using STS2ModManager.App;
using STS2ModManager.Widgets;

[SupportedOSPlatform("windows")]
internal sealed class ConfigPage : UserControl
{
    /// <summary>Suppress mouse-wheel value changes on a ComboBox so users don't accidentally change the selection while scrolling the page.</summary>
    private static void DisableWheelChange(ComboBox combo)
    {
        combo.MouseWheel += (s, e) =>
        {
            if (e is HandledMouseEventArgs h)
            {
                h.Handled = true;
            }
        };
    }

    private readonly LocalizationService loc;
    private readonly Func<string> autoDetectGameDirectory;
    private readonly Action<ModManagerConfig> applyConfiguration;
    private readonly Action<string> setStatus;
    private readonly Label currentVersionValueLabel;
    private readonly Label latestVersionValueLabel;
    private readonly MaterialTextBox2 gamePathTextBox;
    private readonly MaterialTextBox2 disabledFolderTextBox;
    private readonly MaterialComboBox languageComboBox;
    private readonly MaterialComboBox launchModeComboBox;
    private readonly MaterialComboBox forceSteamComboBox;
    private readonly MaterialSwitch autoslayCheckBox;
    private readonly MaterialTextBox2 seedTextBox;
    private readonly MaterialTextBox2 logFileTextBox;
    private readonly MaterialSwitch bootstrapCheckBox;
    private readonly MaterialComboBox fastMpComboBox;
    private readonly MaterialTextBox2 clientIdTextBox;
    private readonly MaterialSwitch noModsCheckBox;
    private readonly MaterialTextBox2 connectLobbyTextBox;
    private readonly TextBox extraLaunchArgumentsTextBox;
    private readonly MaterialSwitch splitModListCheckBox;
    private readonly MaterialComboBox themeModeComboBox;
    private readonly MaterialTextBox2 backupRetentionTextBox;

    public ConfigPage(
        LocalizationService loc,
        ModManagerConfig currentConfig,
        string resolvedGameDirectory,
        string currentVersion,
        string? latestVersion,
        Func<string> autoDetectGameDirectory,
        Action<ModManagerConfig> applyConfiguration,
        Action<string> setStatus)
    {
        this.loc = loc;
        this.autoDetectGameDirectory = autoDetectGameDirectory;
        this.applyConfiguration = applyConfiguration;
        this.setStatus = setStatus;

        AutoScroll = true;
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        UpdateStyles();

        var skin = MaterialSkinManager.Instance;
        var dark = skin.Theme == MaterialSkinManager.Themes.DARK;
        // Match ModsPage cardBack so the two tabs feel consistent.
        Color cardBack = dark ? Color.FromArgb(48, 48, 48) : Color.White;

        // Reflection-enable DoubleBuffered on layout panels (protected property).
        static void EnableDB(Control c)
        {
            var prop = typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(c, true);
        }

        // ── Helper: build a MaterialCard with a title header + body ───────────
        static MaterialCard MakeCard(string title, Control body, Color back)
        {
            var card = new MaterialCard
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(16, 12, 16, 16),
                BackColor = back,
            };
            var titleLabel = new MaterialLabel
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                FontType = MaterialSkinManager.fontType.H6,
                Margin = new Padding(0, 0, 0, 8),
                Text = title,
            };
            body.Dock = DockStyle.Top;
            body.BackColor = back;
            // Add body first so when both are docked-top, title sits on top (last-added = highest z-order means rendered on top, but Dock.Top stacks by reverse order of addition).
            card.Controls.Add(body);
            card.Controls.Add(titleLabel);
            return card;
        }

        // ── Game Path card ────────────────────────────────────────────────────
        var currentGamePathLabel = new Label
        {
            AutoEllipsis = true,
            AutoSize = true,
            Dock = DockStyle.Top,
            Text = loc.Get("config.current_game_path_label", resolvedGameDirectory),
            Margin = new Padding(0, 0, 0, 6),
        };
        gamePathTextBox = new MaterialTextBox2
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("game.game_path_label"),
            Text = currentConfig.GamePath ?? string.Empty,
        };
        var browseButton = new MaterialButton
        {
            AutoSize = true,
            Type = MaterialButton.MaterialButtonType.Outlined,
            UseAccentColor = false,
            HighEmphasis = false,
            Text = loc.Get("config.browse_button"),
            Margin = new Padding(0, 8, 6, 0),
        };
        var autoDetectButton = new MaterialButton
        {
            AutoSize = true,
            Type = MaterialButton.MaterialButtonType.Outlined,
            UseAccentColor = false,
            HighEmphasis = false,
            Text = loc.Get("config.auto_detect_button"),
            Margin = new Padding(0, 8, 6, 0),
        };
        var clearButton = new MaterialButton
        {
            AutoSize = true,
            Type = MaterialButton.MaterialButtonType.Outlined,
            UseAccentColor = false,
            HighEmphasis = false,
            Text = loc.Get("config.clear_button"),
            Margin = new Padding(0, 8, 0, 0),
        };

        browseButton.Click += (_, _) => BrowseForGamePath();
        autoDetectButton.Click += (_, _) => AutoDetectGamePath();
        clearButton.Click += (_, _) => gamePathTextBox.Text = string.Empty;

        var gamePathButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            BackColor = cardBack,
        };
        gamePathButtons.Controls.Add(browseButton);
        gamePathButtons.Controls.Add(autoDetectButton);
        gamePathButtons.Controls.Add(clearButton);

        var gamePathHintLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = skin.TextMediumEmphasisColor,
            Margin = new Padding(0, 8, 0, 0),
            Text = loc.Get("game.game_path_hint"),
        };

        var gamePathRow = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            RowCount = 1,
            BackColor = cardBack,
        };
        gamePathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        gamePathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gamePathRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gamePathRow.Controls.Add(gamePathTextBox, 0, 0);
        gamePathRow.Controls.Add(gamePathButtons, 1, 0);

        var gamePathBody = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            RowCount = 3,
            BackColor = cardBack,
        };
        gamePathBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        gamePathBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gamePathBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gamePathBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gamePathBody.Controls.Add(currentGamePathLabel, 0, 0);
        gamePathBody.Controls.Add(gamePathRow, 0, 1);
        gamePathBody.Controls.Add(gamePathHintLabel, 0, 2);
        var gamePathGroup = MakeCard(loc.Get("game.game_path_group_title"), gamePathBody, cardBack);

        // ── General Settings card ────────────────────────────────────────────
        disabledFolderTextBox = new MaterialTextBox2
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("config.disabled_folder_name_label"),
            Text = currentConfig.DisabledDirectoryName,
        };
        var disabledFolderHintLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = skin.TextMediumEmphasisColor,
            Margin = new Padding(0, 4, 0, 0),
            Text = loc.Get("config.disabled_folder_hint"),
        };
        var languageOptions = new[]
        {
            new LanguageOption(AppLanguage.English, "English"),
            new LanguageOption(AppLanguage.ChineseSimplified, "简体中文")
        };
        languageComboBox = new MaterialComboBox
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("ui.interface_language_label"),
        };
        languageComboBox.Items.AddRange(languageOptions.Cast<object>().ToArray());
        languageComboBox.SelectedItem = languageOptions.First(option => option.Language == currentConfig.Language);
        DisableWheelChange(languageComboBox);

        splitModListCheckBox = new MaterialSwitch
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = loc.Get("ui.split_mod_list_label"),
            Checked = currentConfig.SplitModList,
            BackColor = cardBack,
        };

        var themeOptions = new[]
        {
            new ThemeModeOption(ThemeMode.System, loc.Get("config.theme_mode_system_option")),
            new ThemeModeOption(ThemeMode.Light, loc.Get("config.theme_mode_light_option")),
            new ThemeModeOption(ThemeMode.Dark, loc.Get("config.theme_mode_dark_option")),
        };
        themeModeComboBox = new MaterialComboBox
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("config.theme_mode_label"),
        };
        themeModeComboBox.Items.AddRange(themeOptions.Cast<object>().ToArray());
        themeModeComboBox.SelectedItem = themeOptions.First(o => o.Mode == currentConfig.ThemeMode);
        DisableWheelChange(themeModeComboBox);

        backupRetentionTextBox = new MaterialTextBox2
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("config.backup_retention_label"),
            Text = currentConfig.BackupRetentionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var currentVersionLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = loc.Get("config.current_version_label")
        };
        currentVersionValueLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = FormatVersionValue(currentVersion)
        };
        var latestVersionLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = loc.Get("config.latest_version_label")
        };
        latestVersionValueLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = FormatVersionValue(latestVersion)
        };

        var generalLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            RowCount = 8,
            BackColor = cardBack,
        };
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int r = 0; r < 8; r++) generalLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        generalLayout.Controls.Add(disabledFolderTextBox, 0, 0);
        generalLayout.SetColumnSpan(disabledFolderTextBox, 2);
        generalLayout.Controls.Add(disabledFolderHintLabel, 0, 1);
        generalLayout.SetColumnSpan(disabledFolderHintLabel, 2);
        generalLayout.Controls.Add(languageComboBox, 0, 2);
        generalLayout.SetColumnSpan(languageComboBox, 2);
        generalLayout.Controls.Add(splitModListCheckBox, 0, 3);
        generalLayout.SetColumnSpan(splitModListCheckBox, 2);
        generalLayout.Controls.Add(themeModeComboBox, 0, 4);
        generalLayout.SetColumnSpan(themeModeComboBox, 2);
        generalLayout.Controls.Add(backupRetentionTextBox, 0, 5);
        generalLayout.SetColumnSpan(backupRetentionTextBox, 2);
        generalLayout.Controls.Add(currentVersionLabel, 0, 6);
        generalLayout.Controls.Add(currentVersionValueLabel, 1, 6);
        generalLayout.Controls.Add(latestVersionLabel, 0, 7);
        generalLayout.Controls.Add(latestVersionValueLabel, 1, 7);
        var generalGroup = MakeCard(loc.Get("config.general_settings_group_title"), generalLayout, cardBack);

        // ── Launch Settings card ──────────────────────────────────────────────
        var launchOptions = new[]
        {
            new LaunchModeOption(LaunchMode.Steam, loc.Get("launch.launch_via_steam_option")),
            new LaunchModeOption(LaunchMode.Direct, loc.Get("launch.launch_direct_option"))
        };
        launchModeComboBox = new MaterialComboBox
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("launch.launch_mode_label"),
        };
        launchModeComboBox.Items.AddRange(launchOptions.Cast<object>().ToArray());
        launchModeComboBox.SelectedItem = launchOptions.First(option => option.LaunchMode == currentConfig.LaunchMode);
        DisableWheelChange(launchModeComboBox);

        var parsedLaunchArguments = ParseLaunchArguments(currentConfig.LaunchArguments);
        var lastSteamForceSelectionIndex = parsedLaunchArguments.ForceSteam switch
        {
            true => 1,
            false => 0,
            _ => 0
        };

        forceSteamComboBox = new MaterialComboBox
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("launch.force_steam_label"),
        };
        forceSteamComboBox.Items.AddRange(new object[]
        {
            loc.Get("launch.launch_argument_unset_option"),
            loc.Get("launch.force_steam_on_option"),
            loc.Get("launch.force_steam_off_option")
        });
        forceSteamComboBox.SelectedIndex = parsedLaunchArguments.ForceSteam switch
        {
            true => 1,
            false => 2,
            _ => 0
        };
        DisableWheelChange(forceSteamComboBox);
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

        autoslayCheckBox = new MaterialSwitch
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = loc.Get("launch.autoslay_label"),
            Checked = parsedLaunchArguments.AutoSlay,
            BackColor = cardBack,
        };
        seedTextBox = new MaterialTextBox2
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("launch.seed_label"),
            Text = parsedLaunchArguments.Seed ?? string.Empty,
        };
        logFileTextBox = new MaterialTextBox2
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("launch.log_file_label"),
            Text = parsedLaunchArguments.LogFilePath ?? string.Empty,
        };
        bootstrapCheckBox = new MaterialSwitch
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = loc.Get("launch.bootstrap_label"),
            Checked = parsedLaunchArguments.Bootstrap,
            BackColor = cardBack,
        };
        fastMpComboBox = new MaterialComboBox
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("launch.fast_mp_label"),
        };
        fastMpComboBox.Items.AddRange(new object[]
        {
            loc.Get("launch.launch_argument_unset_option"),
            "host",
            "host_standard",
            "host_daily",
            "host_custom",
            "load",
            "join"
        });
        fastMpComboBox.SelectedItem = string.IsNullOrWhiteSpace(parsedLaunchArguments.FastMpMode)
            ? loc.Get("launch.launch_argument_unset_option")
            : parsedLaunchArguments.FastMpMode;
        DisableWheelChange(fastMpComboBox);
        clientIdTextBox = new MaterialTextBox2
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("launch.client_id_label"),
            Text = parsedLaunchArguments.ClientId ?? string.Empty,
        };
        noModsCheckBox = new MaterialSwitch
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = loc.Get("launch.no_mods_label"),
            Checked = parsedLaunchArguments.NoMods,
            BackColor = cardBack,
        };
        connectLobbyTextBox = new MaterialTextBox2
        {
            Dock = DockStyle.Fill,
            Hint = loc.Get("launch.connect_lobby_label"),
            Text = parsedLaunchArguments.ConnectLobbyId ?? string.Empty,
        };

        var extraLaunchArgumentsLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = loc.Get("launch.extra_launch_arguments_label"),
            Margin = new Padding(0, 8, 0, 4),
        };
        extraLaunchArgumentsTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 64,
            Text = parsedLaunchArguments.ExtraArguments,
        };
        var launchHintLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = skin.TextMediumEmphasisColor,
            Margin = new Padding(0, 8, 0, 0),
            Text = loc.Get("launch.launch_arguments_hint"),
        };

        var launchLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Top,
            RowCount = 8,
            BackColor = cardBack,
        };
        launchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        launchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        for (int r = 0; r < 8; r++) launchLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchLayout.Controls.Add(launchModeComboBox, 0, 0);
        launchLayout.Controls.Add(forceSteamComboBox, 1, 0);
        launchLayout.Controls.Add(seedTextBox, 0, 1);
        launchLayout.Controls.Add(clientIdTextBox, 1, 1);
        launchLayout.Controls.Add(logFileTextBox, 0, 2);
        launchLayout.Controls.Add(connectLobbyTextBox, 1, 2);
        launchLayout.Controls.Add(fastMpComboBox, 0, 3);
        launchLayout.Controls.Add(autoslayCheckBox, 0, 4);
        launchLayout.Controls.Add(bootstrapCheckBox, 1, 4);
        launchLayout.Controls.Add(noModsCheckBox, 0, 5);
        launchLayout.Controls.Add(extraLaunchArgumentsLabel, 0, 6);
        launchLayout.SetColumnSpan(extraLaunchArgumentsLabel, 2);
        launchLayout.Controls.Add(extraLaunchArgumentsTextBox, 0, 7);
        launchLayout.SetColumnSpan(extraLaunchArgumentsTextBox, 2);

        var launchBody = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            RowCount = 2,
            BackColor = cardBack,
        };
        launchBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        launchBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        launchBody.Controls.Add(launchLayout, 0, 0);
        launchBody.Controls.Add(launchHintLabel, 0, 1);
        var launchGroup = MakeCard(loc.Get("launch.launch_settings_group_title"), launchBody, cardBack);

        // ── Footer (Save) ────────────────────────────────────────────────────
        var saveButton = new MaterialButton
        {
            AutoSize = true,
            Type = MaterialButton.MaterialButtonType.Contained,
            UseAccentColor = true,
            HighEmphasis = true,
            Text = loc.Get("common.save_button"),
        };
        saveButton.Click += (_, _) =>
        {
            var retention = 5;
            if (int.TryParse(backupRetentionTextBox.Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                retention = Math.Clamp(parsed, 0, 100);
            }
            applyConfiguration(new ModManagerConfig(
                NormalizeOptionalText(gamePathTextBox.Text),
                disabledFolderTextBox.Text.Trim(),
                ((LanguageOption)languageComboBox.SelectedItem!).Language,
                ((LaunchModeOption)launchModeComboBox.SelectedItem!).LaunchMode,
                BuildLaunchArguments(),
                splitModListCheckBox.Checked,
                ((ThemeModeOption)themeModeComboBox.SelectedItem!).Mode,
                retention));
        };

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        buttonPanel.Controls.Add(saveButton);

        var mainLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(12),
            RowCount = 4
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.Controls.Add(gamePathGroup, 0, 0);
        mainLayout.Controls.Add(generalGroup, 0, 1);
        mainLayout.Controls.Add(launchGroup, 0, 2);
        mainLayout.Controls.Add(buttonPanel, 0, 3);

        // Reduce flicker during scroll: enable double buffering on all layout panels.
        EnableDB(mainLayout);
        EnableDB(gamePathBody);
        EnableDB(gamePathRow);
        EnableDB(gamePathButtons);
        EnableDB(generalLayout);
        EnableDB(launchBody);
        EnableDB(launchLayout);
        EnableDB(buttonPanel);

        Controls.Add(mainLayout);
        ThinScrollBarHost.Attach(this, mainLayout, manageContentWidth: true);
    }

    public void UpdateVersionInfo(string currentVersion, string? latestVersion)
    {
        currentVersionValueLabel.Text = FormatVersionValue(currentVersion);
        latestVersionValueLabel.Text = FormatVersionValue(latestVersion);
    }

    private string FormatVersionValue(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ? loc.Get("ui.unknown_version_label") : version.Trim();
    }

    private void BrowseForGamePath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = loc.Get("config.browse_game_folder_description"),
            SelectedPath = Directory.Exists(gamePathTextBox.Text) ? gamePathTextBox.Text : string.Empty,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            gamePathTextBox.Text = dialog.SelectedPath;
            setStatus(loc.Get("game.game_path_browsed_status", dialog.SelectedPath));
        }
    }

    private void AutoDetectGamePath()
    {
        try
        {
            gamePathTextBox.Text = autoDetectGameDirectory();
            setStatus(loc.Get("game.game_path_detected_status", gamePathTextBox.Text));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                LocalizedFormats.GameNotFoundMessage(loc, exception.Message),
                loc.Get("game.game_not_found_title"),
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
        if (!string.IsNullOrWhiteSpace(fastMpMode) && !string.Equals(fastMpMode, loc.Get("launch.launch_argument_unset_option"), StringComparison.Ordinal))
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
