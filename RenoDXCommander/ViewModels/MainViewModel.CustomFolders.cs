// MainViewModel.CustomFolders.cs -- Custom Game Folders: automatic scans, manual Scan Now, review and import.
//
// Design: custom-folder games join the SAME pipeline as store games. Automatic scans run inside the normal
// detection pass (startup background scan / Refresh / Full Refresh) and hand DetectedGame entries with
// Source = "Custom" to the existing merge + BuildCards code. Games the user imports from the review dialog are
// built with the existing BuildCards and persisted by the existing SaveLibrary (they are ordinary library entries).

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private readonly ICustomFolderScanService _customFolderScanService;
    private CancellationTokenSource? _customScanCts;
    private bool _fullRefreshInProgress;

    /// <summary>Low-confidence candidates found by automatic scans, waiting for the user to review them.</summary>
    [ObservableProperty] private List<CustomGameCandidate> _pendingCustomCandidates = new();

    public Visibility CustomReviewButtonVisibility =>
        PendingCustomCandidates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string CustomReviewButtonText =>
        PendingCustomCandidates.Count == 1
            ? "1 custom folder candidate needs review"
            : $"{PendingCustomCandidates.Count} custom folder candidates need review";

    partial void OnPendingCustomCandidatesChanged(List<CustomGameCandidate> value)
    {
        OnPropertyChanged(nameof(CustomReviewButtonVisibility));
        OnPropertyChanged(nameof(CustomReviewButtonText));
    }

    /// <summary>Cancels any running custom-folder scan (called when the window closes).</summary>
    public void CancelCustomFolderScan() => Interlocked.Exchange(ref _customScanCts, null)?.Cancel();

    private CancellationToken BeginCustomScan()
    {
        // A newer scan (e.g. the user pressed Refresh again) supersedes the running one.
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _customScanCts, cts)?.Cancel();
        return cts.Token;
    }

    // ── Automatic scans (startup / Refresh / Full Refresh) ───────────────────────

    /// <summary>
    /// Normal store detection plus custom folders. Store results are returned exactly as before; when no custom
    /// folder is configured and no custom game is known this is a pure pass-through.
    /// </summary>
    private async Task<List<DetectedGame>> DetectAllGamesDedupedAsync(CustomScanTrigger trigger = CustomScanTrigger.Startup)
    {
        var storeGames = await _gameInitializationService.DetectAllGamesDedupedAsync();

        var folders = _settingsViewModel.CustomGameFolders.ToList();
        var known = LoadKnownCustomGames();
        if (folders.Count == 0 && known.Count == 0) return storeGames;

        var discovered = new List<DetectedGame>();
        try
        {
            if (CustomGameMerger.ShouldScanForNew(trigger, _settingsViewModel.CustomFoldersAutoScan, folders.Count))
            {
                var token = BeginCustomScan();
                var result = await _customFolderScanService.ScanAsync(
                    folders, ExistingGamePaths(storeGames, known), ReservedGameNames(known), token);

                // High-confidence games are imported straight away; ambiguous ones wait for review
                // (non-blocking button) instead of interrupting every launch with a dialog.
                // Games the user declined in an earlier review are left alone.
                var (autoImport, pending) = CustomGameMerger.SplitForAutomaticScan(
                    result.Candidates, _settingsViewModel.CustomFolderDismissed);
                discovered.AddRange(autoImport.Select(c => c.ToDetectedGame()));
                DispatcherQueue?.TryEnqueue(() => PendingCustomCandidates = pending);

                _crashReporter.Log($"[MainViewModel.CustomFolders] {trigger}: +{discovered.Count} auto-imported, {pending.Count} awaiting review, " +
                    $"{result.UnavailableRoots.Count} folder(s) unavailable");
            }
        }
        catch (OperationCanceledException)
        {
            _crashReporter.Log("[MainViewModel.CustomFolders] Scan cancelled");
        }
        catch (Exception ex)
        {
            // A custom-folder problem must never cost the user their library.
            _crashReporter.Log($"[MainViewModel.CustomFolders] Scan failed — {ex.Message}");
        }

        return CustomGameMerger.Merge(storeGames, known, discovered, folders);
    }

    private List<DetectedGame> LoadKnownCustomGames()
    {
        try
        {
            var lib = _gameLibraryService.Load();
            if (lib == null) return new();
            return _gameLibraryService.ToDetectedGames(lib).Where(g => CustomGameMerger.IsCustomSource(g.Source)).ToList();
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CustomFolders] Could not read known custom games — {ex.Message}");
            return new();
        }
    }

    private List<string> ExistingGamePaths(IEnumerable<DetectedGame>? storeGames, IEnumerable<DetectedGame> known)
    {
        var paths = new List<string>();
        if (storeGames != null) paths.AddRange(storeGames.Select(g => g.InstallPath));
        paths.AddRange(known.Select(g => g.InstallPath));
        paths.AddRange(_manualGames.Select(g => g.InstallPath));
        // Cards currently on screen (root path and resolved exe folder) — covers Scan Now, where there is no fresh store scan.
        paths.AddRange(_allCards.SelectMany(c => new[] { c.InstallPath, c.DetectedGame?.InstallPath ?? "" }));
        return paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> ReservedGameNames(IEnumerable<DetectedGame> known)
        => _manualGames.Select(g => g.Name)
            .Concat(known.Select(g => g.Name))
            .Concat(_allCards.Where(c => c.IsManuallyAdded || CustomGameMerger.IsCustomSource(c.Source)).Select(c => c.GameName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // ── Scan Now / review / import ────────────────────────────────────────────────

    /// <summary>
    /// Scans only the configured custom folders and returns every NEW candidate (including ones the user
    /// previously declined, so an explicit scan always gives a fresh look). Nothing is imported here.
    /// </summary>
    public async Task<CustomFolderScanResult> ScanCustomFoldersNowAsync()
    {
        var folders = _settingsViewModel.CustomGameFolders.ToList();
        if (!CustomGameMerger.ShouldScanForNew(CustomScanTrigger.Manual, _settingsViewModel.CustomFoldersAutoScan, folders.Count))
            return new CustomFolderScanResult();

        var known = LoadKnownCustomGames();
        var token = BeginCustomScan();
        var result = await _customFolderScanService.ScanAsync(folders, ExistingGamePaths(null, known), ReservedGameNames(known), token);
        _crashReporter.Log($"[MainViewModel.CustomFolders] Scan Now: {result.Candidates.Count} new candidate(s), " +
            $"{result.UnavailableRoots.Count} folder(s) unavailable");
        return result;
    }

    /// <summary>
    /// Imports the user's selection through the normal card pipeline. Candidates that were shown but left
    /// unchecked are remembered as declined so automatic scans stop suggesting them.
    /// Returns the number of games added.
    /// </summary>
    public async Task<int> ImportCustomCandidatesAsync(
        IReadOnlyList<CustomGameCandidate> selected, IReadOnlyList<CustomGameCandidate> declined)
    {
        if (declined.Count > 0)
        {
            var dismissed = _settingsViewModel.CustomFolderDismissed.ToList();
            foreach (var d in declined)
                if (!dismissed.Any(x => CustomFolderPaths.AreSame(x, d.InstallPath)))
                    dismissed.Add(d.InstallPath);
            _settingsViewModel.CustomFolderDismissed = dismissed;
            SaveSettingsPublic();
        }

        var handled = selected.Concat(declined).Select(c => c.InstallPath).ToList();
        PendingCustomCandidates = PendingCustomCandidates
            .Where(p => !handled.Any(h => CustomFolderPaths.AreSame(h, p.InstallPath))).ToList();

        if (selected.Count == 0) return 0;

        var games = selected
            .Select(c => c.ToDetectedGame())
            .Where(g => !_allCards.Any(c => CustomFolderPaths.Overlaps(c.DetectedGame?.InstallPath ?? c.InstallPath, g.InstallPath)))
            .Where(g => !_manifestBlacklist.Contains(g.Name)
                     && !_manifestBlacklistPrefixes.Any(p => g.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (games.Count == 0) return 0;

        // The review button can be clicked while a startup/Refresh scan is still building cards.
        // Wait (bounded) so two BuildCards runs never mutate the shared caches at the same time.
        for (int i = 0; i < 480 && (IsLoading || IsBackgroundScanning); i++)
            await Task.Delay(250);

        var records = _installer.LoadAll();
        var auxRecords = _auxInstaller.LoadAll();
        var addonCache = _gameLibraryService.Load()?.AddonScanCache
                         ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Same card pipeline as store games: engine, graphics API, bitness, DLSS/Streamline scan,
        // NVIDIA profile + manifest matching and neural-rendering method selection all run inside BuildCards.
        var cards = await Task.Run(() => BuildCards(games, records, auxRecords, addonCache, _genericNotes));
        ApplyCardOverrides(cards);
        ApplyManifestCardOverrides(_manifest, cards);
        GraphicsApiDetector.SaveCache();
        SaveGameApiCache();

        _allCards.AddRange(cards.Where(c => !_allCards.Any(e => GameKey.FromCard(e.GameName, e.Source).ToKey() == GameKey.FromCard(c.GameName, c.Source).ToKey())));
        _allCards = _allCards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();
        _filterViewModel.SetAllCards(_allCards);
        _filterViewModel.UpdateCounts();
        _filterViewModel.ApplyFilter();
        SaveLibrary();

        _crashReporter.Log($"[MainViewModel.CustomFolders] Imported {cards.Count} custom game(s): {string.Join(", ", cards.Select(c => c.GameName))}");
        return cards.Count;
    }
}
