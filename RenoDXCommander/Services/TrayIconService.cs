using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the system tray icon via Win32 Shell_NotifyIcon.
/// Provides close-to-tray, right-click context menu with recent games, and jump list.
/// </summary>
public static class TrayIconService
{
    private const string AppId = "RHI.ReShadeHDRInstaller";

    /// <summary>
    /// Sets the App User Model ID for the current process.
    /// Must be called early at startup so Windows associates the jump list with this exe.
    /// </summary>
    public static void SetProcessAppId()
    {
        SetCurrentProcessExplicitAppUserModelID(AppId);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);
    private static IntPtr _hwnd;
    private static bool _iconCreated;
    private static List<string> _recentGames = new();
    private static Action? _onShowWindow;
    private static Action? _onExit;
    private static Action<string>? _onLaunchGame;

    public const int WM_TRAYICON = 0x8000; // WM_APP

    public static void Initialize(IntPtr hwnd, Action onShowWindow, Action onExit, Action<string> onLaunchGame)
    {
        _hwnd = hwnd;
        _onShowWindow = onShowWindow;
        _onExit = onExit;
        _onLaunchGame = onLaunchGame;
        CreateIcon();
    }

    public static void UpdateRecentGames(List<string> games)
    {
        _recentGames = games.Take(5).ToList();
    }

    public static void Dispose()
    {
        if (_iconCreated)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _iconCreated = false;
        }
    }

    public static void HandleTrayMessage(IntPtr lParam)
    {
        var msg = (int)(lParam & 0xFFFF);
        switch (msg)
        {
            case 0x0203: // WM_LBUTTONDBLCLK
                _onShowWindow?.Invoke();
                break;
            case 0x0205: // WM_RBUTTONUP
                ShowContextMenu();
                break;
        }
    }

    private static void CreateIcon()
    {
        var iconPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "icon.ico");
        var hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        if (hIcon == IntPtr.Zero)
            hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION fallback

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = hIcon,
            szTip = "RHI",
        };
        Shell_NotifyIcon(NIM_ADD, ref nid);
        _iconCreated = true;
    }

    private static void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();
        int id = 100;

        if (_recentGames.Count > 0)
        {
            foreach (var game in _recentGames)
            {
                AppendMenu(hMenu, MF_STRING, (uint)id, game);
                id++;
            }
            AppendMenu(hMenu, MF_SEPARATOR, 0, null);
        }

        AppendMenu(hMenu, MF_STRING, 1, "Open RHI");
        AppendMenu(hMenu, MF_STRING, 2, "Exit");

        // Required for the menu to close when clicking away
        SetForegroundWindow(_hwnd);

        GetCursorPos(out var pt);
        var cmd = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_NONOTIFY, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);

        if (cmd == 1) _onShowWindow?.Invoke();
        else if (cmd == 2) _onExit?.Invoke();
        else if (cmd >= 100 && cmd < 100 + _recentGames.Count)
            _onLaunchGame?.Invoke(_recentGames[cmd - 100]);

        DestroyMenu(hMenu);
    }

    // ── Jump List (taskbar right-click) ─────────────────────────────────────────

    public static void UpdateJumpList(List<string> recentGames)
    {
        try
        {
            var jumpList = (ICustomDestinationList)new CoClass_DestinationList();
            jumpList.SetAppID(AppId);

            jumpList.BeginList(out _, out var removedItems);
            Marshal.ReleaseComObject(removedItems);

            var collection = (IObjectCollection)new CoClass_EnumerableObjectCollection();
            var exePath = Environment.ProcessPath!;

            foreach (var game in recentGames.Take(5))
            {
                var link = (IShellLinkW)new CoClass_ShellLink();
                link.SetPath(exePath);
                link.SetArguments($"--launch \"{game}\"");
                link.SetDescription(game);

                var store = (IPropertyStore)link;
                var titleKey = new PROPERTYKEY { fmtid = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), pid = 2 };
                var pv = new PROPVARIANT { vt = 31, pwszVal = Marshal.StringToCoTaskMemUni(game) }; // VT_LPWSTR
                store.SetValue(ref titleKey, ref pv);
                store.Commit();
                Marshal.FreeCoTaskMem(pv.pwszVal);

                collection.AddObject(link);
            }

            if (recentGames.Count > 0)
                jumpList.AppendCategory("Recent Games", (IObjectArray)collection);

            jumpList.CommitList();
            Marshal.ReleaseComObject(collection);
            Marshal.ReleaseComObject(jumpList);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[TrayIconService.UpdateJumpList] Failed — {ex.Message}");
        }
    }

    public static void ClearJumpList()
    {
        try
        {
            var jumpList = (ICustomDestinationList)new CoClass_DestinationList();
            jumpList.SetAppID(AppId);
            jumpList.DeleteList(AppId);
            Marshal.ReleaseComObject(jumpList);
        }
        catch { }
    }

    // ── P/Invoke ────────────────────────────────────────────────────────────────

    private const int NIM_ADD = 0;
    private const int NIM_DELETE = 2;
    private const int NIF_MESSAGE = 1;
    private const int NIF_ICON = 2;
    private const int NIF_TIP = 4;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x0010;
    private const int LR_DEFAULTSIZE = 0x0040;
    private const uint MF_STRING = 0;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA pnid);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, int type, int cx, int cy, int fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    // ── COM interfaces for Jump List ────────────────────────────────────────────

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CoClass_DestinationList { }

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CoClass_EnumerableObjectCollection { }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CoClass_ShellLink { }

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint pcMinSlots, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, [MarshalAs(UnmanagedType.Interface)] object poa);
        void AppendKnownCategory(int category);
        void AddUserTasks([MarshalAs(UnmanagedType.Interface)] object poa);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, out object ppv);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        // IObjectArray
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        // IObjectCollection
        void AddObject([MarshalAs(UnmanagedType.Interface)] object punk);
        void AddFromArray([MarshalAs(UnmanagedType.Interface)] object poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [ComImport, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr pwszVal;
    }
}
