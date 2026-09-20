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
                Title = "❌ Ошибка чтения",
                Content = $"Не удалось прочитать файл: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        if (!PresetValidator.IsReShadePreset(content))
        {
            _crashReporter.Log($"[DragDropHandler.ProcessDroppedPreset] '{fileName}' is not a recognised ReShade preset");
            var errDialog = new ContentDialog
            {
                Title = "❌ Это не пресет ReShade",
                Content = "Этот файл не распознан как пресет ReShade. В корректном пресете есть строка Techniques= хотя бы с одной записью @.fx.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
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
                Title = "❌ Ошибка хранилища",
                Content = $"Не удалось сохранить пресет в папку пресетов: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        // ── Step 3: Game selection dialog ─────────────────────────────────────
        var cards = ViewModel.AllCards?.ToList() ?? new();
        if (cards.Count == 0)
        {
            var noGamesDialog = new ContentDialog
            {
                Title = "Нет доступных игр",
                Content = "Игры пока не обнаружены. Сначала добавьте игру.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(noGamesDialog);
            return;
        }

        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Выберите игру...",
        };

        var sortedCards = cards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var card in sortedCards)
            combo.Items.Add(new ComboBoxItem { Content = card.GameName, Tag = card });

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
            Text = $"Установите {fileName} в папку игры.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        });
        panel.Children.Add(combo);

        var pickDialog = new ContentDialog
        {
            Title = "🎨 Установить пресет ReShade",
            Content = panel,
            PrimaryButtonText = "Далее",
            CloseButtonText = "Отмена",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var pickResult = await DialogService.ShowSafeAsync(pickDialog);
        if (pickResult != ContentDialogResult.Primary) return;

        if (combo.SelectedItem is not ComboBoxItem selected || selected.Tag is not GameCardViewModel targetCard)
        {
            var noSelection = new ContentDialog
            {
                Title = "Игра не выбрана",
                Content = "Выберите игру для установки пресета.",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
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
                Title = "❌ Не удалось развернуть",
                Content = $"Не удалось скопировать пресет в папку игры: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(errDialog);
            return;
        }

        // ── Step 5: Shader confirmation dialog ───────────────────────────────
        var shaderDialog = new ContentDialog
        {
            Title = "🔧 Установить шейдеры?",
            Content = "Также установить необходимые шейдеры и текстуры?",
            PrimaryButtonText = "Да",
            CloseButtonText = "Нет",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

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
