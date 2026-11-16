// MainViewModel.BfiPresenter.cs — BFI Presenter per-game records, updates (DX add-ons in games, the shared Vulkan
// layer) and the one-time move from RHI's add-on list (where it could be enabled for every game) to Extras.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private const string BfiAddonPackName = "BFI Presenter";

    public string? GetBfiPresenterInstalledAs(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.BfiPresenterInstalledAs.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        return null;
    }

    public void SetBfiPresenterInstalledAs(string gameName, string? relPath, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (!string.IsNullOrEmpty(relPath)) _gameNameService.BfiPresenterInstalledAs[key] = relPath;
        else _gameNameService.BfiPresenterInstalledAs.Remove(key);
        SaveNameMappings();
    }

    /// <summary>
    /// BFI Presenter used to be in the add-on list, where enabling it for all games strobed every ReShade game.
    /// Each copy the add-on list put in a game becomes an Extras install (same file, kept in place), and the name
    /// leaves the add-on selections. Runs before the add-on sync on both init paths: the sync deletes tracked
    /// add-ons that are no longer selected, so the copies are untracked first. Idempotent.
    /// </summary>
    private void MigrateBfiPresenterAddonPack()
    {
        try
        {
            bool settingsChanged = _settingsViewModel.EnabledGlobalAddons.RemoveAll(
                n => n.Equals(BfiAddonPackName, StringComparison.OrdinalIgnoreCase)) > 0;
            foreach (var sel in _gameNameService.PerGameAddonSelection.Values)
                settingsChanged |= sel.RemoveAll(n => n.Equals(BfiAddonPackName, StringComparison.OrdinalIgnoreCase)) > 0;

            int moved = 0;
            foreach (var c in _allCards.ToList())
            {
                if (string.IsNullOrEmpty(c.InstallPath)) continue;
                var dir = ModInstallService.GetAddonDeployPath(c.InstallPath);
                foreach (var name in new[] { BfiPresenterService.Addon64, BfiPresenterService.Addon32 })
                {
                    if (!AddonPackService.IsAddonTrackedInDeployments(dir, name)) continue;
                    AddonPackService.UntrackAddonDeployment(dir, name);
                    var full = Path.Combine(dir, name);
                    if (!File.Exists(full) || GetBfiPresenterInstalledAs(c.GameName, c.Source ?? "") != null) continue;
                    _gameNameService.BfiPresenterInstalledAs[GameKey.From(c.GameName, c.Source ?? "").ToKey()] =
                        Path.GetRelativePath(c.InstallPath, full);
                    settingsChanged = true;
                    moved++;
                }
            }
            if (settingsChanged)
            {
                SaveNameMappings();
                _crashReporter.Log($"[MainViewModel.MigrateBfiPresenterAddonPack] Moved {moved} game(s) from the add-on list to Extras");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _crashReporter.Log($"[MainViewModel.MigrateBfiPresenterAddonPack] {ex.Message}");
        }
    }

    /// <summary>
    /// Brings BFI Presenter up to the latest release where RHI installed it: the DX add-on in each game (compared with
    /// the staged file, so games that were running last time are caught up) and the shared Vulkan layer, and keeps
    /// the layer listed after ReShade. Does nothing (no network) when no game has it. Background thread.
    /// </summary>
    internal async Task UpdateBfiPresenterInstallsAsync()
    {
        var installs = _allCards.ToList()
            .Where(c => !c.IsHidden && !string.IsNullOrEmpty(c.InstallPath))
            .Select(c => (Card: c, As: GetBfiPresenterInstalledAs(c.GameName, c.Source ?? "")))
            .Where(x => x.As != null && BfiPresenterService.IsInstalledIn(x.Card.InstallPath, x.As))
            .ToList();
        if (installs.Count == 0) return;

        var svc = App.Services.GetRequiredService<BfiPresenterService>();
        try
        {
            if (!await svc.CheckForUpdateAsync().ConfigureAwait(false)) return;
            var dx = installs.Where(x => BfiPresenterService.RouteOf(x.As!) == BfiRoute.Dx).ToList();
            if (dx.Count > 0 && await svc.EnsureStagingAsync(BfiRoute.Dx).ConfigureAwait(false))
                foreach (var (card, rel) in dx)
                    if (!card.IsRunning && svc.DxNeedsUpdate(card.InstallPath, rel!))
                        svc.UpdateDx(card.InstallPath, rel!);

            if (installs.Any(x => BfiPresenterService.RouteOf(x.As!) == BfiRoute.Vulkan))
            {
                if (await svc.EnsureStagingAsync(BfiRoute.Vulkan).ConfigureAwait(false)
                    && BfiPresenterService.LayerFilesPresent && svc.LayerNeedsUpdate)
                {
                    var err = svc.InstallOrUpdateLayer();
                    if (err != null) _crashReporter.Log($"[MainViewModel.UpdateBfiPresenterInstalls] Layer not updated — {err}");
                }
                if (BfiPresenterService.LayerOrderWrong) svc.FixLayerOrder();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            _crashReporter.Log($"[MainViewModel.UpdateBfiPresenterInstalls] {ex.Message}");
        }
    }
}
