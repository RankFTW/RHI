using System.Text.Json;
using RenoDXCommander.Models;
using SharpCompress.Archives;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages addon lifecycle: fetch, parse, download, update, deploy.
/// Mirrors ShaderPackService pattern.
/// </summary>
public class AddonPackService : IAddonPackService
{
    private static readonly string AddonsIniUrl =
        "https://raw.githubusercontent.com/crosire/reshade-shaders/list/Addons.ini";

    private static readonly string StagingDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "addons");

    private static readonly string CachePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "addons_cache.ini");

    private static readonly string VersionsJsonPath =
        Path.Combine(StagingDir, "versions.json");

    private readonly HttpClient _http;
    private List<AddonEntry> _packs = new();

    // RenoDX DevKit addon — injected alongside Addons.ini entries
    private static readonly AddonEntry RenoDxDevKitEntry = new(
        SectionId: "renodx-devkit",
        PackageName: "RenoDX DevKit",
        PackageDescription: "RenoDX development tools addon for ReShade",
        DownloadUrl: null,
        DownloadUrl32: "https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-devkit.addon32",
        DownloadUrl64: "https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-devkit.addon64",
        RepositoryUrl: "https://github.com/clshortfuse/renodx",
        EffectInstallPath: null);

    // DLSS Fix addon — fixes DLSS frame generation locking to 2× in Unreal Engine games
    private static readonly AddonEntry DlssFixEntry = new(
        SectionId: "renodx-dlssfix",
        PackageName: "DLSS Fix",
        PackageDescription: "Makes ReShade draw on native game frames instead of frame gen frames. Also hides DLSS upscaling from ReShade.",
        DownloadUrl: "https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-dlssfix.addon64",
        DownloadUrl32: null,
        DownloadUrl64: "https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-dlssfix.addon64",
        RepositoryUrl: "https://github.com/clshortfuse/renodx/wiki/Mods#unreal-engine-",
        EffectInstallPath: null,
        DeployFileName: "renodx-dlssfix");

    public AddonPackService(HttpClient http) => _http = http;

    // ── Public properties ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<AddonEntry> AvailablePacks => _packs.AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyList<string> DownloadedAddonNames
    {
        get
        {
            if (!Directory.Exists(StagingDir))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(StagingDir)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f);
                    return ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase)
                        || ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase);
                })
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <inheritdoc />
    public bool IsDownloaded(string packageName)
    {
        if (!Directory.Exists(StagingDir))
            return false;

        var safeName = SanitizeFileName(packageName);
        return File.Exists(Path.Combine(StagingDir, safeName + ".addon32"))
            || File.Exists(Path.Combine(StagingDir, safeName + ".addon64"));
    }

    /// <inheritdoc />
    public void RemoveAddon(string packageName)
    {
        var safeName = SanitizeFileName(packageName);
        var file32 = Path.Combine(StagingDir, safeName + ".addon32");
        var file64 = Path.Combine(StagingDir, safeName + ".addon64");

        try
        {
            if (File.Exists(file32)) File.Delete(file32);
            if (File.Exists(file64)) File.Delete(file64);

            // Remove from version tracking
            var versions = LoadVersions();
            if (versions.Remove(packageName))
                SaveVersions(versions);

            CrashReporter.Log($"[AddonPackService.RemoveAddon] Removed '{packageName}' from staging.");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AddonPackService.RemoveAddon] Failed for '{packageName}' — {ex.Message}");
        }
    }

    // ── Core fetch / parse / cache ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task EnsureLatestAsync()
    {
        List<AddonEntry>? parsed = null;

        try
        {
            CrashReporter.Log("[AddonPackService.EnsureLatestAsync] Fetching Addons.ini...");
            var iniContent = await _http.GetStringAsync(AddonsIniUrl);

            parsed = AddonsIniParser.Parse(iniContent,
                warning => CrashReporter.Log($"[AddonPackService] Parse warning: {warning}"));

            // Cache the raw INI content for offline fallback
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                await File.WriteAllTextAsync(CachePath, iniContent);
                CrashReporter.Log("[AddonPackService.EnsureLatestAsync] Cached Addons.ini to disk.");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[AddonPackService.EnsureLatestAsync] Failed to write cache — {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AddonPackService.EnsureLatestAsync] Fetch failed — {ex.Message}");

            // Fall back to cached file
            if (File.Exists(CachePath))
            {
                try
                {
                    CrashReporter.Log("[AddonPackService.EnsureLatestAsync] Falling back to cached Addons.ini.");
                    var cachedContent = await File.ReadAllTextAsync(CachePath);
                    parsed = AddonsIniParser.Parse(cachedContent,
                        warning => CrashReporter.Log($"[AddonPackService] Cache parse warning: {warning}"));
                }
                catch (Exception cacheEx)
                {
                    CrashReporter.Log($"[AddonPackService.EnsureLatestAsync] Cache read failed — {cacheEx.Message}");
                }
            }
        }

        parsed ??= new List<AddonEntry>();

        // Append RenoDX DevKit entry (always present)
        if (!parsed.Any(e => e.PackageName.Equals(RenoDxDevKitEntry.PackageName, StringComparison.OrdinalIgnoreCase)))
            parsed.Add(RenoDxDevKitEntry);

        // Append DLSS Fix entry (always present)
        if (!parsed.Any(e => e.PackageName.Equals(DlssFixEntry.PackageName, StringComparison.OrdinalIgnoreCase)))
            parsed.Add(DlssFixEntry);

        _packs = parsed;
        CrashReporter.Log($"[AddonPackService.EnsureLatestAsync] Loaded {_packs.Count} addon entries.");
    }

    // ── Stub methods (implemented in subsequent tasks) ────────────────────────────

    /// <inheritdoc />
    public async Task DownloadAddonAsync(AddonEntry entry, IProgress<(string msg, double pct)>? progress = null, string? versionOverride = null)
    {
        try
        {
            Directory.CreateDirectory(StagingDir);
            var safeName = SanitizeFileName(entry.PackageName);

            // Collect URLs to download
            var downloads = new List<(string url, string extension)>();

            if (!string.IsNullOrEmpty(entry.DownloadUrl32) && !string.IsNullOrEmpty(entry.DownloadUrl64))
            {
                // Both 32/64 variants provided
                downloads.Add((entry.DownloadUrl32, ".addon32"));
                downloads.Add((entry.DownloadUrl64, ".addon64"));
            }
            else if (!string.IsNullOrEmpty(entry.DownloadUrl32))
            {
                downloads.Add((entry.DownloadUrl32, ".addon32"));
            }
            else if (!string.IsNullOrEmpty(entry.DownloadUrl64))
            {
                downloads.Add((entry.DownloadUrl64, ".addon64"));
            }
            else if (!string.IsNullOrEmpty(entry.DownloadUrl))
            {
                // Single URL — determine extension from URL or default to .addon64
                var ext = ClassifyUrlExtension(entry.DownloadUrl);
                downloads.Add((entry.DownloadUrl, ext));
            }

            if (downloads.Count == 0)
            {
                CrashReporter.Log($"[AddonPackService.DownloadAddonAsync] No download URLs for '{entry.PackageName}'");
                return;
            }

            // Use the caller-provided version if available (avoids ETag drift for /latest/ URLs)
            string? versionToken = versionOverride;

            for (int i = 0; i < downloads.Count; i++)
            {
                var (url, ext) = downloads[i];
                var pctBase = (double)i / downloads.Count * 100;
                var pctRange = 100.0 / downloads.Count;

                progress?.Report(($"Downloading {entry.PackageName}...", pctBase));
                CrashReporter.Log($"[AddonPackService.DownloadAddonAsync] Downloading '{entry.PackageName}' from {url}");

                if (IsZipUrl(url))
                {
                    // Download zip to temp, extract .addon32/.addon64 files
                    versionToken ??= await ResolveVersionToken(url);
                    await DownloadAndExtractZipAsync(url, safeName, progress, pctBase, pctRange);
                }
                else
                {
                    // Direct .addon32/.addon64 save
                    versionToken ??= await ResolveVersionToken(url);
                    var destPath = Path.Combine(StagingDir, safeName + ext);
                    await DownloadFileAsync(url, destPath, entry.PackageName, progress, pctBase, pctRange);
                }
            }

            // Track version
            versionToken ??= "unknown";
            SaveAddonVersion(entry.PackageName, versionToken, safeName);

            progress?.Report(($"{entry.PackageName} downloaded.", 100));
            CrashReporter.Log($"[AddonPackService.DownloadAddonAsync] '{entry.PackageName}' download complete. Version = {versionToken}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AddonPackService.DownloadAddonAsync] Failed for '{entry.PackageName}' — {ex.Message}");
            progress?.Report(($"❌ Download failed: {ex.Message}", 0));
        }
    }

    /// <inheritdoc />
    public async Task CheckAndUpdateAllAsync()
    {
        var versions = LoadVersions();
        var downloadedNames = DownloadedAddonNames;

        if (downloadedNames.Count == 0)
        {
            CrashReporter.Log("[AddonPackService.CheckAndUpdateAllAsync] No downloaded addons to update.");
            return;
        }

        CrashReporter.Log($"[AddonPackService.CheckAndUpdateAllAsync] Checking {downloadedNames.Count} addon(s) for updates...");

        foreach (var name in downloadedNames)
        {
            // Find matching AddonEntry in AvailablePacks
            var entry = _packs.FirstOrDefault(e =>
                SanitizeFileName(e.PackageName).Equals(name, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                CrashReporter.Log($"[AddonPackService.CheckAndUpdateAllAsync] No matching pack entry for '{name}', skipping.");
                continue;
            }

            try
            {
                // Pick a representative URL for version resolution
                var versionUrl = entry.DownloadUrl64
                    ?? entry.DownloadUrl32
                    ?? entry.DownloadUrl;

                if (string.IsNullOrEmpty(versionUrl))
                {
                    CrashReporter.Log($"[AddonPackService.CheckAndUpdateAllAsync] No download URL for '{entry.PackageName}', skipping.");
                    continue;
                }

                var remoteVersion = await ResolveVersionToken(versionUrl);
                var storedVersion = versions.TryGetValue(entry.PackageName, out var info)
                    ? info.Version
                    : null;

                if (string.Equals(remoteVersion, storedVersion, StringComparison.Ordinal))
                {
                    CrashReporter.Log($"[AddonPackService.CheckAndUpdateAllAsync] '{entry.PackageName}' is up to date (version: {storedVersion}).");
                    continue;
                }

                CrashReporter.Log($"[AddonPackService.CheckAndUpdateAllAsync] Update available for '{entry.PackageName}': {storedVersion} → {remoteVersion}. Downloading...");
                await DownloadAddonAsync(entry, versionOverride: remoteVersion);
                CrashReporter.Log($"[AddonPackService.CheckAndUpdateAllAsync] '{entry.PackageName}' updated to {remoteVersion}.");
            }
            catch (Exception ex)
            {
                // Retain existing version on failure
                CrashReporter.Log($"[AddonPackService.CheckAndUpdateAllAsync] Update failed for '{entry.PackageName}' — {ex.Message}. Retaining existing version.");
            }
        }

        CrashReporter.Log("[AddonPackService.CheckAndUpdateAllAsync] Update check complete.");
    }

    /// <inheritdoc />
    public void DeployAddonsForGame(string gameName, string installPath, bool is32Bit,
        bool useGlobalSet, List<string>? perGameSelection)
    {
        CrashReporter.Log($"[AddonPackService.DeployAddonsForGame] gameName={gameName}, installPath={installPath}, is32Bit={is32Bit}, useGlobalSet={useGlobalSet}");

        // 1. Determine active addon selection
        var activeSelection = useGlobalSet
            ? perGameSelection ?? new List<string>()  // caller passes EnabledGlobalAddons for global mode
            : perGameSelection ?? new List<string>();

        // 2. Determine correct bitness extension
        var bitnessExt = is32Bit ? ".addon32" : ".addon64";

        // 3. Create deploy directory if missing
        try
        {
            Directory.CreateDirectory(installPath);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AddonPackService.DeployAddonsForGame] Failed to create deploy directory '{installPath}' — {ex.Message}");
            return;
        }

        // 4. Deploy each addon in the active selection
        var deployedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packageName in activeSelection)
        {
            var safeName = SanitizeFileName(packageName);
            var stagingFile = Path.Combine(StagingDir, safeName + bitnessExt);

            if (!File.Exists(stagingFile))
            {
                CrashReporter.Log($"[AddonPackService.DeployAddonsForGame] Skipping '{packageName}' — no {bitnessExt} variant in staging.");
                continue;
            }

            // Use DeployFileName if the entry specifies one, otherwise use the sanitized package name
            var entry = _packs.FirstOrDefault(e =>
                e.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase));
            var deployName = entry?.DeployFileName ?? safeName;

            var destFile = Path.Combine(installPath, deployName + bitnessExt);
            try
            {
                File.Copy(stagingFile, destFile, overwrite: true);
                deployedFileNames.Add(deployName + bitnessExt);
                CrashReporter.Log($"[AddonPackService.DeployAddonsForGame] Deployed '{deployName}{bitnessExt}' to '{installPath}'.");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[AddonPackService.DeployAddonsForGame] Failed to copy '{deployName}{bitnessExt}' — {ex.Message}");
            }
        }

        // 5. Remove stale addon files — only remove files that RHI's addon manager could have deployed.
        // Build the set of all known addon filenames from the available packs list.
        var knownAddonFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in _packs)
        {
            var sn = SanitizeFileName(pack.PackageName);
            knownAddonFileNames.Add(sn + ".addon32");
            knownAddonFileNames.Add(sn + ".addon64");
            // Also track DeployFileName variants so they aren't removed as stale
            if (!string.IsNullOrEmpty(pack.DeployFileName))
            {
                knownAddonFileNames.Add(pack.DeployFileName + ".addon32");
                knownAddonFileNames.Add(pack.DeployFileName + ".addon64");
            }
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(installPath))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileName(file);

                // Only touch files that match a known addon manager pack name
                if (!knownAddonFileNames.Contains(fileName))
                    continue;

                // Don't remove files we just deployed
                if (deployedFileNames.Contains(fileName))
                    continue;

                try
                {
                    File.Delete(file);
                    CrashReporter.Log($"[AddonPackService.DeployAddonsForGame] Removed stale addon '{fileName}' from '{installPath}'.");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[AddonPackService.DeployAddonsForGame] Failed to remove stale addon '{fileName}' — {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AddonPackService.DeployAddonsForGame] Failed to enumerate deploy directory for stale removal — {ex.Message}");
        }

        CrashReporter.Log($"[AddonPackService.DeployAddonsForGame] Deployment complete for '{gameName}'. Deployed {deployedFileNames.Count} addon(s).");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // ── Download helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a file from <paramref name="url"/> to <paramref name="destPath"/>,
    /// reporting progress through the IProgress callback.
    /// </summary>
    private async Task DownloadFileAsync(string url, string destPath, string displayName,
        IProgress<(string msg, double pct)>? progress, double pctBase, double pctRange)
    {
        var tempPath = destPath + ".tmp";
        try
        {
            var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[AddonPackService.DownloadFileAsync] HTTP {resp.StatusCode} for {url}");
                throw new HttpRequestException($"HTTP {resp.StatusCode}");
            }

            var total = resp.Content.Headers.ContentLength ?? -1L;
            long received = 0;
            var buf = new byte[1024 * 1024];

            using (var net = await resp.Content.ReadAsStreamAsync())
            using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                int read;
                while ((read = await net.ReadAsync(buf)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, read));
                    received += read;
                    if (total > 0)
                    {
                        var filePct = pctBase + (double)received / total * pctRange;
                        progress?.Report(($"Downloading {displayName}... {received / 1024} KB / {total / 1024} KB", filePct));
                    }
                }
            }

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tempPath, destPath);
        }
        catch
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Downloads a zip from <paramref name="url"/>, extracts .addon32/.addon64 files
    /// to the staging directory with the given <paramref name="safeName"/> prefix.
    /// </summary>
    private async Task DownloadAndExtractZipAsync(string url, string safeName,
        IProgress<(string msg, double pct)>? progress, double pctBase, double pctRange)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"rhi_addon_{safeName}_{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(url, tempZip, safeName, progress, pctBase, pctRange * 0.8);

            progress?.Report(($"Extracting {safeName}...", pctBase + pctRange * 0.8));

            using var archive = ArchiveFactory.Open(tempZip);
            foreach (var archiveEntry in archive.Entries)
            {
                if (archiveEntry.IsDirectory) continue;
                var key = archiveEntry.Key?.Replace('\\', '/') ?? "";
                var fileName = Path.GetFileName(key);
                var ext = Path.GetExtension(fileName);

                if (!ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destPath = Path.Combine(StagingDir, safeName + ext);
                using var entryStream = archiveEntry.OpenEntryStream();
                using var fileStream = File.Create(destPath);
                await entryStream.CopyToAsync(fileStream);

                CrashReporter.Log($"[AddonPackService.DownloadAndExtractZipAsync] Extracted '{fileName}' → '{destPath}'");
            }

            progress?.Report(($"Extracted {safeName}.", pctBase + pctRange));
        }
        finally
        {
            if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch { }
        }
    }

    // ── URL classification ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the URL points to a .zip archive.
    /// </summary>
    internal static bool IsZipUrl(string url)
    {
        var path = GetUrlPath(url);
        return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Classifies a URL's extension. Returns ".addon32", ".addon64", or ".addon64" as default
    /// for single-URL entries.
    /// </summary>
    internal static string ClassifyUrlExtension(string url)
    {
        var path = GetUrlPath(url);
        if (path.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase)) return ".addon32";
        if (path.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)) return ".addon64";
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return ".zip";
        return ".addon64"; // default for ambiguous single URLs
    }

    private static string GetUrlPath(string url)
    {
        try { return new Uri(url).AbsolutePath; }
        catch { return url; }
    }

    // ── Version tracking (versions.json) ──────────────────────────────────────────

    /// <summary>
    /// Resolves a version token for a download URL. For GitHub release URLs,
    /// extracts the tag from the URL path. For other URLs, uses a HEAD request
    /// to get ETag or Last-Modified.
    /// </summary>
    private async Task<string> ResolveVersionToken(string url)
    {
        try
        {
            // GitHub release download URLs contain the tag: .../releases/download/{tag}/...
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/');
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("download", StringComparison.OrdinalIgnoreCase) && i > 0 &&
                    segments[i - 1].Equals("releases", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[i + 1]; // the tag name
                }
            }

            // Fall back to HEAD request for ETag/Last-Modified
            var req = new HttpRequestMessage(HttpMethod.Head, url);
            req.Headers.Add("User-Agent", "RHI");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return "unknown";
            var etag = resp.Headers.ETag?.Tag;
            var modified = resp.Content.Headers.LastModified?.ToString("O");
            return etag ?? modified ?? "unknown";
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AddonPackService.ResolveVersionToken] Failed — {ex.Message}");
            return "unknown";
        }
    }

    /// <summary>
    /// Reads the versions.json file from the staging directory.
    /// Returns an empty dictionary if the file doesn't exist or is corrupted.
    /// </summary>
    internal static Dictionary<string, AddonVersionInfo> LoadVersions()
    {
        try
        {
            if (!File.Exists(VersionsJsonPath)) return new();
            var json = File.ReadAllText(VersionsJsonPath);
            return JsonSerializer.Deserialize<Dictionary<string, AddonVersionInfo>>(json) ?? new();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AddonPackService.LoadVersions] Failed to read versions.json — {ex.Message}");
            return new();
        }
    }

    /// <summary>
    /// Writes the versions dictionary to versions.json in the staging directory.
    /// </summary>
    internal static void SaveVersions(Dictionary<string, AddonVersionInfo> versions)
    {
        try
        {
            Directory.CreateDirectory(StagingDir);
            var json = JsonSerializer.Serialize(versions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(VersionsJsonPath, json);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AddonPackService.SaveVersions] Failed to write versions.json — {ex.Message}");
        }
    }

    /// <summary>
    /// Saves version info for a single addon into versions.json.
    /// </summary>
    private void SaveAddonVersion(string packageName, string version, string safeName)
    {
        var versions = LoadVersions();
        var has32 = File.Exists(Path.Combine(StagingDir, safeName + ".addon32"));
        var has64 = File.Exists(Path.Combine(StagingDir, safeName + ".addon64"));

        versions[packageName] = new AddonVersionInfo
        {
            Version = version,
            LastChecked = DateTime.UtcNow.ToString("O"),
            FileName32 = has32 ? safeName + ".addon32" : null,
            FileName64 = has64 ? safeName + ".addon64" : null
        };

        SaveVersions(versions);
    }

    /// <summary>
    /// Reads the stored version for a specific addon from versions.json.
    /// </summary>
    internal static string? LoadAddonVersion(string packageName)
    {
        var versions = LoadVersions();
        return versions.TryGetValue(packageName, out var info) ? info.Version : null;
    }

    /// <summary>
    /// Converts a package name to a safe filename by replacing invalid characters.
    /// </summary>
    internal static string SanitizeFileName(string packageName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[packageName.Length];
        for (int i = 0; i < packageName.Length; i++)
            sanitized[i] = Array.IndexOf(invalid, packageName[i]) >= 0 ? '_' : packageName[i];
        return new string(sanitized);
    }

    // Expose paths for testing and other services
    internal static string GetStagingDir() => StagingDir;
    internal static string GetCachePath() => CachePath;
    internal static string GetVersionsJsonPath() => VersionsJsonPath;
}

/// <summary>
/// Version tracking info for a single addon, stored in versions.json.
/// </summary>
public class AddonVersionInfo
{
    public string? Version { get; set; }
    public string? LastChecked { get; set; }
    public string? FileName32 { get; set; }
    public string? FileName64 { get; set; }
}
