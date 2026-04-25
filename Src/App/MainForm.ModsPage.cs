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

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed partial class ModManagerForm
{
    private void ReloadLists(string statusText)
    {
        try
        {
            UpdateDirectoryLabels();
            cachedEnabledMods = ModLoader.LoadMods(modsDirectory);
            cachedDisabledMods = ModLoader.LoadMods(disabledDirectory);
            enabledModCount = cachedEnabledMods.Count;
            disabledModCount = cachedDisabledMods.Count;

            selectedModPaths.Clear();
            RefreshCardDisplay();
            SetStatus(statusText);
            UpdateButtons();
        }
        catch (Exception exception)
        {
            SetStatus(loc.Get("mods.load_failed_status", exception.Message));
            MessageDialog.Error(
                this,
                loc,
                loc.Get("mods.load_error_title"),
                exception.Message);
        }
    }

    private void RefreshCardDisplay()
    {
        cardPanel.SuspendLayout();
        cardPanel.Controls.Clear();

        var search = activeSearchTerm?.Trim() ?? string.Empty;
        bool MatchesSearch(ModInfo mod)
        {
            if (search.Length == 0) return true;
            return (mod.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (mod.Id?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        var enabledFiltered = cachedEnabledMods.Where(MatchesSearch).ToList();
        var disabledFiltered = cachedDisabledMods.Where(MatchesSearch).ToList();

        var showEnabled = activeFilter != ModFilter.Disabled;
        var showDisabled = activeFilter != ModFilter.Enabled;

        if (splitModList && activeFilter == ModFilter.All)
        {
            if (showEnabled && enabledFiltered.Count > 0)
            {
                cardPanel.Controls.Add(WidgetFactory.CreateSectionHeader(loc.Get("mods.enabled_group", enabledFiltered.Count), Font, CardSpacing));
                foreach (var mod in enabledFiltered)
                {
                    cardPanel.Controls.Add(CreateModCard(mod, isEnabled: true));
                }
            }

            if (showDisabled && disabledFiltered.Count > 0)
            {
                cardPanel.Controls.Add(WidgetFactory.CreateSectionHeader(loc.Get("config.disabled_group", disabledDirectoryName, disabledFiltered.Count), Font, CardSpacing));
                foreach (var mod in disabledFiltered)
                {
                    cardPanel.Controls.Add(CreateModCard(mod, isEnabled: false));
                }
            }
        }
        else
        {
            var allMods = new List<(ModInfo Mod, bool Enabled)>();
            if (showEnabled) allMods.AddRange(enabledFiltered.Select(m => (m, true)));
            if (showDisabled) allMods.AddRange(disabledFiltered.Select(m => (m, false)));
            allMods = allMods.OrderBy(pair => pair.Mod.Name, StringComparer.OrdinalIgnoreCase).ToList();

            if (allMods.Count > 0)
            {
                cardPanel.Controls.Add(WidgetFactory.CreateSectionHeader(loc.Get("mods.all_mods_group", allMods.Count), Font, CardSpacing));
                foreach (var (mod, enabled) in allMods)
                {
                    cardPanel.Controls.Add(CreateModCard(mod, isEnabled: enabled));
                }
            }
        }

        if (cardPanel.Controls.Count == 0)
        {
            cardPanel.Controls.Add(WidgetFactory.CreateSectionHeader(loc.Get("launch.no_mods_found_label"), Font, CardSpacing));
        }

        cardPanel.ResumeLayout(performLayout: true);
        SyncCardWidths();
    }

    private void SetActiveFilter(ModFilter filter)
    {
        activeFilter = filter;
        UpdateFilterChipStyles();
        RefreshCardDisplay();
    }

    private void UpdateFilterChipStyles()
    {
        filterAllButton.IsHighlight      = activeFilter == ModFilter.All;
        filterEnabledButton.IsHighlight  = activeFilter == ModFilter.Enabled;
        filterDisabledButton.IsHighlight = activeFilter == ModFilter.Disabled;
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
        var mods = ModLoader.LoadMods(sourceDirectory);

        if (mods.Count == 0)
        {
            return;
        }

        if (!MessageDialog.Confirm(
                this,
                loc,
                loc.Get("mods.bulk_move_title"),
                LocalizedFormats.BulkMovePrompt(loc, operationVerb, mods.Count)))
        {
            SetStatus(LocalizedFormats.BulkMoveCanceledStatus(loc, operationVerb));
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

        var statusMessage = LocalizedFormats.BulkMoveCompletedStatus(loc, operationVerb, changedCount, unchangedCount, failedCount);
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
                return new ModMoveResult(ModMoveOutcome.Unchanged, LocalizedFormats.OperationCanceledStatus(loc, operationVerb, selectedMod.Id));
            }

            if (choice == ConflictChoice.KeepExisting)
            {
                DeleteModDirectories(new[] { selectedMod });
                return new ModMoveResult(ModMoveOutcome.Changed, loc.Get("archive.kept_existing_status", selectedMod.Id));
            }

            DeleteModDirectories(conflicts);
        }

        var targetPath = Path.Combine(targetDirectory, selectedMod.FolderName);
        if (Directory.Exists(targetPath))
        {
            if (showDialogs)
            {
                MessageDialog.Warn(
                    this,
                    loc,
                    loc.Get("mods.move_skipped_title"),
                    loc.Get("archive.target_folder_already_exists_message", selectedMod.FolderName));
            }

            return new ModMoveResult(ModMoveOutcome.Unchanged, loc.Get("mods.move_skipped_status", selectedMod.Id));
        }

        try
        {
            MoveDirectory(selectedMod.FullPath, targetPath);
            return new ModMoveResult(ModMoveOutcome.Changed, LocalizedFormats.MoveCompletedStatus(loc, operationVerb, selectedMod.Id, selectedMod.Name));
        }
        catch (Exception exception)
        {
            if (showDialogs)
            {
                MessageDialog.Error(
                    this,
                    loc,
                    loc.Get("mods.move_error_title"),
                    exception.Message);
            }

            return new ModMoveResult(ModMoveOutcome.Failed, loc.Get("mods.move_failed_status", selectedMod.Id, exception.Message));
        }
    }

    private void OpenSelectedModFolder()
    {
        var selectedMods = GetSelectedMods();
        if (selectedMods.Count == 0)
        {
            SetStatus(loc.Get("saves.select_mod_folder_status"));
            return;
        }

        if (selectedMods.Count > 1)
        {
            SetStatus(loc.Get("saves.select_single_mod_folder_status"));
            return;
        }

        var selectedMod = selectedMods[0];

        if (!Directory.Exists(selectedMod.FullPath))
        {
            var message = loc.Get("mods.mod_folder_missing_message", selectedMod.FullPath);
            SetStatus(loc.Get("common.open_folder_failed_status", message));
            MessageDialog.Warn(
                this,
                loc,
                loc.Get("common.open_folder_error_title"),
                message);
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
            SetStatus(loc.Get("common.open_folder_opened_status", selectedMod.Id));
        }
        catch (Exception exception)
        {
            SetStatus(loc.Get("common.open_folder_failed_status", exception.Message));
            MessageDialog.Error(
                this,
                loc,
                loc.Get("common.open_folder_error_title"),
                exception.Message);
        }
    }

    private void ExportSelectedMods()
    {
        var selectedMods = GetSelectedMods();
        if (selectedMods.Count == 0)
        {
            SetStatus(loc.Get("saves.select_mods_export_status"));
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "zip",
            FileName = BuildDefaultExportArchiveName(selectedMods),
            Filter = loc.Get("ui.export_archive_filter"),
            OverwritePrompt = true,
            Title = loc.Get("ui.export_archive_dialog_title")
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            SetStatus(loc.Get("ui.export_canceled_status"));
            return;
        }

        try
        {
            CreateModArchive(dialog.FileName, selectedMods);
            var message = loc.Get("ui.export_completed_status", selectedMods.Count, dialog.FileName);
            SetStatus(message);
            Notify(loc.Get("ui.export_completed_message", selectedMods.Count, dialog.FileName));
        }
        catch (Exception exception)
        {
            SetStatus(loc.Get("ui.export_failed_status", exception.Message));
            MessageDialog.Error(
                this,
                loc,
                loc.Get("ui.export_failed_title"),
                exception.Message);
        }
    }

    private List<ModInfo> GetSelectedMods()
    {
        var selectedMods = new List<ModInfo>();
        foreach (Control control in cardPanel.Controls)
        {
            if (control is Panel panel &&
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

        // Reserve room for the overlay scrollbar so cards never sit underneath it.
        var panelWidth = Math.Max(CardMinWidth + 24, scrollWidth - cardScrollBar.HoverWidth - 2);
        cardPanel.MinimumSize = new Size(panelWidth, 0);
        cardPanel.MaximumSize = new Size(panelWidth, 0);

        var available = panelWidth - cardPanel.Padding.Horizontal;
        var cardsPerRow = Math.Max(1, available / (CardMinWidth + CardSpacing * 2));
        var cardWidth = (available - cardsPerRow * CardSpacing * 2) / cardsPerRow;
        var headerWidth = available - CardSpacing * 2;

        foreach (Control c in cardPanel.Controls)
        {
            // Section headers span the full row; mod cards share the row
            if (c is Panel p && p.Tag is ModInfo)
            {
                c.Width = Math.Max(CardMinWidth, cardWidth);
            }
            else
            {
                c.Width = Math.Max(CardMinWidth, headerWidth);
            }
        }
    }

    private Panel CreateModCard(ModInfo mod, bool isEnabled)
    {
        var skin = MaterialSkinManager.Instance;
        var dark = skin.Theme == MaterialSkinManager.Themes.DARK;
        var enabledColor = Color.FromArgb(dark ? 129 : 76, dark ? 199 : 175, dark ? 132 : 80);
        var disabledColor = dark ? Color.FromArgb(120, 120, 120) : Color.FromArgb(180, 180, 180);
        var statusColor = isEnabled ? enabledColor : disabledColor;
        var unselectedBack = dark ? Color.FromArgb(48, 48, 48) : Color.White;
        var selectedBack = dark ? Color.FromArgb(33, 56, 86) : Color.FromArgb(219, 234, 249);
        var isSelected = selectedModPaths.Contains(mod.FullPath);
        var cardBack = isSelected ? selectedBack : unselectedBack;

        var card = new Panel
        {
            Height = CardHeight,
            Margin = new Padding(CardSpacing),
            BorderStyle = BorderStyle.None,
            BackColor = cardBack,
            Cursor = Cursors.Hand,
            Tag = mod
        };
        // Custom border: 2px accent when selected, 1px theme divider otherwise.
        card.Paint += (s, e) =>
        {
            var selected = card.Tag is ModInfo m && selectedModPaths.Contains(m.FullPath);
            var skin2 = MaterialSkinManager.Instance;
            var darkNow = skin2.Theme == MaterialSkinManager.Themes.DARK;
            var borderColor = selected
                ? skin2.ColorScheme.AccentColor
                : (darkNow ? Color.FromArgb(70, 70, 70) : Color.FromArgb(210, 210, 210));
            var thickness = selected ? 2 : 1;
            using var pen = new Pen(borderColor, thickness);
            var r = card.ClientRectangle;
            r.Width -= 1; r.Height -= 1;
            e.Graphics.DrawRectangle(pen, r);
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
            BackColor = cardBack,
            Padding = new Padding(8, 6, 8, 6)
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var nameForeground = dark
            ? (isEnabled ? Color.White : Color.FromArgb(170, 170, 170))
            : (isEnabled ? SystemColors.ControlText : SystemColors.GrayText);
        var detailForeground = dark ? Color.FromArgb(170, 170, 170) : SystemColors.GrayText;

        var nameLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = nameForeground,
            BackColor = Color.Transparent,
            Text = mod.Name,
            TextAlign = ContentAlignment.TopLeft
        };

        var detailLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            ForeColor = detailForeground,
            BackColor = Color.Transparent,
            Text = $"ID: {mod.Id}  \u2022  {FormatVersionText(mod.Version)}  \u2022  {mod.FolderName}",
            TextAlign = ContentAlignment.TopLeft
        };

        var enableSwitch = new MaterialSwitch
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Checked = isEnabled,
            Margin = new Padding(8, 0, 0, 0),
            BackColor = cardBack,
            Tag = mod,
        };
        // Avoid recursive ToggleMod when we set Checked programmatically during reload.
        var lastChecked = isEnabled;
        enableSwitch.CheckedChanged += (_, _) =>
        {
            if (enableSwitch.Checked == lastChecked)
            {
                return;
            }
            lastChecked = enableSwitch.Checked;
            ToggleMod(mod, enable: enableSwitch.Checked);
        };

        contentLayout.Controls.Add(nameLabel, 0, 0);
        contentLayout.Controls.Add(enableSwitch, 1, 0);
        contentLayout.SetRowSpan(enableSwitch, 2);
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

        var skin = MaterialSkinManager.Instance;
        var dark = skin.Theme == MaterialSkinManager.Themes.DARK;
        var unselectedBack = dark ? Color.FromArgb(48, 48, 48) : Color.White;
        var selectedBack = dark ? Color.FromArgb(33, 56, 86) : Color.FromArgb(219, 234, 249);

        foreach (Control c in cardPanel.Controls)
        {
            if (c is Panel p && p.Tag is ModInfo currentMod)
            {
                var nowSelected = selectedModPaths.Contains(currentMod.FullPath);
                var bg = nowSelected ? selectedBack : unselectedBack;
                p.BackColor = bg;
                foreach (Control inner in p.Controls)
                {
                    if (inner is TableLayoutPanel tlp)
                    {
                        tlp.BackColor = bg;
                        foreach (Control sw in tlp.Controls)
                        {
                            if (sw is MaterialSwitch ms)
                            {
                                ms.BackColor = bg;
                            }
                        }
                    }
                }
                p.Invalidate();
            }
        }

        UpdateButtons();
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

    private bool TryValidateDirectoryName(string value, out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = loc.Get("ui.empty_folder_name_message");
            return false;
        }

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errorMessage = loc.Get("ui.invalid_folder_characters_message");
            return false;
        }

        if (value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
        {
            errorMessage = loc.Get("ui.single_folder_name_message");
            return false;
        }

        if (value is "." or "..")
        {
            errorMessage = loc.Get("ui.dot_folder_name_message");
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

        return ModLoader.LoadMods(modsDirectory)
            .Concat(ModLoader.LoadMods(disabledDirectory))
            .Where(mod =>
                string.Equals(mod.Id, modId, StringComparison.OrdinalIgnoreCase) &&
                !excluded.Contains(Path.GetFullPath(mod.FullPath)))
            .ToList();
    }

    private ConflictChoice PromptConflictResolution(ModInfo incomingMod, IReadOnlyList<ModInfo> existingMods, string incomingSourceLabel)
    {
        return ConflictResolutionDialog.Show(
            this,
            loc,
            incomingMod,
            existingMods,
            incomingSourceLabel,
            DescribeVersionComparison(incomingMod, existingMods),
            mod => FormatPathForDisplay(mod.FullPath),
            FormatVersionText);
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

    private string FormatVersionText(string? version)
    {
        return string.IsNullOrWhiteSpace(version) ? loc.Get("ui.unknown_version_label") : version.Trim();
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
            var comparison = ModVersion.Compare(incomingMod.Version, existingMod.Version);
            if (comparison > 0)
            {
                comparisonLines.Add(loc.Get("archive.incoming_version_newer_message", FormatVersionText(incomingMod.Version), FormatVersionText(existingMod.Version)));
            }
            else if (comparison < 0)
            {
                comparisonLines.Add(loc.Get("archive.incoming_version_older_message", FormatVersionText(incomingMod.Version), FormatVersionText(existingMod.Version)));
            }
            else if (!string.IsNullOrWhiteSpace(existingMod.Version))
            {
                comparisonLines.Add(loc.Get("archive.incoming_version_same_message", FormatVersionText(existingMod.Version)));
            }
        }

        return string.Join(Environment.NewLine, comparisonLines.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
