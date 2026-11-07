// MainViewModel.Uplift.cs — keeps GITC Uplift (a Neural Rendering method) up to date in the games
// RHI installed it in. Runs at launch and with the 4-hour refresh; nothing is downloaded unless a
// game uses it (installing downloads it on demand).

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private async Task RefreshUpliftInstallsAsync(string caller)
    {
        try
        {
            var cards = _allCards
                .Where(c => !c.IsHidden && !string.IsNullOrEmpty(c.InstallPath) && UpliftService.IsRhiInstalled(c.InstallPath!))
                .ToList();
            var games = cards.Select(c => c.InstallPath!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (games.Count == 0) return;

            var svc = App.Services.GetRequiredService<UpliftService>();
            bool apiFixed = false;
            foreach (var c in cards)
            {
                var path = c.InstallPath!;
                // Uplift's helper exe is DX12, and older RHI counted it: an OpenGL game (OpenMW) was then
                // cached as DX12. Detect again without it (only these few games) and fix the card.
                ForgetGameApi(path);
                var api = DetectGraphicsApi(path, Models.EngineType.Unknown, c.GameName, c.Source);
                var all = _DetectAllApisForCard(path, c.GameName, c.Source);
                if (api != c.GraphicsApi || !all.SetEquals(c.DetectedApis))
                {
                    _crashReporter.Log($"[{caller}] '{c.GameName}' API {c.GraphicsApi} → {api} (without Uplift's helper)");
                    CacheGameApi(path, api, all);
                    apiFixed = true;
                    DispatcherQueue?.TryEnqueue(() => { c.GraphicsApi = api; c.DetectedApis = all; });
                }
                // OpenGL installs from before RHI enabled the Uplift technique (needed for MSAA): once each
                if (svc.EnsureTechniqueFor(path, api == Models.GraphicsApiType.OpenGL))
                    _crashReporter.Log($"[{caller}] GITC Uplift technique enabled for '{c.GameName}' (OpenGL)");
            }
            if (apiFixed) SaveGameApiCache(); // so the next launch starts with the right API
            await svc.CheckForUpdateAsync().ConfigureAwait(false);
            if (!svc.IsStagingReady || svc.HasUpdate)
                await svc.EnsureStagingAsync().ConfigureAwait(false);
            if (!svc.IsStagingReady) return;

            int updated = 0;
            foreach (var path in games)
            {
                if (svc.UpdateInstalled(path)) updated++;
            }
            // Shaders follow through the shader sync hook on the next sync of each game
            _crashReporter.Log($"[{caller}] GITC Uplift v{svc.StagedVersion}: {games.Count} game(s), {updated} updated");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[{caller}] GITC Uplift refresh failed — {ex.Message}");
        }
    }
}
