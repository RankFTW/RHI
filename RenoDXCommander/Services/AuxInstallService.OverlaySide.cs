// AuxInstallService.OverlaySide.cs — Settings › ReShade › ReShade menu position (Default / Left / Right):
// which side of the screen ReShade's menu is docked on, for every game. The edit itself is
// ReShadeOverlayDock.SetSideInText; this applies it after each reshade.ini template write and from
// "Write these to all games".
namespace RenoDXCommander.Services;

public partial class AuxInstallService
{
    /// <summary>The saved choice ("Default", "Left" or "Right"). Null / Default = layouts are left alone. Set at startup.</summary>
    public static Func<string>? OverlaySideResolver { get; set; }

    /// <summary>After a template write: puts ReShade's menu on the chosen side in this game's ReShade configs.</summary>
    internal static void ApplyOverlaySideToGame(string gameDir)
    {
        var side = ReShadeOverlayDock.Normalize(OverlaySideResolver?.Invoke());
        if (side != ReShadeOverlayDock.Default) WriteOverlaySide(gameDir, side);
    }

    /// <summary>
    /// Writes the side into reshade.ini and ReShadeN.ini in <paramref name="gameDir"/> (never ReShadePreset.ini).
    /// Files already that way, and layouts the user arranged differently, are left alone. Returns files changed.
    /// </summary>
    public static int WriteOverlaySide(string gameDir, string side)
    {
        side = ReShadeOverlayDock.Normalize(side);
        if (side == ReShadeOverlayDock.Default) return 0;
        int changed = 0;
        foreach (var ini in ReShadeConfigFiles(gameDir))
        {
            try
            {
                var text = File.ReadAllText(ini);
                var updated = ReShadeOverlayDock.SetSideInText(text, side);
                if (updated == text) continue;
                File.WriteAllText(ini, updated);
                changed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[AuxInstallService.WriteOverlaySide] Failed for '{ini}' — {ex.Message}");
            }
        }
        if (changed > 0)
            CrashReporter.Log($"[AuxInstallService.WriteOverlaySide] ReShade menu docked {side.ToLowerInvariant()} in {changed} file(s) in '{gameDir}'");
        return changed;
    }
}
