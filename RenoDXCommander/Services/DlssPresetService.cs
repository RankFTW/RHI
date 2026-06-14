using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvAPIWrapper.DRS;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages DLSS preset overrides via the NVIDIA Driver Settings (NvDRS) API.
/// Presets are per-game settings stored in the NVIDIA driver profile.
/// Silently no-ops on AMD/Intel systems where NVAPI is unavailable.
/// </summary>
public class DlssPresetService
{
    // ── Raw NVAPI P/Invoke for settings that NvAPIWrapper doesn't recognize ───
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI_QueryInterface(uint id);

    private const uint NVAPI_DRS_SET_SETTING_ID = 0x8A2CF5F5; // Primary (R610+), fallback: 0x577DD202
    private const uint NVAPI_DRS_GET_SETTING_ID = 0xEA99498D; // Primary (R610+), fallback: 0x73BF8338
    private const uint NVAPI_DRS_SAVE_SETTINGS_ID = 0xFCBC7E14;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_DRS_SetSettingPtr_t(IntPtr hSession, IntPtr hProfile, IntPtr pSetting, uint x, uint y);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_DRS_GetSettingPtr_t(IntPtr hSession, IntPtr hProfile, uint settingId, IntPtr pSetting, ref uint x);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_DRS_SaveSettings_t(IntPtr hSession);

    private static NvAPI_DRS_SetSettingPtr_t? _nativeSetSettingPtr;
    private static NvAPI_DRS_GetSettingPtr_t? _nativeGetSettingPtr;
    private static NvAPI_DRS_SaveSettings_t? _nativeSaveSettings;

    private static void EnsureNativeFunctions()
    {
        if (_nativeSetSettingPtr != null) return;
        var setPtr = NvAPI_QueryInterface(NVAPI_DRS_SET_SETTING_ID);
        var getPtr = NvAPI_QueryInterface(NVAPI_DRS_GET_SETTING_ID);
        var savePtr = NvAPI_QueryInterface(NVAPI_DRS_SAVE_SETTINGS_ID);
        if (setPtr != IntPtr.Zero)
            _nativeSetSettingPtr = Marshal.GetDelegateForFunctionPointer<NvAPI_DRS_SetSettingPtr_t>(setPtr);
        if (getPtr != IntPtr.Zero)
            _nativeGetSettingPtr = Marshal.GetDelegateForFunctionPointer<NvAPI_DRS_GetSettingPtr_t>(getPtr);
        if (savePtr != IntPtr.Zero)
            _nativeSaveSettings = Marshal.GetDelegateForFunctionPointer<NvAPI_DRS_SaveSettings_t>(savePtr);
    }

