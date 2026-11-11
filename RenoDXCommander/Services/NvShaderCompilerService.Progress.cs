// NvShaderCompilerService.Progress.cs -- progress for "Compile shaders now" (no admin needed).
//
// NvOSC processes one app at a time: the 0002<id>.nvph files in %LocalAppData%\NVIDIA\DXCache, then in
// LocalLow\NVIDIA\DXCache, in folder order, and keeps the current one open while it works on it. Each file's header is
// "nvph", then the app's exe name (ASCII, null-terminated) at byte 17. So: list those files before starting, then ask
// Windows' Restart Manager which of them NvOSC has open; everything before that file is done.
//
// NvOSC's own log can't be used for this: it's off unless two admin-only DRS settings are set, and NvOSC keeps the
// file locked (no read sharing) until it exits. See rhi-dlss-nvidia.md.

using System.Runtime.InteropServices;
using System.Text;

namespace RenoDXCommander.Services;

/// <summary>How far a shader compile run is: working on app <see cref="Done"/> of <see cref="Total"/>.</summary>
public sealed record ShaderCompileProgress(int Done, int Total, string? CurrentApp, double? Fraction);

/// <summary>One app cache NvOSC will process.</summary>
public sealed record ShaderCacheEntry(string Path, string? ExeName, long Size);

public static partial class NvShaderCompilerService
{
    private const int HeaderNameOffset = 17;
    private const int ForwardWindow = 8; // files checked past the current one per tick (small ones finish between ticks)

    /// <summary>The app caches NvOSC will process, in its order (Local, then LocalLow; folder order in each).</summary>
    public static List<ShaderCacheEntry> BuildPlan()
    {
        var plan = new List<ShaderCacheEntry>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var dir in new[] { Path.Combine(local, "NVIDIA", "DXCache"), Path.Combine(profile, "AppData", "LocalLow", "NVIDIA", "DXCache") })
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("0002*.nvph"))
                    plan.Add(new ShaderCacheEntry(f.FullName, ReadExeName(f.FullName), f.Length));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[NvShaderCompilerService.BuildPlan] {dir}: {ex.Message}");
            }
        }
        CrashReporter.Log($"[NvShaderCompilerService.BuildPlan] {plan.Count} app cache(s), {plan.Sum(e => e.Size) / (1024 * 1024)} MB");
        return plan;
    }

    private static string? ReadExeName(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[HeaderNameOffset + 260];
            int n = fs.Read(buf, 0, buf.Length);
            return ParseExeName(buf.AsSpan(0, n));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The exe name from a cache file's header, or null when it isn't one.</summary>
    public static string? ParseExeName(ReadOnlySpan<byte> header)
    {
        if (header.Length <= HeaderNameOffset || !header[..4].SequenceEqual("nvph"u8)) return null;
        var rest = header[HeaderNameOffset..];
        int end = rest.IndexOf((byte)0);
        if (end <= 0) return null;
        var name = rest[..end];
        foreach (var b in name) if (b < 0x20 || b > 0x7E) return null; // not plain text: some other layout
        return Encoding.ASCII.GetString(name);
    }

    /// <summary>
    /// Moves the current position: the first file from <paramref name="current"/> on (at most ForwardWindow past it)
    /// that NvOSC has open. Unchanged when none is open (between two files).
    /// </summary>
    public static int Advance(int current, int count, Func<int, bool> isOpen)
    {
        for (int i = current; i < count && i <= current + ForwardWindow; i++)
            if (isOpen(i)) return i;
        return current;
    }

    /// <summary>Progress when NvOSC is on file <paramref name="current"/> of the plan; sizes weight the bar.</summary>
    public static ShaderCompileProgress Describe(IReadOnlyList<ShaderCacheEntry> plan, int current, bool started)
    {
        if (plan.Count == 0) return new ShaderCompileProgress(0, 0, null, null);
        double total = plan.Sum(e => (double)e.Size);
        double done = plan.Take(current).Sum(e => (double)e.Size);
        double? fraction = total > 0 ? done / total : null;
        return started
            ? new ShaderCompileProgress(current + 1, plan.Count, plan[current].ExeName, fraction)
            : new ShaderCompileProgress(0, plan.Count, null, 0);
    }

    /// <summary>Follows a running NvOSC through the plan. Tick() does file checks: call it off the UI thread.</summary>
    private sealed class ProgressTracker(IReadOnlyList<ShaderCacheEntry> plan, int pid)
    {
        private int _current;
        private bool _started;

        public ShaderCompileProgress Tick()
        {
            int next = Advance(_current, plan.Count, i => RestartManager.IsOpenBy(plan[i].Path, pid));
            if (next != _current || (!_started && RestartManager.IsOpenBy(plan[next].Path, pid)))
            {
                _current = next;
                _started = true;
            }
            return Describe(plan, _current, _started);
        }
    }

    /// <summary>Which processes have a file open (Restart Manager; no admin needed).</summary>
    private static class RestartManager
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }

        private const int ErrorMoreData = 234;

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint handle, int flags, string key);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint handle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(uint handle, uint nFiles, string[] files, uint nApps, IntPtr apps, uint nServices, string[]? services);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(uint handle, out uint needed, ref uint count, [In, Out] RM_PROCESS_INFO[]? info, ref uint reasons);

        public static bool IsOpenBy(string path, int pid)
        {
            if (RmStartSession(out var h, 0, Guid.NewGuid().ToString("N")) != 0) return false;
            try
            {
                if (RmRegisterResources(h, 1, new[] { path }, 0, IntPtr.Zero, 0, null) != 0) return false;
                uint count = 0, reasons = 0;
                int r = RmGetList(h, out var needed, ref count, null, ref reasons);
                if (r != ErrorMoreData || needed == 0) return false;
                var info = new RM_PROCESS_INFO[needed];
                count = needed;
                if (RmGetList(h, out _, ref count, info, ref reasons) != 0) return false;
                for (int i = 0; i < count; i++)
                    if (info[i].Process.dwProcessId == pid) return true;
                return false;
            }
            finally
            {
                RmEndSession(h);
            }
        }
    }
}
