// =============================================================================
// ModView.CardDiff.cs  -  Diff-based mod card refresh + per-card render
// =============================================================================
//
// This partial owns the rendering pipeline:
//   RefreshCardDisplay -> BuildDisplayItems -> ReconcileCardEntries +
//   ReconcileControlOrder -> SyncCardWidths.
//
// Plus the ModCardEntry record + DisplayItem struct + WM_SETREDRAW
// repaint suspension + the EnableDoubleBuffered reflection helper.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;
using STS2ModManager.Services;
using STS2ModManager.Services.UI;
using STS2ModManager.Views.Widgets;

namespace STS2ModManager.Views.Main;

[SupportedOSPlatform("windows")]
internal sealed partial class ModView
{
    private const int CardMinWidth = 250;
    private const int CardSpacing = 6;
    private const int CardHeight = 96;
    private const int WM_SETREDRAW = 0x000B;

    private readonly Dictionary<string, ModCardEntry> cardEntries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Control> headerControls = new();

    private void RefreshCardDisplay()
    {
        var savedScroll = cardScrollPanel.AutoScrollPosition;

        using (SuspendDrawing(cardScrollPanel))
        {
            cardPanel.SuspendLayout();
            try
            {
                var displayItems = BuildDisplayItems();
                ReconcileCardEntries(displayItems);
                ReconcileControlOrder(displayItems);
                SyncCardWidths();
            }
            finally
            {
                cardPanel.ResumeLayout(performLayout: true);
            }

            // Restore scroll (AutoScrollPosition getter returns negative coords; setter expects positive).
            cardScrollPanel.AutoScrollPosition = new Point(-savedScroll.X, -savedScroll.Y);
        }
    }

    private List<DisplayItem> BuildDisplayItems()
    {
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

        var items = new List<DisplayItem>();

        if (splitModList && activeFilter == ModFilter.All)
        {
            if (showEnabled && enabledFiltered.Count > 0)
            {
                items.Add(DisplayItem.Header(loc.Get("mods.enabled_group", enabledFiltered.Count)));
                foreach (var mod in enabledFiltered)
                {
                    items.Add(DisplayItem.Card(mod, isEnabled: true));
                }
            }

            if (showDisabled && disabledFiltered.Count > 0)
            {
                items.Add(DisplayItem.Header(loc.Get("config.disabled_group", getDisabledDirectoryName(), disabledFiltered.Count)));
                foreach (var mod in disabledFiltered)
                {
                    items.Add(DisplayItem.Card(mod, isEnabled: false));
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
                items.Add(DisplayItem.Header(loc.Get("mods.all_mods_group", allMods.Count)));
                foreach (var (mod, enabled) in allMods)
                {
                    items.Add(DisplayItem.Card(mod, isEnabled: enabled));
                }
            }
        }

        if (items.Count == 0)
        {
            items.Add(DisplayItem.Header(loc.Get("launch.no_mods_found_label")));
        }

        return items;
    }

    private void ReconcileCardEntries(List<DisplayItem> displayItems)
    {
        var allCachedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in cachedEnabledMods) allCachedPaths.Add(m.FullPath);
        foreach (var m in cachedDisabledMods) allCachedPaths.Add(m.FullPath);

        var stalePaths = cardEntries.Keys.Where(p => !allCachedPaths.Contains(p)).ToList();
        foreach (var p in stalePaths)
        {
            var entry = cardEntries[p];
            cardPanel.Controls.Remove(entry.Card);
            entry.Card.Dispose();
            cardEntries.Remove(p);
        }

        var desiredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in displayItems)
        {
            if (item.Mod is { } mod) desiredPaths.Add(mod.FullPath);
        }
        foreach (var (path, entry) in cardEntries)
        {
            if (!desiredPaths.Contains(path) && cardPanel.Controls.Contains(entry.Card))
            {
                cardPanel.Controls.Remove(entry.Card);
            }
        }

