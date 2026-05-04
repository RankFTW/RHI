// MainViewModel.Update.cs -- Update-all commands, update checking, UL caching, and version management.

using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    internal void NotifyUpdateButtonChanged()
    {
        HasUpdatesAvailable = AnyUpdateAvailable;
        OnPropertyChanged(nameof(AnyUpdateAvailable));
        OnPropertyChanged(nameof(UpdateAllBtnBackground));
        OnPropertyChanged(nameof(UpdateAllBtnForeground));
        OnPropertyChanged(nameof(UpdateAllBtnBorder));
    }

    /// <summary>Returns true if the current app version differs from the last seen version.</summary>
    public bool IsNewVersion()
    {
        var current = _updateService.CurrentVersion;
        var currentStr = $"{current.Major}.{current.Minor}.{current.Build}";
        return LastSeenVersion != currentStr;
    }

    /// <summary>Marks the current version as seen and saves settings.</summary>
    public void MarkVersionSeen()
    {
        var current = _updateService.CurrentVersion;
        LastSeenVersion = $"{current.Major}.{current.Minor}.{current.Build}";
        SaveSettingsPublic();
    }

    /// <summary>
    /// Reads the bundled RHI_PatchNotes.md and extracts the last N version sections.
    /// Each section starts with "## vX.Y.Z".
    /// </summary>
    public static string GetRecentPatchNotes(int count = 3)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "RHI_PatchNotes.md");
            if (!File.Exists(path)) return "Patch notes file not found.";

            var lines = File.ReadAllLines(path);
            var sections = new List<string>();
            var currentSection = new List<string>();
            bool inSection = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("## v"))
                {
                    if (inSection && currentSection.Count > 0)
                    {
                        sections.Add(string.Join("\n", currentSection));
                        if (sections.Count >= count) break;
                        currentSection.Clear();
                    }
                    inSection = true;
                    currentSection.Add(line);
                }
                else if (inSection)
                {
                    // Stop at the "---" separator between versions (but don't include it)
                    if (line.Trim() == "---")
                    {
                        sections.Add(string.Join("\n", currentSection));
                        if (sections.Count >= count) break;
                        currentSection.Clear();
                        inSection = false;
                    }
                    else
                    {
                        currentSection.Add(line);
                    }
                }
            }

            // Capture final section if still in progress
            if (inSection && currentSection.Count > 0 && sections.Count < count)
                sections.Add(string.Join("\n", currentSection));

            return sections.Count > 0
                ? string.Join("\n\n---\n\n", sections)
                : "No patch notes available.";
        }
        catch (Exception ex)
        {
            return $"Error reading patch notes: {ex.Message}";
        }
    }

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
            File.WriteAllText(UlMetaPath, System.Text.Json.JsonSerializer.Serialize(meta,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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
            File.WriteAllText(DcMetaPath, System.Text.Json.JsonSerializer.Serialize(meta,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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
    /// Checks if a newer ReLimiter is available by comparing the latest GitHub
    /// release tag version against the locally installed version from meta.
    /// Returns true if an update is available.
    /// </summary>
    public async Task<bool> CheckUlUpdateAsync(List<GameCardViewModel> cards)
    {
        _crashReporter.Log($"[CheckUlUpdateAsync] Starting");

        bool anyInstalled = cards.Any(c => c.UlStatus == GameStatus.Installed);

        // ── Determine which bitness variants are in use ───────────────────
        bool needs64 = cards.Any(c => c.UlStatus == GameStatus.Installed && !c.Is32Bit);
        // 32-bit ReLimiter not yet available — skip 32-bit update checks for now
        bool needs32 = false;

        // If nothing is specifically installed yet (legacy/meta-only), default to 64-bit
        if (!needs64 && !needs32)
            needs64 = true;

        // ── Read installed version from meta (use oldest across bitness variants) ──
        var installedVersion64 = needs64 ? ReadUlInstalledVersion(false) : null;
        var installedVersion32 = needs32 ? ReadUlInstalledVersion(true) : null;
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
            _crashReporter.Log("[CheckUlUpdateAsync] No UL installed and no meta — skipping");
            return false;
        }

        // If UL is installed but no version in meta (legacy install), treat as needing update
        if (installedVersion == null && anyInstalled)
        {
            _crashReporter.Log("[CheckUlUpdateAsync] UL installed but no version in meta — treating as update needed");
        }

        try
        {
            // ── Fetch latest release from GitHub API ──────────────────────
            var json = await _etagCache.GetWithETagAsync(_http, UltraLimiterReleasesApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log($"[CheckUlUpdateAsync] API returned error");
                return false;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            // Get the tag name (version)
            string? remoteVersion = null;
            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                remoteVersion = tagEl.GetString();

            // Always capture the release body for the Info dialog
            if (doc.RootElement.TryGetProperty("body", out var ulBodyEl))
                _latestUlReleaseBody = ulBodyEl.GetString();

            if (string.IsNullOrEmpty(remoteVersion))
            {
                _crashReporter.Log("[CheckUlUpdateAsync] No tag_name in latest release");
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
                        if (assetName?.Equals(UltraLimiterFileName64, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl64 = urlEl.GetString();
                        else if (assetName?.Equals(UltraLimiterFileName32, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl32 = urlEl.GetString();
                    }

                    if (downloadUrl64 != null && downloadUrl32 != null)
                        break;
                }
            }

            // Need at least one matching asset for the variants we care about
            if ((needs64 && string.IsNullOrEmpty(downloadUrl64)) && (needs32 && string.IsNullOrEmpty(downloadUrl32)))
            {
                _crashReporter.Log("[CheckUlUpdateAsync] No matching asset found in latest release");
                return false;
            }

            _crashReporter.Log($"[CheckUlUpdateAsync] Remote version={remoteVersion}, installed={installedVersion ?? "(none)"}");

            // ── Compare versions ──────────────────────────────────────────
            if (installedVersion != null && !IsNewerVersion(remoteVersion, installedVersion))
            {
                _crashReporter.Log("[CheckUlUpdateAsync] No update (installed is current)");
                return false;
            }

            // ── Update available — store remote info and pre-cache ────────
            _latestUlVersion = remoteVersion;
            _latestUlDownloadUrl = downloadUrl64;
            _latestUlDownloadUrl32 = downloadUrl32;
            _crashReporter.Log($"[CheckUlUpdateAsync] Update available: {installedVersion ?? "(none)"} → {remoteVersion}");

            await PreCacheRemoteUlAsync(needs64, needs32);
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[CheckUlUpdateAsync] Failed — {ex.Message}");
            return false;
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
            var json = await _etagCache.GetWithETagAsync(_http, DcReleasesApiUrl).ConfigureAwait(false);
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
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
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

            var tempPath = GetUlCachePath(is32Bit) + ".precache.tmp";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
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

    /// <summary>
    /// Eligibility: card must not be hidden, not excluded from Update All.
    /// </summary>
    private IEnumerable<GameCardViewModel> UpdateAllEligible() =>
        _updateOrchestrationService.UpdateAllEligible(_allCards);

    public async Task UpdateAllRenoDxAsync()
    {
        await _updateOrchestrationService.UpdateAllRenoDxAsync(
            _allCards, _dllOverrideService, DispatcherQueue,
            () => SaveLibrary(),
            () =>
            {
                _filterViewModel.UpdateCounts();
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });
    }

    public async Task UpdateAllReShadeAsync()
    {
        await _updateOrchestrationService.UpdateAllReShadeAsync(
            _allCards, _dllOverrideService, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            },
            shaderResolver: ResolveShaderSelection,
            manifestDllResolver: GetManifestDllNames);
    }

    public async Task UpdateAllUlAsync()
    {
        var ulCards = _allCards.Where(c => c.UlStatus == GameStatus.UpdateAvailable && !c.IsHidden && !c.ExcludeFromUpdateAllUl).ToList();
        if (ulCards.Count == 0) return;

        foreach (var card in ulCards)
        {
            try { await InstallUlAsync(card); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllUlAsync] Failed for '{card.GameName}': {ex.Message}"); }
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    public async Task UpdateAllDcAsync()
    {
        var dcCards = _allCards.Where(c => c.DcStatus == GameStatus.UpdateAvailable && !c.IsHidden && !c.ExcludeFromUpdateAllDc).ToList();
        if (dcCards.Count == 0) return;

        foreach (var card in dcCards)
        {
            try { await InstallDcAsync(card); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllDcAsync] Failed for '{card.GameName}': {ex.Message}"); }
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    public async Task UpdateAllDxvkAsync()
    {
        var dxvkCards = GetUpdateAllDxvkEligibleCards(_allCards);
        if (dxvkCards.Count == 0) return;

        // Ensure latest DXVK staging is available before updating games
        if (!_dxvkService.IsStagingReady)
        {
            try { await _dxvkService.EnsureStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllDxvkAsync] Staging failed — {ex.Message}"); return; }
        }

        foreach (var card in dxvkCards)
        {
            try { await _dxvkService.UpdateAsync(card); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllDxvkAsync] Failed for '{card.GameName}': {ex.Message}"); }
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    /// <summary>
    /// Returns the list of cards eligible for DXVK Update All.
    /// A card is eligible when DxvkStatus == UpdateAvailable, not excluded, and not hidden.
    /// Extracted as a static helper so the filtering logic can be property-tested
    /// without MainViewModel dependencies.
    /// </summary>
    internal static List<GameCardViewModel> GetUpdateAllDxvkEligibleCards(IEnumerable<GameCardViewModel> cards)
        => cards.Where(c => c.DxvkStatus == GameStatus.UpdateAvailable
                         && !c.ExcludeFromUpdateAllDxvk
                         && !c.IsHidden)
                .ToList();

    public async Task UpdateAllOsAsync()
    {
        var osCards = _allCards.Where(c => c.OsStatus == GameStatus.UpdateAvailable && !c.IsHidden && !c.ExcludeFromUpdateAllOs).ToList();
        if (osCards.Count == 0) return;

        // Ensure latest staging is available before updating games
        if (!_optiScalerService.IsStagingReady)
        {
            try { await _optiScalerService.EnsureStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] Staging failed — {ex.Message}"); return; }
        }

        foreach (var card in osCards)
        {
            try { await _optiScalerService.UpdateAsync(card); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] Failed for '{card.GameName}': {ex.Message}"); }
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    public async Task UpdateAllRefAsync()
    {
        await _updateOrchestrationService.UpdateAllREFrameworkAsync(
            _allCards, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });
    }

    public async Task UpdateAllLumaAsync()
    {
        var lumaCards = _allCards.Where(c => c.LumaStatus == GameStatus.UpdateAvailable
            && !c.IsHidden && c.LumaMod?.DownloadUrl != null).ToList();
        if (lumaCards.Count == 0) return;

        foreach (var card in lumaCards)
        {
            try
            {
                await InstallLumaAsync(card);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[UpdateAllLumaAsync] Failed for '{card.GameName}': {ex.Message}");
            }
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    // ── Update checking ───────────────────────────────────────────────────────────

    private async Task CheckForUpdatesAsync(List<GameCardViewModel> cards, List<InstalledModRecord> records, List<AuxInstalledRecord> auxRecords)
    {
        // ── Cooldown: skip update checks if last check was recent ──────────────
        const int CooldownHours = 4;
        var forceCheck = _forceUpdateCheck;
        _forceUpdateCheck = false;

        if (!forceCheck && DateTime.TryParse(_settingsViewModel.LastUpdateCheckUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var lastCheck))
        {
            var elapsed = DateTime.UtcNow - lastCheck;
            if (elapsed < TimeSpan.FromHours(CooldownHours))
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Cooldown active — last check was {elapsed.TotalMinutes:F0}m ago, skipping API calls");
                return;
            }
        }

        await _updateOrchestrationService.CheckForUpdatesAsync(
            cards, records, auxRecords, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            },
            skipRdx: _settingsViewModel.GlobalSkipRdxUpdates,
            skipRs: _settingsViewModel.GlobalSkipRsUpdates,
            skipRef: _settingsViewModel.GlobalSkipRefUpdates);

        // ── Rate-limit guard: if earlier checks triggered 403, skip remaining API calls ──
        if (_etagCache.IsRateLimited)
        {
            _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] GitHub API rate limited — skipping remaining update checks");
            // Record successful check time even if rate-limited, so we don't hammer the API again immediately
            _settingsViewModel.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
            SaveSettingsPublic();
            _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] Update check complete (partial, rate limited) — cooldown timestamp saved");
            return;
        }

        // Check ReLimiter for updates (single global check, applies to all cards with UL installed)
        if (!_settingsViewModel.GlobalSkipUlUpdates)
        {
        try
        {
            var ulUpdateAvailable = await CheckUlUpdateAsync(cards).ConfigureAwait(false);
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] UL update result: {ulUpdateAvailable}, cards with UL installed: {cards.Count(c => c.UlStatus == GameStatus.Installed)}");
            if (ulUpdateAvailable)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var card in cards.Where(c => c.UlStatus == GameStatus.Installed))
                        card.UlStatus = GameStatus.UpdateAvailable;

                    HasUpdatesAvailable = AnyUpdateAvailable;
                    OnPropertyChanged(nameof(AnyUpdateAvailable));
                    OnPropertyChanged(nameof(UpdateAllBtnBackground));
                    OnPropertyChanged(nameof(UpdateAllBtnForeground));
                    OnPropertyChanged(nameof(UpdateAllBtnBorder));
                });
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] UL update check failed — {ex.Message}");
        }
        }

        // Check Display Commander for updates (single global check, applies to all cards with DC installed)
        if (!_settingsViewModel.GlobalSkipDcUpdates)
        {
        try
        {
            var dcUpdateAvailable = await CheckDcUpdateAsync(cards).ConfigureAwait(false);
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DC update result: {dcUpdateAvailable}, cards with DC installed: {cards.Count(c => c.DcStatus == GameStatus.Installed)}");
            if (dcUpdateAvailable)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var card in cards.Where(c => c.DcStatus == GameStatus.Installed))
                        card.DcStatus = GameStatus.UpdateAvailable;

                    HasUpdatesAvailable = AnyUpdateAvailable;
                    OnPropertyChanged(nameof(AnyUpdateAvailable));
                    OnPropertyChanged(nameof(UpdateAllBtnBackground));
                    OnPropertyChanged(nameof(UpdateAllBtnForeground));
                    OnPropertyChanged(nameof(UpdateAllBtnBorder));
                });
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DC update check failed — {ex.Message}");
        }
        }

        // Check OptiScaler for updates (single global check, applies to all cards with OS installed)
        if (!_settingsViewModel.GlobalSkipOsUpdates)
        {
        try
        {
            await _optiScalerService.CheckForUpdateAsync().ConfigureAwait(false);
            var osUpdateAvailable = _optiScalerService.HasUpdate;
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS update result: {osUpdateAvailable}, cards with OS installed: {cards.Count(c => c.OsStatus == GameStatus.Installed)}");
            if (osUpdateAvailable)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var card in cards.Where(c => c.OsStatus == GameStatus.Installed))
                        card.OsStatus = GameStatus.UpdateAvailable;

                    HasUpdatesAvailable = AnyUpdateAvailable;
                    OnPropertyChanged(nameof(AnyUpdateAvailable));
                    OnPropertyChanged(nameof(UpdateAllBtnBackground));
                    OnPropertyChanged(nameof(UpdateAllBtnForeground));
                    OnPropertyChanged(nameof(UpdateAllBtnBorder));
                });
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS update check failed — {ex.Message}");
        }
        }

        // Check DXVK for updates (single global check, applies to all cards with DXVK installed)
        try
        {
            await _dxvkService.CheckForUpdateAsync().ConfigureAwait(false);
            var dxvkUpdateAvailable = _dxvkService.HasUpdate;
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK update result: {dxvkUpdateAvailable}, cards with DXVK installed: {cards.Count(c => c.DxvkStatus == GameStatus.Installed)}");
            if (dxvkUpdateAvailable)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var card in cards.Where(c => c.DxvkStatus == GameStatus.Installed))
                        card.DxvkStatus = GameStatus.UpdateAvailable;

                    HasUpdatesAvailable = AnyUpdateAvailable;
                    OnPropertyChanged(nameof(AnyUpdateAvailable));
                    OnPropertyChanged(nameof(UpdateAllBtnBackground));
                    OnPropertyChanged(nameof(UpdateAllBtnForeground));
                    OnPropertyChanged(nameof(UpdateAllBtnBorder));
                });
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK update check failed — {ex.Message}");
        }

        // Record successful check time
        _settingsViewModel.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
        SaveSettingsPublic();
        _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] Update check complete — cooldown timestamp saved");
    }
}

