// GamePriorityService.cs — Runs games launched from RHI at High process priority.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RenoDXCommander.Services;

/// <summary>
/// Raises the games RHI launches to High priority.
/// A process started without a priority flag inherits its parent's priority when the parent
/// runs at Below Normal or Idle, and Task Scheduler starts RHI at Below Normal in Admin Mode,
/// so games launched from RHI could start at Below Normal. Steam, Epic, Xbox and wrappers
/// (SKSE, MO2, a game's own launcher) start the real game exe themselves, so the game is
/// found by its install folder rather than by the process RHI started.
/// </summary>
public static class GamePriorityService
{
    // Store launchers and a game's own launcher can take a while to start the game exe.
    private static readonly TimeSpan WatchDuration = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(2);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// Brings RHI back to Normal priority when it was started below it (the Admin Mode
    /// scheduled task runs at Below Normal), so the games and launchers it starts stop
    /// inheriting Below Normal.
    /// </summary>
    public static void EnsureNormalPriorityForSelf()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            var current = self.PriorityClass;
            if (current is ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Idle)
            {
                self.PriorityClass = ProcessPriorityClass.Normal;
                CrashReporter.Log($"[GamePriorityService.EnsureNormalPriorityForSelf] RHI started at {current} priority — raised to Normal");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GamePriorityService.EnsureNormalPriorityForSelf] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Sets <paramref name="launched"/> (the game exe RHI started, if any) to High now, then
    /// for a while also sets every process running from <paramref name="installPath"/> to High.
    /// Pass null for <paramref name="launched"/> when RHI started a store launcher, so the
    /// launcher itself is not raised.
    /// </summary>
    public static void BoostLaunchedGame(Process? launched, string? installPath, string gameName)
    {
        var handled = new HashSet<int>();
        if (launched != null)
        {
            try { handled.Add(launched.Id); } catch { }
            TrySetHigh(launched, gameName);
        }

        var folder = NormalizeGameFolder(installPath);
        if (folder == null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var selfId = Environment.ProcessId;
                var buffer = new StringBuilder(1024);
                var deadline = DateTime.UtcNow + WatchDuration;
                while (DateTime.UtcNow < deadline)
                {
                    foreach (var p in Process.GetProcesses())
                    {
                        using (p)
                        {
                            if (p.Id == selfId || handled.Contains(p.Id)) continue;
                            var path = GetImagePath(p.Id, buffer);
                            if (path == null || !IsInFolder(path, folder)) continue;
                            handled.Add(p.Id);
                            TrySetHigh(p, gameName);
                        }
                    }
                    await Task.Delay(WatchInterval);
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[GamePriorityService.BoostLaunchedGame] '{gameName}' watch failed — {ex.Message}");
            }
        });
    }

    private static void TrySetHigh(Process process, string gameName)
    {
        var id = -1;
        try
        {
            id = process.Id;
            var before = process.PriorityClass;
            if (before is ProcessPriorityClass.High or ProcessPriorityClass.RealTime) return;
            process.PriorityClass = ProcessPriorityClass.High;
            CrashReporter.Log($"[GamePriorityService.TrySetHigh] '{gameName}' — {process.ProcessName} (PID {id}) raised from {before} to High");
        }
        catch (Exception ex)
        {
            // Exited already, or elevated / protected by anti-cheat while RHI is not
            CrashReporter.Log($"[GamePriorityService.TrySetHigh] '{gameName}' — couldn't raise PID {id} to High — {ex.Message}");
        }
    }

    /// <summary>Full exe path of a process, or null when it can't be queried.</summary>
    private static string? GetImagePath(int processId, StringBuilder buffer)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            buffer.Clear();
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// Returns the full install folder path ending in a separator, or null when there is no
    /// folder or it is a drive root (everything on the drive would match).
    /// </summary>
    internal static string? NormalizeGameFolder(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return null;
        try
        {
            var full = Path.GetFullPath(installPath);
            if (!Path.EndsInDirectorySeparator(full))
                full += Path.DirectorySeparatorChar;
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || string.Equals(root, full, StringComparison.OrdinalIgnoreCase))
                return null;
            return full;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="exePath"/> is inside <paramref name="folder"/> (from <see cref="NormalizeGameFolder"/>).</summary>
    internal static bool IsInFolder(string exePath, string folder)
        => exePath.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
}
