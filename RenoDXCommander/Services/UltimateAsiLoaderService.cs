using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages Ultimate ASI Loader — staging, download, install, uninstall, and update detection.
/// Supports both 32-bit (Ultimate-ASI-Loader.zip) and 64-bit (Ultimate-ASI-Loader_x64.zip).
/// GitHub: https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases
/// </summary>
public class UltimateAsiLoaderService
{
    public const string AddonType       = "UltimateAsiLoader";
    public const string DefaultDllName  = "dinput8.dll";
    public const string GitHubApiUrl    = "https://api.github.com/repos/ThirteenAG/Ultimate-ASI-Loader/releases/latest";

    // Asset filenames inside each zip
    private const string InnerDllName   = "dinput8.dll";
    private const string Zip32Asset     = "Ultimate-ASI-Loader.zip";
    private const string Zip64Asset     = "Ultimate-ASI-Loader_x64.zip";

    // DLL names supported by UAL (bitness-filtered at call sites)
    public static readonly IReadOnlyList<string> Win32Names = new[]
    {
        "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll",
        "ddraw.dll", "dinput.dll", "dinput8.dll", "dsound.dll",
        "msacm32.dll", "msvfw32.dll",
        "version.dll", "wininet.dll", "winmm.dll", "winhttp.dll",
        "xlive.dll", "binkw32.dll", "bink2w32.dll", "vorbisFile.dll",
        "xinput1_1.dll", "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll",
        "xinput9_1_0.dll", "xinputuap.dll",
    };

    public static readonly IReadOnlyList<string> Win64Names = new[]
    {
        "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll",
        "dinput8.dll", "dsound.dll",
        "version.dll", "wininet.dll", "winmm.dll", "winhttp.dll",
        "binkw64.dll", "bink2w64.dll",
        "xinput1_1.dll", "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll",
        "xinput9_1_0.dll", "xinputuap.dll",
    };

    /// <summary>Names flagged as Recommended for most games.</summary>
    public static readonly IReadOnlyList<string> RecommendedNames = new[]
        { "version.dll", "winmm.dll", "winhttp.dll" };

    /// <summary>Names that may conflict with RHI-managed files (ReShade / OptiScaler).</summary>
    public static readonly IReadOnlyList<string> RhiConflictNames = new[]
        { "dxgi.dll", "d3d11.dll", "d3d12.dll", "d3d9.dll" };

    private readonly HttpClient      _http;
    private readonly ICrashReporter  _crashReporter;
    private readonly IAuxInstallService _auxInstaller;

    private readonly string _stagingDir32;
    private readonly string _stagingDir64;
    private readonly string _versionFile32;
    private readonly string _versionFile64;

    public UltimateAsiLoaderService(HttpClient http, ICrashReporter crashReporter, IAuxInstallService auxInstaller)
    {
        _http          = http;
        _crashReporter = crashReporter;
        _auxInstaller  = auxInstaller;

        var appData    = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _stagingDir32  = Path.Combine(appData, "RHI", "ual");
        _stagingDir64  = Path.Combine(appData, "RHI", "ual64");
        _versionFile32 = Path.Combine(_stagingDir32, "version.txt");
        _versionFile64 = Path.Combine(_stagingDir64, "version.txt");
    }

    // ── State ─────────────────────────────────────────────────────────────────

    public string? StagedVersion32 => File.Exists(_versionFile32) ? File.ReadAllText(_versionFile32).Trim() : null;
    public string? StagedVersion64 => File.Exists(_versionFile64) ? File.ReadAllText(_versionFile64).Trim() : null;

    public bool IsStagingReady32 => File.Exists(Path.Combine(_stagingDir32, DefaultDllName));
    public bool IsStagingReady64 => File.Exists(Path.Combine(_stagingDir64, DefaultDllName));

    public bool   HasUpdate       { get; private set; }
    public string? LatestVersion  { get; private set; }
    public string? ReleaseNotes   { get; private set; }

    // ── Update check ─────────────────────────────────────────────────────────

