using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
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

    private readonly string gameDirectory;
    private readonly string modsDirectory;
    private readonly string settingsFilePath;

    private string disabledDirectoryName;
    private string disabledDirectory;
    private AppLanguage language;
    private UiText text;

    private readonly GroupBox enabledGroup;
    private readonly GroupBox disabledGroup;
    private readonly ListView enabledList;
    private readonly ListView disabledList;
    private readonly Button disableButton;
    private readonly Button enableButton;
    private readonly Button refreshButton;
    private readonly Button restartButton;
    private readonly Button savesButton;
    private readonly Button settingsButton;
    private readonly Button languageButton;
    private readonly Label disabledFolderLabel;
    private readonly Label rootLabel;
    private readonly Label titleLabel;
    private readonly StatusStrip statusStrip;
    private readonly ToolStripStatusLabel statusLabel;
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
        disabledDirectoryName = settings.DisabledDirectoryName;
        language = Localization.ParseOrDefault(settings.LanguageCode);
        text = new UiText(language);

        try
        {
            gameDirectory = FindGameDirectory(AppContext.BaseDirectory);
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

        modsDirectory = Path.Combine(gameDirectory, "mods");
        disabledDirectory = Path.Combine(gameDirectory, disabledDirectoryName);
        Directory.CreateDirectory(modsDirectory);
        Directory.CreateDirectory(disabledDirectory);

        enabledGroup = new GroupBox { Dock = DockStyle.Fill };
        disabledGroup = new GroupBox { Dock = DockStyle.Fill };
        enabledList = CreateListView();
        disabledList = CreateListView();
        disableButton = new Button { AutoSize = true };
        enableButton = new Button { AutoSize = true };
        refreshButton = new Button { AutoSize = true };
        restartButton = new Button { AutoSize = true };
        savesButton = new Button { AutoSize = true };
        settingsButton = new Button { AutoSize = true };
        languageButton = new Button { AutoSize = true };
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

        SuspendLayout();

        enabledGroup.Controls.Add(enabledList);
        disabledGroup.Controls.Add(disabledList);

        titleLabel = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 12)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.None,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(12, 0, 12, 0),
            Padding = new Padding(0, 24, 0, 0),
            WrapContents = false
        };

        buttonPanel.Controls.Add(disableButton);
        buttonPanel.Controls.Add(enableButton);
        buttonPanel.Controls.Add(refreshButton);
        buttonPanel.Controls.Add(restartButton);
        buttonPanel.Controls.Add(savesButton);
        buttonPanel.Controls.Add(settingsButton);
        buttonPanel.Controls.Add(languageButton);

        var listsLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            RowCount = 1
        };

        listsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
        listsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        listsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
        listsLayout.Controls.Add(enabledGroup, 0, 0);
        listsLayout.Controls.Add(buttonPanel, 1, 0);
        listsLayout.Controls.Add(disabledGroup, 2, 0);

        var bottomPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        bottomPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottomPanel.Controls.Add(rootLabel, 0, 0);
        bottomPanel.Controls.Add(disabledFolderLabel, 0, 1);

        var mainLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 3
        };

        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.Controls.Add(titleLabel, 0, 0);
        mainLayout.Controls.Add(listsLayout, 0, 1);
        mainLayout.Controls.Add(bottomPanel, 0, 2);

        statusStrip.Items.Add(statusLabel);

        Controls.Add(mainLayout);
        Controls.Add(statusStrip);

        disableButton.Click += (_, _) => MoveSelectedMod(enabledList, disabledDirectory, "disable");
        enableButton.Click += (_, _) => MoveSelectedMod(disabledList, modsDirectory, "enable");
        refreshButton.Click += (_, _) => ReloadLists(text.ReloadedModListStatus);
        restartButton.Click += (_, _) => RestartGame();
        savesButton.Click += (_, _) => ConfigureSaves();
        settingsButton.Click += (_, _) => ConfigureDisabledDirectory();
        languageButton.Click += (_, _) => ConfigureLanguage();
        enabledList.SelectedIndexChanged += (_, _) => UpdateButtons();
        disabledList.SelectedIndexChanged += (_, _) => UpdateButtons();
        enabledList.DoubleClick += (_, _) => MoveSelectedMod(enabledList, disabledDirectory, "disable");
        disabledList.DoubleClick += (_, _) => MoveSelectedMod(disabledList, modsDirectory, "enable");
        enabledList.Resize += (_, _) => ResizeListColumns(enabledList);
        disabledList.Resize += (_, _) => ResizeListColumns(disabledList);
        Shown += HandleShown;

        EnableArchiveDrop(this);

        ResumeLayout(performLayout: true);

        ApplyLocalizedText();
        UpdateDirectoryLabels();
        ResizeListColumns(enabledList);
        ResizeListColumns(disabledList);
        ReloadLists(text.ReadyStatus(disabledDirectoryName));
    }

    private void HandleShown(object? sender, EventArgs eventArgs)
    {
        Shown -= HandleShown;

        if (startupArchivePaths.Length == 0)
        {
            return;
        }

        InstallArchives(startupArchivePaths, text.ArchiveImportTitle);
    }

    private static ListView CreateListView()
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

        listView.Columns.Add(string.Empty, 200);
        listView.Columns.Add(string.Empty, 280);
        listView.Columns.Add(string.Empty, 180);
        return listView;
    }

    private void ApplyLocalizedText()
    {
        text = new UiText(language);
        Text = text.AppTitle;
        titleLabel.Text = text.TitleText;
        disableButton.Text = text.DisableSelectedButton;
        enableButton.Text = text.EnableSelectedButton;
        refreshButton.Text = text.RefreshButton;
        restartButton.Text = text.RestartGameButton;
        savesButton.Text = text.SavesButton;
        settingsButton.Text = text.DisabledFolderButton;
        languageButton.Text = text.LanguageButton;
        enabledList.Columns[0].Text = text.IdColumn;
        enabledList.Columns[1].Text = text.NameColumn;
        enabledList.Columns[2].Text = text.FolderColumn;
        disabledList.Columns[0].Text = text.IdColumn;
        disabledList.Columns[1].Text = text.NameColumn;
        disabledList.Columns[2].Text = text.FolderColumn;
        UpdateDirectoryLabels();
    }

    private void ReloadLists(string statusText)
    {
        try
        {
            UpdateDirectoryLabels();
            var enabledMods = LoadMods(modsDirectory);
            var disabledMods = LoadMods(disabledDirectory);

            PopulateList(enabledList, enabledMods);
            PopulateList(disabledList, disabledMods);

            enabledGroup.Text = $"Enabled ({enabledMods.Count})";
            enabledGroup.Text = text.EnabledGroup(enabledMods.Count);
            disabledGroup.Text = text.DisabledGroup(disabledDirectoryName, disabledMods.Count);
            ResizeListColumns(enabledList);
            ResizeListColumns(disabledList);
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

    private void MoveSelectedMod(ListView sourceList, string targetDirectory, string operationVerb)
    {
        if (sourceList.SelectedItems.Count == 0)
        {
            return;
        }

        var selectedMod = sourceList.SelectedItems[0].Tag as ModInfo;
        if (selectedMod is null)
        {
            SetStatus(text.SelectedModNotResolvedStatus);
            return;
        }

        var conflicts = FindModsById(selectedMod.Id, selectedMod.FullPath);
        if (conflicts.Count > 0)
        {
            var choice = PromptConflictResolution(
                selectedMod,
                conflicts,
                $"selected mod at {FormatPathForDisplay(selectedMod.FullPath)}");

            if (choice == ConflictChoice.Cancel)
            {
                SetStatus(text.OperationCanceledStatus(operationVerb, selectedMod.Id));
                return;
            }

            if (choice == ConflictChoice.KeepExisting)
            {
                DeleteModDirectories(new[] { selectedMod });
                ReloadLists(text.KeptExistingStatus(selectedMod.Id));
                return;
            }

            DeleteModDirectories(conflicts);
        }

        var targetPath = Path.Combine(targetDirectory, selectedMod.FolderName);
        if (Directory.Exists(targetPath))
        {
            MessageBox.Show(
                text.TargetFolderAlreadyExistsMessage(selectedMod.FolderName),
                text.MoveSkippedTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            SetStatus(text.MoveSkippedStatus(selectedMod.Id));
            return;
        }

        try
        {
            MoveDirectory(selectedMod.FullPath, targetPath);
            ReloadLists(text.MoveCompletedStatus(operationVerb, selectedMod.Id, selectedMod.Name));
        }
        catch (Exception exception)
        {
            SetStatus(text.MoveFailedStatus(selectedMod.Id, exception.Message));
            MessageBox.Show(
                exception.Message,
                text.MoveErrorTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
            installPlan.FolderName,
            Path.Combine(disabledDirectory, installPlan.FolderName));

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

        var targetPath = Path.Combine(disabledDirectory, installPlan.FolderName);
        if (Directory.Exists(targetPath))
        {
            return new ArchiveInstallStepResult(
                new OperationResult(false, text.ArchiveTargetFolderExists(archiveFileName, installPlan.FolderName)),
                StopProcessingArchive: false);
        }

        string? extractionRoot = null;
        try
        {
            extractionRoot = ExtractArchiveToTemporaryFolder(archivePath, installPlan);
            MoveDirectory(Path.Combine(extractionRoot, installPlan.FolderName), targetPath);
            return new ArchiveInstallStepResult(
                new OperationResult(true, text.ArchiveInstalled(archiveFileName, installPlan.Id, disabledDirectoryName)),
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

    private void PopulateList(ListView listView, IReadOnlyList<ModInfo> mods)
    {
        listView.BeginUpdate();
        listView.Items.Clear();

        foreach (var mod in mods)
        {
            var item = new ListViewItem(mod.Id);
            item.SubItems.Add(mod.Name);
            item.SubItems.Add(mod.FolderName);
            item.Tag = mod;
            item.ToolTipText = text.ModTooltip(mod.Id, mod.Name, FormatVersionText(mod.Version), mod.FolderName);
            listView.Items.Add(item);
        }

        listView.EndUpdate();
    }

    private void ResizeListColumns(ListView listView)
    {
        if (listView.Columns.Count != 3 || listView.ClientSize.Width <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(240, listView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        listView.Columns[0].Width = Math.Max(140, (int)(availableWidth * 0.26f));
        listView.Columns[1].Width = Math.Max(220, (int)(availableWidth * 0.48f));
        listView.Columns[2].Width = Math.Max(140, availableWidth - listView.Columns[0].Width - listView.Columns[1].Width);
    }

    private void UpdateButtons()
    {
        disableButton.Enabled = enabledList.SelectedItems.Count > 0;
        enableButton.Enabled = disabledList.SelectedItems.Count > 0;
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

    private void ConfigureDisabledDirectory()
    {
        var updatedName = PromptForDirectoryName(disabledDirectoryName);
        if (updatedName is null || string.Equals(updatedName, disabledDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var newDirectory = Path.Combine(gameDirectory, updatedName);
        if (string.Equals(newDirectory, modsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                text.DisabledFolderMatchesModsMessage,
                text.InvalidDirectoryTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            MigrateDisabledDirectory(disabledDirectory, newDirectory);
            disabledDirectoryName = updatedName;
            disabledDirectory = newDirectory;
            Directory.CreateDirectory(disabledDirectory);
            SaveSettings(CurrentSettings());
            ReloadLists(text.DisabledFolderUpdatedStatus(disabledDirectoryName));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                text.UpdateFailedTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetStatus(text.DisabledFolderUpdateFailedStatus(exception.Message));
        }
    }

    private void ConfigureLanguage()
    {
        var updatedLanguage = PromptForLanguage(language);
        if (!updatedLanguage.HasValue || updatedLanguage.Value == language)
        {
            return;
        }

        language = updatedLanguage.Value;
        ApplyLocalizedText();
        SaveSettings(CurrentSettings());
        ReloadLists(text.LanguageUpdatedStatus);
    }

    private void ConfigureSaves()
    {
        using var dialog = new SaveManagerDialog(text);
        dialog.ShowDialog(this);
    }

    private void RestartGame()
    {
        try
        {
            ForceStopGame();
            LaunchGameViaSteam();
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
            Arguments = $"-applaunch {SlayTheSpire2AppId}",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(steamPath) ?? AppContext.BaseDirectory
        };

        Process.Start(startInfo);
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

            return new AppSettings(disabledDirectoryValue, languageCode);
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
        return new AppSettings(disabledDirectoryName, Localization.ToCode(language));
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

            var modId = fileStem;
            var modName = fileStem;
            string? modVersion = null;

            ReadManifestMetadata(document.RootElement, ref modId, ref modName, ref modVersion);

            var folderName = ResolveArchiveFolderName(directoryName, fileStem, modId, isSpecialManifestFile);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return false;
            }

            var extractFullDirectory = manifestCountInDirectory == 1 &&
                (!string.IsNullOrEmpty(directoryPath) || isSpecialManifestFile);

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
                folderName,
                entryPath,
                segments.Length - 1,
                extractFullDirectory ? directoryPath + "/" : string.Empty,
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
        var destinationRoot = Path.Combine(extractionRoot, installPlan.FolderName);
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
                entryPath = entryPath.Substring(installPlan.RootPrefix.Length);
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
                .Where(candidate => string.IsNullOrEmpty(GetArchiveDirectory(candidate.NormalizedPath)))
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
internal sealed class SaveManagerDialog : Form
{
    private readonly UiText text;
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
    private readonly Button closeButton;
    private readonly StatusStrip statusStrip;
    private readonly ToolStripStatusLabel statusLabel;

    private bool suppressSelectionEvents;

    public SaveManagerDialog(UiText text)
    {
        this.text = text;
        saveRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlayTheSpire2", "steam");

        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 560);
        Size = new Size(1120, 680);
        Text = text.SavesDialogTitle;

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
        closeButton = new Button { AutoSize = true, Text = text.CloseButton };
        statusStrip = new StatusStrip();
        statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        SuspendLayout();

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
        transferButtonsPanel.Controls.Add(closeButton);

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

        statusStrip.Items.Add(statusLabel);
        Controls.Add(mainLayout);
        Controls.Add(statusStrip);

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
        closeButton.Click += (_, _) => Close();

        ResumeLayout(performLayout: true);

        saveRootLabel.Text = text.SaveRootLabel(saveRoot);
        ResizeSaveListColumns(vanillaList);
        ResizeSaveListColumns(moddedList);
        ReloadSteamUsers(text.ReadySaveManagerStatus);
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
        statusLabel.Text = message;
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
    string FolderName,
    string EntryPath,
    int ManifestDepth,
    string RootPrefix,
    bool ExtractFullDirectory,
    IReadOnlyList<string> SourceEntries);

internal sealed record ArchiveInstallStepResult(OperationResult Result, bool StopProcessingArchive);

internal sealed record ArchiveEntryInfo(ZipArchiveEntry Entry, string NormalizedPath);

internal sealed record AppSettings(string DisabledDirectoryName, string? LanguageCode)
{
    public static AppSettings Default { get; } = new(".mods", null);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class ModManagerJsonContext : JsonSerializerContext
{
}

internal sealed record OperationResult(bool RefreshRequired, string Message);

internal sealed record LanguageOption(AppLanguage Language, string DisplayName)
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
    public string EnableSelectedButton => isChinese ? "<- 启用所选" : "<- Enable selected";
    public string RefreshButton => isChinese ? "刷新" : "Refresh";
    public string RestartGameButton => isChinese ? "重启游戏" : "Restart Game";
    public string SavesButton => isChinese ? "存档..." : "Saves...";
    public string DisabledFolderButton => isChinese ? "禁用目录..." : "Disabled folder...";
    public string LanguageButton => isChinese ? "语言..." : "Language...";
    public string IdColumn => "ID";
    public string NameColumn => isChinese ? "名称" : "Name";
    public string FolderColumn => isChinese ? "文件夹" : "Folder";
    public string ArchiveImportTitle => isChinese ? "导入压缩包" : "Archive Import";
    public string ReloadedModListStatus => isChinese ? "已重新加载模组列表。" : "Reloaded mod list.";
    public string GameNotFoundTitle => isChinese ? "未找到游戏" : "Game Not Found";
    public string LoadErrorTitle => isChinese ? "加载错误" : "Load Error";
    public string SelectedModNotResolvedStatus => isChinese ? "无法解析当前选中的模组。" : "The selected mod could not be resolved.";
    public string MoveSkippedTitle => isChinese ? "已跳过移动" : "Move Skipped";
    public string MoveErrorTitle => isChinese ? "移动错误" : "Move Error";
    public string NoFilesProvidedStatus => isChinese ? "未提供任何文件。" : "No files were provided.";
    public string DisabledFolderMatchesModsMessage => isChinese ? "禁用模组目录不能与启用模组目录相同。" : "The disabled mods folder cannot be the same as the enabled mods folder.";
    public string InvalidDirectoryTitle => isChinese ? "无效目录" : "Invalid Directory";
    public string UpdateFailedTitle => isChinese ? "更新失败" : "Update Failed";
    public string DisabledFolderDialogTitle => isChinese ? "禁用模组目录" : "Disabled Mods Folder";
    public string DisabledFolderPrompt => isChinese ? "选择游戏根目录下用于存放已禁用模组的文件夹名称。" : "Choose the folder name under the game root used to store disabled mods.";
    public string DisabledFolderHint => isChinese ? "示例: .mods, disabled-mods, archived-mods" : "Examples: .mods, disabled-mods, archived-mods";
    public string SaveButton => isChinese ? "保存" : "Save";
    public string CancelButton => isChinese ? "取消" : "Cancel";
    public string InvalidDirectoryNameTitle => isChinese ? "无效目录名" : "Invalid Directory Name";
    public string EmptyFolderNameMessage => isChinese ? "文件夹名称不能为空。" : "The folder name cannot be empty.";
    public string InvalidFolderCharactersMessage => isChinese ? "文件夹名称包含无效字符。" : "The folder name contains invalid characters.";
    public string SingleFolderNameMessage => isChinese ? "请输入单个文件夹名称，不要填写路径。" : "Use a single folder name, not a path.";
    public string DotFolderNameMessage => isChinese ? "文件夹名称不能是 '.' 或 '..'。" : "The folder name cannot be '.' or '..'.";
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
    public string GameRestartedStatus => isChinese ? "已强制关闭游戏并通过 Steam 重新启动。" : "Force-stopped the game and relaunched it through Steam.";

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

    public string GameRestartFailedStatus(string message)
    {
        return isChinese ? $"重启游戏失败: {message}" : $"Failed to restart the game: {message}";
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

    public string DisabledFolderUpdateFailedStatus(string message)
    {
        return isChinese ? $"更新禁用模组目录失败: {message}" : $"Failed to update disabled mods folder: {message}";
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

    public string ArchiveInstalled(string archiveFileName, string modId, string disabledDirectoryName)
    {
        return isChinese
            ? $"{archiveFileName}: 已将 {modId} 安装到 {disabledDirectoryName}。"
            : $"{archiveFileName}: installed {modId} to {disabledDirectoryName}.";
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