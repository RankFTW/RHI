using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    // ── Update checking ───────────────────────────────────────────────────────────

    private async Task CheckForUpdatesAsync(List<GameCardViewModel> cards, List<InstalledModRecord> records, List<AuxInstalledRecord> auxRecords, bool userInitiated = false)
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

        // Nexus GraphQL calls are gated by the "Background Update Checks" setting.
        // "On" (default): full checks including Nexus. "Minimal": Nexus only on explicit user action.
        // forceCheck = true when user clicks Refresh/Update All, so Nexus always runs on explicit action.
        bool nexusEnabled = userInitiated || _settingsViewModel.BackgroundUpdateChecks == "On";

        // "Minimal" background checks: skip all component update checks unless user explicitly triggered.
        // Manifests, PCGW, DLSS manifest, and shader packs still run (they're in RunBackgroundScanAndMergeAsync).
        if (!userInitiated && _settingsViewModel.BackgroundUpdateChecks == "Minimal")
        {
            _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] BackgroundUpdateChecks=Minimal — skipping all component update checks (user-initiated only)");
            return;
        }

        // If the user changed the channel to Custom after the last install, the record's Channel
        // is stale — patch it here so CheckReShadeUpdateLocal correctly skips custom/legacy channels.
        foreach (var card in cards)
        {
            if (card.RsRecord == null) continue;
            var effectiveChannel = ResolveReShadeChannel(card.GameName, card.Source ?? "");
            if (!string.Equals(card.RsRecord.Channel, effectiveChannel, StringComparison.OrdinalIgnoreCase))
            {
                _crashReporter.Log($"[CheckForUpdatesAsync] Patching RS channel for '{card.GameName}': '{card.RsRecord.Channel}' → '{effectiveChannel}'");
                card.RsRecord.Channel = effectiveChannel;
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
        // Note: Nexus check uses a different API (GraphQL, not GitHub) so it runs regardless
        if (_etagCache.IsRateLimited)
        {
            _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] GitHub API rate limited — skipping remaining GitHub-based update checks");

            // Still run the Nexus check (uses Nexus GraphQL API, not GitHub) — only if background checks are enabled
            if (nexusEnabled)
            try
            {
                var nexusModsToCheck = cards
                    .Where(c => !c.ExcludeFromUpdateAllRenoDx
                        && (c.Status == GameStatus.Installed || c.Status == GameStatus.Available || c.Status == GameStatus.NotInstalled))
                    .Select(c =>
                    {
                        string? nexusUrl = null;
                        if (c.IsExternalOnly && !string.IsNullOrEmpty(c.ExternalUrl)
                            && c.ExternalUrl.Contains("nexusmods.com", StringComparison.OrdinalIgnoreCase))
                            nexusUrl = c.ExternalUrl;
                        else if (!string.IsNullOrEmpty(c.NexusUrl))
                            nexusUrl = c.NexusUrl;
                        return (c.GameName, NexusUrl: nexusUrl, c.RdxInstalledVersion);
                    })
                    .Where(x => !string.IsNullOrEmpty(x.NexusUrl))
                    .Select(x => (x.GameName, x.NexusUrl!, x.RdxInstalledVersion))
                    .ToList();

                if (nexusModsToCheck.Count > 0)
                {
                    var nexusUpdated = await _nexusUpdateService.CheckForUpdatesAsync(nexusModsToCheck).ConfigureAwait(false);
                    if (nexusUpdated.Count > 0)
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            foreach (var card in cards.Where(c => nexusUpdated.Contains(c.GameName)))
                            {
                                if (card.Status == GameStatus.Installed)
                                    card.Status = GameStatus.UpdateAvailable;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Nexus update check failed (rate-limited path) — {ex.Message}");
            }

            // ── Nexus update check for Luma mods (rate-limited path) ──
            if (nexusEnabled) await CheckLumaNexusUpdatesAsync(cards).ConfigureAwait(false);

            // Record successful check time even if rate-limited, so we don't hammer the API again immediately
            _settingsViewModel.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
            SaveSettingsPublic();
            SaveLibrary();
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
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS stable update result: {osUpdateAvailable}, cards with OS installed: {cards.Count(c => c.OsStatus == GameStatus.Installed)}");

            // Also check nightly if any installed game uses it
            bool anyNightly = cards.Any(c => c.OsStatus == GameStatus.Installed && GetOsVariant(c.GameName, c.Source ?? "") == "Nightly");
            if (anyNightly)
            {
                await _optiScalerService.CheckForNightlyUpdateAsync().ConfigureAwait(false);
                // Also refresh the available builds list so the cog dialog has current entries
                _ = _optiScalerService.FetchAvailableNightlyBuildsAsync();
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS nightly update result: {_optiScalerService.HasUpdateNightly}");
            }

            // Also check DLSS NR if any installed game uses it
            bool anyDlssNr = cards.Any(c => c.OsStatus == GameStatus.Installed && GetOsVariant(c.GameName, c.Source ?? "") == "DlssNr");
            if (anyDlssNr)
            {
                await _optiScalerService.CheckForDlssNrUpdateAsync().ConfigureAwait(false);
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS DLSS NR update result: {_optiScalerService.HasUpdateDlssNr}");
            }

            // Also check F5 DLSSNR if any installed game uses it
            bool anyF5 = cards.Any(c => c.OsStatus == GameStatus.Installed && GetOsVariant(c.GameName, c.Source ?? "") == "F5");
            if (anyF5)
            {
                await _optiScalerService.CheckForF5UpdateAsync().ConfigureAwait(false);
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS F5 DLSSNR update result: {_optiScalerService.HasUpdateF5}");
            }

            DispatcherQueue?.TryEnqueue(() =>
            {
                foreach (var card in cards.Where(c => c.OsStatus == GameStatus.Installed))
                {
                    var osVariant = GetOsVariant(card.GameName, card.Source ?? "");
                    bool osHasUpdate = osVariant switch {
                        "Nightly" => _optiScalerService.HasUpdateNightly,
                        "DlssNr"  => _optiScalerService.HasUpdateDlssNr,
                        "F5"      => _optiScalerService.HasUpdateF5,
                        _         => _optiScalerService.HasUpdate
                    };
                    if (osHasUpdate) card.OsStatus = GameStatus.UpdateAvailable;
                }

                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });

            // Check OptiPatcher update and auto-deploy if newer version available
            try
            {
                bool optiPatcherHasUpdate = await _optiScalerService.CheckOptiPatcherUpdateAsync().ConfigureAwait(false);
                if (optiPatcherHasUpdate)
                {
                    _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] OptiPatcher update available — downloading and auto-deploying");
                    await _optiScalerService.EnsureOptiPatcherStagingAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OptiPatcher update check failed — {ex.Message}");
            }

            // Check DLSS Enabler update and auto-deploy if newer version available
            try
            {
                var dlssEnablerService = App.Services.GetRequiredService<DlssEnablerService>();
                bool deHasUpdate = await dlssEnablerService.CheckForUpdateAsync().ConfigureAwait(false);
                if (deHasUpdate)
                {
                    await dlssEnablerService.EnsureStagingAsync().ConfigureAwait(false);
                    dlssEnablerService.AutoUpdateStandaloneInstalls(_allCards, this);
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DLSS Enabler check failed — {ex.Message}");
            }

            // Check RTX Encore update and auto-deploy to installed games
            try
            {
                var rtxEncoreSvc = App.Services.GetRequiredService<RtxEncoreService>();
                var rtxOldVersion = rtxEncoreSvc.StagedVersion;
                await rtxEncoreSvc.CheckForUpdateAsync().ConfigureAwait(false);
                if (rtxEncoreSvc.HasUpdate)
                {
                    await rtxEncoreSvc.EnsureStagingAsync().ConfigureAwait(false);
                    if (rtxEncoreSvc.IsStagingReady)
                    {
                        App.Services.GetRequiredService<IUpdateLogService>().Record(new Models.UpdateLogEntry
                        {
                            Timestamp     = DateTime.UtcNow,
                            Category      = "Component",
                            ComponentName = "RTX Encore",
                            OldVersion    = rtxOldVersion,
                            NewVersion    = rtxEncoreSvc.StagedVersion,
                        });
                        foreach (var c in cards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
                        {
                            var installedAs = GetRtxEncoreInstalledAs(c.GameName, c.Source ?? "");
                            if (!string.IsNullOrEmpty(installedAs) && rtxEncoreSvc.IsInstalledIn(c.InstallPath!, installedAs))
                                await rtxEncoreSvc.InstallAsync(c.InstallPath!, installedAs, installedAs).ConfigureAwait(false);
                        }
                    }
                }
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] RTX Encore: cached={rtxOldVersion}, remote={rtxEncoreSvc.LatestVersion}, hasUpdate={rtxEncoreSvc.HasUpdate}");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] RTX Encore check failed — {ex.Message}");
            }

            // Check RenoDX DLSS5 addon update and auto-deploy if newer version available
            try
            {
                var rdx5Service = App.Services.GetRequiredService<Renodx5AddonService>();
                bool rdx5HasUpdate = await rdx5Service.CheckForUpdateAsync().ConfigureAwait(false);
                if (rdx5HasUpdate)
                    await rdx5Service.EnsureStagingAsync().ConfigureAwait(false);
                // Also refresh the available versions cache (used by the NR section addon version picker)
                await rdx5Service.FetchAndCacheAvailableVersionsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] RenoDX DLSS5 addon check failed — {ex.Message}");
            }

            // Check DLSS Tool (ShortFuse) SF variant update
            try
            {
                var rdx5Service = App.Services.GetRequiredService<Renodx5AddonService>();
                bool sfHasUpdate = await rdx5Service.CheckForSfUpdateAsync().ConfigureAwait(false);
                if (sfHasUpdate)
                    await rdx5Service.EnsureSfStagingAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DLSS Tool SF check failed — {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS update check failed — {ex.Message}");
        }
        }

        // Check DXVK for updates — check each variant that's actually in use
        try
        {
            var dxvkCards = cards.Where(c => c.DxvkStatus == GameStatus.Installed).ToList();
            var variantsInUse = dxvkCards
                .Select(c => ResolveDxvkVariant(c.GameName, c.Source ?? ""))
                .Distinct()
                .ToList();

            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK variants in use: [{string.Join(", ", variantsInUse)}], cards with DXVK installed: {dxvkCards.Count}");

            foreach (var variant in variantsInUse)
            {
                // Temporarily switch the service variant to check this one
                var savedVariant = _dxvkService.SelectedVariant;
                _dxvkService.SelectedVariant = variant;
                await _dxvkService.CheckForUpdateAsync().ConfigureAwait(false);
                var hasUpdate = _dxvkService.HasUpdate;
                _dxvkService.SelectedVariant = savedVariant;

                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK {variant} update result: {hasUpdate}");

                if (hasUpdate)
                {
                    var capturedVariant = variant;
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        int flagged = 0;
                        foreach (var card in dxvkCards)
                        {
                            var resolvedVariant = ResolveDxvkVariant(card.GameName, card.Source ?? "");
                            if (resolvedVariant == capturedVariant)
                            {
                                card.DxvkStatus = GameStatus.UpdateAvailable;
                                flagged++;
                            }
                        }
                        _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK {capturedVariant}: flagged {flagged} card(s) as UpdateAvailable");

                        HasUpdatesAvailable = AnyUpdateAvailable;
                        OnPropertyChanged(nameof(AnyUpdateAvailable));
                        OnPropertyChanged(nameof(UpdateAllBtnBackground));
                        OnPropertyChanged(nameof(UpdateAllBtnForeground));
                        OnPropertyChanged(nameof(UpdateAllBtnBorder));
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK update check failed — {ex.Message}");
        }

        // ── Nexus Mods update check (external-only games with Nexus URLs) ─────────
        if (nexusEnabled)
        try
        {
            // For external-only games, the Nexus URL is in ExternalUrl (set by manifest forceExternalOnly).
            // For wiki-matched games with both Snapshot+Nexus, it's in NexusUrl (from GameMod).
            var nexusModsToCheck = cards
                .Where(c => !c.ExcludeFromUpdateAllRenoDx
                    && (c.Status == GameStatus.Installed || c.Status == GameStatus.Available || c.Status == GameStatus.NotInstalled))
                .Select(c =>
                {
                    // Resolve the Nexus URL: ExternalUrl for external-only, NexusUrl for wiki-matched
                    string? nexusUrl = null;
                    if (c.IsExternalOnly && !string.IsNullOrEmpty(c.ExternalUrl)
                        && c.ExternalUrl.Contains("nexusmods.com", StringComparison.OrdinalIgnoreCase))
                        nexusUrl = c.ExternalUrl;
                    else if (!string.IsNullOrEmpty(c.NexusUrl))
                        nexusUrl = c.NexusUrl;
                    return (c.GameName, NexusUrl: nexusUrl, c.RdxInstalledVersion);
                })
                .Where(x => !string.IsNullOrEmpty(x.NexusUrl))
                .Select(x => (x.GameName, x.NexusUrl!, x.RdxInstalledVersion))
                .ToList();

            if (nexusModsToCheck.Count > 0)
            {
                var nexusUpdated = await _nexusUpdateService.CheckForUpdatesAsync(nexusModsToCheck).ConfigureAwait(false);
                if (nexusUpdated.Count > 0)
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        foreach (var card in cards.Where(c => nexusUpdated.Contains(c.GameName)))
                        {
                            if (card.Status == GameStatus.Installed)
                                card.Status = GameStatus.UpdateAvailable;
                        }
                        // Nexus updates do NOT contribute to Update All button
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Nexus update check failed — {ex.Message}");
        }

        // ── Nexus Mods update check for Luma mods ──
        if (nexusEnabled) await CheckLumaNexusUpdatesAsync(cards).ConfigureAwait(false);
        try
        {
            var emuCards = cards.Where(c => c.IsEmulator && c.Status == GameStatus.Installed && c.EmulatorAddonNames?.Count > 0).ToList();
            _crashReporter.Log($"[CheckForUpdatesAsync] Emulator update check: {emuCards.Count} card(s) eligible (total emulators={cards.Count(c => c.IsEmulator)}, installed={cards.Count(c => c.IsEmulator && c.Status == GameStatus.Installed)}, hasAddons={cards.Count(c => c.IsEmulator && c.EmulatorAddonNames?.Count > 0)})");
            foreach (var emuCard in emuCards)
            {
                var deployPath = ModInstallService.GetAddonDeployPath(emuCard.InstallPath);
                bool anyUpdate = false;

                foreach (var wikiName in emuCard.EmulatorAddonNames!)
                {
                    var mod = _allMods.FirstOrDefault(m => m.Name.Equals(wikiName, StringComparison.OrdinalIgnoreCase));
                    if (mod?.SnapshotUrl == null) { _crashReporter.Log($"[CheckForUpdatesAsync] Emulator addon '{wikiName}' — no wiki match or no SnapshotUrl"); continue; }

                    var fileName = Path.GetFileName(mod.SnapshotUrl);
                    var localFile = Path.Combine(deployPath, fileName);
                    if (!File.Exists(localFile)) { _crashReporter.Log($"[CheckForUpdatesAsync] Emulator addon '{wikiName}' — file missing: {fileName}"); anyUpdate = true; break; }

                    // Check remote size
                    try
                    {
                        using var headResp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, mod.SnapshotUrl)).ConfigureAwait(false);
                        if (headResp.IsSuccessStatusCode && headResp.Content.Headers.ContentLength.HasValue)
                        {
                            var localSize = new FileInfo(localFile).Length;
                            if (headResp.Content.Headers.ContentLength.Value != localSize)
                            {
                                _crashReporter.Log($"[CheckForUpdatesAsync] Emulator addon '{wikiName}' — size mismatch: local={localSize}, remote={headResp.Content.Headers.ContentLength.Value}");
                                anyUpdate = true;
                                break;
                            }
                        }
                    }
                    catch { /* Skip on error — don't flag as update */ }
                }

                if (anyUpdate)
                {
                    _crashReporter.Log($"[CheckForUpdatesAsync] Emulator '{emuCard.GameName}' has updates available");
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        emuCard.Status = GameStatus.UpdateAvailable;
                        HasUpdatesAvailable = AnyUpdateAvailable;
                        OnPropertyChanged(nameof(AnyUpdateAvailable));
                        OnPropertyChanged(nameof(UpdateAllBtnBackground));
                        OnPropertyChanged(nameof(UpdateAllBtnForeground));
                        OnPropertyChanged(nameof(UpdateAllBtnBorder));
                    });
                }
                else
                {
                    _crashReporter.Log($"[CheckForUpdatesAsync] Emulator '{emuCard.GameName}' is up to date");
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Emulator update check failed — {ex.Message}");
        }

        // ── DOF Fix update check ──────────────────────────────────────────────
        try
        {
            await _dofFixService.CheckForUpdateAsync().ConfigureAwait(false);
            if (_dofFixService.HasUpdate)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var card in cards.Where(c => c.IsDofFixEligible && c.DofFixStatus == GameStatus.Installed))
                        card.DofFixStatus = GameStatus.UpdateAvailable;

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
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DOF Fix update check failed — {ex.Message}");
        }

        // Record successful check time
        _settingsViewModel.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
        SaveSettingsPublic();
        SaveLibrary();
        _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] Update check complete — cooldown timestamp saved");
    }

    /// <summary>Baseline key for Luma mods checked by Nexus date, kept apart from RenoDX Nexus baselines (keyed by game name).</summary>
    internal static string LumaNexusKey(string gameName) => "Luma|" + gameName;

    /// <summary>
    /// Nexus update check for Luma mods.
    /// Nexus Premium installs compare the installed Nexus file ID (keyed by game name).
    /// Other mods the wiki lists as Nexus-only compare the mod's Nexus "updated" date with a baseline
    /// taken when first seen; a new install (new InstalledAt) resets the baseline.
    /// </summary>
    private async Task CheckLumaNexusUpdatesAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        try
        {
            var installed = cards
                .Where(c => c.LumaStatus is GameStatus.Installed or GameStatus.UpdateAvailable
                         && !c.IsHidden
                         && c.LumaNexusUrl != null
                         && c.LumaRecord != null)
                .ToList();

            var premium = installed
                .Where(c => c.LumaRecord!.NexusFileId != null && c.LumaStatus == GameStatus.Installed)
                .Select(c => (c.GameName, c.LumaNexusUrl!, (string?)c.LumaRecord!.NexusFileId!.Value.ToString()));
            var nexusOnly = installed
                .Where(c => c.LumaRecord!.NexusFileId == null && c.LumaMod?.DownloadUrl == null)
                .Select(c => (LumaNexusKey(c.GameName), c.LumaNexusUrl!, (string?)c.LumaRecord!.InstalledAt.ToString("o")));
            var toCheck = premium.Concat(nexusOnly).ToList();
            if (toCheck.Count == 0) return;

            _crashReporter.Log($"[MainViewModel.CheckLumaNexusUpdatesAsync] Checking {toCheck.Count} Luma Nexus mod(s) for updates");
            var updated = await _nexusUpdateService.CheckForUpdatesAsync(toCheck).ConfigureAwait(false);
            if (updated.Count == 0) return;

            DispatcherQueue?.TryEnqueue(() =>
            {
                foreach (var card in installed.Where(c => updated.Contains(c.GameName) || updated.Contains(LumaNexusKey(c.GameName))))
                {
                    if (card.LumaStatus == GameStatus.Installed)
                        card.LumaStatus = GameStatus.UpdateAvailable;
                }
                NotifyUpdateButtonChanged();
            });
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckLumaNexusUpdatesAsync] Luma Nexus update check failed — {ex.Message}");
        }
    }
}
