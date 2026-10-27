// CustomReShadeHashService.cs — Detects when custom ReShade DLLs in the Custom folder
// have been updated by the user and triggers redeployment to all games using them.

using System.Security.Cryptography;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Tracks SHA256 hashes of custom ReShade DLLs in the Custom\ReShade folder.
/// On check, compares current hashes against stored state and redeploys changed DLLs
/// to all games using the Custom RS channel (DX → file copy, Vulkan → CopyFileWithElevation).
/// </summary>
public class CustomReShadeHashService
{
    private static readonly string HashFilePath = Path.Combine(
        DlssStreamlineService.RsCustomDir, "custom_reshade_hashes.json");

    private readonly IGameNameService _gameNameService;
    private readonly ICrashReporter _crashReporter;

    public CustomReShadeHashService(IGameNameService gameNameService, ICrashReporter crashReporter)
    {
        _gameNameService = gameNameService;
        _crashReporter = crashReporter;
    }

    /// <summary>
    /// Ensures the hash file exists. If missing, creates it with current hashes (no redeploy).
    /// Call on app startup to establish the baseline.
    /// </summary>
    public void EnsureInitialized()
    {
        if (File.Exists(HashFilePath)) return;

        var customDir = DlssStreamlineService.RsCustomDir;
        if (!Directory.Exists(customDir)) return;

        var dllFiles = Directory.GetFiles(customDir, "*.dll");
        if (dllFiles.Length == 0) return;

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dllPath in dllFiles)
        {
            var filename = Path.GetFileName(dllPath);
            hashes[filename] = ComputeSha256(dllPath);
        }

        SaveHashes(hashes);
        _crashReporter.Log($"[CustomReShadeHashService] Initialized hash file with {hashes.Count} DLL(s)");
    }

    /// <summary>
    /// Checks all DLLs in the Custom\ReShade folder for changes and redeploys where needed.
    /// Returns the number of games redeployed to.
    /// </summary>
    public int CheckAndRedeploy(IReadOnlyList<GameCardViewModel> allCards)
    {
        var customDir = DlssStreamlineService.RsCustomDir;
        if (!Directory.Exists(customDir)) return 0;

        var dllFiles = Directory.GetFiles(customDir, "*.dll");
        if (dllFiles.Length == 0) return 0;

        // Load stored hashes
        var storedHashes = LoadHashes();

        // Compute current hashes
        var currentHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dllPath in dllFiles)
        {
            var filename = Path.GetFileName(dllPath);
            currentHashes[filename] = ComputeSha256(dllPath);
        }

        // Find changed files
        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (filename, hash) in currentHashes)
        {
            if (!storedHashes.TryGetValue(filename, out var storedHash) || !string.Equals(hash, storedHash, StringComparison.Ordinal))
            {
                changedFiles.Add(filename);
            }
        }

        if (changedFiles.Count == 0) return 0;

        _crashReporter.Log($"[CustomReShadeHashService] {changedFiles.Count} custom DLL(s) changed: {string.Join(", ", changedFiles)}");

        int redeployCount = 0;

        // Find all games using Custom channel and determine which custom DLL they use
        var customGames = GetCustomChannelGames(allCards);

        foreach (var (card, selectedDll) in customGames)
        {
            if (!changedFiles.Contains(selectedDll)) continue;
            if (string.IsNullOrEmpty(card.InstallPath)) continue;

            var sourcePath = AuxInstallService.GetCustomReShadePathForFile(selectedDll);
            if (!File.Exists(sourcePath)) continue;

            if (card.RequiresVulkanInstall)
            {
                // Vulkan: update the global layer
                try
                {
                    var layerDll = Path.Combine(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                    if (File.Exists(layerDll))
                    {
                        AuxInstallService.CopyFileWithElevation(sourcePath, layerDll);
                        redeployCount++;
                        _crashReporter.Log($"[CustomReShadeHashService] Redeployed '{selectedDll}' to Vulkan layer");
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[CustomReShadeHashService] Failed to redeploy Vulkan layer — {ex.Message}");
                }
            }
            else
            {
                // DX: copy directly to the game's install path
                try
                {
                    var record = card.RsRecord;
                    if (record == null) continue;

                    var destPath = Path.Combine(card.InstallPath, record.InstalledAs);
                    File.Copy(sourcePath, destPath, overwrite: true);
                    redeployCount++;
                    _crashReporter.Log($"[CustomReShadeHashService] Redeployed '{selectedDll}' to '{card.GameName}'");
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[CustomReShadeHashService] Failed to redeploy to '{card.GameName}' — {ex.Message}");
                }
            }
        }

        // Save updated hashes (always — even if redeploy partially failed, the DLLs are still the new version)
        SaveHashes(currentHashes);
        _crashReporter.Log($"[CustomReShadeHashService] Hashes updated. Redeployed to {redeployCount} game(s).");

        return redeployCount;
    }

    /// <summary>
    /// Returns all games on Custom channel paired with the DLL filename they use.
    /// </summary>
    private List<(GameCardViewModel card, string dllFilename)> GetCustomChannelGames(IReadOnlyList<GameCardViewModel> allCards)
    {
        var results = new List<(GameCardViewModel, string)>();
        var selections = _gameNameService.CustomReShadeSelection;

        foreach (var card in allCards)
        {
            if (card.RsStatus == GameStatus.NotInstalled) continue;

            var channel = card.RsRecord?.Channel;
            if (!string.Equals(channel, AuxInstallService.ChannelCustom, StringComparison.OrdinalIgnoreCase))
                continue;

            // Determine which DLL this game uses — try composite key first, fall back to name-only
            string dllFilename;
            var compositeKey = GameKey.FromCard(card.GameName, card.Source).ToKey();
            if (selections.TryGetValue(compositeKey, out var selected) && !string.IsNullOrEmpty(selected))
            {
                dllFilename = selected;
            }
            else if (selections.TryGetValue(card.GameName, out selected) && !string.IsNullOrEmpty(selected))
            {
                dllFilename = selected;
            }
            else
            {
                // Fallback: use ReShade64.dll or ReShade32.dll based on bitness
                dllFilename = card.Is32Bit ? AuxInstallService.RsStaged32 : AuxInstallService.RsStaged64;
            }

            results.Add((card, dllFilename));
        }

        return results;
    }

    private static Dictionary<string, string> LoadHashes()
    {
        if (!File.Exists(HashFilePath)) return new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = File.ReadAllText(HashFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveHashes(Dictionary<string, string> hashes)
    {
        try
        {
            var dir = Path.GetDirectoryName(HashFilePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HashFilePath, json);
        }
        catch (Exception ex) { CrashReporter.Log($"[CustomReShadeHashService.SaveHashes] Failed to save hash file — {ex.Message}"); }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
