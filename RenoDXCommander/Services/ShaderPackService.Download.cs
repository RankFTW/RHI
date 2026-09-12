// ShaderPackService.Download.cs — Pack download, extraction, version resolution, and extracted-file tracking
using System.Collections.Concurrent;
using System.Text.Json;
using SharpCompress.Archives;

namespace RenoDXCommander.Services;

public partial class ShaderPackService
{
    // Per-pack download lock — prevents the same pack from being downloaded concurrently
    // when EnsureLatestAsync is called from both the cache phase and background scan phase.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _packLocks = new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim GetPackLock(string packId)
        => _packLocks.GetOrAdd(packId, _ => new SemaphoreSlim(1, 1));

    // ── Main entry point ──────────────────────────────────────────────────────────

    /// <summary>
    /// Checks every pack. A pack is (re-)downloaded when:
    ///   • its version token has changed (new release / changed ETag), OR
    ///   • its cache zip is missing from the downloads folder, OR
    ///   • it has no extracted files in the staging Shaders/Textures tree.
    /// Failures in one pack are logged and skipped; others continue.
    /// </summary>
    public async Task EnsureLatestAsync(
        IProgress<string>? progress = null)
    {
        // Run all pack checks in parallel (each is an independent hash comparison or download)
        var tasks = _packs.Select(pack => Task.Run(async () =>
        {
            try { await EnsurePackAsync(pack, progress); }
            catch (Exception ex)
            { CrashReporter.Log($"[ShaderPackService.EnsureLatestAsync] Unexpected error for '{pack.Id}' — {ex.Message}"); }
        }));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Downloads and extracts only the specified packs and their transitive dependencies (on-demand).
    /// Packs that are already cached are skipped.
    /// </summary>
    public async Task EnsurePacksAsync(IEnumerable<string> packIds, IProgress<string>? progress = null)
    {
        var expandedIds = ExpandDependencies(packIds);
        var needed = _packs.Where(p => expandedIds.Contains(p.Id)).ToList();
        if (needed.Count == 0) return;

        var tasks = needed.Select(pack => Task.Run(async () =>
        {
            try { await EnsurePackAsync(pack, progress); }
            catch (Exception ex)
            { CrashReporter.Log($"[ShaderPackService.EnsurePacksAsync] Unexpected error for '{pack.Id}' — {ex.Message}"); }
        }));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Returns true if the given pack's files are already cached locally.
    /// </summary>
    public bool IsPackCached(string packId)
    {
        var pack = _packs.FirstOrDefault(p => p.Id.Equals(packId, StringComparison.OrdinalIgnoreCase));
        if (pack == null) return false;

        // Check if the cache zip exists and files are extracted
        var cacheFiles = Directory.Exists(DownloadPaths.Shaders)
            ? Directory.GetFiles(DownloadPaths.Shaders, $"shaders_{pack.Id}.*")
            : Array.Empty<string>();
        var cachePath = cacheFiles.FirstOrDefault(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        if (cachePath == null) return false;

        return PackHasExtractedFiles(pack.Id, cachePath);
    }

    // ── Per-pack download + extract ───────────────────────────────────────────────

    private async Task EnsurePackAsync(
        ShaderPack pack,
        IProgress<string>? progress)
    {
        var packLock = GetPackLock(pack.Id);
        if (!await packLock.WaitAsync(0))
        {
            // Another call is already downloading this pack — skip
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Skipped — already being downloaded by another task");
            return;
        }
        try
        {
        // ── Note: GhRelease packs always call the API to check for newer versions ──
        // The ETag cache ensures this is a cheap 304 Not Modified when nothing has changed.
        // DO NOT add an early-exit here based on stored version — it breaks update detection.

        string? downloadUrl;
        string versionToken;

        if (pack.Kind == SourceKind.GhRelease)
        {
            (downloadUrl, versionToken) = await ResolveGhRelease(pack);
            if (downloadUrl == null) return;
        }
        else
        {
            downloadUrl = pack.Url;
            versionToken = await ResolveDirectUrlVersion(pack);
        }

        // Derive the expected cache path so we can check physical existence
        var ext = Path.GetExtension(new Uri(downloadUrl).AbsolutePath);
        if (string.IsNullOrEmpty(ext)) ext = ".zip";
        var cachePath = Path.Combine(DownloadPaths.Shaders, $"shaders_{pack.Id}{ext}");

        var stored = LoadStoredVersion(pack.Id);
        var versionMatch = stored == versionToken && versionToken != "unknown";
        var cacheExists = File.Exists(cachePath);
        var hasExtracted = PackHasExtractedFiles(pack.Id, cachePath);

        if (versionMatch && cacheExists && hasExtracted)
        {
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Up to date ({versionToken})");
            return;
        }

        CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Need update — " +
            $"versionMatch={versionMatch} cacheExists={cacheExists} hasExtracted={hasExtracted}");

        // ── Download ──────────────────────────────────────────────────────────────
        progress?.Report($"Downloading {pack.DisplayName}...");
        CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Downloading from {downloadUrl}");

        Directory.CreateDirectory(DownloadPaths.Shaders);
        var tempPath = cachePath + ".tmp";

        try
        {
            var dlResp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!dlResp.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Download failed ({dlResp.StatusCode})");
                return;
            }

            var total = dlResp.Content.Headers.ContentLength ?? -1L;
            long received = 0;
            var buf = new byte[1024 * 1024]; // 1 MB

            using (var net = await dlResp.Content.ReadAsStreamAsync())
            using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
            {
                int read;
                while ((read = await net.ReadAsync(buf)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, read));
                    received += read;
                    if (total > 0)
                        progress?.Report($"Downloading {pack.DisplayName}... {received / 1024} KB / {total / 1024} KB");
                }
            }

            if (File.Exists(cachePath)) File.Delete(cachePath);
            File.Move(tempPath, cachePath);
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch (Exception cleanupEx) { CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] Temp file cleanup failed — {cleanupEx.Message}"); }
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Download exception — {ex.Message}");
            return;
        }

        // ── Extract ───────────────────────────────────────────────────────────────
        progress?.Report($"Extracting {pack.DisplayName}...");
        try
        {
            Directory.CreateDirectory(ShadersDir);
            Directory.CreateDirectory(TexturesDir);

            // Direct .fx/.fxh file — not an archive, just copy to Shaders folder
            var cacheExt = Path.GetExtension(cachePath);
            if (cacheExt.Equals(".fx", StringComparison.OrdinalIgnoreCase) || cacheExt.Equals(".fxh", StringComparison.OrdinalIgnoreCase))
            {
                var destPath = Path.Combine(ShadersDir, pack.Id, Path.GetFileName(cachePath));
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(cachePath, destPath, overwrite: true);
                RecordExtractedFiles(pack.Id, cachePath);
                CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Copied direct shader file");
            }
            else
            {
            using var archive = ArchiveFactory.Open(cachePath);
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;

                var key = entry.Key?.Replace('\\', '/') ?? "";

                string? rootDir = null;
                string? relInRoot = null;

                foreach (var (token, dir) in new[]
                {
                    ("Shaders/",  ShadersDir),
                    ("Textures/", TexturesDir),
                })
                {
                    int idx = key.IndexOf("/" + token, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        rootDir = dir;
                        relInRoot = key.Substring(idx + 1 + token.Length);
                        break;
                    }
                    if (key.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    {
                        rootDir = dir;
                        relInRoot = key.Substring(token.Length);
                        break;
                    }
                }

                if (rootDir == null || string.IsNullOrEmpty(relInRoot))
                {
                    // Fallback: if the file is a shader (.fx/.fxh) at the repo root (no Shaders/ subdirectory),
                    // treat it as a shader file. Common for single-file shader repos like LumaBoost.
                    var keyExt = Path.GetExtension(key);
                    if (keyExt.Equals(".fx", StringComparison.OrdinalIgnoreCase) || keyExt.Equals(".fxh", StringComparison.OrdinalIgnoreCase))
                    {
                        rootDir = ShadersDir;
                        // Strip the GitHub archive root folder (e.g. "LumaBoost-main/LumaBoost.fx" → "LumaBoost.fx")
                        var slashIdx = key.IndexOf('/');
                        relInRoot = slashIdx >= 0 ? key.Substring(slashIdx + 1) : key;
                    }
                    else
                    {
                        continue;
                    }
                }

                // Skip shaders that are known to fail compilation
                var fileName = Path.GetFileName(relInRoot);
                if (rootDir == ShadersDir && ExcludedShaderFiles.Contains(fileName)) continue;

                // Place each pack's files into a subdirectory named after the pack ID
                var relPath = Path.Combine(pack.Id, relInRoot.Replace('/', Path.DirectorySeparatorChar));
                var destPath = Path.Combine(rootDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using var entryStream = entry.OpenEntryStream();
                using var fileStream = File.Create(destPath);
                await entryStream.CopyToAsync(fileStream);
            }

            // Copy ReShade framework headers to the staging root so all packs can find them
            foreach (var header in ReShadeHeaders)
            {
                var packHeader = Path.Combine(ShadersDir, pack.Id, header);
                var rootHeader = Path.Combine(ShadersDir, header);
                if (File.Exists(packHeader))
                    try { File.Copy(packHeader, rootHeader, overwrite: true); }
                    catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] Failed to copy header '{header}' to root — {ex.Message}"); }
            }

            // Record which files this pack contributed so we can verify presence later
            RecordExtractedFiles(pack.Id, cachePath);
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Extracted successfully");
            } // end else (archive extraction)
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Extraction failed — {ex.Message}");
            return;
        }

        SaveStoredVersion(pack.Id, versionToken);
        ClearIncludeCache();
        progress?.Report($"{pack.DisplayName} updated.");
        CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Done. Version = {versionToken}");
        }
        finally { packLock.Release(); }
    }

