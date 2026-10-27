using Microsoft.Win32;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages per-game Windows Auto HDR via two registry locations:
///
/// 1. FORCE ENABLE — HKCU\Software\Microsoft\Direct3D\{ApplicationN}\
///    Subkey name: auto-assigned (e.g. "Application0", "Application1", ...)
///    Values:  Name         = REG_SZ  full exe path
///             D3DBehaviors = REG_SZ  "BufferUpgradeOverride=1;BufferUpgradeEnable10Bit=1"
///    This is the mechanism used by ledoge/autohdr_force. Forces the game into the
///    Auto HDR pipeline regardless of Microsoft's compatibility list.
///
/// 2. BRIGHTNESS — HKCU\Software\Microsoft\DirectX\UserGpuPreferences
///    Value name: full exe path (REG_SZ)
///    Value data: semicolon-delimited flags, e.g. "AutoHDRStrength=75;AppStatus=4096;"
///    This controls the intensity slider (0–100 → 0 to ~1000 nits peak).
///    Other flags (AppStatus, VRROptimizeEnable, etc.) are preserved.
/// </summary>
public class AutoHdrService : IAutoHdrService
{
    // ── Registry paths ────────────────────────────────────────────────────────
    private const string Direct3DPath      = @"Software\Microsoft\Direct3D";
    private const string UserGpuPrefPath   = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string DBehaviors        = "BufferUpgradeOverride=1;BufferUpgradeEnable10Bit=1";
    private const string StrengthKey       = "AutoHDRStrength";

    private static readonly HashSet<string> _exeExclusions = new(StringComparer.OrdinalIgnoreCase)
        { "unins000", "UnityCrashHandler64", "UnityCrashHandler32", "CrashReporter", "launcher" };

    // ── Exe resolution ────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the full exe path for Auto HDR registry writes.
    /// Priority: user override → manifest override → largest exe in installPath.
    /// </summary>
    public string? ResolveExePath(string gameName, string installPath,
        Dictionary<string, string>? userLaunchExeOverrides,
        Dictionary<string, string>? manifestLaunchExeOverrides)
    {
        if (string.IsNullOrEmpty(installPath)) return null;

        // 1. User override — absolute path
        if (userLaunchExeOverrides?.TryGetValue(gameName, out var userExe) == true
            && !string.IsNullOrEmpty(userExe) && File.Exists(userExe))
        {
            CrashReporter.Log($"[AutoHdrService.ResolveExePath] User override: '{userExe}' for '{gameName}'");
            return userExe;
        }

        // 2. Manifest override — relative from installPath
        if (manifestLaunchExeOverrides?.TryGetValue(gameName, out var manifestExe) == true
            && !string.IsNullOrEmpty(manifestExe))
        {
            var combined = Path.Combine(installPath, manifestExe.TrimStart('/', '\\'));
            if (File.Exists(combined))
            {
                CrashReporter.Log($"[AutoHdrService.ResolveExePath] Manifest override: '{combined}' for '{gameName}'");
                return combined;
            }
        }

        // 3. Largest exe in installPath
        if (!Directory.Exists(installPath)) return null;
        try
        {
            var best = Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(e => !_exeExclusions.Contains(Path.GetFileNameWithoutExtension(e)))
                .OrderByDescending(e => new FileInfo(e).Length)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(best))
            {
                CrashReporter.Log($"[AutoHdrService.ResolveExePath] Largest exe: '{best}' for '{gameName}'");
                return best;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AutoHdrService.ResolveExePath] Exe scan failed for '{gameName}': {ex.Message}");
        }

