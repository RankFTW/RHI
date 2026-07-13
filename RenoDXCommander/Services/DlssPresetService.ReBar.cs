using System.Diagnostics;
using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvAPIWrapper.DRS;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class DlssPresetService
{
    // ── Get render scale ──────────────────────────────────────────────────────

    /// <summary>Returns the current SR render scale percentage (0 = Off/Default, 33-100 = active).</summary>
    public uint GetSrRenderScale(string gameName, string installPath)
    {
        var mode = GetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID);
        if (mode != RENDER_SCALE_CUSTOM) return 0;
        return GetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID);
    }

    /// <summary>Returns the current RR render scale percentage (0 = Off/Default, 33-100 = active).</summary>
    public uint GetRrRenderScale(string gameName, string installPath)
    {
        var mode = GetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_ID);
        if (mode != RENDER_SCALE_CUSTOM) return 0;
        return GetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID);
    }

    // ── Set render scale ──────────────────────────────────────────────────────

    /// <summary>Sets the SR render scale. 0 = reset to Default (delete from profile). 33-100 = set custom percentage.</summary>
    public bool SetSrRenderScale(string gameName, string installPath, uint percentage)
    {
        if (percentage == 0)
        {
            DeletePreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID);
            DeletePreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID);
            return true;
        }
        SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID, RENDER_SCALE_CUSTOM);
        return SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID, percentage);
    }

    /// <summary>Sets the RR render scale. 0 = reset to Default (delete from profile). 33-100 = set custom percentage.</summary>
    public bool SetRrRenderScale(string gameName, string installPath, uint percentage)
    {
        if (percentage == 0)
        {
            DeletePreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_ID);
            DeletePreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID);
            return true;
        }
        SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_ID, RENDER_SCALE_CUSTOM);
        return SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID, percentage);
    }

    // ── ReBAR (Resizable BAR) ─────────────────────────────────────────────────

    private const uint REBAR_FEATURE_ID = 0x000F00BA;
    private const uint REBAR_EXPR_MODES_ID = 0x00C09D09;
    private const uint REBAR_SIZE_LIMIT_ID = 0x000F00FF;

    /// <summary>ReBAR mode options. Standard = Mode 0, Optimized = Mode 2.</summary>
    public static readonly (string Name, uint Value)[] ReBarModes =
    [
        ("Standard", 0x00000000),
        ("Optimized", 0x00000002),
    ];

    /// <summary>ReBAR size limit options. Value is the size in bytes as a 64-bit integer.</summary>
    public static readonly (string Name, ulong Value)[] ReBarSizeLimits =
    [
        ("512MB", 0x0000000020000000),
        ("1GB (Default)", 0x0000000040000000),
        ("1.5GB", 0x0000000060000000),
        ("2GB", 0x0000000080000000),
        ("4GB", 0x0000000100000000),
    ];

    /// <summary>Returns true if ReBAR Feature is enabled for this game's NVIDIA profile.</summary>
    public bool GetReBarEnabled(string gameName, string installPath)
        => GetPreset(gameName, installPath, REBAR_FEATURE_ID) != 0;

    /// <summary>Returns the ReBAR Expr Mode value (0 = Standard, 2 = Optimized).</summary>
    public uint GetReBarMode(string gameName, string installPath)
        => GetPreset(gameName, installPath, REBAR_EXPR_MODES_ID);

    /// <summary>Returns the ReBAR Size Limit in bytes (0 = not set / use driver default).</summary>
    public ulong GetReBarSizeLimit(string gameName, string installPath)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return 0;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null) { CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] No profile for '{gameName}'"); return 0; }

            // Try to read binary setting via NvAPIWrapper directly.
            try
            {
                var setting = profile.GetSetting(REBAR_SIZE_LIMIT_ID);
                if (setting.CurrentValue is byte[] bytes && bytes.Length >= 8)
                {
                    var val = BitConverter.ToUInt64(bytes, 0);
                    CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] Read 0x{val:X16} for '{gameName}'");
                    return val;
                }
                if (setting.CurrentValue is uint dwordVal && dwordVal != 0)
                {
                    CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] Read DWORD 0x{dwordVal:X8} for '{gameName}'");
                    return dwordVal;
                }
                CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] Setting found but value type={setting.CurrentValue?.GetType().Name ?? "null"} for '{gameName}'");
            }
            catch (Exception innerEx)
            {
                CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] GetSetting threw for '{gameName}' — {innerEx.Message}");
                // Fallback: use PS helper to read
                var psVal = ReadReBarSizeLimitViaPs(gameName);
                if (psVal != 0) return psVal;
            }

            // Fallback: raw NVAPI reads DWORD position which doesn't work for BINARY
            return 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] Error for '{gameName}' — {ex.Message}");
            return 0;
        }
    }

    /// <summary>Reads ReBAR Size Limit via a PowerShell helper (fallback for drivers where GetSetting throws).</summary>
    private ulong ReadReBarSizeLimitViaPs(string gameName)
    {
        // If we've previously written a value for this game, use our local cache
        if (_rebarSizeLimitCache.TryGetValue(gameName, out var cached))
        {
            CrashReporter.Log($"[DlssPresetService.ReadReBarSizeLimitViaPs] Using cached value 0x{cached:X16} for '{gameName}'");
            return cached;
        }
        return 0;
    }

    // Local cache for ReBAR size limits written during this session
    private readonly Dictionary<string, ulong> _rebarSizeLimitCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Enables or disables ReBAR for a game. When enabling, also sets Mode to the specified value.</summary>
    public bool SetReBarEnabled(string gameName, string installPath, bool enabled, uint mode = 0x00000000)
    {
        CrashReporter.Log($"[DlssPresetService.SetReBarEnabled] gameName='{gameName}', enabled={enabled}, mode=0x{mode:X8}");
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return false;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null)
            {
                if (!AutoCreateProfiles) return false;
                profile = CreateProfileForGame(gameName, installPath);
                if (profile == null) return false;
            }
            else if (AutoCreateProfiles && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                EnsureExeRegistered(profile, gameName, installPath);
            }

            uint featureVal = enabled ? 1u : 0u;

            // Set Feature flag, Expr Modes, and Size Limit (default 1GB)
            profile.SetSetting(REBAR_FEATURE_ID, featureVal);
            if (enabled)
            {
                profile.SetSetting(REBAR_EXPR_MODES_ID, mode);
                // Also set Size Limit to 1GB default if not already set
                var existingSizeSetting = profile.Settings.FirstOrDefault(s => s.SettingId == REBAR_SIZE_LIMIT_ID);
                if (existingSizeSetting == null)
                    profile.SetSetting(REBAR_SIZE_LIMIT_ID, BitConverter.GetBytes(0x0000000040000000UL));
            }
            else
            {
                profile.SetSetting(REBAR_EXPR_MODES_ID, 0u);
            }
            _session.Save();

            CrashReporter.Log($"[DlssPresetService.SetReBarEnabled] Set ReBAR Feature=0x{featureVal:X8}, Mode=0x{mode:X8} for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("INVALID_USER_PRIVILEGE"))
            {
                // Requires elevation — use helper process
                CrashReporter.Log($"[DlssPresetService.SetReBarEnabled] Requires elevation, launching elevated helper...");
                return SetReBarElevated(gameName, installPath, enabled, mode);
            }
            CrashReporter.Log($"[DlssPresetService.SetReBarEnabled] Error for '{gameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Sets ReBAR via an elevated process that re-invokes NVAPI with admin rights.</summary>
    private bool SetReBarElevated(string gameName, string installPath, bool enabled, uint mode)
    {
        try
        {
            // Resolve the actual NVIDIA profile name (may differ from RHI game name)
            var matchedProfile = FindProfile(gameName, installPath);
            var profileName = matchedProfile?.Name ?? gameName;

            // Build command: use PowerShell to load NVAPI and set the settings with admin
            var featureVal = enabled ? 1u : 0u;
            var modeVal = enabled ? mode : 0u;

            // Write a temporary script that sets the NVAPI values
            var scriptPath = Path.Combine(Path.GetTempPath(), "rhi_rebar_set.ps1");
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var nvApiPath = Path.Combine(AppContext.BaseDirectory, "NvAPIWrapper.dll");

            var script = $@"
Add-Type -Path '{nvApiPath.Replace("'", "''")}'
[NvAPIWrapper.NVIDIA]::Initialize()
$session = [NvAPIWrapper.DRS.DriverSettingsSession]::CreateAndLoad()
$profile = $null
foreach ($p in $session.Profiles) {{
    if ($p.Name -eq '{profileName.Replace("'", "''")}') {{ $profile = $p; break }}
}}
if ($null -eq $profile) {{
    foreach ($p in $session.Profiles) {{
        foreach ($app in $p.Applications) {{
            # Try to find by install path
        }}
    }}
}}
if ($null -ne $profile) {{
    $profile.SetSetting([uint32]0x000F00BA, [uint32]{featureVal})
    $profile.SetSetting([uint32]0x00C09D09, [uint32]{modeVal})
    [byte[]]$sizeBytes = @(0x00,0x00,0x00,0x40,0x00,0x00,0x00,0x00)
    $existingSize = $profile.Settings | Where-Object {{ $_.SettingId -eq 0x000F00FF }}
    if ($null -eq $existingSize -and {featureVal} -eq 1) {{
        $profile.SetSetting([uint32]0x000F00FF, $sizeBytes)
    }}
    $session.Save()
    Write-Host 'OK'
}} else {{
    Write-Host 'PROFILE_NOT_FOUND'
}}
";
            File.WriteAllText(scriptPath, script);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(10000);

            // Clean up
            try { File.Delete(scriptPath); } catch { }

            // Reload session to reflect changes made by the elevated process
            _session = DriverSettingsSession.CreateAndLoad();
            _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _session.Profiles)
                _cachedProfiles.TryAdd(p.Name, p);
            InvalidateProfileLookupCache();

            CrashReporter.Log($"[DlssPresetService.SetReBarElevated] Elevated process completed for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetReBarElevated] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Sets the ReBAR Expr Mode (0 = Standard, 2 = Optimized). Only meaningful when ReBAR is enabled.</summary>
    public bool SetReBarMode(string gameName, string installPath, uint mode)
    {
        var result = SetPreset(gameName, installPath, REBAR_EXPR_MODES_ID, mode);
        if (!result)
        {
            // Likely privilege error — try elevated
            CrashReporter.Log($"[DlssPresetService.SetReBarMode] Direct set failed, trying elevated for '{gameName}'");
            return SetReBarElevated(gameName, installPath, true, mode);
        }
        return result;
    }

    /// <summary>Sets the ReBAR Size Limit. Pass 0 to clear (revert to driver default). Value is size in bytes.</summary>
    public bool SetReBarSizeLimit(string gameName, string installPath, ulong sizeBytes)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return false;

        // NvAPIWrapper's in-process binary SetSetting is broken (writes wrong struct layout).
        // Always use the PowerShell helper which calls NvAPIWrapper from a fresh process — this works correctly.
        return SetReBarSizeLimitElevated(gameName, installPath, sizeBytes);
    }

    /// <summary>Sets ReBAR Size Limit via an elevated PowerShell process.</summary>
    private bool SetReBarSizeLimitElevated(string gameName, string installPath, ulong sizeBytes)
    {
        try
        {
            // Resolve the actual NVIDIA profile name (may differ from RHI game name)
            var profile = FindProfile(gameName, installPath);
            var profileName = profile?.Name ?? gameName;

            var scriptPath = Path.Combine(Path.GetTempPath(), "rhi_rebar_size.ps1");
            var nvApiPath = Path.Combine(AppContext.BaseDirectory, "NvAPIWrapper.dll");
            var hexBytes = BitConverter.ToString(BitConverter.GetBytes(sizeBytes)).Replace("-", ",0x");

            var script = $@"
Add-Type -Path '{nvApiPath.Replace("'", "''")}'
[NvAPIWrapper.NVIDIA]::Initialize()
$session = [NvAPIWrapper.DRS.DriverSettingsSession]::CreateAndLoad()
$profile = $null
foreach ($p in $session.Profiles) {{
    if ($p.Name -eq '{profileName.Replace("'", "''")}') {{ $profile = $p; break }}
}}
if ($null -ne $profile) {{
    [byte[]]$bytes = @(0x{hexBytes})
    $profile.SetSetting([uint32]0x000F00FF, $bytes)
    $session.Save()
}}
";
            File.WriteAllText(scriptPath, script);
            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimitElevated] Script: bytes=0x{hexBytes}, game='{gameName}'");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                CrashReporter.Log("[DlssPresetService.SetReBarSizeLimitElevated] Process.Start returned null");
                return false;
            }
            process.WaitForExit(10000);
            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimitElevated] PS exited with code {process.ExitCode}");
            try { File.Delete(scriptPath); } catch { }

            _session = DriverSettingsSession.CreateAndLoad();
            _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _session.Profiles)
                _cachedProfiles.TryAdd(p.Name, p);
            InvalidateProfileLookupCache();

            // Cache the written value so reads work even if GetSetting throws on newer drivers
            _rebarSizeLimitCache[gameName] = sizeBytes;

            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimitElevated] Done for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimitElevated] Failed — {ex.Message}");
            return false;
        }
    }

}
