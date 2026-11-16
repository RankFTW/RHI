// BfiPresenterService.VulkanLayer.cs -- the BFI Presenter Vulkan layer, installed exactly as the author's
// install_vulkan_layer.bat does it, so RHI, the bat and the BFI Presenter Manager share one install:
//   files    %LOCALAPPDATA%\BFIPresenter\Vulkan\ (BFIPresenterVK.dll, BFIPresenterVK32.dll, VkLayer_BFI_presenter.json,
//            VkLayer_BFI_presenter32.json; the JSONs point at the DLLs beside them)
//   registry HKCU\SOFTWARE\Khronos\Vulkan\ImplicitLayers, value name = the JSON's full path, REG_DWORD 0. Also HKLM
//            ("all users") when games run as administrator: Windows' Vulkan loader ignores per-user layers in
//            elevated programs.
// Load order: machine-wide layers before per-user ones, and within one key the order the values were added.
// ReShade must load before BFI Presenter (BFI Presenter then sits below ReShade and strobes ReShade's output), so
// BFI Presenter's values are removed and added again to put them last, as the bat does.

using System.Diagnostics;
using Microsoft.Win32;

namespace RenoDXCommander.Services;

public partial class BfiPresenterService
{
    public const string LayerDll64  = "BFIPresenterVK.dll";
    public const string LayerDll32  = "BFIPresenterVK32.dll";
    public const string LayerJson64 = "VkLayer_BFI_presenter.json";
    public const string LayerJson32 = "VkLayer_BFI_presenter32.json";
    private const string ImplicitLayersKey = @"SOFTWARE\Khronos\Vulkan\ImplicitLayers";

    public static string LayerDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BFIPresenter", "Vulkan");

    private static string[] LayerValueNames => new[]
    {
        Path.Combine(LayerDir, LayerJson64),
        Path.Combine(LayerDir, LayerJson32),
    };

    public static bool LayerFilesPresent =>
        new[] { LayerDll64, LayerDll32, LayerJson64, LayerJson32 }.All(f => File.Exists(Path.Combine(LayerDir, f)));

    public static bool IsLayerRegisteredForUser => IsRegistered(Registry.CurrentUser);
    public static bool IsLayerRegisteredForAllUsers => IsRegistered(Registry.LocalMachine);

    /// <summary>Files in place and registered for this user or for all users.</summary>
    public static bool IsLayerInstalled => LayerFilesPresent && (IsLayerRegisteredForUser || IsLayerRegisteredForAllUsers);

