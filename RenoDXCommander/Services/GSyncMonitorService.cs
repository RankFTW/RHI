// GSyncMonitorService.cs — lists the connected monitors for the per-monitor G-SYNC ⚙ (Settings › Driver Settings),
// reads their live G-SYNC state and restarts the GPU so a change takes effect.
// The setting itself is a driver profile per monitor: DlssPresetService.MonitorGSync.cs.
//
// Live state: NvAPI_DISP_GetAdaptiveSyncData, bit 0 of the flags = adaptive sync disabled (no admin needed).
// Dead ends (Nov 2026), so nobody tries them again: NvAPI_DISP_SetAdaptiveSyncData's disable flag doesn't persist
// (the driver reverts it within seconds); writing nvlddmkm\State\DisplayDatabase\<monitor>\MonitorDataRegistryKey
// (SYSTEM only) is overwritten by the driver from the profile when it starts; a refresh-rate change doesn't make
// the driver re-read the profile. Only a GPU restart (or a Windows restart) does.

using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace RenoDXCommander.Services;

/// <summary>A connected NVIDIA display and its G-SYNC state.</summary>
/// <param name="GdiName">Windows display name, e.g. \\.\DISPLAY7.</param>
/// <param name="FriendlyName">Monitor name, e.g. "MSI MPG343CQR".</param>
/// <param name="ProfileName">Driver profile name for the monitor ("0x6936:0x3DB2"), or null when the EDID couldn't be read.</param>
/// <param name="VrrCapable">The driver reports adaptive sync data for this display.</param>
/// <param name="LiveAllowed">G-SYNC allowed on the display right now.</param>
public record GSyncMonitorInfo(string GdiName, string FriendlyName, string? ProfileName, bool VrrCapable, bool LiveAllowed);

public static class GSyncMonitorService
{
    /// <summary>Shown wherever RHI restarts the graphics driver (G-SYNC ⚙, DLDSR Apply, DLDSR smoothness).</summary>
    public const string OverclockResetNote =
        "Restarting the graphics driver resets GPU overclocks and undervolts (MSI Afterburner, the NVIDIA app's "
        + "Performance tuning and similar tools), so reapply them afterwards.";

