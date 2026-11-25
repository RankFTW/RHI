// MainViewModel.NoAddonReShade.cs -- manifest noAddonReShadeGames: games that get ReShade without add-on support (the
// No Addons channel) by default, e.g. HELLDIVERS 2, whose anti-cheat blocks add-on ReShade. User decisions (Nov 2026):
// applied once per game ("Game|Store" in GameNameService.ManifestNoAddonApplied), so a user who switches back to add-on
// ReShade keeps that; and an add-on ReShade already installed there is swapped at once (SetUseNormalReShade removes it
// and the components that need add-ons: managed add-ons, RenoDX, ReLimiter, Display Commander; then ReShade is installed
// again without add-ons). A game the user already set up (No Addons, or a channel / legacy / custom pick) is only marked.
// Skipped and retried later: a running game. Not for Vulkan games (one shared ReShade layer).

using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Applies the manifest's No Addons default to listed games it hasn't been applied to yet. Both init paths call it.</summary>
    private void ApplyManifestNoAddonReShade(string caller)
    {
        var list = _manifest?.NoAddonReShadeGames;
        if (list == null || list.Count == 0) return;
        var names = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        var applied = _gameNameService.ManifestNoAddonApplied;
        var todo = _allCards
            .Where(c => names.Contains(c.GameName) && !applied.Contains(GameKey.FromCard(c.GameName, c.Source ?? "").ToKey()))
            .ToList();
        if (todo.Count == 0) return;

        // Card changes and the ReShade install run on the UI thread (the No Addons switch is a UI-thread operation)
        DispatcherQueue?.TryEnqueue(async () =>
        {
            foreach (var card in todo)
            {
                try
                {
                    var key = GameKey.FromCard(card.GameName, card.Source ?? "").ToKey();
                    if (applied.Contains(key)) continue;
                    if (card.IsRunning)
                    {
                        _crashReporter.Log($"[{caller}] No Addons default for '{card.GameName}' waits: the game is running");
                        continue; // tried again next start
                    }
                    applied.Add(key);
                    SaveNameMappings();

                    bool userChannel = _reShadeChannelOverrides.ContainsKey(card.GameName)
                        || _reShadeChannelOverrides.Keys.Any(k => k.StartsWith(card.GameName + "|", StringComparison.OrdinalIgnoreCase));
                    if (card.UseNormalReShade || userChannel || card.RequiresVulkanInstall)
                    {
                        _crashReporter.Log($"[{caller}] No Addons default for '{card.GameName}' not applied "
                            + $"({(card.UseNormalReShade ? "already No Addons" : userChannel ? "the user picked a ReShade channel" : "Vulkan game")})");
                        continue;
                    }

                    bool wasInstalled = card.IsRsInstalled;
                    _crashReporter.Log($"[{caller}] Manifest: '{card.GameName}' → ReShade without add-ons{(wasInstalled ? " (reinstalling)" : "")}");
                    SetUseNormalReShade(card, true);
                    if (wasInstalled)
                        await InstallReShadeCommand.ExecuteAsync(card);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    _crashReporter.Log($"[{caller}] No Addons default for '{card.GameName}' failed — {ex.Message}");
                }
            }
        });
    }
}
