// MainViewModel.CustomFolders.cs -- "Custom game folders": scans the folders from Settings for games the
// store scan doesn't find (launch + Refresh + Scan now), keeps the new ones as pending candidates for the
// window's notice / review dialog, and adds the ones the user picks as manual games with the chosen exe
// as their launch exe. Nothing is added without the user choosing it.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private volatile IReadOnlyList<CustomGameCandidate> _pendingCustomGames = Array.Empty<CustomGameCandidate>();
    private int _customFolderScanRunning;

    /// <summary>New games found by the last custom folder scan that the user hasn't added or ignored.</summary>
    public IReadOnlyList<CustomGameCandidate> PendingCustomGames => _pendingCustomGames;

    /// <summary>Raised on the UI thread when <see cref="PendingCustomGames"/> changes.</summary>
    public event Action? PendingCustomGamesChanged;

    public IReadOnlyList<string> CustomGameFolders => _gameNameService.CustomGameFolders;
    public IReadOnlySet<string> IgnoredCustomGameFolders => _gameNameService.IgnoredCustomGameFolders;

    /// <summary>Adds a folder to the list. Returns an error message for the user, or null on success.</summary>
    public string? AddCustomGameFolder(string path)
    {
        var error = CustomFolderScanService.ValidateFolder(path, _gameNameService.CustomGameFolders);
        if (error != null)
        {
            _crashReporter.Log($"[MainViewModel.AddCustomGameFolder] Refused '{path}' — {error}");
            return error;
        }
        _gameNameService.SetCustomGameFolders(_gameNameService.CustomGameFolders.Append(path));
        SaveNameMappings();
        _crashReporter.Log($"[MainViewModel.AddCustomGameFolder] Added '{CustomFolderScanService.NormalizePath(path)}'");
        return null;
    }

    public void RemoveCustomGameFolder(string path)
    {
        _gameNameService.SetCustomGameFolders(_gameNameService.CustomGameFolders
            .Where(f => !f.Equals(CustomFolderScanService.NormalizePath(path), StringComparison.OrdinalIgnoreCase)));
        SaveNameMappings();
        _crashReporter.Log($"[MainViewModel.RemoveCustomGameFolder] Removed '{path}'");
        // Candidates from that folder aren't offered any more
        SetPendingCustomGames(_pendingCustomGames.Where(c =>
            !c.CustomFolder.Equals(CustomFolderScanService.NormalizePath(path), StringComparison.OrdinalIgnoreCase)).ToList());
    }

    public void ClearIgnoredCustomGameFolders()
    {
        _gameNameService.ClearIgnoredCustomGameFolders();
        SaveNameMappings();
        _crashReporter.Log("[MainViewModel.ClearIgnoredCustomGameFolders] Cleared");
    }

    /// <summary>Background scan after the library is built (launch and Refresh). No-op without folders.</summary>
    private void StartCustomFolderScan(string reason)
    {
        if (_gameNameService.CustomGameFolders.Count == 0 || _backgroundStopped) return;
        // The library snapshot is taken on the UI thread (callers may be on a pool thread)
        var dq = DispatcherQueue;
        if (dq == null) return;
        dq.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _ = RunCustomFolderScanSafeAsync(reason));
    }

    private async Task RunCustomFolderScanSafeAsync(string reason)
    {
        try { await ScanCustomFoldersAsync(reason); }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex) { _crashReporter.Log($"[MainViewModel.StartCustomFolderScan] {reason} scan failed — {ex.Message}"); }
    }

    /// <summary>
    /// Scans the custom folders now and replaces the pending candidates. Returns the new list, or null
    /// when a scan is already running. Must be called on the UI thread (it snapshots the library).
    /// </summary>
    public async Task<IReadOnlyList<CustomGameCandidate>?> ScanCustomFoldersAsync(string reason)
    {
        if (Interlocked.Exchange(ref _customFolderScanRunning, 1) == 1)
        {
            _crashReporter.Log($"[MainViewModel.ScanCustomFoldersAsync] {reason}: a scan is already running");
            return null;
        }
        try
        {
            var folders = _gameNameService.CustomGameFolders;
            if (folders.Count == 0)
            {
                SetPendingCustomGames(Array.Empty<CustomGameCandidate>());
                return Array.Empty<CustomGameCandidate>();
            }
            using var activity = BeginActivity("Checking custom game folders...");
            var request = BuildCustomFolderScanRequest(folders);
            _crashReporter.Log($"[MainViewModel.ScanCustomFoldersAsync] {reason}: scanning {folders.Count} folder(s)");
            var scanner = App.Services.GetRequiredService<ICustomFolderScanService>();
            var found = await scanner.ScanAsync(request, _backgroundLifetime.Token).ConfigureAwait(false);
            if (_backgroundStopped) return found;
            SetPendingCustomGames(found);
            return found;
        }
        finally
        {
            Interlocked.Exchange(ref _customFolderScanRunning, 0);
        }
    }

    private CustomFolderScanRequest BuildCustomFolderScanRequest(IReadOnlyList<string> folders)
    {
        var cards = _allCards.ToList();
        var manual = _manualGames.ToList();
        var mods = _allMods.ToList();

        var paths = new List<string>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cards)
        {
            if (!string.IsNullOrEmpty(c.InstallPath)) paths.Add(c.InstallPath);
            if (!string.IsNullOrEmpty(c.DetectedGame?.InstallPath)) paths.Add(c.DetectedGame.InstallPath);
            names.Add(_gameDetectionService.NormalizeName(c.GameName));
        }
        foreach (var g in manual)
        {
            if (!string.IsNullOrEmpty(g.InstallPath)) paths.Add(g.InstallPath);
            names.Add(_gameDetectionService.NormalizeName(g.Name));
        }
        // Games taken out with "Remove from RHI" aren't offered again
        foreach (var key in _gameNameService.RemovedGames.ToList())
            names.Add(_gameDetectionService.NormalizeName(GameKey.Parse(key).Name));
        lock (_removedCardsLock)
            foreach (var rc in _removedCards.Values)
                if (!string.IsNullOrEmpty(rc.InstallPath)) paths.Add(rc.InstallPath);
        names.Remove("");

        // Exact (normalized) title lookup: wiki/db mod names first, then PCGW
        var modTitles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in mods)
        {
            if (string.IsNullOrWhiteSpace(m.Name) || m.IsGenericUnreal || m.IsGenericUnity) continue;
            modTitles.TryAdd(_gameDetectionService.NormalizeName(m.Name), m.Name);
        }
        var pcgw = _pcgwService;
        string? Canonical(string name)
        {
            var n = _gameDetectionService.NormalizeName(name);
            if (n.Length == 0) return null;
            if (modTitles.TryGetValue(n, out var t)) return t;
            try { return pcgw.FindCentralTitle(name); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MainViewModel.CustomFolders] PCGW title lookup failed for '{name}' — {ex.Message}");
                return null;
            }
        }

        return new CustomFolderScanRequest
        {
            Folders = folders,
            IgnoredFolders = _gameNameService.IgnoredCustomGameFolders,
            LibraryPaths = paths,
            KnownNames = names,
            CanonicalTitle = Canonical,
        };
    }

    private void SetPendingCustomGames(IReadOnlyList<CustomGameCandidate> list)
    {
        _pendingCustomGames = list;
        var dq = DispatcherQueue;
        if (dq == null) return;
        dq.TryEnqueue(() =>
        {
            try { PendingCustomGamesChanged?.Invoke(); }
            catch (Exception ex) { _crashReporter.Log($"[MainViewModel.PendingCustomGamesChanged] Handler failed — {ex.Message}"); }
        });
    }

    /// <summary>Takes candidates off the pending list (added, ignored or gone).</summary>
    public void DismissCustomGames(IEnumerable<CustomGameCandidate> done)
    {
        var drop = new HashSet<string>(done.Select(c => c.FolderPath), StringComparer.OrdinalIgnoreCase);
        SetPendingCustomGames(_pendingCustomGames.Where(c => !drop.Contains(c.FolderPath)).ToList());
    }

    /// <summary>Remembers the candidates' folders so they aren't offered again.</summary>
    public void IgnoreCustomGames(IReadOnlyCollection<CustomGameCandidate> candidates)
    {
        if (candidates.Count == 0) return;
        _gameNameService.AddIgnoredCustomGameFolders(candidates.Select(c => c.FolderPath));
        SaveNameMappings();
        _crashReporter.Log($"[MainViewModel.IgnoreCustomGames] Ignored {candidates.Count}: {string.Join(", ", candidates.Select(c => c.FolderPath))}");
        DismissCustomGames(candidates);
    }

    /// <summary>
    /// Adds a candidate as a manual game with <paramref name="exe"/> as its launch exe. UI thread.
    /// Returns an error message for the user, or null when it was added.
    /// </summary>
    public string? AddCustomGame(CustomGameCandidate candidate, string name, ScoredExe exe)
    {
        name = name.Trim();
        if (string.IsNullOrEmpty(name)) return "No name was given.";
        if (!File.Exists(exe.FullPath)) return $"{exe.RelativePath} no longer exists.";

        var norm = _gameDetectionService.NormalizeName(name);
        var existing = _allCards.FirstOrDefault(c => _gameDetectionService.NormalizeName(c.GameName) == norm);
        if (existing != null || _manualGames.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return $"\"{existing?.GameName ?? name}\" is already in your library.";

        // The card's install path comes from engine detection on the folder we pass. Pass the game
        // root when that lands next to the chosen exe (or the manifest has a path override relative to
        // the root); otherwise pass the exe's own folder so ReShade goes where the chosen exe runs.
        var exeDir = Path.GetDirectoryName(exe.FullPath) ?? candidate.FolderPath;
        var (detected, _) = _gameDetectionService.DetectEngineAndPath(candidate.FolderPath);
        bool rootIsRight = detected.TrimEnd('\\').Equals(exeDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                           || _installPathOverrides.ContainsKey(name)
                           || (candidate.Engine is EngineType.Unreal or EngineType.UnrealLegacy);
        var installRoot = rootIsRight ? candidate.FolderPath : exeDir;

        _crashReporter.Log($"[MainViewModel.AddCustomGame] '{name}' root='{candidate.FolderPath}' installRoot='{installRoot}' exe='{exe.FullPath}' (detected '{detected}')");

        // Launch exe first so anything reading it while the card is built sees it
        _gameNameService.LaunchExeOverrides[name] = exe.FullPath;
        SaveNameMappings();

        AddManualGame(new DetectedGame
        {
            Name = name,
            InstallPath = installRoot,
            Source = "Manual",
            IsManuallyAdded = true,
        });
        DismissCustomGames(new[] { candidate });
        return null;
    }

    /// <summary>
    /// When a manual game that lives in a custom folder is removed, remember its folder so the scan
    /// doesn't offer it again.
    /// </summary>
    private void IgnoreCustomFolderGameOnRemove(GameCardViewModel card)
    {
        var folders = _gameNameService.CustomGameFolders;
        if (folders.Count == 0) return;
        var path = !string.IsNullOrEmpty(card.DetectedGame?.InstallPath) ? card.DetectedGame.InstallPath : card.InstallPath;
        if (string.IsNullOrEmpty(path)) return;
        var root = CustomFolderScanService.FindCandidateRoot(path, folders);
        if (root == null) return;
        _gameNameService.AddIgnoredCustomGameFolders(new[] { root });
        SaveNameMappings();
        _crashReporter.Log($"[MainViewModel.IgnoreCustomFolderGameOnRemove] '{card.GameName}' removed — ignoring '{root}' in custom folder scans");
    }
}
