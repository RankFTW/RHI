using System.Diagnostics;

namespace RenoDXCommander.Services;

/// <summary>
/// Paths to files shipped next to RHI.exe.
/// Uses the exe's own folder (correct for single-file publish), not AppContext.BaseDirectory.
/// </summary>
public static class AppPaths
{
    /// <summary>Folder that contains RHI.exe.</summary>
    public static string ExeDir { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>Path of a file shipped next to RHI.exe.</summary>
    public static string InExeDir(string fileName) => Path.Combine(ExeDir, fileName);

    /// <summary>The 7z.exe shipped next to RHI.exe.</summary>
    public static string Bundled7Zip => InExeDir("7z.exe");

    private static string? _found7Zip;

    /// <summary>
    /// Finds 7z.exe: the bundled copy first, then the usual 7-Zip install folders, then PATH.
    /// Returns null when none is found. Blocks for up to 5 s on the PATH check, so call it off the UI thread.
    /// </summary>
    public static string? Find7Zip()
    {
        if (_found7Zip != null && (_found7Zip == "7z.exe" || System.IO.File.Exists(_found7Zip)))
            return _found7Zip;

        var candidates = new[]
        {
            Bundled7Zip,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
        };
        foreach (var path in candidates)
        {
            if (System.IO.File.Exists(path))
            {
                if (!string.Equals(path, Bundled7Zip, StringComparison.OrdinalIgnoreCase))
                    CrashReporter.Log($"[AppPaths.Find7Zip] Bundled 7-Zip missing, using {path}");
                return _found7Zip = path;
            }
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "7z.exe",
                Arguments = "--help",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (proc != null)
            {
                if (!proc.WaitForExit(5000))
                {
                    try { proc.Kill(); }
                    catch (InvalidOperationException ex) { CrashReporter.Log($"[AppPaths.Find7Zip] Failed to stop 7z probe — {ex.Message}"); }
                }
                CrashReporter.Log("[AppPaths.Find7Zip] Using 7z.exe from PATH");
                return _found7Zip = "7z.exe";
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 7z.exe not on PATH is the normal case.
        }

        CrashReporter.Log("[AppPaths.Find7Zip] 7-Zip not found at any known location");
        return null;
    }
}
