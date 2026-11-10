// AuxInstallService.DisplayModes.cs -- lets Display modes protect a game's values from writers that push one
// value to every game (reshade.ini merge on ReShade install / update, the Settings "write to all games" buttons,
// relimiter.ini setup on ReLimiter install). Set by MainViewModel (DisplayModeService.Keep).

namespace RenoDXCommander.Services;

public partial class AuxInstallService
{
    /// <summary>(folder, item keys) → action that puts the game's display mode values back, or null. Set at startup.</summary>
    public static Func<string, IReadOnlyList<string>, Action?>? DisplayModeKeeper { get; set; }

    /// <summary>
    /// Call before writing global values into a game's ini; run the returned action (if any) afterwards.
    /// Returns null when the game in <paramref name="folder"/> isn't managed by Display modes.
    /// </summary>
    public static Action? KeepDisplayModeValues(string? folder, IReadOnlyList<string> itemKeys)
    {
        var keeper = DisplayModeKeeper;
        if (keeper == null || string.IsNullOrWhiteSpace(folder)) return null;
        try
        {
            var restore = keeper(folder, itemKeys);
            if (restore == null) return null;
            return () =>
            {
                try { restore(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    CrashReporter.Log($"[AuxInstallService.KeepDisplayModeValues] Restore failed for '{folder}' — {ex.Message}");
                }
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CrashReporter.Log($"[AuxInstallService.KeepDisplayModeValues] '{folder}' — {ex.Message}");
            return null;
        }
    }
}