    public async Task CheckForUpdateAsync()
    {
        var (version, _, body) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version))
        {
            _crashReporter.Log("[UltimateAsiLoaderService.CheckForUpdateAsync] Could not resolve latest version");
            return;
        }

        LatestVersion = version;
        ReleaseNotes  = body;

        // HasUpdate if either bitness variant needs updating
        var cur32 = StagedVersion32;
        var cur64 = StagedVersion64;
        HasUpdate = !string.Equals(cur32, version, StringComparison.OrdinalIgnoreCase)
                 || !string.Equals(cur64, version, StringComparison.OrdinalIgnoreCase);

        _crashReporter.Log($"[UltimateAsiLoaderService.CheckForUpdateAsync] 32={cur32 ?? "(none)"} 64={cur64 ?? "(none)"} Remote={version} HasUpdate={HasUpdate}");
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    public async Task EnsureStagingAsync(bool is32Bit, IProgress<(string msg, double pct)>? progress = null)
    {
        var stagingDir  = is32Bit ? _stagingDir32  : _stagingDir64;
        var versionFile = is32Bit ? _versionFile32 : _versionFile64;
        var assetName   = is32Bit ? Zip32Asset      : Zip64Asset;
        bool isReady    = is32Bit ? IsStagingReady32 : IsStagingReady64;
        string? staged  = is32Bit ? StagedVersion32  : StagedVersion64;

        if (isReady && !HasUpdate)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] {(is32Bit ? "32" : "64")}-bit already valid — skipping");
            return;
        }

        Directory.CreateDirectory(stagingDir);
        progress?.Report(("Downloading ASI Loader...", 10));

        var (version, assets, body) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || assets == null || !assets.TryGetValue(assetName, out var downloadUrl))
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] Could not resolve download URL for {assetName}");
            return;
        }

        progress?.Report(("Downloading ASI Loader...", 30));

        try
        {
            var zipBytes = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            progress?.Report(("Extracting...", 70));

            // Extract dinput8.dll from the zip
            using var ms  = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.GetEntry(InnerDllName)
                     ?? zip.Entries.FirstOrDefault(e => e.Name.Equals(InnerDllName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] {InnerDllName} not found in archive");
                return;
            }

            var destPath = Path.Combine(stagingDir, DefaultDllName);
            using var src  = entry.Open();
            using var dest = File.Create(destPath);
            await src.CopyToAsync(dest).ConfigureAwait(false);

            File.WriteAllText(versionFile, version);
            ReleaseNotes = body;
            HasUpdate    = false;
            _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] Staged v{version} ({(is32Bit ? "32" : "64")}-bit) to {stagingDir}");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] Failed — {ex.Message}");
        }

        progress?.Report(("ASI Loader ready", 100));
    }

    // ── Install / Uninstall ───────────────────────────────────────────────────

    /// <summary>
    /// Installs UAL to a game folder as <paramref name="dllName"/>.
    /// If the target name already exists and is NOT an RHI-managed file,
    /// renames it to &lt;name&gt;Hooked.dll first (Hooked chaining).
    /// Returns (success, hookedOriginal) where hookedOriginal is the original filename if chaining happened.
    /// </summary>
    public async Task<(bool success, string? hookedOriginal)> InstallAsync(
        GameCardViewModel card,
        string dllName,
        IProgress<(string msg, double pct)>? progress = null)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return (false, null);

        await EnsureStagingAsync(card.Is32Bit, progress).ConfigureAwait(false);

        bool isReady = card.Is32Bit ? IsStagingReady32 : IsStagingReady64;
        if (!isReady)
        {
            _crashReporter.Log("[UltimateAsiLoaderService.InstallAsync] Staging not ready");
            return (false, null);
        }

        var stagingDir = card.Is32Bit ? _stagingDir32 : _stagingDir64;
        var src        = Path.Combine(stagingDir, DefaultDllName);
        var destPath   = Path.Combine(card.InstallPath, dllName);

        // Hooked chaining — if the target DLL exists and is NOT ours, rename it first
        string? hookedOriginal = null;
        if (File.Exists(destPath) && !IsRhiManagedFile(card, dllName))
        {
            var ext        = Path.GetExtension(dllName);           // ".dll"
            var nameNoExt  = Path.GetFileNameWithoutExtension(dllName); // "version"
            var hookedName = $"{nameNoExt}Hooked{ext}";            // "versionHooked.dll"
            var hookedPath = Path.Combine(card.InstallPath, hookedName);

            try
            {
                File.Move(destPath, hookedPath, overwrite: true);
                hookedOriginal = hookedName;
                _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Chained '{dllName}' → '{hookedName}' for '{card.GameName}'");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Chain rename failed — {ex.Message}");
                return (false, null);
            }
        }

        progress?.Report(("Deploying ASI Loader...", 80));

        try
        {
            File.Copy(src, destPath, overwrite: true);
            _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Installed as '{dllName}' in '{card.InstallPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Copy failed — {ex.Message}");
            // Undo hooked rename if the install itself failed
            if (hookedOriginal != null)
            {
                try { File.Move(Path.Combine(card.InstallPath, hookedOriginal), destPath, overwrite: true); } catch { }
                hookedOriginal = null;
            }
            return (false, null);
        }

        // Persist tracking record
        var record = new AuxInstalledRecord
        {
            GameName    = card.GameName,
            InstallPath = card.InstallPath,
            Store       = card.Source ?? "",
            AddonType   = AddonType,
            InstalledAs = dllName,
            InstalledAt = DateTime.UtcNow,
            SourceUrl   = GitHubApiUrl,
        };
        _auxInstaller.SaveAuxRecord(record);

        progress?.Report(("ASI Loader installed!", 100));
        return (true, hookedOriginal);
    }

    /// <summary>
    /// Uninstalls UAL from a game folder. Restores Hooked backup if present.
    /// </summary>
    public bool Uninstall(GameCardViewModel card)
    {
        var record = _auxInstaller.FindRecord(card.GameName, card.InstallPath ?? "", AddonType);
        if (record == null)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] No record for '{card.GameName}'");
            return false;
        }

        var dllPath = Path.Combine(record.InstallPath, record.InstalledAs);
        try
        {
            if (File.Exists(dllPath))
                File.Delete(dllPath);

            // Restore Hooked backup if it exists
            var ext        = Path.GetExtension(record.InstalledAs);
            var nameNoExt  = Path.GetFileNameWithoutExtension(record.InstalledAs);
            var hookedPath = Path.Combine(record.InstallPath, $"{nameNoExt}Hooked{ext}");
            if (File.Exists(hookedPath))
            {
                File.Move(hookedPath, dllPath, overwrite: true);
                _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] Restored '{nameNoExt}Hooked{ext}' → '{record.InstalledAs}' for '{card.GameName}'");
            }

            _auxInstaller.RemoveRecord(record);
            _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] Removed from '{record.InstallPath}'");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Auto-update (redeploy to already-installed games) ─────────────────────

    /// <summary>
    /// After EnsureStagingAsync fetches a new version, redeploy to all games that have UAL installed.
    /// </summary>
    public async Task AutoUpdateInstalledGamesAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        if (!HasUpdate) return;

        var installed = cards.Where(c =>
            !string.IsNullOrEmpty(c.InstallPath)
            && _auxInstaller.FindRecord(c.GameName, c.InstallPath, AddonType) != null).ToList();

        foreach (var card in installed)
        {
            var record = _auxInstaller.FindRecord(card.GameName, card.InstallPath!, AddonType);
            if (record == null) continue;

            var stagingDir = card.Is32Bit ? _stagingDir32 : _stagingDir64;
            var src        = Path.Combine(stagingDir, DefaultDllName);
            var dest       = Path.Combine(record.InstallPath, record.InstalledAs);

            if (!File.Exists(src)) continue;

            try
            {
                File.Copy(src, dest, overwrite: true);
                _crashReporter.Log($"[UltimateAsiLoaderService.AutoUpdate] Updated '{card.GameName}' ({record.InstalledAs})");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[UltimateAsiLoaderService.AutoUpdate] Failed for '{card.GameName}' — {ex.Message}");
            }
        }
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    public bool IsInstalledIn(string gameName, string? installPath)
        => !string.IsNullOrEmpty(installPath)
           && _auxInstaller.FindRecord(gameName, installPath, AddonType) != null;

    public string? InstalledDllName(string gameName, string? installPath)
        => string.IsNullOrEmpty(installPath)
            ? null
            : _auxInstaller.FindRecord(gameName, installPath, AddonType)?.InstalledAs;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns true if the given DLL name at this game's path is managed by RHI (ReShade/OptiScaler/DC).</summary>
    private static bool IsRhiManagedFile(GameCardViewModel card, string dllName)
    {
        if (!string.IsNullOrEmpty(card.RsInstalledFile)
            && card.RsInstalledFile.Equals(dllName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(card.OsInstalledFile)
            && card.OsInstalledFile.Equals(dllName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(card.DcInstalledFile)
            && card.DcInstalledFile.Equals(dllName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private async Task<(string? version, Dictionary<string, string>? assets, string? body)> FetchLatestReleaseInfoAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            req.Headers.Add("User-Agent", "RHI");
            req.Headers.Add("Accept", "application/vnd.github+json");
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[UltimateAsiLoaderService] GitHub API returned {resp.StatusCode}");
                return (null, null, null);
            }

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag     = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var body    = root.TryGetProperty("body",     out var bodyEl) ? bodyEl.GetString() : null;
            var version = tag?.TrimStart('v');  // "v9.7.4" → "9.7.4"

            if (string.IsNullOrEmpty(version)) return (null, null, null);

            // Build asset URL map
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("assets", out var assetsEl))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameEl)
                        && asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        var name = nameEl.GetString();
                        var url  = urlEl.GetString();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                            assets[name] = url;
                    }
                }
            }

            _crashReporter.Log($"[UltimateAsiLoaderService.FetchLatestReleaseInfoAsync] Latest = {version}");
            return (version, assets, body);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.FetchLatestReleaseInfoAsync] Failed — {ex.Message}");
            return (null, null, null);
        }
    }
}
