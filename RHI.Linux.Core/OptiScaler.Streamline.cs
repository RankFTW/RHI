using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public sealed partial class OptiScaler
{
    // ── Streamline deployment ────────────────────────────────────────────────
    // The newest nvngx_dlssg.dll goes beside Streamline so its frame generation works, as on Windows.
    private static void DeployStreamline(string dir, string? source, string? fg)
    {
        var folder = Path.Combine(LinuxPaths.ResolveCase(dir, "OptiScaler"), "Streamline");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        if (source == null) return;
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.EnumerateFiles(source, "*.dll")) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        if (fg != null) File.Copy(fg, Path.Combine(folder, DlssFiles.Fg), true);
    }

    // Frame generation is optional: Streamline still deploys when the DLSS FG download fails.
    private async Task<(string? Folder, string? Fg)> FetchStreamline(GamePreferences prefs, IProgress<string>? progress)
    {
        var folder = await dlss.Fetch(DlssKind.Streamline, prefs.OsStreamlineVersion, progress);
        try { return (folder, await dlss.Fetch(DlssKind.FG, null, progress)); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            CrashReporter.Log("OptiScaler Streamline DLSS FG: " + ex.Message);
            return (folder, null);
        }
    }

    // OptiScaler settings → Streamline: deploys (or removes) the selected Streamline in OptiScaler/Streamline.
    public async Task ApplyStreamline(Game game, GamePreferences prefs, IProgress<string>? progress = null)
    {
        GameSetup.RequireClosed(game);
        if (Record(game) == null) return;
        var source = prefs.OsDeployStreamline ? await FetchStreamline(prefs, progress) : default;
        await Task.Run(() => DeployStreamline(game.InstallDirectory, source.Folder, source.Fg));
    }
}
