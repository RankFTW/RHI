using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

/// <summary>
/// Controls NVIDIA Digital Vibrance per-display via raw NVAPI.
/// DVC range: 0-100, where 50 is neutral/default.
/// Silently no-ops on AMD/Intel systems where NVAPI is unavailable.
/// </summary>
public static class DigitalVibranceService
{
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI_QueryInterface(uint id);

    // NVAPI function IDs for DVC
    private const uint NVAPI_ENUM_NVIDIA_DISPLAY_HANDLE = 0x9ABDD40D;
    private const uint NVAPI_GET_ASSOCIATED_DISPLAY_NAME = 0x22A78B05;
    private const uint NVAPI_GET_DVC_INFO = 0x4085DE45;        // Non-Ex: version + current + min + max (4 fields)
    private const uint NVAPI_GET_DVC_INFO_EX = 0x0E45002D;     // Ex: version + current + min + max + default (5 fields)
    private const uint NVAPI_SET_DVC_LEVEL = 0x172409B4;       // Non-Ex: takes (handle, outputId, level) directly
    private const uint NVAPI_SET_DVC_LEVEL_EX = 0x4A82C2B1;    // Ex: takes struct

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_Initialize_t();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_EnumNvidiaDisplayHandle_t(int thisEnum, ref IntPtr pHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GetAssociatedNvidiaDisplayName_t(IntPtr hNvDisplay, IntPtr pName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GetDVCInfoEx_t(IntPtr hNvDisplay, int outputId, IntPtr pDVCInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_SetDVCLevelEx_t(IntPtr hNvDisplay, int outputId, IntPtr pDVCInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GetDVCInfo_t(IntPtr hNvDisplay, int outputId, IntPtr pDVCInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_SetDVCLevel_t(IntPtr hNvDisplay, int outputId, int level);

    private static NvAPI_EnumNvidiaDisplayHandle_t? _enumDisplayHandle;
    private static NvAPI_GetAssociatedNvidiaDisplayName_t? _getDisplayName;
    private static NvAPI_GetDVCInfoEx_t? _getDvcInfoEx;
    private static NvAPI_SetDVCLevelEx_t? _setDvcLevelEx;
    private static NvAPI_GetDVCInfo_t? _getDvcInfo;
    private static NvAPI_SetDVCLevel_t? _setDvcLevel;
    private static bool _initialized;
    private static bool _isSupported;
    private static bool _useExApi; // true = use Ex variants, false = use non-Ex

    /// <summary>
    /// DVC struct sizes:
    /// Non-Ex (NV_DISPLAY_DVC_INFO): version + current + min + max = 4 * 4 = 16 bytes
    /// Ex (NV_DISPLAY_DVC_INFO_EX): version + current + min + max + default = 5 * 4 = 20 bytes
    /// Version field = size | (1 << 16)
    /// </summary>
    private const int DVC_INFO_SIZE = 16;
    private const int DVC_INFO_VERSION = DVC_INFO_SIZE | (1 << 16);
    private const int DVC_INFO_EX_SIZE = 20;
    private const int DVC_INFO_EX_VERSION = DVC_INFO_EX_SIZE | (1 << 16);

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            // NvAPI_Initialize must be called before any other NVAPI function resolves correctly
            var initPtr = NvAPI_QueryInterface(0x0150E828); // NvAPI_Initialize
            if (initPtr != IntPtr.Zero)
            {
                var initFn = Marshal.GetDelegateForFunctionPointer<NvAPI_Initialize_t>(initPtr);
                initFn(); // Safe to call multiple times — subsequent calls are no-ops
            }

            var enumPtr = NvAPI_QueryInterface(NVAPI_ENUM_NVIDIA_DISPLAY_HANDLE);
            var namePtr = NvAPI_QueryInterface(NVAPI_GET_ASSOCIATED_DISPLAY_NAME);

            if (enumPtr == IntPtr.Zero)
            {
                CrashReporter.Log("[DigitalVibranceService.EnsureInitialized] EnumDisplayHandle not available");
                return;
            }

            _enumDisplayHandle = Marshal.GetDelegateForFunctionPointer<NvAPI_EnumNvidiaDisplayHandle_t>(enumPtr);
            if (namePtr != IntPtr.Zero)
                _getDisplayName = Marshal.GetDelegateForFunctionPointer<NvAPI_GetAssociatedNvidiaDisplayName_t>(namePtr);

            // Try Ex variants first (have defaultLevel field)
            var getExPtr = NvAPI_QueryInterface(NVAPI_GET_DVC_INFO_EX);
            var setExPtr = NvAPI_QueryInterface(NVAPI_SET_DVC_LEVEL_EX);

            if (getExPtr != IntPtr.Zero && setExPtr != IntPtr.Zero)
            {
                _getDvcInfoEx = Marshal.GetDelegateForFunctionPointer<NvAPI_GetDVCInfoEx_t>(getExPtr);
                _setDvcLevelEx = Marshal.GetDelegateForFunctionPointer<NvAPI_SetDVCLevelEx_t>(setExPtr);
                _useExApi = true;
                _isSupported = true;
                CrashReporter.Log("[DigitalVibranceService.EnsureInitialized] DVC Ex functions resolved successfully");
                return;
            }

            // Fall back to non-Ex variants
            var getPtr = NvAPI_QueryInterface(NVAPI_GET_DVC_INFO);
            var setPtr = NvAPI_QueryInterface(NVAPI_SET_DVC_LEVEL);

            if (getPtr != IntPtr.Zero && setPtr != IntPtr.Zero)
            {
                _getDvcInfo = Marshal.GetDelegateForFunctionPointer<NvAPI_GetDVCInfo_t>(getPtr);
                _setDvcLevel = Marshal.GetDelegateForFunctionPointer<NvAPI_SetDVCLevel_t>(setPtr);
                _useExApi = false;
                _isSupported = true;
                CrashReporter.Log("[DigitalVibranceService.EnsureInitialized] DVC non-Ex functions resolved successfully");
                return;
            }

            CrashReporter.Log($"[DigitalVibranceService.EnsureInitialized] DVC not available (getEx={getExPtr != IntPtr.Zero}, setEx={setExPtr != IntPtr.Zero}, get={getPtr != IntPtr.Zero}, set={setPtr != IntPtr.Zero})");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DigitalVibranceService.EnsureInitialized] Failed — {ex.Message}");
            _isSupported = false;
        }
    }

    /// <summary>Whether NVIDIA DVC is available on this system.</summary>
    public static bool IsSupported
    {
        get
        {
            EnsureInitialized();
            return _isSupported;
        }
    }

    /// <summary>
    /// Enumerates all active NVIDIA display handles with their display names.
    /// Returns (displayName, displayIndex) pairs.
    /// </summary>
    public static List<(string Name, int Index)> EnumerateDisplays()
    {
        var result = new List<(string Name, int Index)>();
        EnsureInitialized();
        if (!_isSupported || _enumDisplayHandle == null) return result;

        try
        {
            for (int i = 0; i < 16; i++)
            {
                var handle = IntPtr.Zero;
                int status = _enumDisplayHandle(i, ref handle);
                if (status != 0 || handle == IntPtr.Zero) break;

                string name = $"Display {i}";
                if (_getDisplayName != null)
                {
                    var nameBuffer = Marshal.AllocHGlobal(128);
                    try
                    {
                        int nameStatus = _getDisplayName(handle, nameBuffer);
                        if (nameStatus == 0)
                        {
                            var rawName = Marshal.PtrToStringAnsi(nameBuffer);
                            if (!string.IsNullOrWhiteSpace(rawName))
                                name = rawName;
                        }
                    }
                    finally { Marshal.FreeHGlobal(nameBuffer); }
                }

                result.Add((name, i));
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DigitalVibranceService.EnumerateDisplays] Error — {ex.Message}");
        }

        return result;
    }

    /// <summary>Gets the current DVC level for the given display index. Returns 50 (neutral) on failure.</summary>
    public static int GetLevel(int displayIndex)
    {
        EnsureInitialized();
        if (!_isSupported || _enumDisplayHandle == null) return 50;

        try
        {
            var handle = IntPtr.Zero;
            int status = _enumDisplayHandle(displayIndex, ref handle);
            if (status != 0 || handle == IntPtr.Zero) return 50;

            if (_useExApi && _getDvcInfoEx != null)
            {
                var ptr = Marshal.AllocHGlobal(DVC_INFO_EX_SIZE);
                try
                {
                    Marshal.WriteInt32(ptr, 0, DVC_INFO_EX_VERSION);
                    Marshal.WriteInt32(ptr, 4, 0);
                    Marshal.WriteInt32(ptr, 8, 0);
                    Marshal.WriteInt32(ptr, 12, 0);
                    Marshal.WriteInt32(ptr, 16, 0);

                    int result = _getDvcInfoEx(handle, 0, ptr);
                    if (result != 0) return 50;

                    return Marshal.ReadInt32(ptr, 4); // currentLevel
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            else if (_getDvcInfo != null)
            {
                var ptr = Marshal.AllocHGlobal(DVC_INFO_SIZE);
                try
                {
                    Marshal.WriteInt32(ptr, 0, DVC_INFO_VERSION);
                    Marshal.WriteInt32(ptr, 4, 0);
                    Marshal.WriteInt32(ptr, 8, 0);
                    Marshal.WriteInt32(ptr, 12, 0);

                    int result = _getDvcInfo(handle, 0, ptr);
                    if (result != 0) return 50;

                    return Marshal.ReadInt32(ptr, 4); // currentLevel
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }

            return 50;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DigitalVibranceService.GetLevel] Error for display {displayIndex} — {ex.Message}");
            return 50;
        }
    }

    /// <summary>Sets the DVC level (0-100) for the given display index.</summary>
    public static bool SetLevel(int displayIndex, int level)
    {
        EnsureInitialized();
        if (!_isSupported || _enumDisplayHandle == null) return false;

        level = Math.Clamp(level, 0, 100);

        try
        {
            var handle = IntPtr.Zero;
            int status = _enumDisplayHandle(displayIndex, ref handle);
            if (status != 0 || handle == IntPtr.Zero) return false;

            if (_useExApi && _setDvcLevelEx != null)
            {
                var ptr = Marshal.AllocHGlobal(DVC_INFO_EX_SIZE);
                try
                {
                    Marshal.WriteInt32(ptr, 0, DVC_INFO_EX_VERSION);
                    Marshal.WriteInt32(ptr, 4, level);
                    Marshal.WriteInt32(ptr, 8, 0);
                    Marshal.WriteInt32(ptr, 12, 100);
                    Marshal.WriteInt32(ptr, 16, 50);

                    int result = _setDvcLevelEx(handle, 0, ptr);
                    if (result != 0)
                    {
                        CrashReporter.Log($"[DigitalVibranceService.SetLevel] Ex NVAPI returned {result} for display {displayIndex}, level {level}");
                        return false;
                    }
                    return true;
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            else if (_setDvcLevel != null)
            {
                int result = _setDvcLevel(handle, 0, level);
                if (result != 0)
                {
                    CrashReporter.Log($"[DigitalVibranceService.SetLevel] NVAPI returned {result} for display {displayIndex}, level {level}");
                    return false;
                }
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DigitalVibranceService.SetLevel] Error for display {displayIndex}, level {level} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restores saved DVC levels for all displays.
    /// Key format in the dictionary: display index as string (e.g. "0", "1").
    /// </summary>
    public static void RestoreSavedLevels(Dictionary<string, int>? saved)
    {
        if (saved == null || saved.Count == 0) return;
        EnsureInitialized();
        if (!_isSupported) return;

        foreach (var (key, level) in saved)
        {
            if (int.TryParse(key, out var displayIndex))
            {
                var success = SetLevel(displayIndex, level);
                CrashReporter.Log($"[DigitalVibranceService.RestoreSavedLevels] Display {displayIndex} → {level} ({(success ? "OK" : "FAILED")})");
            }
        }
    }
}
