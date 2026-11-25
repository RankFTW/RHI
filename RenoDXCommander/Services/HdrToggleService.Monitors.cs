// HdrToggleService.Monitors.cs -- every active monitor with its GDI name (\\.\DISPLAYn), friendly name, Windows HDR
// state and exact refresh rate, and Windows HDR on/off for one monitor. Used by Settings › Display › Monitors.

using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

public static partial class HdrToggleService
{
    /// <summary>An active monitor. <see cref="GdiName"/> is its source (\\.\DISPLAYn), used for resolution changes.</summary>
    public record MonitorTarget(string GdiName, string Name, uint AdapterLow, int AdapterHigh, uint TargetId,
        bool HdrSupported, bool HdrOn, double RefreshHz);

    /// <summary>Every active monitor, once each. Empty on failure.</summary>
    public static List<MonitorTarget> GetMonitors()
    {
        var result = new List<MonitorTarget>();
        try
        {
            int err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
            if (err != 0) return result;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (err != 0) return result;

            var seen = new HashSet<(uint, int, uint)>();
            for (int i = 0; i < pathCount; i++)
            {
                var t = paths[i].targetInfo;
                if (!seen.Add((t.adapterId.LowPart, t.adapterId.HighPart, t.id))) continue;

                var src = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                src.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                src.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                src.header.adapterId = paths[i].sourceInfo.adapterId;
                src.header.id = paths[i].sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;
                var gdi = src.GdiDeviceName?.TrimEnd('\0').Trim() ?? "";
                if (gdi.Length == 0) continue;

                var nameInfo = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                nameInfo.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                nameInfo.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                nameInfo.header.adapterId = t.adapterId;
                nameInfo.header.id = t.id;
                string name = $"Display {result.Count + 1}";
                if (DisplayConfigGetTargetDeviceName(ref nameInfo) == 0)
                {
                    var n = nameInfo.monitorFriendlyDeviceName?.TrimEnd('\0').Trim() ?? "";
                    if (n.Length > 0) name = n;
                }

                var (supported, on) = ReadHdrState(t.adapterId, t.id);

                double hz = t.refreshRate.Denominator == 0 ? 0 : (double)t.refreshRate.Numerator / t.refreshRate.Denominator;
                result.Add(new MonitorTarget(gdi, name, t.adapterId.LowPart, t.adapterId.HighPart, t.id, supported, on, hz));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            CrashReporter.Log($"[HdrToggleService.GetMonitors] Failed — {ex.Message}");
        }
        return result;
    }

    // Windows 11 24H2+: "advanced colour" (the old info's enabled bit) is also on with Auto Colour Management in SDR, so
    // HDR has its own info / set packets there. Older Windows returns an error for them and the old packets are used.
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 15;
    private const uint DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE = 16;
    private const uint ADVANCED_COLOR_MODE_HDR = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // bit 4 = highDynamicRangeSupported, bit 5 = highDynamicRangeUserEnabled
        public uint colorEncoding;
        public uint bitsPerColorChannel;
        public uint activeColorMode; // 0 SDR, 1 WCG, 2 HDR
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SET_HDR_STATE
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value; // bit 0 = enableHdr
    }

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_HDR_STATE setPacket);

    /// <summary>Whether the monitor can do HDR and whether Windows HDR is on (not just advanced colour / ACM).</summary>
    private static (bool Supported, bool On) ReadHdrState(LUID adapterId, uint targetId)
    {
        var info2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2();
        info2.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2;
        info2.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>();
        info2.header.adapterId = adapterId;
        info2.header.id = targetId;
        if (DisplayConfigGetDeviceInfo(ref info2) == 0)
            return ((info2.value & 0x10) != 0, info2.activeColorMode == ADVANCED_COLOR_MODE_HDR);

        var color = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        color.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
        color.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>();
        color.header.adapterId = adapterId;
        color.header.id = targetId;
        if (DisplayConfigGetDeviceInfo(ref color) != 0) return (false, false);
        return ((color.value & 0x1) != 0, (color.value & 0x2) != 0);
    }

    /// <summary>
    /// Turns Windows HDR on or off for one display target: the HDR state packet (Windows 11 24H2+), else the advanced
    /// colour state (older Windows, where advanced colour is the HDR switch). 0 = done, else the Windows error.
    /// </summary>
    private static int SetHdrState(LUID adapterId, uint targetId, bool on)
    {
        var hdr = new DISPLAYCONFIG_SET_HDR_STATE();
        hdr.header.type = DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE;
        hdr.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_HDR_STATE>();
        hdr.header.adapterId = adapterId;
        hdr.header.id = targetId;
        hdr.value = on ? 1u : 0u;
        int rc = DisplayConfigSetDeviceInfo(ref hdr);
        if (rc == 0) return 0;

        var set = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
        set.header.type = DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
        set.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>();
        set.header.adapterId = adapterId;
        set.header.id = targetId;
        set.value = on ? 1u : 0u;
        int rc2 = DisplayConfigSetDeviceInfo(ref set);
        CrashReporter.Log($"[HdrToggleService.SetHdrState] HDR state packet rc={rc}; advanced colour state rc={rc2}");
        return rc2;
    }

    /// <summary>Turns Windows HDR on or off for one monitor. True when Windows accepted it.</summary>
    public static bool SetHdr(MonitorTarget monitor, bool on)
    {
        try
        {
            int rc = SetHdrState(new LUID { LowPart = monitor.AdapterLow, HighPart = monitor.AdapterHigh }, monitor.TargetId, on);
            CrashReporter.Log($"[HdrToggleService.SetHdr] '{monitor.Name}' ({monitor.GdiName}) HDR {(on ? "on" : "off")} → rc={rc}");
            return rc == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            CrashReporter.Log($"[HdrToggleService.SetHdr] Failed — {ex.Message}");
            return false;
        }
    }
}