    // ── NVAPI (live state) ────────────────────────────────────────────────────

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvQueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvInitializeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvEnumDisplayFn(int index, ref IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvGetDisplayNameFn(IntPtr handle, StringBuilder name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvGetDisplayIdByNameFn([MarshalAs(UnmanagedType.LPStr)] string name, out uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NvGetAdaptiveSyncFn(uint displayId, byte[] data);

    private static T? NvFn<T>(uint id) where T : Delegate
    {
        var p = NvQueryInterface(id);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    /// <summary>Live adaptive sync state per GDI display name (true = allowed). Displays without VRR are absent.</summary>
    private static Dictionary<string, bool> ReadLiveAdaptiveSync()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var init = NvFn<NvInitializeFn>(0x0150E828);
            var enumDisp = NvFn<NvEnumDisplayFn>(0x9ABDD40D);          // NvAPI_EnumNvidiaDisplayHandle
            var getName = NvFn<NvGetDisplayNameFn>(0x22A78B05);        // NvAPI_GetAssociatedNvidiaDisplayName
            var getId = NvFn<NvGetDisplayIdByNameFn>(0xAE457190);      // NvAPI_DISP_GetDisplayIdByDisplayName
            var getSync = NvFn<NvGetAdaptiveSyncFn>(0xB73D1EE9);       // NvAPI_DISP_GetAdaptiveSyncData
            if (init == null || enumDisp == null || getName == null || getId == null || getSync == null)
            {
                CrashReporter.Log("[GSyncMonitorService.ReadLiveAdaptiveSync] NVAPI display functions not available");
                return result;
            }
            if (init() != 0) return result;

            for (int i = 0; i < 16; i++)
            {
                IntPtr handle = IntPtr.Zero;
                if (enumDisp(i, ref handle) != 0) break;
                var name = new StringBuilder(64);
                if (getName(handle, name) != 0) continue;
                if (getId(name.ToString(), out var displayId) != 0) continue;

                // NV_GET_ADAPTIVE_SYNC_DATA_V1 (40 bytes): version | maxFrameInterval | flags (bit 0 = disabled) | ...
                var buf = new byte[40];
                BitConverter.GetBytes((uint)(40 | (1 << 16))).CopyTo(buf, 0);
                int r = getSync(displayId, buf);
                if (r != 0)
                {
                    CrashReporter.Log($"[GSyncMonitorService.ReadLiveAdaptiveSync] {name}: GetAdaptiveSyncData returned {r}");
                    continue;
                }
                result[name.ToString()] = (BitConverter.ToUInt32(buf, 8) & 1) == 0;
            }
        }
        catch (DllNotFoundException) { /* no NVIDIA driver */ }
        catch (Exception ex) when (ex is EntryPointNotFoundException or MarshalDirectiveException or SEHException)
        {
            CrashReporter.Log($"[GSyncMonitorService.ReadLiveAdaptiveSync] {ex.Message}");
        }
        return result;
    }

    // ── EDID → profile name ───────────────────────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x1;

    /// <summary>
    /// Driver profile name for a monitor: "0x" + EDID bytes 8-9 read little-endian (the PnP manufacturer id,
    /// so MSI = 0x6936) + ":0x" + the product code (bytes 10-11, little-endian). Matches NVIDIA's profiles.
    /// </summary>
    internal static string? ProfileNameFromEdid(byte[] edid)
    {
        if (edid.Length < 12) return null;
        ushort mfg = BitConverter.ToUInt16(edid, 8);
        ushort product = BitConverter.ToUInt16(edid, 10);
        return $"0x{mfg:X4}:0x{product:X4}";
    }

    /// <summary>The EDID of the monitor on a GDI display (\\.\DISPLAYn), from its device's registry key.</summary>
    private static byte[]? ReadEdid(string gdiName)
    {
        try
        {
            var mon = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(gdiName, 0, ref mon, EDD_GET_DEVICE_INTERFACE_NAME)) return null;
            // \\?\DISPLAY#MSI3DB2#5&bdeebb&1&UID4357#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}
            var parts = (mon.DeviceID ?? "").Split('#');
            if (parts.Length < 3) return null;
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
            return key?.GetValue("EDID") as byte[];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            CrashReporter.Log($"[GSyncMonitorService.ReadEdid] {gdiName}: {ex.Message}");
            return null;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Active displays with their live G-SYNC state. Blocking (NVAPI + registry): call off the UI thread.</summary>
    public static List<GSyncMonitorInfo> GetMonitors()
    {
        var list = new List<GSyncMonitorInfo>();
        var live = ReadLiveAdaptiveSync();
        var friendly = HdrToggleService.GetGdiNameMap();
        var gdiNames = friendly.Keys.Union(live.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var gdi in gdiNames)
        {
            var edid = ReadEdid(gdi);
            var profile = edid != null ? ProfileNameFromEdid(edid) : null;
            var name = friendly.TryGetValue(gdi, out var f) && !string.IsNullOrWhiteSpace(f) ? f : gdi;
            bool capable = live.TryGetValue(gdi, out var allowed);
            list.Add(new GSyncMonitorInfo(gdi, name, profile, capable, capable && allowed));
            CrashReporter.Log($"[GSyncMonitorService.GetMonitors] {gdi} '{name}' profile={profile ?? "?"} vrr={capable} live={allowed}");
        }
        return list;
    }

    /// <summary>
    /// Restarts the NVIDIA GPU (pnputil /restart-device) so the driver reads the monitor profiles again.
    /// Needs admin rights. Screens go black for a few seconds.
    /// </summary>
    public static async Task<bool> RestartGpuAsync()
    {
        string? deviceId = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT PNPDeviceID FROM Win32_VideoController WHERE PNPDeviceID LIKE 'PCI\\\\VEN_10DE%'");
            foreach (ManagementObject obj in searcher.Get()) { deviceId = obj["PNPDeviceID"]?.ToString(); break; }
        }
        catch (Exception ex) when (ex is ManagementException or COMException)
        {
            CrashReporter.Log($"[GSyncMonitorService.RestartGpuAsync] WMI: {ex.Message}");
        }
        if (string.IsNullOrEmpty(deviceId))
        {
            CrashReporter.Log("[GSyncMonitorService.RestartGpuAsync] No NVIDIA GPU found");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo("pnputil", $"/restart-device \"{deviceId}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            var output = await p.StandardOutput.ReadToEndAsync() + await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            CrashReporter.Log($"[GSyncMonitorService.RestartGpuAsync] pnputil exit {p.ExitCode}: {output.Trim()}");
            return p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CrashReporter.Log($"[GSyncMonitorService.RestartGpuAsync] {ex.Message}");
            return false;
        }
    }
}
