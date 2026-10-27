// AutoUpdateService.cs -- Silent background auto-update for all RHI-managed components.
// Triggered after each update check (startup and 4-hour periodic timer).
// Respects all per-game ExcludeFromUpdateAll* flags via the existing UpdateAll* methods.
// Games that are currently running are queued for retry; a timer polls every 60 seconds.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Silently installs pending component updates one game at a time in the background.
/// Does not show any dialogs or progress UI — all feedback is log-only.
/// </summary>
public class AutoUpdateService
{
    private readonly ICrashReporter _crashReporter;
    private readonly SettingsViewModel _settings;

    // Lazily assigned by the wiring code in MainViewModel.BackgroundScan.cs
    // so that we don't create a circular DI dependency.
    private MainViewModel? _viewModel;

    // Cards whose update was deferred because the game was running.
    // Simple string key "GameName|Source|Component" so we know what to retry.
    private readonly System.Collections.Concurrent.ConcurrentQueue<RetryEntry> _retryQueue = new();

    // 60-second polling timer — null until at least one entry enters the retry queue.
    private System.Threading.Timer? _retryTimer;
    private readonly object _retryTimerLock = new();

    // Guard so two passes never run concurrently (startup + 4h timer can overlap).
    private int _running; // 0 = idle, 1 = running  (Interlocked)

    private volatile bool _stopped;

    public void Stop()
    {
        _stopped = true;
        StopRetryTimer();
        _retryQueue.Clear();
    }

    private record RetryEntry(GameCardViewModel Card, string Component);

    public AutoUpdateService(ICrashReporter crashReporter, SettingsViewModel settings)
    {
        _crashReporter = crashReporter;
        _settings = settings;
    }