    /// <summary>
    /// Sets a DWORD setting directly via raw NVAPI, bypassing NvAPIWrapper.
    /// Used for settings that NvAPIWrapper doesn't recognize (SETTING_NOT_FOUND).
    /// Uses unmanaged memory with the exact struct size (12320 bytes) matching the driver's expectation.
    /// </summary>
    private bool SetSettingRawNvApi(IntPtr sessionHandle, IntPtr profileHandle, uint settingId, uint value)
    {
        EnsureNativeFunctions();
        if (_nativeSetSettingPtr == null || _nativeSaveSettings == null) return false;

        // Allocate exactly 12320 bytes (the correct NVDRS_SETTING_V1 size as reported by NvAPIWrapper)
        const int STRUCT_SIZE = 12320;
        var ptr = Marshal.AllocHGlobal(STRUCT_SIZE);
        try
        {
            // Zero the entire struct
            unsafe
            {
                new Span<byte>((void*)ptr, STRUCT_SIZE).Clear();
            }

            // version at offset 0: size | (1 << 16)
            Marshal.WriteInt32(ptr, 0, STRUCT_SIZE | (1 << 16));
            // settingName at offset 4: leave zeroed (empty string)
            // settingId at offset 4 + 4096 = 4100
            Marshal.WriteInt32(ptr, 4100, (int)settingId);
            // settingType at offset 4104: 0 = DWORD
            Marshal.WriteInt32(ptr, 4104, 0);
            // settingLocation at offset 4108: 0
            Marshal.WriteInt32(ptr, 4108, 0);
            // isCurrentPredefined at offset 4112: 0
            Marshal.WriteInt32(ptr, 4112, 0);
            // isPredefinedValid at offset 4116: 0
            Marshal.WriteInt32(ptr, 4116, 0);
            // predefinedValue union at offset 4120: leave zeroed (4100 bytes — NVDRS_BINARY_SETTING)
            // currentValue union at offset 4120 + 4100 = 8220: write the DWORD value
            Marshal.WriteInt32(ptr, 8220, (int)value);

            int result = _nativeSetSettingPtr(sessionHandle, profileHandle, ptr, 0, 0);
            if (result != 0)
            {
                CrashReporter.Log($"[DlssPresetService.SetSettingRawNvApi] NvAPI_DRS_SetSetting returned {result} for 0x{settingId:X8}={value}");
                return false;
            }

            int saveResult = _nativeSaveSettings(sessionHandle);
            if (saveResult != 0)
            {
                CrashReporter.Log($"[DlssPresetService.SetSettingRawNvApi] NvAPI_DRS_SaveSettings returned {saveResult}");
                return false;
            }

            CrashReporter.Log($"[DlssPresetService.SetSettingRawNvApi] Set 0x{settingId:X8}={value} via raw NVAPI");
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Gets a DWORD setting directly via raw NVAPI, bypassing NvAPIWrapper.
    /// Used for settings that NvAPIWrapper can't read (newer setting IDs).
    /// </summary>
    private uint? GetSettingRawNvApi(IntPtr sessionHandle, IntPtr profileHandle, uint settingId)
    {
        EnsureNativeFunctions();
        if (_nativeGetSettingPtr == null) return null;

        const int STRUCT_SIZE = 12320;
        var ptr = Marshal.AllocHGlobal(STRUCT_SIZE);
        try
        {
            unsafe
            {
                new Span<byte>((void*)ptr, STRUCT_SIZE).Clear();
            }
            Marshal.WriteInt32(ptr, 0, STRUCT_SIZE | (1 << 16));

            uint extraParam = 0;
            int result = _nativeGetSettingPtr(sessionHandle, profileHandle, settingId, ptr, ref extraParam);
            if (result != 0)
                return null;

            // Read DWORD value from currentValue at offset 8220
            return (uint)Marshal.ReadInt32(ptr, 8220);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    // ── Setting IDs ───────────────────────────────────────────────────────────
    private const uint NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID = 0x10E41DF3;
    private const uint NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID = 0x10E41DF7;
    private const uint NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID = 0x10E41DF1;
    private const uint NGX_DLSS_SR_RENDER_SCALE_ID = 0x10AFB768; // SR Render Scale mode (Default=3, Custom=6)
    private const uint NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID = 0x10E41DF5; // SR Render Scale custom value (percentage as uint)
    private const uint NGX_DLSS_RR_RENDER_SCALE_ID = 0x10BD9423; // RR Render Scale mode
    private const uint NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID = 0x10C7D4A2; // RR Render Scale custom value

    private const uint RENDER_SCALE_DEFAULT = 0x03;
    private const uint RENDER_SCALE_CUSTOM = 0x06;

    // ── Preset values ─────────────────────────────────────────────────────────
    public static readonly (string Name, uint Value)[] SrPresets =
    [
        ("Default", 0x00000000),
        ("J", 0x0000000A),
        ("K", 0x0000000B),
        ("L", 0x0000000C),
        ("M", 0x0000000D),
    ];

    public static readonly (string Name, uint Value)[] RrPresets =
    [
        ("Default", 0x00000000),
        ("D", 0x00000004),
        ("E", 0x00000005),
    ];

    public static readonly (string Name, uint Value)[] FgPresets =
    [
        ("Default", 0x00000000),
        ("A", 0x00000001),
        ("B", 0x00000002),
    ];

    /// <summary>Named render scale options for SR and RR. "Custom" is handled separately via a TextBox.</summary>
    public static readonly (string Name, uint Value)[] RenderScaleOptions =
    [
        ("Off", 0),
        ("100% DLAA", 100),
        ("99% DLAA Alt", 99),
        ("88% DLAA Lite", 88),
        ("77% Ultra Quality", 77),
        ("75% Quality+", 75),
        ("67% Quality", 67),
        ("58% Balanced", 58),
        ("50% Performance", 50),
        ("45% Performance-", 45),
        ("33% Ultra Perf", 33),
        ("Custom", 0xFFFFFFFF), // Sentinel — actual value comes from TextBox
    ];

    // ── State ─────────────────────────────────────────────────────────────────
    private DriverSettingsSession? _session;
    private Dictionary<string, DriverSettingsProfile>? _cachedProfiles;
    private bool _isSupported;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string dllToLoad);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);

    public bool IsSupported => _isSupported;

    /// <summary>When true, SetPreset will auto-create NVIDIA profiles for games that don't have one.</summary>
    public bool AutoCreateProfiles { get; set; } = true;

    /// <summary>Counter for profiles created during the current batch operation. Reset before use.</summary>
    public int ProfilesCreatedCount { get; set; }

    /// <summary>When true, the ReBAR enable warning dialog won't be shown again this session.</summary>
    public bool ReBarWarningAcknowledged { get; set; }

    /// <summary>When true, the ReBAR Optimized mode warning won't be shown again this session.</summary>
    public bool ReBarOptimizedWarningAcknowledged { get; set; }

    /// <summary>
    /// Initializes NVAPI. Call once during app startup.
    /// No-op if NVIDIA drivers are not installed.
    /// </summary>
    public void Initialize()
    {
        try
        {
            var handle = LoadLibrary("nvapi64.dll");
            if (handle == IntPtr.Zero)
            {
                CrashReporter.Log("[DlssPresetService.Initialize] nvapi64.dll not found — NVIDIA drivers not installed, presets disabled");
                return;
            }
            FreeLibrary(handle);

            NVIDIA.Initialize();
            _session = DriverSettingsSession.CreateAndLoad();
            _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in _session.Profiles)
            {
                _cachedProfiles.TryAdd(profile.Name, profile);
            }
            _isSupported = true;

            CrashReporter.Log($"[DlssPresetService.Initialize] NVAPI initialized, {_cachedProfiles.Count} profiles cached");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.Initialize] Failed — {ex.Message}");
            _isSupported = false;
        }
    }

