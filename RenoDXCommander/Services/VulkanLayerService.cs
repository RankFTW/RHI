using System.Security.Principal;
using Microsoft.Win32;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the global Vulkan ReShade implicit layer: install, uninstall, and status detection.
/// The layer is registered system-wide via HKLM registry and files in C:\ProgramData\ReShade\.
/// All methods are static — the service has no instance state.
/// </summary>
public static class VulkanLayerService
{
    // ── Constants ──────────────────────────────────────────────────────────────────

    public const string LayerDirectory = @"C:\ProgramData\ReShade\";
    public const string LayerDllName = "ReShade64.dll";
    public const string LayerManifestName = "ReShade64.json";
    public const string RegistryKeyPath = @"SOFTWARE\Khronos\Vulkan\ImplicitLayers";
    public const string LayerName = "VK_LAYER_reshade";

    private static string LayerDllPath => Path.Combine(LayerDirectory, LayerDllName);
    private static string LayerManifestPath => Path.Combine(LayerDirectory, LayerManifestName);

    // ── Admin detection ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the current process is running with administrator privileges.
    /// </summary>
    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    // ── Layer status ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the Vulkan ReShade layer is currently installed by verifying:
    /// 1. The registry entry exists in HKLM ImplicitLayers
    /// 2. The manifest JSON file exists on disk
    /// 3. The ReShade64.dll exists in the layer directory
    /// Returns true only if all three conditions are met.
    /// </summary>
    public static bool IsLayerInstalled()
    {
        return IsLayerInstalled(Registry.LocalMachine, RegistryKeyPath, LayerManifestPath, LayerDllPath);
    }

    /// <summary>
    /// Testable overload that checks layer installation status using the provided
    /// registry hive, key path, manifest path, and DLL path.
    /// Returns true only if all three conditions (registry entry, manifest file, DLL file) are met.
    /// </summary>
    internal static bool IsLayerInstalled(RegistryKey registryHive, string registryKeyPath, string manifestPath, string dllPath)
    {
        try
        {
            // Check registry entry
            using var key = registryHive.OpenSubKey(registryKeyPath, writable: false);
            if (key == null) return false;

            var value = key.GetValue(manifestPath);
            if (value == null) return false;

            // Check manifest file
            if (!File.Exists(manifestPath)) return false;

            // Check DLL file
            if (!File.Exists(dllPath)) return false;

            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[VulkanLayerService.IsLayerInstalled] Error checking layer status — {ex.Message}");
            return false;
        }
    }

