// MainViewModel.BackgroundScan.cs -- Background scanning, card merging, and staging migrations.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    // ── Phase 2: Background scan and merge ──────────────────────────────────────

    /// <summary>
    /// Phase 2 background path: runs the full detection + network pipeline
    /// (identical to InitializeAsync) and merges fresh results into the
    /// already-displayed cached cards. Runs as fire-and-forget after Phase 1.
    /// </summary>
    private async Task RunBackgroundScanAndMergeAsync(SavedGameLibrary savedLib, bool isStartup = false)
    {
        IsBackgroundScanning = true;
        BackgroundScanStatusText = "Scanning for changes...";
        _crashReporter.Log("[MainViewModel.RunBackgroundScanAndMergeAsync] Starting background scan...");

        try
        {
            bool wikiFetchFailed = false;
            Task rsTask = Task.CompletedTask;
            Task normalRsTask = Task.CompletedTask;
            Task osTask = Task.CompletedTask;
            Task dlssTask = Task.CompletedTask;

            // Start Nexus Mods + PCGW + Lyall initialization early (network I/O)
            var nexusInitTask = Task.Run(async () => {
                try { await _nexusModsService.InitAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] NexusModsService init failed — {ex.Message}"); }
            });
            var pcgwCacheTask = Task.Run(async () => {
                try { await _pcgwService.LoadCacheAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] PcgwService cache load failed — {ex.Message}"); }
            });
            var uwFixInitTask = Task.Run(async () => {
                try { await _uwFixService.InitAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] UltrawideFixService init failed — {ex.Message}"); }
            });
            var ultraPlusInitTask = Task.Run(async () => {
                try { await _ultraPlusService.InitAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] UltraPlusService init failed — {ex.Message}"); }
            });

            // Launch all background tasks (identical to InitializeAsync)
            var wikiTask        = _wikiService.FetchAllAsync();
            var lumaTask        = _lumaService.FetchCompletedModsAsync();
            var lumaUeTask      = _lumaService.FetchGenericUeTableAsync();
            var manifestTask    = _manifestService.FetchAsync();
            var detectTask   = DetectAllGamesDedupedAsync();
            var osWikiTask   = Task.Run(async () => {
                try { await _optiScalerWikiService.FetchAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] OptiScaler wiki fetch failed — {ex.Message}"); }
            });
            var hdrDbTask    = Task.Run(async () => {
                try { await _hdrDatabaseService.FetchAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] HDR database fetch failed — {ex.Message}"); }
            });
            rsTask           = Task.Run(async () => {
                try
                {
                    // Always download both stable and nightly so per-game overrides work
                    var stableTask = _rsUpdateService.EnsureLatestAsync();
                    var nightlyTask = _rsNightlyService.EnsureLatestAsync();
                    await Task.WhenAll(stableTask, nightlyTask);
                }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] ReShade update task failed — {ex.Message}"); }
            });
            normalRsTask     = Task.Run(async () => {
                try { await _normalRsUpdateService.EnsureLatestAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Normal ReShade update task failed — {ex.Message}"); }
            });
            var shaderPackTask = Task.Run(async () => {
                try { await _shaderPackService.EnsureLatestAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Shader pack task failed — {ex.Message}"); }
            });
            var addonPackTask = Task.Run(async () => {
                try {
                    await _addonPackService.EnsureLatestAsync();
                    // Note: CheckAndUpdateAllAsync is called after ApplyManifestOverrides below,
                    // so manifest-driven entries (releaseApiUrl etc.) are available for version resolution.
                }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Addon pack task failed — {ex.Message}"); }
            });
            osTask           = Task.Run(async () => {
                try { await _optiScalerService.EnsureStagingAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] OptiScaler staging task failed — {ex.Message}"); }
            });
            var osNightlyTask2 = Task.Run(async () => {
                try
                {
                    if (_allCards.Any(c => GetOsVariant(c.GameName, c.Source ?? "") == "Nightly"))
                        await _optiScalerService.EnsureNightlyStagingAsync();
                }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] OptiScaler nightly staging task failed — {ex.Message}"); }
            });
            dlssTask         = Task.Run(async () => {
                try { await _optiScalerService.EnsureDlssStagingAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] DLSS staging task failed — {ex.Message}"); }
            });
            var dlssManifestTask2 = Task.Run(async () => {
                try { await _dlssStreamlineService.FetchManifestAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] DLSS manifest fetch failed — {ex.Message}"); }
            });
            var dxvkTask     = Task.Run(async () => {
                try
                {
                    // Only download the globally selected variant.
                    // Other variants are downloaded on-demand when a per-game override needs them.
                    await _dxvkService.EnsureStagingAsync();
                }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] DXVK staging task failed — {ex.Message}"); }
            });
            var dofFixTask2 = Task.Run(async () => {
                try { await _dofFixService.EnsureStagingAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] DOF Fix staging task failed — {ex.Message}"); }
            });
            var ualTask = Task.Run(async () => {
                try
                {
                    await _ualService.CheckForUpdateAsync();
                    if (_ualService.HasUpdate)
                    {
                        await _ualService.EnsureStagingAsync(is32Bit: false);
                        await _ualService.EnsureStagingAsync(is32Bit: true);
                        await _ualService.AutoUpdateInstalledGamesAsync(_allCards);
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] UAL task failed — {ex.Message}"); }
            });

            // Await detection first — this never needs network
            var freshGames = await detectTask;

            // Await network tasks individually so failures don't block
            try { await wikiTask; } catch (Exception ex) { wikiFetchFailed = true; _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Wiki fetch failed (offline?) — {ex.Message}"); }
            try { await lumaTask; } catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Luma fetch failed (offline?) — {ex.Message}"); }
            try { await lumaUeTask; } catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Luma UE table fetch failed (offline?) — {ex.Message}"); }
            try { _manifest = await manifestTask; AuxInstallService.GlobalManifest = _manifest; } catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Manifest fetch failed — {ex.Message}"); }
            try { await osWikiTask; } catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] OptiScaler wiki task failed — {ex.Message}"); }
            try { await hdrDbTask; } catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] HDR database task failed — {ex.Message}"); }
            try { await addonPackTask; } catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Addon pack await failed — {ex.Message}"); }

            // Apply manifest-driven shader pack and addon pack overrides
            (_shaderPackService as ShaderPackService)?.ApplyManifestOverrides(_manifest);
            (_addonPackService as AddonPackService)?.ApplyManifestOverrides(_manifest);
            DlssPresetService.ApplyManifestPresets(_manifest);

            // Run addon update check AFTER manifest overrides are applied so releaseApiUrl entries are available
            try { await _addonPackService.CheckAndUpdateAllAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Addon update check failed — {ex.Message}"); }
            _dlssPresetService.ApplyManifestProfileConfig(_manifest);
            FeatureFlags.ApplyManifest(_manifest?.FeatureFlags);
            _dofFixService.SetSkipGames(_manifest?.DofFixSkipGames);
            _dofFixService.SetForceGames(_manifest?.DofFixForceGames);
            if (_manifest?.ComponentUrls?.TryGetValue("ueDofFix", out var dofFixUrl2) == true)
                _dofFixService.ManifestUrlOverride = dofFixUrl2;
            if (_manifest?.ComponentUrls?.TryGetValue("dc", out var dcApiUrl2) == true && !string.IsNullOrEmpty(dcApiUrl2))
                DcReleasesApiUrlOverride = dcApiUrl2;

            // Extract wiki/luma results
            var wikiResult = !wikiFetchFailed ? await wikiTask : default;
            _allMods      = wikiResult.Mods ?? new();
            _genericNotes = wikiResult.GenericNotes ?? new();
            try { _lumaMods = lumaTask.IsCompletedSuccessfully ? await lumaTask : new(); }
            catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Luma mods deserialization failed — {ex.Message}"); _lumaMods = new(); }
            try { _lumaGenericEntries = lumaUeTask.IsCompletedSuccessfully ? await lumaUeTask : new(StringComparer.OrdinalIgnoreCase); }
            catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Luma UE entries failed — {ex.Message}"); _lumaGenericEntries = new(StringComparer.OrdinalIgnoreCase); }

            // ── Detect new wiki mods ────────────────────────────────────────────
            if (!wikiFetchFailed)
            {
                var currentModNames = _allMods
                    .Where(m => m.SnapshotUrl != null) // Only mods with downloadable addons
                    .Select(m => m.Name)
                    .ToList();

                _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Wiki mods check: {currentModNames.Count} downloadable mods");

                // Seed on first launch so everything isn't shown as "new"
                _seenWikiModsService.SeedIfEmpty(currentModNames);

                var newMods = _seenWikiModsService.GetNewMods(currentModNames);
                _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] New wiki mods: {newMods.Count} (seen: {_seenWikiModsService.GetSeenMods().Count})");
                if (newMods.Count > 0)
                {
                    _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] New mods: {string.Join(", ", newMods.Take(10))}{(newMods.Count > 10 ? "..." : "")}");
                    DispatcherQueue?.TryEnqueue(() => NewWikiMods = newMods);
                }
            }
            else
            {
                _crashReporter.Log("[RunBackgroundScanAndMergeAsync] Wiki fetch failed, skipping new mods check");
            }

            // ── Detect new Ultra+ mods ────────────────────────────────────────────
            {
                var currentUltraPlusMods = _ultraPlusService.GetAllGameNames().ToList();
                _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Ultra+ mods check: {currentUltraPlusMods.Count} mods");

                _seenUltraPlusModsService.SeedIfEmpty(currentUltraPlusMods);

                var newUltraPlusMods = _seenUltraPlusModsService.GetNewMods(currentUltraPlusMods);
                _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] New Ultra+ mods: {newUltraPlusMods.Count} (seen: {_seenUltraPlusModsService.GetSeenMods().Count})");
                if (newUltraPlusMods.Count > 0)
                {
                    _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] New Ultra+ mods: {string.Join(", ", newUltraPlusMods.Take(10))}{(newUltraPlusMods.Count > 10 ? "..." : "")}");
                    DispatcherQueue?.TryEnqueue(() => NewUltraPlusMods = newUltraPlusMods);
                }
            }

            // ── Detect new Luma completed mods ───────────────────────────────────
            if (_lumaMods.Count > 0)
            {
                var currentLumaMods = _lumaMods.Select(m => m.Name).ToList();
                _seenLumaModsService.SeedIfEmpty(currentLumaMods);
                var newLumaMods = _seenLumaModsService.GetNewMods(currentLumaMods);
                _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] New Luma mods: {newLumaMods.Count} (seen: {_seenLumaModsService.GetSeenMods().Count})");
                if (newLumaMods.Count > 0)
                {
                    _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] New Luma mods: {string.Join(", ", newLumaMods.Take(10))}{(newLumaMods.Count > 10 ? "..." : "")}");
                    DispatcherQueue?.TryEnqueue(() => NewLumaMods = newLumaMods);
                }
            }

            // Merge fresh games with cached games (same logic as InitializeAsync)
            ApplyGameRenames(freshGames);
            var cachedGames = _gameLibraryService.ToDetectedGames(savedLib);
            var freshKeys = freshGames
                .Where(g => !string.IsNullOrEmpty(g.InstallPath))
                .Select(g => (
                    Name: _gameDetectionService.NormalizeName(g.Name),
                    Source: (g.Source ?? "").ToLowerInvariant()))
                .ToHashSet();
            var detectedGames = freshGames
                .Concat(cachedGames.Where(g =>
                {
                    if (string.IsNullOrEmpty(g.InstallPath)) return true;
                    var key = (
                        Name: _gameDetectionService.NormalizeName(g.Name),
                        Source: (g.Source ?? "").ToLowerInvariant());
                    return !freshKeys.Contains(key);
                }))
                .ToList();
            _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Merged library: {freshGames.Count} detected + {cachedGames.Count} cached → {detectedGames.Count} total");

            // Apply persisted renames and folder overrides
            ApplyGameRenames(detectedGames);
            ApplyFolderOverrides(detectedGames);

            // Combine auto-detected + manual games
            var manualNames = _manualGames.Select(g => _gameDetectionService.NormalizeName(g.Name))
                                          .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allGames = detectedGames
                .Where(g => !manualNames.Contains(_gameDetectionService.NormalizeName(g.Name)))
                .Concat(_manualGames)
                .ToList();

            // Apply remote manifest data
            ApplyManifest(_manifest);
            _pcgwService.CheckManifestCacheVersion(_manifest);

            // Apply manifest-driven legacy ReShade version overrides
            if (_manifest?.LegacyReShadeVersions != null)
            {
                foreach (var (gameName, version) in _manifest.LegacyReShadeVersions)
                {
                    // Check for any existing override — either legacy name-only or composite key
                    bool hasOverride = _reShadeChannelOverrides.ContainsKey(gameName)
                        || _reShadeChannelOverrides.Keys.Any(k => k.StartsWith($"{gameName}|", StringComparison.OrdinalIgnoreCase));
                    if (!hasOverride)
                        SetReShadeChannelOverride(gameName, version);
                }
            }

            if (_manifest != null)
                GameCardViewModel.MergeManifestAuthorData(_manifest.DonationUrls, _manifest.AuthorDisplayNames);
            ApplyManifestStatusOverrides();

            // Remove manifest-blacklisted entries
            if (_manifestBlacklist.Count > 0 || _manifestBlacklistPrefixes.Count > 0)
                allGames = allGames.Where(g => !_manifestBlacklist.Contains(g.Name)
                    && !_manifestBlacklistPrefixes.Any(p => g.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();

            _crashReporter.Log("[RunBackgroundScanAndMergeAsync] Loading install records...");
            var records    = _installer.LoadAll();
            var auxRecords = _auxInstaller.LoadAll();
            _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Records loaded: {records.Count} mod, {auxRecords.Count} aux");
            var addonCache = savedLib.AddonScanCache ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            // Ensure Nexus Mods dictionary and PCGW AppID cache are ready before building cards
            _crashReporter.Log("[RunBackgroundScanAndMergeAsync] Awaiting background init tasks...");
            await nexusInitTask;
            await pcgwCacheTask;
            await uwFixInitTask;
            await ultraPlusInitTask;
            _crashReporter.Log("[RunBackgroundScanAndMergeAsync] Background init tasks complete");

            // On standard refresh, re-check games in the DLSS skip list.
            // If a previously-skipped game now has DLSS (e.g. preloaded game released),
            // it's removed from the skip cache so BuildCards will scan it normally.
            // Skip on startup — only run when the user explicitly triggers a refresh.
            if (!isStartup)
                _dlssStreamlineService.RecheckSkipList(allGames);

            // Build fresh cards
            _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Building cards for {allGames.Count} games...");
            var freshCards = await Task.Run(() => BuildCards(allGames, records, auxRecords, addonCache, _genericNotes));
            _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] BuildCards complete: {freshCards.Count} cards");
            GraphicsApiDetector.SaveCache();
            SaveGameApiCache();

            // Apply card overrides and manifest card overrides
            ApplyCardOverrides(freshCards);
            ApplyManifestCardOverrides(_manifest, freshCards);

            // Apply manifest DLL name overrides
            ApplyManifestDllRenames();

            // Reconcile default naming
            ReconcileDefaultNaming();

            // Merge fresh cards into displayed cards
            MergeCards(freshCards);

            // Save updated library
            _ = Task.Run(() => { try { SaveLibrary(); } catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Fire-and-forget SaveLibrary failed — {ex.Message}"); } });

            // Check for updates (async, parallel, non-blocking)
            _crashReporter.Log("[RunBackgroundScanAndMergeAsync] Starting background update checks...");
            _ = Task.Run(async () =>
            {
                try { await CheckForUpdatesAsync(_allCards, records, auxRecords); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Background update check failed — {ex}"); }

                // DLSS/Streamline auto-update (runs after manifest is fetched and cards have detection)
                try { await RunDlssAutoUpdateAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] DLSS auto-update failed — {ex.Message}"); }

                // Silent component auto-update (runs after update check flags pending updates)
                try { _autoUpdateService.TriggerAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Component auto-update trigger failed — {ex.Message}"); }

                // Start periodic update check timer (fires every 4h while app is running)
                StartPeriodicUpdateCheckTimer();
            });

            // Update status text with final counts
            var offlineMode = wikiFetchFailed;
            DispatcherQueue?.TryEnqueue(() =>
            {
                StatusText = offlineMode
                    ? $"{detectedGames.Count} games detected · offline mode (mod info unavailable)"
                    : $"{detectedGames.Count} games detected · {InstalledCount} mods installed";
                SubStatusText = "";

                // Re-scroll to selected game after merge (cards may have shifted)
                ScrollToSelectedGame?.Invoke();

                // Force a detail panel rebuild on the currently selected card so the UI
                // reflects the fresh manifest data (presets, notes, NR column, etc.)
                // that wasn't available during Phase 1 cache load.
                // Delay slightly beyond the 150ms SelectionChanged debounce timer so our
                // rebuild runs last and doesn't get overwritten by TryRestoreSelection.
                if (!string.IsNullOrEmpty(LastSelectedGameName))
                {
                    var parsed = GameKey.Parse(LastSelectedGameName);
                    var selectedCard = _allCards.FirstOrDefault(c =>
                        c.GameName.Equals(parsed.Name, StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrEmpty(parsed.Store) || c.Source == parsed.Store));
                    if (selectedCard != null)
                    {
                        var cardToRebuild = selectedCard;
                        _ = Task.Delay(300).ContinueWith(_ =>
                            DispatcherQueue?.TryEnqueue(() =>
                            {
                                _crashReporter.Log($"[BackgroundScan] Rebuilding panel for selected card '{cardToRebuild.GameName}'");
                                cardToRebuild.NotifyAll();
                                RequestCardRebuild?.Invoke(cardToRebuild);
                            }));
                    }
                    else
                    {
                        _crashReporter.Log($"[BackgroundScan] Could not find selected card for '{LastSelectedGameName}' — skipping panel rebuild");
                    }
                }
            });

            // ── Deferred background work: ReShade staging + OptiScaler staging + shader sync ──
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.WhenAll(rsTask, normalRsTask, osTask, dlssTask, dxvkTask);
                }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Deferred ReShade sync failed — {ex.Message}"); }

                // Redeploy Streamline to all games where it's enabled (after OptiScaler staging is ready)
                try
                {
                    foreach (var card in _allCards.Where(c => c.IsOsInstalled && !string.IsNullOrEmpty(c.InstallPath)
                        && GetOsDeployStreamline(c.GameName, c.Source ?? "")))
                    {
                        try { _optiScalerService.DeployStreamlineToGame(card.InstallPath!, GetOsStreamlineVersion(card.GameName, card.Source ?? "")); }
                        catch (Exception ex) { _crashReporter.Log($"[StreamlineRedeploy] Failed for '{card.GameName}' — {ex.Message}"); }
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Streamline redeploy loop failed — {ex.Message}"); }

                if (_shaderPackReadyTask != null)
                {
                    try { await _shaderPackReadyTask; }
                    catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] ShaderPackReady failed — {ex.Message}"); }
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

                    var allNeededPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var card in rsCards)
                    {
                        var sel = ResolveShaderSelection(card.GameName, card.ShaderModeOverride, card.Source ?? "");
                        if (sel != null) allNeededPacks.UnionWith(sel);
                    }
                    if (allNeededPacks.Count > 0)
                        await _shaderPackService.EnsurePacksAsync(allNeededPacks);

                    var syncTasks = rsCards
                        .Select(card =>
                        {
                            var effectiveSelection = ResolveShaderSelection(card.GameName, card.ShaderModeOverride, card.Source ?? "");
                            var exclusions = effectiveSelection?
                                .ToDictionary(id => id, id => _shaderPackService.GetExcludedFiles(id),
                                    StringComparer.OrdinalIgnoreCase);
                            return Task.Run(() => _shaderPackService.SyncGameFolder(card.InstallPath, effectiveSelection, exclusions));
                        });
                    await Task.WhenAll(syncTasks);
                }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] SyncShaders failed — {ex.Message}"); }

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
                            if (card.UseNormalReShade)
                            {
                                return Task.Run(() => _addonPackService.DeployAddonsForGame(
                                    card.GameName, card.InstallPath, card.Is32Bit,
                                    useGlobalSet: true, perGameSelection: new List<string>()));
                            }

                            string addonMode = GetPerGameAddonMode(card.GameName, card.Source ?? "");
                            bool useGlobalSet = addonMode != "Select";
                            List<string>? selection = useGlobalSet
                                ? _settingsViewModel.EnabledGlobalAddons
                                : (_gameNameService.PerGameAddonSelection.TryGetValue(GameKey.FromCard(card.GameName, card.Source).ToKey(), out var sel) ? sel : null);
                            return Task.Run(() => _addonPackService.DeployAddonsForGame(
                                card.GameName, card.InstallPath, card.Is32Bit, useGlobalSet, selection));
                        });
                    await Task.WhenAll(addonTasks);
                }
                catch (Exception ex) { _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] SyncAddons failed — {ex.Message}"); }

                finally
                {
                    DispatcherQueue?.TryEnqueue(() => { SubStatusText = ""; });
                }
            });
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RunBackgroundScanAndMergeAsync] Background scan failed — {ex.Message}");
            _crashReporter.WriteCrashReport("RunBackgroundScanAndMergeAsync", ex);
            // Leave cached cards in place — user sees stale data but app remains functional
        }
        finally
        {
            IsBackgroundScanning = false;
            BackgroundScanStatusText = "";
        }
    }

    /// <summary>
    /// Reconciles fresh cards from the background scan with the currently displayed
    /// cached cards. Updates existing cards in-place (so WinUI bindings fire),
    /// adds new games, and removes stale games.
    /// </summary>
    private void MergeCards(List<GameCardViewModel> freshCards)
    {
        _crashReporter.Log($"[MergeCards] Merging {freshCards.Count} fresh cards into {_allCards.Count} existing cards...");

        // Zero-detection guard: if background scan returned 0 games but we have cached cards,
        // this likely indicates a transient failure — skip merge to preserve cached state.
        if (freshCards.Count == 0 && _allCards.Count > 0)
        {
            _crashReporter.Log("[MergeCards] Background scan returned 0 games — skipping merge to preserve cached state.");
            return;
        }

        // Build lookup of existing cards by composite key (GameName|Source) for multi-store support
        var existingByKey = new Dictionary<string, GameCardViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in _allCards)
        {
            var key = GameKey.FromCard(card.GameName, card.Source).ToKey();
            existingByKey.TryAdd(key, card);
        }

        // Build set of fresh composite keys for stale detection
        var freshKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fc in freshCards)
            freshKeys.Add(GameKey.FromCard(fc.GameName, fc.Source).ToKey());

        var cardsToAdd = new List<GameCardViewModel>();

        // For each fresh card: update existing or mark as new
        foreach (var fresh in freshCards)
        {
            var freshKey = GameKey.FromCard(fresh.GameName, fresh.Source).ToKey();
            if (existingByKey.TryGetValue(freshKey, out var existing))
            {
                // Update mutable properties in-place so WinUI bindings fire
                // Preserve UpdateAvailable status if the fresh scan shows Installed
                // (the update check already determined an update exists — don't lose it)
                existing.Status             = (existing.Status == GameStatus.UpdateAvailable && fresh.Status == GameStatus.Installed) ? GameStatus.UpdateAvailable : fresh.Status;
                existing.RsStatus           = (existing.RsStatus == GameStatus.UpdateAvailable && fresh.RsStatus == GameStatus.Installed) ? GameStatus.UpdateAvailable : fresh.RsStatus;
                existing.UlStatus           = (existing.UlStatus == GameStatus.UpdateAvailable && fresh.UlStatus == GameStatus.Installed) ? GameStatus.UpdateAvailable : fresh.UlStatus;
                existing.DcStatus           = (existing.DcStatus == GameStatus.UpdateAvailable && fresh.DcStatus == GameStatus.Installed) ? GameStatus.UpdateAvailable : fresh.DcStatus;
                existing.OsStatus           = (existing.OsStatus == GameStatus.UpdateAvailable && fresh.OsStatus == GameStatus.Installed) ? GameStatus.UpdateAvailable : fresh.OsStatus;
                existing.RefStatus          = (existing.RefStatus == GameStatus.UpdateAvailable && fresh.RefStatus == GameStatus.Installed) ? GameStatus.UpdateAvailable : fresh.RefStatus;
                existing.LumaStatus         = (existing.LumaStatus == GameStatus.UpdateAvailable && fresh.LumaStatus == GameStatus.Installed) ? GameStatus.UpdateAvailable : fresh.LumaStatus;
                existing.Mod                = fresh.Mod;
                existing.InstalledRecord    = fresh.InstalledRecord;
                existing.RsRecord           = fresh.RsRecord;
                existing.NexusModsUrl       = fresh.NexusModsUrl;
                existing.PcgwUrl            = fresh.PcgwUrl;
                existing.UwFixUrl        = fresh.UwFixUrl;
                existing.UwFixSource     = fresh.UwFixSource;
                existing.UltraPlusUrl    = fresh.UltraPlusUrl;
                existing.EngineHint         = fresh.EngineHint;
                existing.GraphicsApi        = fresh.GraphicsApi;
                existing.Is32Bit            = fresh.Is32Bit;
                existing.WikiStatus         = fresh.WikiStatus;
                existing.Maintainer         = fresh.Maintainer;
                existing.InstallPath        = fresh.InstallPath;
                existing.Source             = fresh.Source;
                existing.IsGenericMod       = fresh.IsGenericMod;
                existing.IsExternalOnly     = fresh.IsExternalOnly;
                existing.ExternalUrl        = fresh.ExternalUrl;
                existing.ExternalLabel      = fresh.ExternalLabel;
                existing.NexusUrl           = fresh.NexusUrl;
                existing.DiscordUrl         = fresh.DiscordUrl;
                existing.NameUrl            = fresh.NameUrl;
                existing.Notes              = fresh.Notes;
                existing.NotesUrl           = fresh.NotesUrl;
                existing.NotesUrlLabel      = fresh.NotesUrlLabel;
                existing.UseUeExtended      = fresh.UseUeExtended;
                existing.IsRtxHdrEnabled    = fresh.IsRtxHdrEnabled;
                existing.InstalledAddonFileName = fresh.InstalledAddonFileName;
                existing.RdxInstalledVersion    = fresh.RdxInstalledVersion;
                existing.RsInstalledFile        = fresh.RsInstalledFile;
                existing.RsInstalledVersion     = fresh.RsInstalledVersion;
                existing.DetectedGame           = fresh.DetectedGame;
                existing.DetectedApis           = fresh.DetectedApis;
                existing.IsDualApiGame          = fresh.IsDualApiGame;
                existing.LumaMod                = fresh.LumaMod;
                existing.IsLumaMode             = false;
                existing.LumaRecord             = fresh.LumaRecord;
                existing.LumaNotes              = fresh.LumaNotes;
                existing.LumaNotesUrl           = fresh.LumaNotesUrl;
                existing.LumaNotesUrlLabel      = fresh.LumaNotesUrlLabel;
                existing.LumaHdrSupported       = fresh.LumaHdrSupported;
                existing.LumaDlssFsrSupported   = fresh.LumaDlssFsrSupported;
                existing.IsNativeHdrGame        = fresh.IsNativeHdrGame;
                existing.IsManifestUeExtended   = fresh.IsManifestUeExtended;
                existing.LumaRenodxCompatible   = fresh.LumaMod != null;
                existing.EngineIniProjectOverride = fresh.EngineIniProjectOverride;
                existing.DllOverrideEnabled      = fresh.DllOverrideEnabled;
                existing.ExcludeFromUpdateAllReShade = fresh.ExcludeFromUpdateAllReShade;
                existing.ExcludeFromUpdateAllRenoDx  = fresh.ExcludeFromUpdateAllRenoDx;
                existing.ExcludeFromUpdateAllUl      = fresh.ExcludeFromUpdateAllUl;
                existing.ExcludeFromUpdateAllDc      = fresh.ExcludeFromUpdateAllDc;
                existing.UseNormalReShade        = fresh.UseNormalReShade;
                existing.ShaderModeOverride      = fresh.ShaderModeOverride;
                existing.UlInstalledFile         = fresh.UlInstalledFile;
                existing.UlInstalledVersion      = fresh.UlInstalledVersion;
                existing.DcInstalledFile         = fresh.DcInstalledFile;
                existing.DcInstalledVersion      = fresh.DcInstalledVersion;
                existing.OsInstalledFile         = fresh.OsInstalledFile;
                existing.OsInstalledVersion      = fresh.OsInstalledVersion;
                existing.RefRecord               = fresh.RefRecord;
                existing.RefInstalledVersion     = fresh.RefInstalledVersion;

                // ── DXVK fields ──────────────────────────────────────────
                existing.DxvkStatus              = fresh.DxvkStatus;
                existing.DxvkInstalledVersion    = fresh.DxvkInstalledVersion;
                existing.DxvkRecord              = fresh.DxvkRecord;
                existing.DxvkEnabled             = fresh.DxvkEnabled;
                existing.ExcludeFromUpdateAllDxvk = fresh.ExcludeFromUpdateAllDxvk;

                // ── DLSS / Streamline fields ─────────────────────────────
                if (fresh.DlssDetection != null)
                    existing.ApplyDlssDetection(fresh.DlssDetection);
            }
            else
            {
                // New game detected — add to list
                cardsToAdd.Add(fresh);
            }
        }

        // Remove stale games (not in fresh set AND not manually added)
        var cardsToRemove = _allCards
            .Where(c => !freshKeys.Contains(GameKey.FromCard(c.GameName, c.Source).ToKey()) && !c.IsManuallyAdded)
            .ToList();

        foreach (var stale in cardsToRemove)
            _allCards.Remove(stale);

        // Add new games
        _allCards.AddRange(cardsToAdd);

        _crashReporter.Log($"[MergeCards] Updated {freshCards.Count - cardsToAdd.Count} existing, added {cardsToAdd.Count} new, removed {cardsToRemove.Count} stale");

        // Preserve SelectedGame: if still in list keep it, if removed select first card
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (SelectedGame != null && !_allCards.Contains(SelectedGame))
                SelectedGame = _allCards.Count > 0 ? _allCards[0] : null;

            // Re-sort by game name
            _allCards = _allCards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();

            // Push to FilterViewModel, apply filter
            _filterViewModel.SetAllCards(_allCards);
            _filterViewModel.UpdateCounts();
            _filterViewModel.ApplyFilter();

            // Refresh the selected game's detail panel so merged data (LumaMod, wiki, etc.) is visible
            SelectedGame?.NotifyAll();
        });
    }

    private static string FormatAge(DateTime utc)
    {
        var age = DateTime.UtcNow - utc;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours   < 1) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalDays    < 1) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalDays}d ago";
    }

    /// <summary>
    /// One-time migration: if the user was on Nightly channel and DLLs exist in the old
    /// shared staging folder (%LocalAppData%\RHI\reshade\), move them to the new dedicated
    /// nightly folder (%LocalAppData%\RHI\reshade-nightly\). This runs silently with no user input.
    /// </summary>
    private void MigrateNightlyStagingFolder()
    {
        try
        {
            // Only migrate if user was on Nightly and the new nightly folder doesn't already have valid DLLs
            if (!IsReShadeNightly) return;

            var nightlyDir = AuxInstallService.RsNightlyStagingDir;
            var nightlyPath64 = AuxInstallService.RsNightlyStagedPath64;
            var nightlyPath32 = AuxInstallService.RsNightlyStagedPath32;

            // If nightly folder already has valid DLLs, no migration needed
            if (File.Exists(nightlyPath64) && new FileInfo(nightlyPath64).Length > AuxInstallService.MinReShadeSize
                && File.Exists(nightlyPath32) && new FileInfo(nightlyPath32).Length > AuxInstallService.MinReShadeSize)
                return;

            var oldPath64 = AuxInstallService.RsStagedPath64;
            var oldPath32 = AuxInstallService.RsStagedPath32;
            var oldVersionFile = Path.Combine(AuxInstallService.RsStagingDir, "reshade_version.txt");

            // Check if old shared folder has nightly DLLs (version file starts with "nightly-")
            if (!File.Exists(oldVersionFile)) return;
            var oldVersion = File.ReadAllText(oldVersionFile).Trim();
            if (!oldVersion.StartsWith("nightly-", StringComparison.OrdinalIgnoreCase)) return;

            // Old folder has nightly DLLs — move them to the new nightly folder
            Directory.CreateDirectory(nightlyDir);

            if (File.Exists(oldPath64) && new FileInfo(oldPath64).Length > AuxInstallService.MinReShadeSize)
            {
                File.Copy(oldPath64, nightlyPath64, overwrite: true);
                File.Delete(oldPath64);
            }
            if (File.Exists(oldPath32) && new FileInfo(oldPath32).Length > AuxInstallService.MinReShadeSize)
            {
                File.Copy(oldPath32, nightlyPath32, overwrite: true);
                File.Delete(oldPath32);
            }

            // Move the version file too
            var newVersionFile = Path.Combine(nightlyDir, "reshade_version.txt");
            if (File.Exists(oldVersionFile))
            {
                File.Copy(oldVersionFile, newVersionFile, overwrite: true);
                File.Delete(oldVersionFile);
            }

            _crashReporter.Log($"[MigrateNightlyStagingFolder] Migrated nightly DLLs from shared folder to {nightlyDir}");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MigrateNightlyStagingFolder] Migration failed (non-fatal) — {ex.Message}");
        }
    }

    /// <summary>
    /// One-time migration: if the legacy shared DXVK staging folder (%LocalAppData%\RHI\dxvk\)
    /// has DLLs, move them to the correct variant-specific folder based on the global setting.
    /// </summary>
    private void MigrateDxvkStagingFolder()
    {
        try
        {
            var legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "dxvk");
            var legacyVersionFile = Path.Combine(legacyDir, "version.txt");
            var legacyX64 = Path.Combine(legacyDir, "x64");

            // Only migrate if legacy folder has content
            if (!Directory.Exists(legacyX64) || !File.Exists(legacyVersionFile)) return;

            // Determine which variant the legacy folder belongs to based on global setting
            var targetVariant = _dxvkService.SelectedVariant;
            var targetDir = DxvkService.GetStagingDirForVariant(targetVariant);
            var targetVersionFile = DxvkService.GetVersionFileForVariant(targetVariant);

            // If target already has valid staging, skip
            if (DxvkService.IsStagingReadyForVariant(targetVariant)) return;

            // Move contents to the target variant folder
            Directory.CreateDirectory(targetDir);

            // Copy x64 and x32 folders
            foreach (var subDir in new[] { "x64", "x32" })
            {
                var srcDir = Path.Combine(legacyDir, subDir);
                var destDir = Path.Combine(targetDir, subDir);
                if (Directory.Exists(srcDir))
                {
                    Directory.CreateDirectory(destDir);
                    foreach (var file in Directory.GetFiles(srcDir))
                        File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
                }
            }

            // Copy version file
            if (File.Exists(legacyVersionFile))
                File.Copy(legacyVersionFile, targetVersionFile, overwrite: true);

            // Clean up legacy folder
            try { Directory.Delete(legacyDir, recursive: true); } catch { }

            _crashReporter.Log($"[MigrateDxvkStagingFolder] Migrated legacy DXVK staging to {targetDir} (variant={targetVariant})");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MigrateDxvkStagingFolder] Migration failed (non-fatal) — {ex.Message}");
        }
    }
}