    // ── Get presets ───────────────────────────────────────────────────────────

    public uint GetSrPreset(string gameName, string installPath)
        => GetPreset(gameName, installPath, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID);

    public uint GetRrPreset(string gameName, string installPath)
        => GetPreset(gameName, installPath, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID);

    public uint GetFgPreset(string gameName, string installPath)
        => GetPreset(gameName, installPath, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID);

    // ── Set presets ───────────────────────────────────────────────────────────

    public bool SetSrPreset(string gameName, string installPath, uint preset)
        => SetPreset(gameName, installPath, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);

    public bool SetRrPreset(string gameName, string installPath, uint preset)
        => SetPreset(gameName, installPath, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);

    public bool SetFgPreset(string gameName, string installPath, uint preset)
        => SetPreset(gameName, installPath, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);

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

    /// <summary>Sets the SR render scale. 0 = reset to Default. 33-100 = set custom percentage.</summary>
    public bool SetSrRenderScale(string gameName, string installPath, uint percentage)
    {
        if (percentage == 0)
        {
            SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID, RENDER_SCALE_DEFAULT);
            return SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID, 0);
        }
        SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_ID, RENDER_SCALE_CUSTOM);
        return SetPreset(gameName, installPath, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID, percentage);
    }

    /// <summary>Sets the RR render scale. 0 = reset to Default. 33-100 = set custom percentage.</summary>
    public bool SetRrRenderScale(string gameName, string installPath, uint percentage)
    {
        if (percentage == 0)
        {
            SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_ID, RENDER_SCALE_DEFAULT);
            return SetPreset(gameName, installPath, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID, 0);
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
            if (profile == null) return 0;

            var setting = profile.Settings.FirstOrDefault(s => s.SettingId == REBAR_SIZE_LIMIT_ID);
            if (setting?.CurrentValue is byte[] bytes && bytes.Length >= 8)
                return BitConverter.ToUInt64(bytes, 0);
            if (setting?.CurrentValue is uint dwordVal)
                return dwordVal; // Fallback if driver reports it as DWORD

            return 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.GetReBarSizeLimit] Error for '{gameName}' — {ex.Message}");
            return 0;
        }
    }

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
    if ($p.Name -eq '{gameName.Replace("'", "''")}') {{ $profile = $p; break }}
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

            var bytes = BitConverter.GetBytes(sizeBytes);
            profile.SetSetting(REBAR_SIZE_LIMIT_ID, bytes);
            _session.Save();

            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimit] Set size limit to 0x{sizeBytes:X16} for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("INVALID_USER_PRIVILEGE"))
            {
                CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimit] Requires elevation for '{gameName}'");
                return SetReBarSizeLimitElevated(gameName, installPath, sizeBytes);
            }
            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimit] Error for '{gameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Sets ReBAR Size Limit via an elevated PowerShell process.</summary>
    private bool SetReBarSizeLimitElevated(string gameName, string installPath, ulong sizeBytes)
    {
        try
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), "rhi_rebar_size.ps1");
            var nvApiPath = Path.Combine(AppContext.BaseDirectory, "NvAPIWrapper.dll");
            var hexBytes = BitConverter.ToString(BitConverter.GetBytes(sizeBytes)).Replace("-", ",0x");

            var script = $@"