    // ── Install ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Installs the Vulkan ReShade layer:
    /// 1. Creates C:\ProgramData\ReShade\ if needed
    /// 2. Copies ReShade64.dll from staging directory
    /// 3. Writes ReShade64.json manifest
    /// 4. Registers in HKLM\SOFTWARE\Khronos\Vulkan\ImplicitLayers
    /// Throws UnauthorizedAccessException if not running as admin.
    /// Throws FileNotFoundException if staged ReShade64.dll is missing.
    /// </summary>
    public static void InstallLayer()
    {
        if (!IsRunningAsAdmin())
            throw new UnauthorizedAccessException(
                "Administrator privileges are required to install the Vulkan ReShade layer.");

        var stagedDll = AuxInstallService.RsStagedPath64;
        if (!File.Exists(stagedDll))
            throw new FileNotFoundException(
                "Staged ReShade64.dll not found. Ensure ReShade has been downloaded first.", stagedDll);

        try
        {
            // 1. Create layer directory
            Directory.CreateDirectory(LayerDirectory);

            // 2. Copy ReShade64.dll from staging
            File.Copy(stagedDll, LayerDllPath, overwrite: true);
            CrashReporter.Log("[VulkanLayerService.InstallLayer] Copied ReShade64.dll to layer directory");

            // 3. Copy bundled manifest JSON (falls back to generated manifest if bundled file missing)
            var bundledManifest = Path.Combine(AppContext.BaseDirectory, LayerManifestName);
            if (File.Exists(bundledManifest))
            {
                File.Copy(bundledManifest, LayerManifestPath, overwrite: true);
                CrashReporter.Log("[VulkanLayerService.InstallLayer] Copied bundled ReShade64.json to layer directory");
            }
            else
            {
                File.WriteAllText(LayerManifestPath, GenerateManifestJson());
                CrashReporter.Log("[VulkanLayerService.InstallLayer] Wrote generated layer manifest JSON (bundled file not found)");
            }

            // 4. Register in HKLM registry
            using var key = Registry.LocalMachine.CreateSubKey(RegistryKeyPath, writable: true);
            key.SetValue(LayerManifestPath, 0, RegistryValueKind.DWord);
            CrashReporter.Log("[VulkanLayerService.InstallLayer] Registered layer in HKLM ImplicitLayers");
        }
        catch (UnauthorizedAccessException) { throw; }
        catch (FileNotFoundException) { throw; }
        catch (Exception ex)
        {
            CrashReporter.Log($"[VulkanLayerService.InstallLayer] Failed — {ex.Message}");
            throw;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uninstalls the Vulkan ReShade layer:
    /// 1. Removes registry entry from HKLM ImplicitLayers
    /// 2. Deletes ReShade64.dll and ReShade64.json from layer directory
    /// Treats missing entries/files as success (idempotent).
    /// Throws UnauthorizedAccessException if not running as admin.
    /// </summary>
    public static void UninstallLayer()
    {
        if (!IsRunningAsAdmin())
            throw new UnauthorizedAccessException(
                "Administrator privileges are required to uninstall the Vulkan ReShade layer.");

        UninstallLayer(Registry.LocalMachine, RegistryKeyPath, LayerManifestPath, LayerDllPath);
    }

    /// <summary>
    /// Testable overload that uninstalls the Vulkan ReShade layer using the provided
    /// registry hive, key path, manifest path, and DLL path.
    /// Treats missing entries/files as success (idempotent).
    /// </summary>
    internal static void UninstallLayer(RegistryKey registryHive, string registryKeyPath, string manifestPath, string dllPath)
    {
        try
        {
            // 1. Remove registry entry (idempotent)
            using var key = registryHive.OpenSubKey(registryKeyPath, writable: true);
            if (key != null)
            {
                try { key.DeleteValue(manifestPath, throwOnMissingValue: false); }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[VulkanLayerService.UninstallLayer] Failed to remove registry entry — {ex.Message}");
                }
            }

            // 2. Delete DLL (idempotent)
            if (File.Exists(dllPath))
            {
                try { File.Delete(dllPath); }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[VulkanLayerService.UninstallLayer] Failed to delete DLL — {ex.Message}");
                }
            }

            // 3. Delete manifest (idempotent)
            if (File.Exists(manifestPath))
            {
                try { File.Delete(manifestPath); }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[VulkanLayerService.UninstallLayer] Failed to delete manifest — {ex.Message}");
                }
            }

            CrashReporter.Log("[VulkanLayerService.UninstallLayer] Vulkan layer uninstalled");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[VulkanLayerService.UninstallLayer] Unexpected error — {ex.Message}");
            throw;
        }
    }

    // ── Manifest generation ───────────────────────────────────────────────────────

    /// <summary>
    /// Generates the JSON manifest content for the Vulkan layer.
    /// </summary>
    internal static string GenerateManifestJson()
    {
        // Use escaped backslashes for the Windows path in JSON
        var libraryPath = LayerDllPath.Replace(@"\", @"\\");

        return $$"""
            {
                "file_format_version": "1.0.0",
                "layer": {
                    "name": "{{LayerName}}",
                    "type": "GLOBAL",
                    "library_path": "{{libraryPath}}",
                    "api_version": "1.3.0",
                    "implementation_version": "1",
                    "description": "ReShade"
                }
            }
            """;
    }
}
