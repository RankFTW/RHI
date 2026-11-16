// DetailPanelBuilder.Extras.RtxEncorePresets.cs -- "Your presets" panel at the top of the RTX Encore ⚙:
// the user's own presets (RtxEncorePresetService, shared by all games) in one dropdown with Apply,
// Save current and a "..." flyout to rename or clear. Same look and flow as OptiScaler's user slots.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>The slot last picked in the dropdown, kept while the app runs (the cog reopens after Apply).</summary>
    private static int _rtxEncoreSelectedSlot = -1;

    /// <summary>One setting a preset holds: where it is in rtx-encore.jsonc and the value the ⚙ shows when it's absent.</summary>
    private sealed record RtxEncorePresetSetting(string Section, string Key, string Default);

    /// <param name="settings">Filled while the ⚙ is built; read when Save / Apply is clicked.</param>
    /// <param name="afterApply">Runs after a preset is written (game-specific follow-ups), before the ⚙ reopens.</param>
    /// <param name="reopen">Closes and reopens the ⚙ so every dropdown shows the applied values.</param>
    private FrameworkElement BuildRtxEncorePresetsPanel(CogDialog cog, string gameName, string? cfgPath,
        IReadOnlyList<RtxEncorePresetSetting> settings, Action afterApply, Action reopen)
    {
        bool canEdit = cfgPath != null;
        var panel = new StackPanel { Spacing = 8 };
        var border = new Border
        {
            Background = UIFactory.GetBrush(MainWindow.OsPresetPanelBg),
            BorderBrush = UIFactory.GetBrush(MainWindow.OsPresetPanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 2, 0, 6),
            Child = panel,
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Presets",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        panel.Children.Add(MainWindow.MakeOsPresetLabel("Your presets (shared by all games)"));

        var slots = RtxEncorePresetService.Load();
        int n = RtxEncorePresetService.SlotCount;
        static string SlotLabel(RtxEncorePreset? p, int i) =>
            p == null ? $"Slot {i + 1} (empty)" : string.IsNullOrWhiteSpace(p.Name) ? $"Slot {i + 1}" : p.Name!;

        if (_rtxEncoreSelectedSlot < 0 || _rtxEncoreSelectedSlot >= n)
            _rtxEncoreSelectedSlot = Math.Max(0, Array.FindIndex(slots, s => s != null));

        var combo = CogDialog.MakeCombo(Enumerable.Range(0, n).Select(i => SlotLabel(slots[i], i)).ToList(), _rtxEncoreSelectedSlot);
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTipService.SetToolTip(combo, "Your own presets. Pick a slot, then Apply it to this game or save this game's settings into it.");

        var applyBtn = CogDialog.StyledButton("Apply", CogButtonKind.Primary, compact: true);
        var saveBtn  = CogDialog.StyledButton("Save current", CogButtonKind.Secondary, compact: true);
        var moreBtn  = CogDialog.StyledButton("...", CogButtonKind.Secondary, compact: true);
        ToolTipService.SetToolTip(moreBtn, "Rename or clear this slot");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(moreBtn, "Rename or clear this slot");

        int Selected() => Math.Clamp(combo.SelectedIndex, 0, n - 1);

        void Refresh()
        {
            bool has = slots[Selected()] != null;
            bool canApply = has && canEdit;
            applyBtn.IsEnabled = canApply;
            applyBtn.Opacity = canApply ? 1.0 : CogDialog.DisabledOpacity;
            saveBtn.IsEnabled = canEdit;
            saveBtn.Opacity = canEdit ? 1.0 : CogDialog.DisabledOpacity;
            saveBtn.Content = has ? "Overwrite" : "Save current";
            ToolTipService.SetToolTip(applyBtn, !canEdit ? "Install RTX Encore to apply a preset."
                : has ? "Apply this preset to this game" : "This slot is empty. Save this game's settings into it first.");
            ToolTipService.SetToolTip(saveBtn, !canEdit ? "Install RTX Encore to save its settings."
                : has ? "Replace this slot with this game's settings (keeps its name)" : "Save this game's settings into this slot");
        }
        void Relabel()
        {
            int i = combo.SelectedIndex;
            combo.ItemsSource = Enumerable.Range(0, n).Select(k => SlotLabel(slots[k], k)).ToList();
            combo.SelectedIndex = i;
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) _rtxEncoreSelectedSlot = combo.SelectedIndex;
            Refresh();
        };

        applyBtn.Click += (_, _) =>
        {
            int i = Selected();
            var p = slots[i];
            if (p == null || cfgPath == null) return;
            try
            {
                // Only the settings this ⚙ shows (a preset from an older RHI may hold fewer or more)
                var writes = settings
                    .Select(s => (s.Section, s.Key, Value: p.Values.TryGetValue(RtxEncorePresetService.KeyOf(s.Section, s.Key), out var v) ? v : null))
                    .Where(w => w.Value != null)
                    .Select(w => (w.Section, w.Key, RtxEncoreConfig.TokenFor(w.Value!)))
                    .ToList();
                RtxEncoreConfig.SetMany(cfgPath, writes);
                afterApply();
                CrashReporter.Log($"[RtxEncorePreset.Apply] '{SlotLabel(p, i)}' ({writes.Count} settings) applied to '{gameName}'");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                CrashReporter.Log($"[RtxEncorePreset.Apply] Slot {i + 1} — {ex.Message}");
                cog.SetError($"Couldn't apply the preset: {ex.Message}");
                return;
            }
            reopen();
        };

        saveBtn.Click += (_, _) =>
        {
            int i = Selected();
            if (cfgPath == null) return;
            try
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var s in settings)
                    values[RtxEncorePresetService.KeyOf(s.Section, s.Key)] = RtxEncoreConfig.Get(cfgPath, s.Section, s.Key) ?? s.Default;
                var preset = slots[i] ?? new RtxEncorePreset();
                preset.Values = values; // keeps the name
                slots[i] = preset;
                if (!RtxEncorePresetService.Save(slots))
                {
                    cog.SetError("Couldn't save the preset. See the log for details.");
                    return;
                }
                Relabel();
                Refresh();
                CrashReporter.Log($"[RtxEncorePreset.Save] '{gameName}' saved to slot {i + 1} ({values.Count} settings)");
                cog.SetSuccess($"Saved this game's settings to {SlotLabel(slots[i], i)}. Use ... to name it.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[RtxEncorePreset.Save] Slot {i + 1} — {ex.Message}");
                cog.SetError($"Couldn't save the preset: {ex.Message}");
            }
        };

        // "..." flyout: rename + clear
        var nameBox = CogDialog.MakeTextBox("", "Preset name");
        nameBox.MinWidth = 200;
        var renameBtn = CogDialog.StyledButton("Rename", CogButtonKind.Primary, compact: true);
        var clearBtn  = CogDialog.StyledButton("Clear slot", CogButtonKind.Danger, compact: true);
        var flyoutPanel = new StackPanel { Spacing = 8 };
        flyoutPanel.Children.Add(CogDialog.Inline(nameBox, renameBtn));
        flyoutPanel.Children.Add(clearBtn);
        var flyout = new Flyout { Content = flyoutPanel, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        flyout.Opening += (_, _) =>
        {
            int i = Selected();
            nameBox.Text = slots[i]?.Name ?? "";
            renameBtn.IsEnabled = slots[i] != null;
            clearBtn.IsEnabled = slots[i] != null;
        };
        moreBtn.Flyout = flyout;

        void Rename()
        {
            int i = Selected();
            if (slots[i] == null) return; // nothing saved here yet
            slots[i]!.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim();
            if (!RtxEncorePresetService.Save(slots)) cog.SetError("Couldn't save the preset. See the log for details.");
            Relabel();
            Refresh();
            flyout.Hide();
        }
        renameBtn.Click += (_, _) => Rename();
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true; // Enter must not close the cog
            Rename();
        };
        clearBtn.Click += (_, _) =>
        {
            int i = Selected();
            slots[i] = null;
            if (!RtxEncorePresetService.Save(slots)) cog.SetError("Couldn't save the preset. See the log for details.");
            Relabel();
            Refresh();
            flyout.Hide();
            cog.SetSuccess($"Cleared slot {i + 1}.");
        };

        Refresh();
        var row = CogDialog.Inline(combo, applyBtn, saveBtn, moreBtn);
        row.ColumnSpacing = 6;
        panel.Children.Add(row);
        panel.Children.Add(MainWindow.MakeOsPresetCaption(
            "Save stores every setting below. Apply writes them to this game's rtx-encore.jsonc; the Neural Rendering DLL and Smooth Motion's graphics API stay as they are for each game."));
        return border;
    }
}
