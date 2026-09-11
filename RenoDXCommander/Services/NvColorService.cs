using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

/// <summary>
/// Controls NVIDIA display output colour depth and dynamic range via raw NVAPI.
/// Uses NvAPI_Disp_ColorControl (0x92F9D80D) with NV_COLOR_DATA_V3.
/// </summary>
public static class NvColorService
{
    // ── NVAPI function IDs ────────────────────────────────────────────────────
    private const uint NVAPI_DISP_COLOR_CONTROL_ID          = 0x92F9D80D;
    private const uint NVAPI_DISP_GET_DISPLAYID_BY_NAME_ID  = 0xAE457190;
    private const uint NVAPI_ENUM_DISPLAY_HANDLE_ID         = 0x9ABDD40D;
    private const uint NVAPI_GET_ASSOCIATED_DISPLAY_NAME_ID = 0x22A78B05;
    private const uint NVAPI_DISP_GET_GDI_PRIMARY_ID        = 0x1E9D8A31;

    // ── cmd values ────────────────────────────────────────────────────────────
    private const byte NV_COLOR_CMD_GET = 1;
    private const byte NV_COLOR_CMD_SET = 2;

    // ── NV_BPC enum values ────────────────────────────────────────────────────
    public const byte BPC_DEFAULT = 0;
    public const byte BPC_6       = 4;
    public const byte BPC_8       = 1;
    public const byte BPC_10      = 2;
    public const byte BPC_12      = 3;
    public const byte BPC_16      = 5;

    // ── NV_DYNAMIC_RANGE enum values ──────────────────────────────────────────
    public const byte DYNAMIC_RANGE_VESA = 0; // Full
    public const byte DYNAMIC_RANGE_CEA  = 1; // Limited

    // ── NV_COLOR_FORMAT enum values ───────────────────────────────────────────
    public const byte COLOR_FORMAT_RGB      = 0;
    public const byte COLOR_FORMAT_YCbCr422 = 1;
    public const byte COLOR_FORMAT_YCbCr444 = 2;
    public const byte COLOR_FORMAT_DEFAULT  = 0xFE;
    public const byte COLOR_FORMAT_AUTO     = 0xFF;

    // ── NV_COLOR_DATA_V3 struct — actual size is 16 bytes (4-byte aligned fields) ─
    // Confirmed via NvAPI_Disp_ColorControl probe: size=16 returns success.
    // Layout (little-endian):
    //   [0-3]  version  (uint32) = size | (3 << 16)
    //   [4-5]  size     (uint16) = 16
    //   [6]    cmd      (byte)   = NV_COLOR_CMD_GET(1) or SET(2)
    //   [7]    padding
    //   [8]    colorFormat  (byte)
    //   [9]    colorimetry  (byte)
    //   [10]   dynamicRange (byte)
    //   [11]   padding
    //   [12]   bpc          (byte)
    //   [13-15] padding
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct NV_COLOR_DATA_V3
    {
        [FieldOffset(0)]  public uint   version;
        [FieldOffset(4)]  public ushort size;
        [FieldOffset(6)]  public byte   cmd;
        [FieldOffset(8)]  public byte   colorFormat;
        [FieldOffset(9)]  public byte   colorimetry;
        [FieldOffset(10)] public byte   dynamicRange;
        [FieldOffset(12)] public byte   bpc;
    }