    // ── Extracted-file tracking ───────────────────────────────────────────────────

    // Settings key that stores the list of files extracted by a pack.
    // Value is a JSON array of paths relative to RsStagingDir.
    private string FileListKey(string packId) => $"ShaderPack_{packId}_Files";

    // Settings key that stores the cache zip's last-write-time (UTC ticks) for a pack.
    // When the stored timestamp matches the current zip, we skip the expensive per-file
    // existence check in PackHasExtractedFiles.
    private string CacheTimestampKey(string packId) => $"ShaderPack_{packId}_CacheTimestamp";

    /// <summary>
    /// Returns true when every file previously recorded for this pack still exists
    /// on disk AND the cache zip itself exists. Either condition missing → re-extract.
    /// Uses a timestamp-based fast path: if the cache zip's last-write-time matches
    /// the stored timestamp, the per-file check is skipped entirely.
    /// </summary>
    private bool PackHasExtractedFiles(string packId, string cachePath)
    {
        if (!File.Exists(cachePath)) return false;
        _settingsLock.Wait();
        try
        {
            var d = ReadSettings();
            if (!d.TryGetValue(FileListKey(packId), out var json) || string.IsNullOrEmpty(json))
                return false;

            var currentTimestamp = File.GetLastWriteTimeUtc(cachePath).Ticks.ToString();
            if (d.TryGetValue(CacheTimestampKey(packId), out var storedTimestamp)
                && storedTimestamp == currentTimestamp)
            {
                return true;
            }

            var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            if (files.Count == 0) return false;
            if (!files.All(rel => File.Exists(Path.Combine(AuxInstallService.RsStagingDir, rel))))
                return false;

            // All files verified — write timestamp to cache
            var dCopy = new Dictionary<string, string>(d);
            dCopy[CacheTimestampKey(packId)] = currentTimestamp;
            try { WriteSettings(dCopy); }
            catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.PackHasExtractedFiles] Failed to save cache timestamp for '{packId}' — {ex.Message}"); }

            return true;
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.PackHasExtractedFiles] Failed to check extracted files for '{packId}' — {ex.Message}"); return false; }
        finally { _settingsLock.Release(); }
    }

    /// <summary>
    /// After a successful extraction, walks the archive again and records every
    /// extracted relative path so PackHasExtractedFiles can verify them next run.
    /// </summary>
    private void RecordExtractedFiles(string packId, string cachePath)
    {
        try
        {
            var files = new List<string>();

            // Direct .fx/.fxh file — not an archive, just record the single file
            var cacheExt = Path.GetExtension(cachePath);
            if (cacheExt.Equals(".fx", StringComparison.OrdinalIgnoreCase) || cacheExt.Equals(".fxh", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(Path.Combine("Shaders", packId, Path.GetFileName(cachePath)));
            }
            else
            {
            using var archive = ArchiveFactory.Open(cachePath);
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                var key = entry.Key?.Replace('\\', '/') ?? "";

                string? rootDir = null;
                string? relInRoot = null;
                foreach (var (token, dir) in new[]
                {
                    ("Shaders/",  ShadersDir),
                    ("Textures/", TexturesDir),
                })
                {
                    int idx = key.IndexOf("/" + token, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0) { rootDir = dir; relInRoot = key.Substring(idx + 1 + token.Length); break; }
                    if (key.StartsWith(token, StringComparison.OrdinalIgnoreCase)) { rootDir = dir; relInRoot = key.Substring(token.Length); break; }
                }
                if (rootDir == null || string.IsNullOrEmpty(relInRoot))
                {
                    // Fallback: root-level .fx/.fxh files (no Shaders/ subdirectory)
                    var fileExt = Path.GetExtension(key);
                    if (fileExt.Equals(".fx", StringComparison.OrdinalIgnoreCase) || fileExt.Equals(".fxh", StringComparison.OrdinalIgnoreCase))
                    {
                        rootDir = ShadersDir;
                        var slashIdx = key.IndexOf('/');
                        relInRoot = slashIdx >= 0 ? key.Substring(slashIdx + 1) : key;
                    }
                    else
                    {
                        continue;
                    }
                }
                // Skip excluded shaders so they are never recorded or deployed
                var recFileName = Path.GetFileName(relInRoot);
                if (rootDir == ShadersDir && ExcludedShaderFiles.Contains(recFileName)) continue;
                // Store as relative path from RsStagingDir, with pack subdirectory
                var subDir = rootDir == ShadersDir ? "Shaders" : "Textures";
                files.Add(Path.Combine(subDir, packId, relInRoot.Replace('/', Path.DirectorySeparatorChar)));
            }
            } // end else (archive recording)

            Dictionary<string, string> d = new();
            _settingsLock.Wait();
            try
            {
                d = new Dictionary<string, string>(ReadSettings());
                d[FileListKey(packId)] = JsonSerializer.Serialize(files);
                WriteSettings(d);
            }
            finally { _settingsLock.Release(); }
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.RecordExtractedFiles] Failed for '{packId}' — {ex.Message}"); }
    }

    // ── Source resolution ─────────────────────────────────────────────────────────

    private async Task<(string? url, string version)> ResolveGhRelease(
        ShaderPack pack)
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, pack.Url).ConfigureAwait(false);
            if (json == null)
            {
                CrashReporter.Log($"[ShaderPackService.ResolveGhRelease] [{pack.Id}] GitHub API returned error");
                return (null, "");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    bool matches = pack.AssetExt == null ||
                                   name.EndsWith(pack.AssetExt, StringComparison.OrdinalIgnoreCase);
                    if (matches && !string.IsNullOrEmpty(url))
                        return (url, name);
                }
            }

            // Fall back to source code zipball
            if (root.TryGetProperty("zipball_url", out var zb))
            {
                var tagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "unknown" : "unknown";
                var zbUrl = zb.GetString();
                if (!string.IsNullOrEmpty(zbUrl))
                    return (zbUrl, $"source_{tagName}.zip");
            }

            CrashReporter.Log($"[ShaderPackService.ResolveGhRelease] [{pack.Id}] No suitable asset found");
            return (null, "");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.ResolveGhRelease] [{pack.Id}] GH API error — {ex.Message}");
            return (null, "");
        }
    }

    private async Task<string> ResolveDirectUrlVersion(ShaderPack pack)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Head, pack.Url);
            req.Headers.Add("User-Agent", "RHI");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return "unknown";
            var etag = resp.Headers.ETag?.Tag;
            var modified = resp.Content.Headers.LastModified?.ToString("O");
            return etag ?? modified ?? "unknown";
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.ResolveDirectUrlVersion] Failed to resolve version for URL — {ex.Message}"); return "unknown"; }
    }

    // ── Settings persistence ──────────────────────────────────────────────────────

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "settings.json");

    /// <summary>Serializes all settings.json reads and writes to prevent concurrent access errors.</summary>
    private static readonly SemaphoreSlim _settingsLock = new(1, 1);

    /// <summary>In-memory cache of settings.json — loaded once on first read, invalidated on every write.</summary>
    private static Dictionary<string, string>? _settingsCache;

    /// <summary>Reads the settings dict — uses in-memory cache, only hits disk when cache is cold.</summary>
    private static Dictionary<string, string> ReadSettings()
    {
        if (_settingsCache != null) return _settingsCache;
        try
        {
            if (!File.Exists(SettingsPath)) return _settingsCache = new();
            _settingsCache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath)) ?? new();
            return _settingsCache;
        }
        catch { return _settingsCache = new(); }
    }

    /// <summary>Writes the settings dict to disk and updates the in-memory cache.</summary>
    private static void WriteSettings(Dictionary<string, string> d)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        _settingsCache = d; // update cache with written state
    }

    private string VersionKey(string packId) => $"ShaderPack_{packId}_Version";

    private string? LoadStoredVersion(string packId)
    {
        _settingsLock.Wait();
        try
        {
            var d = ReadSettings();
            return d.TryGetValue(VersionKey(packId), out var v) ? v : null;
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.LoadStoredVersion] Failed to load stored version for '{packId}' — {ex.Message}"); return null; }
        finally { _settingsLock.Release(); }
    }

    private void SaveStoredVersion(string packId, string version)
    {
        _settingsLock.Wait();
        try
        {
            var d = new Dictionary<string, string>(ReadSettings());
            d[VersionKey(packId)] = version;
            WriteSettings(d);
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.SaveStoredVersion] Failed to save version for '{packId}' — {ex.Message}"); }
        finally { _settingsLock.Release(); }
    }
}

