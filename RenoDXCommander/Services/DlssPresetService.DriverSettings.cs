using System.Diagnostics;
using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvAPIWrapper.DRS;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class DlssPresetService
{
    // ── Nvidia Profile Overrides ─────────────────────────────────────────────

    // VSync settings
    private const uint VSYNC_MODE_ID = 0x00A879CF;
    private const uint VSYNC_TEAR_CONTROL_ID = 0x005A375C;

    // Power / CPU settings
    private const uint POWER_MANAGEMENT_MODE_ID = 0x1057EB71;
    private const uint CPU_EXPR_MODES_ID = 0x105E2A1D;

    // Latency settings
    private const uint LATENCY_ULL_ENABLE_ID = 0x10835000;
    private const uint LATENCY_ULL_MODE_ID = 0x0005F543;
    private const uint LATENCY_MAX_PRE_RENDERED_FRAMES_ID = 0x007BA09E;

    // Smooth Motion settings
    private const uint SMOOTH_MOTION_ENABLE_ID = 0xB0D384C0;
    private const uint SMOOTH_MOTION_APIS_ID = 0xB0CC0875;
    private const uint SMOOTH_MOTION_FLIP_PACING_FS_ID = 0xB03A4546;
    private const uint SMOOTH_MOTION_FLIP_PACING_WIN_ID = 0xB03A4547;

    // Shader Cache settings (global profile)
    private const uint SHADER_CACHE_SIZE_ID = 0x00AC8497;
    private const uint SHADER_PRECOMPILE_ID = 0x00EAD189;

    // G-Sync settings (global profile)
    private const uint GSYNC_APP_MODE_ID = 0x1194F158;
    private const uint GSYNC_GLOBAL_MODE_ID = 0x1094F1F7;
    private const uint PREFERRED_REFRESH_RATE_ID = 0x0064B541;

    // ── VSync option arrays ───────────────────────────────────────────────────

    public static readonly (string Name, uint Value)[] VSyncModeOptions =
    [
        ("App Controlled", 0x60925292),
        ("Force Off", 0x08416747),
        ("Force On", 0x47814940),
        ("Fast Sync", 0x18888888),
    ];

    public static readonly (string Name, uint Value)[] VSyncTearControlOptions =
    [
        ("Standard", 0x96861077),
        ("Adaptive", 0x99941284),
    ];

    // ── Power / CPU option arrays ─────────────────────────────────────────────

    public static readonly (string Name, uint Value)[] PowerManagementOptions =
    [
        ("Optimal Performance", 0x00000005),
        ("Adaptive", 0x00000000),
        ("Maximum Performance", 0x00000001),
    ];

    public static readonly (string Name, uint Value)[] CpuExprModeOptions =
    [
        ("Mode 0 (Default)", 0x00000000),
        ("Mode 1", 0x00000001),
        ("Mode 4", 0x00000004),
    ];

    // ── Latency option arrays ─────────────────────────────────────────────────

    /// <summary>Combined Low Latency Mode options matching NVIDIA App behavior.</summary>
    public static readonly (string Name, uint Value)[] LowLatencyModeOptions =
    [
        ("Off", 0x00000000),
        ("On", 0x00000001),
        ("Ultra", 0x00000002),
    ];

    // ── Smooth Motion option arrays ───────────────────────────────────────────

    public static readonly (string Name, uint Value)[] SmoothMotionEnableOptions =
    [
        ("Off", 0x00000000),
        ("On [40 Series+]", 0x00000001),
    ];

    public static readonly (string Name, uint Value)[] SmoothMotionApisOptions =
    [
        ("None", 0x00000000),
        ("DX12", 0x00000001),
        ("DX11", 0x00000002),
        ("DX11/12", 0x00000003),
        ("VK", 0x00000004),
        ("DX12, VK", 0x00000005),
        ("DX11, VK", 0x00000006),
        ("All [DX11/12, VK]", 0x00000007),
    ];

    public static readonly (string Name, uint Value)[] SmoothMotionFlipPacingFsOptions =
    [
        ("Off [Latency]", 0x00000000),
        ("On [Pacing]", 0xFFFFFFFF),
    ];

    public static readonly (string Name, uint Value)[] SmoothMotionFlipPacingWinOptions =
    [
        ("Off [Latency]", 0x00000000),
        ("On [Pacing]", 0x00000001),
        ("Force On [Pacing]", 0xFFFFFFFF),
    ];

    // ── Shader Cache option arrays ────────────────────────────────────────────

    public static readonly (string Name, uint Value)[] ShaderCacheSizeOptions =
    [
        ("512MB", 0x00000200),
        ("1GB", 0x00000400),
        ("4GB", 0x00001000),
        ("5GB", 0x00001400),
        ("10GB", 0x00002800),
        ("12GB", 0x00003000),
        ("16GB (Default)", 0x00004000),
        ("24GB", 0x00006000),
        ("32GB", 0x00008000),
        ("48GB", 0x0000C000),
        ("64GB", 0x00010000),
        ("100GB", 0x00019000),
        ("Unlimited", 0xFFFFFFFF),
    ];

    public static readonly (string Name, uint Value)[] ShaderPrecompileOptions =
    [
        ("Off", 0x00000000),
        ("Low (Default)", 0x00000001),
        ("Medium", 0x00000002),
        ("High", 0x00000003),
    ];

    // ── G-Sync / Refresh Rate option arrays ───────────────────────────────────

    public static readonly (string Name, uint Value)[] GSyncModeOptions =
    [
        ("Off", 0x00000000),
        ("Fullscreen only", 0x00000001),
        ("Fullscreen/Windowed", 0x00000002),
    ];

    public static readonly (string Name, uint Value)[] PreferredRefreshRateOptions =
    [
        ("App Setting", 0x00000000),
        ("Highest available", 0x00000001),
    ];

    // ── VSync get/set ─────────────────────────────────────────────────────────

    public uint GetVSyncMode(string gameName, string installPath)
        => GetPreset(gameName, installPath, VSYNC_MODE_ID);

    public bool SetVSyncMode(string gameName, string installPath, uint value)
    {
        if (value == VSyncModeOptions[0].Value) return DeletePreset(gameName, installPath, VSYNC_MODE_ID);
        return SetPreset(gameName, installPath, VSYNC_MODE_ID, value);
    }

    public uint GetVSyncTearControl(string gameName, string installPath)
        => GetPreset(gameName, installPath, VSYNC_TEAR_CONTROL_ID);

    public bool SetVSyncTearControl(string gameName, string installPath, uint value)
    {
        if (value == VSyncTearControlOptions[0].Value) return DeletePreset(gameName, installPath, VSYNC_TEAR_CONTROL_ID);
        return SetPreset(gameName, installPath, VSYNC_TEAR_CONTROL_ID, value);
    }

    // ── Global VSync (base profile) ──────────────────────────────────────────

    /// <summary>Gets the global VSync mode from the base profile. Returns null if not explicitly set.</summary>
    public uint? GetGlobalVSyncMode()
    {
        if (!_isSupported || _session == null) return null;
        try
        {
            var baseProfile = _session.BaseProfile;
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(baseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
                return GetSettingRawNvApi(sessionHandle, profileHandle, VSYNC_MODE_ID);
            return null;
        }
        catch { return null; }
    }

    /// <summary>Sets the global VSync mode on the base profile.</summary>
    public bool SetGlobalVSyncMode(uint value)
    {
        if (!_isSupported || _session == null) return false;
        try
        {
            var baseProfile = _session.BaseProfile;
            baseProfile.SetSetting(VSYNC_MODE_ID, value);
            _session.Save();
            CrashReporter.Log($"[DlssPresetService.SetGlobalVSyncMode] Set to 0x{value:X8}");
            return true;
        }
        catch (Exception ex)
        {
            // Try raw API fallback
            try
            {
                var sessionHandle = GetHandlePtr(_session.Handle);
                var profileHandle = GetHandlePtr(_session.BaseProfile.Handle);
                if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
                {
                    if (SetSettingRawNvApi(sessionHandle, profileHandle, VSYNC_MODE_ID, value))
                        return true;
                }
            }
            catch { }
            CrashReporter.Log($"[DlssPresetService.SetGlobalVSyncMode] Error — {ex.Message}");
            return false;
        }
    }

    // ── Power / CPU get/set ───────────────────────────────────────────────────

    /// <summary>Gets the global power management mode from the base profile. Returns null if not explicitly set (default = Optimal Performance 0x05).</summary>
    public uint? GetGlobalPowerMode()
    {
        if (!_isSupported || _session == null) return null;
        try
        {
            var baseProfile = _session.BaseProfile;
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(baseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
                return GetSettingRawNvApi(sessionHandle, profileHandle, POWER_MANAGEMENT_MODE_ID);
            return null;
        }
        catch { return null; }
    }

    /// <summary>Sets the global power management mode on the base profile.</summary>
    public bool SetGlobalPowerMode(uint value)
    {
        if (!_isSupported || _session == null) return false;
        try
        {
            var baseProfile = _session.BaseProfile;
            baseProfile.SetSetting(POWER_MANAGEMENT_MODE_ID, value);
            _session.Save();
            CrashReporter.Log($"[DlssPresetService.SetGlobalPowerMode] Set to 0x{value:X8}");
            return true;
        }
        catch (Exception ex)
        {
            try
            {
                var sessionHandle = GetHandlePtr(_session.Handle);
                var profileHandle = GetHandlePtr(_session.BaseProfile.Handle);
                if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
                {
                    if (SetSettingRawNvApi(sessionHandle, profileHandle, POWER_MANAGEMENT_MODE_ID, value))
                        return true;
                }
            }
            catch { }
            CrashReporter.Log($"[DlssPresetService.SetGlobalPowerMode] Error — {ex.Message}");
            return false;
        }
    }

    public uint GetPowerManagementMode(string gameName, string installPath)
    {
        // Power Management: 0 is a valid value ("Adaptive"), but when the setting
        // doesn't exist at all, the effective default is Optimal (0x05).
        // Use raw API to distinguish "setting absent" (null) from "explicitly 0".
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return 0x00000005; // Default: Optimal

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null) return 0x00000005;

            // Try NvAPIWrapper first
            var setting = profile.Settings.FirstOrDefault(s => s.SettingId == POWER_MANAGEMENT_MODE_ID);
            if (setting?.CurrentValue is uint value)
                return value;

            // Try raw API (returns null if setting doesn't exist on profile)
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(profile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var rawValue = GetSettingRawNvApi(sessionHandle, profileHandle, POWER_MANAGEMENT_MODE_ID);
                if (rawValue.HasValue)
                    return rawValue.Value; // Setting exists — return actual value (including 0 = Adaptive)
            }

            // Setting truly doesn't exist — return NVIDIA default
            return 0x00000005; // Optimal Performance
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.GetPowerManagementMode] Error — {ex.Message}");
            return 0x00000005;
        }
    }

    public bool SetPowerManagementMode(string gameName, string installPath, uint value)
    {
        if (value == PowerManagementOptions[0].Value) return DeletePreset(gameName, installPath, POWER_MANAGEMENT_MODE_ID);
        return SetPreset(gameName, installPath, POWER_MANAGEMENT_MODE_ID, value);
    }

    public uint GetCpuExprMode(string gameName, string installPath)
        => GetPreset(gameName, installPath, CPU_EXPR_MODES_ID);

    public bool SetCpuExprMode(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, CPU_EXPR_MODES_ID, value);

    // ── Latency get/set ───────────────────────────────────────────────────────

    /// <summary>Gets the Low Latency Mode state by reading ULL Mode (0=Off, 1=On, 2=Ultra).</summary>
    public uint GetLowLatencyMode(string gameName, string installPath)
        => GetPreset(gameName, installPath, LATENCY_ULL_MODE_ID);

    /// <summary>
    /// Sets Low Latency Mode as a single control matching NVIDIA App behavior.
    /// Off: Pre-Rendered=0, Enable=0, Mode=0
    /// On: Pre-Rendered=1, Enable=0, Mode=1
    /// Ultra: Pre-Rendered=1, Enable=1, Mode=2
    /// </summary>
    /// <summary>
    /// Sets Low Latency Mode per-game. Uses elevated process for ULL Enable/Mode (requires admin).
    /// Off: Pre-Rendered=0, Enable=0, Mode=0
    /// On: Pre-Rendered=1, Enable=0, Mode=1
    /// Ultra: Pre-Rendered=1, Enable=1, Mode=2
    /// </summary>
    public bool SetLowLatencyMode(string gameName, string installPath, uint mode)
    {
        CrashReporter.Log($"[DlssPresetService.SetLowLatencyMode] gameName='{gameName}', mode={mode}");

        // Pre-Rendered Frames always works without elevation
        uint preRendered = mode >= 1 ? 1u : 0u;
        SetPreset(gameName, installPath, LATENCY_MAX_PRE_RENDERED_FRAMES_ID, preRendered);

        // ULL Enable and Mode — try in-process first (works when running as admin)
        uint ullEnable = mode == 2 ? 1u : 0u;
        uint ullMode = mode;
        bool modeOk = SetPreset(gameName, installPath, LATENCY_ULL_MODE_ID, ullMode);
        bool enableOk = SetPreset(gameName, installPath, LATENCY_ULL_ENABLE_ID, ullEnable);

        if (modeOk && enableOk)
            return true;

        // If in-process failed, try elevated helper
        CrashReporter.Log($"[DlssPresetService.SetLowLatencyMode] In-process failed, trying elevated helper");
        return SetLowLatencyElevated(gameName, installPath, ullEnable, ullMode);
    }

    /// <summary>Sets ULL Enable and Mode via an elevated PowerShell process (these settings require admin).</summary>
    private bool SetLowLatencyElevated(string gameName, string installPath, uint ullEnable, uint ullMode)
    {
        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "rhi_latency_set.ps1");
            var nvApiPath = Path.Combine(AppContext.BaseDirectory, "NvAPIWrapper.dll");

            // Use raw NVAPI P/Invoke from PowerShell — NvAPIWrapper can't write these settings
            var script = $@"
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class NvApi {{
    [DllImport(""nvapi64.dll"", EntryPoint = ""nvapi_QueryInterface"", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr QueryInterface(uint id);

    public delegate int NvAPI_Initialize();
    public delegate int DRS_CreateSession(out IntPtr hSession);
    public delegate int DRS_LoadSettings(IntPtr hSession);
    public delegate int DRS_FindProfileByName(IntPtr hSession, [MarshalAs(UnmanagedType.LPWStr)] string profileName, out IntPtr hProfile);
    public delegate int DRS_SetSetting(IntPtr hSession, IntPtr hProfile, IntPtr pSetting, uint x, uint y);
    public delegate int DRS_SaveSettings(IntPtr hSession);
    public delegate int DRS_DestroySession(IntPtr hSession);
}}
'@ -Language CSharp

# Initialize NVAPI first (required before resolving newer function pointers)
$pInit = [NvApi]::QueryInterface([uint32]0x0150E828)
$initDel = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($pInit, [NvApi+NvAPI_Initialize])
$initDel.Invoke() | Out-Null

# Resolve function pointers (AFTER Initialize — newer IDs like SetSetting require init first)
$pCreateSession = [NvApi]::QueryInterface([uint32]0x0694D52E)
$pLoadSettings  = [NvApi]::QueryInterface([uint32]0x375DBD6B)
$pFindProfile   = [NvApi]::QueryInterface([uint32]0x7E4A9A0B)
$pSetSetting    = [NvApi]::QueryInterface([uint32]0x577DD202)
if ($pSetSetting -eq [IntPtr]::Zero) {{
    # Try newer function ID as fallback
    $pSetSetting = [NvApi]::QueryInterface([uint32]2317669877)
}}
$pSaveSettings  = [NvApi]::QueryInterface([uint32]4240072212)
$pDestroy       = [NvApi]::QueryInterface([uint32]3671834584)

# Create session
$createDel = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($pCreateSession, [NvApi+DRS_CreateSession])
$hSession = [IntPtr]::Zero
$createDel.Invoke([ref]$hSession) | Out-Null

# Load settings
$loadDel = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($pLoadSettings, [NvApi+DRS_LoadSettings])
$loadDel.Invoke($hSession) | Out-Null

# Find profile
$findDel = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($pFindProfile, [NvApi+DRS_FindProfileByName])
$hProfile = [IntPtr]::Zero
$findResult = $findDel.Invoke($hSession, '{gameName.Replace("'", "''")}', [ref]$hProfile)
if ($findResult -ne 0) {{
    Write-Host ""PROFILE_NOT_FOUND:$findResult""
    exit 1
}}

# Set ULL Mode (0x0005F543)
$setDel = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($pSetSetting, [NvApi+DRS_SetSetting])

function Set-DwordSetting($session, $profile, $settingId, $value) {{
    $structSize = 12320
    $ptr = [System.Runtime.InteropServices.Marshal]::AllocHGlobal($structSize)
    try {{
        # Zero fill
        $zeros = New-Object byte[] $structSize
        [System.Runtime.InteropServices.Marshal]::Copy($zeros, 0, $ptr, $structSize)
        # version
        [System.Runtime.InteropServices.Marshal]::WriteInt32($ptr, 0, ($structSize -bor (1 -shl 16)))
        # settingId at offset 4100
        [System.Runtime.InteropServices.Marshal]::WriteInt32($ptr, 4100, $settingId)
        # settingType at offset 4104 = 0 (DWORD)
        [System.Runtime.InteropServices.Marshal]::WriteInt32($ptr, 4104, 0)
        # currentValue dword at offset 8220
        [System.Runtime.InteropServices.Marshal]::WriteInt32($ptr, 8220, $value)

        $result = $setDel.Invoke($session, $profile, $ptr, [uint32]0, [uint32]0)
        Write-Host ""SetSetting 0x$($settingId.ToString('X8'))=$value result=$result""
        return $result
    }} finally {{
        [System.Runtime.InteropServices.Marshal]::FreeHGlobal($ptr)
    }}
}}

Set-DwordSetting $hSession $hProfile 0x0005F543 {ullMode}
Set-DwordSetting $hSession $hProfile 0x10835000 {ullEnable}

# Save
$saveDel = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($pSaveSettings, [NvApi+DRS_SaveSettings])
$saveResult = $saveDel.Invoke($hSession)
Write-Host ""SaveSettings result=$saveResult""

# Destroy session
$destroyDel = [System.Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer($pDestroy, [NvApi+DRS_DestroySession])
$destroyDel.Invoke($hSession) | Out-Null
";
            File.WriteAllText(scriptPath, script);

            var logPath = Path.Combine(Path.GetTempPath(), "rhi_latency_log.txt");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"& '{scriptPath}' *> '{logPath}'\"",
                Verb = "runas",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(15000);

            // Read script output from log file
            if (File.Exists(logPath))
            {
                var output = File.ReadAllText(logPath).Trim();
                CrashReporter.Log($"[DlssPresetService.SetLowLatencyElevated] Script output: {output}");
                try { File.Delete(logPath); } catch { }
            }

            try { File.Delete(scriptPath); } catch { }

            // Reload session to reflect changes
            _session = DriverSettingsSession.CreateAndLoad();
            _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _session.Profiles)
                _cachedProfiles.TryAdd(p.Name, p);
            InvalidateProfileLookupCache();

            CrashReporter.Log($"[DlssPresetService.SetLowLatencyElevated] Elevated process completed for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetLowLatencyElevated] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Smooth Motion get/set ─────────────────────────────────────────────────

    public uint GetSmoothMotionEnable(string gameName, string installPath)
        => GetPreset(gameName, installPath, SMOOTH_MOTION_ENABLE_ID);

    public bool SetSmoothMotionEnable(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, SMOOTH_MOTION_ENABLE_ID, value);

    public uint GetSmoothMotionApis(string gameName, string installPath)
        => GetPreset(gameName, installPath, SMOOTH_MOTION_APIS_ID);

    public bool SetSmoothMotionApis(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, SMOOTH_MOTION_APIS_ID, value);

    public uint GetSmoothMotionFlipPacingFs(string gameName, string installPath)
        => GetPreset(gameName, installPath, SMOOTH_MOTION_FLIP_PACING_FS_ID);

    public bool SetSmoothMotionFlipPacingFs(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, SMOOTH_MOTION_FLIP_PACING_FS_ID, value);

    public uint GetSmoothMotionFlipPacingWin(string gameName, string installPath)
        => GetPreset(gameName, installPath, SMOOTH_MOTION_FLIP_PACING_WIN_ID);

    public bool SetSmoothMotionFlipPacingWin(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, SMOOTH_MOTION_FLIP_PACING_WIN_ID, value);

    // ── Shader Cache (global profile) get/set ─────────────────────────────────

    /// <summary>Gets the global shader cache size setting. Returns 0x00004000 (16GB) as default if not explicitly set.</summary>
    public uint GetShaderCacheSize()
    {
        if (!_isSupported || _session == null) return 0x00004000; // 16GB default
        try
        {
            var baseProfile = _session.BaseProfile;
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(baseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var rawVal = GetSettingRawNvApi(sessionHandle, profileHandle, SHADER_CACHE_SIZE_ID);
                if (rawVal.HasValue && rawVal.Value != 0) return rawVal.Value;
            }

            // Fallback to NvAPIWrapper
            var setting = baseProfile.Settings.FirstOrDefault(s => s.SettingId == SHADER_CACHE_SIZE_ID);
            if (setting?.CurrentValue is uint val && val != 0) return val;
            return 0x00004000; // 16GB default
        }
        catch { return 0x00004000; }
    }

    /// <summary>Sets the global shader cache size.</summary>
    public bool SetShaderCacheSize(uint value)
    {
        if (!_isSupported || _session == null) return false;
        try
        {
            var baseProfile = _session.BaseProfile;
            baseProfile.SetSetting(SHADER_CACHE_SIZE_ID, value);
            _session.Save();
            CrashReporter.Log($"[DlssPresetService.SetShaderCacheSize] Set to 0x{value:X8}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetShaderCacheSize] Error — {ex.Message}");
            return false;
        }
    }

    /// <summary>Gets the global shader pre-compile setting. Returns 0x00000001 (Low) as default if not explicitly set.</summary>
    public uint GetShaderPrecompile()
    {
        if (!_isSupported || _session == null) return 0x00000001; // Low default
        try
        {
            var baseProfile = _session.BaseProfile;
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(baseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var rawVal = GetSettingRawNvApi(sessionHandle, profileHandle, SHADER_PRECOMPILE_ID);
                if (rawVal.HasValue) return rawVal.Value; // 0 = Off (explicitly set), null = not set
            }

            // Fallback to NvAPIWrapper
            var setting = baseProfile.Settings.FirstOrDefault(s => s.SettingId == SHADER_PRECOMPILE_ID);
            if (setting?.CurrentValue is uint val) return val;
            return 0x00000001; // Low default (when setting doesn't exist at all)
        }
        catch { return 0x00000001; }
    }

    /// <summary>Sets the global shader pre-compile setting.</summary>
    public bool SetShaderPrecompile(uint value)
    {
        if (!_isSupported || _session == null) return false;
        try
        {
            var baseProfile = _session.BaseProfile;
            try
            {
                baseProfile.SetSetting(SHADER_PRECOMPILE_ID, value);
                _session.Save();
                CrashReporter.Log($"[DlssPresetService.SetShaderPrecompile] Set to 0x{value:X8}");
                return true;
            }
            catch
            {
                // Fallback to raw API if NvAPIWrapper doesn't recognize the setting
                var sH = GetHandlePtr(_session.Handle);
                var pH = GetHandlePtr(baseProfile.Handle);
                if (sH != IntPtr.Zero && pH != IntPtr.Zero && SetSettingRawNvApi(sH, pH, SHADER_PRECOMPILE_ID, value))
                    return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetShaderPrecompile] Error — {ex.Message}");
            return false;
        }
    }

    // ── G-Sync / Refresh Rate (global profile) get/set ────────────────────────

    /// <summary>Gets the G-Sync mode (reads App mode — both should be in sync).</summary>
    public uint GetGSyncMode()
    {
        if (!_isSupported || _session == null) return 0x00000001; // Default: Fullscreen only
        try
        {
            var baseProfile = _session.BaseProfile;
            var setting = baseProfile.Settings.FirstOrDefault(s => s.SettingId == GSYNC_APP_MODE_ID);
            if (setting?.CurrentValue is uint val) return val;

            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(baseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var rawVal = GetSettingRawNvApi(sessionHandle, profileHandle, GSYNC_APP_MODE_ID);
                if (rawVal.HasValue) return rawVal.Value;
            }
            return 0x00000001; // Default: Fullscreen only
        }
        catch { return 0x00000001; }
    }

    /// <summary>Sets both G-Sync App Mode and Global Mode together.</summary>
    public bool SetGSyncMode(uint value)
    {
        if (!_isSupported || _session == null) return false;
        try
        {
            var baseProfile = _session.BaseProfile;
            baseProfile.SetSetting(GSYNC_APP_MODE_ID, value);
            baseProfile.SetSetting(GSYNC_GLOBAL_MODE_ID, value);
            _session.Save();
            CrashReporter.Log($"[DlssPresetService.SetGSyncMode] Set both App+Global to 0x{value:X8}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetGSyncMode] Error — {ex.Message}");
            return false;
        }
    }

    /// <summary>Gets the preferred refresh rate setting.</summary>
    public uint GetPreferredRefreshRate()
    {
        if (!_isSupported || _session == null) return 0x00000000; // Default: App Setting
        try
        {
            var baseProfile = _session.BaseProfile;
            var setting = baseProfile.Settings.FirstOrDefault(s => s.SettingId == PREFERRED_REFRESH_RATE_ID);
            if (setting?.CurrentValue is uint val) return val;

            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(baseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var rawVal = GetSettingRawNvApi(sessionHandle, profileHandle, PREFERRED_REFRESH_RATE_ID);
                if (rawVal.HasValue) return rawVal.Value;
            }
            // Not set — NVIDIA default is "App Setting" (0x00)
            return 0x00000000;
        }
        catch { return 0x00000000; }
    }

    /// <summary>Sets the preferred refresh rate.</summary>
    public bool SetPreferredRefreshRate(uint value)
    {
        if (!_isSupported || _session == null) return false;
        try
        {
            var baseProfile = _session.BaseProfile;
            baseProfile.SetSetting(PREFERRED_REFRESH_RATE_ID, value);
            _session.Save();
            CrashReporter.Log($"[DlssPresetService.SetPreferredRefreshRate] Set to 0x{value:X8}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetPreferredRefreshRate] Error — {ex.Message}");
            return false;
        }
    }

    // ── Global ReBAR ──────────────────────────────────────────────────────────

    /// <summary>Gets the global ReBAR enable state from the base profile. Returns null if not set.</summary>
    public bool? GetGlobalReBarEnabled()
    {
        if (!_isSupported || _session == null) return null;
        try
        {
            var baseProfile = _session.BaseProfile;
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(baseProfile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var rawVal = GetSettingRawNvApi(sessionHandle, profileHandle, REBAR_FEATURE_ID);
                if (rawVal.HasValue)
                    return rawVal.Value != 0;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>Sets global ReBAR enable on the base profile.</summary>
    public bool SetGlobalReBarEnabled(bool enabled)
    {
        if (!_isSupported || _session == null) return false;
        try
        {
            var baseProfile = _session.BaseProfile;

            // ReBAR Feature requires elevation — use elevated helper
            var scriptPath = Path.Combine(Path.GetTempPath(), "rhi_global_rebar_enable.ps1");
            var nvApiPath = Path.Combine(AppContext.BaseDirectory, "NvAPIWrapper.dll");

            string scriptBody;
            if (enabled)
            {
                scriptBody = $@"
Add-Type -Path '{nvApiPath.Replace("'", "''")}'
[NvAPIWrapper.NVIDIA]::Initialize()
$session = [NvAPIWrapper.DRS.DriverSettingsSession]::CreateAndLoad()
$baseProfile = $session.BaseProfile
$baseProfile.SetSetting([uint32]0x000F00BA, [uint32]1)
$baseProfile.SetSetting([uint32]0x00C09D09, [uint32]0)
$session.Save()
";
            }
            else
            {
                // When disabling, delete the settings to truly clear them
                scriptBody = $@"
Add-Type -Path '{nvApiPath.Replace("'", "''")}'
[NvAPIWrapper.NVIDIA]::Initialize()
$session = [NvAPIWrapper.DRS.DriverSettingsSession]::CreateAndLoad()
$baseProfile = $session.BaseProfile
try {{ $baseProfile.DeleteSetting([uint32]0x000F00BA) }} catch {{}}
try {{ $baseProfile.DeleteSetting([uint32]0x00C09D09) }} catch {{}}
try {{ $baseProfile.DeleteSetting([uint32]0x000F00FF) }} catch {{}}
$session.Save()
";
            }

            File.WriteAllText(scriptPath, scriptBody);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(10000);
            try { File.Delete(scriptPath); } catch { }

            if (enabled)
            {
                // Also write default size limit if not already set
                var currentSize = GetGlobalReBarSizeLimit();
                if (currentSize == 0)
                    SetGlobalReBarSizeLimit(0x0000000040000000); // 1GB default
            }

            // Reload session
            try
            {
                _session = DriverSettingsSession.CreateAndLoad();
                _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _session.Profiles)
                    _cachedProfiles.TryAdd(p.Name, p);
                InvalidateProfileLookupCache();
            }
            catch { }

            CrashReporter.Log($"[DlssPresetService.SetGlobalReBarEnabled] Set to {enabled}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetGlobalReBarEnabled] Error — {ex.Message}");
            return false;
        }
    }

    /// <summary>Gets the global ReBAR size limit from the base profile. Returns 0 if not set.</summary>
    public ulong GetGlobalReBarSizeLimit()
    {
        if (!_isSupported || _session == null) return 0;
        try
        {
            var baseProfile = _session.BaseProfile;
            var setting = baseProfile.GetSetting(REBAR_SIZE_LIMIT_ID);
            if (setting.CurrentValue is byte[] bytes && bytes.Length >= 8)
                return BitConverter.ToUInt64(bytes, 0);
            return 0;
        }
        catch { return 0; }
    }

    /// <summary>Sets the global ReBAR size limit on the base profile.</summary>
    public bool SetGlobalReBarSizeLimit(ulong sizeBytes)
    {
        if (!_isSupported || _session == null) return false;

        try
        {
            EnsureNativeFunctions();
            var baseProfile = _session.BaseProfile;
            var sessionH = GetHandlePtr(_session.Handle);
            var profileH = GetHandlePtr(baseProfile.Handle);

            if (sessionH == IntPtr.Zero || profileH == IntPtr.Zero || _nativeSetSettingPtr == null)
            {
                CrashReporter.Log("[DlssPresetService.SetGlobalReBarSizeLimit] Could not get handles or native function");
                return false;
            }

            // Write BINARY setting via raw NVAPI with correct struct layout
            const int STRUCT_SIZE = 12320;
            var ptr = Marshal.AllocHGlobal(STRUCT_SIZE);
            try
            {
                unsafe { new Span<byte>((void*)ptr, STRUCT_SIZE).Clear(); }

                Marshal.WriteInt32(ptr, 0, STRUCT_SIZE | (1 << 16)); // version
                Marshal.WriteInt32(ptr, 4100, (int)REBAR_SIZE_LIMIT_ID); // settingId
                Marshal.WriteInt32(ptr, 4104, 1); // settingType = BINARY
                Marshal.WriteInt32(ptr, 8216, 8); // binary length = 8 bytes
                Marshal.WriteInt64(ptr, 8220, (long)sizeBytes); // data

                int result = _nativeSetSettingPtr(sessionH, profileH, ptr, 0, 0);
                if (result != 0)
                {
                    CrashReporter.Log($"[DlssPresetService.SetGlobalReBarSizeLimit] Raw SetSetting returned {result}");
                    return false;
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }

            // Save
            if (_nativeSaveSettings != null)
                _nativeSaveSettings(sessionH);

            // Reload session
            try
            {
                _session = DriverSettingsSession.CreateAndLoad();
                _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _session.Profiles)
                    _cachedProfiles.TryAdd(p.Name, p);
                InvalidateProfileLookupCache();
            }
            catch { }

            CrashReporter.Log($"[DlssPresetService.SetGlobalReBarSizeLimit] Set to 0x{sizeBytes:X16}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetGlobalReBarSizeLimit] Error — {ex.Message}");
            return false;
        }
    }

}
