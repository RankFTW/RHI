using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages Ryubing emulator sub-game detection, addon swapping, and per-game storage.
/// </summary>
public class EmulatorService : IEmulatorService
{
    private const string RhiEmulatorFolder = "_rhi_emulator";
    private const string ActiveFile = "active.txt";
    private static readonly string RyubingAppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ryujinx");

    /// <inheritdoc />
    public bool IsRyubingExe(string exePath)
    {
        var fileName = Path.GetFileName(exePath);
        return fileName.Equals("Ryujinx.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public List<RyubingGameMetadata> ScanRyubingGames()
    {
        var results = new List<RyubingGameMetadata>();
        var gamesDir = Path.Combine(RyubingAppData, "games");
        if (!Directory.Exists(gamesDir)) return results;

        try
        {
            foreach (var titleDir in Directory.GetDirectories(gamesDir))
            {
                var titleId = Path.GetFileName(titleDir);
                var metadataPath = Path.Combine(titleDir, "gui", "metadata.json");
                if (!File.Exists(metadataPath)) continue;

                try
                {
                    var json = File.ReadAllText(metadataPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var title = root.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(title)) continue;

                    results.Add(new RyubingGameMetadata
                    {
                        TitleId = titleId,
                        Title = title,
                        Favorite = root.TryGetProperty("favorite", out var favProp) && favProp.GetBoolean(),
                        TimespanPlayed = root.TryGetProperty("timespan_played", out var tsProp)
                            ? tsProp.GetString() : null,
                        LastPlayedUtc = root.TryGetProperty("last_played_utc", out var lpProp)
                            ? lpProp.GetString() : null,
                    });
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[EmulatorService.ScanRyubingGames] Failed to parse metadata for '{titleId}' — {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[EmulatorService.ScanRyubingGames] Failed to scan games dir — {ex.Message}");
        }

        return results;
    }

    /// <inheritdoc />
    public string? ResolveRomPath(string titleId)
    {
        var configPath = Path.Combine(RyubingAppData, "Config.json");
        if (!File.Exists(configPath)) return null;

        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("game_dirs", out var gameDirs)) return null;

            foreach (var dir in gameDirs.EnumerateArray())
            {
                var dirPath = dir.GetString();
                if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath)) continue;

                // Scan for ROM files containing the title ID in the filename
                try
                {
                    foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.TopDirectoryOnly))
                    {
                        var ext = Path.GetExtension(file);
                        if (!ext.Equals(".nsp", StringComparison.OrdinalIgnoreCase)
                            && !ext.Equals(".xci", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (fileName.Contains(titleId, StringComparison.OrdinalIgnoreCase))
                            return file;
                    }

                    // Fallback: check subdirectories one level deep
                    foreach (var subDir in Directory.GetDirectories(dirPath))
                    {
                        foreach (var file in Directory.GetFiles(subDir, "*", SearchOption.TopDirectoryOnly))
                        {
                            var ext = Path.GetExtension(file);
                            if (!ext.Equals(".nsp", StringComparison.OrdinalIgnoreCase)
                                && !ext.Equals(".xci", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var fileName = Path.GetFileNameWithoutExtension(file);
                            if (fileName.Contains(titleId, StringComparison.OrdinalIgnoreCase))
                                return file;
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[EmulatorService.ResolveRomPath] Error scanning '{dirPath}' — {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[EmulatorService.ResolveRomPath] Failed to read config — {ex.Message}");
        }

        return null;
    }

    /// <inheritdoc />
    public string? GetActiveGame(string emulatorPath)
    {
        var activePath = Path.Combine(emulatorPath, RhiEmulatorFolder, ActiveFile);
        if (!File.Exists(activePath)) return null;
        try
        {
            var content = File.ReadAllText(activePath).Trim();
            return string.IsNullOrEmpty(content) ? null : content;
        }
        catch { return null; }
    }

    /// <inheritdoc />
    public void SwapActiveGame(string emulatorPath, string titleId)
    {
        var rhiDir = Path.Combine(emulatorPath, RhiEmulatorFolder);
        Directory.CreateDirectory(rhiDir);

        // 1. Save current active game's reshade.ini back to its subfolder
        var currentActive = GetActiveGame(emulatorPath);
        if (!string.IsNullOrEmpty(currentActive) && currentActive != titleId)
        {
            var currentIni = Path.Combine(emulatorPath, "reshade.ini");
            var currentSubDir = Path.Combine(rhiDir, currentActive);
            if (File.Exists(currentIni) && Directory.Exists(currentSubDir))
            {
                try
                {
                    File.Copy(currentIni, Path.Combine(currentSubDir, "reshade.ini"), overwrite: true);
                    CrashReporter.Log($"[EmulatorService.SwapActiveGame] Saved reshade.ini for '{currentActive}'");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[EmulatorService.SwapActiveGame] Failed to save reshade.ini for '{currentActive}' — {ex.Message}");
                }
            }
        }

        // 2. Remove all renodx-*.addon64 from emulator root
        try
        {
            foreach (var addonFile in Directory.GetFiles(emulatorPath, "renodx-*.addon64"))
            {
                File.Delete(addonFile);
            }
            // Also remove .addon32 variants
            foreach (var addonFile in Directory.GetFiles(emulatorPath, "renodx-*.addon32"))
            {
                File.Delete(addonFile);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[EmulatorService.SwapActiveGame] Failed to clean addons from root — {ex.Message}");
        }

        // 3. Copy target game's addon and reshade.ini to root
        var targetSubDir = Path.Combine(rhiDir, titleId);
        if (Directory.Exists(targetSubDir))
        {
            try
            {
                // Copy addon(s)
                foreach (var addonFile in Directory.GetFiles(targetSubDir, "renodx-*.addon64"))
                {
                    var destPath = Path.Combine(emulatorPath, Path.GetFileName(addonFile));
                    File.Copy(addonFile, destPath, overwrite: true);
                }
                foreach (var addonFile in Directory.GetFiles(targetSubDir, "renodx-*.addon32"))
                {
                    var destPath = Path.Combine(emulatorPath, Path.GetFileName(addonFile));
                    File.Copy(addonFile, destPath, overwrite: true);
                }

                // Copy reshade.ini
                var targetIni = Path.Combine(targetSubDir, "reshade.ini");
                if (File.Exists(targetIni))
                {
                    File.Copy(targetIni, Path.Combine(emulatorPath, "reshade.ini"), overwrite: true);
                }

                CrashReporter.Log($"[EmulatorService.SwapActiveGame] Swapped to '{titleId}'");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[EmulatorService.SwapActiveGame] Failed to copy files for '{titleId}' — {ex.Message}");
            }
        }

        // 4. Write active.txt
        try
        {
            File.WriteAllText(Path.Combine(rhiDir, ActiveFile), titleId);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[EmulatorService.SwapActiveGame] Failed to write active.txt — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task InstallAddonAsync(string emulatorPath, string titleId, string addonUrl, string addonFileName)
    {
        var rhiDir = Path.Combine(emulatorPath, RhiEmulatorFolder);
        var gameDir = Path.Combine(rhiDir, titleId);
        Directory.CreateDirectory(gameDir);

        var destPath = Path.Combine(gameDir, addonFileName);

        // Download the addon
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RHI");
        var response = await http.GetAsync(addonUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var file = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(file).ConfigureAwait(false);

        // Create a default reshade.ini if one doesn't exist for this game
        var iniPath = Path.Combine(gameDir, "reshade.ini");
        if (!File.Exists(iniPath))
        {
            // Copy from the emulator root if it exists, otherwise create minimal
            var rootIni = Path.Combine(emulatorPath, "reshade.ini");
            if (File.Exists(rootIni))
                File.Copy(rootIni, iniPath);
            else
                File.WriteAllText(iniPath, "[GENERAL]\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\nTextureSearchPaths=.\\reshade-shaders\\Textures\\**\n");
        }

        CrashReporter.Log($"[EmulatorService.InstallAddonAsync] Installed '{addonFileName}' for titleId '{titleId}'");
    }

    /// <inheritdoc />
    public void UninstallAddon(string emulatorPath, string titleId)
    {
        var gameDir = Path.Combine(emulatorPath, RhiEmulatorFolder, titleId);
        if (!Directory.Exists(gameDir)) return;

        try
        {
            Directory.Delete(gameDir, recursive: true);
            CrashReporter.Log($"[EmulatorService.UninstallAddon] Removed subfolder for '{titleId}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[EmulatorService.UninstallAddon] Failed to delete subfolder for '{titleId}' — {ex.Message}");
        }

        // If this was the active game, clean up the root
        var active = GetActiveGame(emulatorPath);
        if (active == titleId)
        {
            try
            {
                foreach (var addonFile in Directory.GetFiles(emulatorPath, "renodx-*.addon64"))
                    File.Delete(addonFile);
                foreach (var addonFile in Directory.GetFiles(emulatorPath, "renodx-*.addon32"))
                    File.Delete(addonFile);

                var activePath = Path.Combine(emulatorPath, RhiEmulatorFolder, ActiveFile);
                if (File.Exists(activePath)) File.Delete(activePath);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[EmulatorService.UninstallAddon] Failed to clean root — {ex.Message}");
            }
        }
    }
}
