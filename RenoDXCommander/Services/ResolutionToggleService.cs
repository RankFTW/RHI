// ResolutionToggleService.cs — Changes desktop resolution on game launch and restores it on exit.
// Uses Win32 ChangeDisplaySettingsEx via P/Invoke. No admin required for the primary display.

using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

/// <summary>
/// Enumerates supported display resolutions and changes/restores the desktop resolution.
/// Used by the Resolution Auto-Toggle feature (dev-only, gated by DevUnlockService).
/// </summary>
public static class ResolutionToggleService
{
    // ── Win32 P/Invoke ────────────────────────────────────────────────────────

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int CDS_UPDATEREGISTRY = 0x00000001;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    // ── Public types ──────────────────────────────────────────────────────────

    /// <summary>A supported display resolution with refresh rate.</summary>
    public record DisplayResolution(uint Width, uint Height, uint RefreshRate)
    {
        public string Label => RefreshRate > 0
            ? $"{Width}x{Height} @ {RefreshRate}Hz"
            : $"{Width}x{Height}";

        /// <summary>Serialization key: "WxH@Hz" or "WxH".</summary>
        public string Key => RefreshRate > 0 ? $"{Width}x{Height}@{RefreshRate}" : $"{Width}x{Height}";

        public static DisplayResolution? Parse(string? key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try
            {
                var parts = key.Split('@');
                var wh = parts[0].Split('x');
                uint w = uint.Parse(wh[0]);
                uint h = uint.Parse(wh[1]);
                uint hz = parts.Length > 1 ? uint.Parse(parts[1]) : 0;
                return new DisplayResolution(w, h, hz);
            }
            catch { return null; }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all unique resolutions supported by the primary display,
    /// sorted by width descending then height descending then refresh rate descending.
    /// </summary>
    public static List<DisplayResolution> GetSupportedResolutions()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<DisplayResolution>();
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        int i = 0;
        while (EnumDisplaySettings(null, i++, ref mode))
        {
            var r = new DisplayResolution(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency);
            if (seen.Add(r.Key))
                list.Add(r);
        }
        return list
            .OrderByDescending(r => r.Width)
            .ThenByDescending(r => r.Height)
            .ThenByDescending(r => r.RefreshRate)
            .ToList();
    }

    /// <summary>
    /// Gets the current desktop resolution of the primary display.
    /// </summary>
    public static DisplayResolution? GetCurrentResolution()
    {
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode)) return null;
        return new DisplayResolution(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency);
    }

    /// <summary>
    /// Changes the primary display to the specified resolution.
    /// Returns true on success.
    /// </summary>
    public static bool SetResolution(DisplayResolution res)
    {
        try
        {
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            mode.dmPelsWidth = res.Width;
            mode.dmPelsHeight = res.Height;
            mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
            if (res.RefreshRate > 0)
            {
                mode.dmDisplayFrequency = res.RefreshRate;
                mode.dmFields |= (uint)DM_DISPLAYFREQUENCY;
            }

            int result = ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            CrashReporter.Log($"[ResolutionToggleService.SetResolution] {res.Label} → result={result}");
            return result == DISP_CHANGE_SUCCESSFUL;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ResolutionToggleService.SetResolution] Exception — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restores the desktop to its default/original resolution by passing a null DEVMODE.
    /// </summary>
    public static bool RestoreResolution()
    {
        try
        {
            int result = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            CrashReporter.Log($"[ResolutionToggleService.RestoreResolution] result={result}");
            return result == DISP_CHANGE_SUCCESSFUL;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ResolutionToggleService.RestoreResolution] Exception — {ex.Message}");
            return false;
        }
    }
}
