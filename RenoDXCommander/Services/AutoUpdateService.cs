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
        if (!_settings.AutoUpdateComponents) return;
        if (_viewModel == null) return;

        _ = Task.Run(RunPassAsync);
    }

    // ── Main pass ─────────────────────────────────────────────────────────────────

    private async Task RunPassAsync()
    {
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
        if (_viewModel == null) return;

        // Collect every card with any pending update (mirrors AnyUpdateAvailable logic).
        var cards = _viewModel.AllCards.ToList();

        // ── RenoDX ──────────────────────────────────────────────────────────────
        var rdxCards = cards.Where(c =>
            c.Status == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllRenoDx
            && !c.IsExternalOnly
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        foreach (var card in rdxCards)
        {
            if (card.IsRunning) { EnqueueRetry(card, "RenoDX"); continue; }
            await TryUpdateOneAsync("RenoDX", card,
                () => _viewModel.UpdateAllRenoDxAsync()).ConfigureAwait(false);
            await Pause().ConfigureAwait(false);
        }

        // ── ReShade ─────────────────────────────────────────────────────────────
        var rsCards = cards.Where(c =>
            c.RsStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllReShade
            && !c.RequiresVulkanInstall
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        foreach (var card in rsCards)
        {
            if (card.IsRunning) { EnqueueRetry(card, "ReShade"); continue; }
            await TryUpdateOneAsync("ReShade", card,
                () => _viewModel.UpdateAllReShadeAsync()).ConfigureAwait(false);
            await Pause().ConfigureAwait(false);
        }

        // Vulkan ReShade — treat Vulkan cards as a group; skip the whole group if any is running.
        var vulkanCards = cards.Where(c =>
            c.RsStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllReShade
            && c.RequiresVulkanInstall
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        if (vulkanCards.Count > 0)
        {
            bool anyRunning = vulkanCards.Any(c => c.IsRunning);
            if (anyRunning)
            {
                foreach (var vc in vulkanCards) EnqueueRetry(vc, "ReShade");
            }
            else
            {
                // UpdateAllReShadeAsync handles Vulkan internally; just trigger it.
                try
                {
                    _crashReporter.Log($"[AutoUpdateService] Updating ReShade (Vulkan, {vulkanCards.Count} game(s))");
                    await _viewModel.UpdateAllReShadeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[AutoUpdateService] Vulkan ReShade update failed — {ex.Message}");
                }
                await Pause().ConfigureAwait(false);
            }
        }

        // ── ReLimiter ────────────────────────────────────────────────────────────
        var ulCards = cards.Where(c =>
            c.UlStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllUl
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        foreach (var card in ulCards)
        {
            if (card.IsRunning) { EnqueueRetry(card, "ReLimiter"); continue; }
            await TryUpdateOneAsync("ReLimiter", card,
                () => _viewModel.UpdateAllUlAsync()).ConfigureAwait(false);
            await Pause().ConfigureAwait(false);
        }

        // ── Display Commander ────────────────────────────────────────────────────
        var dcCards = cards.Where(c =>
            c.DcStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllDc
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        foreach (var card in dcCards)
        {
            if (card.IsRunning) { EnqueueRetry(card, "DC"); continue; }
            await TryUpdateOneAsync("DC", card,
                () => _viewModel.UpdateAllDcAsync()).ConfigureAwait(false);
            await Pause().ConfigureAwait(false);
        }

        // ── OptiScaler ───────────────────────────────────────────────────────────
        var osCards = cards.Where(c =>
            c.OsStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllOs
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        foreach (var card in osCards)
        {
            if (card.IsRunning) { EnqueueRetry(card, "OptiScaler"); continue; }
            await TryUpdateOneAsync("OptiScaler", card,
                () => _viewModel.UpdateAllOsAsync()).ConfigureAwait(false);
            await Pause().ConfigureAwait(false);
        }

        // ── RE Framework ─────────────────────────────────────────────────────────
        var refCards = cards.Where(c =>
            c.RefStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllRef
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        foreach (var card in refCards)
        {
            if (card.IsRunning) { EnqueueRetry(card, "REFramework"); continue; }
            await TryUpdateOneAsync("REFramework", card,
                () => _viewModel.UpdateAllRefAsync()).ConfigureAwait(false);
            await Pause().ConfigureAwait(false);
        }

        // ── DXVK ─────────────────────────────────────────────────────────────────
        var dxvkCards = cards.Where(c =>
            c.DxvkStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllDxvk
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        foreach (var card in dxvkCards)
        {
            if (card.IsRunning) { EnqueueRetry(card, "DXVK"); continue; }
            await TryUpdateOneAsync("DXVK", card,
                () => _viewModel.UpdateAllDxvkAsync()).ConfigureAwait(false);
            await Pause().ConfigureAwait(false);
        }

        // ── Luma ─────────────────────────────────────────────────────────────────
        var lumaCards = cards.Where(c =>
            c.LumaStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && c.LumaMod?.DownloadUrl != null
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        foreach (var card in lumaCards)
        {
            if (card.IsRunning) { EnqueueRetry(card, "Luma"); continue; }
            await TryUpdateOneAsync("Luma", card,
                () => _viewModel.UpdateAllLumaAsync()).ConfigureAwait(false);
            await Pause().ConfigureAwait(false);
        }

        // ── DOF Fix ──────────────────────────────────────────────────────────────
        var dofCards = cards.Where(c =>
            c.DofFixStatus == Models.GameStatus.UpdateAvailable
            && !c.IsHidden
            && !c.ExcludeFromUpdateAllDofFix
            && !string.IsNullOrEmpty(c.InstallPath)).ToList();

        foreach (var card in dofCards)
        {
            if (card.IsRunning) { EnqueueRetry(card, "DofFix"); continue; }
            await TryUpdateOneAsync("DofFix", card,
                () => _viewModel.UpdateAllDofFixAsync()).ConfigureAwait(false);
            await Pause().ConfigureAwait(false);
        }

        // If anything was deferred, start the retry watcher.
        if (!_retryQueue.IsEmpty)
            EnsureRetryTimerRunning();

        // ── Nexus Mods (dev-unlocked, premium only) ──────────────────────────
        if (FeatureFlags.NexusMods)
        {
            var nexusDl = _viewModel.AllCards.Count > 0
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
                    await TryUpdateOneAsync("Nexus", card,
                        () => _viewModel.UpdateNexusModAsync(card)).ConfigureAwait(false);
                    await Pause().ConfigureAwait(false);
                }
            }
        }
    }

    // ── Per-card update wrapper ───────────────────────────────────────────────────

    /// <summary>
    /// Calls the batch UpdateAll* method but catches IOExceptions (file locked)
    /// and re-queues the card for retry.  All other exceptions are logged and swallowed.
    /// </summary>
    private async Task TryUpdateOneAsync(string component, GameCardViewModel card, Func<Task> updateAll)
    {
        try
        {
            _crashReporter.Log($"[AutoUpdateService] Updating {component} for '{card.GameName}'");
            await updateAll().ConfigureAwait(false);
        }
        catch (IOException ioEx)
        {
            _crashReporter.Log($"[AutoUpdateService] {component} for '{card.GameName}' — file locked, queued for retry ({ioEx.Message})");
            EnqueueRetry(card, component);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[AutoUpdateService] {component} for '{card.GameName}' — failed ({ex.Message})");
        }
    }

    // ── Retry queue & timer ───────────────────────────────────────────────────────

    private void EnqueueRetry(GameCardViewModel card, string component)
    {
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
            if (_retryTimer != null) return; // already running
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
        if (_viewModel == null) return;
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

    private Task RunRetryUpdateAsync(RetryEntry entry) => entry.Component switch
    {
        "RenoDX"     => _viewModel!.UpdateAllRenoDxAsync(),
        "ReShade"    => _viewModel!.UpdateAllReShadeAsync(),
        "ReLimiter"  => _viewModel!.UpdateAllUlAsync(),
        "DC"         => _viewModel!.UpdateAllDcAsync(),
        "OptiScaler" => _viewModel!.UpdateAllOsAsync(),
        "REFramework"=> _viewModel!.UpdateAllRefAsync(),
        "DXVK"       => _viewModel!.UpdateAllDxvkAsync(),
        "Luma"       => _viewModel!.UpdateAllLumaAsync(),
        "DofFix"     => _viewModel!.UpdateAllDofFixAsync(),
        "Nexus"      => _viewModel!.UpdateNexusModAsync(entry.Card),
        _            => Task.CompletedTask,
    };

    private void StopRetryTimer()
    {
        lock (_retryTimerLock)
        {
            _retryTimer?.Dispose();
            _retryTimer = null;
        }
    }

    /// <summary>2-second breathing gap between individual card updates — keeps the pass non-disruptive.</summary>
    private static Task Pause() => Task.Delay(TimeSpan.FromSeconds(2));
}
