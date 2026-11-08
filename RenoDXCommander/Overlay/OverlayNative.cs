// OverlayNative.cs -- Win32 calls used by the RHI overlay (hotkey, foreground window, monitor, fullscreen state).
using System.Runtime.InteropServices;

namespace RenoDXCommander.Overlay;

internal static class OverlayNative
{
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;        // 0 = battery, 1 = plugged in, 255 = unknown
        public byte BatteryFlag;         // 128 = no battery, 255 = unknown
        public byte BatteryLifePercent;  // 0-100, 255 = unknown
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    /// <summary>Battery percent and whether it's charging, or null on a PC without a battery.</summary>
    internal static (int Percent, bool Charging)? Battery()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        if (s.BatteryFlag == 128 || s.BatteryFlag == 255 || s.BatteryLifePercent > 100) return null;
        return (s.BatteryLifePercent, s.ACLineStatus == 1);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    /// <summary>Moves a file to the Recycle Bin without any Windows dialogs. True when it's gone.</summary>
    internal static bool RecycleFile(string path)
    {
        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI,
        };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted && !File.Exists(path);
    }

    /// <summary>QUERY_USER_NOTIFICATION_STATE: 3 = a Direct3D app is running in exclusive fullscreen.</summary>
    [DllImport("shell32.dll")]
    internal static extern int SHQueryUserNotificationState(out int state);

    internal static int QueryNotificationState()
    {
        try { return SHQueryUserNotificationState(out var s) == 0 ? s : -1; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return -1; }
    }
}