        return null;
    }

    // ── Direct3D subkey helpers (force enable) ────────────────────────────────

    /// <summary>
    /// Finds the Direct3D Application subkey that has Name = exePath, or null.
    /// </summary>
    private static RegistryKey? FindDirect3DSubkey(RegistryKey direct3DKey, string exePath)
    {
        foreach (var subName in direct3DKey.GetSubKeyNames())
        {
            var sub = direct3DKey.OpenSubKey(subName, writable: false);
            if (sub == null) continue;
            var name = sub.GetValue("Name") as string;
            sub.Close();
            if (string.Equals(name, exePath, StringComparison.OrdinalIgnoreCase))
            {
                return direct3DKey.OpenSubKey(subName, writable: true);
            }
        }
        return null;
    }

    /// <summary>
    /// Finds a free "ApplicationN" subkey name under Direct3D (Application0, Application1, …).
    /// </summary>
    private static string FindFreeApplicationSubkey(RegistryKey direct3DKey)
    {
        for (int i = 0; i < 1000; i++)
        {
            var candidate = $"Application{i}";
            if (!direct3DKey.GetSubKeyNames().Contains(candidate))
                return candidate;
        }
        return $"Application{Guid.NewGuid():N}"; // fallback
    }

    private static void WriteForceEnable(string exePath)
    {
        using var d3dKey = Registry.CurrentUser.CreateSubKey(Direct3DPath, writable: true);
        if (d3dKey == null) return;

        // Find existing subkey for this exe or create a new one
        var sub = FindDirect3DSubkey(d3dKey, exePath);
        if (sub == null)
        {
            var newName = FindFreeApplicationSubkey(d3dKey);
            sub = d3dKey.CreateSubKey(newName, writable: true);
        }
        if (sub == null) return;

        sub.SetValue("Name", exePath, RegistryValueKind.String);
        sub.SetValue("D3DBehaviors", DBehaviors, RegistryValueKind.String);
        sub.Close();
        CrashReporter.Log($"[AutoHdrService.WriteForceEnable] Wrote Direct3D subkey for '{exePath}'");
    }

    private static void RemoveForceEnable(string exePath)
    {
        using var d3dKey = Registry.CurrentUser.OpenSubKey(Direct3DPath, writable: true);
        if (d3dKey == null) return;

        foreach (var subName in d3dKey.GetSubKeyNames().ToArray())
        {
            using var sub = d3dKey.OpenSubKey(subName, writable: false);
            if (sub == null) continue;
            var name = sub.GetValue("Name") as string;
            if (string.Equals(name, exePath, StringComparison.OrdinalIgnoreCase))
            {
                sub.Close();
                d3dKey.DeleteSubKeyTree(subName, throwOnMissingSubKey: false);
                CrashReporter.Log($"[AutoHdrService.RemoveForceEnable] Deleted Direct3D subkey '{subName}' for '{exePath}'");
                return;
            }
        }
    }

    private static bool HasForceEnable(string exePath)
    {
        using var d3dKey = Registry.CurrentUser.OpenSubKey(Direct3DPath, writable: false);
        if (d3dKey == null) return false;
        using var sub = FindDirect3DSubkey(d3dKey, exePath);
        return sub != null;
    }

    // ── UserGpuPreferences helpers (strength) ─────────────────────────────────

    private static Dictionary<string, string> ParseFlags(string raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw)) return dict;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx > 0) dict[part[..idx].Trim()] = part[(idx + 1)..].Trim();
        }
        return dict;
    }

    private static string SerialiseFlags(Dictionary<string, string> flags)
        => string.Join(";", flags.Select(kv => $"{kv.Key}={kv.Value}")) + (flags.Count > 0 ? ";" : "");

    private static string? ReadGpuPref(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserGpuPrefPath, writable: false);
            return key?.GetValue(exePath) as string;
        }
        catch { return null; }
    }

    private static void WriteGpuPref(string exePath, string data)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UserGpuPrefPath, writable: true);
            key?.SetValue(exePath, data, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AutoHdrService.WriteGpuPref] Failed for '{exePath}': {ex.Message}");
        }
    }

    private static void RemoveStrength(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UserGpuPrefPath, writable: true);
            if (key == null) return;
            var raw = key.GetValue(exePath) as string;
            if (raw == null) return;

            var flags = ParseFlags(raw);
            flags.Remove(StrengthKey);
            flags.Remove("AutoHDREnable"); // clean up legacy entries written by old RHI versions

            if (flags.Count == 0)
                key.DeleteValue(exePath, throwOnMissingValue: false);
            else
                key.SetValue(exePath, SerialiseFlags(flags), RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AutoHdrService.RemoveStrength] Failed for '{exePath}': {ex.Message}");
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns whether Auto HDR is currently forced for the given exe path.</summary>
    public bool IsEnabled(string exePath) => HasForceEnable(exePath);

    /// <summary>Returns the current AutoHDRStrength (0–100), or -1 if not set.</summary>
    public int GetStrength(string exePath)
    {
        var raw = ReadGpuPref(exePath);
        if (raw == null) return -1;
        var flags = ParseFlags(raw);
        if (flags.TryGetValue(StrengthKey, out var v) && int.TryParse(v, out var s))
            return Math.Clamp(s, 0, 100);
        return -1;
    }

    /// <summary>
    /// Enables Auto HDR: writes the Direct3D force-enable subkey AND the strength value.
    /// </summary>
    public bool Enable(string exePath, int strength = 50)
    {
        strength = Math.Clamp(strength, 0, 100);
        try
        {
            // 1. Force-enable via Direct3D subkey
            WriteForceEnable(exePath);

            // 2. Strength via UserGpuPreferences (preserves other flags)
            var raw = ReadGpuPref(exePath) ?? "";
            var flags = ParseFlags(raw);
            flags[StrengthKey] = strength.ToString();
            WriteGpuPref(exePath, SerialiseFlags(flags));

            CrashReporter.Log($"[AutoHdrService.Enable] '{exePath}' strength={strength}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AutoHdrService.Enable] Failed for '{exePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Disables Auto HDR: removes the Direct3D subkey and the strength flag.
    /// </summary>
    public bool Disable(string exePath)
    {
        try
        {
            RemoveForceEnable(exePath);
            RemoveStrength(exePath);
            CrashReporter.Log($"[AutoHdrService.Disable] Removed Auto HDR for '{exePath}'");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AutoHdrService.Disable] Failed for '{exePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Updates only the strength value without changing the enable state.</summary>
    public bool SetStrength(string exePath, int strength)
    {
        strength = Math.Clamp(strength, 0, 100);
        try
        {
            var raw = ReadGpuPref(exePath) ?? "";
            var flags = ParseFlags(raw);
            flags[StrengthKey] = strength.ToString();
            WriteGpuPref(exePath, SerialiseFlags(flags));
            CrashReporter.Log($"[AutoHdrService.SetStrength] '{exePath}' strength={strength}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AutoHdrService.SetStrength] Failed for '{exePath}': {ex.Message}");
            return false;
        }
    }
}
