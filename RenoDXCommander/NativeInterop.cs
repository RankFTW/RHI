using System.Runtime.InteropServices;

namespace RenoDXCommander;

/// <summary>
/// Contains all COM interop definitions, P/Invoke declarations, and native structs
/// used by the application. Centralizes native method imports from MainWindow code-behind.
/// </summary>
internal static class NativeInterop
{
    // ── COM interop for IFileOpenDialog ──────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    internal static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

    internal static Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [Flags]
    internal enum FOS : uint
    {
        FOS_PICKFOLDERS     = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
    }

    internal enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000,
    }

    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr hwnd);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    internal class FileOpenDialogClass { }

    // ── Window persistence helpers (user32.dll) ─────────────────────────────────

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINTL pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public System.Drawing.Point ptMinPosition;
        public System.Drawing.Point ptMaxPosition;
        public RECT rcNormalPosition;
    }

    internal const int SW_MAXIMIZE = 3;
    internal const int SW_RESTORE = 9;

    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    // ── DWM dark mode for title bar (fixes white title bar in taskbar thumbnail) ─

    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>
    /// Tells DWM to render the non-client area (title bar, caption buttons) in dark mode.
    /// Fixes the taskbar thumbnail showing a white title bar for WinUI 3 apps with custom colors.
    /// </summary>
    internal static void EnableDarkTitleBar(IntPtr hwnd)
    {
        int value = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    // ── Minimum window size enforcement via WndProc subclass ────────────────────

    internal const int GWLP_WNDPROC = -4;
    internal const int WM_GETMINMAXINFO = 0x0024;
    internal const int WM_EXITSIZEMOVE  = 0x0232;  // fires once when resize/move drag ends
    internal const int MinWindowWidth = 1220;
    internal const int MinWindowHeight = 800;

    internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public System.Drawing.Point ptReserved;
        public System.Drawing.Point ptMaxSize;
        public System.Drawing.Point ptMaxPosition;
        public System.Drawing.Point ptMinTrackSize;
        public System.Drawing.Point ptMaxTrackSize;
    }

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hWnd);

    // ── Drag-and-drop via Win32 (WM_DROPFILES) for unpackaged apps ──────────────

    internal const int WM_DROPFILES = 0x0233;

    [DllImport("shell32.dll")]
    internal static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint DragQueryFile(IntPtr hDrop, uint iFile, char[]? lpszFile, uint cch);

    [DllImport("shell32.dll")]
    internal static extern void DragFinish(IntPtr hDrop);

    /// <summary>
    /// Allows messages from lower-privilege processes to reach an elevated window.
    /// Required so that drag-and-drop from Explorer works when the app is run as admin.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ChangeWindowMessageFilterEx(
        IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);

    internal const uint MSGFLT_ALLOW = 1;
    internal const uint WM_COPYGLOBALDATA = 0x0049;

    // ── OLE drag-and-drop registration ─────────────────────────────────────────

    [DllImport("ole32.dll")]
    internal static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    internal static extern void OleUninitialize();

    [DllImport("ole32.dll")]
    internal static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget pDropTarget);

    [DllImport("ole32.dll")]
    internal static extern int RevokeDragDrop(IntPtr hwnd);

    // ── OLE COM interfaces ─────────────────────────────────────────────────────

    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDropTarget
    {
        [PreserveSig] int DragEnter(IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
        [PreserveSig] int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop(IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
    }

    [ComImport, Guid("0000010e-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDataObject
    {
        [PreserveSig] int GetData(ref FORMATETC format, out STGMEDIUM medium);
        [PreserveSig] int GetDataHere(ref FORMATETC format, ref STGMEDIUM medium);
        [PreserveSig] int QueryGetData(ref FORMATETC format);
        [PreserveSig] int SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, [MarshalAs(UnmanagedType.Bool)] bool fRelease);
    }

    // ── OLE structs and constants ───────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FORMATETC
    {
        public ushort cfFormat;
        public IntPtr ptd;
        public uint dwAspect;
        public int lindex;
        public uint tymed;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STGMEDIUM
    {
        public uint tymed;
        public IntPtr unionmember;
        public IntPtr pUnkForRelease;
    }

    internal const ushort CF_TEXT = 1;
    internal const ushort CF_UNICODETEXT = 13;
    internal const ushort CF_HDROP = 15;

    internal const uint DVASPECT_CONTENT = 1;
    internal const uint TYMED_HGLOBAL = 1;

    internal const uint DROPEFFECT_NONE = 0;
    internal const uint DROPEFFECT_COPY = 1;

    // ── Kernel32 helpers for reading STGMEDIUM data ─────────────────────────────

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    internal static extern UIntPtr GlobalSize(IntPtr hMem);

    [DllImport("ole32.dll")]
    internal static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

    // ── Window activation ───────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(uint dwProcessId);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string message);

    internal delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam,
        UIntPtr subclassId, UIntPtr referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc procedure, UIntPtr subclassId, UIntPtr referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc procedure, UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    internal static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProp(IntPtr hwnd, string name, IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr RemoveProp(IntPtr hwnd, string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FLASHWINFO
    {
        internal uint cbSize;
        internal IntPtr hwnd;
        internal uint dwFlags;
        internal uint uCount;
        internal uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlashWindowEx(ref FLASHWINFO info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNamedPipeServerProcessId(
        Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// Requests foreground activation without joining another process's input queue.
    /// Windows may refuse focus stealing; that is preferable to hanging our UI when
    /// the foreground process (including the installer) is blocked or unresponsive.
    /// </summary>
    internal static void ForceToForeground(IntPtr hwnd)
    {
        Services.ForegroundActivation.Request(hwnd);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(IntPtr hWnd);

    // ── Win32 Open File Dialog (fallback for WinRT FileOpenPicker COM failures) ──

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool GetOpenFileName(ref OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct OpenFileName
    {
        public int structSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string filter;
        public string customFilter;
        public int maxCustFilter;
        public int filterIndex;
        public string file;
        public int maxFile;
        public string fileTitle;
        public int maxFileTitle;
        public string initialDir;
        public string title;
        public int flags;
        public short fileOffset;
        public short fileExtension;
        public string defExt;
        public IntPtr custData;
        public IntPtr hook;
        public string templateName;
        public IntPtr reservedPtr;
        public int reservedInt;
        public int flagsEx;
    }

    // ── Native stack capture (StackWalk64) ──────────────────────────────────────
    // Used by the freeze heartbeat to capture a native call stack of the UI thread.
    // Safety contract:
    //   1. OpenThread → SuspendThread → GetThreadContext (copies CONTEXT blob) → ResumeThread
    //      immediately. Zero allocations between Suspend and Resume.
    //   2. StackWalk64 + all DbgHelp calls run AFTER ResumeThread, under _dbgHelpLock.
    //   3. SymInitialize called once at startup; dbghelp.dll is already loaded for MiniDumpWriteDump.

    internal const uint THREAD_GET_CONTEXT       = 0x0008;
    internal const uint THREAD_SUSPEND_RESUME    = 0x0002;
    internal const uint THREAD_QUERY_INFORMATION = 0x0040;
    internal const uint IMAGE_FILE_MACHINE_AMD64 = 0x8664;

    // ── Resource counters (GDI/USER objects, memory) ────────────────────────────

    // GetGuiResources flags
    internal const uint GR_GDIOBJECTS  = 0;
    internal const uint GR_USEROBJECTS = 1;

    [DllImport("user32.dll")]
    internal static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint    dwLength;          // must be set to sizeof(MEMORYSTATUSEX)
        public uint    dwMemoryLoad;
        public ulong   ullTotalPhys;
        public ulong   ullAvailPhys;
        public ulong   ullTotalPageFile;
        public ulong   ullAvailPageFile;
        public ulong   ullTotalVirtual;
        public ulong   ullAvailVirtual;
        public ulong   ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ── Module lookup from address ───────────────────────────────────────────────
    internal const uint GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS        = 0x00000004;
    internal const uint GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT  = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetModuleHandleExW(
        uint   dwFlags,
        IntPtr lpModuleName,   // address when FROM_ADDRESS flag is set
        out IntPtr phModule);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetModuleHandleW(
        [MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetModuleFileNameW(
        IntPtr hModule,
        System.Text.StringBuilder lpFilename,
        uint nSize);

    // ── x64 stack unwinding (no DbgHelp needed for the walk itself) ──────────────
    // RUNTIME_FUNCTION: three DWORDs — BeginAddress, EndAddress, UnwindInfoAddress (all RVAs)
    [StructLayout(LayoutKind.Sequential)]
    internal struct RUNTIME_FUNCTION
    {
        public uint BeginAddress;
        public uint EndAddress;
        public uint UnwindInfoAddress;
    }

    // RtlLookupFunctionEntry returns a pointer to RUNTIME_FUNCTION for ControlPc,
    // and outputs the ImageBase of the containing module.
    // HistoryTable (3rd param) is an optional cache — pass IntPtr.Zero.
    [DllImport("kernel32.dll")]
    internal static extern IntPtr RtlLookupFunctionEntry(
        ulong    ControlPc,
        out ulong ImageBase,
        IntPtr   HistoryTable);

    // RtlVirtualUnwind unwinds one frame. Updates ContextRecord in place.
    // HandlerData and EstablisherFrame are output only — we don't use them.
    // ContextPointers (last param) may be null.
    [DllImport("kernel32.dll")]
    internal static extern IntPtr RtlVirtualUnwind(
        uint     HandlerType,   // UNW_FLAG_NHANDLER = 0
        ulong    ImageBase,
        ulong    ControlPc,
        IntPtr   FunctionEntry, // PRUNTIME_FUNCTION from RtlLookupFunctionEntry
        IntPtr   ContextRecord, // PCONTEXT — updated in place
        out IntPtr HandlerData,
        out ulong  EstablisherFrame,
        IntPtr   ContextPointers);  // PKNONVOLATILE_CONTEXT_POINTERS — may be null

    // ── Message pump probe ───────────────────────────────────────────────────────
    internal const uint WM_NULL         = 0x0000;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;
    internal const uint SMTO_BLOCK       = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd,
        uint   Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint   fuFlags,
        uint   uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsHungAppWindow(IntPtr hwnd);

    // ── Event / wait for native-block test ──────────────────────────────────────
    internal const uint WAIT_TIMEOUT   = 0x00000102;
    internal const uint WAIT_OBJECT_0  = 0x00000000;
    internal const uint INFINITE       = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateEventW(
        IntPtr lpEventAttributes, bool bManualReset, bool bInitialState,
        [MarshalAs(UnmanagedType.LPWStr)] string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    // x64 CONTEXT block is exactly 1232 bytes (winnt.h CONTEXT for AMD64).
    // We treat it as an opaque byte blob; StackWalk64 reads/modifies it internally.
    internal const int CONTEXT_X64_SIZE = 1232;

    // CONTEXT.ContextFlags offset = 48, value 0x10007F = CONTEXT_ALL
    internal const uint CONTEXT_ALL_FLAGS = 0x0010007F;

    // STACKFRAME64 is 88 bytes. We use an explicit layout struct so the JIT
    // can stack-allocate it without heap allocation.
    [StructLayout(LayoutKind.Sequential)]
    internal struct STACKFRAME64
    {
        public ADDRESS64 AddrPC;
        public ADDRESS64 AddrReturn;
        public ADDRESS64 AddrFrame;
        public ADDRESS64 AddrStack;
        public ADDRESS64 AddrBStore;
        public IntPtr    FuncTableEntry;
        public ulong     Params0, Params1, Params2, Params3;
        public bool      Far;
        public bool      Virtual;
        public ulong     Reserved0, Reserved1, Reserved2;
        public KDHELP64  KdHelp;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ADDRESS64
    {
        public ulong  Offset;
        public ushort Segment;
        public uint   Mode; // AddrMode enum: flat=3
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KDHELP64
    {
        public ulong  Thread, ThCallbackStack, ThCallbackBStore, NextCallback, FramePointer;
        public ulong  KiCallUserMode, KeUserCallbackDispatcher, SystemRangeStart, KiUserExceptionDispatcher;
        public ulong  StackBase, StackLimit;
        public ulong  BuildVersion;
        public uint   RetpolineStubFunctionTableSize;
        public ulong  RetpolineStubFunctionTable;
        public uint   RetpolineStubOffset;
        public uint   RetpolineStubSize;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ulong[] Reserved0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll")]
    internal static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll")]
    internal static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetThreadContext(IntPtr hThread, IntPtr lpContext);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        IntPtr lpBuffer,
        UIntPtr nSize,
        out UIntPtr lpNumberOfBytesRead);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr  BaseAddress;
        public IntPtr  AllocationBase;
        public uint    AllocationProtect;
        public ushort  PartitionId;
        public UIntPtr RegionSize;
        public uint    State;   // MEM_COMMIT=0x1000, MEM_RESERVE=0x2000, MEM_FREE=0x10000
        public uint    Protect;
        public uint    Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern UIntPtr VirtualQuery(
        IntPtr lpAddress,
        out MEMORY_BASIC_INFORMATION lpBuffer,
        UIntPtr dwLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    // SYMBOL_INFO for SymFromAddr — name buffer appended inline after the struct.
    // We allocate a fixed buffer of MAX_SYM_NAME + sizeof(SYMBOL_INFO) bytes.
    internal const int MAX_SYM_NAME = 256;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SYMBOL_INFO
    {
        public uint   SizeOfStruct;  // must be sizeof(SYMBOL_INFO) = 88
        public uint   TypeIndex;
        public ulong  Reserved1, Reserved2;
        public uint   Index;
        public uint   Size;
        public ulong  ModBase;
        public uint   Flags;
        public ulong  Value;
        public ulong  Address;
        public uint   Register;
        public uint   Scope;
        public uint   Tag;
        public uint   NameLen;
        public uint   MaxNameLen;
        // Name[1] is appended here — we handle it by reading from the pinned buffer directly
        public unsafe fixed char Name[MAX_SYM_NAME + 1];
    }

    [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern unsafe bool SymFromAddr(
        IntPtr       hProcess,
        ulong        Address,
        out ulong    Displacement,
        SYMBOL_INFO* Symbol);

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SymInitialize(IntPtr hProcess, IntPtr userSearchPath, bool fInvadeProcess);

    [DllImport("dbghelp.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool StackWalk64(
        uint   MachineType,
        IntPtr hProcess,
        IntPtr hThread,
        ref STACKFRAME64 StackFrame,
        IntPtr ContextRecord,    // pointer to CONTEXT blob
        IntPtr ReadMemoryRoutine,
        IntPtr FunctionTableAccessRoutine,
        IntPtr GetModuleBaseRoutine,
        IntPtr TranslateAddress);

    [DllImport("dbghelp.dll")]
    internal static extern IntPtr SymFunctionTableAccess64(IntPtr hProcess, ulong AddrBase);

    [DllImport("dbghelp.dll")]
    internal static extern ulong SymGetModuleBase64(IntPtr hProcess, ulong dwAddr);

    // ── Process Snapshot (PssCaptureSnapshot) ───────────────────────────────────
    // Available from Windows 8.1. Used to snapshot the process before writing a
    // minidump, so MiniDumpWriteDump doesn't suspend the calling thread.

    [Flags]
    internal enum PssCaptureFlags : uint
    {
        PSS_CAPTURE_NONE                    = 0x00000000,
        PSS_CAPTURE_VA_CLONE                = 0x00000001,
        PSS_CAPTURE_HANDLES                 = 0x00000004,
        PSS_CAPTURE_HANDLE_NAME_INFORMATION = 0x00000008,
        PSS_CAPTURE_HANDLE_BASIC_INFORMATION= 0x00000010,
        PSS_CAPTURE_HANDLE_TYPE_SPECIFIC_INFORMATION = 0x00000020,
        PSS_CAPTURE_HANDLE_TRACE            = 0x00000040,
        PSS_CAPTURE_THREADS                 = 0x00000080,
        PSS_CAPTURE_THREAD_CONTEXT          = 0x00000100,
        PSS_CREATE_BREAKAWAY_OPTIONAL       = 0x04000000,
        PSS_CREATE_USE_VM_ALLOCATIONS       = 0x20000000,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint PssCaptureSnapshot(
        IntPtr processHandle,
        PssCaptureFlags captureFlags,
        uint threadContextFlags,
        out IntPtr snapshotHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint PssFreeSnapshot(
        IntPtr processHandle,
        IntPtr snapshotHandle);

    // CONTEXT_ALL for x64: captures full thread state including integer + float registers
    internal const uint CONTEXT_ALL_X64 = 0x0010003F;

    // ── Minidump ─────────────────────────────────────────────────────────────────

    [Flags]
    internal enum MiniDumpType : uint
    {
        MiniDumpNormal                         = 0x00000000,
        MiniDumpWithFullMemory                 = 0x00000002,
        MiniDumpWithHandleData                 = 0x00000004,
        MiniDumpWithThreadInfo                 = 0x00001000,
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint   processId,
        IntPtr hFile,
        MiniDumpType dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);
}
