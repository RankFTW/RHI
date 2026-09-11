using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages dgVoodoo2 deployment for Luma games that require a DX9→DX11 translation layer.
/// Downloads and caches the dgVoodoo2 zip, extracts only the needed files (MS/x86/D3D9.dll),
/// and writes a pre-configured dgVoodoo.conf. Uses the sentinel pattern for clean uninstall.
/// </summary>
public class DgVoodooService
{
    private const string D3D9Entry   = "MS/x86/D3D9.dll";
    private const string D3D9Dll     = "D3D9.dll";
    private const string ConfFile    = "dgVoodoo.conf";

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "dgvoodoo");

    private readonly HttpClient _http;

    public DgVoodooService(HttpClient http)
    {
        _http = http;
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the dgVoodoo2 zip for the given version is cached locally.
    /// No-op if already cached (HEAD Content-Length comparison).
    /// </summary>
    public async Task EnsureStagedAsync(string version, string url)
    {
        Directory.CreateDirectory(CacheDir);
        var zipPath = GetCachedZipPath(version);

        if (File.Exists(zipPath))
        {
            // Check if already cached via Content-Length comparison
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Head, url);
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var remoteSize = resp.Content.Headers.ContentLength;
                    var localSize = new FileInfo(zipPath).Length;
                    if (remoteSize.HasValue && remoteSize.Value == localSize)
                    {
                        CrashReporter.Log($"[DgVoodooService.EnsureStagedAsync] Cache hit for v{version} ({localSize} bytes)");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DgVoodooService.EnsureStagedAsync] HEAD check failed — {ex.Message}, re-downloading");
            }
        }

        CrashReporter.Log($"[DgVoodooService.EnsureStagedAsync] Downloading dgVoodoo2 v{version} from {url}");
        var tempPath = zipPath + ".tmp";
        try
        {
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var buf = new byte[1024 * 1024];
            using (var net = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, useAsync: true))
            {
                int read;
                while ((read = await net.ReadAsync(buf).ConfigureAwait(false)) > 0)
                    await file.WriteAsync(buf.AsMemory(0, read)).ConfigureAwait(false);
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(tempPath, zipPath);
            CrashReporter.Log($"[DgVoodooService.EnsureStagedAsync] Downloaded dgVoodoo2 v{version} ({new FileInfo(zipPath).Length} bytes)");
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
            CrashReporter.Log($"[DgVoodooService.EnsureStagedAsync] Download failed — {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Returns true if the dgVoodoo2 zip for the given version is cached.
    /// </summary>
    public bool IsCached(string version) => File.Exists(GetCachedZipPath(version));

    // ── Deploy ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deploys D3D9.dll (from MS/x86/) and dgVoodoo.conf to the game folder.
    /// Uses SentinelBackup on D3D9.dll so uninstall can restore any game-original.
    /// dgVoodoo.conf is always written by RHI and tracked for deletion on uninstall.
    /// Returns the list of relative paths deployed (for tracking in LumaInstalledRecord.InstalledFiles).
    /// </summary>
    public List<string> DeployToGame(string installPath, string version)
    {
        var deployed = new List<string>();
        var zipPath = GetCachedZipPath(version);
        if (!File.Exists(zipPath))
        {
            CrashReporter.Log($"[DgVoodooService.DeployToGame] Zip not cached for v{version} — skipping deploy");
            return deployed;
        }

        // ── Deploy D3D9.dll ──────────────────────────────────────────────────
        var d3d9Dest = Path.Combine(installPath, D3D9Dll);
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(D3D9Entry)
                ?? zip.Entries.FirstOrDefault(e =>
                    e.FullName.Equals(D3D9Entry, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                CrashReporter.Log($"[DgVoodooService.DeployToGame] {D3D9Entry} not found in zip");
                return deployed;
            }

            AuxInstallService.SentinelBackup(d3d9Dest);
            using (var src = entry.Open())
            using (var dst = new FileStream(d3d9Dest, FileMode.Create, FileAccess.Write, FileShare.None))
                src.CopyTo(dst);

            deployed.Add(D3D9Dll);
            CrashReporter.Log($"[DgVoodooService.DeployToGame] Deployed {D3D9Dll} to '{installPath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DgVoodooService.DeployToGame] Failed to deploy {D3D9Dll} — {ex.Message}");
            return deployed;
        }

        // ── Write dgVoodoo.conf ──────────────────────────────────────────────
        var confDest = Path.Combine(installPath, ConfFile);
        try
        {
            File.WriteAllText(confDest, BuildConf());
            deployed.Add(ConfFile);
            CrashReporter.Log($"[DgVoodooService.DeployToGame] Wrote {ConfFile} to '{installPath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DgVoodooService.DeployToGame] Failed to write {ConfFile} — {ex.Message}");
        }

        return deployed;
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes D3D9.dll (via SentinelRestore) and dgVoodoo.conf from the game folder.
    /// Called when dgVoodoo2 files are NOT already tracked in LumaInstalledRecord.InstalledFiles
    /// (e.g. for games installed before this feature was added).
    /// </summary>
    public void RemoveFromGame(string installPath)
    {
        var d3d9Path = Path.Combine(installPath, D3D9Dll);
        if (File.Exists(d3d9Path) || File.Exists(d3d9Path + ".original"))
        {
            if (File.Exists(d3d9Path)) File.Delete(d3d9Path);
            AuxInstallService.SentinelRestore(d3d9Path);
            CrashReporter.Log($"[DgVoodooService.RemoveFromGame] Removed/restored {D3D9Dll} in '{installPath}'");
        }

        var confPath = Path.Combine(installPath, ConfFile);
        if (File.Exists(confPath))
        {
            File.Delete(confPath);
            CrashReporter.Log($"[DgVoodooService.RemoveFromGame] Deleted {ConfFile} in '{installPath}'");
        }
    }

    /// <summary>
    /// Returns true if RHI has deployed dgVoodoo2 to this game folder
    /// (detected by the presence of our sentinel marker D3D9.dll.original).
    /// </summary>
    public bool IsDeployed(string installPath)
        => File.Exists(Path.Combine(installPath, D3D9Dll + ".original"));

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetCachedZipPath(string version)
        => Path.Combine(CacheDir, $"dgVoodoo2_{version}.zip");

    /// <summary>
    /// Builds the dgVoodoo.conf content with settings required for Luma/DLSS5-Feeder use:
    /// - DisableAndPassThru=false (ships as true — must override or dgVoodoo does nothing)
    /// - OutputAPI=d3d11_fl11_0 (translate to DX11)
    /// - VideoCard=geforce_9800_gt (fixes dynamic shadows in BL2/MoH/TW2)
    /// - VRAM=1024 (default 256MB causes VRAM crashes)
    /// - dgVoodooWatermark=false
    /// </summary>
    private static string BuildConf() => @"; dgVoodoo2 configuration — managed by RHI
; Do not edit manually. RHI will overwrite this file on Luma reinstall.

[General]
OutputAPI = d3d11_fl11_0

[DirectX]
DisableAndPassThru = false
VideoCard = geforce_9800_gt
VRAM = 1024
dgVoodooWatermark = false
";
}
