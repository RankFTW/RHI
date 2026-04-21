using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads and maintains Lilium's HDR ReShade shaders from GitHub.
///
/// Release URL : https://api.github.com/repos/EndlesslyFlowering/ReShade_HDR_shaders/releases/latest
/// Assets      : one or more .7z files named chronologically (e.g. 2024-11-07.7z).
///               The filename is the version token — a newer release always sorts later
///               as an ordinal string comparison (ISO date prefix).
///
/// Local layout after extraction:
///   %LocalAppData%\RenoDXCommander\reshade\Shaders\   ← fx / fxh / hlsl files
///   %LocalAppData%\RenoDXCommander\reshade\Textures\  ← png / png textures
///
/// The last-downloaded asset filename is stored in settings.json under "LiliumShadersVersion"
/// so we can skip re-downloading when the release hasn't changed.
/// </summary>
public class LiliumShaderService : ILiliumShaderService
{
    private readonly HttpClient _http;
    private readonly GitHubETagCache _etagCache;

    public LiliumShaderService(HttpClient http, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;
    }

    private const string GhApiUrl = "https://api.github.com/repos/EndlesslyFlowering/ReShade_HDR_shaders/releases/latest";
    private const string SettingsKey = "LiliumShadersVersion";

    // Staging paths (inside the RenoDXCommander reshade folder)
    public static readonly string ShadersDir  = Path.Combine(AuxInstallService.RsStagingDir, "Shaders");
    public static readonly string TexturesDir = Path.Combine(AuxInstallService.RsStagingDir, "Textures");

    // DC global path — shaders go here when DC Mode is active
    public static readonly string DcReshadeDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Display_Commander", "Reshade");
    public static readonly string DcShadersDir  = Path.Combine(DcReshadeDir, "Shaders");
    public static readonly string DcTexturesDir = Path.Combine(DcReshadeDir, "Textures");

    // Game-local path name (non-DC mode)
    public const string GameReShadeShaders = "reshade-shaders";

    public static bool ShadersAvailable =>
        Directory.Exists(ShadersDir)  && Directory.EnumerateFiles(ShadersDir).Any() ||
        Directory.Exists(TexturesDir) && Directory.EnumerateFiles(TexturesDir).Any();

    /// <summary>
    /// Checks GitHub for the latest release. Downloads and extracts into the staging
    /// Shaders / Textures folders if the release is newer than what's already cached.
    /// Progress is optional — pass null for fire-and-forget background use.
    /// Never throws — all errors are logged via CrashReporter.
    /// </summary>
    public async Task EnsureLatestAsync(
        IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("Checking Lilium HDR shaders...");

            // ── 1. Fetch latest release metadata ─────────────────────────────────
            string json;
            try
            {
                json = await _etagCache.GetWithETagAsync(_http, GhApiUrl).ConfigureAwait(false);
                if (json == null)
                {
                    CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] GitHub API returned error");
                    return;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] GitHub API request failed — {ex.Message}");
                return;
            }

            // ── 2. Parse — find the first .7z asset ──────────────────────────────
            string? assetName = null;
            string? downloadUrl = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var assets = doc.RootElement.GetProperty("assets");
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                    {
                        assetName   = name;
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Failed to parse GitHub response — {ex.Message}");
                return;
            }

            if (assetName == null || downloadUrl == null)
            {
                CrashReporter.Log("[LiliumShaderService.EnsureLatestAsync] No .7z asset found in latest release");
                return;
            }

            // ── 3. Compare against stored version (filename = version token) ─────
            var stored = LoadStoredVersion();
            if (string.Compare(assetName, stored, StringComparison.OrdinalIgnoreCase) <= 0
                && ShadersAvailable)
            {
                CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Already up to date ({assetName})");
                progress?.Report($"Lilium shaders up to date ({assetName})");
                return;
            }

            progress?.Report($"Downloading Lilium shaders ({assetName})...");
            CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Downloading {assetName} from {downloadUrl}");

            // ── 4. Download the .7z into the downloads cache ─────────────────────
            Directory.CreateDirectory(DownloadPaths.Shaders);
            var cachePath = Path.Combine(DownloadPaths.Shaders, assetName);
            var tempPath  = cachePath + ".tmp";

            try
            {
                var dlResp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!dlResp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Download failed ({dlResp.StatusCode})");
                    return;
                }

                var total = dlResp.Content.Headers.ContentLength ?? -1L;
                long downloaded = 0;
                var buf = new byte[1024 * 1024]; // 1 MB

                using (var net  = await dlResp.Content.ReadAsStreamAsync())
                using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
                {
                    int read;
                    while ((read = await net.ReadAsync(buf)) > 0)
                    {
                        await file.WriteAsync(buf.AsMemory(0, read));
                        downloaded += read;
                        if (total > 0)
                            progress?.Report($"Downloading Lilium shaders... {downloaded / 1024} KB / {total / 1024} KB");
                    }
                }

                if (File.Exists(cachePath)) File.Delete(cachePath);
                File.Move(tempPath, cachePath);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch (Exception cleanupEx) { CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Failed to clean up temp file '{tempPath}' — {cleanupEx.Message}"); }
                CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Download exception — {ex.Message}");
                return;
            }

