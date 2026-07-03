using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

/// <summary>
/// Toggles Windows HDR (Advanced Color) on the primary display using the CCD API.
/// No admin required. Uses DisplayConfigGetDeviceInfo / DisplayConfigSetDeviceInfo.
/// </summary>
public static class HdrToggleService
{
    // ── Win32 P/Invoke ────────────────────────────────────────────────────────

    private const int QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    private const int DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10;

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // bit 0 = advancedColorSupported, bit 1 = advancedColorEnabled, bit 2 = wideColorEnforced, bit 3 = advancedColorForceDisabled
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // bit 0 = enableAdvancedColor
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE setPacket);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Returns true if HDR (Advanced Color) is currently enabled on the primary display.</summary>
    public static bool IsHdrEnabled()
    {
        try
        {
            var (adapterId, targetId) = GetPrimaryTarget();
            if (targetId == 0 && adapterId.LowPart == 0) return false;

            var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
            info.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
            info.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>();
            info.header.adapterId = adapterId;
            info.header.id = targetId;

            int result = DisplayConfigGetDeviceInfo(ref info);
            CrashReporter.Log($"[HdrToggleService.IsHdrEnabled] result={result}, value=0x{info.value:X8}, adapterId=({adapterId.LowPart},{adapterId.HighPart}), targetId={targetId}");
            if (result != 0) return false;

            // bit 0 = advancedColorSupported, bit 1 = advancedColorEnabled
            bool enabled = (info.value & 0x2) != 0;
            return enabled;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[HdrToggleService.IsHdrEnabled] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Enables HDR (Advanced Color) on the primary display.</summary>
    public static bool EnableHdr()
    {
        try
        {
            var (adapterId, targetId) = GetPrimaryTarget();
            if (targetId == 0 && adapterId.LowPart == 0) return false;

            var setState = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
            setState.header.type = DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
            setState.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>();
            setState.header.adapterId = adapterId;
            setState.header.id = targetId;
            setState.value = 1; // enableAdvancedColor = 1

            int result = DisplayConfigSetDeviceInfo(ref setState);
            if (result == 0)
            {
                CrashReporter.Log("[HdrToggleService.EnableHdr] HDR enabled successfully");
                return true;
            }
            CrashReporter.Log($"[HdrToggleService.EnableHdr] Failed with error code {result}");
            return false;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[HdrToggleService.EnableHdr] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Disables HDR (Advanced Color) on the primary display.</summary>
    public static bool DisableHdr()
    {
        try
        {
            var (adapterId, targetId) = GetPrimaryTarget();
            if (targetId == 0 && adapterId.LowPart == 0) return false;

            var setState = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
            setState.header.type = DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
            setState.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>();
            setState.header.adapterId = adapterId;
            setState.header.id = targetId;
            setState.value = 0; // enableAdvancedColor = 0

            int result = DisplayConfigSetDeviceInfo(ref setState);
            if (result == 0)
            {
                CrashReporter.Log("[HdrToggleService.DisableHdr] HDR disabled successfully");
                return true;
            }
            CrashReporter.Log($"[HdrToggleService.DisableHdr] Failed with error code {result}");
            return false;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[HdrToggleService.DisableHdr] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (LUID adapterId, uint targetId) GetPrimaryTarget()
    {
        int err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
        if (err != 0) return (default, 0);

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (err != 0) return (default, 0);

        // Return the first active path (primary display)
        if (pathCount > 0)
            return (paths[0].targetInfo.adapterId, paths[0].targetInfo.id);

        return (default, 0);
    }
}
