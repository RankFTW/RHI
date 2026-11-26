using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Fetches the latest UL release info (version + download URL) from GitHub.
    /// Populates _latestUlVersion and _latestUlDownloadUrl.
    /// </summary>

    /// <summary>Persists UL installed version to the meta file.</summary>
    private static void SaveUlMeta(string version, bool is32Bit)
    {
        try
        {
            var cleanVersion = version.TrimStart('v', 'V');

            // Read existing meta to preserve the other bitness entry
            Dictionary<string, object>? meta = null;
            if (File.Exists(UlMetaPath))
            {
                try
                {
                    var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                        File.ReadAllText(UlMetaPath));
                    if (existing != null) meta = existing;
                }
                catch { /* corrupt file — start fresh */ }
            }
            meta ??= new Dictionary<string, object>();

            var key = is32Bit ? "InstalledVersion32" : "InstalledVersion64";
            meta[key] = cleanVersion;
            meta["UpdatedAt"] = DateTime.UtcNow.ToString("o");

            // Migrate: also write legacy key so older builds still work
            meta["InstalledVersion"] = cleanVersion;

            Directory.CreateDirectory(Path.GetDirectoryName(UlMetaPath)!);
            FileHelper.WriteAllTextAtomic(UlMetaPath, System.Text.Json.JsonSerializer.Serialize(meta,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "MainViewModel.SaveUlMeta");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.SaveUlMeta] Failed to save UL metadata — {ex.Message}");
        }
    }

    /// <summary>Reads the installed UL version for the given bitness from the meta file, or null if not found.</summary>
    private static string? ReadUlInstalledVersion(bool is32Bit)
    {
        try
        {
            if (!File.Exists(UlMetaPath)) return null;
            var metaJson = File.ReadAllText(UlMetaPath);
            var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(metaJson);
            if (meta == null) return null;

            var key = is32Bit ? "InstalledVersion32" : "InstalledVersion64";
            if (meta.TryGetValue(key, out var verEl))
                return verEl.GetString()?.TrimStart('v', 'V');
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.ReadUlInstalledVersion] Failed — {ex.Message}");
        }
        return null;
    }

    /// <summary>Persists DC installed version to the meta file.</summary>
    private static void SaveDcMeta(string version, bool is32Bit)
    {
        try
        {
            var cleanVersion = version.TrimStart('v', 'V');

            Dictionary<string, object>? meta = null;
            if (File.Exists(DcMetaPath))
            {
                try
                {
                    var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                        File.ReadAllText(DcMetaPath));
                    if (existing != null) meta = existing;
                }
                catch { /* corrupt file — start fresh */ }
            }
            meta ??= new Dictionary<string, object>();

            var key = is32Bit ? "InstalledVersion32" : "InstalledVersion64";
            meta[key] = cleanVersion;
            meta["UpdatedAt"] = DateTime.UtcNow.ToString("o");
            meta["InstalledVersion"] = cleanVersion;

            Directory.CreateDirectory(Path.GetDirectoryName(DcMetaPath)!);
            FileHelper.WriteAllTextAtomic(DcMetaPath, System.Text.Json.JsonSerializer.Serialize(meta,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "MainViewModel.SaveDcMeta");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.SaveDcMeta] Failed to save DC metadata — {ex.Message}");
        }
    }

    /// <summary>Reads the installed DC version for the given bitness from the meta file, or null if not found.</summary>
    internal static string? ReadDcInstalledVersion(bool is32Bit)
    {
        try
        {
            if (!File.Exists(DcMetaPath)) return null;
            var metaJson = File.ReadAllText(DcMetaPath);
            var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(metaJson);
            if (meta == null) return null;

            var key = is32Bit ? "InstalledVersion32" : "InstalledVersion64";
            if (meta.TryGetValue(key, out var verEl))
                return verEl.GetString()?.TrimStart('v', 'V');
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.ReadDcInstalledVersion] Failed — {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Checks ReLimiter updates per game. The installed version comes from each game's own addon file. Games that
    /// run the latest release are compared with it; once the latest release is v4, games that don't qualify for v4
    /// (UlV4Rules) are compared with the last v3 release instead. A game on v4 that no longer qualifies is never
    /// moved back to v3 automatically (the ReLimiter ⚙ warns and offers it). Pinned games are skipped.
    /// Returns the games with an update, or null when the check failed.
    /// </summary>
    public async Task<List<GameCardViewModel>?> CheckUlUpdateAsync(List<GameCardViewModel> cards)
    {
        _crashReporter.Log($"[CheckUlUpdateAsync] Starting");
        var result = new List<GameCardViewModel>();

        // Games pinned to a release (ReLimiter ⚙ › Version) are never updated
        var installed = cards.Where(c => !IsUlPinned(c)
            && (c.UlStatus == GameStatus.Installed || c.UlStatus == GameStatus.UpdateAvailable)).ToList();
        if (installed.Count == 0)
        {
            _crashReporter.Log("[CheckUlUpdateAsync] No (unpinned) ReLimiter installs — skipping");
            return result;
        }

        try
        {
            // ── Fetch latest release from GitHub API ──────────────────────
            var json = await _etagCache.GetWithETagAsync(_http, UltraLimiterReleasesApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log($"[CheckUlUpdateAsync] API returned error");
                return null;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            // Get the tag name (version)
            string? remoteVersion = null;
            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                remoteVersion = tagEl.GetString();

            // Always capture the release body for the Info dialog
            if (doc.RootElement.TryGetProperty("body", out var ulBodyEl))
                _latestUlReleaseBody = ulBodyEl.GetString();

            var latest = UlV4Rules.ParseVersion(remoteVersion);
            if (string.IsNullOrEmpty(remoteVersion) || latest == null)
            {
                _crashReporter.Log($"[CheckUlUpdateAsync] No usable tag_name in latest release ('{remoteVersion}')");
                return null;
            }

            // Get the download URLs for both bitness variants from assets
            string? downloadUrl64 = null;
            string? downloadUrl32 = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) &&
                        asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        var assetName = name.GetString();
                        if (assetName?.Equals(UltraLimiterFileName64, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl64 = urlEl.GetString();
                        else if (assetName?.Equals(UltraLimiterFileName32, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl32 = urlEl.GetString();
                    }

                    if (downloadUrl64 != null && downloadUrl32 != null)
                        break;
                }
            }

            _latestUlVersion = remoteVersion;
            _latestUlDownloadUrl = downloadUrl64;
            _latestUlDownloadUrl32 = downloadUrl32;

            // Dev test build (unlock.txt): it is the latest 64-bit release; installs copy it, nothing to pre-cache
            bool devBuild = ApplyUlDevOverride();
            if (devBuild)
            {
                remoteVersion = _latestUlVersion!;
                latest = UlV4Rules.ParseVersion(remoteVersion)!;
            }

            bool latestIsV4 = latest.Major >= UlV4Rules.V4Major;
            if (latestIsV4) await GpuInfoService.GetNvidiaGpusAsync().ConfigureAwait(false);

            bool pre64 = false, pre32 = false;
            Version? oldest = null;
            foreach (var card in installed)
            {
                var have = UlInstalledFileVersion(card); // null = old file name or no version resource: update it
                Version? target;
                if (!latestIsV4 || UlWantsV4(card))
                {
                    if (string.IsNullOrEmpty(card.Is32Bit ? downloadUrl32 : downloadUrl64)) continue;
                    target = latest;
                }
                else
                {
                    if (have != null && have.Major >= UlV4Rules.V4Major)
                    {
                        _crashReporter.Log($"[CheckUlUpdateAsync] '{card.GameName}' has v{UlV4Rules.DisplayVersion(have)} but doesn't qualify for v4 — left alone");
                        continue;
                    }
                    var v3Tag = await GetLastV3TagAsync(card.Is32Bit).ConfigureAwait(false);
                    target = UlV4Rules.ParseVersion(v3Tag);
                    if (target == null) continue;
                }

                if (have == null || UlV4Rules.IsOlder(have, target))
                {
                    result.Add(card);
                    if (target == latest) { if (card.Is32Bit) pre32 = true; else pre64 = true; }
                    if (have != null && (oldest == null || UlV4Rules.IsOlder(have, oldest))) oldest = have;
                }
            }

            _crashReporter.Log($"[CheckUlUpdateAsync] Latest={remoteVersion}, {result.Count} of {installed.Count} game(s) have an update"
                + (result.Count > 0 ? ": " + string.Join(", ", result.Select(c => c.GameName)) : ""));
            if (result.Count == 0) return result;

            App.Services.GetRequiredService<IUpdateLogService>().Record(new Models.UpdateLogEntry
            {
                Timestamp     = DateTime.UtcNow,
                Category      = "Component",
                ComponentName = "ReLimiter",
                OldVersion    = oldest != null ? UlV4Rules.DisplayVersion(oldest) : null,
                NewVersion    = remoteVersion,
            });

            if (devBuild) pre64 = false;
            if (pre64 || pre32) await PreCacheRemoteUlAsync(pre64, pre32);
            return result;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[CheckUlUpdateAsync] Failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if a newer Display Commander is available by comparing the latest GitHub
    /// release tag version against the locally installed version from meta.
    /// Returns true if an update is available.
    /// </summary>
    public async Task<bool> CheckDcUpdateAsync(List<GameCardViewModel> cards)
    {
        _crashReporter.Log($"[CheckDcUpdateAsync] Starting");

        bool anyInstalled = cards.Any(c => c.DcStatus == GameStatus.Installed || c.DcStatus == GameStatus.UpdateAvailable);

        // ── Determine which bitness variants are in use ───────────────────
        bool needs64 = cards.Any(c => (c.DcStatus == GameStatus.Installed || c.DcStatus == GameStatus.UpdateAvailable) && !c.Is32Bit);
        bool needs32 = cards.Any(c => (c.DcStatus == GameStatus.Installed || c.DcStatus == GameStatus.UpdateAvailable) && c.Is32Bit);

        // If nothing is specifically installed yet (legacy/meta-only), default to 64-bit
        if (!needs64 && !needs32)
            needs64 = true;

        // ── Read installed version from meta (use oldest across bitness variants) ──
        var installedVersion64 = needs64 ? ReadDcInstalledVersion(false) : null;
        var installedVersion32 = needs32 ? ReadDcInstalledVersion(true) : null;
        var installedVersion = (installedVersion64, installedVersion32) switch
        {
            (null, null) => null,
            (null, var v) => v,
            (var v, null) => v,
            // Use the older of the two so we trigger an update if either is behind
            var (v64, v32) => (Version.TryParse(v64, out var ver64) && Version.TryParse(v32, out var ver32))
                ? (ver64 <= ver32 ? v64 : v32)
                : v64, // fallback to 64-bit if parsing fails
        };

        if (installedVersion == null && !anyInstalled)
        {
            _crashReporter.Log("[CheckDcUpdateAsync] No DC installed and no meta — skipping");
            return false;
        }

        // If DC is installed but no version in meta (legacy install), treat as needing update
        if (installedVersion == null && anyInstalled)
        {
            _crashReporter.Log("[CheckDcUpdateAsync] DC installed but no version in meta — treating as update needed");
        }

        try
        {
            // ── Fetch latest release from GitHub API ──────────────────────
            var json = await _etagCache.GetWithETagAsync(_http, EffectiveDcReleasesApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log($"[CheckDcUpdateAsync] API returned error");
                return false;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            // Get the tag name (version) — DC uses a fixed "latest_build" tag,
            // so extract the real version from the release body ("Version in binaries: X.Y.Z.W")
            string? remoteVersion = null;
            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                remoteVersion = tagEl.GetString();

            // DC's tag is always "latest_build" which is useless for comparison.
            // Parse the actual version from the release body text.
            if (doc.RootElement.TryGetProperty("body", out var bodyEl))
            {
                var body = bodyEl.GetString() ?? "";
                _latestDcReleaseBody = body; // Always capture for the Info dialog
                var versionMatch = System.Text.RegularExpressions.Regex.Match(
                    body, @"\*{0,2}Version in binaries\*{0,2}:\s*([\d.]+)");
                if (versionMatch.Success)
                    remoteVersion = versionMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(remoteVersion))
            {
                _crashReporter.Log("[CheckDcUpdateAsync] No version found in tag or release body");
                return false;
            }

            // Get the download URLs for both bitness variants from assets
            string? downloadUrl64 = null;
            string? downloadUrl32 = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) &&
                        asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        var assetName = name.GetString();
                        if (assetName?.Equals(DcFileName64, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl64 = urlEl.GetString();
                        else if (assetName?.Equals(DcFileName32, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl32 = urlEl.GetString();
                    }

                    if (downloadUrl64 != null && downloadUrl32 != null)
                        break;
                }
            }

            // Need at least one matching asset for the variants we care about
            if ((needs64 && string.IsNullOrEmpty(downloadUrl64)) && (needs32 && string.IsNullOrEmpty(downloadUrl32)))
            {
                _crashReporter.Log("[CheckDcUpdateAsync] No matching asset found in latest release");
                return false;
            }

            _crashReporter.Log($"[CheckDcUpdateAsync] Remote version={remoteVersion}, installed={installedVersion ?? "(none)"}");

            // ── Compare versions ──────────────────────────────────────────
            if (installedVersion != null && !IsNewerVersion(remoteVersion, installedVersion))
            {
                _crashReporter.Log("[CheckDcUpdateAsync] No update (installed is current)");
                return false;
            }

            // ── Update available — store remote info and pre-cache ────────
            _latestDcVersion = remoteVersion;
            _latestDcDownloadUrl = downloadUrl64;
            _latestDcDownloadUrl32 = downloadUrl32;
            _crashReporter.Log($"[CheckDcUpdateAsync] Update available: {installedVersion ?? "(none)"} → {remoteVersion}");
            App.Services.GetRequiredService<IUpdateLogService>().Record(new Models.UpdateLogEntry
            {
                Timestamp     = DateTime.UtcNow,
                Category      = "Component",
                ComponentName = "Display Commander",
                OldVersion    = installedVersion,
                NewVersion    = remoteVersion,
            });

            await PreCacheRemoteDcAsync(needs64, needs32);
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[CheckDcUpdateAsync] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Downloads the remote DC file(s) into the cache using the URLs from the latest release.</summary>
    private async Task PreCacheRemoteDcAsync(bool needs64, bool needs32)
    {
        if (needs64)
            await PreCacheRemoteDcVariantAsync(false);
        if (needs32)
            await PreCacheRemoteDcVariantAsync(true);
    }

    /// <summary>Downloads a single bitness variant of the DC file into the cache.</summary>
    private async Task PreCacheRemoteDcVariantAsync(bool is32Bit)
    {
        try
        {
            var url = is32Bit ? _latestDcDownloadUrl32 : _latestDcDownloadUrl;
            if (string.IsNullOrEmpty(url))
            {
                _crashReporter.Log($"[PreCacheRemoteDcAsync] No download URL available for {(is32Bit ? "32-bit" : "64-bit")}");
                return;
            }

            var tempPath = GetDcCachePath(is32Bit) + ".precache.tmp";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return;

            using (var net = await resp.Content.ReadAsStreamAsync())
            using (var file = File.Create(tempPath))
            {
                var buf = new byte[1024 * 1024];
                int read;
                while ((read = await net.ReadAsync(buf)) > 0)
                    await file.WriteAsync(buf.AsMemory(0, read));
            }

            if (File.Exists(GetDcCachePath(is32Bit))) File.Delete(GetDcCachePath(is32Bit));
            File.Move(tempPath, GetDcCachePath(is32Bit));
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[PreCacheRemoteDcAsync] Failed ({(is32Bit ? "32-bit" : "64-bit")}) — {ex.Message}");
        }
    }

    /// <summary>
    /// Compares two version strings (e.g. "1.0.0" vs "1.0.1").
    /// Returns true if <paramref name="remote"/> is newer than <paramref name="installed"/>.
    /// Strips leading 'v' if present.
    /// </summary>
    private static bool IsNewerVersion(string remote, string installed)
    {
        static Version? Parse(string s)
        {
            s = s.TrimStart('v', 'V').Trim();
            return Version.TryParse(s, out var v) ? v : null;
        }

        var r = Parse(remote);
        var i = Parse(installed);
        if (r == null || i == null) return !string.Equals(remote, installed, StringComparison.OrdinalIgnoreCase);
        return r > i;
    }

    /// <summary>True when the game is pinned to a ReLimiter release (skipped by the update check and Update All).</summary>
    public bool IsUlPinned(GameCardViewModel card) => GetUlPinnedVersion(card.GameName, card.Source ?? "") != null;

    // Cached latest release info from the update check
    private string? _latestUlVersion;
    private string? _latestUlDownloadUrl;
    private string? _latestUlDownloadUrl32;
    private string? _latestUlReleaseBody;

    /// <summary>Markdown body of the latest ReLimiter GitHub release, or null if not yet fetched.</summary>
    public string? LatestUlReleaseBody => _latestUlReleaseBody;

    /// <summary>URL to the GitHub release page for the latest UL version, or null if unknown.</summary>
    public string? LatestUlReleasePageUrl => _latestUlVersion != null
        ? $"https://github.com/RankFTW/ReLimiter/releases/tag/{_latestUlVersion}"
        : null;

    /// <summary>Downloads the remote UL file(s) into the cache using the URLs from the latest release.</summary>
    private async Task PreCacheRemoteUlAsync(bool needs64, bool needs32)
    {
        if (needs64)
            await PreCacheRemoteUlVariantAsync(false);
        if (needs32)
            await PreCacheRemoteUlVariantAsync(true);
    }

    /// <summary>Downloads a single bitness variant of the UL file into the cache.</summary>
    private async Task PreCacheRemoteUlVariantAsync(bool is32Bit)
    {
        try
        {
            var url = is32Bit ? _latestUlDownloadUrl32 : _latestUlDownloadUrl;
            if (string.IsNullOrEmpty(url))
            {
                _crashReporter.Log($"[PreCacheRemoteUlAsync] No download URL available for {(is32Bit ? "32-bit" : "64-bit")}");
                return;
            }
            // A test build in the cache (newer than the latest release) isn't replaced
            if (await Task.Run(() => IsUlCacheNewerThanLatest(is32Bit)))
            {
                _crashReporter.Log($"[PreCacheRemoteUlAsync] Cached build is newer than {_latestUlVersion} (test build) — not replacing it");
                return;
            }

            var tempPath = GetUlCachePath(is32Bit) + ".precache.tmp";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return;

            using (var net = await resp.Content.ReadAsStreamAsync())
            using (var file = File.Create(tempPath))
            {
                var buf = new byte[1024 * 1024];
                int read;
                while ((read = await net.ReadAsync(buf)) > 0)
                    await file.WriteAsync(buf.AsMemory(0, read));
            }

            if (File.Exists(GetUlCachePath(is32Bit))) File.Delete(GetUlCachePath(is32Bit));
            File.Move(tempPath, GetUlCachePath(is32Bit));
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[PreCacheRemoteUlAsync] Failed ({(is32Bit ? "32-bit" : "64-bit")}) — {ex.Message}");
        }
    }
}

