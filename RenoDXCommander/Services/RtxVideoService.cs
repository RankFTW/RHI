// RtxVideoService.cs -- RTX Video Enhancements (RTX Video Super Resolution + RTX Video HDR): the global
// video settings in NVIDIA App › System › Video. They apply to video in browsers and players, not games.
//
// How the NVIDIA App does it (found by decompiling NvCplDisplayPlugin.dll, Nov 2026): it loads the NVIDIA
// App's own NvCpl\NvCpl.dll and calls NvCplApiGetSetting / NvCplApiSetSetting with the setting ids below,
// then NvCplApiExecute(0x10000, 0, -1) to apply. The driver applies the change at once (no restart) and
// saves it itself under HKLM\...\Class\{4d36e968-...}\NNNN (_User_Global_VAL_SuperResolution, _TrueHDR*,
// _RTXVideoFlags). Writing those registry values directly does nothing: the driver only reads them at load.
// No admin needed. The driver's own System32\nvcpl.dll refuses these ids (error 7), so the NVIDIA App is
// required. Only the ids and sizes the NVIDIA App uses are touched: an unknown id crashed the test process.

using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

public sealed record RtxVideoState(
    bool SrSupported,
    int SrMode,          // 0 off, 1-4 manual quality (very low .. high), 5 auto
    int SrGpuPriority,   // auto mode only: 0 low, 1 medium, 2 high
    int SrStatus,        // 0 inactive, 1-4 = the quality level in use right now
    bool HdrSupported,
    bool HdrOn,
    int HdrPeak,         // nits
    int HdrMiddleGrey,   // nits, 10-100
    int HdrContrast,     // -100..100
    int HdrSaturation,   // -100..100
    int HdrPeakMin,      // the display's slider range for peak
    int HdrPeakMax,
    bool SrIndicator,
    bool HdrIndicator);

public static class RtxVideoService
{
    // ── Setting ids and sizes (from the NVIDIA App's plugin) ──────────────────
    private const uint SR_INFO = 0x315;          // 20 bytes: +0 supported, +4 mode
    private const uint SR_STATUS = 0x316;        // 4
    private const uint SR_VALUE = 0x317;         // 4, set
    private const uint SR_GPU_PRIORITY = 0x32F;  // 4
    private const uint HDR_INFO = 0x318;         // 48, get + set (sliders)
    private const uint HDR_ENABLE = 0x31A;       // 4, set
    private const uint VIDEO_FLAGS = 0x330;      // 4: 0x100 SR indicator, 0x001 HDR indicator
    private const int SR_INFO_SIZE = 0x14;
    private const int HDR_INFO_SIZE = 0x30;
    private const uint EXECUTE_APPLY = 0x10000;

    public const int FlagSrIndicator = 0x100;
    public const int FlagHdrIndicator = 0x001;

    // HDR struct offsets
    private const int HDR_SUPPORTED = 0x00, HDR_ON = 0x04, HDR_PEAK = 0x10, HDR_MID = 0x14,
        HDR_CONTRAST = 0x18, HDR_SATURATION = 0x1C, HDR_PEAK_MAX = 0x20, HDR_PEAK_MIN = 0x24;

