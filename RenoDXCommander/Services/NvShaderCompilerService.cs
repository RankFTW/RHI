// NvShaderCompilerService.cs -- "Compile shaders now": runs NVIDIA's own shader compiler (NvOSC.exe, shipped with
// the driver, the tool behind the NVIDIA App's Shader Cache › Compile now). It rebuilds the current user's DirectX 12
// shader caches (%LocalAppData%\NVIDIA\DXCache and LocalLow) for the installed driver. No admin needed.
//
// Found by reading NvOSC.exe / the NVIDIA App's NvCplPlugin.dll (Nov 2026, driver 59x):
//   NvOSC.exe -runTool [-resUtil=MIN|MED|MAX]   (no -resUtil = the global "Shader pre-compile" level)
//   It loads nvgpucomp64.dll / NvMemMapStoragex.dll by relative name, so the working folder must be the
//   driver folder (otherwise exit code 3, "Library/DLL loading failure"). Stop = kill NvOSC.exe.

using System.Diagnostics;
using Microsoft.Win32;

namespace RenoDXCommander.Services;

public static partial class NvShaderCompilerService
{
    private const string ExeName = "NvOSC.exe";
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    /// <summary>Full path of the installed driver's NvOSC.exe, or null when the driver doesn't have it.</summary>
    public static string? FindCompiler()
    {
        try
        {
            // The display adapter's user-mode driver paths point into the current driver's DriverStore folder
            using var cls = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (cls != null)
            {
                foreach (var sub in cls.GetSubKeyNames())
                {
                    using var key = cls.OpenSubKey(sub);
                    if (key?.GetValue("ProviderName") is not string provider
                        || !provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) continue;
                    var umd = key.GetValue("UserModeDriverName") switch
                    {
                        string[] arr => arr.FirstOrDefault(),
                        string s => s,
                        _ => null,
                    };
                    var dir = string.IsNullOrWhiteSpace(umd) ? null : Path.GetDirectoryName(umd.Split(',')[0].Trim());
                    if (dir != null && File.Exists(Path.Combine(dir, ExeName))) return Path.Combine(dir, ExeName);
                }
            }

            // Fallback: the newest NVIDIA display driver folder that has it
            var repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");
            if (!Directory.Exists(repo)) return null;
            return new DirectoryInfo(repo).EnumerateDirectories("nv_dispi.inf_amd64_*")
                .Where(d => File.Exists(Path.Combine(d.FullName, ExeName)))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .Select(d => Path.Combine(d.FullName, ExeName))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            CrashReporter.Log($"[NvShaderCompilerService.FindCompiler] {ex.Message}");
            return null;
        }
    }

    /// <summary>True while NVIDIA's compiler is running (started by RHI, the NVIDIA App or its idle task).</summary>
    public static bool IsRunning()
    {
        var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName));
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    /// <summary>
    /// Runs the compiler and waits for it to finish. <paramref name="resUtil"/> = "MIN" / "MED" / "MAX", or null for
    /// the global Shader pre-compile level. Returns the exit code (0 = done), or null if it couldn't start.
    /// With <paramref name="progress"/>, reports which app it's on about once a second (see .Progress.cs).
    /// </summary>
    public static async Task<int?> RunAsync(string exePath, string? resUtil, CancellationToken ct,
        IProgress<ShaderCompileProgress>? progress = null)
    {
        List<ShaderCacheEntry>? plan = null;
        if (progress != null)
        {
            plan = await Task.Run(BuildPlan, ct);
            progress.Report(Describe(plan, 0, started: false));
        }

        var psi = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-runTool");
        if (resUtil != null) psi.ArgumentList.Add("-resUtil=" + resUtil);

        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            CrashReporter.Log($"[NvShaderCompilerService.RunAsync] Start failed — {ex.Message}");
            return null;
        }
        if (proc == null) return null;
        using (proc)
        {
            CrashReporter.Log($"[NvShaderCompilerService.RunAsync] Started {exePath} -runTool{(resUtil != null ? " -resUtil=" + resUtil : "")} (pid {proc.Id})");
            var sw = Stopwatch.StartNew();
            try
            {
                if (plan is { Count: > 0 })
                {
                    var tracker = new ProgressTracker(plan, proc.Id);
                    while (!proc.HasExited)
                    {
                        await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(1000, ct));
                        ct.ThrowIfCancellationRequested();
                        if (proc.HasExited) break;
                        progress!.Report(await Task.Run(tracker.Tick, ct));
                    }
                }
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                CrashReporter.Log("[NvShaderCompilerService.RunAsync] RHI closing, compiler left running");
                return null;
            }
            CrashReporter.Log($"[NvShaderCompilerService.RunAsync] Exit code {proc.ExitCode} after {sw.Elapsed.TotalSeconds:F0} s");
            return proc.ExitCode;
        }
    }

    /// <summary>Stops every running NvOSC.exe (what the NVIDIA App's Stop does).</summary>
    public static int Stop()
    {
        int stopped = 0;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            using (p)
            {
                try { p.Kill(); stopped++; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    CrashReporter.Log($"[NvShaderCompilerService.Stop] pid {p.Id}: {ex.Message}");
                }
            }
        }
        CrashReporter.Log($"[NvShaderCompilerService.Stop] Stopped {stopped}");
        return stopped;
    }
}