        foreach (var h in headerControls)
        {
            cardPanel.Controls.Remove(h);
            h.Dispose();
        }
        headerControls.Clear();
    }

    private void ReconcileControlOrder(List<DisplayItem> displayItems)
    {
        var finalControls = new List<Control>(displayItems.Count);
        foreach (var item in displayItems)
        {
            Control control;
            if (item.Mod is { } mod)
            {
                if (!cardEntries.TryGetValue(mod.FullPath, out var entry))
                {
                    entry = CreateModCardEntry(mod);
                    cardEntries[mod.FullPath] = entry;
                }
                UpdateModCardEntry(entry, mod, item.IsEnabled);
                control = entry.Card;
            }
            else
            {
                var header = WidgetFactory.CreateSectionHeader(item.HeaderText!, Font, CardSpacing);
                headerControls.Add(header);
                control = header;
            }
            finalControls.Add(control);
        }

        for (int i = 0; i < finalControls.Count; i++)
        {
            var ctl = finalControls[i];
            if (!cardPanel.Controls.Contains(ctl))
            {
                cardPanel.Controls.Add(ctl);
            }
            cardPanel.Controls.SetChildIndex(ctl, i);
        }
    }

    private void SyncCardWidths()
    {
        var scrollWidth = cardScrollPanel.ClientSize.Width;
        if (scrollWidth <= 0)
        {
            return;
        }

        var panelWidth = Math.Max(CardMinWidth + 24, scrollWidth - cardScrollBar.HoverWidth - 2);
        cardPanel.MinimumSize = new Size(panelWidth, 0);
        cardPanel.MaximumSize = new Size(panelWidth, 0);

        var available = panelWidth - cardPanel.Padding.Horizontal;
        var cardsPerRow = Math.Max(1, available / (CardMinWidth + CardSpacing * 2));
        var cardWidth = (available - cardsPerRow * CardSpacing * 2) / cardsPerRow;
        var headerWidth = available - CardSpacing * 2;

        foreach (Control c in cardPanel.Controls)
        {
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

    private ModCardEntry CreateModCardEntry(ModInfo mod)
    {
        var card = new Panel
        {
            Height = CardHeight,
            Margin = new Padding(CardSpacing),
            BorderStyle = BorderStyle.None,
            Cursor = Cursors.Hand,
            Tag = mod
        };
        EnableDoubleBuffered(card);

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
            Width = 5
        };

        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(8, 6, 8, 6)
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));

        // NOTE: don't set Font explicitly here. When a card is recreated after
        // a toggle, the user control's inherited Font may have shifted (e.g.
        // MaterialSkin's Roboto finishing registration), and a captured Font
        // instance from earlier cards no longer matches. Letting the Label
        // inherit from its parent guarantees every card renders identically.
        var nameLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var versionLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var detailLabel = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var enableSwitch = new MaterialSwitch
        {
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = new Padding(8, 0, 0, 0),
        };

        contentLayout.Controls.Add(nameLabel, 0, 0);
        contentLayout.Controls.Add(enableSwitch, 1, 0);
        contentLayout.SetRowSpan(enableSwitch, 3);
        contentLayout.Controls.Add(versionLabel, 0, 1);
        contentLayout.Controls.Add(detailLabel, 0, 2);

        card.Controls.Add(contentLayout);
        card.Controls.Add(indicator);

        var entry = new ModCardEntry(card, indicator, contentLayout, nameLabel, versionLabel, detailLabel, enableSwitch);
        entry.Mod = mod;

        // Right-click anywhere on the card opens the version-chooser menu
        // (only populated when the mod has any archived versions).
        var contextMenu = new ContextMenuStrip
        {
            Renderer = new ThemedContextMenuRenderer(),
            ShowImageMargin = false,
        };
        contextMenu.Opening += (s, e) =>
        {
            var modNow = entry.Card.Tag as ModInfo ?? entry.Mod;
            PopulateVersionMenu(contextMenu, modNow, entry.IsEnabled);
            if (contextMenu.Items.Count == 0)
            {
                e.Cancel = true;
            }
        };
        card.ContextMenuStrip = contextMenu;
        contentLayout.ContextMenuStrip = contextMenu;
        nameLabel.ContextMenuStrip = contextMenu;
        versionLabel.ContextMenuStrip = contextMenu;
        detailLabel.ContextMenuStrip = contextMenu;
        indicator.ContextMenuStrip = contextMenu;
        enableSwitch.ContextMenuStrip = contextMenu;

        enableSwitch.CheckedChanged += (_, _) =>
        {
            if (entry.SuppressSwitchHandler) return;
            var modNow = entry.Card.Tag as ModInfo ?? entry.Mod;
            ToggleModRequested?.Invoke(modNow);
            ToggleMod(modNow, enable: enableSwitch.Checked);
        };

        void SelectThis(object? s, EventArgs e)
        {
            var modNow = entry.Card.Tag as ModInfo ?? entry.Mod;
            SelectCard(card, modNow);
        }
        card.Click += SelectThis;
        contentLayout.Click += SelectThis;
        nameLabel.Click += SelectThis;
        versionLabel.Click += SelectThis;
        detailLabel.Click += SelectThis;
        indicator.Click += SelectThis;

        return entry;
    }

    private void UpdateModCardEntry(ModCardEntry entry, ModInfo mod, bool isEnabled)
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

        // Text colours are intentionally state-independent: only the left
        // indicator strip and the toggle switch reflect enabled / disabled.
        // Otherwise toggling a mod made the name and id "pop" or "fade"
        // which read as a font-weight change.
        var nameForeground = dark ? Color.White : SystemColors.ControlText;
        var versionForeground = dark ? Color.FromArgb(210, 210, 210) : Color.FromArgb(60, 60, 60);
        var detailForeground = dark ? Color.FromArgb(140, 140, 140) : Color.FromArgb(140, 140, 140);

        entry.Mod = mod;
        entry.IsEnabled = isEnabled;
        entry.Card.Tag = mod;

        entry.Card.BackColor = cardBack;
        entry.Content.BackColor = cardBack;
        entry.EnableSwitch.BackColor = cardBack;
        entry.Indicator.BackColor = statusColor;
        entry.NameLabel.BackColor = cardBack;
        entry.VersionLabel.BackColor = cardBack;
        entry.DetailLabel.BackColor = cardBack;
        entry.NameLabel.ForeColor = nameForeground;
        entry.VersionLabel.ForeColor = versionForeground;
        entry.DetailLabel.ForeColor = detailForeground;

        var newName = mod.Name ?? string.Empty;
        if (!string.Equals(entry.NameLabel.Text, newName, StringComparison.Ordinal))
        {
            entry.NameLabel.Text = newName;
        }

        var versionCount = archiveVersionsByModId.TryGetValue(mod.Id, out var versions) ? versions.Count : 0;
        var versionsHint = versionCount > 1
            ? $"  \u2022  {loc.Get("archive.version_count_hint", versionCount)}"
            : string.Empty;
        var newVersion = $"{FormatVersionText(mod.Version)}{versionsHint}";
        if (!string.Equals(entry.VersionLabel.Text, newVersion, StringComparison.Ordinal))
        {
            entry.VersionLabel.Text = newVersion;
        }

        var newDetail = mod.Id;
        if (!string.Equals(entry.DetailLabel.Text, newDetail, StringComparison.Ordinal))
        {
            entry.DetailLabel.Text = newDetail;
        }

        if (entry.EnableSwitch.Checked != isEnabled)
        {
            entry.SuppressSwitchHandler = true;
            try { entry.EnableSwitch.Checked = isEnabled; }
            finally { entry.SuppressSwitchHandler = false; }
        }

        entry.Card.Invalidate();
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
                            else if (sw is Label lbl)
                            {
                                lbl.BackColor = bg;
                            }
                        }
                    }
                }
                p.Invalidate();
            }
        }

        UpdateButtons();
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

    private sealed class ModCardEntry
    {
        public Panel Card { get; }
        public Panel Indicator { get; }
        public TableLayoutPanel Content { get; }
        public Label NameLabel { get; }
        public Label VersionLabel { get; }
        public Label DetailLabel { get; }
        public MaterialSwitch EnableSwitch { get; }
        public ModInfo Mod { get; set; } = null!;
        public bool IsEnabled { get; set; }
        public bool SuppressSwitchHandler { get; set; }

        public ModCardEntry(Panel card, Panel indicator, TableLayoutPanel content, Label name, Label version, Label detail, MaterialSwitch sw)
        {
            Card = card;
            Indicator = indicator;
            Content = content;
            NameLabel = name;
            VersionLabel = version;
            DetailLabel = detail;
            EnableSwitch = sw;
        }
    }

    private readonly record struct DisplayItem(ModInfo? Mod, bool IsEnabled, string? HeaderText)
    {
        public static DisplayItem Card(ModInfo mod, bool isEnabled) => new(mod, isEnabled, null);
        public static DisplayItem Header(string text) => new(null, false, text);
    }

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static IDisposable SuspendDrawing(Control control) => new DrawingSuspension(control);

    private sealed class DrawingSuspension : IDisposable
    {
        private readonly Control _control;
        public DrawingSuspension(Control control)
        {
            _control = control;
            if (_control.IsHandleCreated)
            {
                SendMessage(_control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            }
        }
        public void Dispose()
        {
            if (_control.IsHandleCreated)
            {
                SendMessage(_control.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                _control.Invalidate(invalidateChildren: true);
            }
        }
    }

    private static void EnableDoubleBuffered(Control control)
    {
        var prop = typeof(Control).GetProperty(
            "DoubleBuffered",
            BindingFlags.Instance | BindingFlags.NonPublic);
        prop?.SetValue(control, true);
    }
}
