// MainViewModel.Init.cs -- Initialization, detection, card building, refresh, and library persistence.

using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    // Normalize titles for tolerant lookup: remove punctuation, trademarks, parenthetical text, diacritics
    private static string NormalizeForLookup(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        // Remove common trademark symbols
        s = s.Replace("™", "").Replace("®", "").Replace("©", "");
        // Remove parenthetical content
        s = Regex.Replace(s, "\\([^)]*\\)", "");
        s = Regex.Replace(s, "\\[[^]]*\\]", "");
        // Normalize unicode and remove diacritics
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        }
        var noDiacritics = sb.ToString().Normalize(NormalizationForm.FormC);
        // Remove punctuation, keep letters/numbers and spaces
        var cleaned = Regex.Replace(noDiacritics, "[^0-9A-Za-z ]+", " ");
        // Collapse whitespace and trim
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();
        // Remove common edition suffixes
        cleaned = Regex.Replace(cleaned, "\\b(enhanced edition|remastered|edition|ultimate|definitive)\\b", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, "\\s+", " ").Trim();
        return cleaned.ToLowerInvariant();
    }

    private string? GetGenericNote(string gameName, Dictionary<string, string> genericNotes)
    {
        if (string.IsNullOrEmpty(gameName) || genericNotes == null || genericNotes.Count == 0) return null;
        // Check user name mappings from JSON settings file
        try
        {
            var s = SettingsViewModel.LoadSettingsFile();
            if (s.TryGetValue("NameMappings", out var json) && !string.IsNullOrEmpty(json))
            {
                var map = JsonSerializer.Deserialize<Dictionary<string,string>>(json);
                if (map != null)
                {
                    if (map.TryGetValue(gameName, out var mapped) && !string.IsNullOrEmpty(mapped))
                    {
                        if (genericNotes.TryGetValue(mapped, out var mv) && !string.IsNullOrEmpty(mv)) return mv;
                    }
                    var n = NormalizeForLookup(gameName);
                    foreach (var kv in map)
                    {
                        if (NormalizeForLookup(kv.Key).Equals(n, StringComparison.OrdinalIgnoreCase))
                        {
                            if (genericNotes.TryGetValue(kv.Value, out var mv2) && !string.IsNullOrEmpty(mv2)) return mv2;
                        }
                    }
                }
            }
        }
        catch (Exception ex) { _crashReporter.Log($"[MainViewModel.LookupGenericNotes] Name mapping lookup failed for '{gameName}' — {ex.Message}"); }
        // direct
        if (genericNotes.TryGetValue(gameName, out var v) && !string.IsNullOrEmpty(v)) return v;
        // detection-normalized
        try { var k = _gameDetectionService.NormalizeName(gameName); if (!string.IsNullOrEmpty(k) && genericNotes.TryGetValue(k, out var v2) && !string.IsNullOrEmpty(v2)) return v2; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.LookupGenericNotes] NormalizeName failed for '{gameName}' — {ex.Message}"); }
        // normalized-equality scan
        var tgt = NormalizeForLookup(gameName);
        foreach (var kv in genericNotes)
        {
            if (NormalizeForLookup(kv.Key).Equals(tgt, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        }
        return null;
    }

    // InstallCompleted event handler removed — card state is updated in-place
    // by InstallModAsync, so no full rescan is needed after install.

    // ── Commands ──────────────────────────────────────────────────────────────────

    public async Task RefreshAsync()
    {
        await InitializeAsync(forceRescan: true);
    }

    [RelayCommand]
    public async Task FullRefreshAsync()
    {
        // Clear all caches so every game is re-scanned from disk.
        _engineTypeCache.Clear();
        _resolvedPathCache.Clear();
        _addonFileCache.Clear();
        _bitnessCache.Clear();
        await InitializeAsync(forceRescan: true);
    }

    // ── Init ──

    public async Task InitializeAsync(bool forceRescan = false)
    {
        IsLoading = true;
        if (!_hasInitialized) DisplayedGames.Clear();
        _allCards.Clear();
        _originalDetectedNames.Clear();

        _crashReporter.Log($"[MainViewModel.InitializeAsync] Started (forceRescan={forceRescan})");

        // Clear API caches on full refresh so all detection runs fresh
        if (forceRescan)
        {
            _gameApiCache.Clear();
            GraphicsApiDetector.ClearCache();
        }

        try
        {

            var savedLib = _gameLibraryService.Load();
            List<DetectedGame> detectedGames;
            Dictionary<string, bool> addonCache;
            bool wikiFetchFailed = false;
            Task rsTask = Task.CompletedTask; // hoisted so we can defer the await until after cards display
            Task normalRsTask = Task.CompletedTask; // hoisted so we can defer the await until after cards display
            Task osTask = Task.CompletedTask; // hoisted so we can defer the await until after cards display
            Task dlssTask = Task.CompletedTask; // hoisted so we can defer the await until after cards display

            // Start Nexus Mods + PCGW initialization early (network I/O, runs in parallel with other fetches)
            var nexusInitTask = Task.Run(async () => {
                try { await _nexusModsService.InitAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] NexusModsService init failed — {ex.Message}"); }
            });
            var pcgwCacheTask = Task.Run(async () => {
                try { await _pcgwService.LoadCacheAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] PcgwService cache load failed — {ex.Message}"); }
            });

            // Merge hidden/favourite from library file with any already loaded from settings.json
            if (savedLib?.HiddenGames != null)
                foreach (var g in savedLib.HiddenGames) _hiddenGames.Add(g);
            if (savedLib?.FavouriteGames != null)
                foreach (var g in savedLib.FavouriteGames) _favouriteGames.Add(g);
            _manualGames = savedLib != null ? _gameLibraryService.ToManualGames(savedLib) : new();

            // Load engine + addon caches from the saved library so BuildCards can
            // skip expensive filesystem traversals for games seen on a previous run.
            if (savedLib != null)
            {
                _engineTypeCache   = savedLib.EngineTypeCache   ?? new(StringComparer.OrdinalIgnoreCase);
                _resolvedPathCache = savedLib.ResolvedPathCache ?? new(StringComparer.OrdinalIgnoreCase);
                _addonFileCache    = savedLib.AddonFileCache    ?? new(StringComparer.OrdinalIgnoreCase);
                _bitnessCache      = savedLib.BitnessCache      ?? new(StringComparer.OrdinalIgnoreCase);
                LastSelectedGameName = savedLib.LastSelectedGame;
            }

            // 1. Set status messages and addonCache based on whether cache exists
            bool hasCachedLibrary = savedLib != null && !forceRescan;
            if (hasCachedLibrary)
            {
                StatusText    = $"Library loaded ({savedLib!.Games.Count} games, scanned {FormatAge(savedLib.LastScanned)})";
                SubStatusText = "Checking for new games and fetching latest mod info...";
                addonCache    = savedLib.AddonScanCache;
            }
            else
            {
                StatusText    = "Scanning game library...";
                SubStatusText = "Running store scans + wiki fetch simultaneously...";
                addonCache    = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }

            // 2. Launch all background tasks (identical for both paths)
            var wikiTask     = _wikiService.FetchAllAsync();
            var lumaTask     = _lumaService.FetchCompletedModsAsync();
            var manifestTask = _manifestService.FetchAsync();
            var detectTask   = DetectAllGamesDedupedAsync();
            var osWikiTask   = Task.Run(async () => {
                try { await _optiScalerWikiService.FetchAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] OptiScaler wiki fetch failed — {ex.Message}"); }
            });
            var hdrDbTask    = Task.Run(async () => {
                try { await _hdrDatabaseService.FetchAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] HDR database fetch failed — {ex.Message}"); }
            });
            rsTask           = Task.Run(async () => {
                try { await _rsUpdateService.EnsureLatestAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] ReShade update task failed — {ex.Message}"); }
            });
            normalRsTask     = Task.Run(async () => {
                try { await _normalRsUpdateService.EnsureLatestAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Normal ReShade update task failed — {ex.Message}"); }
            });
            osTask           = Task.Run(async () => {
                try { await _optiScalerService.EnsureStagingAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] OptiScaler staging task failed — {ex.Message}"); }
            });
            dlssTask         = Task.Run(async () => {
                try { await _optiScalerService.EnsureDlssStagingAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] DLSS staging task failed — {ex.Message}"); }
            });

            // 3. Await detection first — this never needs network
            var freshGames = await detectTask;

            // 4. Await network tasks individually so failures don't block game display
            try { await wikiTask; } catch (Exception ex) { wikiFetchFailed = true; _crashReporter.Log($"[MainViewModel.InitializeAsync] Wiki fetch failed (offline?) — {ex.Message}"); }
            try { await lumaTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Luma fetch failed (offline?) — {ex.Message}"); }
            try { _manifest = await manifestTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Manifest fetch failed — {ex.Message}"); }
            try { await osWikiTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] OptiScaler wiki task failed — {ex.Message}"); }
            try { await hdrDbTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] HDR database task failed — {ex.Message}"); }
            // rsTask deferred until after cards display
            // osTask deferred until after cards display
            // dlssTask deferred until after cards display

            // 5. Extract wiki/luma results
            var wikiResult = !wikiFetchFailed ? await wikiTask : default;
            _allMods      = wikiResult.Mods ?? new();
            _genericNotes = wikiResult.GenericNotes ?? new();
            try { _lumaMods = lumaTask.IsCompletedSuccessfully ? await lumaTask : new(); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Luma mods deserialization failed — {ex.Message}"); _lumaMods = new(); }

            // 6. Merge or use directly based on cache
            ApplyGameRenames(freshGames);
            if (hasCachedLibrary)
            {
                var cachedGames = _gameLibraryService.ToDetectedGames(savedLib!);

                // Merge: start with fresh scan, then add any cached games that weren't re-detected
                // (e.g. games on a disconnected drive). Fresh scan wins for duplicates.
                // Deduplicate by BOTH normalized name AND install path to prevent renamed games
                // from appearing twice if the rename didn't carry over (e.g. after app update).
                var freshNorms = freshGames.Select(g => _gameDetectionService.NormalizeName(g.Name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var freshPaths = freshGames
                    .Where(g => !string.IsNullOrEmpty(g.InstallPath))
                    .Select(g => g.InstallPath.TrimEnd(Path.DirectorySeparatorChar))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                detectedGames = freshGames
                    .Concat(cachedGames.Where(g =>
                        !freshNorms.Contains(_gameDetectionService.NormalizeName(g.Name))
                        && (string.IsNullOrEmpty(g.InstallPath)
                            || !freshPaths.Contains(g.InstallPath.TrimEnd(Path.DirectorySeparatorChar)))))
                    .ToList();

                _crashReporter.Log($"[MainViewModel.InitializeAsync] Merged library: {freshGames.Count} detected + {cachedGames.Count} cached → {detectedGames.Count} total");
            }
            else
            {
                detectedGames = freshGames;
                _crashReporter.Log($"[MainViewModel.InitializeAsync] Wiki fetch complete: {_allMods.Count} mods. Store scan complete: {detectedGames.Count} games.");
            }

            // Apply persisted renames so user-chosen names survive Refresh.
            ApplyGameRenames(detectedGames);

            // Apply persisted folder overrides so user-chosen paths survive Refresh.
            ApplyFolderOverrides(detectedGames);

            // Combine auto-detected + manual games.
            // Manual games override auto-detected ones with the same name.
            var manualNames = _manualGames.Select(g => _gameDetectionService.NormalizeName(g.Name))
                                          .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allGames = detectedGames
                .Where(g => !manualNames.Contains(_gameDetectionService.NormalizeName(g.Name)))
                .Concat(_manualGames)
                .ToList();

            // Apply remote manifest data before building cards (local user overrides take priority)
            ApplyManifest(_manifest);

            // Merge manifest-provided author donation URLs and display names
            if (_manifest != null)
                GameCardViewModel.MergeManifestAuthorData(_manifest.DonationUrls, _manifest.AuthorDisplayNames);

            // Apply manifest-driven wiki status overrides to mod list
            ApplyManifestStatusOverrides();

            // Remove manifest-blacklisted entries entirely (non-game apps, etc.)
            if (_manifestBlacklist.Count > 0)
                allGames = allGames.Where(g => !_manifestBlacklist.Contains(g.Name)).ToList();

            var records    = _installer.LoadAll();
            var auxRecords = _auxInstaller.LoadAll();

            // Snapshot update statuses from old cards so they survive the rebuild.
            // The background CheckForUpdatesAsync will re-verify, but this avoids
            // a visual gap where the update badge disappears until the network check completes.
            var prevUpdateStatus = new Dictionary<string, (GameStatus mod, GameStatus rs, GameStatus dc, GameStatus ul, GameStatus refFw, GameStatus os)>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _allCards)
                prevUpdateStatus[c.GameName] = (c.Status, c.RsStatus, c.DcStatus, c.UlStatus, c.RefStatus, c.OsStatus);

            SubStatusText = "Matching mods and checking install status...";

            // Ensure Nexus Mods dictionary and PCGW AppID cache are ready before building cards
            await nexusInitTask;
            await pcgwCacheTask;

            _crashReporter.Log($"[MainViewModel.InitializeAsync] Building cards for {allGames.Count} games...");
            _allCards = await Task.Run(() => BuildCards(allGames, records, auxRecords, addonCache, _genericNotes));
            _crashReporter.Log($"[MainViewModel.InitializeAsync] BuildCards complete: {_allCards.Count} cards");
            GraphicsApiDetector.SaveCache();
            SaveGameApiCache();

            // Apply manifest DLL name overrides to any existing installs whose filenames don't match
            ApplyManifestDllRenames();

            // Reconcile default naming for games without overrides (Defect 1.7)
            ReconcileDefaultNaming();

            // Carry forward UpdateAvailable status from previous cards
            foreach (var c in _allCards)
            {
                if (prevUpdateStatus.TryGetValue(c.GameName, out var prev))
                {
                    if (prev.mod == GameStatus.UpdateAvailable && c.Status == GameStatus.Installed)
                        c.Status = GameStatus.UpdateAvailable;
                    if (prev.rs == GameStatus.UpdateAvailable && c.RsStatus == GameStatus.Installed)
                        c.RsStatus = GameStatus.UpdateAvailable;
                    if (prev.dc == GameStatus.UpdateAvailable && c.DcStatus == GameStatus.Installed)
                        c.DcStatus = GameStatus.UpdateAvailable;
                    if (prev.ul == GameStatus.UpdateAvailable && c.UlStatus == GameStatus.Installed)
                        c.UlStatus = GameStatus.UpdateAvailable;
                    if (prev.refFw == GameStatus.UpdateAvailable && c.RefStatus == GameStatus.Installed)
                        c.RefStatus = GameStatus.UpdateAvailable;
                    if (prev.os == GameStatus.UpdateAvailable && c.OsStatus == GameStatus.Installed)
                        c.OsStatus = GameStatus.UpdateAvailable;
                }
            }

            // Check for updates (async, parallel, non-blocking)
            _crashReporter.Log("[MainViewModel.InitializeAsync] Starting background update checks...");
            _ = Task.Run(async () =>
            {
                try
                {
                    await CheckForUpdatesAsync(_allCards, records, auxRecords);
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[MainViewModel.InitializeAsync] Background update check failed — {ex}");
                }
            });

            _allCards = _allCards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();

            // If the previously selected game card was removed during refresh, reset selection.
            if (SelectedGame != null && !_allCards.Contains(SelectedGame))
                SelectedGame = null;

            _ = Task.Run(() => { try { SaveLibrary(); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Fire-and-forget SaveLibrary failed — {ex.Message}"); } }); // fire-and-forget — don't block UI
            _filterViewModel.SetAllCards(_allCards);
            _filterViewModel.UpdateCounts();
            _filterViewModel.ApplyFilter();

            // ── Deferred background work: ReShade staging + OptiScaler staging + shader sync ──────────────
            // These are not needed for card display, so we run them after the UI is ready.
            // rsTask (ReShade download/staging) was started earlier but not awaited.
            // osTask (OptiScaler download/staging) was started earlier but not awaited.
            // dlssTask (DLSS DLL download/staging) was started earlier but not awaited.
            // _shaderPackReadyTask (shader pack download) was started in MainWindow constructor.
            _ = Task.Run(async () =>
            {
                try
                {
                    // Wait for ReShade staging, OptiScaler staging, and DLSS staging to finish in parallel
                    await Task.WhenAll(rsTask, normalRsTask, osTask, dlssTask);
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Deferred ReShade sync failed — {ex.Message}"); }

                // Wait for shader packs to be downloaded/extracted
                if (_shaderPackReadyTask != null)
                {
                    try { await _shaderPackReadyTask; }
                    catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] ShaderPackReady failed — {ex.Message}"); }
                }

                // Deploy shaders to all installed game locations
                try
                {
                    var rsCards = _allCards
                        .Where(card => !string.IsNullOrEmpty(card.InstallPath))
                        .Where(card => card.RequiresVulkanInstall
                            ? VulkanFootprintService.Exists(card.InstallPath)
                            : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable)
                        .ToList();

                    // Ensure needed packs are downloaded (on-demand when CacheAllShaders is off)
                    var allNeededPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var card in rsCards)
                    {
                        var sel = ResolveShaderSelection(card.GameName, card.ShaderModeOverride);
                        if (sel != null) allNeededPacks.UnionWith(sel);
                    }
                    if (allNeededPacks.Count > 0)
                        await _shaderPackService.EnsurePacksAsync(allNeededPacks);

                    var syncTasks = rsCards
                        .Select(card =>
                        {
                            var effectiveSelection = ResolveShaderSelection(card.GameName, card.ShaderModeOverride);
                            return Task.Run(() => _shaderPackService.SyncGameFolder(card.InstallPath, effectiveSelection));
                        });
                    await Task.WhenAll(syncTasks);
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] SyncShaders failed — {ex.Message}"); }

                // Deploy managed addons to all installed game locations
                try
                {
                    var addonTasks = _allCards
                        .Where(card => !string.IsNullOrEmpty(card.InstallPath))
                        .Where(card => card.RequiresVulkanInstall
                            ? VulkanFootprintService.Exists(card.InstallPath)
                            : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable)
                        .Select(card =>
                        {
                            // Skip addon deployment for normal ReShade games (Req 3.1, 3.2)
                            if (card.UseNormalReShade)
                            {
                                return Task.Run(() => _addonPackService.DeployAddonsForGame(
                                    card.GameName, card.InstallPath, card.Is32Bit,
                                    useGlobalSet: true, perGameSelection: new List<string>()));
                            }

                            string addonMode = GetPerGameAddonMode(card.GameName);
                            bool useGlobalSet = addonMode != "Select";
                            List<string>? selection = useGlobalSet
                                ? _settingsViewModel.EnabledGlobalAddons
                                : (_gameNameService.PerGameAddonSelection.TryGetValue(card.GameName, out var sel) ? sel : null);
                            return Task.Run(() => _addonPackService.DeployAddonsForGame(
                                card.GameName, card.InstallPath, card.Is32Bit, useGlobalSet, selection));
                        });
                    await Task.WhenAll(addonTasks);
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] SyncAddons failed — {ex.Message}"); }

                finally
                {
                    DispatcherQueue?.TryEnqueue(() => { SubStatusText = ""; });
                }
            });

            var offlineMode = wikiFetchFailed;
            StatusText    = offlineMode
                ? $"{detectedGames.Count} games detected · offline mode (mod info unavailable)"
                : $"{detectedGames.Count} games detected · {InstalledCount} mods installed";
            SubStatusText = "";
        }
        catch (Exception ex)
        {
            StatusText = "Error loading";
            SubStatusText = ex.Message;
            _crashReporter.WriteCrashReport("InitializeAsync", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private Task<List<DetectedGame>> DetectAllGamesDedupedAsync()
        => _gameInitializationService.DetectAllGamesDedupedAsync();

    // ── Card building ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Addon filenames that are hosted at a URL different from the standard RenoDX CDN.
    /// Used to override both the mod's SnapshotUrl (install button) and the
    /// InstalledModRecord.SnapshotUrl (update detection) whenever the file is found on disk.
    /// </summary>
    private static readonly Dictionary<string, string> _addonFileUrlOverrides =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["renodx-ue-extended.addon64"] = "https://marat569.github.io/renodx/renodx-ue-extended.addon64",
    };

    /// <summary>
    /// Per-game install path overrides: maps game name to a sub-path relative to the
    /// detected root. Used when the game exe lives in a non-standard location that the
    /// engine-detection heuristics do not resolve automatically.
    /// Seeded with hardcoded defaults; the remote manifest can add more via ApplyManifest.
    /// </summary>
    private readonly Dictionary<string, string> _installPathOverrides =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cyberpunk 2077"] = @"bin\x64",
    };

    /// <summary>
    /// Returns the authoritative download URL for a given addon filename,
    /// substituting an override when the file has a known alternative source.
    /// Falls back to the generic Unreal URL for all other .addon64 files.
    /// </summary>
    private static string ResolveAddonUrl(string addonFileName)
    {
        if (_addonFileUrlOverrides.TryGetValue(addonFileName, out var url))
            return url;
        // Default: use the standard RenoDX GitHub Releases URL
        return $"https://github.com/clshortfuse/renodx/releases/download/snapshot/{addonFileName}";
    }

    private GameMod MakeGenericUnreal() => new()
    {
        Name = "Generic Unreal Engine", Maintainer = "ShortFuse",
        SnapshotUrl = WikiService.GenericUnrealUrl, Status = "✅", IsGenericUnreal = true
    };
    private GameMod MakeGenericUnity() => new()
    {
        Name = "Generic Unity Engine", Maintainer = "Voosh",
        SnapshotUrl = WikiService.GenericUnityUrl64, SnapshotUrl32 = WikiService.GenericUnityUrl32,
        Status = "✅", IsGenericUnity = true
    };

    private List<GameCardViewModel> BuildCards(
        List<DetectedGame> detectedGames,
        List<InstalledModRecord> records,
        List<AuxInstalledRecord> auxRecords,
        Dictionary<string, bool> addonCache,
        Dictionary<string, string> genericNotes)
    {
        var cards = new List<GameCardViewModel>();
        var genericUnreal = MakeGenericUnreal();
        var genericUnity  = MakeGenericUnity();

        // Load RE Framework install records for matching to cards
        var refRecords = _refService.GetRecords();

        // Thread-safe caches populated during parallel detection, saved to library afterwards.
        var newEngineTypeCache   = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newResolvedPathCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newBitnessCache      = new ConcurrentDictionary<string, MachineType>(StringComparer.OrdinalIgnoreCase);

        var gameInfos = detectedGames.AsParallel().Select(game =>
        {
            string installPath;
            EngineType engine;

            var rootKey = game.InstallPath.TrimEnd('\\', '/').ToLowerInvariant();

            // Check for manifest engine override first — if one exists, skip the
            // expensive filesystem-based engine detection entirely since the result
            // would be overridden anyway. This prevents repeated full directory
            // traversals for games with custom engine names (e.g. Northlight, Anvil)
            // that map to EngineType.Unknown and bypass the engine cache.
            var engineOverrideLabel = ResolveEngineOverride(game.Name, out var engineOverride);

            if (engineOverrideLabel != null
                && _resolvedPathCache.TryGetValue(rootKey, out var overrideCachedPath)
                && Directory.Exists(overrideCachedPath))
            {
                // Manifest override + cached path → skip detection completely
                installPath = overrideCachedPath;
                engine = engineOverride;
            }
            else if (engineOverrideLabel != null)
            {
                // Manifest override but no cached path → detect path only, use override engine
                (installPath, _) = _gameDetectionService.DetectEngineAndPath(game.InstallPath);
                engine = engineOverride;
            }
            else if (_engineTypeCache.TryGetValue(rootKey, out var cachedEngine)
                && !string.Equals(cachedEngine, nameof(EngineType.Unknown), StringComparison.OrdinalIgnoreCase)
                && _resolvedPathCache.TryGetValue(rootKey, out var cachedPath)
                && Directory.Exists(cachedPath))
            {
                installPath = cachedPath;
                engine = Enum.TryParse<EngineType>(cachedEngine, out var e) ? e : EngineType.Unknown;
            }
            else
            {
                (installPath, engine) = _gameDetectionService.DetectEngineAndPath(game.InstallPath);
            }

            // Apply manifest engine override (takes priority over auto-detection and cache)
            if (engineOverrideLabel != null) engine = engineOverride;

            // Record for saving
            newEngineTypeCache[rootKey]   = engine.ToString();
            newResolvedPathCache[rootKey] = installPath;

            // Apply per-game install path overrides (e.g. Cyberpunk 2077 → bin\x64)
            if (_installPathOverrides.TryGetValue(game.Name, out var subPath))
            {
                var overridePath = Path.Combine(game.InstallPath, subPath);
                if (Directory.Exists(overridePath))
                    installPath = overridePath;
            }

            // Detect bitness: use cached value if available, otherwise run PE detection.
            MachineType machineType;
            var resolvedKey = installPath.ToLowerInvariant();
            if (_bitnessCache.TryGetValue(resolvedKey, out var cachedMachine))
            {
                machineType = cachedMachine;
            }
            else
            {
                machineType = _peHeaderService.DetectGameArchitecture(installPath);
            }
            newBitnessCache[resolvedKey] = machineType;

            var mod      = _gameDetectionService.MatchGame(game, _allMods, _nameMappings);
            // Wiki unlink: completely disconnect the game from wiki — no mod, no generic fallback
            bool isWikiUnlinked = _manifestWikiUnlinks.Contains(game.Name);
            if (isWikiUnlinked) mod = null;
            // UnrealLegacy (UE3 and below) cannot use the RenoDX addon system — no fallback mod offered.
            var fallback = (mod == null && !isWikiUnlinked)
                           ? (engine == EngineType.Unreal      ? genericUnreal
                            : engine == EngineType.Unity       ? genericUnity : null) : null;

            // If the wiki mod matched but has no download URL (common for games listed
            // in the generic engine tables), inject the generic engine addon URL so the
            // install button works. The wiki mod's status and notes are preserved.
            if (mod != null && mod.SnapshotUrl == null && mod.NexusUrl == null && mod.DiscordUrl == null)
            {
                var engineFallback = engine == EngineType.Unreal ? genericUnreal
                                   : engine == EngineType.Unity  ? genericUnity : null;
                if (engineFallback != null)
                {
                    mod = new GameMod
                    {
                        Name            = mod.Name,
                        Maintainer      = engineFallback.Maintainer,
                        SnapshotUrl     = engineFallback.SnapshotUrl,
                        SnapshotUrl32   = engineFallback.SnapshotUrl32,
                        Status          = mod.Status,
                        Notes           = mod.Notes,
                        NameUrl         = mod.NameUrl,
                        IsGenericUnreal = engineFallback.IsGenericUnreal,
                        IsGenericUnity  = engineFallback.IsGenericUnity,
                    };
                    fallback = engineFallback;
                }
            }

            return (game, installPath, engine, mod, fallback, machineType, engineOverrideLabel);
        }).ToList();

        // Snapshot the new caches for SaveLibrary.
        _engineTypeCache   = new Dictionary<string, string>(newEngineTypeCache, StringComparer.OrdinalIgnoreCase);
        _resolvedPathCache = new Dictionary<string, string>(newResolvedPathCache, StringComparer.OrdinalIgnoreCase);
        _bitnessCache      = new Dictionary<string, MachineType>(newBitnessCache, StringComparer.OrdinalIgnoreCase);
        var newAddonFileCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var safeAddonCache = new ConcurrentDictionary<string, bool>(addonCache, StringComparer.OrdinalIgnoreCase);
        var cardBag = new ConcurrentBag<GameCardViewModel>();

        var slowGameThresholdMs = 500; // Log games that take longer than this
        var gameTimings = new ConcurrentBag<(string name, long ms)>();

        Parallel.ForEach(gameInfos, (item) =>
        {
            var gameStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var (game, installPath, engine, mod, origFallback, detectedMachine, engineOverrideLabel) = item;
            // Always show every detected game — even if no wiki mod exists.
            // The card will have no install button if there's no snapshot URL,
            // but a RenoDX addon already on disk will still be detected and shown.
            // Wiki exclusion overrides everything — user explicitly wants no wiki match
            var fallback     = origFallback;  // mutable local copy
            var effectiveMod = _wikiExclusions.Contains(game.Name) ? null : (mod ?? fallback);

            var record = records.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase));

            // ── Path reconciliation for RenoDX mod records ────────────────────────
            // Xbox/Microsoft Store games change install paths on every update
            // (e.g. version number embedded in the WindowsApps folder name).
            // When the record's GameName matches but InstallPath differs, try to
            // migrate the addon file to the new path so the mod stays detected.
            if (record != null
                && !record.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = record.InstallPath;
                var addonFile = record.AddonFileName;
                var newFilePath = string.IsNullOrEmpty(addonFile) ? null : Path.Combine(installPath, addonFile);
                var oldFilePath = string.IsNullOrEmpty(addonFile) ? null : Path.Combine(oldPath, addonFile);

                if (newFilePath != null && File.Exists(newFilePath))
                {
                    // Addon already exists at the new path (user may have reinstalled)
                    _crashReporter.Log($"[BuildCards] Path reconciliation: '{game.Name}' path changed '{oldPath}' → '{installPath}', addon already at new path");
                }
                else if (oldFilePath != null && File.Exists(oldFilePath))
                {
                    // Try to copy the addon from the old path to the new path
                    try
                    {
                        var newDeployDir = Path.GetDirectoryName(newFilePath!)!;
                        Directory.CreateDirectory(newDeployDir);
                        File.Copy(oldFilePath, newFilePath!, overwrite: true);
                        _crashReporter.Log($"[BuildCards] Path reconciliation: '{game.Name}' copied addon '{addonFile}' from '{oldPath}' → '{installPath}'");
                    }
                    catch (Exception ex)
                    {
                        // WindowsApps or other restricted paths may deny access — that's OK
                        _crashReporter.Log($"[BuildCards] Path reconciliation: '{game.Name}' failed to copy addon from '{oldPath}' → '{installPath}' — {ex.Message}");
                    }
                }
                else
                {
                    _crashReporter.Log($"[BuildCards] Path reconciliation: '{game.Name}' path changed '{oldPath}' → '{installPath}', addon not found at either path (mod lost during game update)");
                }

                // Always update the record to the new detected path
                record.InstallPath = installPath;
                _installer.SaveRecordPublic(record);
            }

            // Fallback: match by InstallPath for records saved with mod name instead of game name
            // (e.g. "Generic Unreal Engine" from before the fix).
            if (record == null)
            {
                record = records.FirstOrDefault(r =>
                    r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
                if (record != null)
                {
                    // Fix the record's GameName so future lookups work correctly
                    record.GameName = game.Name;
                    _installer.SaveRecordPublic(record);
                }
            }

            // Always scan disk for renodx-* addon files — catches manual installs and
            // games not yet on the wiki that already have a mod installed.
            // Use the addon file cache to skip expensive recursive scans on subsequent launches.
            string? addonOnDisk = null;
            var cacheKey = installPath.ToLowerInvariant();

            // If we have a DB record, always verify the file is still on disk — never
            // rely on the addon file cache alone, because the cache may be stale
            // (e.g. mod was installed/uninstalled since the last BuildCards).
            if (record != null)
            {
                var expectedFile = record.AddonFileName;
                if (!string.IsNullOrEmpty(expectedFile)
                    && File.Exists(Path.Combine(installPath, expectedFile)))
                {
                    addonOnDisk = expectedFile;
                }
                else
                {
                    // Record exists but file not at expected location — rescan
                    addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
                }
            }
            else if (_addonFileCache.TryGetValue(cacheKey, out var cachedAddonFile))
            {
                if (!string.IsNullOrEmpty(cachedAddonFile)
                    && File.Exists(Path.Combine(installPath, cachedAddonFile)))
                {
                    addonOnDisk = cachedAddonFile;
                }
                else if (!string.IsNullOrEmpty(cachedAddonFile))
                {
                    // Cache says an addon was here but the file is gone — rescan
                    addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
                }
                // else: cache says "" (no addon found on previous scan) — trust it
                // and skip the expensive recursive scan. A Full Refresh clears the
                // cache, so newly installed addons will be found on the next rescan.
            }
            else if (safeAddonCache.TryGetValue(cacheKey, out _))
            {
                addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
            }
            else
            {
                addonOnDisk = ScanForInstalledAddon(installPath, effectiveMod);
                safeAddonCache[cacheKey] = addonOnDisk != null;
            }
            newAddonFileCache[cacheKey] = addonOnDisk ?? "";

            if (addonOnDisk != null && record == null)
            {
                // Use ResolveAddonUrl so files like renodx-ue-extended.addon64 get their
                // correct source URL rather than the generic CDN URL from effectiveMod.
                record = new InstalledModRecord
                {
                    GameName      = game.Name,
                    InstallPath   = installPath,
                    AddonFileName = addonOnDisk,
                    InstalledAt   = File.GetLastWriteTimeUtc(Path.Combine(installPath, addonOnDisk)),
                    SnapshotUrl   = ResolveAddonUrl(addonOnDisk),
                };
                _installer.SaveRecordPublic(record);
            }
            else if (addonOnDisk == null && record != null)
            {
                // DB record exists but addon file is no longer on disk — user manually removed it.
                // Remove the stale record so the card shows Available rather than Installed.
                _installer.RemoveRecord(record);
                record = null;
            }

            // If the installed addon on disk has a different source URL than what the
            // wiki mod specifies (e.g. renodx-ue-extended.addon64 on a generic UE card),
            // patch effectiveMod so the install/update button uses the correct URL.
            if (addonOnDisk != null && effectiveMod?.SnapshotUrl != null
                && _addonFileUrlOverrides.TryGetValue(addonOnDisk, out var addonOverrideUrl))
            {
                effectiveMod = new GameMod
                {
                    Name        = effectiveMod.Name,
                    Maintainer  = effectiveMod.Maintainer,
                    SnapshotUrl = addonOverrideUrl,
                    Status      = effectiveMod.Status,
                    Notes       = effectiveMod.Notes,
                    NexusUrl    = effectiveMod.NexusUrl,
                    DiscordUrl  = effectiveMod.DiscordUrl,
                    NameUrl     = effectiveMod.NameUrl,
                    IsGenericUnreal = effectiveMod.IsGenericUnreal,
                    IsGenericUnity  = effectiveMod.IsGenericUnity,
                };
            }

            // Named addon found on disk but no wiki entry exists → show Discord link
            // so the user can find support/info for their mod.
            if (addonOnDisk != null && effectiveMod == null)
            {
                effectiveMod = new GameMod
                {
                    Name       = game.Name,
                    Status     = "💬",
                    DiscordUrl = "https://discord.gg/gF4GRJWZ2A",
                };
            }

            // ── Manifest snapshot override ────────────────────────────────────────
            // If the manifest provides a direct snapshot URL for this game, inject it
            // into the effectiveMod. This handles cases where the wiki parser fails to
            // capture the snapshot link or the name mapping doesn't resolve correctly.
            if (_manifest?.SnapshotOverrides != null
                && _manifest.SnapshotOverrides.TryGetValue(game.Name, out var snapshotOverrideUrl)
                && !string.IsNullOrEmpty(snapshotOverrideUrl))
            {
                if (effectiveMod != null)
                {
                    effectiveMod.SnapshotUrl = snapshotOverrideUrl;
                }
                else
                {
                    effectiveMod = new GameMod
                    {
                        Name        = game.Name,
                        SnapshotUrl = snapshotOverrideUrl,
                        Status      = "✅",
                    };
                }
            }

            // Apply UE-Extended preference: if the game has it saved OR the file is on disk,
            // force the Mod URL to the marat569 source so the install button targets it.
            // Native HDR games always use UE-Extended, regardless of user toggle.
            // UE-Extended whitelist supersedes everything — hide Nexus link and force install/update/reinstall.
            bool isNativeHdr = IsNativeHdrGameMatch(game.Name);
            bool useUeExt = (addonOnDisk == UeExtendedFile)
                            || IsUeExtendedGameMatch(game.Name)
                            || (isNativeHdr && (effectiveMod?.IsGenericUnreal == true || engine == EngineType.Unreal));
            if (useUeExt && effectiveMod != null)
            {
                // Create or override the mod to use UE-Extended URL
                effectiveMod = new GameMod
                {
                    Name            = effectiveMod?.Name ?? "Generic Unreal Engine",
                    Maintainer      = effectiveMod?.Maintainer ?? "ShortFuse",
                    SnapshotUrl     = UeExtendedUrl,
                    Status          = effectiveMod?.Status ?? "✅",
                    Notes           = effectiveMod?.Notes,
                    IsGenericUnreal = true,
                };
                // Persist preference if it was detected from disk or the game is native HDR
                if (addonOnDisk == UeExtendedFile || isNativeHdr)
                    _ueExtendedGames.Add(game.Name);
            }
            // UE-Extended whitelist games that have no engine detected — force them to use UE-Extended
            else if (useUeExt && effectiveMod == null)
            {
                effectiveMod = new GameMod
                {
                    Name            = "Generic Unreal Engine",
                    Maintainer      = "ShortFuse",
                    SnapshotUrl     = UeExtendedUrl,
                    Status          = "✅",
                    IsGenericUnreal = true,
                };
                fallback = effectiveMod;
                if (isNativeHdr)
                    _ueExtendedGames.Add(game.Name);
            }

            // UE-Extended whitelist supersedes Nexus/Discord external links — force installable
            if (useUeExt && effectiveMod != null)
            {
                // Strip Nexus/Discord links so the card shows install/update/reinstall buttons
                effectiveMod.NexusUrl   = null;
                effectiveMod.DiscordUrl = null;
            }

            // Look up aux records for this game
            var rsRec = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                (r.AddonType == AuxInstallService.TypeReShade || r.AddonType == AuxInstallService.TypeReShadeNormal));

            // ── Path reconciliation for ReShade aux records ───────────────────────
            // Same Xbox/Microsoft Store path-change issue as RenoDX mod records above.
            if (rsRec != null
                && !rsRec.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase))
            {
                var oldRsPath = rsRec.InstallPath;
                var rsFile = rsRec.InstalledAs;
                var newRsFilePath = string.IsNullOrEmpty(rsFile) ? null : Path.Combine(installPath, rsFile);
                var oldRsFilePath = string.IsNullOrEmpty(rsFile) ? null : Path.Combine(oldRsPath, rsFile);

                if (newRsFilePath != null && File.Exists(newRsFilePath))
                {
                    _crashReporter.Log($"[BuildCards] RS path reconciliation: '{game.Name}' path changed '{oldRsPath}' → '{installPath}', ReShade already at new path");
                }
                else if (oldRsFilePath != null && File.Exists(oldRsFilePath))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newRsFilePath!)!);
                        File.Copy(oldRsFilePath, newRsFilePath!, overwrite: true);
                        _crashReporter.Log($"[BuildCards] RS path reconciliation: '{game.Name}' copied ReShade '{rsFile}' from '{oldRsPath}' → '{installPath}'");
                    }
                    catch (Exception ex)
                    {
                        _crashReporter.Log($"[BuildCards] RS path reconciliation: '{game.Name}' failed to copy ReShade from '{oldRsPath}' → '{installPath}' — {ex.Message}");
                    }
                }
                else
                {
                    _crashReporter.Log($"[BuildCards] RS path reconciliation: '{game.Name}' path changed '{oldRsPath}' → '{installPath}', ReShade not found at either path");
                }

                rsRec.InstallPath = installPath;
                _auxInstaller.SaveAuxRecord(rsRec);
            }

            // Verify DB records against disk — if the file no longer exists the record is stale.
            // This handles the case where the user manually deleted files without using RDXC.
            if (rsRec != null && !File.Exists(Path.Combine(rsRec.InstallPath, rsRec.InstalledAs)))
            {
                _auxInstaller.RemoveRecord(rsRec);
                rsRec = null;
            }

            // ── Disk detection for ReShade ────────────────────────────────────────
            // If no DB record exists, scan disk for the known filenames so that
            // manually installed or previously installed instances are shown correctly.
            //
            // IMPORTANT: Skip filenames that are already claimed by DC via its
            // AuxInstalledRecord or DLL override config, to avoid misidentifying
            // a renamed DC file as ReShade.
            var dcRecForExclusion = auxRecords.FirstOrDefault(r =>
                r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                r.AddonType == "DisplayCommander");
            var dcClaimedFileName = dcRecForExclusion?.InstalledAs;

            if (rsRec == null)
            {
                // dxgi.dll — only attribute to ReShade if positively identified as ReShade
                // AND not already claimed by DC
                var dxgiPath = Path.Combine(installPath, AuxInstallService.RsNormalName);
                bool dxgiClaimedByDc = dcClaimedFileName != null &&
                    dcClaimedFileName.Equals(AuxInstallService.RsNormalName, StringComparison.OrdinalIgnoreCase);
                if (!dxgiClaimedByDc && File.Exists(dxgiPath) && AuxInstallService.IsReShadeFile(dxgiPath))
                {
                    rsRec = new AuxInstalledRecord
                    {
                        GameName    = game.Name,
                        InstallPath = installPath,
                        AddonType   = AuxInstallService.TypeReShade,
                        InstalledAs = AuxInstallService.RsNormalName,
                        InstalledAt = File.GetLastWriteTimeUtc(dxgiPath),
                    };
                }
                else
                {
                    // Content-based fallback: scan known proxy DLL names for ReShade binary signatures.
                    // ReShade can only inject via specific Windows system DLL proxies, so we only
                    // check those names rather than every DLL in the folder.
                    // Skip WindowsApps paths — always access-denied
                    bool isWinAppsRs = installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                                    || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase);
                    if (!isWinAppsRs)
                    try
                    {
                        foreach (var proxyName in DllOverrideConstants.CommonDllNames)
                        {
                            // Skip filenames already checked above
                            if (proxyName.Equals(AuxInstallService.RsNormalName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // Skip filenames claimed by DC via override
                            if (dcClaimedFileName != null &&
                                proxyName.Equals(dcClaimedFileName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var candidatePath = Path.Combine(installPath, proxyName);
                            if (!File.Exists(candidatePath))
                                continue;

                            if (AuxInstallService.IsReShadeFileStrict(candidatePath))
                            {
                                rsRec = new AuxInstalledRecord
                                {
                                    GameName    = game.Name,
                                    InstallPath = installPath,
                                    AddonType   = AuxInstallService.TypeReShade,
                                    InstalledAs = proxyName,
                                    InstalledAt = File.GetLastWriteTimeUtc(candidatePath),
                                };
                                break;
                            }
                        }
                    }
                    catch (Exception) { /* Permission or IO errors — skip gracefully */ }
                }
            }

            var newCard = new GameCardViewModel
            {
                GameName               = game.Name,
                Mod                    = effectiveMod,
                DetectedGame           = game,
                InstallPath            = installPath,
                Source                 = game.Source,
                InstalledRecord        = record,
                Status                 = record != null ? GameStatus.Installed : GameStatus.Available,
                WikiStatus             = (_wikiExclusions.Contains(game.Name)
                                           || (effectiveMod?.SnapshotUrl == null && effectiveMod?.DiscordUrl != null && effectiveMod?.NexusUrl == null))
                                          ? "💬"
                                          : (mod == null && fallback != null && !useUeExt && !isNativeHdr)
                                            ? "?"
                                            : effectiveMod?.Status ?? "—",
                Maintainer             = effectiveMod?.Maintainer ?? "",
                IsGenericMod           = useUeExt || (fallback != null && mod == null),
                EngineHint             = engineOverrideLabel != null
                                       ? (useUeExt && engine == EngineType.Unknown ? "Unreal Engine" : engineOverrideLabel)
                                       : (useUeExt && engine == EngineType.Unknown) ? "Unreal Engine"
                                       : engine == EngineType.Unreal       ? "Unreal Engine"
                                       : engine == EngineType.UnrealLegacy ? "Unreal (Legacy)"
                                       : engine == EngineType.Unity        ? "Unity"
                                       : engine == EngineType.REEngine     ? "RE Engine" : "",
                Notes                  = effectiveMod != null ? BuildNotes(game.Name, effectiveMod, fallback, genericNotes, isNativeHdr) : "",
                InstalledAddonFileName = record?.AddonFileName,
                RdxInstalledVersion = record != null ? AuxInstallService.ReadInstalledVersion(record.InstallPath, record.AddonFileName) : null,
                IsHidden               = _hiddenGames.Contains(game.Name),
                IsFavourite            = _favouriteGames.Contains(game.Name),
                IsManuallyAdded        = game.IsManuallyAdded,
                UseUeExtended          = useUeExt,
                IsExternalOnly         = _wikiExclusions.Contains(game.Name)
                                         ? true
                                         : effectiveMod?.SnapshotUrl == null &&
                                           (effectiveMod?.NexusUrl != null || effectiveMod?.DiscordUrl != null),
                ExternalUrl            = _wikiExclusions.Contains(game.Name)
                                         ? "https://discord.gg/gF4GRJWZ2A"
                                         : effectiveMod?.NexusUrl ?? effectiveMod?.DiscordUrl ?? "",
                ExternalLabel          = _wikiExclusions.Contains(game.Name)
                                         ? "Download from Discord"
                                         : effectiveMod?.NexusUrl != null ? "Download from Nexus Mods" : "Download from Discord",
                NexusUrl               = effectiveMod?.NexusUrl,
                DiscordUrl             = _wikiExclusions.Contains(game.Name)
                                         ? "https://discord.gg/gF4GRJWZ2A"
                                         : effectiveMod?.DiscordUrl,
                NameUrl                = effectiveMod?.NameUrl,
                ExcludeFromUpdateAllReShade = _gameNameService.UpdateAllExcludedReShade.Contains(game.Name),
                ExcludeFromUpdateAllRenoDx  = _gameNameService.UpdateAllExcludedRenoDx.Contains(game.Name),
                ExcludeFromUpdateAllUl      = _gameNameService.UpdateAllExcludedUl.Contains(game.Name),
                ExcludeFromUpdateAllDc      = _gameNameService.UpdateAllExcludedDc.Contains(game.Name),
                ExcludeFromUpdateAllOs      = _gameNameService.UpdateAllExcludedOs.Contains(game.Name),
                ExcludeFromUpdateAllRef     = _gameNameService.UpdateAllExcludedRef.Contains(game.Name),
                UseNormalReShade           = _gameNameService.NormalReShadeGames.Contains(game.Name),
                ShaderModeOverride     = _perGameShaderMode.TryGetValue(game.Name, out var smBc) ? smBc : null,
                Is32Bit                = ResolveIs32Bit(game.Name, detectedMachine),
                GraphicsApi            = DetectGraphicsApi(installPath, engine, game.Name),
                DetectedApis           = _DetectAllApisForCard(installPath, game.Name),
                VulkanRenderingPath    = _vulkanRenderingPaths.TryGetValue(game.Name, out var vrpBc) ? vrpBc : "DirectX",
                DllOverrideEnabled     = _dllOverrides.ContainsKey(game.Name),
                IsNativeHdrGame        = isNativeHdr,
                IsManifestUeExtended   = useUeExt && !isNativeHdr,
                RsRecord               = rsRec,
                RsStatus               = rsRec != null ? GameStatus.Installed : GameStatus.NotInstalled,
                RsInstalledFile        = rsRec?.InstalledAs,
                RsInstalledVersion     = rsRec != null ? AuxInstallService.ReadInstalledVersion(rsRec.InstallPath, rsRec.InstalledAs) : null,
                IsREEngineGame         = engine == EngineType.REEngine,
            };

            // ── Luma matching ──────────────────────────────────────────────────────
            newCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(newCard.DetectedApis);

            // Cache the API detection results for subsequent launches
            if (!string.IsNullOrEmpty(installPath))
                CacheGameApi(installPath, newCard.GraphicsApi, newCard.DetectedApis);

            // For Vulkan games, RS is installed when reshade.ini exists in the game folder.
            if (newCard.RequiresVulkanInstall)
            {
                bool rsIniExists = File.Exists(Path.Combine(newCard.InstallPath, "reshade.ini"));
                newCard.RsStatus = rsIniExists ? GameStatus.Installed : GameStatus.NotInstalled;
                newCard.RsInstalledVersion = rsIniExists
                    ? AuxInstallService.ReadInstalledVersion(VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName)
                    : null;
            }

            newCard.LumaFeatureEnabled = LumaFeatureEnabled;

            // ── ReLimiter detection ────────────────────────────────────────────
            if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                var ulDeployPath = ModInstallService.GetAddonDeployPath(installPath);
                var ulFileName = GetUlFileName(newCard.Is32Bit);
                var legacyUlFileName = newCard.Is32Bit ? LegacyUltraLimiterFileName32 : LegacyUltraLimiterFileName;
                if (File.Exists(Path.Combine(ulDeployPath, ulFileName))
                    || File.Exists(Path.Combine(installPath, ulFileName))
                    || File.Exists(Path.Combine(ulDeployPath, legacyUlFileName))
                    || File.Exists(Path.Combine(installPath, legacyUlFileName)))
                {
                    newCard.UlStatus = GameStatus.Installed;
                    newCard.UlInstalledFile = ulFileName;
                    newCard.UlInstalledVersion = ReadUlInstalledVersion(newCard.Is32Bit);
                }
            }

            // ── Display Commander detection ────────────────────────────────────
            if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
            {
                var dcDeployPath = ModInstallService.GetAddonDeployPath(installPath);
                var dcFileName = GetDcFileName(newCard.Is32Bit);

                // Check for default DC addon files on disk
                if (File.Exists(Path.Combine(dcDeployPath, dcFileName))
                    || File.Exists(Path.Combine(installPath, dcFileName)))
                {
                    newCard.DcStatus = GameStatus.Installed;
                    newCard.DcInstalledFile = dcFileName;
                    // Try PE file version first, fall back to meta
                    var dcFilePath = File.Exists(Path.Combine(dcDeployPath, dcFileName))
                        ? Path.Combine(dcDeployPath, dcFileName)
                        : Path.Combine(installPath, dcFileName);
                    var peVer = AuxInstallService.ReadInstalledVersion(
                        Path.GetDirectoryName(dcFilePath)!, Path.GetFileName(dcFilePath));
                    var metaVer = ReadDcInstalledVersion(newCard.Is32Bit);
                    if (metaVer == "latest_build") metaVer = null;
                    newCard.DcInstalledVersion = peVer ?? metaVer;
                }
                // Also detect legacy DC Lite files for migration
                else
                {
                    var legacyDcFileName = GetLegacyDcFileName(newCard.Is32Bit);
                    if (File.Exists(Path.Combine(dcDeployPath, legacyDcFileName))
                        || File.Exists(Path.Combine(installPath, legacyDcFileName)))
                    {
                        newCard.DcStatus = GameStatus.UpdateAvailable;
                        newCard.DcInstalledFile = legacyDcFileName;
                        var legacyFilePath = File.Exists(Path.Combine(dcDeployPath, legacyDcFileName))
                            ? Path.Combine(dcDeployPath, legacyDcFileName)
                            : Path.Combine(installPath, legacyDcFileName);
                        var peVer = AuxInstallService.ReadInstalledVersion(
                            Path.GetDirectoryName(legacyFilePath)!, Path.GetFileName(legacyFilePath));
                        var metaVer = ReadDcInstalledVersion(newCard.Is32Bit);
                        if (metaVer == "latest_build") metaVer = null;
                        newCard.DcInstalledVersion = peVer ?? metaVer;
                        _crashReporter.Log($"[BuildCards] Legacy DC Lite detected for '{game.Name}' — marking for migration");
                    }
                    else
                    {
                        // Check for DC with custom DLL override name via AuxInstalledRecord
                        var dcRec = auxRecords.FirstOrDefault(r =>
                            r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                            r.AddonType == "DisplayCommander");

                        // ── Path reconciliation for DC aux records ────────────────────
                        if (dcRec != null
                            && !dcRec.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase))
                        {
                            var oldDcPath = dcRec.InstallPath;
                            var dcFile = dcRec.InstalledAs;
                            var newDcFilePath = string.IsNullOrEmpty(dcFile) ? null : Path.Combine(installPath, dcFile);
                            var oldDcFilePath = string.IsNullOrEmpty(dcFile) ? null : Path.Combine(oldDcPath, dcFile);

                            if (newDcFilePath != null && File.Exists(newDcFilePath))
                            {
                                _crashReporter.Log($"[BuildCards] DC path reconciliation: '{game.Name}' path changed, DC already at new path");
                            }
                            else if (oldDcFilePath != null && File.Exists(oldDcFilePath))
                            {
                                try
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(newDcFilePath!)!);
                                    File.Copy(oldDcFilePath, newDcFilePath!, overwrite: true);
                                    _crashReporter.Log($"[BuildCards] DC path reconciliation: '{game.Name}' copied DC '{dcFile}' from '{oldDcPath}' → '{installPath}'");
                                }
                                catch (Exception ex)
                                {
                                    _crashReporter.Log($"[BuildCards] DC path reconciliation: '{game.Name}' failed to copy DC — {ex.Message}");
                                }
                            }
                            else
                            {
                                _crashReporter.Log($"[BuildCards] DC path reconciliation: '{game.Name}' path changed, DC not found at either path");
                            }

                            dcRec.InstallPath = installPath;
                            _auxInstaller.SaveAuxRecord(dcRec);
                        }

                        if (dcRec != null && File.Exists(Path.Combine(dcRec.InstallPath, dcRec.InstalledAs)))
                        {
                            newCard.DcStatus = GameStatus.Installed;
                            newCard.DcInstalledFile = dcRec.InstalledAs;
                            var peVer2 = AuxInstallService.ReadInstalledVersion(dcRec.InstallPath, dcRec.InstalledAs);
                            var metaVer2 = ReadDcInstalledVersion(newCard.Is32Bit);
                            if (metaVer2 == "latest_build") metaVer2 = null;
                            newCard.DcInstalledVersion = peVer2 ?? metaVer2;
                        }
                        else if (dcRec != null)
                        {
                            // Record exists but file not on disk — stale record
                            _auxInstaller.RemoveRecord(dcRec);
                        }
                    }
                }
            }

            // ── OptiScaler detection ───────────────────────────────────────
            if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath) && !newCard.Is32Bit)
            {
                // First check for an existing tracking record
                var osRec = auxRecords.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                    r.AddonType == OptiScalerService.AddonType);

                // ── Path reconciliation for OptiScaler aux records ────────────────
                if (osRec != null
                    && !osRec.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase))
                {
                    var oldOsPath = osRec.InstallPath;
                    var osFile = osRec.InstalledAs;
                    var newOsFilePath = string.IsNullOrEmpty(osFile) ? null : Path.Combine(installPath, osFile);
                    var oldOsFilePath = string.IsNullOrEmpty(osFile) ? null : Path.Combine(oldOsPath, osFile);

                    if (newOsFilePath != null && File.Exists(newOsFilePath))
                    {
                        _crashReporter.Log($"[BuildCards] OS path reconciliation: '{game.Name}' path changed, OptiScaler already at new path");
                    }
                    else if (oldOsFilePath != null && File.Exists(oldOsFilePath))
                    {
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(newOsFilePath!)!);
                            File.Copy(oldOsFilePath, newOsFilePath!, overwrite: true);
                            _crashReporter.Log($"[BuildCards] OS path reconciliation: '{game.Name}' copied OptiScaler '{osFile}' from '{oldOsPath}' → '{installPath}'");
                        }
                        catch (Exception ex)
                        {
                            _crashReporter.Log($"[BuildCards] OS path reconciliation: '{game.Name}' failed to copy OptiScaler — {ex.Message}");
                        }
                    }
                    else
                    {
                        _crashReporter.Log($"[BuildCards] OS path reconciliation: '{game.Name}' path changed, OptiScaler not found at either path");
                    }

                    osRec.InstallPath = installPath;
                    _auxInstaller.SaveAuxRecord(osRec);
                }

                if (osRec != null && File.Exists(Path.Combine(osRec.InstallPath, osRec.InstalledAs)))
                {
                    newCard.OsStatus = GameStatus.Installed;
                    newCard.OsInstalledFile = osRec.InstalledAs;
                    newCard.OsInstalledVersion = _optiScalerService.StagedVersion;
                }
                else if (osRec != null)
                {
                    // Record exists but file not on disk — stale record (OptiScaler manually deleted)
                    _auxInstaller.RemoveRecord(osRec);

                    // If ReShade64.dll exists, rename it back to the correct ReShade filename
                    try
                    {
                        var rsCoexistPath = Path.Combine(installPath, OptiScalerService.ReShadeCoexistName);
                        if (File.Exists(rsCoexistPath))
                        {
                            var resolvedName = _dllOverrideService.GetEffectiveRsName(game.Name);
                            var resolvedPath = Path.Combine(installPath, resolvedName);

                            if (!resolvedName.Equals(OptiScalerService.ReShadeCoexistName, StringComparison.OrdinalIgnoreCase)
                                && !File.Exists(resolvedPath))
                            {
                                File.Move(rsCoexistPath, resolvedPath);
                                CrashReporter.Log($"[BuildCards] Restored ReShade '{OptiScalerService.ReShadeCoexistName}' → '{resolvedName}' for {game.Name}");

                                // Update ReShade tracking record
                                var rsRecord = auxRecords.FirstOrDefault(r =>
                                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase) &&
                                    (r.AddonType == AuxInstallService.TypeReShade || r.AddonType == AuxInstallService.TypeReShadeNormal));
                                if (rsRecord != null)
                                {
                                    rsRecord.InstalledAs = resolvedName;
                                    _auxInstaller.SaveAuxRecord(rsRecord);
                                }

                                // Update card RS state
                                newCard.RsInstalledFile = resolvedName;
                            }
                        }
                    }
                    catch (Exception rsEx)
                    {
                        CrashReporter.Log($"[BuildCards] ReShade restore after stale OS record failed for {game.Name} — {rsEx.Message}");
                    }
                }
                else
                {
                    // No tracking record — try binary signature detection
                    // Skip WindowsApps paths — always access-denied, wastes time on retries
                    bool isWindowsApps = installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
                                      || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase);
                    var detectedDll = isWindowsApps ? null : _optiScalerService.DetectInstallation(installPath);
                    if (detectedDll != null)
                    {
                        // Create a tracking record for the detected installation
                        var newOsRec = new AuxInstalledRecord
                        {
                            GameName    = game.Name,
                            InstallPath = installPath,
                            AddonType   = OptiScalerService.AddonType,
                            InstalledAs = detectedDll,
                            InstalledAt = File.GetLastWriteTimeUtc(Path.Combine(installPath, detectedDll)),
                        };
                        _auxInstaller.SaveAuxRecord(newOsRec);

                        newCard.OsStatus = GameStatus.Installed;
                        newCard.OsInstalledFile = detectedDll;
                        newCard.OsInstalledVersion = _optiScalerService.StagedVersion;
                    }
                }
            }

            // ── RE Framework record matching ───────────────────────────────────
            if (newCard.IsREEngineGame)
            {
                var refRec = refRecords.FirstOrDefault(r =>
                    r.GameName.Equals(game.Name, StringComparison.OrdinalIgnoreCase));
                if (refRec != null)
                {
                    newCard.RefRecord = refRec;
                    newCard.RefStatus = GameStatus.Installed;
                    newCard.RefInstalledVersion = refRec.InstalledVersion;
                }
            }

            var lumaMatch = MatchLumaGame(game.Name);
            if (lumaMatch != null)
            {
                newCard.LumaMod = lumaMatch;

                // Auto-enable Luma for manifest-listed games (unless user explicitly disabled)
                if (_manifest?.LumaDefaultGames != null
                    && !_lumaEnabledGames.Contains(game.Name)
                    && !_lumaDisabledGames.Contains(game.Name)
                    && _manifest.LumaDefaultGames.Any(g => g.Equals(game.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    _lumaEnabledGames.Add(game.Name);
                }

                newCard.IsLumaMode = _lumaEnabledGames.Contains(game.Name);
                // Check if Luma is installed on disk
                var lumaRec = LumaService.GetRecordByPath(installPath);
                if (lumaRec != null)
                {
                    newCard.LumaRecord = lumaRec;
                    newCard.LumaStatus = GameStatus.Installed;
                }
            }

            // ── Nexus Mods & PCGW link resolution ──────────────────────────────
            try
            {
                newCard.NexusModsUrl = _nexusModsService.ResolveUrl(game.Name, _manifest);
            }
            catch (Exception ex) { _crashReporter.Log($"[BuildCards] NexusModsUrl resolve failed for '{game.Name}' — {ex.Message}"); }

            try
            {
                newCard.PcgwUrl = _pcgwService.ResolveUrlAsync(game.Name, game.SteamAppId, installPath, _manifest)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex) { _crashReporter.Log($"[BuildCards] PcgwUrl resolve failed for '{game.Name}' — {ex.Message}"); }

            cardBag.Add(newCard);

            gameStopwatch.Stop();
            var elapsedMs = gameStopwatch.ElapsedMilliseconds;
            gameTimings.Add((game.Name, elapsedMs));
            if (elapsedMs > slowGameThresholdMs)
                _crashReporter.Log($"[BuildCards] SLOW: '{game.Name}' took {elapsedMs}ms ({installPath})");
            });

        cards.AddRange(cardBag);

        // Log BuildCards timing summary
        var sortedTimings = gameTimings.OrderByDescending(t => t.ms).ToList();
        var totalBuildMs = sortedTimings.Sum(t => t.ms);
        var slowCount = sortedTimings.Count(t => t.ms > slowGameThresholdMs);
        _crashReporter.Log($"[BuildCards] Timing: {sortedTimings.Count} games, {slowCount} slow (>{slowGameThresholdMs}ms), total CPU time {totalBuildMs}ms");
        foreach (var (name, ms) in sortedTimings.Take(10))
            _crashReporter.Log($"[BuildCards] Top: '{name}' = {ms}ms");

        ApplyCardOverrides(cards);
        ApplyManifestCardOverrides(_manifest, cards);

        // Persist the addon file cache for next launch.
        _addonFileCache = new Dictionary<string, string>(newAddonFileCache, StringComparer.OrdinalIgnoreCase);

        return cards;
    }

    private string BuildNotes(string gameName, GameMod effectiveMod, GameMod? fallback, Dictionary<string, string> genericNotes, bool isNativeHdr = false)
    {
        // Native HDR / UE-Extended whitelisted games always get the HDR warning,
        // whether they have a specific wiki mod or are using the generic UE fallback.
        if (isNativeHdr)
        {
            var parts = new List<string>();
            parts.Add("⚠ In-game HDR must be turned ON for UE-Extended to work correctly in this title.");

            // Include wiki tooltip if present (from a specific mod entry)
            if (fallback == null && !string.IsNullOrWhiteSpace(effectiveMod.Notes))
            {
                parts.Add("");
                parts.Add(effectiveMod.Notes);
            }

            // Do NOT include generic UE game-specific settings — these are for the
            // generic addon, not UE-Extended. UE-Extended whitelisted games don't
            // need generic addon installation guidance.

            return string.Join("\n", parts);
        }

        // Specific mod — wiki tooltip note (may be null/empty if no tooltip)
        if (fallback == null) return effectiveMod.Notes ?? "";

        var notesParts = new List<string>();

        if (effectiveMod.IsGenericUnreal)
        {
            var specific = GetGenericNote(gameName, genericNotes);
            if (!string.IsNullOrEmpty(specific))
            {
                notesParts.Add("📋 Game-specific settings:");
                notesParts.Add(specific);
            }
            notesParts.Add(UnrealWarnings);
        }
        else // Unity
        {
            var specific = GetGenericNote(gameName, genericNotes);
            if (!string.IsNullOrEmpty(specific))
            {
                notesParts.Add("📋 Game-specific settings:");
                notesParts.Add(specific);
            }
        }

        return string.Join("\n", notesParts);
    }

    private static string? ScanForInstalledAddon(string installPath, GameMod? mod)
    {
        if (!Directory.Exists(installPath)) return null;
        // Skip WindowsApps paths — always access-denied for file scanning
        if (installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            // Check the AddonPath subfolder from reshade.ini first
            var addonSearchPath = ModInstallService.ResolveAddonSearchPath(installPath);
            if (addonSearchPath != null && Directory.Exists(addonSearchPath))
            {
                if (mod?.AddonFileName != null && File.Exists(Path.Combine(addonSearchPath, mod.AddonFileName)))
                    return mod.AddonFileName;
                foreach (var ext in new[] { "*.addon64", "*.addon32" })
                {
                    var found = Directory.GetFiles(addonSearchPath, ext)
                        .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                    if (found != null) return Path.GetFileName(found);
                }
            }

            if (mod?.AddonFileName != null && File.Exists(Path.Combine(installPath, mod.AddonFileName)))
                return mod.AddonFileName;
            // First try direct files in the folder
            foreach (var ext in new[] { "*.addon64", "*.addon32" })
            {
                var found = Directory.GetFiles(installPath, ext)
                    .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                if (found != null) return Path.GetFileName(found);
            }

            // Search common subdirectories (Binaries/Win64, Binaries/Win32) and fallback to a limited recursive search
            var commonPaths = new[] { "Binaries\\Win64", "Binaries\\Win32", "Binaries\\x86", "x64", "x86" };
            foreach (var sub in commonPaths)
            {
                try
                {
                    var sp = Path.Combine(installPath, sub);
                    if (!Directory.Exists(sp)) continue;
                    foreach (var ext in new[] { "*.addon64", "*.addon32" })
                    {
                        var found = Directory.GetFiles(sp, ext)
                            .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                        if (found != null) return Path.GetFileName(found);
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddon] Subdir scan failed for '{sub}' in '{installPath}' — {ex.Message}"); }
            }

            // Last resort: depth-limited recursive search (catch and ignore access issues).
            // Addon files are always near the game exe, so 4 levels is sufficient.
            try
            {
                foreach (var ext in new[] { "*.addon64", "*.addon32" })
                {
                    var found = ScanAddonShallow(installPath, ext, 4);
                    if (found != null) return found;
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddon] Recursive scan failed for '{installPath}' — {ex.Message}"); }
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddon] Top-level scan failed for '{installPath}' — {ex.Message}"); }
        return null;
    }

    private static string? ScanAddonShallow(string dir, string pattern, int depth)
    {
        if (depth < 0 || !Directory.Exists(dir)) return null;
        try
        {
            var found = Directory.GetFiles(dir, pattern)
                .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
            if (found != null) return Path.GetFileName(found);
            if (depth > 0)
            {
                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch (DirectoryNotFoundException) { return null; }
                catch (UnauthorizedAccessException) { return null; }

                foreach (var sub in subdirs)
                {
                    // Skip subdirectories that no longer exist (symlinks, junctions, race conditions)
                    if (!Directory.Exists(sub)) continue;
                    var r = ScanAddonShallow(sub, pattern, depth - 1);
                    if (r != null) return r;
                }
            }
        }
        catch (DirectoryNotFoundException) { /* Expected for broken symlinks/junctions — suppress noise */ }
        catch (UnauthorizedAccessException) { /* Expected for protected directories — suppress noise */ }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanAddonShallow] Scan failed for '{dir}' — {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Lightweight addon scan: checks the direct folder and common subdirs only.
    /// Skips the expensive depth-limited recursive search. Used on normal Refresh
    /// when the cache indicates no addon was previously found. Full Refresh forces
    /// a deep rescan via ScanForInstalledAddon.
    /// </summary>
    private static string? ScanForInstalledAddonQuick(string installPath, GameMod? mod)
    {
        if (!Directory.Exists(installPath)) return null;
        // Skip WindowsApps paths — always access-denied for file scanning
        if (installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            // Check the AddonPath subfolder from reshade.ini first
            var addonSearchPath = ModInstallService.ResolveAddonSearchPath(installPath);
            if (addonSearchPath != null && Directory.Exists(addonSearchPath))
            {
                if (mod?.AddonFileName != null && File.Exists(Path.Combine(addonSearchPath, mod.AddonFileName)))
                    return mod.AddonFileName;
                foreach (var ext in new[] { "*.addon64", "*.addon32" })
                {
                    var found = Directory.GetFiles(addonSearchPath, ext)
                        .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                    if (found != null) return Path.GetFileName(found);
                }
            }

            if (mod?.AddonFileName != null && File.Exists(Path.Combine(installPath, mod.AddonFileName)))
                return mod.AddonFileName;
            foreach (var ext in new[] { "*.addon64", "*.addon32" })
            {
                var found = Directory.GetFiles(installPath, ext)
                    .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                if (found != null) return Path.GetFileName(found);
            }
            var commonPaths = new[] { "Binaries\\Win64", "Binaries\\Win32", "Binaries\\x86", "x64", "x86" };
            foreach (var sub in commonPaths)
            {
                try
                {
                    var sp = Path.Combine(installPath, sub);
                    if (!Directory.Exists(sp)) continue;
                    foreach (var ext in new[] { "*.addon64", "*.addon32" })
                    {
                        var found = Directory.GetFiles(sp, ext)
                            .FirstOrDefault(f => Path.GetFileName(f).StartsWith("renodx", StringComparison.OrdinalIgnoreCase));
                        if (found != null) return Path.GetFileName(found);
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddonQuick] Subdir scan failed for '{sub}' in '{installPath}' — {ex.Message}"); }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[MainViewModel.ScanForInstalledAddonQuick] Scan failed for '{installPath}' — {ex.Message}"); }
        return null;
    }

    public void SaveLibraryPublic() => SaveLibrary();
    private void SaveLibrary()
    {
        var detectedGames = _allCards
            .Where(c => !c.IsManuallyAdded && c.DetectedGame != null)
            .Select(c => c.DetectedGame!)
            .ToList();

        // Build addon cache safely — multiple DLC cards can share the same install path,
        // so use a plain dict with [] assignment instead of ToDictionary (which throws on dupes).
        var addonCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
            addonCache[c.InstallPath.ToLowerInvariant()] = !string.IsNullOrEmpty(c.InstalledAddonFileName);

        // Keep _addonFileCache in sync with current card state so that installs and
        // uninstalls performed since the last BuildCards are reflected on the next Refresh.
        foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
        {
            var key = c.InstallPath.ToLowerInvariant();
            if (!string.IsNullOrEmpty(c.InstalledAddonFileName))
                _addonFileCache[key] = c.InstalledAddonFileName;
            else if (!_addonFileCache.ContainsKey(key))
                _addonFileCache[key] = "";
        }

        _gameLibraryService.Save(detectedGames, addonCache, _hiddenGames, _favouriteGames, _manualGames,
            _engineTypeCache, _resolvedPathCache, _addonFileCache, _bitnessCache, LastSelectedGameName);
    }

    private static string FormatAge(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours   < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays    < 1) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }
}

