// GamePauser.cs -- optional "pause the game while the RHI overlay is open" (Settings › RHI overlay, off by default).
// Windows can't stop another program reading the controller (games read XInput directly, even in the
// background), so the only way to keep the game from reacting is to freeze its process: NtSuspendProcess
// when the overlay opens, NtResumeProcess when it closes. Every exit path resumes it, including RHI closing
// or crashing, so a game is never left frozen. Fails quietly (logged) when RHI isn't allowed to, e.g. an
// elevated game while RHI isn't in Admin Mode.
using System.Runtime.InteropServices;

using RenoDXCommander.Services;

namespace RenoDXCommander.Overlay;

internal static class GamePauser
{
    private const uint PROCESS_SUSPEND_RESUME = 0x0800;

    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr process);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    private static readonly object Gate = new();
    private static IntPtr _handle;      // the paused game's process (open while paused)
    private static int _pid;
    private static bool _hooked;

    internal static bool IsPaused { get { lock (Gate) return _handle != IntPtr.Zero; } }

    /// <summary>Freezes the process. False when it couldn't (already paused, access denied, …).</summary>
    internal static bool Pause(int pid)
    {
        lock (Gate)
        {
            if (_handle != IntPtr.Zero) return false;
            HookExit();
            var h = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
            if (h == IntPtr.Zero)
            {
                CrashReporter.Log($"[GamePauser.Pause] Can't open PID {pid} (error {Marshal.GetLastWin32Error()}) — game not paused");
                return false;
            }
            int status = NtSuspendProcess(h);
            if (status != 0)
            {
                CrashReporter.Log($"[GamePauser.Pause] NtSuspendProcess failed (0x{status:X8}) — game not paused");
                NativeInterop.CloseHandle(h);
                return false;
            }
            _handle = h;
            _pid = pid;
            CrashReporter.Log($"[GamePauser.Pause] Game paused (PID {pid})");
            return true;
        }
    }

    /// <summary>Unfreezes the paused game, if any. Safe to call any number of times, from any thread.</summary>
    internal static void Resume()
    {
        lock (Gate)
        {
            if (_handle == IntPtr.Zero) return;
            int status = NtResumeProcess(_handle);
            NativeInterop.CloseHandle(_handle);
            _handle = IntPtr.Zero;
            CrashReporter.Log(status == 0
                ? $"[GamePauser.Resume] Game resumed (PID {_pid})"
                : $"[GamePauser.Resume] NtResumeProcess failed (0x{status:X8}) for PID {_pid}");
        }
    }

    /// <summary>Resume on any way RHI goes away (close, crash), so the game never stays frozen.</summary>
    private static void HookExit()
    {
        if (_hooked) return;
        _hooked = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Resume();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Resume();
    }
}
