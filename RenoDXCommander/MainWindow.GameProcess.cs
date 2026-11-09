// MainWindow.GameProcess.cs -- finds a launched game's processes in its folders.
using System.Runtime.InteropServices;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    // Helper processes that run from a game's folder but aren't the game: crash reporters,
    // browser sub-processes, anti-cheat bootstrappers. Unreal's crashpad_handler starts before
    // the game exe, so picking it meant "Running" / HDR-off-on-exit followed the wrong process.
    private static readonly string[] GameHelperProcessPrefixes =
    {
        "crashpad_handler", "crashreportclient", "crashreporter", "crashhandler", "crashsender",
        "unitycrashhandler", "unrealcefsubprocess", "epicwebhelper", "cefsharp", "qtwebengineprocess",
        "bugsplat", "werfault", "start_protected_game", "easyanticheat", "unins",
    };

    internal static bool IsGameHelperProcess(string processName) =>
        GameHelperProcessPrefixes.Any(p => processName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Folders to look in for the game's processes, most specific first: the exe folder, then
    /// the game's root. Some games run their real exe outside the folder RHI installs into
    /// (Phantom Blade Zero: ProjectPBZ\Binaries\Win64 holds only crashpad_handler; the game is
    /// PBZ\steam\PBZ.exe). Each entry is a full lower-case path with a trailing backslash.
    /// </summary>
    internal static List<string> GameProcessSearchRoots(string installPath)
    {
        var roots = new List<string>();
        void AddOne(string p)
        {
            var n = Path.GetFullPath(p).TrimEnd('\\').ToLowerInvariant() + "\\";
            if (n.Length > 3 && !roots.Contains(n)) roots.Add(n); // never a bare drive root
        }
        void Add(string? p)
        {
            if (string.IsNullOrEmpty(p)) return;
            AddOne(p);
            // Running processes report their real folder. Game Pass games on another drive live in
            // e.g. D:\xbox\<Game>\Content behind a WindowsApps junction, so add that folder too.
            if (XboxPaths.IsWindowsApps(p) && XboxPaths.ResolveRealFolder(p) is { } real) AddOne(real);
        }
        Add(installPath);

        var full = Path.GetFullPath(installPath).TrimEnd('\\');
        // Unreal: <Root>\<Project>\Binaries\Win64 → <Root>
        var bin = full.LastIndexOf(@"\Binaries\", StringComparison.OrdinalIgnoreCase);
        if (bin > 0) Add(Path.GetDirectoryName(full.Substring(0, bin)));
        // Store library roots: ...\steamapps\common\<Game>, ...\Epic Games\<Game> etc.
        foreach (var marker in new[] { @"\steamapps\common\", @"\Epic Games\", @"\GOG Games\" })
        {
            var i = full.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var start = i + marker.Length;
            var end = full.IndexOf('\\', start);
            Add(end < 0 ? full : full.Substring(0, end));
        }
        return roots;
    }

    /// <summary>
    /// Game processes (helpers skipped) running from the first search root that has any.
    /// Empty if none yet. Uses the cheap limited-query image path, not Process.MainModule.
    /// </summary>
    private static List<System.Diagnostics.Process> FindGameProcesses(List<string> roots)
    {
        var byRoot = new List<System.Diagnostics.Process>?[roots.Count];
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            int hit = -1;
            if (!IsGameHelperProcess(p.ProcessName))
            {
                var path = GetProcessImagePath(p.Id);
                if (path != null)
                {
                    path = path.ToLowerInvariant();
                    for (int r = 0; r < roots.Count && hit < 0; r++)
                        if (path.StartsWith(roots[r], StringComparison.Ordinal)) hit = r;
                }
            }
            if (hit >= 0) (byRoot[hit] ??= new()).Add(p);
            else p.Dispose();
        }
        var result = byRoot.FirstOrDefault(l => l != null) ?? new();
        foreach (var l in byRoot) if (l != null && l != result) foreach (var p in l) p.Dispose();
        return result;
    }

    /// <summary>
    /// Waits until every game process from <paramref name="roots"/> has exited. Re-checks for a
    /// few seconds after each exit, so a launcher handing over to the game (or the game
    /// restarting itself) isn't taken as the game closing.
    /// </summary>
    private static async Task WaitForGameExitAsync(List<string> roots, List<System.Diagnostics.Process> procs)
    {
        while (procs.Count > 0)
        {
            try { await Task.WhenAny(procs.Select(p => p.WaitForExitAsync())).ConfigureAwait(false); }
            catch (Exception ex) { CrashReporter.Log($"[MainWindow.WaitForGameExitAsync] Wait failed — {ex.Message}"); }

            List<System.Diagnostics.Process> next = new();
            for (int attempt = 0; attempt < 5 && next.Count == 0; attempt++)
            {
                next = procs.Where(p => { try { return !p.HasExited; } catch (Exception) { return false; } }).ToList();
                if (next.Count > 0) break;
                await Task.Delay(1000).ConfigureAwait(false);
                try { next = FindGameProcesses(roots); }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.WaitForGameExitAsync] Process scan failed — {ex.Message}"); }
            }
            foreach (var p in procs.Except(next)) p.Dispose();
            procs = next;
        }
    }

    private static string DescribeProcesses(List<System.Diagnostics.Process> procs) =>
        string.Join(", ", procs.Select(p => { try { return $"'{p.ProcessName}' (PID {p.Id})"; } catch (Exception) { return "(exited)"; } }));

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint flags, System.Text.StringBuilder exeName, ref uint size);

    internal static string? GetProcessImagePath(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null; // system / protected process: not a game we launched
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { NativeInterop.CloseHandle(h); }
    }
}
