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

    private static readonly object _verLock = new();
    private static (long length, DateTime writeUtc, string? version) _verCache;

    /// <summary>Installed layer version, re-read only when the file's size or date changes
    /// (never size alone: Stable and Nightly 6.8.0 are the same size).</summary>
    public static string? InstalledLayerVersionCached()
    {
        var fi = new FileInfo(Path.Combine(LayerDirectory, LayerDllName));
        if (!fi.Exists) return null;
        lock (_verLock)
        {
            if (_verCache.length == fi.Length && _verCache.writeUtc == fi.LastWriteTimeUtc) return _verCache.version;
            var v = AuxInstallService.ReadInstalledVersion(LayerDirectory, LayerDllName);
            _verCache = (fi.Length, fi.LastWriteTimeUtc, v);
            return v;
        }
    }

    // ── Admin detection ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the current process is running with administrator privileges,
    /// or has effective write access to system resources even without an elevated token.
    /// </summary>
    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
            return true;

        // Fallback: try to open an HKLM key for write — succeeds when running as
        // a true admin account even without UAC elevation (e.g. built-in Administrator).
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", writable: true);
            if (key != null) return true;
        }
        catch (Exception) { /* access denied when not elevated is the expected result */ }

        return false;
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
            var bundledManifest = AppPaths.InExeDir(LayerManifestName);
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

            // BFI Presenter's Vulkan layer must load after ReShade: a ReShade registered after it lands below it
            if (BfiPresenterService.LayerOrderWrong)
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                    .GetService<BfiPresenterService>(App.Services)?.FixLayerOrder();
        }
        catch (UnauthorizedAccessException) { throw; }
        catch (FileNotFoundException) { throw; }
        catch (Exception ex)
        {
            CrashReporter.Log($"[VulkanLayerService.InstallLayer] Failed — {ex.Message}");
            throw;
        }
    }

    // ── Channel sync ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The channel the shared layer should run: the first Vulkan game's override other than
    /// Stable (they're kept the same), else Stable.
    /// </summary>
    public static string ResolveLayerChannel(IEnumerable<string?> vulkanOverrides) =>
        vulkanOverrides.FirstOrDefault(ch => !string.IsNullOrEmpty(ch)
            && !string.Equals(ch, AuxInstallService.ChannelStable, StringComparison.OrdinalIgnoreCase))
        ?? AuxInstallService.ChannelStable;

    /// <summary>
    /// True when the layer DLL (64-bit, and 32-bit if present) differs from the staged build
    /// for <paramref name="channel"/>. Custom and missing staging/layer files count as not outdated.
    /// Compares contents: Stable and Nightly builds can have the same size.
    /// </summary>
    public static bool IsLayerOutdated(string channel)
    {
        if (string.Equals(channel, AuxInstallService.ChannelCustom, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var (staged, layer) in LayerPairs(channel))
        {
            if (!IsValidStaged(staged) || !File.Exists(layer)) continue;
            if (!FilesIdentical(staged, layer)) return true;
        }
        return false;
    }

    /// <summary>
    /// Copies the staged build for <paramref name="channel"/> over the layer DLLs that differ
    /// (64-bit, and 32-bit when the layer has one). Elevates if needed. Run off the UI thread.
    /// Returns true if any file was replaced. Custom is handled by its own picker and is skipped.
    /// </summary>
    public static bool SyncLayerToChannel(string channel)
    {
        EnsureOffSwitchRenamed();
        if (string.Equals(channel, AuxInstallService.ChannelCustom, StringComparison.OrdinalIgnoreCase))
        {
            CrashReporter.Log("[VulkanLayerService.SyncLayerToChannel] Custom channel — layer left as picked");
            return false;
        }

        bool changed = false;
        foreach (var (staged, layer) in LayerPairs(channel))
        {
            var name = Path.GetFileName(layer);
            if (!File.Exists(layer)) continue; // 32-bit layer is optional; 64-bit absent = layer not installed
            if (!IsValidStaged(staged))
            {
                CrashReporter.Log($"[VulkanLayerService.SyncLayerToChannel] {channel}: staged '{staged}' missing or too small — {name} left unchanged");
                continue;
            }
            if (FilesIdentical(staged, layer)) continue;
            try
            {
                AuxInstallService.CopyFileWithElevation(staged, layer);
                changed = true;
                CrashReporter.Log($"[VulkanLayerService.SyncLayerToChannel] {name} → {channel} ({AuxInstallService.ReadInstalledVersion(LayerDirectory, name) ?? "?"})");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                // IOException = a Vulkan game is running; Win32Exception = UAC prompt declined
                CrashReporter.Log($"[VulkanLayerService.SyncLayerToChannel] Failed to copy {name} for {channel} — {ex.Message}");
            }
        }
        return changed;
    }

    private static IEnumerable<(string staged, string layer)> LayerPairs(string channel)
    {
        yield return (AuxInstallService.GetStagedPathForChannel(channel, false), LayerDllPath);
        yield return (AuxInstallService.GetStagedPathForChannel(channel, true), Path.Combine(LayerDirectory, "ReShade32.dll"));
    }

    private static bool IsValidStaged(string path) =>
        File.Exists(path) && new FileInfo(path).Length > AuxInstallService.MinReShadeSize;

    internal static bool FilesIdentical(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            using var sa = File.OpenRead(a);
            using var sb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var ha = System.Security.Cryptography.SHA256.HashData(sa);
            var hb = System.Security.Cryptography.SHA256.HashData(sb);
            return ha.AsSpan().SequenceEqual(hb);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[VulkanLayerService.FilesIdentical] Could not compare '{a}' and '{b}' — {ex.Message}");
            return true; // can't read = don't try to overwrite
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

    // ── Off-switch variable ───────────────────────────────────────────────────────

    // The Vulkan loader requires every implicit layer to name an environment variable that turns it
    // off ("disable_environment"). ReShade's installer uses DISABLE_VK_LAYER_reshade_1, and DOOM
    // Eternal sets exactly that variable to block ReShade. RHI names it _2 instead (crosire's own
    // workaround, ReShade forum thread 9638): no game needs the variable for ReShade to work, so
    // only programs that set _1 to keep ReShade out are affected.
    public const string OffSwitch = "DISABLE_VK_LAYER_reshade_2";
    internal const string StockOffSwitch = "DISABLE_VK_LAYER_reshade_1";

    /// <summary>The layer manifest text with ReShade's stock off-switch renamed, or null when it has none.</summary>
    internal static string? RenameOffSwitch(string json) =>
        json.Contains("\"" + StockOffSwitch + "\"", StringComparison.Ordinal)
            ? json.Replace("\"" + StockOffSwitch + "\"", "\"" + OffSwitch + "\"", StringComparison.Ordinal)
            : null;

    /// <summary>
    /// Renames the stock off-switch in an installed layer manifest (installed by older RHI versions or
    /// ReShade's own installer). Only that one entry changes. Elevates if needed. Run off the UI thread.
    /// </summary>
    public static void EnsureOffSwitchRenamed()
    {
        if (!File.Exists(LayerManifestPath)) return;
        string? tmp = null;
        try
        {
            var updated = RenameOffSwitch(File.ReadAllText(LayerManifestPath));
            if (updated == null) return;
            tmp = Path.Combine(Path.GetTempPath(), $"rhi_{LayerManifestName}");
            File.WriteAllText(tmp, updated);
            AuxInstallService.CopyFileWithElevation(tmp, LayerManifestPath);
            CrashReporter.Log($"[VulkanLayerService.EnsureOffSwitchRenamed] {StockOffSwitch} -> {OffSwitch} in {LayerManifestPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            CrashReporter.Log($"[VulkanLayerService.EnsureOffSwitchRenamed] Failed — {ex.Message}");
        }
        finally
        {
            if (tmp != null)
            {
                try { File.Delete(tmp); }
                catch (IOException ex) { CrashReporter.Log($"[VulkanLayerService.EnsureOffSwitchRenamed] Temp file left — {ex.Message}"); }
            }
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
                    "description": "ReShade",
                    "disable_environment": {
                        "{{OffSwitch}}": "1"
                    }
                }
            }
            """;
    }
}
