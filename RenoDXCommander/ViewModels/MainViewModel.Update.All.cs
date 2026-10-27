using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Eligibility: card must not be hidden, not excluded from Update All.
    /// </summary>
    private IEnumerable<GameCardViewModel> UpdateAllEligible() =>
        _updateOrchestrationService.UpdateAllEligible(_allCards);

    public Task UpdateAllRenoDxAsync() => UpdateAllRenoDxAsync(_allCards);

    /// <summary>Updates only the given cards (the silent auto-updater passes the outdated, non-running ones).</summary>
    public async Task UpdateAllRenoDxAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        // Capture games with Nexus update flags BEFORE running updates
        var nexusFlaggedGames = cards
            .Where(c => c.Status == GameStatus.UpdateAvailable && !c.IsExternalOnly)
            .Select(c => c.GameName)
            .ToList();

        await _updateOrchestrationService.UpdateAllRenoDxAsync(
            cards, _dllOverrideService, DispatcherQueue,
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

        // Reset Nexus baselines for games that had update flags (clears false positives from Nexus page edits)
        foreach (var gameName in nexusFlaggedGames)
            _nexusUpdateService.ResetBaseline(gameName);

        NotifyUpdateButtonChanged();

        // Update emulator cards (bundle re-download)
        var emuCards = cards
            .Where(c => c.IsEmulator && c.Status == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRenoDx)
            .ToList();
        _crashReporter.Log($"[UpdateAllRenoDxAsync] Emulator cards eligible: {emuCards.Count} (total emulators: {cards.Count(c => c.IsEmulator)}, with UpdateAvailable: {cards.Count(c => c.IsEmulator && c.Status == GameStatus.UpdateAvailable)})");
        foreach (var card in emuCards)
            await InstallEmulatorAddonsAsync(card);
    }

    public Task UpdateAllReShadeAsync() => UpdateAllReShadeAsync(_allCards);

    /// <summary>Updates only the given cards (the silent auto-updater passes the outdated, non-running ones).</summary>
    public async Task UpdateAllReShadeAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        await _updateOrchestrationService.UpdateAllReShadeAsync(
            cards, _dllOverrideService, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            },
            shaderResolver: (gameName, store, shaderModeOverride) => ResolveShaderSelection(gameName, shaderModeOverride, store),
            manifestDllResolver: GetManifestDllNames,
            channelResolver: (gameName, store) => ResolveReShadeChannel(gameName, store ?? ""),
            keepRsIniUpdatedResolver: (gameName, store) => GetKeepRsIniUpdated(gameName, store));
    }

    public Task UpdateAllUlAsync() => UpdateAllUlAsync(_allCards);

    /// <summary>Updates only the given cards (the silent auto-updater passes the outdated, non-running ones).</summary>
    public async Task UpdateAllUlAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        var ulCards = cards.Where(c => c.UlStatus == GameStatus.UpdateAvailable && !c.IsHidden && !c.ExcludeFromUpdateAllUl).ToList();
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

    public Task UpdateAllDcAsync() => UpdateAllDcAsync(_allCards);

    /// <summary>Updates only the given cards (the silent auto-updater passes the outdated, non-running ones).</summary>
    public async Task UpdateAllDcAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        var dcCards = cards.Where(c => c.DcStatus == GameStatus.UpdateAvailable && !c.IsHidden && !c.ExcludeFromUpdateAllDc).ToList();
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

    public Task UpdateAllDxvkAsync() => UpdateAllDxvkAsync(_allCards);

    /// <summary>Updates only the given cards (the silent auto-updater passes the outdated, non-running ones).</summary>
    public async Task UpdateAllDxvkAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        var dxvkCards = GetUpdateAllDxvkEligibleCards(cards);
        if (dxvkCards.Count == 0) return;

        // Ensure global variant staging is available
        if (!_dxvkService.IsStagingReady)
        {
            try { await _dxvkService.EnsureStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllDxvkAsync] Staging failed — {ex.Message}"); return; }
        }

        var globalVariant = _dxvkService.SelectedVariant;

        foreach (var card in dxvkCards)
        {
            try
            {
                // Resolve per-game variant (may differ from global)
                var effectiveVariant = ResolveDxvkVariant(card.GameName, card.Source ?? "");
                if (effectiveVariant != globalVariant)
                {
                    // Switch to per-game variant, ensure its staging is ready
                    _dxvkService.SelectedVariant = effectiveVariant;
                    if (!_dxvkService.IsStagingReady)
                        await _dxvkService.EnsureStagingAsync();
                }
                else if (_dxvkService.SelectedVariant != globalVariant)
                {
                    _dxvkService.SelectedVariant = globalVariant;
                }

                await _dxvkService.UpdateAsync(card);
            }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllDxvkAsync] Failed for '{card.GameName}': {ex.Message}"); }
        }

        // Restore global variant
        _dxvkService.SelectedVariant = globalVariant;

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

    public Task UpdateAllOsAsync() => UpdateAllOsAsync(_allCards);

    /// <summary>Updates only the given cards (the silent auto-updater passes the outdated, non-running ones).</summary>
    public async Task UpdateAllOsAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        var osCards = cards.Where(c => c.OsStatus == GameStatus.UpdateAvailable && !c.IsHidden && !c.ExcludeFromUpdateAllOs).ToList();
        if (osCards.Count == 0) return;

        var stableCards  = osCards.Where(c => GetOsVariant(c.GameName, c.Source ?? "") == "Stable" || GetOsVariant(c.GameName, c.Source ?? "") == "").ToList();
        var nightlyCards = osCards.Where(c => GetOsVariant(c.GameName, c.Source ?? "") == "Nightly").ToList();
        var dlssNrCards  = osCards.Where(c => GetOsVariant(c.GameName, c.Source ?? "") == "DlssNr").ToList();
        var f5Cards      = osCards.Where(c => GetOsVariant(c.GameName, c.Source ?? "") == "F5").ToList();

        // Ensure stable staging if any stable cards need updating
        if (stableCards.Count > 0 && !_optiScalerService.IsStagingReady)
        {
            try { await _optiScalerService.EnsureStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] Stable staging failed — {ex.Message}"); return; }
        }

        // Ensure nightly staging if any nightly cards need updating
        if (nightlyCards.Count > 0 && !_optiScalerService.IsStagingReadyNightly)
        {
            try { await _optiScalerService.EnsureNightlyStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] Nightly staging failed — {ex.Message}"); return; }
        }

        // Ensure DLSS NR staging if any DlssNr cards need updating
        if (dlssNrCards.Count > 0 && !_optiScalerService.IsStagingReadyDlssNr)
        {
            try { await _optiScalerService.EnsureDlssNrStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] DLSS NR staging failed — {ex.Message}"); return; }
        }

        // Ensure F5 staging if any F5 cards need updating
        if (f5Cards.Count > 0 && !_optiScalerService.IsStagingReadyF5)
        {
            try { await _optiScalerService.EnsureF5StagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] F5 DLSSNR staging failed — {ex.Message}"); return; }
        }

        foreach (var card in osCards)
        {
            var cardVariant = GetOsVariant(card.GameName, card.Source ?? "");
            // For nightly: pass the pinned build date if set (exempt games are already filtered out above)
            var nightlyBuild = cardVariant == "Nightly" ? GetOsNightlyBuild(card.GameName, card.Source ?? "") : null;
            if (!string.IsNullOrEmpty(nightlyBuild))
            {
                // Pinned build — should have been excluded by ExcludeFromUpdateAllOs, but guard here too
                _crashReporter.Log($"[UpdateAllOsAsync] Skipping '{card.GameName}' — pinned to nightly build {nightlyBuild}");
                continue;
            }
            try { await _optiScalerService.UpdateAsync(card, variantHint: cardVariant, nightlyBuildHint: null); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] Failed for '{card.GameName}': {ex.Message}"); continue; }

            // Re-deploy Streamline and DLSS Enabler if they were enabled — UpdateAsync doesn't handle this
            if ((cardVariant == "Nightly" || cardVariant == "DlssNr" || cardVariant == "F5") && !string.IsNullOrEmpty(card.InstallPath))
            {
                if (GetOsDeployStreamline(card.GameName, card.Source ?? ""))
                {
                    try
                    {
                        var slVersion = GetOsStreamlineVersion(card.GameName, card.Source ?? "");
                        _optiScalerService.DeployStreamlineToGame(card.InstallPath, slVersion);
                        _crashReporter.Log($"[UpdateAllOsAsync] Re-deployed Streamline to '{card.GameName}' post-update");
                    }
                    catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] Streamline re-deploy failed for '{card.GameName}' — {ex.Message}"); }
                }
                if (GetOsDeployDlssEnabler(card.GameName, card.Source ?? ""))
                {
                    try
                    {
                        var dlssEnablerService = App.Services.GetRequiredService<DlssEnablerService>();
                        var optiScalerDir = System.IO.Path.Combine(card.InstallPath, "OptiScaler");
                        await dlssEnablerService.InstallAsync(optiScalerDir).ConfigureAwait(false);
                        _crashReporter.Log($"[UpdateAllOsAsync] Re-deployed DLSS Enabler to '{card.GameName}' post-update");
                    }
                    catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] DLSS Enabler re-deploy failed for '{card.GameName}' — {ex.Message}"); }
                }
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

    public Task UpdateAllRefAsync() => UpdateAllRefAsync(_allCards);

    /// <summary>Updates only the given cards (the silent auto-updater passes the outdated, non-running ones).</summary>
    public async Task UpdateAllRefAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        await _updateOrchestrationService.UpdateAllREFrameworkAsync(
            cards, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });
    }

    public Task UpdateAllLumaAsync() => UpdateAllLumaAsync(_allCards);

    /// <summary>Updates only the given cards (the silent auto-updater passes the outdated, non-running ones).</summary>
    public async Task UpdateAllLumaAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        var lumaCards = cards.Where(c => c.LumaStatus == GameStatus.UpdateAvailable
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

    public Task UpdateAllDofFixAsync() => UpdateAllDofFixAsync(_allCards);

    /// <summary>Updates only the given cards (the silent auto-updater passes the outdated, non-running ones).</summary>
    public async Task UpdateAllDofFixAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        await _updateOrchestrationService.UpdateAllDofFixAsync(
            cards, _dofFixService, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });
    }
}

