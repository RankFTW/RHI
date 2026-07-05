using System.Runtime.InteropServices;
using NvAPIWrapper;
using NvAPIWrapper.DRS;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages DLSS preset overrides via the NVIDIA Driver Settings (NvDRS) API.
/// Presets are per-game settings stored in the NVIDIA driver profile.
/// Silently no-ops on AMD/Intel systems where NVAPI is unavailable.
/// </summary>
public partial class DlssPresetService
{
    // ── Raw NVAPI P/Invoke for settings that NvAPIWrapper doesn't recognize ───
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI_QueryInterface(uint id);

    private const uint NVAPI_DRS_SET_SETTING_ID = 0x8A2CF5F5; // Primary (R610+), fallback: 0x577DD202
    private const uint NVAPI_DRS_SET_SETTING_LEGACY_ID = 0x577DD202; // Legacy 3-param version (works for BINARY settings)
    private const uint NVAPI_DRS_GET_SETTING_ID = 0xEA99498D; // Primary (R610+), fallback: 0x73BF8338
    private const uint NVAPI_DRS_SAVE_SETTINGS_ID = 0xFCBC7E14;
    private const uint NVAPI_DRS_RESTORE_PROFILE_DEFAULT_ID = 0xFA5B6166;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_DRS_SetSettingPtr_t(IntPtr hSession, IntPtr hProfile, IntPtr pSetting, uint x, uint y);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_DRS_SetSettingLegacy_t(IntPtr hSession, IntPtr hProfile, IntPtr pSetting);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_DRS_GetSettingPtr_t(IntPtr hSession, IntPtr hProfile, uint settingId, IntPtr pSetting, ref uint x);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_DRS_SaveSettings_t(IntPtr hSession);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_DRS_RestoreProfileDefault_t(IntPtr hSession, IntPtr hProfile);

    private static NvAPI_DRS_SetSettingPtr_t? _nativeSetSettingPtr;
    private static NvAPI_DRS_SetSettingLegacy_t? _nativeSetSettingLegacy;
    private static NvAPI_DRS_GetSettingPtr_t? _nativeGetSettingPtr;
    private static NvAPI_DRS_SaveSettings_t? _nativeSaveSettings;
    private static NvAPI_DRS_RestoreProfileDefault_t? _nativeRestoreProfileDefault;