// Re-open the partial class to add exclusion storage (same file pattern)
public partial class ShaderPackService
{
    // ── Per-file exclusion storage ────────────────────────────────────────────────

    private static string ExcludedFilesKey(string packId) => $"excluded_{packId}";

    /// <summary>Gets the set of shader filenames explicitly excluded by the user for this pack.</summary>
    public HashSet<string> GetExcludedFiles(string packId)
    {
        _settingsLock.Wait();
        try
        {
            var d = ReadSettings();
            if (!d.TryGetValue(ExcludedFilesKey(packId), out var json) || string.IsNullOrEmpty(json))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.GetExcludedFiles] Failed for '{packId}' — {ex.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        finally { _settingsLock.Release(); }
    }

    public void SetExcludedFiles(string packId, IEnumerable<string> excluded)
    {
        _settingsLock.Wait();
        try
        {
            var d = new Dictionary<string, string>(ReadSettings());
            var list = excluded.ToList();
            if (list.Count > 0)
                d[ExcludedFilesKey(packId)] = JsonSerializer.Serialize(list);
            else
                d.Remove(ExcludedFilesKey(packId));
            WriteSettings(d);
            CrashReporter.Log($"[ShaderPackService.SetExcludedFiles] Saved {list.Count} exclusion(s) for '{packId}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.SetExcludedFiles] Failed for '{packId}' — {ex.Message}");
        }
        finally { _settingsLock.Release(); }
    }

    /// <summary>
    /// Scans the pack's staging subfolder and records all found files in settings.json.
    /// Used after importing shader files from an archive.
    /// </summary>
    public void RecordExtractedFilesFromDir(string packId)
    {
        var packShadersDir = Path.Combine(ShadersDir, packId);
        var packTexturesDir = Path.Combine(TexturesDir, packId);
        var files = new List<string>();

        if (Directory.Exists(packShadersDir))
        {
            foreach (var file in Directory.EnumerateFiles(packShadersDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(packShadersDir, file);
                files.Add(Path.Combine("Shaders", packId, rel));
            }
        }
        if (Directory.Exists(packTexturesDir))
        {
            foreach (var file in Directory.EnumerateFiles(packTexturesDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(packTexturesDir, file);
                files.Add(Path.Combine("Textures", packId, rel));
            }
        }

        _settingsLock.Wait();
        try
        {
            var d = new Dictionary<string, string>(ReadSettings());
            d[FileListKey(packId)] = JsonSerializer.Serialize(files);
            // No version token — leave any existing version as-is
            WriteSettings(d);
            CrashReporter.Log($"[ShaderPackService.RecordExtractedFilesFromDir] Recorded {files.Count} file(s) for pack '{packId}'");
        }
        finally { _settingsLock.Release(); }
    }
}
