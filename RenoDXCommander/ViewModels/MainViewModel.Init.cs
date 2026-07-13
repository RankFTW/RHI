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
    public async Task FullRefreshAsync(IProgress<string>? progress = null)
    {
        // Clear all caches so every game is re-scanned from disk.
        progress?.Report("Clearing caches...");
        _engineTypeCache.Clear();
        _resolvedPathCache.Clear();
        _addonFileCache.Clear();
        _bitnessCache.Clear();
        _dlssStreamlineService.ClearScanCaches();
        await InitializeAsync(forceRescan: true, progress: progress);
    }

    /// <summary>Forces the next update check to bypass the 4-hour cooldown.</summary>
    public void ForceNextUpdateCheck() => _forceUpdateCheck = true;

    // ── Init ──

    public async Task InitializeAsync(bool forceRescan = false, IProgress<string>? progress = null)
    {
        IsLoading = true;
        if (!_hasInitialized) DisplayedGames.Clear();

        // Capture update statuses BEFORE clearing cards — needed to restore them after rebuild
        var prevUpdateStatus = new Dictionary<string, (GameStatus mod, GameStatus rs, GameStatus dc, GameStatus ul, GameStatus refFw, GameStatus os, GameStatus dxvk)>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCards)
            prevUpdateStatus[c.GameName] = (c.Status, c.RsStatus, c.DcStatus, c.UlStatus, c.RefStatus, c.OsStatus, c.DxvkStatus);

        _allCards.Clear();
        _originalDetectedNames.Clear();

        _crashReporter.Log($"[MainViewModel.InitializeAsync] Started (forceRescan={forceRescan})");

        // Sync global peak nits setting for INI deploys
        AuxInstallService.GlobalPeakNits = _settingsViewModel.PeakNits;
        AuxInstallService.GlobalPeakNitsEnabled = _settingsViewModel.PeakNitsEnabled;
        AuxInstallService.GlobalPeakNitsPresets = _settingsViewModel.PeakNitsPresets;

        // Wire up per-game custom ReShade DLL selection resolver
        AuxInstallService.CustomReShadeSelectionResolver = (gameName) =>
            _gameNameService.CustomReShadeSelection.TryGetValue(gameName, out var sel) ? sel : null;

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
            var uwFixInitTask = Task.Run(async () => {
                try { await _uwFixService.InitAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] UltrawideFixService init failed — {ex.Message}"); }
            });
            var ultraPlusInitTask = Task.Run(async () => {
                try { await _ultraPlusService.InitAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] UltraPlusService init failed — {ex.Message}"); }
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

                // Restore DXVK per-game overrides from saved library
                _dxvkEnabledGames = savedLib.DxvkEnabledGames ?? new(StringComparer.OrdinalIgnoreCase);
                _excludeFromUpdateAllDxvk = savedLib.ExcludeFromUpdateAllDxvk ?? new(StringComparer.OrdinalIgnoreCase);
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

            // ── Instant cache UI: if we have a cached library and this isn't a forced rescan,
            // show cached cards immediately and run the full scan in the background.
            if (hasCachedLibrary)
            {
                // Initialize DLSS preset service in background (not needed until detail panel is shown)
                _ = Task.Run(() => { try { _dlssPresetService.Initialize(); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] DLSS preset init failed (cache path) — {ex.Message}"); } });
                // Restore saved Digital Vibrance levels on startup
                _ = Task.Run(() => { try { DigitalVibranceService.RestoreSavedLevels(Settings.DigitalVibranceSettings); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] DVC restore failed — {ex.Message}"); } });
                await LoadCacheAndBuildCardsAsync(savedLib!);
                _ = RunBackgroundScanAndMergeAsync(savedLib!);
                return;
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

            // One-time migration: move nightly DLLs from old shared folder to new nightly folder
            MigrateNightlyStagingFolder();

            rsTask           = Task.Run(async () => {
                try
                {
                    // Always download both stable and nightly so per-game overrides work
                    var stableTask = _rsUpdateService.EnsureLatestAsync();
                    var nightlyTask = _rsNightlyService.EnsureLatestAsync();
                    await Task.WhenAll(stableTask, nightlyTask);
                }
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
            var dlssManifestTask = Task.Run(async () => {
                try { await _dlssStreamlineService.FetchManifestAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] DLSS manifest fetch failed — {ex.Message}"); }
            });
            // Initialize DLSS preset service (loads NVAPI + caches driver profiles)
            _ = Task.Run(() => { try { _dlssPresetService.Initialize(); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] DLSS preset init failed — {ex.Message}"); } });
            // Restore saved Digital Vibrance levels on startup
            _ = Task.Run(() => { try { DigitalVibranceService.RestoreSavedLevels(Settings.DigitalVibranceSettings); } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] DVC restore failed — {ex.Message}"); } });
            var dxvkTask     = Task.Run(async () => {
                try
                {
                    // Sync the saved DXVK variant to the service before staging
                    _dxvkService.SelectedVariant = _settingsViewModel.DxvkVariant switch
                    {
                        "Stable" => DxvkVariant.Stable,
                        "LiliumHdr" => DxvkVariant.LiliumHdr,
                        _ => DxvkVariant.Development,
                    };

                    // Migrate legacy shared staging folder to variant-specific folders
                    MigrateDxvkStagingFolder();

                    // Only download the globally selected variant at startup.
                    // Other variants are downloaded on-demand when a per-game override needs them.
                    await _dxvkService.EnsureStagingAsync();
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] DXVK staging task failed — {ex.Message}"); }
            });
            var dofFixTask = Task.Run(async () => {
                try { await _dofFixService.EnsureStagingAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] DOF Fix staging task failed — {ex.Message}"); }
            });

            // 3. Await detection first — this never needs network
            progress?.Report("Detecting games...");
            var freshGames = await detectTask;

            // 4. Await network tasks individually so failures don't block game display
            try { await wikiTask; } catch (Exception ex) { wikiFetchFailed = true; _crashReporter.Log($"[MainViewModel.InitializeAsync] Wiki fetch failed (offline?) — {ex.Message}"); }
            try { await lumaTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Luma fetch failed (offline?) — {ex.Message}"); }
            try { _manifest = await manifestTask; AuxInstallService.GlobalManifest = _manifest; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] Manifest fetch failed — {ex.Message}"); }
            try { await osWikiTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] OptiScaler wiki task failed — {ex.Message}"); }
            try { await hdrDbTask; } catch (Exception ex) { _crashReporter.Log($"[MainViewModel.InitializeAsync] HDR database task failed — {ex.Message}"); }
            // rsTask deferred until after cards display
            // osTask deferred until after cards display
            // dlssTask deferred until after cards display

            // Apply manifest-driven shader pack and addon pack overrides
            (_shaderPackService as ShaderPackService)?.ApplyManifestOverrides(_manifest);
            (_addonPackService as AddonPackService)?.ApplyManifestOverrides(_manifest);
            DlssPresetService.ApplyManifestPresets(_manifest);
            _dlssPresetService.ApplyManifestProfileConfig(_manifest);
            _dofFixService.SetSkipGames(_manifest?.DofFixSkipGames);
            _dofFixService.SetForceGames(_manifest?.DofFixForceGames);
            if (_manifest?.ComponentUrls?.TryGetValue("ueDofFix", out var dofFixUrl) == true)
                _dofFixService.ManifestUrlOverride = dofFixUrl;

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
                // A cached game is only excluded if a fresh game has the same name AND
                // the same source (store). This allows the same game on different platforms
                // to coexist while preventing duplicates within a single store.
                var freshKeys = freshGames
                    .Where(g => !string.IsNullOrEmpty(g.InstallPath))
                    .Select(g => (
                        Name: _gameDetectionService.NormalizeName(g.Name),
                        Source: (g.Source ?? "").ToLowerInvariant()))
                    .ToHashSet();
                detectedGames = freshGames
                    .Concat(cachedGames.Where(g =>
                    {
                        if (string.IsNullOrEmpty(g.InstallPath)) return true; // keep orphaned cached entries
                        var key = (
                            Name: _gameDetectionService.NormalizeName(g.Name),
                            Source: (g.Source ?? "").ToLowerInvariant());
                        return !freshKeys.Contains(key);
                    }))
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

            // Apply manifest-driven legacy ReShade version overrides
            // (only if user hasn't already set their own override for that game)
            if (_manifest?.LegacyReShadeVersions != null)
            {
                foreach (var (gameName, version) in _manifest.LegacyReShadeVersions)
                {
                    if (!_reShadeChannelOverrides.ContainsKey(gameName))
                        SetReShadeChannelOverride(gameName, version);
                }
            }

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
            // (prevUpdateStatus was captured at the top of InitializeAsync before _allCards.Clear())

            SubStatusText = "Matching mods and checking install status...";

            // Ensure Nexus Mods dictionary and PCGW AppID cache are ready before building cards
            await nexusInitTask;
            await pcgwCacheTask;
            await uwFixInitTask;
            await ultraPlusInitTask;

            _crashReporter.Log($"[MainViewModel.InitializeAsync] Building cards for {allGames.Count} games...");
            progress?.Report($"Building cards for {allGames.Count} games...");
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
                    if (prev.mod == GameStatus.UpdateAvailable && c.Status == GameStatus.Installed && !c.ExcludeFromUpdateAllRenoDx)
                        c.Status = GameStatus.UpdateAvailable;
                    if (prev.rs == GameStatus.UpdateAvailable && c.RsStatus == GameStatus.Installed && !c.ExcludeFromUpdateAllReShade)
                        c.RsStatus = GameStatus.UpdateAvailable;
                    if (prev.dc == GameStatus.UpdateAvailable && c.DcStatus == GameStatus.Installed && !c.ExcludeFromUpdateAllDc)
                        c.DcStatus = GameStatus.UpdateAvailable;
                    if (prev.ul == GameStatus.UpdateAvailable && c.UlStatus == GameStatus.Installed && !c.ExcludeFromUpdateAllUl)
                        c.UlStatus = GameStatus.UpdateAvailable;
                    if (prev.refFw == GameStatus.UpdateAvailable && c.RefStatus == GameStatus.Installed && !c.ExcludeFromUpdateAllRef)
                        c.RefStatus = GameStatus.UpdateAvailable;
                    if (prev.os == GameStatus.UpdateAvailable && c.OsStatus == GameStatus.Installed && !c.ExcludeFromUpdateAllOs)
                        c.OsStatus = GameStatus.UpdateAvailable;
                    if (prev.dxvk == GameStatus.UpdateAvailable && c.DxvkStatus == GameStatus.Installed && !c.ExcludeFromUpdateAllDxvk)
                        c.DxvkStatus = GameStatus.UpdateAvailable;
                }
            }

            // Notify the Update All button after restoring update statuses
            NotifyUpdateButtonChanged();

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

            // ── One-time migration: global Nightly → per-game overrides ──────────────
            if (_reShadeChannelOverrides.Remove("__nightly_migration_pending"))
            {
                int migrated = 0;
                foreach (var card in _allCards)
                {
                    if (!_reShadeChannelOverrides.ContainsKey(card.GameName))
                    {
                        _reShadeChannelOverrides[card.GameName] = "Nightly";
                        migrated++;
                    }
                }
                _reShadeChannelOverrides["__nightly_migration_done"] = "true";
                SaveNameMappings();
                _crashReporter.Log($"[MainViewModel.InitializeAsync] Nightly migration complete — {migrated} games set to Nightly");
            }

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
                    // Wait for ReShade staging, OptiScaler staging, DLSS staging, and DXVK staging to finish in parallel
                    await Task.WhenAll(rsTask, normalRsTask, osTask, dlssTask, dxvkTask);
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

}