            // ── 5. Extract Shaders/ and Textures/ from the .7z ──────────────────
            progress?.Report("Extracting Lilium shaders...");
            try
            {
                Directory.CreateDirectory(ShadersDir);
                Directory.CreateDirectory(TexturesDir);

                using var archive = ArchiveFactory.Open(cachePath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;

                    // Normalise path separators so the logic works regardless of archive format
                    var key = entry.Key?.Replace('\\', '/') ?? "";

                    // Find the index of "/Shaders/" or "/Textures/" (or a root-level one)
                    // and use everything AFTER that segment as the relative path so the
                    // full subdirectory structure inside Shaders/ and Textures/ is preserved.
                    string? rootDir   = null;
                    string? relInRoot = null;

                    foreach (var (token, dir) in new[]
                    {
                        ("Shaders/",  ShadersDir),
                        ("Textures/", TexturesDir),
                    })
                    {
                        // Match "Shaders/" at the start of the key, or after any "/"
                        int idx = key.IndexOf("/" + token, StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0)
                        {
                            rootDir   = dir;
                            relInRoot = key.Substring(idx + 1 + token.Length); // path after "Shaders/"
                            break;
                        }
                        if (key.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                        {
                            rootDir   = dir;
                            relInRoot = key.Substring(token.Length);           // path after "Shaders/"
                            break;
                        }
                    }

                    if (rootDir == null || string.IsNullOrEmpty(relInRoot)) continue;

                    // Convert forward slashes to OS path separators and build destination
                    var relPath  = relInRoot.Replace('/', Path.DirectorySeparatorChar);
                    var destPath = Path.Combine(rootDir, relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    using var entryStream = entry.OpenEntryStream();
                    using var fileStream  = File.Create(destPath);
                    await entryStream.CopyToAsync(fileStream);
                }

                CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Extracted successfully from {assetName}");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Extraction failed — {ex.Message}");
                return;
            }

            // ── 6. Store the version so we skip next time ─────────────────────────
            SaveStoredVersion(assetName);
            progress?.Report($"Lilium shaders updated ({assetName})");
            CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Done. Version saved as {assetName}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[LiliumShaderService.EnsureLatestAsync] Unexpected error — {ex.Message}");
        }
    }

    // ── Shader deployment helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Copies staging Shaders/ and Textures/ to the DC global Reshade folder.
    /// Only copies if the destination folder does not already exist — preserves
    /// any shaders the user has already placed there manually.
    /// </summary>
    public void DeployToDcFolder()
    {
        if (!ShadersAvailable) return;
        DeployFolderIfAbsent(ShadersDir,  DcShadersDir);
        DeployFolderIfAbsent(TexturesDir, DcTexturesDir);
    }

    /// <summary>
    /// Copies staging Shaders/ and Textures/ into <c>gameDir\reshade-shaders\</c>.
    /// Only copies if the destination does not already exist.
    /// Call only when DC is NOT installed for this game.
    /// </summary>
    public void DeployToGameFolder(string gameDir)
    {
        if (!ShadersAvailable) return;
        var rsDir = Path.Combine(gameDir, GameReShadeShaders);
        DeployFolderIfAbsent(ShadersDir,  Path.Combine(rsDir, "Shaders"));
        DeployFolderIfAbsent(TexturesDir, Path.Combine(rsDir, "Textures"));
    }

    /// <summary>
    /// Removes <c>gameDir\reshade-shaders\</c> if it exists.
    /// Called when DC is installed to the same game folder.
    /// </summary>
    public void RemoveFromGameFolder(string gameDir)
    {
        var rsDir = Path.Combine(gameDir, GameReShadeShaders);
        if (Directory.Exists(rsDir))
        {
            try { Directory.Delete(rsDir, recursive: true); }
            catch (Exception ex) { CrashReporter.Log($"[LiliumShaderService.RemoveFromGameFolder] Failed to remove reshade-shaders — {ex.Message}"); }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Copies files from <paramref name="sourceDir"/> to <paramref name="destDir"/>
    /// on a per-file basis. A file is only copied when it does not already exist at
    /// the destination — so existing files placed by the user are never overwritten,
    /// but an empty or partially-populated destination folder will be filled in.
    /// This correctly handles the case where the Shaders / Textures folder exists
    /// in the DC Reshade directory but is empty or missing some files.
    /// </summary>
    private void DeployFolderIfAbsent(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;

        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel      = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, rel);
            if (File.Exists(destFile)) continue; // this specific file already present — skip
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            File.Copy(file, destFile, overwrite: false);
        }
    }

    // ── Settings persistence ──────────────────────────────────────────────────────

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "settings.json");

    private string? LoadStoredVersion()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            return d != null && d.TryGetValue(SettingsKey, out var v) ? v : null;
        }
        catch (Exception ex) { CrashReporter.Log($"[LiliumShaderService.LoadStoredVersion] Failed to load stored version — {ex.Message}"); return null; }
    }

    private void SaveStoredVersion(string version)
    {
        try
        {
            Dictionary<string, string> d = new();
            if (File.Exists(SettingsPath))
                d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath))
                    ?? new();
            d[SettingsKey] = version;
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { CrashReporter.Log($"[LiliumShaderService.SaveStoredVersion] Failed to save version — {ex.Message}"); }
    }
}
