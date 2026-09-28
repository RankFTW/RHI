namespace RHI.Linux.Core;

public sealed partial class OptiScaler
{
    // ── Streamline deployment ────────────────────────────────────────────────
    private static void DeployStreamline(string dir, string? source)
    {
        var folder = Path.Combine(LinuxPaths.ResolveCase(dir, "OptiScaler"), "Streamline");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        if (source == null) return;
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.EnumerateFiles(source, "*.dll")) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
    }

    // OptiScaler settings → Streamline: deploys (or removes) the selected Streamline in OptiScaler/Streamline.
    public async Task ApplyStreamline(Game game, GamePreferences prefs, IProgress<string>? progress = null)
    {
        GameSetup.RequireClosed(game);
        if (Record(game) == null) return;
        var source = prefs.OsDeployStreamline ? await dlss.Fetch(DlssKind.Streamline, prefs.OsStreamlineVersion, progress) : null;
        await Task.Run(() => DeployStreamline(game.InstallDirectory, source));
    }
}
