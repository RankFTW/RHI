using System.Diagnostics;
using RenoDXCommander.Models;

namespace RHI.Linux.Core;

public sealed class GameSetup(Downloads downloads, Catalog catalog)
{
    public GraphicsApiType Api(Game game, GamePreferences preferences)
    {
        if (preferences.Api != "Auto" && Enum.TryParse<GraphicsApiType>(preferences.Api, out var selected)) return selected;
        var value = catalog.ManifestString("graphicsApiOverrides", game.Name)?.Replace("DX", "DirectX");
        return Enum.TryParse<GraphicsApiType>(value, out var mapped) ? mapped : game.Api;
    }
    public static void RequireClosed(Game game)
    {
        if (!OperatingSystem.IsLinux() || game.Executable == null) return;
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out _)) continue;
            try
            {
                var command = File.ReadAllText(Path.Combine(directory, "cmdline")).Split('\0').FirstOrDefault() ?? "";
                if (command.Replace('\\', '/').EndsWith("/" + Path.GetFileName(game.Executable), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Close " + game.Name + " before changing its installed components.");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    public async Task InstallReShade(Game game, GamePreferences preferences, IProgress<string>? progress, string? proxyOverride = null)
    {
        RequireClosed(game);
        var proxy = proxyOverride ?? Installation.ProxyFor(Api(game, preferences));
        var (path, version) = await downloads.ReShade(preferences.Channel, game.Architecture, progress);
        try
        {
            var compiler = await downloads.ShaderCompiler(game.Architecture, progress);
            RequireClosed(game);
            await Task.Run(() =>
            {
                var installation = new Installation(game.InstallDirectory);
                installation.Install("ReShade", version, [new(installation.ReShadeFile(proxy), File.ReadAllBytes(path)), Installation.DefaultIni(), compiler], proxy: proxy);
            });
        }
        finally { File.Delete(path); }
    }
    public GameMod? Mod(Game game, GamePreferences preferences) =>
        preferences.ModName == null ? catalog.Match(game) : catalog.Mods.FirstOrDefault(m => m.Name == preferences.ModName);
    public async Task InstallRenoDx(Game game, GamePreferences preferences, IProgress<string>? progress)
    {
        RequireClosed(game);
        var mod = Mod(game, preferences) ?? throw new IOException("No matching RenoDX mod is available. Choose a mod in Advanced settings.");
        var url = catalog.AddonUrl(game, mod) ?? throw new IOException("This mod needs a manual download. Open Mod instructions for its author's download link.");
        // Fetch before modifying the game, then install the required ReShade dependency automatically.
        var path = await downloads.Fetch(url, progress, true);
        Downloads.ValidatePe(path, game.Architecture);
        var reshade = InstallationStatus.Read(game).Get("ReShade");
        if (!reshade.Installed || (reshade.Channel != "Local" && reshade.Channel != preferences.Channel)) await InstallReShade(game, preferences, progress);
        RequireClosed(game);
        await Task.Run(() => new Installation(game.InstallDirectory).Install("RenoDX", mod.Name + " • " + DateTime.UtcNow.ToString("yyyy-MM-dd"),
            [new(Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath)), File.ReadAllBytes(path))]));
        preferences.ModName = mod.Name;
        if (url.Contains("ue-extended", StringComparison.OrdinalIgnoreCase) && mod.Notes?.Contains("Engine.ini", StringComparison.OrdinalIgnoreCase) == true)
        {
            var inis = IniSettings.FindEngineInis(game);
            if (inis.Count == 1) ApplyHdr(game, inis[0]);
        }
    }
    public static void ApplyHdr(Game game, string ini)
    {
        RequireClosed(game);
        IniSettings.Apply(ini, IniSettings.UnrealHdr, readOnly: true);
        IniSettings.Apply(LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.ini"), IniSettings.RenoDxHdr);
    }
    public static void RestoreHdr(Game game)
    {
        RequireClosed(game);
        foreach (var ini in IniSettings.FindEngineInis(game)) IniSettings.Restore(ini);
        IniSettings.Restore(LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.ini"));
    }
    public static bool AnySteamGameRunning()
    {
        var steam = Process.GetProcessesByName("reaper");
        try { return steam.Length > 0; } finally { foreach (var p in steam) p.Dispose(); }
    }
    // Select the launcher from the owning Steam installation, not the game's library.
    // ArgumentList avoids shell parsing and preserves Flatpak's application id as one argument.
    public static ProcessStartInfo SteamStartInfo(Game game, bool shutdown)
    {
        var source = game.SteamRoot is { } root ? GameDiscovery.SteamSource(root) : game.Source;
        var info = new ProcessStartInfo { UseShellExecute = false };
        if (source == "Steam Flatpak")
        {
            info.FileName = "flatpak";
            info.ArgumentList.Add("run");
            info.ArgumentList.Add("com.valvesoftware.Steam");
        }
        else if (source == "Steam Snap")
        {
            info.FileName = "snap";
            info.ArgumentList.Add("run");
            info.ArgumentList.Add("steam");
        }
        else info.FileName = "steam";
        info.ArgumentList.Add(shutdown ? "-shutdown" : "-silent");
        return info;
    }

    public static Task ConfigureSteam(Game game, string config, IProgress<string>? progress, LaunchExtras? extras = null)
    {
        var proxy = new Installation(game.InstallDirectory).ReadState().Proxy;
        if (proxy == null && (extras == null || extras.Dlls.Count == 0 && Proton.HasEnvironment("", extras))) throw new IOException("Install ReShade first.");
        return ConfigureLaunchOptions(game, config, progress, options => Proton.LaunchOptions(options, proxy, extras));
    }

    public static async Task ConfigureLaunchOptions(Game game, string config, IProgress<string>? progress, Func<string, string> merge)
    {
        RequireClosed(game);
        if (AnySteamGameRunning()) throw new IOException("Close your running Steam games, then choose Apply again. Steam needs a restart to save this setup.");
        // Validate the merge before asking Steam to exit.
        _ = merge(Proton.ReadOptions(config, game.AppId!) ?? "");
        var restart = Proton.SteamRunning();
        try
        {
            if (restart)
            {
                progress?.Report("Waiting for Steam to close…");
                using var shutdown = Process.Start(SteamStartInfo(game, shutdown: true));
                var until = DateTime.UtcNow.AddSeconds(45);
                while (Proton.SteamRunning() && DateTime.UtcNow < until) await Task.Delay(500);
                if (Proton.SteamRunning()) throw new IOException("Steam is still closing. Exit it from its menu, then click Apply again.");
            }
            var options = merge(Proton.ReadOptions(config, game.AppId!) ?? "");
            Proton.SaveOptions(config, game.AppId!, options);
        }
        finally
        {
            if (restart && !Proton.SteamRunning()) Process.Start(SteamStartInfo(game, shutdown: false))?.Dispose();
        }
    }
}
