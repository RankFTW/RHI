// MainViewModel.CustomDlls.cs — glue for Custom DLLs (the user's own DLLs from Custom\DLLs\): the game facts the
// service needs, the names other RHI components hold, and the redeploy pass (launch, folder watcher, periodic check).

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private ICustomDllService? _customDlls;

    public ICustomDllService CustomDlls => _customDlls ??= App.Services.GetRequiredService<ICustomDllService>();

    /// <summary>The card as the custom DLL service sees it.</summary>
    public static CustomDllGame CustomDllGameFor(GameCardViewModel card) =>
        new(card.GameName, card.Source ?? "", card.InstallPath ?? "", card.Is32Bit, card.IsRunning, card.IsHidden);

    /// <summary>DLL names RHI components other than custom DLLs hold in this game (refused as targets).</summary>
    public Dictionary<string, string> NonCustomDllOwners(GameCardViewModel card)
    {
        var owners = GetRhiDllOwners(card);
        foreach (var k in owners.Where(kv => kv.Value.StartsWith("Custom DLL (", StringComparison.Ordinal)).Select(kv => kv.Key).ToList())
            owners.Remove(k);
        return owners;
    }

    /// <summary>
    /// Copies newer custom DLLs over RHI's copies in every game (hidden / running games skipped). Runs on a
    /// background thread; one summary log line per pass.
    /// </summary>
    public void RedeployCustomDlls(string caller)
    {
        _ = Task.Run(() =>
        {
            try
            {
                GameCardViewModel[] cards;
                try { cards = _allCards.ToArray(); }
                catch (ArgumentException ex) { _crashReporter.Log($"[MainViewModel.RedeployCustomDlls] Card list changed — {ex.Message}"); return; }
                var games = cards.Where(c => !string.IsNullOrEmpty(c.InstallPath)).Select(CustomDllGameFor).ToList();
                var result = CustomDlls.CheckAndRedeploy(games, caller);
                if (result.Updated > 0 || result.Dropped > 0)
                    DispatcherQueue?.TryEnqueue(() => CustomDllsChanged?.Invoke());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _crashReporter.Log($"[MainViewModel.RedeployCustomDlls] {caller} failed — {ex.Message}");
            }
        });
    }

    /// <summary>Watches Custom\DLLs\ and redeploys about 2 s after a file is added or replaced. Once per session.</summary>
    private void StartCustomDllWatcher()
    {
        try { CustomDlls.StartWatching(() => RedeployCustomDlls("FolderWatcher")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _crashReporter.Log($"[MainViewModel.StartCustomDllWatcher] {ex.Message}");
        }
    }

    /// <summary>Raised on the UI thread after a redeploy changed something (the shown game's Extras can refresh).</summary>
    public event Action? CustomDllsChanged;
}
