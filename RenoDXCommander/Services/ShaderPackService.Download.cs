// ShaderPackService.Download.cs — Pack download, extraction, version resolution, and extracted-file tracking
using System.Text.Json;
using SharpCompress.Archives;

namespace RenoDXCommander.Services;

public partial class ShaderPackService
{
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
        var tasks = Packs.Select(pack => Task.Run(async () =>
        {
            try { await EnsurePackAsync(pack, progress); }
            catch (Exception ex)
            { CrashReporter.Log($"[ShaderPackService.EnsureLatestAsync] Unexpected error for '{pack.Id}' — {ex.Message}"); }
        }));
        await Task.WhenAll(tasks);
    }

    // ── Per-pack download + extract ───────────────────────────────────────────────

    private async Task EnsurePackAsync(
        ShaderPack pack,
        IProgress<string>? progress)
    {
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

                if (rootDir == null || string.IsNullOrEmpty(relInRoot)) continue;

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
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Extraction failed — {ex.Message}");
            return;
        }

        SaveStoredVersion(pack.Id, versionToken);
        progress?.Report($"{pack.DisplayName} updated.");
        CrashReporter.Log($"[ShaderPackService.EnsurePackAsync] [{pack.Id}] Done. Version = {versionToken}");
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
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            if (d == null || !d.TryGetValue(FileListKey(packId), out var json) || string.IsNullOrEmpty(json))
                return false; // no record → treat as missing

            // Fast path: if the cache zip hasn't changed since we last verified all files,
            // skip the expensive per-file existence check.
            var currentTimestamp = File.GetLastWriteTimeUtc(cachePath).Ticks.ToString();
            if (d.TryGetValue(CacheTimestampKey(packId), out var storedTimestamp)
                && storedTimestamp == currentTimestamp)
            {
                return true;
            }

            var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            if (files.Count == 0) return false;
            // All recorded files must still exist
            if (!files.All(rel => File.Exists(Path.Combine(AuxInstallService.RsStagingDir, rel))))
                return false;

            // All files verified — store the cache zip timestamp so next check is instant
            d[CacheTimestampKey(packId)] = currentTimestamp;
            try
            {
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.PackHasExtractedFiles] Failed to save cache timestamp for '{packId}' — {ex.Message}"); }

            return true;
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.PackHasExtractedFiles] Failed to check extracted files for '{packId}' — {ex.Message}"); return false; }
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
                if (rootDir == null || string.IsNullOrEmpty(relInRoot)) continue;
                // Skip excluded shaders so they are never recorded or deployed
                var recFileName = Path.GetFileName(relInRoot);
                if (rootDir == ShadersDir && ExcludedShaderFiles.Contains(recFileName)) continue;
                // Store as relative path from RsStagingDir, with pack subdirectory
                var subDir = rootDir == ShadersDir ? "Shaders" : "Textures";
                files.Add(Path.Combine(subDir, packId, relInRoot.Replace('/', Path.DirectorySeparatorChar)));
            }

            Dictionary<string, string> d = new();
            if (File.Exists(SettingsPath))
                d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath)) ?? new();
            d[FileListKey(packId)] = JsonSerializer.Serialize(files);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
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

    private string VersionKey(string packId) => $"ShaderPack_{packId}_Version";

    private string? LoadStoredVersion(string packId)
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            return d != null && d.TryGetValue(VersionKey(packId), out var v) ? v : null;
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.LoadStoredVersion] Failed to load stored version for '{packId}' — {ex.Message}"); return null; }
    }

    private void SaveStoredVersion(string packId, string version)
    {
        try
        {
            Dictionary<string, string> d = new();
            if (File.Exists(SettingsPath))
                d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath)) ?? new();
            d[VersionKey(packId)] = version;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.SaveStoredVersion] Failed to save version for '{packId}' — {ex.Message}"); }
    }
}