    // ── Delegate types ────────────────────────────────────────────────────────
    private delegate int NvAPI_QueryInterface_t(uint id);
    private delegate int NvAPI_Initialize_t();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_Disp_ColorControl_t(uint displayId, ref NV_COLOR_DATA_V3 colorData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_DISP_GetDisplayIdByDisplayName_t(
        [MarshalAs(UnmanagedType.LPStr)] string displayName, out uint displayId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_EnumNvidiaDisplayHandle_t(int thisEnum, out IntPtr pNvDispHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvAPI_GetAssociatedNvidiaDisplayName_t(IntPtr nvDispHandle,
        [MarshalAs(UnmanagedType.LPStr)] System.Text.StringBuilder displayName);

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI_QueryInterface(uint id);

    // ── Cached function pointers ──────────────────────────────────────────────
    private static NvAPI_Disp_ColorControl_t?              _colorControl;
    private static NvAPI_DISP_GetDisplayIdByDisplayName_t? _getDisplayIdByName;
    private static NvAPI_EnumNvidiaDisplayHandle_t?        _enumDisplayHandle;
    private static NvAPI_GetAssociatedNvidiaDisplayName_t? _getDisplayName;
    private static bool _initialized;
    private static bool _initFailed;

    // ── Win32 for GDI display name enumeration ────────────────────────────────
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DISPLAY_DEVICE
    {
        public uint   cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]  public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint   StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private const uint DISPLAY_DEVICE_ACTIVE = 0x00000001;

    // ── CCD source name P/Invoke (for GDI device name → friendly name mapping) ─
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER_SMALL { public int type; public uint size; public ulong adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public int    type;
        public uint   size;
        public ulong  adapterId;
        public uint   id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }    // ── Public model ──────────────────────────────────────────────────────────
    public sealed record NvDisplay(uint DisplayId, string Name);

    public sealed record NvColorData(byte Bpc, byte DynamicRange, byte ColorFormat);

    // ── Initialisation ────────────────────────────────────────────────────────
    private static bool EnsureInit()
    {
        if (_initialized) return true;
        if (_initFailed)  return false;
        try
        {
            var colorControlPtr = NvAPI_QueryInterface(NVAPI_DISP_COLOR_CONTROL_ID);
            var getIdByNamePtr  = NvAPI_QueryInterface(NVAPI_DISP_GET_DISPLAYID_BY_NAME_ID);
            var enumHandlePtr   = NvAPI_QueryInterface(NVAPI_ENUM_DISPLAY_HANDLE_ID);
            var getNamePtr      = NvAPI_QueryInterface(NVAPI_GET_ASSOCIATED_DISPLAY_NAME_ID);

            if (colorControlPtr == IntPtr.Zero) { _initFailed = true; return false; }

            _colorControl    = Marshal.GetDelegateForFunctionPointer<NvAPI_Disp_ColorControl_t>(colorControlPtr);
            if (getIdByNamePtr != IntPtr.Zero)
                _getDisplayIdByName = Marshal.GetDelegateForFunctionPointer<NvAPI_DISP_GetDisplayIdByDisplayName_t>(getIdByNamePtr);
            if (enumHandlePtr != IntPtr.Zero)
                _enumDisplayHandle = Marshal.GetDelegateForFunctionPointer<NvAPI_EnumNvidiaDisplayHandle_t>(enumHandlePtr);
            if (getNamePtr != IntPtr.Zero)
                _getDisplayName = Marshal.GetDelegateForFunctionPointer<NvAPI_GetAssociatedNvidiaDisplayName_t>(getNamePtr);

            _initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NvColorService.EnsureInit] Failed — {ex.Message}");
            _initFailed = true;
            return false;
        }
    }

    // ── Display enumeration ───────────────────────────────────────────────────

    /// <summary>
    /// Enumerates active NVIDIA displays. Returns display ID + friendly name.
    /// Uses HdrToggleService for friendly names (CCD API returns proper EDID names).
    /// NVIDIA displayIds are obtained via NvAPI_DISP_GetDisplayIdByDisplayName.
    /// </summary>
    public static List<NvDisplay> GetDisplays()
    {
        var result = new List<NvDisplay>();
        if (!EnsureInit() || _getDisplayIdByName == null) return result;

        // Get displayIds for all active GDI displays
        var nvDisplays = new List<(string GdiName, uint DisplayId)>();
        for (int i = 1; i <= 12; i++)
        {
            var gdiName = $"\\\\.\\DISPLAY{i}";
            int ret = _getDisplayIdByName(gdiName, out uint displayId);
            if (ret == 0 && displayId != 0)
                nvDisplays.Add((gdiName, displayId));
        }

        if (nvDisplays.Count == 0) return result;

        // Get friendly names keyed by GDI device name so we match exactly,
        // not by index position (which is fragile when GDI and CCD orderings differ).
        var gdiNameMap = HdrToggleService.GetGdiNameMap();

        foreach (var (gdiName, displayId) in nvDisplays)
        {
            string name = gdiNameMap.TryGetValue(gdiName, out var friendly) ? friendly : gdiName;
            result.Add(new NvDisplay(displayId, name));
        }

        return result;
    }

    // ── Get / Set colour data ─────────────────────────────────────────────────

    /// <summary>Reads current colour depth and dynamic range for the given displayId.</summary>
    public static NvColorData? GetColorData(uint displayId)
    {
        if (!EnsureInit() || _colorControl == null) return null;
        try
        {
            var data = new NV_COLOR_DATA_V3
            {
                version = MakeVersion(3, ColorDataSize),
                size    = (ushort)ColorDataSize,
                cmd     = NV_COLOR_CMD_GET,
            };
            int ret = _colorControl(displayId, ref data);
            if (ret != 0)
            {
                CrashReporter.Log($"[NvColorService.GetColorData] NvAPI_Disp_ColorControl(GET) returned {ret} for displayId={displayId}");
                return null;
            }
            return new NvColorData(data.bpc, data.dynamicRange, data.colorFormat);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NvColorService.GetColorData] Exception — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sets colour depth and dynamic range for the given displayId.
    /// Returns true on success.
    /// </summary>
    public static bool SetColorData(uint displayId, byte bpc, byte dynamicRange)
    {
        if (!EnsureInit() || _colorControl == null) return false;
        try
        {
            // Read current format first so we don't clobber it
            byte colorFormat = COLOR_FORMAT_RGB;
            var current = GetColorData(displayId);
            if (current != null) colorFormat = current.ColorFormat;

            var data = new NV_COLOR_DATA_V3
            {
                version      = MakeVersion(3, ColorDataSize),
                size         = (ushort)ColorDataSize,
                cmd          = NV_COLOR_CMD_SET,
                colorFormat  = colorFormat,
                colorimetry  = 0,
                dynamicRange = dynamicRange,
                bpc          = bpc,
            };
            int ret = _colorControl(displayId, ref data);
            if (ret != 0)
            {
                CrashReporter.Log($"[NvColorService.SetColorData] NvAPI_Disp_ColorControl(SET) returned {ret} for displayId={displayId}");
                return false;
            }
            CrashReporter.Log($"[NvColorService.SetColorData] displayId={displayId} bpc={bpc} dynamicRange={dynamicRange} → OK");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NvColorService.SetColorData] Exception — {ex.Message}");
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static uint MakeVersion(uint ver, int size)
        => (uint)size | (ver << 16);

    private static readonly int ColorDataSize = Marshal.SizeOf<NV_COLOR_DATA_V3>(); // should be 16

    /// <summary>Converts BPC byte value to display string.</summary>
    public static string BpcToLabel(byte bpc) => bpc switch
    {
        1 => "6 bpc",
        2 => "8 bpc",
        3 => "10 bpc",
        4 => "12 bpc",
        5 => "16 bpc",
        _ => "Default",
    };

    /// <summary>Converts BPC label to byte value.</summary>
    public static byte LabelToBpc(string label) => label switch
    {
        "6 bpc"  => 1,
        "8 bpc"  => 2,
        "10 bpc" => 3,
        "12 bpc" => 4,
        "16 bpc" => 5,
        _        => 3, // default 10 bpc
    };

    /// <summary>Converts dynamic range byte to display string.</summary>
    public static string DynamicRangeToLabel(byte dr) => dr switch
    {
        DYNAMIC_RANGE_VESA => "Full",
        DYNAMIC_RANGE_CEA  => "Limited",
        _                  => "Full",
    };

    /// <summary>Converts dynamic range label to byte value.</summary>
    public static byte LabelToDynamicRange(string label) =>
        label == "Limited" ? DYNAMIC_RANGE_CEA : DYNAMIC_RANGE_VESA;
}
