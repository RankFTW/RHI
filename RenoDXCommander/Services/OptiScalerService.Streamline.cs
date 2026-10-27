using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

public partial class OptiScalerService
{
    // ── Streamline deployment ─────────────────────────────────────────────────

    /// <summary>
    /// The Streamline staging folder — %LocalAppData%\RHI\Streamline\.
    /// Mirrors the private StreamlineCacheDir in DlssStreamlineService.
    /// </summary>
    public static string StreamlineStagingFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "Streamline");

    /// <summary>
    /// Copies all files from the specified Streamline version subfolder to
    /// &lt;installPath&gt;\OptiScaler\Streamline\. If version is null or not found,
    /// falls back to the first available subfolder.
    /// Always uses plain File.Copy — no .original backups since these are RHI-managed files.
    /// </summary>
    public void DeployStreamlineToGame(string installPath, string? version = null)
    {
        if (string.IsNullOrEmpty(installPath)) return;

        var sourceRootDir = StreamlineStagingFolder;
        if (!Directory.Exists(sourceRootDir))
        {
            CrashReporter.Log($"[OptiScalerService.DeployStreamlineToGame] Streamline staging folder not found — {sourceRootDir}");
            return;
        }

        // Resolve the version subfolder to deploy from
        string? sourceDir = null;
        if (!string.IsNullOrEmpty(version))
        {
            var versionDir = Path.Combine(sourceRootDir, version);
            if (Directory.Exists(versionDir) && Directory.GetFiles(versionDir, "*.dll").Length > 0)
                sourceDir = versionDir;
        }

        // Fallback: pick the subfolder with the highest version number
        if (sourceDir == null)
        {
            var subDirs = Directory.GetDirectories(sourceRootDir)
                .Where(d => Directory.GetFiles(d, "*.dll").Length > 0)
                .OrderByDescending(d => Version.TryParse(Path.GetFileName(d), out var v) ? v : new Version(0, 0))
                .FirstOrDefault();
            sourceDir = subDirs ?? sourceRootDir;
        }

        var destDir = Path.Combine(installPath, "OptiScaler", "Streamline");
        Directory.CreateDirectory(destDir);

        var files = Directory.GetFiles(sourceDir, "*.dll");
        int copied = 0;
        foreach (var file in files)
        {
            try
            {
                // Plain File.Copy — no .original backups. These are RHI-managed files, not game originals.
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
                copied++;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.DeployStreamlineToGame] Failed to copy '{Path.GetFileName(file)}' — {ex.Message}");
            }
        }

        CrashReporter.Log($"[OptiScalerService.DeployStreamlineToGame] Deployed {copied} Streamline file(s) from '{Path.GetFileName(sourceDir)}' to {installPath}");

        // Also deploy the newest nvngx_dlssg.dll into the Streamline folder.
        // This enables FG support within Streamline when using OptiScaler Nightly/DLSS NR.
        // Use cached dll if already on disk; otherwise fire-and-forget download.
        var dlssSvc = App.Services.GetRequiredService<IDlssStreamlineService>();
        const string DlssgDll = "nvngx_dlssg.dll";
        var dlssgDest = Path.Combine(destDir, DlssgDll);
        _ = Task.Run(async () =>
        {
            try
            {
                var cachedDlssg = await dlssSvc.EnsureNewestDlssgCachedAsync().ConfigureAwait(false);
                if (cachedDlssg != null && File.Exists(cachedDlssg))
                {
                    File.Copy(cachedDlssg, dlssgDest, overwrite: true);
                    CrashReporter.Log($"[OptiScalerService.DeployStreamlineToGame] Deployed {DlssgDll} to Streamline folder in '{installPath}'");
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.DeployStreamlineToGame] Failed to deploy {DlssgDll} — {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Removes the OptiScaler\Streamline\ subfolder from the given game install path.
    /// </summary>
    public void RemoveStreamlineFromGame(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;

        var destDir = Path.Combine(installPath, "OptiScaler", "Streamline");
        if (Directory.Exists(destDir))
        {
            try
            {
                Directory.Delete(destDir, recursive: true);
                CrashReporter.Log($"[OptiScalerService.RemoveStreamlineFromGame] Removed Streamline folder from {installPath}");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.RemoveStreamlineFromGame] Failed — {ex.Message}");
            }
        }
    }
}