    private static bool IsRegistered(RegistryKey hive)
    {
        try
        {
            using var key = hive.OpenSubKey(ImplicitLayersKey);
            if (key == null) return false;
            var names = key.GetValueNames();
            return LayerValueNames.All(v => names.Contains(v, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            CrashReporter.Log($"[BfiPresenterService.IsRegistered] {ex.Message}");
            return false;
        }
    }

    /// <summary>True when the layer's files differ from the staged ones (an update is waiting).</summary>
    public bool LayerNeedsUpdate =>
        IsStagingReady(BfiRoute.Vulkan) && LayerFilesPresent
        && new[] { LayerDll64, LayerDll32, LayerJson64, LayerJson32 }
            .Any(f => !VulkanLayerService.FilesIdentical(StagedPath(BfiRoute.Vulkan, f), Path.Combine(LayerDir, f)));

    /// <summary>
    /// Copies the staged layer into LayerDir and registers it for this user (and for all users when RHI runs as
    /// administrator), after ReShade. Returns an error message, or null on success. Files in use (a Vulkan game
    /// running) are reported, not forced.
    /// </summary>
    public string? InstallOrUpdateLayer()
    {
        if (!IsStagingReady(BfiRoute.Vulkan)) return "The Vulkan layer couldn't be downloaded.";
        try
        {
            Directory.CreateDirectory(LayerDir);
            foreach (var f in new[] { LayerDll64, LayerDll32, LayerJson64, LayerJson32 })
            {
                var src = StagedPath(BfiRoute.Vulkan, f);
                var dst = Path.Combine(LayerDir, f);
                if (File.Exists(dst) && VulkanLayerService.FilesIdentical(src, dst)) continue;
                File.Copy(src, dst, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[BfiPresenterService.InstallOrUpdateLayer] Copy failed — {ex.Message}");
            return "The Vulkan layer's files are in use. Close any running Vulkan game and try again.";
        }

        if (!RegisterLast(Registry.CurrentUser, force: true))
            return "Couldn't register the Vulkan layer for your Windows user.";
        if (VulkanLayerService.IsRunningAsAdmin())
            RegisterLast(Registry.LocalMachine, force: true); // games run as administrator need it there too
        _crashReporter.Log($"[BfiPresenterService.InstallOrUpdateLayer] Layer {StagedVersion(BfiRoute.Vulkan)} in '{LayerDir}' (all users: {IsLayerRegisteredForAllUsers})");
        return null;
    }

    /// <summary>
    /// Adds the layer's values to <paramref name="hive"/> as the last entries. Without <paramref name="force"/> it only
    /// acts when they're registered there and a ReShade layer is listed after them. True when they're registered.
    /// </summary>
    private bool RegisterLast(RegistryKey hive, bool force)
    {
        try
        {
            using var key = hive.CreateSubKey(ImplicitLayersKey, writable: true);
            if (key == null) return false;
            if (!force && !NeedsReorder(key)) return IsRegistered(hive);
            foreach (var v in LayerValueNames)
            {
                if (key.GetValueNames().Contains(v, StringComparer.OrdinalIgnoreCase)) key.DeleteValue(v, throwOnMissingValue: false);
                key.SetValue(v, 0, RegistryValueKind.DWord);
            }
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _crashReporter.Log($"[BfiPresenterService.RegisterLast] {(hive == Registry.LocalMachine ? "HKLM" : "HKCU")} — {ex.Message}");
            return false;
        }
    }

    /// <summary>A ReShade layer value listed after BFI Presenter's in the same key (wrong load order).</summary>
    private static bool NeedsReorder(RegistryKey key)
    {
        var names = key.GetValueNames();
        int bfi = Array.FindIndex(names, n => LayerValueNames.Contains(n, StringComparer.OrdinalIgnoreCase));
        if (bfi < 0) return false;
        return names.Skip(bfi + 1).Any(n => Path.GetFileName(n).Contains("ReShade", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ReShade listed after BFI Presenter in a key where both are registered (ReShade reinstalled since). The
    /// per-user key is fixed here; the machine-wide one needs administrator rights (false when it couldn't be fixed).
    /// </summary>
    public bool FixLayerOrder()
    {
        bool ok = true;
        try
        {
            using (var cu = Registry.CurrentUser.OpenSubKey(ImplicitLayersKey))
                if (cu != null && NeedsReorder(cu)) ok &= RegisterLast(Registry.CurrentUser, force: true);
            using (var lm = Registry.LocalMachine.OpenSubKey(ImplicitLayersKey))
                if (lm != null && NeedsReorder(lm))
                    ok &= VulkanLayerService.IsRunningAsAdmin() && RegisterLast(Registry.LocalMachine, force: true);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _crashReporter.Log($"[BfiPresenterService.FixLayerOrder] {ex.Message}");
            return false;
        }
        if (!ok) _crashReporter.Log("[BfiPresenterService.FixLayerOrder] ReShade is listed after BFI Presenter for all users; needs administrator rights to fix");
        return ok;
    }

    /// <summary>True when ReShade is listed after BFI Presenter in either key.</summary>
    public static bool LayerOrderWrong
    {
        get
        {
            try
            {
                using var cu = Registry.CurrentUser.OpenSubKey(ImplicitLayersKey);
                using var lm = Registry.LocalMachine.OpenSubKey(ImplicitLayersKey);
                return (cu != null && NeedsReorder(cu)) || (lm != null && NeedsReorder(lm));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Registers (or with <paramref name="remove"/> unregisters) the layer for all users. Direct when RHI runs as
    /// administrator, otherwise through reg.exe in an elevated prompt (one UAC prompt). True when it ended as asked.
    /// </summary>
    public async Task<bool> SetAllUsersAsync(bool remove)
    {
        if (VulkanLayerService.IsRunningAsAdmin())
        {
            if (remove) return RemoveValues(Registry.LocalMachine);
            return RegisterLast(Registry.LocalMachine, force: true);
        }
        var key = @"HKLM\" + ImplicitLayersKey;
        var cmds = new List<string>();
        foreach (var v in LayerValueNames)
        {
            cmds.Add($"reg delete \"{key}\" /v \"{v}\" /f >nul 2>&1");
            if (!remove) cmds.Add($"reg add \"{key}\" /v \"{v}\" /t REG_DWORD /d 0 /f >nul");
        }
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/c " + string.Join(" & ", cmds),
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p != null) await p.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _crashReporter.Log($"[BfiPresenterService.SetAllUsers] Elevation declined — {ex.Message}");
            return false;
        }
        bool done = remove ? !IsLayerRegisteredForAllUsers : IsLayerRegisteredForAllUsers;
        _crashReporter.Log($"[BfiPresenterService.SetAllUsers] {(remove ? "Unregister" : "Register")} for all users: {(done ? "done" : "failed")}");
        return done;
    }

    /// <summary>
    /// Removes the layer from this PC (registry values and files). Every game using it, including games set up with
    /// the BFI Presenter Manager, stops using it. The all-users registration needs administrator rights.
    /// </summary>
    public async Task<string?> UninstallLayerAsync()
    {
        RemoveValues(Registry.CurrentUser);
        if (IsLayerRegisteredForAllUsers && !await SetAllUsersAsync(remove: true).ConfigureAwait(false))
            return "The layer is still registered for all users: that needs administrator rights.";
        try
        {
            foreach (var f in new[] { LayerDll64, LayerDll32, LayerJson64, LayerJson32 })
            {
                var p = Path.Combine(LayerDir, f);
                if (File.Exists(p)) File.Delete(p);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[BfiPresenterService.UninstallLayer] {ex.Message}");
            return "The layer was unregistered, but its files are in use. Close any running Vulkan game.";
        }
        _crashReporter.Log("[BfiPresenterService.UninstallLayer] Removed");
        return null;
    }

    private bool RemoveValues(RegistryKey hive)
    {
        try
        {
            using var key = hive.OpenSubKey(ImplicitLayersKey, writable: true);
            if (key == null) return true;
            foreach (var v in LayerValueNames) key.DeleteValue(v, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _crashReporter.Log($"[BfiPresenterService.RemoveValues] {ex.Message}");
            return false;
        }
    }
}