Add-Type -Path '{nvApiPath.Replace("'", "''")}'
[NvAPIWrapper.NVIDIA]::Initialize()
$session = [NvAPIWrapper.DRS.DriverSettingsSession]::CreateAndLoad()
$profile = $null
foreach ($p in $session.Profiles) {{
    if ($p.Name -eq '{gameName.Replace("'", "''")}') {{ $profile = $p; break }}
}}
if ($null -ne $profile) {{
    [byte[]]$bytes = @(0x{hexBytes})
    $profile.SetSetting([uint32]0x000F00FF, $bytes)
    $session.Save()
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
            try { File.Delete(scriptPath); } catch { }

            _session = DriverSettingsSession.CreateAndLoad();
            _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in _session.Profiles)
                _cachedProfiles.TryAdd(p.Name, p);

            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimitElevated] Done for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.SetReBarSizeLimitElevated] Failed — {ex.Message}");
            return false;
        }
    }

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
        ("Low (Default)", 0x00000001),
        ("Medium", 0x00000002),
        ("High", 0x00000003),
    ];

    // ── G-Sync / Refresh Rate option arrays ───────────────────────────────────

    public static readonly (string Name, uint Value)[] GSyncModeOptions =
    [
        ("Off", 0x00000000),
        ("Fullscreen only", 0x00000001),
        ("Fullscreen and Windowed", 0x00000002),
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
        => SetPreset(gameName, installPath, VSYNC_MODE_ID, value);

    public uint GetVSyncTearControl(string gameName, string installPath)
        => GetPreset(gameName, installPath, VSYNC_TEAR_CONTROL_ID);

    public bool SetVSyncTearControl(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, VSYNC_TEAR_CONTROL_ID, value);

    // ── Power / CPU get/set ───────────────────────────────────────────────────

    public uint GetPowerManagementMode(string gameName, string installPath)
        => GetPreset(gameName, installPath, POWER_MANAGEMENT_MODE_ID);

    public bool SetPowerManagementMode(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, POWER_MANAGEMENT_MODE_ID, value);

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
                if (rawVal.HasValue && rawVal.Value != 0) return rawVal.Value;
            }

            // Fallback to NvAPIWrapper
            var setting = baseProfile.Settings.FirstOrDefault(s => s.SettingId == SHADER_PRECOMPILE_ID);
            if (setting?.CurrentValue is uint val && val != 0) return val;
            return 0x00000001; // Low default
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
        if (!_isSupported || _session == null) return 0x00000002; // Default: Fullscreen and Windowed
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
            return 0x00000002;
        }
        catch { return 0x00000002; }
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
        if (!_isSupported || _session == null) return 0x00000001; // Default: Highest available
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
            return 0x00000001;
        }
        catch { return 0x00000001; }
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

    // ── Profile Export/Import ──────────────────────────────────────────────────

    /// <summary>All RHI-managed setting IDs for export/import.</summary>
    private static readonly uint[] ManagedSettingIds =
    [
        VSYNC_MODE_ID, VSYNC_TEAR_CONTROL_ID,
        POWER_MANAGEMENT_MODE_ID, CPU_EXPR_MODES_ID,
        LATENCY_ULL_ENABLE_ID, LATENCY_ULL_MODE_ID, LATENCY_MAX_PRE_RENDERED_FRAMES_ID,
        SMOOTH_MOTION_ENABLE_ID, SMOOTH_MOTION_APIS_ID, SMOOTH_MOTION_FLIP_PACING_FS_ID, SMOOTH_MOTION_FLIP_PACING_WIN_ID,
        REBAR_FEATURE_ID, REBAR_EXPR_MODES_ID,
        NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID,
        NGX_DLSS_SR_RENDER_SCALE_ID, NGX_DLSS_SR_RENDER_SCALE_CUSTOM_ID, NGX_DLSS_RR_RENDER_SCALE_ID, NGX_DLSS_RR_RENDER_SCALE_CUSTOM_ID,
    ];

    /// <summary>Exports all RHI-managed NVIDIA profile settings to a dictionary for serialization.</summary>
    public Dictionary<string, object> ExportProfiles(IEnumerable<(string GameName, string InstallPath)> games)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        if (!_isSupported || _session == null || _cachedProfiles == null) return result;

        var managedSet = new HashSet<uint>(ManagedSettingIds);
        var sessionHandle = GetHandlePtr(_session.Handle);

        foreach (var (gameName, installPath) in games)
        {
            try
            {
                var profile = FindProfile(gameName, installPath);
                if (profile == null) continue;

                // Skip if already exported under a different game name (same profile)
                if (result.ContainsKey(profile.Name)) continue;

                var profileData = new Dictionary<string, object>();

                // Get applications
                try
                {
                    var apps = profile.Applications.Select(a => a.ApplicationName).ToList();
                    if (apps.Count > 0) profileData["applications"] = apps;
                }
                catch { /* Some profiles throw on Applications access */ }

                // Get managed settings — first pass via NvAPIWrapper (fast)
                var settings = new Dictionary<string, uint>();
                try
                {
                    foreach (var setting in profile.Settings)
                    {
                        if (managedSet.Contains(setting.SettingId) && setting.CurrentValue is uint v)
                            settings[$"0x{setting.SettingId:X8}"] = v;
                    }
                }
                catch { /* Some profiles throw on Settings access */ }

                // Second pass: raw API for ALL managed settings not yet found
                // (needed for newer settings written via raw API that NvAPIWrapper can't enumerate)
                if (sessionHandle != IntPtr.Zero)
                {
                    var profileHandle = GetHandlePtr(profile.Handle);
                    if (profileHandle != IntPtr.Zero)
                    {
                        foreach (var settingId in ManagedSettingIds)
                        {
                            if (settings.ContainsKey($"0x{settingId:X8}")) continue;
                            try
                            {
                                var rawVal = GetSettingRawNvApi(sessionHandle, profileHandle, settingId);
                                if (rawVal.HasValue)
                                    settings[$"0x{settingId:X8}"] = rawVal.Value;
                            }
                            catch { /* Skip unreadable settings */ }
                        }
                    }
                }

                if (settings.Count > 0)
                {
                    profileData["settings"] = settings;
                    result[profile.Name] = profileData;
                    CrashReporter.Log($"[DlssPresetService.ExportProfiles] '{gameName}' → profile '{profile.Name}': {settings.Count} settings");
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DlssPresetService.ExportProfiles] Skipping '{gameName}': {ex.Message}");
            }
        }

        CrashReporter.Log($"[DlssPresetService.ExportProfiles] Exported {result.Count} profiles with custom settings");
        return result;
    }

    /// <summary>Imports NVIDIA profile settings from a previously exported dictionary.</summary>
    public int ImportProfiles(Dictionary<string, object> data)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null) return 0;
        int importedCount = 0;

        foreach (var kvp in data)
        {
            try
            {
                var profileName = kvp.Key;
                if (kvp.Value is not System.Text.Json.JsonElement elem) continue;

                // Find or create profile
                DriverSettingsProfile? profile = null;
                if (!_cachedProfiles.TryGetValue(profileName, out profile))
                {
                    // Create profile
                    profile = DriverSettingsProfile.CreateProfile(_session, profileName, null);
                    _cachedProfiles.TryAdd(profileName, profile);
                    CrashReporter.Log($"[DlssPresetService.ImportProfiles] Created profile '{profileName}'");
                }

                // Register applications
                if (elem.TryGetProperty("applications", out var appsElem) && appsElem.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var existingApps = profile.Applications.Select(a => a.ApplicationName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var appElem in appsElem.EnumerateArray())
                    {
                        var appName = appElem.GetString();
                        if (!string.IsNullOrEmpty(appName) && !existingApps.Contains(appName))
                        {
                            try
                            {
                                ProfileApplication.CreateApplication(profile, appName, profileName, "", Array.Empty<string>(), false, "");
                                CrashReporter.Log($"[DlssPresetService.ImportProfiles] Registered '{appName}' on '{profileName}'");
                            }
                            catch { }
                        }
                    }
                }

                // Apply settings
                if (elem.TryGetProperty("settings", out var settingsElem) && settingsElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in settingsElem.EnumerateObject())
                    {
                        if (uint.TryParse(prop.Name.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var settingId))
                        {
                            var value = prop.Value.GetUInt32();
                            try
                            {
                                profile.SetSetting(settingId, value);
                            }
                            catch
                            {
                                // Fallback to raw API for settings NvAPIWrapper doesn't know
                                var sH = GetHandlePtr(_session.Handle);
                                var pH = GetHandlePtr(profile.Handle);
                                if (sH != IntPtr.Zero && pH != IntPtr.Zero)
                                    SetSettingRawNvApi(sH, pH, settingId, value);
                            }
                        }
                    }
                }

                importedCount++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DlssPresetService.ImportProfiles] Error importing '{kvp.Key}' — {ex.Message}");
            }
        }

        if (importedCount > 0)
            _session.Save();

        CrashReporter.Log($"[DlssPresetService.ImportProfiles] Imported {importedCount} profiles");
        return importedCount;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private uint GetPreset(string gameName, string installPath, uint settingId)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return 0;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null) return 0;

            var setting = profile.Settings.FirstOrDefault(s => s.SettingId == settingId);
            if (setting?.CurrentValue is uint value)
                return value;

            // Fallback: try raw NVAPI for newer settings not visible through NvAPIWrapper
            var sessionHandle = GetHandlePtr(_session.Handle);
            var profileHandle = GetHandlePtr(profile.Handle);
            if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
            {
                var rawValue = GetSettingRawNvApi(sessionHandle, profileHandle, settingId);
                if (rawValue.HasValue)
                    return rawValue.Value;
            }

            return 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.GetPreset] Error for '{gameName}' — {ex.Message}");
            return 0;
        }
    }

    private bool SetPreset(string gameName, string installPath, uint settingId, uint preset)
    {
        if (!_isSupported || _session == null || _cachedProfiles == null)
            return false;

        try
        {
            var profile = FindProfile(gameName, installPath);
            if (profile == null)
            {
                if (!AutoCreateProfiles)
                {
                    CrashReporter.Log($"[DlssPresetService.SetPreset] No profile found for '{gameName}' (auto-create disabled)");
                    return false;
                }
                // Auto-create a profile for this game
                profile = CreateProfileForGame(gameName, installPath);
                if (profile == null)
                {
                    CrashReporter.Log($"[DlssPresetService.SetPreset] No profile found and could not create one for '{gameName}'");
                    return false;
                }
            }
            else if (AutoCreateProfiles && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                // Ensure the game's exe is registered in the profile's Applications list
                EnsureExeRegistered(profile, gameName, installPath);
            }

            profile.SetSetting(settingId, preset);
            _session.Save();

            CrashReporter.Log($"[DlssPresetService.SetPreset] Set 0x{settingId:X8}={preset} for '{gameName}'");
            return true;
        }
        catch (Exception ex)
        {
            // If SETTING_NOT_FOUND, try direct raw NVAPI call (bypasses NvAPIWrapper entirely)
            if (ex.Message.Contains("SETTING_NOT_FOUND"))
            {
                try
                {
                    var profile = FindProfile(gameName, installPath);
                    if (profile != null && _session != null)
                    {
                        // Get raw IntPtr handles from the NvAPIWrapper structs
                        var sessionHandle = GetHandlePtr(_session.Handle);
                        var profileHandle = GetHandlePtr(profile.Handle);
                        if (sessionHandle != IntPtr.Zero && profileHandle != IntPtr.Zero)
                        {
                            if (SetSettingRawNvApi(sessionHandle, profileHandle, settingId, preset))
                                return true;
                        }
                    }
                }
                catch (Exception ex2)
                {
                    CrashReporter.Log($"[DlssPresetService.SetPreset] Raw NVAPI fallback failed for '{gameName}' — {ex2.Message}");
                }
            }
            CrashReporter.Log($"[DlssPresetService.SetPreset] Error for '{gameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds the NVIDIA driver profile for a game by matching title or exe names.
    /// </summary>
    private DriverSettingsProfile? FindProfile(string gameName, string installPath)
    {
        if (_cachedProfiles == null) return null;

        // Try exact title match first
        if (_cachedProfiles.TryGetValue(gameName, out var exactProfile))
            return exactProfile;

        // Try matching by exe names in the install path (recursive to handle subdirectories)
        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
        {
            try
            {
                var exeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var file in Directory.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories))
                        exeNames.Add(Path.GetFileName(file));
                }
                catch (UnauthorizedAccessException)
                {
                    // Fallback to top-level only if recursive fails (WindowsApps etc.)
                    foreach (var file in Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly))
                        exeNames.Add(Path.GetFileName(file)!);
                }

                CrashReporter.Log($"[DlssPresetService.FindProfile] '{gameName}' — found {exeNames.Count} exe(s) in '{installPath}': {string.Join(", ", exeNames.Take(5))}");

                // Try matching profile applications against exe names
                foreach (var profile in _cachedProfiles.Values)
                {
                    foreach (var app in profile.Applications)
                    {
                        if (exeNames.Contains(app.ApplicationName))
                        {
                            CrashReporter.Log($"[DlssPresetService.FindProfile] Matched profile '{profile.Name}' via app '{app.ApplicationName}'");
                            return profile;
                        }
                    }
                }

                // Try matching profile NAME against exe names (custom profiles named after the exe)
                foreach (var exeName in exeNames)
                {
                    if (_cachedProfiles.TryGetValue(exeName, out var exeProfile))
                    {
                        CrashReporter.Log($"[DlssPresetService.FindProfile] Matched profile '{exeProfile.Name}' via profile name == exe name");
                        return exeProfile;
                    }
                }

                // Try fuzzy title match (strip special characters like colons, compare case-insensitive)
                var normalizedGameName = NormalizeForMatch(gameName);
                foreach (var kvp in _cachedProfiles)
                {
                    if (NormalizeForMatch(kvp.Key) == normalizedGameName)
                    {
                        CrashReporter.Log($"[DlssPresetService.FindProfile] Matched profile '{kvp.Key}' via fuzzy title match for '{gameName}'");
                        return kvp.Value;
                    }
                }

                CrashReporter.Log($"[DlssPresetService.FindProfile] No profile matched any exe in '{installPath}'");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DlssPresetService.FindProfile] Error scanning '{installPath}' — {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the raw IntPtr from an NvAPIWrapper handle struct via reflection.
    /// </summary>
    private static IntPtr GetHandlePtr<T>(T handle) where T : struct
    {
        var field = typeof(T).GetField("_MemoryAddress",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null) return IntPtr.Zero;
        return (IntPtr)field.GetValue(handle)!;
    }

    /// <summary>
    /// Normalizes a string for fuzzy matching by removing special characters and lowercasing.
    /// </summary>
    private static string NormalizeForMatch(string input)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == ' ')
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Creates a new NVIDIA driver profile for a game that doesn't have one.
    /// Uses the largest exe in the install path as the application name.
    /// </summary>
    private DriverSettingsProfile? CreateProfileForGame(string gameName, string installPath)
    {
        if (_session == null || _cachedProfiles == null) return null;
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) return null;

        try
        {
            // Find the game exe (largest exe, excluding known non-game names)
            var excludeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "unins000", "UnityCrashHandler64", "UnityCrashHandler32", "CrashReporter", "launcher" };
            string? gameExe = null;
            try
            {
                gameExe = Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(e => !excludeNames.Contains(Path.GetFileNameWithoutExtension(e)))
                    .OrderByDescending(e => new FileInfo(e).Length)
                    .Select(Path.GetFileName)
                    .FirstOrDefault();
            }
            catch { }

            if (string.IsNullOrEmpty(gameExe)) return null;

            // Create the profile
            var profile = DriverSettingsProfile.CreateProfile(_session, gameName, null);

            // Add the exe as an application
            ProfileApplication.CreateApplication(profile, gameExe, gameName, "", Array.Empty<string>(), false, "");

            _session.Save();

            // Cache the new profile
            _cachedProfiles.TryAdd(gameName, profile);

            CrashReporter.Log($"[DlssPresetService.CreateProfileForGame] Created profile '{gameName}' with app '{gameExe}'");
            ProfilesCreatedCount++;
            return profile;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.CreateProfileForGame] Failed for '{gameName}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Ensures the game's exe is registered in the profile's Applications list.
    /// If no exe from the install path is found in the profile, adds the largest exe.
    /// This is needed because NVIDIA applies settings based on the Applications list,
    /// not the profile name — a profile matched by title without the exe registered won't work.
    /// </summary>
    private void EnsureExeRegistered(DriverSettingsProfile profile, string gameName, string installPath)
    {
        try
        {
            // Get exe names from install path
            var excludeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "unins000", "UnityCrashHandler64", "UnityCrashHandler32", "CrashReporter", "launcher" };
            var exeFiles = new List<string>();
            try
            {
                exeFiles = Directory.GetFiles(installPath, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(e => !excludeNames.Contains(Path.GetFileNameWithoutExtension(e)))
                    .Where(e => !Path.GetFileName(e).Contains(" - copy", StringComparison.OrdinalIgnoreCase)
                             && !Path.GetFileName(e).Contains(" copy", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch { return; }

            if (exeFiles.Count == 0) return;

            // Check if any exe is already registered
            var registeredApps = profile.Applications.Select(a => a.ApplicationName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var exe in exeFiles)
            {
                if (registeredApps.Contains(Path.GetFileName(exe)))
                    return; // Already registered
            }

            // Not registered — add the largest exe
            var gameExe = exeFiles.OrderByDescending(e => new FileInfo(e).Length).Select(Path.GetFileName).FirstOrDefault();
            if (string.IsNullOrEmpty(gameExe)) return;

            ProfileApplication.CreateApplication(profile, gameExe, gameName, "", Array.Empty<string>(), false, "");
            _session?.Save();
            CrashReporter.Log($"[DlssPresetService.EnsureExeRegistered] Added '{gameExe}' to profile '{profile.Name}' for '{gameName}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.EnsureExeRegistered] Failed for '{gameName}' — {ex.Message}");
        }
    }
}
