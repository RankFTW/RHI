// MainWindow.Events.Components.OptiScaler.Presets.cs — Presets panel at the top of the OptiScaler cog:
// ready-made presets (optiscaler_presets.json) as buttons, and the 4 user slots in one dropdown.

using System;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using RenoDXCommander.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>The user slot last picked in the dropdown, kept while the app runs (the cog reopens after Apply).</summary>
    private static int _osSelectedSlot = -1;

    private const string OsPresetHint = "Start from a preset, then fine-tune the settings below. Hover a preset for what it does.";

    private FrameworkElement BuildOsPresetsPanel(OsCogContext ctx)
    {
        var card = ctx.Card;
        var panel = new StackPanel { Spacing = 10 };
        // Translucent white rather than the theme's navy card colours: the cog is neutral grey,
        // so this reads as a slightly lighter grey panel on it.
        var border = new Border
        {
            Background = UIFactory.GetBrush(OsPresetPanelBg),
            BorderBrush = UIFactory.GetBrush(OsPresetPanelBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 2, 0, 6),
            Child = panel,
        };

        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        void ShowStatus(string text, bool ok)
        {
            status.Text = text;
            status.Foreground = UIFactory.Brush(ok ? ResourceKeys.AccentGreenBrush : ResourceKeys.AccentRedBrush);
            status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Presets",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        // ── Ready-made presets ──────────────────────────────────────────────
        var manifestPresets = OsPresetService.ManifestPresets;
        var active = manifestPresets.FirstOrDefault(p => IsOsPresetActive(card, p));

        var description = MakeOsPresetCaption(active != null
            ? $"In use: {active.Name}. {active.Description}".TrimEnd()
            : OsPresetHint);
        string restingDescription = description.Text;

        var pills = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        foreach (var preset in manifestPresets)
        {
            var p = preset;
            bool isActive = ReferenceEquals(p, active);
            var pill = MakeOsPresetPill(p.Name ?? "Preset", isActive);
            ToolTipService.SetToolTip(pill, string.IsNullOrEmpty(p.Description) ? $"Apply {p.Name} to this game" : p.Description);
            pill.PointerEntered += (_, _) => { if (!string.IsNullOrEmpty(p.Description)) description.Text = $"{p.Name}: {p.Description}"; };
            pill.PointerExited  += (_, _) => description.Text = restingDescription;
            pill.Click += async (_, _) =>
            {
                try
                {
                    foreach (var b in pills.Children.OfType<Button>()) b.IsEnabled = false;
                    ShowStatus($"Applying {p.Name}...", true);
                    ApplyOsPreset(card, p);
                    if (p.InstallUniversalMfg == true)
                    {
                        ShowStatus($"Applying {p.Name}: installing Universal MFG Unlocker...", true);
                        var error = await ViewModel.EnsureUniversalMfgForPresetAsync(card);
                        RefreshExtrasIfSelected(card);
                        if (error != null)
                        {
                            ShowStatus($"{p.Name} settings applied, but: {error}", false);
                            foreach (var b in pills.Children.OfType<Button>()) b.IsEnabled = true;
                            return;
                        }
                    }
                    CrashReporter.Log($"[OsPreset.ApplyManifest] '{p.Name}' applied to '{card.GameName}'");
                    ReopenOsCog(ctx);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[OsPreset.ApplyManifest] '{p.Name}' — {ex.Message}");
                    ShowStatus($"Couldn't apply {p.Name}: {ex.Message}", false);
                    foreach (var b in pills.Children.OfType<Button>()) b.IsEnabled = true;
                }
            };
            pills.Children.Add(pill);
        }
        panel.Children.Add(MakeOsPresetLabel("Recommended"));
        panel.Children.Add(pills);
        panel.Children.Add(description);

        panel.Children.Add(new Border { Height = 1, Background = UIFactory.GetBrush(OsPresetPanelBorder) });

        // ── User slots ──────────────────────────────────────────────────────
        panel.Children.Add(MakeOsPresetLabel("Your presets (shared by all games)"));
        panel.Children.Add(BuildOsUserSlotsRow(ctx, ShowStatus));
        panel.Children.Add(status);
        return border;
    }

    private Grid BuildOsUserSlotsRow(OsCogContext ctx, Action<string, bool> showStatus)
    {
        var slots = OsPresetService.Load();
        static string SlotLabel(OsPreset? p, int i) =>
            p == null ? $"Slot {i + 1} (empty)" : string.IsNullOrWhiteSpace(p.Name) ? $"Slot {i + 1}" : p.Name!;

        if (_osSelectedSlot < 0 || _osSelectedSlot > 3)
            _osSelectedSlot = Math.Max(0, Array.FindIndex(slots, s => s != null));

        var combo = CogDialog.MakeCombo(Enumerable.Range(0, 4).Select(i => SlotLabel(slots[i], i)).ToList(), _osSelectedSlot);
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTipService.SetToolTip(combo, "Your own presets. Pick a slot, then Apply it to this game or save the current settings into it.");

        var applyBtn = CogDialog.StyledButton("Apply", CogButtonKind.Primary, compact: true);
        var saveBtn  = CogDialog.StyledButton("Save current", CogButtonKind.Secondary, compact: true);
        var moreBtn  = CogDialog.StyledButton("...", CogButtonKind.Secondary, compact: true);
        ToolTipService.SetToolTip(moreBtn, "Rename or clear this slot");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(moreBtn, "Rename or clear this slot");

        void Refresh()
        {
            int i = Math.Clamp(combo.SelectedIndex, 0, 3);
            bool has = slots[i] != null;
            applyBtn.IsEnabled = has;
            applyBtn.Opacity = has ? 1.0 : CogDialog.DisabledOpacity;
            saveBtn.Content = has ? "Overwrite" : "Save current";
            ToolTipService.SetToolTip(applyBtn, has ? "Apply this preset to this game" : "This slot is empty. Save the current settings into it first.");
            ToolTipService.SetToolTip(saveBtn, has
                ? "Replace this slot with the current settings (keeps its name)"
                : "Save the current settings into this slot");
        }
        void RelabelSelected()
        {
            int i = combo.SelectedIndex;
            var items = Enumerable.Range(0, 4).Select(n => SlotLabel(slots[n], n)).ToList();
            combo.ItemsSource = items;
            combo.SelectedIndex = i;
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0) _osSelectedSlot = combo.SelectedIndex;
            Refresh();
        };

        applyBtn.Click += (_, _) =>
        {
            int i = Math.Clamp(combo.SelectedIndex, 0, 3);
            var p = slots[i];
            if (p == null) return;
            try { ApplyOsPreset(ctx.Card, p); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OsPreset.Apply] Slot {i + 1} — {ex.Message}");
                showStatus($"Couldn't apply the preset: {ex.Message}", false);
                return;
            }
            ReopenOsCog(ctx);
        };

        saveBtn.Click += (_, _) =>
        {
            int i = Math.Clamp(combo.SelectedIndex, 0, 3);
            try
            {
                slots[i] ??= new OsPreset();
                CaptureOsPreset(ctx, slots[i]!); // keeps the name
                OsPresetService.Save(slots);
                RelabelSelected();
                Refresh();
                showStatus($"Saved the current settings to {SlotLabel(slots[i], i)}.", true);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OsPreset.Save] Slot {i + 1} — {ex.Message}");
                showStatus($"Couldn't save the preset: {ex.Message}", false);
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
            int i = Math.Clamp(combo.SelectedIndex, 0, 3);
            nameBox.Text = slots[i]?.Name ?? "";
            clearBtn.IsEnabled = slots[i] != null;
        };
        moreBtn.Flyout = flyout;

        void Rename()
        {
            int i = Math.Clamp(combo.SelectedIndex, 0, 3);
            slots[i] ??= new OsPreset();
            slots[i]!.Name = string.IsNullOrWhiteSpace(nameBox.Text) ? null : nameBox.Text.Trim();
            OsPresetService.Save(slots);
            RelabelSelected();
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
            int i = Math.Clamp(combo.SelectedIndex, 0, 3);
            slots[i] = null;
            OsPresetService.Save(slots);
            RelabelSelected();
            Refresh();
            flyout.Hide();
            showStatus($"Cleared slot {i + 1}.", true);
        };

        Refresh();
        var row = CogDialog.Inline(combo, applyBtn, saveBtn, moreBtn);
        row.ColumnSpacing = 6;
        return row;
    }

    /// <summary>True when the game's current settings match every frame generation setting the preset sets.</summary>
    private bool IsOsPresetActive(GameCardViewModel card, OsPreset p)
    {
        if (!card.IsOsInstalled || string.IsNullOrEmpty(card.InstallPath)) return false;
        var store = card.Source ?? "";
        static bool Same(string? want, string? have, string fallback) =>
            want == null || string.Equals(want, string.IsNullOrEmpty(have) || have.Equals("auto", StringComparison.OrdinalIgnoreCase) ? fallback : have,
                StringComparison.OrdinalIgnoreCase);

        if (!Same(p.FgEnabled, ReadOsIniSectionValue(card.InstallPath, "FrameGen", "Enabled", ""), "false")) return false;
        if (!Same(p.FgInput, ViewModel.GetOsFgInput(card.GameName, store), "auto")) return false;
        if (!Same(p.FgOutput, ViewModel.GetOsFgOutput(card.GameName, store), "auto")) return false;
        if (string.Equals(p.FgOutput, "dlssg", StringComparison.OrdinalIgnoreCase)
            && !Same(p.FgNvngxReplacement, ViewModel.GetOsFgNvngxReplacement(card.GameName, store), "None")) return false;
        if (!Same(p.HudFix, ReadOsIniSectionValue(card.InstallPath, "OptiFG", "HUDFix", ""), "auto")) return false;
        if (!Same(p.ForceReflex, ReadOsIniSectionValue(card.InstallPath, "fakenvapi", "ForceReflex", ""), "0")) return false;
        if (!Same(p.UseGamesReflexMarkers, ReadOsIniSectionValue(card.InstallPath, "DLSSG", "UseGamesReflexMarkers", ""), "true")) return false;
        if (p.DeployStreamline.HasValue && p.DeployStreamline.Value != ViewModel.GetOsDeployStreamline(card.GameName, store)) return false;
        if (p.InstallUniversalMfg == true && !card.Rtx40MfgInstalled) return false;
        return true;
    }

    /// <summary>Rebuilds the Extras section if this game is on screen (Universal MFG Unlocker row changed).</summary>
    private void RefreshExtrasIfSelected(GameCardViewModel card) => _detailPanelBuilder.RequestExtrasRebuild(card);

    private static Button MakeOsPresetPill(string name, bool isActive)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (isActive)
            content.Children.Add(new FontIcon { Glyph = "", FontSize = 11 });
        content.Children.Add(new TextBlock { Text = name, FontSize = 12 });
        return new Button
        {
            Content = content,
            Height = 30,
            Padding = new Thickness(14, 0, 14, 0),
            CornerRadius = new CornerRadius(15),
            BorderThickness = new Thickness(1),
            Background  = isActive ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.GetBrush(OsPresetPillBg),
            Foreground  = UIFactory.Brush(isActive ? ResourceKeys.AccentBlueBrush : ResourceKeys.TextPrimaryBrush),
            BorderBrush = isActive ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.GetBrush(OsPresetPillBorder),
        };
    }

    // Grey layers for the presets panel (white at low opacity over the cog's grey)
    private const string OsPresetPanelBg     = "#0AFFFFFF";
    private const string OsPresetPanelBorder = "#1AFFFFFF";
    private const string OsPresetPillBg      = "#12FFFFFF";
    private const string OsPresetPillBorder  = "#26FFFFFF";

    private static TextBlock MakeOsPresetLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
    };

    private static TextBlock MakeOsPresetCaption(string text) => new()
    {
        Text = text,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
    };
}
