namespace RHI.Linux.Core;

public sealed partial class OptiScaler
{
    // ── ReShade coexistence ──────────────────────────────────────────────────
    // OptiScaler takes ReShade's proxy name and loads it as ReShade64.dll instead (LoadReshade=true).
    private static void MoveReShadeAside(Installation installation, string dir, string dllName)
    {
        var target = LinuxPaths.ResolveCase(dir, dllName);
        if (installation.ReadState().Files.FirstOrDefault(f => f.Component == "ReShade" && LinuxPaths.ResolveCase(dir, f.Path) == target) is { } reshade)
            installation.Move("ReShade", reshade.Path, Installation.ReShadeBesideOptiScaler);
    }

    // ReShade returns to its proxy name once OptiScaler no longer uses it.
    private static void RestoreReShade(Installation installation, string dir)
    {
        var state = installation.ReadState();
        if (state.Proxy == null || state.Files.All(f => f.Component != "ReShade" || !f.Path.Equals(Installation.ReShadeBesideOptiScaler, StringComparison.OrdinalIgnoreCase))) return;
        var proxy = LinuxPaths.ResolveCase(dir, state.Proxy);
        if (File.Exists(proxy) || state.Files.Any(f => LinuxPaths.ResolveCase(dir, f.Path) == proxy)) return;
        installation.Move("ReShade", Installation.ReShadeBesideOptiScaler, state.Proxy);
    }
}
