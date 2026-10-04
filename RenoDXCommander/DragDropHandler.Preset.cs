// DragDropHandler.Preset.cs — Preset drop processing: validate, store, game selection, deploy, shader confirmation.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DragDropHandler
{
    /// <summary>
    /// Processes a dropped .ini file: validate → store → game selection → deploy → shader confirmation.
    /// </summary>
    public async Task ProcessDroppedPreset(string iniPath)
    {
        var fileName = Path.GetFileName(iniPath);
        _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] Received '{fileName}'");

        // ── Step 1: Read and validate ─────────────────────────────────────────
        string content;
        try
        {
            content = File.ReadAllText(iniPath);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] Failed to read '{iniPath}' — {ex.Message}");
            var errDialog = new ContentDialog
            {
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            }.Localize("Title", Loc.Get("❌ Read Error")).Localize("Content", Loc.Format($"Failed to read the file: {ex.Message}")).Localize("CloseButtonText", Loc.Get("OK"));
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        if (!PresetValidator.IsReShadePreset(content))
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] '{fileName}' is not a recognised ReShade preset");
            var errDialog = new ContentDialog
            {
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            }.Localize("Title", Loc.Get("❌ Not a ReShade Preset")).Localize("Content", Loc.Get("This file is not a recognised ReShade preset. A valid preset must contain a Techniques= line with at least one @.fx entry.")).Localize("CloseButtonText", Loc.Get("OK"));
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        // ── Step 2: Copy to presets folder ────────────────────────────────────
        try
        {
            Directory.CreateDirectory(PresetPopupHelper.PresetsDir);
            var destPreset = Path.Combine(PresetPopupHelper.PresetsDir, fileName);
            File.Copy(iniPath, destPreset, overwrite: true);
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] Stored '{fileName}' in presets folder");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] Failed to copy to presets folder — {ex.Message}");
            var errDialog = new ContentDialog
            {
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            }.Localize("Title", Loc.Get("❌ Storage Error")).Localize("Content", Loc.Format($"Failed to save preset to the presets folder: {ex.Message}")).Localize("CloseButtonText", Loc.Get("OK"));
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        // ── Step 3: Game selection dialog ─────────────────────────────────────
        var cards = ViewModel.AllCards?.ToList() ?? new();
        if (cards.Count == 0)
        {
            var noGamesDialog = new ContentDialog
            {
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            }.Localize("Title", Loc.Get("No Games Available")).Localize("Content", Loc.Get("No games are currently detected. Add a game first.")).Localize("CloseButtonText", Loc.Get("OK"));
            await DialogService.ShowSafeAsync(noGamesDialog);
            return;
        }

        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = Loc.Get("Select a game..."),
            ItemTemplate = null,
        };

        var sortedCards = cards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var card in sortedCards)
            combo.Items.Add(new ComboBoxItem { ContentTemplate = null, Content = card.GameName, Tag = card });

        // Auto-select the currently selected game in the sidebar
        if (ViewModel.SelectedGame != null)
        {
            for (int i = 0; i < sortedCards.Count; i++)
            {
                if (string.Equals(sortedCards[i].GameName, ViewModel.SelectedGame.GameName, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush)
        }.Localize("Text", Loc.Format($"Install {fileName} to a game folder.")));
        panel.Children.Add(combo);

        var pickDialog = new ContentDialog
        {
            Content = panel,
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark
        }.Localize("Title", Loc.Get("🎨 Install ReShade Preset")).Localize("PrimaryButtonText", Loc.Get("Next")).Localize("CloseButtonText", Loc.Get("Cancel"));

        var pickResult = await DialogService.ShowSafeAsync(pickDialog);
        if (pickResult != ContentDialogResult.Primary) return;

        if (combo.SelectedItem is not ComboBoxItem selected || selected.Tag is not GameCardViewModel targetCard)
        {
            var noSelection = new ContentDialog
            {
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            }.Localize("Title", Loc.Get("No Game Selected")).Localize("Content", Loc.Get("Please select a game to install the preset to.")).Localize("CloseButtonText", Loc.Get("OK"));
            await DialogService.ShowSafeAsync(noSelection);
            return;
        }

        var gameName = targetCard.GameName;
        var installPath = targetCard.InstallPath;

        // ── Step 4: Copy preset to game folder ───────────────────────────────
        try
        {
            var destGame = Path.Combine(installPath, fileName);
            File.Copy(iniPath, destGame, overwrite: true);
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] Deployed '{fileName}' to '{installPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] Failed to deploy preset — {ex.Message}");
            var errDialog = new ContentDialog
            {
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark
            }.Localize("Title", Loc.Get("❌ Deploy Failed")).Localize("Content", Loc.Format($"Failed to copy preset to game folder: {ex.Message}")).Localize("CloseButtonText", Loc.Get("OK"));
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        // ── Step 5: Shader confirmation dialog ───────────────────────────────
        var shaderDialog = new ContentDialog
        {
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark
        }.Localize("Title", Loc.Get("🔧 Install Shaders?")).Localize("Content", Loc.Get("Also install the required shaders and textures?")).Localize("PrimaryButtonText", Loc.Get("Yes")).Localize("CloseButtonText", Loc.Get("No"));

        var shaderResult = await DialogService.ShowSafeAsync(shaderDialog);
        if (shaderResult == ContentDialogResult.Primary)
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] User chose to install shaders for '{gameName}'");
            await ViewModel.ApplyPresetShadersAsync(gameName, new[] { iniPath }, targetCard.Source ?? "");

            // Rebuild overrides panel so the shader toggle reflects the new "Select" mode
            if (ViewModel.SelectedGame is { } selectedCard
                && selectedCard.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase))
            {
                _window.BuildOverridesPanel(selectedCard);
            }
        }
        else
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] User declined shader install for '{gameName}'");
        }
    }
}
