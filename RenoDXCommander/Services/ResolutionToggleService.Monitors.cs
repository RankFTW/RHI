// ResolutionToggleService.Monitors.cs -- resolution and refresh rate of one monitor by its GDI name (\\.\DISPLAYn).
// Used by Settings › Display › Monitors (which offers keep / revert after a change).

using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

public static partial class ResolutionToggleService
{
    private const uint DM_INTERLACED = 0x2;

    /// <summary>The modes <paramref name="device"/> supports (no interlaced ones), widest first, then highest refresh rate.</summary>
    public static List<DisplayResolution> GetSupportedResolutions(string device)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<DisplayResolution>();
        try
        {
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            int i = 0;
            while (EnumDisplaySettings(device, i++, ref mode))
            {
                if ((mode.dmDisplayFlags & DM_INTERLACED) != 0 || mode.dmDisplayFrequency <= 1) continue;
                var r = new DisplayResolution(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency);
                if (seen.Add(r.Key)) list.Add(r);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            CrashReporter.Log($"[ResolutionToggleService.GetSupportedResolutions] '{device}': {ex.Message}");
        }
        return list.OrderByDescending(r => r.Width).ThenByDescending(r => r.Height).ThenByDescending(r => r.RefreshRate).ToList();
    }

    /// <summary>The current mode of <paramref name="device"/> and whether it's the main display (desktop origin 0,0).</summary>
    public static (DisplayResolution? Current, bool Primary) GetCurrentMode(string device)
    {
        try
        {
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(device, ENUM_CURRENT_SETTINGS, ref mode)) return (null, false);
            return (new DisplayResolution(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency),
                mode.dmPositionX == 0 && mode.dmPositionY == 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            CrashReporter.Log($"[ResolutionToggleService.GetCurrentMode] '{device}': {ex.Message}");
            return (null, false);
        }
    }

    /// <summary>Sets <paramref name="device"/> to <paramref name="res"/> (saved, like Windows' own setting). 0 = success, else Windows' DISP_CHANGE code.</summary>
    public static int SetResolution(string device, DisplayResolution res)
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
            int rc = ChangeDisplaySettingsEx(device, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            CrashReporter.Log($"[ResolutionToggleService.SetResolution] '{device}' {res.Label} → rc={rc}");
            return rc;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            CrashReporter.Log($"[ResolutionToggleService.SetResolution] '{device}': {ex.Message}");
            return -1;
        }
    }
}
