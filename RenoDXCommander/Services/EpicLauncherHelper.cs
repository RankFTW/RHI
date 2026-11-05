// EpicLauncherHelper.cs — starts the Epic Games Launcher in the background before an Epic
// game is launched from its exe. Epic games started directly fail their ownership/online
// check unless the launcher is already running.
using System.Diagnostics;
using Microsoft.Win32;

namespace RenoDXCommander.Services;

public static class EpicLauncherHelper
{
    private const string ProcessName = "EpicGamesLauncher";

    public static bool IsRunning()
    {
        var procs = Process.GetProcessesByName(ProcessName);
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    /// <summary>
    /// Starts the launcher silently if it isn't running and waits for it to come up.
    /// Returns true if the launcher is running afterwards. Never throws.
    /// </summary>
    public static async Task<bool> EnsureRunningAsync(CancellationToken ct = default)
    {
        try
        {
            if (await Task.Run(IsRunning, ct)) return true;

            var exe = await Task.Run(FindLauncherExe, ct);
            if (exe != null)
            {
                CrashReporter.Log($"[EpicLauncherHelper.EnsureRunningAsync] Starting Epic launcher: {exe} -silent");
                Process.Start(new ProcessStartInfo(exe)
                {
                    Arguments = "-silent",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                })?.Dispose();
            }
            else
            {
                // No exe found: the bare protocol starts the launcher (may show its window).
                CrashReporter.Log("[EpicLauncherHelper.EnsureRunningAsync] Launcher exe not found, starting via protocol");
                Process.Start(new ProcessStartInfo("com.epicgames.launcher://") { UseShellExecute = true })?.Dispose();
            }

            // Wait for the process, then give its client services a few seconds to sign in.
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(500, ct);
                if (IsRunning())
                {
                    await Task.Delay(6000, ct);
                    CrashReporter.Log("[EpicLauncherHelper.EnsureRunningAsync] Epic launcher is running");
                    return true;
                }
            }
            CrashReporter.Log("[EpicLauncherHelper.EnsureRunningAsync] Epic launcher did not start within 20 s");
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                       or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            CrashReporter.Log($"[EpicLauncherHelper.EnsureRunningAsync] Failed to start Epic launcher — {ex.Message}");
            return false;
        }
    }

    /// <summary>Reads the launcher exe from the com.epicgames.launcher protocol handler, else the default install path.</summary>
    private static string? FindLauncherExe()
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(@"com.epicgames.launcher\shell\open\command");
            var cmd = key?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(cmd))
            {
                cmd = cmd.Trim();
                string path;
                if (cmd.StartsWith('"'))
                {
                    var end = cmd.IndexOf('"', 1);
                    path = end > 1 ? cmd.Substring(1, end - 1) : cmd.Trim('"');
                }
                else
                {
                    var idx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    path = idx > 0 ? cmd.Substring(0, idx + 4) : cmd;
                }
                if (File.Exists(path)) return path;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            CrashReporter.Log($"[EpicLauncherHelper.FindLauncherExe] Registry read failed — {ex.Message}");
        }

        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var sub in new[] { @"Portal\Binaries\Win64", @"Portal\Binaries\Win32" })
        {
            var p = Path.Combine(pf86, "Epic Games", "Launcher", sub, "EpicGamesLauncher.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
