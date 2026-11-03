// SettingsHandler.RemovedGames.cs -- "Removed games" button in Settings › Tools & Maintenance › Troubleshooting:
// lists games taken out with Management › Remove from RHI and puts them back.
// Built in C# (XAML constraint), added beside Full refresh the first time Settings opens.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private Button? _removedGamesBtn;

    private void EnsureRemovedGamesButton()
    {
        if (_removedGamesBtn != null)
        {
            UpdateRemovedGamesButton();
            return;
        }
        try
        {
            // Searched down from the cards panel: FrameworkElement.Parent can be null on the first open
            var host = FindPanelContaining(_window.SettingsCardsPanel, _window.FullRefreshBtn);
            if (host == null)
            {
                CrashReporter.Log("[SettingsHandler.EnsureRemovedGamesButton] Troubleshooting panel not found — button not added");
                return;
            }
            _removedGamesBtn = new Button { Style = FindStyle("SettingsButtonStyle") };
            if (Application.Current.Resources.TryGetValue("SettingsLabelBrush", out var brush) && brush is Microsoft.UI.Xaml.Media.Brush b)
                _removedGamesBtn.Foreground = b;
            ToolTipService.SetToolTip(_removedGamesBtn,
                "Games you took out with Management › Remove from RHI. Open the list to put one back.");
            _removedGamesBtn.Click += async (s, e) =>
            {
                try { await ShowRemovedGamesDialogAsync(); }
                catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.RemovedGames] Dialog failed — {ex.Message}"); }
            };
            host.Children.Add(_removedGamesBtn);
            UpdateRemovedGamesButton();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureRemovedGamesButton] Failed — {ex.Message}");
        }
    }

    /// <summary>Finds the Panel that directly holds <paramref name="target"/>, walking Panel children and Border children.</summary>
    private static Panel? FindPanelContaining(UIElement root, UIElement target)
    {
        if (root is Panel p)
        {
            foreach (var child in p.Children)
                if (ReferenceEquals(child, target)) return p;
            foreach (var child in p.Children)
                if (FindPanelContaining(child, target) is Panel found) return found;
        }
        else if (root is Border { Child: UIElement inner })
            return FindPanelContaining(inner, target);
        return null;
    }

    private void UpdateRemovedGamesButton()
    {
        if (_removedGamesBtn == null) return;
        int n = ViewModel.RemovedGameList.Count;
        _removedGamesBtn.Content = n > 0 ? $"Removed games ({n})" : "Removed games";
    }

    private async Task ShowRemovedGamesDialogAsync()
    {
        var list = new StackPanel { Spacing = 6 };
        var empty = new TextBlock
        {
            Text = "No games have been removed. Use Remove from RHI on a game's Management section to take it out of the list.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        var note = new TextBlock
        {
            Text = "Restore puts the game back in your list.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        bool needsRefresh = false;

        void Fill()
        {
            list.Children.Clear();
            var games = ViewModel.RemovedGameList;
            note.Visibility = games.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (games.Count == 0) { list.Children.Add(empty); return; }
            foreach (var (key, name, store) in games)
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                });
                if (!string.IsNullOrEmpty(store))
                    text.Children.Add(new TextBlock
                    {
                        Text = store,
                        FontSize = 11,
                        Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                    });
                row.Children.Add(text);
                var restore = new Button { Content = "Restore", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                restore.Click += (s, e) =>
                {
                    try
                    {
                        if (!ViewModel.RestoreRemovedGame(key)) needsRefresh = true;
                        Fill();
                        UpdateRemovedGamesButton();
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[SettingsHandler.RemovedGames] Restore '{key}' failed — {ex.Message}");
                    }
                };
                Grid.SetColumn(restore, 1);
                row.Children.Add(restore);
                list.Children.Add(row);
            }
        }
        Fill();

        var content = new StackPanel();
        content.Children.Add(note);
        content.Children.Add(new ScrollViewer { Content = list, MaxHeight = 380, Padding = new Thickness(0, 0, 14, 0) });

        var dialog = new ContentDialog
        {
            Title = "Removed games",
            Content = content,
            CloseButtonText = "Close",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);

        if (needsRefresh)
        {
            // A game removed in an earlier session that wasn't found yet this session: a refresh brings it back
            CrashReporter.Log("[SettingsHandler.RemovedGames] Restored game(s) not in this session's scan — refreshing");
            await _window.RefreshWithScrollRestore();
        }
    }
}
