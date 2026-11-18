// MainWindow.Collections.Bulk.cs -- several games at once: the Classic / Tabs game list allows Ctrl / Shift
// click (ListView Extended mode; plain click still picks one game and the detail page follows
// SelectedItem as before). Picked games are highlighted through GameCardViewModel.IsMultiSelected
// (the list's own selected background is transparent). Right-clicking one of them opens a menu for
// all of them: add to / remove from a collection, new collection with them, Manage. Spotlight's covers
// stay single (bulk work there goes through the Manage window).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private readonly List<GameCardViewModel> _multiSelected = new();

    /// <summary>Once, after GameList gets its items: Ctrl / Shift click picks several games.</summary>
    private void EnableGameListMultiSelect()
    {
        try { GameList.SelectionMode = ListViewSelectionMode.Extended; }
        catch (Exception ex) { CrashReporter.Log($"[MainWindow.EnableGameListMultiSelect] {ex.Message}"); }
    }

    /// <summary>Called on every GameList selection change: highlights the picked games when there are several.</summary>
    private void SyncMultiSelection()
    {
        try
        {
            var now = GameList.SelectedItems.Count > 1
                ? GameList.SelectedItems.OfType<GameCardViewModel>().ToList()
                : new List<GameCardViewModel>();
            foreach (var c in _multiSelected)
                if (!now.Contains(c)) c.IsMultiSelected = false;
            foreach (var c in now) c.IsMultiSelected = true;
            _multiSelected.Clear();
            _multiSelected.AddRange(now);
        }
        catch (Exception ex) { CrashReporter.Log($"[MainWindow.SyncMultiSelection] {ex.Message}"); }
    }

    /// <summary>The picked games when the right-clicked one is among several picked ones, else null.</summary>
    private List<GameCardViewModel>? MultiSelectionFor(GameCardViewModel card) =>
        !_spotlightOn && _multiSelected.Count > 1 && _multiSelected.Contains(card) ? _multiSelected.ToList() : null;

    /// <summary>Back to one game: the one the detail page shows.</summary>
    private void ClearMultiSelection()
    {
        try
        {
            var keep = ViewModel.SelectedGame;
            GameList.SelectedItems.Clear();
            if (keep != null && ViewModel.DisplayedGames.Contains(keep)) GameList.SelectedItem = keep;
        }
        catch (Exception ex) { CrashReporter.Log($"[MainWindow.ClearMultiSelection] {ex.Message}"); }
    }

    private void ShowBulkGameMenu(FrameworkElement anchor, List<GameCardViewModel> games)
    {
        try
        {
            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
            menu.Items.Add(new MenuFlyoutItem { Text = $"{games.Count} games selected", IsEnabled = false });
            menu.Items.Add(new MenuFlyoutSeparator());

            var keys = games.Select(MainViewModel.CollectionKeyOf).ToList();
            var names = ViewModel.CollectionNames;

            // Add to collection: one item per collection (adds every picked game), then New collection…
            var add = new MenuFlyoutSubItem { Text = "Add to collection" };
            foreach (var name in names)
            {
                var members = ViewModel.CollectionStore.GetMembers(name);
                int missing = keys.Count(k => members == null || !members.Contains(k));
                string n = name;
                var item = new MenuFlyoutItem
                {
                    Text = missing == 0 ? $"{name} (all already in it)" : missing == games.Count ? name : $"{name} ({missing} not in it yet)",
                    IsEnabled = missing > 0,
                };
                item.Click += (s, e) =>
                {
                    int changed = ViewModel.SetCollectionMembership(n, keys, true);
                    ShowStatusBarMessage($"Added {changed} {(changed == 1 ? "game" : "games")} to {n}.", TimeSpan.FromSeconds(5));
                    UpdateCollectionEmptyText();
                };
                add.Items.Add(item);
            }
            if (names.Count > 0) add.Items.Add(new MenuFlyoutSeparator());
            var create = new MenuFlyoutItem { Text = "New collection…" };
            create.Click += async (s, e) =>
            {
                try { await NewCollectionAsync(anchor.XamlRoot ?? Content?.XamlRoot, games); }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.BulkNewCollection_Click] {ex.Message}"); }
            };
            add.Items.Add(create);
            menu.Items.Add(add);

            // Remove from collection: only collections that hold at least one of them
            var remove = new MenuFlyoutSubItem { Text = "Remove from collection" };
            foreach (var name in names)
            {
                var members = ViewModel.CollectionStore.GetMembers(name);
                int inIt = members == null ? 0 : keys.Count(members.Contains);
                if (inIt == 0) continue;
                string n = name;
                var item = new MenuFlyoutItem { Text = inIt == games.Count ? name : $"{name} ({inIt} in it)" };
                item.Click += (s, e) =>
                {
                    int changed = ViewModel.SetCollectionMembership(n, keys, false);
                    ShowStatusBarMessage($"Removed {changed} {(changed == 1 ? "game" : "games")} from {n}.", TimeSpan.FromSeconds(5));
                    UpdateCollectionEmptyText();
                };
                remove.Items.Add(item);
            }
            if (remove.Items.Count > 0) menu.Items.Add(remove);

            var manage = new MenuFlyoutItem { Text = "Manage collections…" };
            manage.Click += async (s, e) =>
            {
                try { await ShowCollectionManagerAsync(anchor.XamlRoot ?? Content?.XamlRoot, null); }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.BulkManage_Click] {ex.Message}"); }
            };
            menu.Items.Add(manage);

            menu.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = "Clear selection" };
            clear.Click += (s, e) => ClearMultiSelection();
            menu.Items.Add(clear);

            menu.ShowAt(anchor);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShowBulkGameMenu] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Opens the Manage collections window, then brings the chips and the list up to date.</summary>
    private async Task ShowCollectionManagerAsync(XamlRoot? xamlRoot, string? select)
    {
        if (xamlRoot == null) return;
        bool pad = _padHints?.Visibility == Visibility.Visible;
        var dialog = new CollectionManagerDialog(ViewModel, xamlRoot, select ?? ViewModel.ActiveCollectionName, pad,
            pad ? new Action<Control>(PadFocus) : c => c.Focus(FocusState.Programmatic));
        await dialog.ShowAsync();
        RefreshFilterButtonStyles();
        RefreshTabsChrome();
        UpdateCollectionEmptyText();
    }
}
