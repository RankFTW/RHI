// ForegroundGameResolver.cs -- finds which library game owns the window in front, for the RHI overlay.
// Works for games started outside RHI too: the foreground window's process exe is matched against each
// game's folders (the same search roots RHI uses to track a launched game, MainWindow.GameProcess.cs).
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Overlay;

/// <summary>The window the overlay opens over, and the library game it belongs to (null when RHI doesn't know it).</summary>
internal sealed record ForegroundGame(IntPtr Hwnd, int ProcessId, string? ExePath, GameCardViewModel? Card);

internal static class ForegroundGameResolver
{
    /// <summary>
    /// The foreground window and its game, or null when there's no foreground window or it belongs to RHI itself.
    /// </summary>
    internal static ForegroundGame? Resolve(IReadOnlyList<GameCardViewModel> cards)
    {
        var hwnd = OverlayNative.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        OverlayNative.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;

        var exe = MainWindow.GetProcessImagePath((int)pid);
        return new ForegroundGame(hwnd, (int)pid, exe, exe == null ? null : MatchCard(cards, exe));
    }

    /// <summary>The game whose folders hold <paramref name="exePath"/>; the most specific (longest) folder wins.</summary>
    internal static GameCardViewModel? MatchCard(IReadOnlyList<GameCardViewModel> cards, string exePath)
    {
        var path = exePath.Replace('/', '\\').ToLowerInvariant();
        GameCardViewModel? best = null;
        int bestLen = 0;
        foreach (var card in cards.ToList())
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;
            List<string> roots;
            try { roots = MainWindow.GameProcessSearchRoots(card.InstallPath); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue; // a malformed saved path: skip that game
            }
            foreach (var root in roots)
                if (root.Length > bestLen && path.StartsWith(root, StringComparison.Ordinal))
                {
                    best = card;
                    bestLen = root.Length;
                }
        }
        return best;
    }
}
