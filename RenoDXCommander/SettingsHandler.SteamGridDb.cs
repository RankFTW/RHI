// SettingsHandler.SteamGridDb.cs -- the SteamGridDB card in Settings › Accounts & Data: the user's own API key for
// browsing community covers and banners in Spotlight (game menu › Artwork › Browse...). Built in C# (XAML
// constraint) and added after the Accounts section's existing card. The key lives in
// %LocalAppData%\RHI\steamgriddb_api.txt (SteamGridDbService), not settings.json.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private TextBox? _sgdbKeyBox;
    private TextBlock? _sgdbStatus;
    private Button? _sgdbSaveBtn;
    private Button? _sgdbClearBtn;
    private bool _sgdbCardFailed;
    private bool _sgdbValidating;

    /// <summary>Opens Settings at a section (e.g. "Accounts"), for links from elsewhere in RHI.</summary>
    internal void OpenAtSection(string key)
    {
        try
        {
            // Opening restores the last section, so point that at the wanted one first
            ViewModel.Settings.SettingsLastSection = key;
            SettingsButton_Click(_window, new RoutedEventArgs());
            _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => JumpToSection(key, animate: false, saveAsLast: true));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.OpenAtSection] {key}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void EnsureSteamGridDbCard()
    {
        if (_sgdbKeyBox != null) { RefreshSteamGridDbStatus(); return; }
        if (_sgdbCardFailed) return;
        try
        {
            StackPanel? section = null;
            foreach (var child in _window.SettingsCardsPanel.Children)
                if (child is StackPanel sp && sp.Tag is string t && t == "Section:Accounts") { section = sp; break; }
            if (section == null)
            {
                _sgdbCardFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureSteamGridDbCard] Accounts section not found — card not added");
                return;
            }

            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var body = new StackPanel { Spacing = 9 };
            card.Child = body;
            body.Children.Add(new TextBlock { Text = "Artwork", Style = FindStyle("SettingsCardTitleStyle") });
            body.Children.Add(new TextBlock
            {
                Text = "With your own free SteamGridDB API key you can browse community covers and banners for your games "
                     + "in the Spotlight layout (right-click a game › Artwork › Browse covers / Browse banners).",
                Style = FindStyle("SettingsNoteStyle"),
                TextWrapping = TextWrapping.Wrap,
            });

            // Setting row: label | [key box | Save]
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
            row.Children.Add(new TextBlock
            {
                Text = "SteamGridDB API key",
                Style = FindStyle("SettingsLabelStyle"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var inner = new Grid { ColumnSpacing = 6 };
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _sgdbKeyBox = new TextBox { Style = FindStyle("SettingsTextBoxStyle"), IsSpellCheckEnabled = false };
            ToolTipService.SetToolTip(_sgdbKeyBox,
                "Paste your SteamGridDB API key, then press Enter or Save. The saved key isn't shown again; "
              + "paste a new one to replace it.");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_sgdbKeyBox, "SteamGridDB API key");
            _sgdbKeyBox.KeyDown += (s, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter) return;
                e.Handled = true;
                _ = SaveSteamGridDbKeyAsync(fromButton: false);
            };
            _sgdbKeyBox.LostFocus += (s, e) => _ = SaveSteamGridDbKeyAsync(fromButton: false);
            inner.Children.Add(_sgdbKeyBox);
            _sgdbSaveBtn = MakeSgdbButton("Save", "SettingsButtonStyle", ResourceKeys.SettingsLabelBrush, "Check the key with SteamGridDB and save it.");
            _sgdbSaveBtn.Click += (s, e) => _ = SaveSteamGridDbKeyAsync(fromButton: true);
            Grid.SetColumn(_sgdbSaveBtn, 1);
            inner.Children.Add(_sgdbSaveBtn);
            Grid.SetColumn(inner, 1);
            row.Children.Add(inner);
            body.Children.Add(row);

            _sgdbStatus = new TextBlock { Style = FindStyle("SettingsNoteStyle"), TextWrapping = TextWrapping.Wrap };
            body.Children.Add(_sgdbStatus);

            var buttons = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            var link = new HyperlinkButton
            {
                Content = "Get a free API key",
                NavigateUri = new Uri(SteamGridDbService.ApiKeyPage),
                FontSize = 12,
                MinHeight = 30,
                Padding = new Thickness(12, 5, 12, 5),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            };
            ToolTipService.SetToolTip(link, "Opens steamgriddb.com. Sign in (with Steam), then generate a key under Preferences › API and paste it above.");
            buttons.Children.Add(link);
            _sgdbClearBtn = MakeSgdbButton("Remove", "SettingsDangerButtonStyle", ResourceKeys.AccentRedBrush, "Remove the saved SteamGridDB key.");
            _sgdbClearBtn.Click += (s, e) =>
            {
                SteamGridDbService.ClearKey();
                if (_sgdbKeyBox != null) _sgdbKeyBox.Text = "";
                RefreshSteamGridDbStatus();
                SetSgdbStatus("Key removed.", ResourceKeys.TextTertiaryBrush);
            };
            buttons.Children.Add(_sgdbClearBtn);
            body.Children.Add(buttons);

            // After the existing Accounts card(s): the section's last child
            section.Children.Add(card);
            RefreshSteamGridDbStatus();
        }
        catch (Exception ex)
        {
            _sgdbCardFailed = true;
            _sgdbKeyBox = null;
            CrashReporter.Log($"[SettingsHandler.EnsureSteamGridDbCard] Failed — {ex.Message}");
        }
    }

    private static Button MakeSgdbButton(string text, string style, string brushKey, string tooltip)
    {
        var btn = new Button { Content = text, Style = FindStyle(style) };
        if (Application.Current.Resources.TryGetValue(brushKey, out var v) && v is Brush brush)
        {
            btn.Foreground = brush;
            // Keep the text colour on hover / press (WinUI hover colour sticks otherwise)
            btn.Resources["ButtonForegroundPointerOver"] = brush;
            btn.Resources["ButtonForegroundPressed"] = brush;
        }
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    private void SetSgdbStatus(string text, string brushKey)
    {
        if (_sgdbStatus == null) return;
        _sgdbStatus.Text = text;
        _sgdbStatus.Foreground = UIFactory.Brush(brushKey);
        _sgdbStatus.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshSteamGridDbStatus()
    {
        if (_sgdbKeyBox == null) return;
        var key = SteamGridDbService.ApiKey;
        _sgdbKeyBox.PlaceholderText = key == null ? "Paste your key" : $"Saved (ends in {Tail(key)})";
        if (_sgdbClearBtn != null) _sgdbClearBtn.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
        if (_sgdbValidating) return;
        if (key == null) SetSgdbStatus("No key saved. Spotlight's artwork picker shows only the Steam art.", ResourceKeys.TextTertiaryBrush);
        else SetSgdbStatus("Key saved. Browse covers and banners from a game's Artwork menu in Spotlight.", ResourceKeys.AccentGreenBrush);
    }

    private static string Tail(string key) => key.Length <= 4 ? key : key[^4..];

    /// <summary>Checks the typed key with SteamGridDB and saves it (Enter, leaving the box, or Save).</summary>
    private async Task SaveSteamGridDbKeyAsync(bool fromButton)
    {
        if (_sgdbKeyBox == null || _sgdbValidating) return;
        try
        {
            var typed = _sgdbKeyBox.Text?.Trim() ?? "";
            if (typed.Length == 0)
            {
                if (fromButton) SetSgdbStatus("Paste a key first.", ResourceKeys.AccentAmberDimBrush);
                return;
            }
            if (string.Equals(typed, SteamGridDbService.ApiKey, StringComparison.Ordinal))
            {
                _sgdbKeyBox.Text = "";
                RefreshSteamGridDbStatus();
                return;
            }

            _sgdbValidating = true;
            if (_sgdbSaveBtn != null) { _sgdbSaveBtn.IsEnabled = false; _sgdbSaveBtn.Content = "Checking..."; }
            SetSgdbStatus("Checking the key with SteamGridDB...", ResourceKeys.TextTertiaryBrush);
            var token = _window.LifetimeToken;
            var status = await Task.Run(() => SteamGridDbService.ValidateKeyAsync(typed, token));
            if (_window.IsShuttingDown) return;
            _sgdbValidating = false;
            if (_sgdbSaveBtn != null) { _sgdbSaveBtn.IsEnabled = true; _sgdbSaveBtn.Content = "Save"; }

            switch (status)
            {
                case SteamGridDbService.Status.Ok:
                    if (SteamGridDbService.SaveKey(typed) is { } err)
                    {
                        SetSgdbStatus($"Couldn't save the key: {err}", ResourceKeys.AccentRedBrush);
                        return;
                    }
                    _sgdbKeyBox.Text = "";
                    RefreshSteamGridDbStatus();
                    break;
                case SteamGridDbService.Status.BadKey:
                    SetSgdbStatus("SteamGridDB didn't accept this key. Check you copied all of it.", ResourceKeys.AccentRedBrush);
                    break;
                case SteamGridDbService.Status.Offline:
                    SetSgdbStatus("Couldn't reach SteamGridDB to check the key. Check your connection and try again.", ResourceKeys.AccentAmberDimBrush);
                    break;
                default:
                    SetSgdbStatus("SteamGridDB sent an unexpected reply. Try again later.", ResourceKeys.AccentAmberDimBrush);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.SaveSteamGridDbKeyAsync] {ex.GetType().Name}: {ex.Message}");
            SetSgdbStatus("Couldn't check the key.", ResourceKeys.AccentRedBrush);
        }
        finally
        {
            _sgdbValidating = false;
            if (_sgdbSaveBtn != null && !_window.IsShuttingDown) { _sgdbSaveBtn.IsEnabled = true; _sgdbSaveBtn.Content = "Save"; }
        }
    }
}
