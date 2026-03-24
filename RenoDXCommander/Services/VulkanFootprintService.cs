namespace RenoDXCommander.Services;

/// <summary>
/// Manages the per-game Vulkan ReShade footprint file (RDXC_VULKAN_FOOTPRINT).
/// The footprint signals that a game is using Vulkan ReShade via the global implicit layer,
/// enabling shader deployment, status detection, and per-game uninstall.
/// All methods are static, idempotent, and swallow exceptions (matching VulkanLayerService pattern).
/// </summary>
public static class VulkanFootprintService
{
    public const string FootprintFileName = "RDXC_VULKAN_FOOTPRINT";
    public const string FootprintContent =
        "Managed by RHI — Vulkan ReShade footprint.\n"
      + "Do not delete this file while Vulkan ReShade is in use.";

    /// <summary>
    /// Creates the footprint file in the game directory.
    /// Idempotent: if the file already exists, does nothing.
    /// Logs and swallows IO exceptions.
    /// </summary>
    public static void Create(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, FootprintFileName);
            if (File.Exists(path)) return;

            Directory.CreateDirectory(gameDir);
            File.WriteAllText(path, FootprintContent);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[VulkanFootprintService.Create] Error creating footprint in {gameDir} — {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes the footprint file from the game directory.
    /// Idempotent: if the file does not exist, does nothing.
    /// Logs and swallows IO exceptions.
    /// </summary>
    public static void Delete(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, FootprintFileName);
            if (!File.Exists(path)) return;

            File.Delete(path);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[VulkanFootprintService.Delete] Error deleting footprint in {gameDir} — {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true if the footprint file exists in the game directory.
    /// </summary>
    public static bool Exists(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, FootprintFileName);
            return File.Exists(path);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[VulkanFootprintService.Exists] Error checking footprint in {gameDir} — {ex.Message}");
            return false;
        }
    }
}