    /// <summary>Called once by the wiring code after DI is fully resolved.</summary>
    public void SetViewModel(MainViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    // ── Public entry point ────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full silent update pass if AutoUpdateComponents is enabled.
    /// Safe to call from any thread; returns immediately (fire-and-forget internally).
    /// </summary>
    public void TriggerAsync()
    {
        if (_stopped) return;
        if (!_settings.AutoUpdateComponents) return;
        if (_viewModel == null) return;

        _ = Task.Run(RunPassAsync);
    }

    // ── Main pass ─────────────────────────────────────────────────────────────────

    private async Task RunPassAsync()
    {
        if (_stopped) return;
        if (System.Threading.Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            _crashReporter.Log("[AutoUpdateService] Pass skipped — previous pass still running");
            return;
        }

        try
        {
            _crashReporter.Log("[AutoUpdateService] Silent auto-update pass started");
            // Run on background thread — the UpdateAll* methods internally dispatch
            // property changes to the UI thread via DispatcherQueue.TryEnqueue().
            await RunUpdatePassAsync().ConfigureAwait(false);
            _crashReporter.Log("[AutoUpdateService] Silent auto-update pass complete");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[AutoUpdateService] Pass failed — {ex.Message}");
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task RunUpdatePassAsync()
    {
        if (_stopped || _viewModel == null) return;
        var vm = _viewModel;

        // Snapshot once; each component below is updated with ONE call that covers only the
        // outdated, non-running games. (Calling the full Update All once per outdated game
        // repeated the work for every game and also updated games that were running.)
        var cards = vm.AllCards.ToList();

        // ── RenoDX ──────────────────────────────────────────────────────────────
        await UpdateComponentAsync("RenoDX", cards.Where(c =>
                c.Status == Models.GameStatus.UpdateAvailable
                && !c.IsHidden
                && !c.ExcludeFromUpdateAllRenoDx
                && !c.IsExternalOnly
                && !string.IsNullOrEmpty(c.InstallPath)),
            batch => vm.UpdateAllRenoDxAsync(batch)).ConfigureAwait(false);

        // ── ReShade (per-game DLL) ──────────────────────────────────────────────
        // Only outdated games: the manual Update All reinstalls into every ReShade game.
        await UpdateComponentAsync("ReShade", cards.Where(c =>
                c.RsStatus == Models.GameStatus.UpdateAvailable
                && !c.IsHidden
                && !c.ExcludeFromUpdateAllReShade
                && !c.RequiresVulkanInstall
                && !string.IsNullOrEmpty(c.InstallPath)),
            batch => vm.UpdateAllReShadeAsync(batch)).ConfigureAwait(false);

        // ── ReShade (Vulkan) ────────────────────────────────────────────────────
        // One global layer shared by every Vulkan game: update it only when none is running.
        var vulkanOutdated = cards.Where(c =>
            c.RsStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllReShade
            && c.RequiresVulkanInstall
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        if (vulkanOutdated.Count > 0)
        {
            var vulkanGroup = VulkanGroup(cards);
            if (vulkanGroup.Any(c => c.IsRunning))
            {
                foreach (var vc in vulkanOutdated) EnqueueRetry(vc, "ReShade");
            }
            else
            {
                await TryUpdateAsync("ReShade (Vulkan)", vulkanOutdated,
                    () => vm.UpdateAllReShadeAsync(vulkanGroup)).ConfigureAwait(false);
                await Pause().ConfigureAwait(false);
            }
        }

        // ── ReLimiter ────────────────────────────────────────────────────────────
        await UpdateComponentAsync("ReLimiter", cards.Where(c =>
                c.UlStatus == Models.GameStatus.UpdateAvailable
                && !c.IsHidden
                && !c.ExcludeFromUpdateAllUl
                && !string.IsNullOrEmpty(c.InstallPath)),
            batch => vm.UpdateAllUlAsync(batch)).ConfigureAwait(false);

        // ── Display Commander ────────────────────────────────────────────────────
        await UpdateComponentAsync("DC", cards.Where(c =>
                c.DcStatus == Models.GameStatus.UpdateAvailable
                && !c.IsHidden
                && !c.ExcludeFromUpdateAllDc
                && !string.IsNullOrEmpty(c.InstallPath)),
            batch => vm.UpdateAllDcAsync(batch)).ConfigureAwait(false);

        // ── OptiScaler ───────────────────────────────────────────────────────────
        await UpdateComponentAsync("OptiScaler", cards.Where(c =>
                c.OsStatus == Models.GameStatus.UpdateAvailable
                && !c.IsHidden
                && !c.ExcludeFromUpdateAllOs
                && !string.IsNullOrEmpty(c.InstallPath)),
            batch => vm.UpdateAllOsAsync(batch)).ConfigureAwait(false);

        // ── RE Framework ─────────────────────────────────────────────────────────
        await UpdateComponentAsync("REFramework", cards.Where(c =>
                c.RefStatus == Models.GameStatus.UpdateAvailable
                && !c.IsHidden
                && !c.ExcludeFromUpdateAllRef
                && !string.IsNullOrEmpty(c.InstallPath)),
            batch => vm.UpdateAllRefAsync(batch)).ConfigureAwait(false);

        // ── DXVK ─────────────────────────────────────────────────────────────────
        await UpdateComponentAsync("DXVK", cards.Where(c =>
                c.DxvkStatus == Models.GameStatus.UpdateAvailable
                && !c.IsHidden
                && !c.ExcludeFromUpdateAllDxvk
                && !string.IsNullOrEmpty(c.InstallPath)),
            batch => vm.UpdateAllDxvkAsync(batch)).ConfigureAwait(false);

        // ── Luma ─────────────────────────────────────────────────────────────────
        await UpdateComponentAsync("Luma", cards.Where(c =>
                c.LumaStatus == Models.GameStatus.UpdateAvailable
                && !c.IsHidden
                && c.LumaMod?.DownloadUrl != null
                && !string.IsNullOrEmpty(c.InstallPath)),
            batch => vm.UpdateAllLumaAsync(batch)).ConfigureAwait(false);

        // ── DOF Fix ──────────────────────────────────────────────────────────────
        await UpdateComponentAsync("DofFix", cards.Where(c =>
                c.DofFixStatus == Models.GameStatus.UpdateAvailable
                && !c.IsHidden
                && !c.ExcludeFromUpdateAllDofFix
                && !string.IsNullOrEmpty(c.InstallPath)),
            batch => vm.UpdateAllDofFixAsync(batch)).ConfigureAwait(false);

        // If anything was deferred, start the retry watcher.
        if (!_retryQueue.IsEmpty)
            EnsureRetryTimerRunning();

        // ── Nexus Mods (dev-unlocked, premium only) ──────────────────────────
        // Per-card by nature (UpdateNexusModAsync takes one card).
        if (FeatureFlags.NexusMods)
        {
            var nexusDl = vm.AllCards.Count > 0
                ? App.Services.GetRequiredService<NexusDownloadService>()
                : null;

            if (nexusDl?.IsApiKeyConfigured == true && nexusDl.IsPremium)
            {
                var nexusCards = cards.Where(c =>
                    c.Status == Models.GameStatus.UpdateAvailable
                    && !c.IsHidden
                    && !c.ExcludeFromUpdateAllRenoDx
                    && c.IsExternalOnly
                    && !string.IsNullOrEmpty(c.NexusUrl)
                    && !string.IsNullOrEmpty(c.InstallPath)).ToList();

                foreach (var card in nexusCards)
                {
                    if (card.IsRunning) { EnqueueRetry(card, "Nexus"); continue; }
                    await TryUpdateAsync("Nexus", new[] { card },
                        () => vm.UpdateNexusModAsync(card)).ConfigureAwait(false);
                    await Pause().ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Updates one component: running games go to the retry queue, the rest are updated
    /// together in a single call.
    /// </summary>
    private async Task UpdateComponentAsync(string component, IEnumerable<GameCardViewModel> outdated,
        Func<IReadOnlyList<GameCardViewModel>, Task> updateBatch)
    {
        if (_stopped) return;
        var batch = new List<GameCardViewModel>();
        foreach (var card in outdated)
        {
            if (card.IsRunning) EnqueueRetry(card, component);
            else batch.Add(card);
        }
        if (batch.Count == 0) return;

        await TryUpdateAsync(component, batch, () => updateBatch(batch)).ConfigureAwait(false);
        await Pause().ConfigureAwait(false);
    }

    /// <summary>All non-hidden Vulkan games — they share one ReShade layer.</summary>
    private static List<GameCardViewModel> VulkanGroup(IEnumerable<GameCardViewModel> cards) =>
        cards.Where(c => c.RequiresVulkanInstall && !c.IsHidden && !string.IsNullOrEmpty(c.InstallPath)).ToList();

    // ── Per-card update wrapper ───────────────────────────────────────────────────

    /// <summary>
    /// Runs an update call. The UpdateAll* methods catch per-game errors themselves; an
    /// IOException that still escapes (file locked) re-queues the affected games for retry.
    /// </summary>
    private async Task TryUpdateAsync(string component, IReadOnlyList<GameCardViewModel> games, Func<Task> update)
    {
        if (_stopped) return;
        var names = string.Join(", ", games.Select(g => "'" + g.GameName + "'"));
        try
        {
            _crashReporter.Log($"[AutoUpdateService] Updating {component} for {names}");
            await update().ConfigureAwait(false);
        }
        catch (IOException ioEx)
        {
            _crashReporter.Log($"[AutoUpdateService] {component} for {names} — file locked, queued for retry ({ioEx.Message})");
            foreach (var g in games) EnqueueRetry(g, component);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[AutoUpdateService] {component} for {names} — failed ({ex.Message})");
        }
    }

    // ── Retry queue & timer ───────────────────────────────────────────────────────

    private void EnqueueRetry(GameCardViewModel card, string component)
    {
        if (_stopped) return;
        // Avoid duplicate entries for the same card+component.
        // ConcurrentQueue has no Contains — we accept rare duplicates harmlessly
        // since UpdateAll* is idempotent when nothing needs updating.
        _retryQueue.Enqueue(new RetryEntry(card, component));
        _crashReporter.Log($"[AutoUpdateService] {component} for '{card.GameName}' queued for retry (running={card.IsRunning})");
    }

    private void EnsureRetryTimerRunning()
    {
        lock (_retryTimerLock)
        {
            if (_stopped || _retryTimer != null) return;
            _retryTimer = new System.Threading.Timer(
                _ => _ = Task.Run(RetryPassAsync),
                null,
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60));
            _crashReporter.Log("[AutoUpdateService] Retry watcher started (60s interval)");
        }
    }

    private async Task RetryPassAsync()
    {
        if (_stopped || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        try { await RetryPassCoreAsync().ConfigureAwait(false); }
        catch (Exception ex) { _crashReporter.Log($"[AutoUpdateService] Retry pass failed — {ex.Message}"); }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    private async Task RetryPassCoreAsync()
    {
        if (_stopped || _viewModel == null) return;
        if (_retryQueue.IsEmpty)
        {
            StopRetryTimer();
            return;
        }

        // Drain the queue into a snapshot to avoid infinite loops on persistent failures.
        var snapshot = new List<RetryEntry>();
        while (_retryQueue.TryDequeue(out var entry))
            snapshot.Add(entry);

        _crashReporter.Log($"[AutoUpdateService] Retry pass — {snapshot.Count} pending update(s)");

        var stillPending = new List<RetryEntry>();

        foreach (var entry in snapshot)
        {
            if (_stopped) return;
            if (entry.Card.IsRunning)
            {
                // Still running — re-queue.
                stillPending.Add(entry);
                continue;
            }

            try
            {
                _crashReporter.Log($"[AutoUpdateService] Retry: {entry.Component} for '{entry.Card.GameName}'");
                // Run on background thread — UpdateAll* methods internally dispatch
                // property changes to the UI thread via DispatcherQueue.TryEnqueue().
                await RunRetryUpdateAsync(entry).ConfigureAwait(false);
            }
            catch (IOException ioEx)
            {
                _crashReporter.Log($"[AutoUpdateService] Retry: {entry.Component} for '{entry.Card.GameName}' — still locked ({ioEx.Message}), re-queued");
                stillPending.Add(entry);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[AutoUpdateService] Retry: {entry.Component} for '{entry.Card.GameName}' — failed ({ex.Message}), dropping");
            }

            await Pause().ConfigureAwait(false);
        }

        // Re-enqueue anything still pending.
        foreach (var entry in stillPending)
            _retryQueue.Enqueue(entry);

        if (_retryQueue.IsEmpty)
        {
            _crashReporter.Log("[AutoUpdateService] Retry queue empty — stopping retry watcher");
            StopRetryTimer();
        }
    }

    // Retries update just the one game — except Vulkan ReShade, which is a shared layer.
    private Task RunRetryUpdateAsync(RetryEntry entry)
    {
        var vm = _viewModel!;
        var one = new[] { entry.Card };
        return entry.Component switch
        {
            "RenoDX"      => vm.UpdateAllRenoDxAsync(one),
            "ReShade"     => entry.Card.RequiresVulkanInstall
                                ? RetryVulkanAsync(vm)
                                : vm.UpdateAllReShadeAsync(one),
            "ReLimiter"   => vm.UpdateAllUlAsync(one),
            "DC"          => vm.UpdateAllDcAsync(one),
            "OptiScaler"  => vm.UpdateAllOsAsync(one),
            "REFramework" => vm.UpdateAllRefAsync(one),
            "DXVK"        => vm.UpdateAllDxvkAsync(one),
            "Luma"        => vm.UpdateAllLumaAsync(one),
            "DofFix"      => vm.UpdateAllDofFixAsync(one),
            "Nexus"       => vm.UpdateNexusModAsync(entry.Card),
            _             => Task.CompletedTask,
        };
    }

    private Task RetryVulkanAsync(MainViewModel vm)
    {
        var group = VulkanGroup(vm.AllCards.ToList());
        // A Vulkan game is still running: throw IOException so the retry loop re-queues it.
        if (group.Any(c => c.IsRunning))
            throw new IOException("A Vulkan game is still running");
        return vm.UpdateAllReShadeAsync(group);
    }

    private void StopRetryTimer()
    {
        lock (_retryTimerLock)
        {
            _retryTimer?.Dispose();
            _retryTimer = null;
        }
    }

    /// <summary>2-second breathing gap between individual card updates — keeps the pass non-disruptive.</summary>
    private Task Pause() => _stopped ? Task.CompletedTask : Task.Delay(TimeSpan.FromSeconds(2));
}