    // Buffers are much bigger than the size passed, so a DLL that writes past the size can't corrupt memory.
    private const int BufferSize = 1024;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int InitFn(IntPtr a, IntPtr b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CloseFn();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SettingFn(uint flags, ref ulong target, uint id, byte[] buffer, ulong size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ExecuteFn(uint command, IntPtr arg, int arg2);

    private static readonly object Gate = new();
    private static bool _loadTried;
    private static InitFn? _init;
    private static CloseFn? _close;
    private static SettingFn? _get;
    private static SettingFn? _set;
    private static ExecuteFn? _execute;

    /// <summary>The NVIDIA App's NvCpl.dll, or null when the NVIDIA App isn't installed.</summary>
    public static string? FindNvCplDll()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"C:\Program Files" })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var path = Path.Combine(root, "NVIDIA Corporation", "NVIDIA App", "NvCpl", "NvCpl.dll");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static bool EnsureLoaded()
    {
        if (_loadTried) return _get != null;
        _loadTried = true;
        var path = FindNvCplDll();
        if (path == null)
        {
            CrashReporter.Log("[RtxVideoService.EnsureLoaded] NVIDIA App's NvCpl.dll not found");
            return false;
        }
        try
        {
            var lib = NativeLibrary.Load(path);
            _init = Marshal.GetDelegateForFunctionPointer<InitFn>(NativeLibrary.GetExport(lib, "NvCplApiInit"));
            _close = Marshal.GetDelegateForFunctionPointer<CloseFn>(NativeLibrary.GetExport(lib, "NvCplApiClose"));
            _get = Marshal.GetDelegateForFunctionPointer<SettingFn>(NativeLibrary.GetExport(lib, "NvCplApiGetSetting"));
            _set = Marshal.GetDelegateForFunctionPointer<SettingFn>(NativeLibrary.GetExport(lib, "NvCplApiSetSetting"));
            _execute = Marshal.GetDelegateForFunctionPointer<ExecuteFn>(NativeLibrary.GetExport(lib, "NvCplApiExecute"));
            CrashReporter.Log($"[RtxVideoService.EnsureLoaded] Loaded {path}");
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or ArgumentException)
        {
            _get = null;
            CrashReporter.Log($"[RtxVideoService.EnsureLoaded] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Runs <paramref name="body"/> inside NvCplApiInit / NvCplApiClose. Call from a background thread.</summary>
    private static T? Session<T>(string caller, Func<T?> body) where T : class
    {
        lock (Gate)
        {
            if (!EnsureLoaded()) return null;
            int rc = _init!(IntPtr.Zero, IntPtr.Zero);
            if (rc != 0)
            {
                CrashReporter.Log($"[RtxVideoService.{caller}] NvCplApiInit failed ({rc})");
                return null;
            }
            try { return body(); }
            finally { _close!(); }
        }
    }

    private static int Get(uint id, byte[] buffer, int size)
    {
        ulong target = 0;
        return _get!(1, ref target, id, buffer, (ulong)size);
    }

    private static int Set(uint id, byte[] buffer, int size)
    {
        ulong target = 0;
        return _set!(1, ref target, id, buffer, (ulong)size);
    }

    private static int GetInt(uint id, out int value)
    {
        var b = new byte[BufferSize];
        int rc = Get(id, b, 4);
        value = BitConverter.ToInt32(b, 0);
        return rc;
    }

    private static int SetInt(uint id, int value)
    {
        var b = new byte[BufferSize];
        BitConverter.GetBytes(value).CopyTo(b, 0);
        return Set(id, b, 4);
    }

    private static int Apply() => _execute!(EXECUTE_APPLY, IntPtr.Zero, -1);

    /// <summary>Reads every setting. Null when the NVIDIA App isn't installed or the driver refuses.</summary>
    public static RtxVideoState? Read() => Session("Read", () =>
    {
        var sr = new byte[BufferSize];
        int rcSr = Get(SR_INFO, sr, SR_INFO_SIZE);
        var hdr = new byte[BufferSize];
        int rcHdr = Get(HDR_INFO, hdr, HDR_INFO_SIZE);
        if (rcSr != 0 && rcHdr != 0)
        {
            CrashReporter.Log($"[RtxVideoService.Read] Not available (sr={rcSr}, hdr={rcHdr})");
            return null;
        }
        GetInt(SR_STATUS, out int status);
        GetInt(SR_GPU_PRIORITY, out int priority);
        GetInt(VIDEO_FLAGS, out int flags);
        int I(byte[] b, int o) => BitConverter.ToInt32(b, o);
        return new RtxVideoState(
            SrSupported: rcSr == 0 && I(sr, 0) != 0,
            SrMode: I(sr, 4),
            SrGpuPriority: Math.Clamp(priority, 0, 2),
            SrStatus: status,
            HdrSupported: rcHdr == 0 && I(hdr, HDR_SUPPORTED) != 0,
            HdrOn: I(hdr, HDR_ON) != 0,
            HdrPeak: I(hdr, HDR_PEAK),
            HdrMiddleGrey: I(hdr, HDR_MID),
            HdrContrast: I(hdr, HDR_CONTRAST) - 100,
            HdrSaturation: I(hdr, HDR_SATURATION) - 100,
            HdrPeakMin: I(hdr, HDR_PEAK_MIN),
            HdrPeakMax: I(hdr, HDR_PEAK_MAX),
            SrIndicator: (flags & FlagSrIndicator) != 0,
            HdrIndicator: (flags & FlagHdrIndicator) != 0);
    });

    private static bool Write(string caller, string what, Func<int> body)
    {
        var ok = Session(caller, () =>
        {
            int rc = body();
            int rcApply = rc == 0 ? Apply() : -1;
            CrashReporter.Log($"[RtxVideoService.{caller}] {what}: set={rc}, apply={rcApply}");
            return (object)(rc == 0 && rcApply == 0);
        });
        return ok is true;
    }

    /// <summary>Super Resolution: 0 off, 1-4 manual quality, 5 auto.</summary>
    public static bool SetSuperResolution(int mode) =>
        Write("SetSuperResolution", $"mode={mode}", () => SetInt(SR_VALUE, Math.Clamp(mode, 0, 5)));

    /// <summary>Super Resolution GPU priority (auto mode): 0 low, 1 medium, 2 high.</summary>
    public static bool SetGpuPriority(int priority) =>
        Write("SetGpuPriority", $"priority={priority}", () => SetInt(SR_GPU_PRIORITY, Math.Clamp(priority, 0, 2)));

    public static bool SetHdrEnabled(bool on) =>
        Write("SetHdrEnabled", $"on={on}", () => SetInt(HDR_ENABLE, on ? 1 : 0));

    /// <summary>Video HDR sliders. Reads the current block first and changes only the four values.</summary>
    public static bool SetHdrValues(int peak, int middleGrey, int contrast, int saturation) =>
        Write("SetHdrValues", $"peak={peak}, mid={middleGrey}, contrast={contrast}, saturation={saturation}", () =>
        {
            var b = new byte[BufferSize];
            int rc = Get(HDR_INFO, b, HDR_INFO_SIZE);
            if (rc != 0) return rc;
            int min = BitConverter.ToInt32(b, HDR_PEAK_MIN), max = BitConverter.ToInt32(b, HDR_PEAK_MAX);
            if (max > 0 && min > 0 && min <= max) peak = Math.Clamp(peak, min, max);
            BitConverter.GetBytes(peak).CopyTo(b, HDR_PEAK);
            BitConverter.GetBytes(Math.Clamp(middleGrey, 10, 100)).CopyTo(b, HDR_MID);
            BitConverter.GetBytes(Math.Clamp(contrast, -100, 100) + 100).CopyTo(b, HDR_CONTRAST);
            BitConverter.GetBytes(Math.Clamp(saturation, -100, 100) + 100).CopyTo(b, HDR_SATURATION);
            return Set(HDR_INFO, b, HDR_INFO_SIZE);
        });

    /// <summary>Turns one status indicator bit (<see cref="FlagSrIndicator"/> / <see cref="FlagHdrIndicator"/>) on or off.</summary>
    public static bool SetIndicator(int flag, bool on) =>
        Write("SetIndicator", $"flag=0x{flag:X}, on={on}", () =>
        {
            int rc = GetInt(VIDEO_FLAGS, out int flags);
            if (rc != 0) return rc;
            flags = on ? flags | flag : flags & ~flag;
            return SetInt(VIDEO_FLAGS, flags);
        });
}