    private static void EnsureNativeFunctions()
    {
        if (_nativeSetSettingPtr != null) return;
        var setPtr = NvAPI_QueryInterface(NVAPI_DRS_SET_SETTING_ID);
        var setLegacyPtr = NvAPI_QueryInterface(NVAPI_DRS_SET_SETTING_LEGACY_ID);
        var getPtr = NvAPI_QueryInterface(NVAPI_DRS_GET_SETTING_ID);
        var savePtr = NvAPI_QueryInterface(NVAPI_DRS_SAVE_SETTINGS_ID);
        var restorePtr = NvAPI_QueryInterface(NVAPI_DRS_RESTORE_PROFILE_DEFAULT_ID);
        if (setPtr != IntPtr.Zero)
            _nativeSetSettingPtr = Marshal.GetDelegateForFunctionPointer<NvAPI_DRS_SetSettingPtr_t>(setPtr);
        if (setLegacyPtr != IntPtr.Zero)
            _nativeSetSettingLegacy = Marshal.GetDelegateForFunctionPointer<NvAPI_DRS_SetSettingLegacy_t>(setLegacyPtr);
        if (getPtr != IntPtr.Zero)
            _nativeGetSettingPtr = Marshal.GetDelegateForFunctionPointer<NvAPI_DRS_GetSettingPtr_t>(getPtr);
        if (savePtr != IntPtr.Zero)
            _nativeSaveSettings = Marshal.GetDelegateForFunctionPointer<NvAPI_DRS_SaveSettings_t>(savePtr);
        if (restorePtr != IntPtr.Zero)
            _nativeRestoreProfileDefault = Marshal.GetDelegateForFunctionPointer<NvAPI_DRS_RestoreProfileDefault_t>(restorePtr);
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

    // DLSS Preset Override mode (tells driver how to interpret preset selection)
    // N/A=0x00 (no override), Latest Recommended=0x01, Custom=0x02
    private const uint DLSS_SR_PRESET_OVERRIDE_ID = 0x00634291;

    // DLSS Latest DLL driver override (tells driver to inject its own bundled DLL)
    private const uint DLSS_SR_LATEST_DLL_ID = 0x10E41E01;
    private const uint DLSS_RR_LATEST_DLL_ID = 0x10E41E02;
    private const uint DLSS_FG_LATEST_DLL_ID = 0x10E41E03;

    // Multi Frame Generation (MFG) settings
    private const uint MFG_MODE_OVERRIDE_ID = 0x10308298;       // Off=0, Fixed=2, Dynamic=4
    private const uint MFG_GENERATION_FACTOR_ID = 0x104D6667;   // App-controlled=0, 2x=1, 3x=2, 4x=3, 5x=4, 6x=5
    private const uint MFG_DYNAMIC_MAX_COUNT_ID = 0x10562D0F;   // Off=0, 3x=2, 4x=3, 5x=4, 6x=5
    private const uint MFG_DYNAMIC_TARGET_FPS_ID = 0x10CF4125;  // Off=0, MaxRefresh=0x01000000, or FPS value directly

    private const uint RENDER_SCALE_DEFAULT = 0x03;
    private const uint RENDER_SCALE_CUSTOM = 0x06;

    // ── Preset values ─────────────────────────────────────────────────────────
    public static (string Name, uint Value)[] SrPresets =
    [
        ("Default", 0x00000000),
        ("J - TF1", 0x0000000A),
        ("K - TF1", 0x0000000B),
        ("L - TF2", 0x0000000C),
        ("M - TF2", 0x0000000D),
        ("Latest Recommended", 0x00FFFFFF),
    ];

    public static (string Name, uint Value)[] RrPresets =
    [
        ("Default", 0x00000000),
        ("D - TF1", 0x00000004),
        ("E - TF1", 0x00000005),
        ("Latest Recommended", 0x00FFFFFF),
    ];

    public static (string Name, uint Value)[] FgPresets =
    [
        ("Default", 0x00000000),
        ("A", 0x00000001),
        ("B", 0x00000002),
        ("Latest Recommended", 0x00FFFFFE),
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

    /// <summary>
    /// Per-game profile lookup cache. Avoids repeated expensive FindProfile calls
    /// (recursive exe scanning, profile iteration) for the same game within a session.
    /// Key = gameName, Value = matched profile (or null sentinel via _profileCacheMisses).
    /// </summary>
    private readonly Dictionary<string, DriverSettingsProfile?> _profileLookupCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Invalidates the per-game profile lookup cache. Call after session reload.</summary>
    private void InvalidateProfileLookupCache() => _profileLookupCache.Clear();

    /// <summary>
    /// Generic exe names excluded from profile matching — these appear in many games
    /// and cause false matches against unrelated NVIDIA profiles.
    /// </summary>
    private static readonly HashSet<string> _defaultExcludedExeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Launcher.exe", "launcher.exe",
        "EpicOnlineServicesInstaller.exe",
        "UnityCrashHandler64.exe", "UnityCrashHandler32.exe",
        "CrashReporter.exe", "CrashReportClient.exe",
        "unins000.exe", "Uninstall.exe",
        "dxsetup.exe", "DXSETUP.exe",
        "vcredist_x64.exe", "vcredist_x86.exe",
        "UE4PrereqSetup_x64.exe",
        "crashpad_handler.exe",
        "UbisoftConnectInstaller.exe",
    };

    private HashSet<string> _excludedProfileExeNames = _defaultExcludedExeNames;
    private Dictionary<string, string>? _launchExeOverrides;
    private Dictionary<string, string>? _profileNameOverrides;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string dllToLoad);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);

    public bool IsSupported => _isSupported;

    /// <summary>The NVIDIA driver version string (e.g. "572.16") or empty if not available.</summary>
    public string DriverVersionString { get; private set; } = "";

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

            // Capture driver version
            try
            {
                var driverVer = NVIDIA.DriverVersion;
                DriverVersionString = $"{driverVer / 100}.{driverVer % 100:D2}";
            }
            catch { DriverVersionString = ""; }

            _session = DriverSettingsSession.CreateAndLoad();
            _cachedProfiles = new Dictionary<string, DriverSettingsProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in _session.Profiles)
            {
                _cachedProfiles.TryAdd(profile.Name, profile);
            }
            InvalidateProfileLookupCache();
            _isSupported = true;

            CrashReporter.Log($"[DlssPresetService.Initialize] NVAPI initialized, {_cachedProfiles.Count} profiles cached");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssPresetService.Initialize] Failed — {ex.Message}");
            _isSupported = false;
        }
    }

    // ── Manifest-driven preset overrides ─────────────────────────────────────

    /// <summary>
    /// Applies manifest-defined DLSS preset overrides. Appends new presets to
    /// the existing arrays (after "Default", before any duplicates).
    /// </summary>
    public static void ApplyManifestPresets(RemoteManifest? manifest)
    {
        if (manifest?.DlssPresets == null) return;

        if (manifest.DlssPresets.Sr is { Count: > 0 } sr)
            SrPresets = MergePresets(SrPresets, sr);
        if (manifest.DlssPresets.Rr is { Count: > 0 } rr)
            RrPresets = MergePresets(RrPresets, rr);
        if (manifest.DlssPresets.Fg is { Count: > 0 } fg)
            FgPresets = MergePresets(FgPresets, fg);

        CrashReporter.Log($"[DlssPresetService.ApplyManifestPresets] SR={SrPresets.Length}, RR={RrPresets.Length}, FG={FgPresets.Length}");
    }

    /// <summary>
    /// Applies manifest-driven profile matching config: additional exe exclusions
    /// and launch exe overrides for direct profile matching.
    /// </summary>
    public void ApplyManifestProfileConfig(RemoteManifest? manifest)
    {
        if (manifest == null) return;

        // Merge manifest exe exclusions with hardcoded defaults
        if (manifest.ProfileExeExclusions is { Count: > 0 } exclusions)
        {
            _excludedProfileExeNames = new HashSet<string>(_defaultExcludedExeNames, StringComparer.OrdinalIgnoreCase);
            foreach (var exe in exclusions)
                _excludedProfileExeNames.Add(exe);
        }

        // Store launch exe overrides for direct profile matching
        _launchExeOverrides = manifest.LaunchExeOverrides;
        _profileNameOverrides = manifest.ProfileNameOverrides;

        // Invalidate the lookup cache since overrides may now route games to different profiles
        InvalidateProfileLookupCache();
    }

    private static (string Name, uint Value)[] MergePresets(
        (string Name, uint Value)[] existing,
        List<ManifestPresetEntry> entries)
    {
        var merged = new List<(string Name, uint Value)>(existing);

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;

            if (entry.Disabled == true)
            {
                // Remove by name (case-insensitive), but never remove "Default"
                if (!entry.Name.Equals("Default", StringComparison.OrdinalIgnoreCase))
                    merged.RemoveAll(p => p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            // Add if not already present — insert alphabetically between Default (first) and Latest Recommended (last)
            if (merged.Any(p => p.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))) continue;

            // Find insertion point: after Default, before Latest Recommended, alphabetical among the rest
            int insertIdx = merged.Count; // default: end
            for (int i = 1; i < merged.Count; i++) // skip index 0 (Default)
            {
                if (merged[i].Name.Equals("Latest Recommended", StringComparison.OrdinalIgnoreCase))
                {
                    insertIdx = i; // insert before Latest Recommended
                    break;
                }
                if (string.Compare(entry.Name, merged[i].Name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    insertIdx = i;
                    break;
                }
            }
            merged.Insert(insertIdx, (entry.Name, (uint)entry.Value));
        }
        return merged.ToArray();
    }

    // ── Get presets ───────────────────────────────────────────────────────────

    public uint GetSrPreset(string gameName, string installPath)
        => GetPreset(gameName, installPath, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID);

    public uint GetRrPreset(string gameName, string installPath)
        => GetPreset(gameName, installPath, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID);

    public uint GetFgPreset(string gameName, string installPath)
        => GetPreset(gameName, installPath, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID);

    // ── DLSS Latest DLL driver override detection ─────────────────────────────

    /// <summary>Returns true if the NVIDIA driver is overriding the game's DLSS SR DLL with its own latest version.</summary>
    public bool IsSrDriverOverrideActive(string gameName, string installPath)
        => GetPreset(gameName, installPath, DLSS_SR_LATEST_DLL_ID) == 1;

    /// <summary>Returns true if the NVIDIA driver is overriding the game's DLSS RR DLL.</summary>
    public bool IsRrDriverOverrideActive(string gameName, string installPath)
        => GetPreset(gameName, installPath, DLSS_RR_LATEST_DLL_ID) == 1;

    /// <summary>Returns true if the NVIDIA driver is overriding the game's DLSS FG DLL.</summary>
    public bool IsFgDriverOverrideActive(string gameName, string installPath)
        => GetPreset(gameName, installPath, DLSS_FG_LATEST_DLL_ID) == 1;

    // ── Set presets ───────────────────────────────────────────────────────────

    public bool SetSrPreset(string gameName, string installPath, uint preset)
    {
        if (preset == 0x00000000u)
        {
            // Delete the setting so it inherits from global/base profile
            DeletePreset(gameName, installPath, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID);
            DeletePreset(gameName, installPath, DLSS_SR_PRESET_OVERRIDE_ID);
            return true;
        }

        var result = SetPreset(gameName, installPath, NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);
        if (result)
        {
            // Set the companion "Preset Override" setting so the driver knows to apply it
            uint overrideMode = preset == 0x00FFFFFFu ? 0x00000001u  // Latest Recommended
                              : 0x00000002u;                         // Custom (any specific preset)
            SetPreset(gameName, installPath, DLSS_SR_PRESET_OVERRIDE_ID, overrideMode);
        }
        return result;
    }

    public bool SetRrPreset(string gameName, string installPath, uint preset)
    {
        if (preset == 0x00000000u)
        {
            DeletePreset(gameName, installPath, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID);
            return true;
        }
        return SetPreset(gameName, installPath, NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);
    }

    public bool SetFgPreset(string gameName, string installPath, uint preset)
    {
        if (preset == 0x00000000u)
        {
            DeletePreset(gameName, installPath, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID);
            return true;
        }
        return SetPreset(gameName, installPath, NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID, preset);
    }

    // ── Multi Frame Generation (MFG) ─────────────────────────────────────────

    public uint GetMfgMode(string gameName, string installPath)
        => GetPreset(gameName, installPath, MFG_MODE_OVERRIDE_ID);

    public bool SetMfgMode(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, MFG_MODE_OVERRIDE_ID, value);

    public uint GetMfgGenerationFactor(string gameName, string installPath)
        => GetPreset(gameName, installPath, MFG_GENERATION_FACTOR_ID);

    public bool SetMfgGenerationFactor(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, MFG_GENERATION_FACTOR_ID, value);

    public uint GetMfgDynamicMaxCount(string gameName, string installPath)
        => GetPreset(gameName, installPath, MFG_DYNAMIC_MAX_COUNT_ID);

    public bool SetMfgDynamicMaxCount(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, MFG_DYNAMIC_MAX_COUNT_ID, value);

    public uint GetMfgDynamicTargetFps(string gameName, string installPath)
        => GetPreset(gameName, installPath, MFG_DYNAMIC_TARGET_FPS_ID);

    public bool SetMfgDynamicTargetFps(string gameName, string installPath, uint value)
        => SetPreset(gameName, installPath, MFG_DYNAMIC_TARGET_FPS_ID, value);

    // ── Lilium HDR DXVK Present Method ────────────────────────────────────────

    private const uint VULKAN_OGL_PRESENT_METHOD_ID = 0x20D690F8; // Present Method (Vulkan/OpenGL)
    private const uint OGL_DX_PRESENT_METHOD_ID = 0x20324987;     // Present Method (OpenGL/DX) — includes DXVK promotion flags

    /// <summary>
    /// Sets the NVIDIA profile settings required for Lilium HDR DXVK to output HDR correctly.
    /// Sets Vulkan present method to "layered on DXGI Swapchain" and enables DXVK promotion.
    /// </summary>
    public void SetLiliumPresentMethod(string gameName, string installPath)
    {
        SetPreset(gameName, installPath, VULKAN_OGL_PRESENT_METHOD_ID, 0x00000001); // Prefer layered on DXGI Swapchain
        SetPreset(gameName, installPath, OGL_DX_PRESENT_METHOD_ID, 0x00080004);     // Allow DXVK Promotion (DXGI/DirectFlip)
    }

    /// <summary>
    /// Clears the Lilium HDR present method settings (restores to Auto / default).
    /// </summary>
    public void ClearLiliumPresentMethod(string gameName, string installPath)
    {
        SetPreset(gameName, installPath, VULKAN_OGL_PRESENT_METHOD_ID, 0x00000002); // Auto (default)
        SetPreset(gameName, installPath, OGL_DX_PRESENT_METHOD_ID, 0x00000000);     // Default (no flags)
    }

}
