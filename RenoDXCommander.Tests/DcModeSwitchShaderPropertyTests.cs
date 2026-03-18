using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property tests for DC mode switch shader transitions.
/// Tests verify that shader operations (SyncGameFolder, RemoveFromGameFolder,
/// RestoreOriginalIfPresent) are correctly triggered based on effective DC mode
/// level transitions during per-game mode switches.
///
/// Feature: dc-mode-shader-deployment
/// </summary>
public class DcModeSwitchShaderPropertyTests : IDisposable
{
    private readonly string _tempRoot;

    public DcModeSwitchShaderPropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcSwitchProp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── Property 3: Mode switch 0→non-zero deploys shaders only when DC installed ──

    /// <summary>
    /// Property 3: Mode switch 0→non-zero deploys shaders only when DC is installed.
    ///
    /// For any game card where the effective DC mode level transitions from 0 to a
    /// non-zero value, <c>SyncGameFolder</c> SHALL be called if and only if DC is
    /// installed for that game. If DC is not installed, no shader operations SHALL occur.
    ///
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property ModeSwitchZeroToNonZero_DeploysShaders_OnlyWhenDcInstalled()
    {
        // Generate: new per-game DC mode (1 or 2), whether DC is installed
        var genNewPerGame = Gen.Elements(1, 2);
        var genDcInstalled = Arb.From<bool>().Generator;
        var genGlobal = Gen.Elements(0, 1, 2);

        var genConfig = from newPerGame in genNewPerGame
                        from dcInstalled in genDcInstalled
                        from globalLevel in genGlobal
                        select (newPerGame, dcInstalled, globalLevel);

        return Prop.ForAll(
            Arb.From(genConfig),
            config =>
            {
                var (newPerGame, dcInstalled, globalLevel) = config;

                // Previous per-game override is 0 (or null with global=0) to ensure
                // previous effective level is 0. Use explicit 0 per-game override
                // so the card is NOT affected by Vulkan/DllOverride/Luma flags
                // (those force effective to 0 regardless, preventing a real transition).
                int? previousPerGameDcMode = 0;

                var tracker = new TrackingShaderPackService();
                var vm = CreateViewModelWithTracker(tracker);
                vm.DcModeLevel = globalLevel;

                var card = CreateCard(
                    "TestGame",
                    perGameDcMode: newPerGame,  // new value: 1 or 2
                    dcInstalled: dcInstalled);

                InjectCards(vm, new List<GameCardViewModel> { card });

                // Act: switch from previous=0 to new=1 or 2
                vm.ApplyDcModeSwitchForCard("TestGame", previousPerGameDcMode);

                // Assert: SyncGameFolder called iff DC is installed
                var syncCalled = tracker.SyncGameFolderCalled;
                var expectSync = dcInstalled;

                return (syncCalled == expectSync)
                    .Label($"NewPerGame={newPerGame}, DcInstalled={dcInstalled}, Global={globalLevel} → " +
                           $"SyncGameFolderCalled={syncCalled} (expected {expectSync})");
            });
    }

    // ── Property 4: Mode switch non-zero→0 removes shaders ─────────────────

    /// <summary>
    /// Property 4: Mode switch non-zero→0 removes shaders.
    ///
    /// For any game card where the effective DC mode level transitions from a
    /// non-zero value to 0, <c>RemoveFromGameFolder</c> and
    /// <c>RestoreOriginalIfPresent</c> SHALL be called for that game.
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property ModeSwitchNonZeroToZero_RemovesShaders()
    {
        // Generate: previous per-game DC mode (1 or 2), global level (any)
        var genPreviousPerGame = Gen.Elements(1, 2);
        var genGlobal = Gen.Elements(0, 1, 2);

        var genConfig = from previousPerGame in genPreviousPerGame
                        from globalLevel in genGlobal
                        select (previousPerGame, globalLevel);

        return Prop.ForAll(
            Arb.From(genConfig),
            config =>
            {
                var (previousPerGame, globalLevel) = config;

                // New per-game override is 0 → effective level becomes 0
                int? previousPerGameDcMode = previousPerGame;

                var tracker = new TrackingShaderPackService();
                var vm = CreateViewModelWithTracker(tracker);
                vm.DcModeLevel = globalLevel;

                // Card with DC installed, per-game mode set to 0 (the new value),
                // no flags that force effective to 0 (DllOverride, Luma, Vulkan)
                var card = CreateCard(
                    "TestGame",
                    perGameDcMode: 0,       // new value: 0
                    dcInstalled: true,
                    dllOverrideEnabled: false,
                    isLumaMode: false,
                    isVulkan: false);

                InjectCards(vm, new List<GameCardViewModel> { card });

                // Act: switch from previous=1 or 2 to new=0
                vm.ApplyDcModeSwitchForCard("TestGame", previousPerGameDcMode);

                // Assert: both RemoveFromGameFolder and RestoreOriginalIfPresent called
                var removeCalled = tracker.RemoveFromGameFolderCalled;
                var restoreCalled = tracker.RestoreOriginalIfPresentCalled;

                return (removeCalled && restoreCalled)
                    .Label($"PreviousPerGame={previousPerGame}, Global={globalLevel} → " +
                           $"RemoveCalled={removeCalled}, RestoreCalled={restoreCalled} " +
                           $"(expected both true)");
            });
    }

    // ── Property 5: Mode switch within non-zero levels does not touch shaders ──

    /// <summary>
    /// Property 5: Mode switch within non-zero levels does not touch shaders.
    ///
    /// For any game card where the effective DC mode level transitions from one
    /// non-zero value to another non-zero value (1→2 or 2→1), no shader deployment
    /// or removal methods SHALL be called (no SyncGameFolder, RemoveFromGameFolder,
    /// RestoreOriginalIfPresent).
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property ModeSwitchWithinNonZeroLevels_DoesNotTouchShaders()
    {
        // Generate transitions: 1→2 or 2→1
        var genTransition = Gen.Elements((1, 2), (2, 1));
        var genGlobal = Gen.Elements(0, 1, 2);

        var genConfig = from transition in genTransition
                        from globalLevel in genGlobal
                        select (transition.Item1, transition.Item2, globalLevel);

        return Prop.ForAll(
            Arb.From(genConfig),
            config =>
            {
                var (previousPerGame, newPerGame, globalLevel) = config;

                var tracker = new TrackingShaderPackService();
                var vm = CreateViewModelWithTracker(tracker);
                vm.DcModeLevel = globalLevel;

                // Card with DC installed, new per-game mode set, no flags that force effective to 0
                var card = CreateCard(
                    "TestGame",
                    perGameDcMode: newPerGame,
                    dcInstalled: true,
                    dllOverrideEnabled: false,
                    isLumaMode: false,
                    isVulkan: false);

                InjectCards(vm, new List<GameCardViewModel> { card });

                // Act: switch from previous non-zero to new non-zero
                vm.ApplyDcModeSwitchForCard("TestGame", previousPerGame);

                // Assert: no shader methods called
                var syncCalled = tracker.SyncGameFolderCalled;
                var removeCalled = tracker.RemoveFromGameFolderCalled;
                var restoreCalled = tracker.RestoreOriginalIfPresentCalled;

                return (!syncCalled && !removeCalled && !restoreCalled)
                    .Label($"Previous={previousPerGame}, New={newPerGame}, Global={globalLevel} → " +
                           $"SyncCalled={syncCalled}, RemoveCalled={removeCalled}, RestoreCalled={restoreCalled} " +
                           $"(expected all false)");
            });
    }

    // ── Property 6: Effective DC mode level computation ────────────────────────

    /// <summary>
    /// Property 6: Effective DC mode level computation.
    ///
    /// For any game card configuration (DllOverrideEnabled, IsLumaMode,
    /// RequiresVulkanInstall, PerGameDcMode, global DcModeLevel), the effective
    /// DC mode level SHALL be:
    /// - 0 if DllOverrideEnabled is true
    /// - 0 if IsLumaMode is true
    /// - 0 if RequiresVulkanInstall is true and PerGameDcMode is null
    /// - PerGameDcMode if PerGameDcMode has a value
    /// - global DcModeLevel otherwise
    ///
    /// Tested indirectly: a transition from effective=0 to the computed effective
    /// level is set up. If the computed level is > 0 (and DC is installed),
    /// SyncGameFolder must be called. If 0, no shader ops occur.
    ///
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property EffectiveDcModeLevelComputation_MatchesResolutionRules()
    {
        var genDllOverride = Arb.From<bool>().Generator;
        var genLumaMode = Arb.From<bool>().Generator;
        var genVulkan = Arb.From<bool>().Generator;
        var genPerGame = Gen.OneOf(
            Gen.Constant<int?>(null),
            Gen.Elements<int?>(0, 1, 2));
        var genGlobal = Gen.Elements(0, 1, 2);

        var genConfig = from dllOverride in genDllOverride
                        from lumaMode in genLumaMode
                        from vulkan in genVulkan
                        from perGame in genPerGame
                        from global_ in genGlobal
                        select (dllOverride, lumaMode, vulkan, perGame, global_);

        return Prop.ForAll(
            Arb.From(genConfig),
            config =>
            {
                var (dllOverride, lumaMode, vulkan, perGame, globalLevel) = config;

                // Compute expected effective level per the resolution rules
                int expectedEffective;
                if (dllOverride)
                    expectedEffective = 0;
                else if (lumaMode)
                    expectedEffective = 0;
                else if (vulkan && !perGame.HasValue)
                    expectedEffective = 0;
                else
                    expectedEffective = perGame ?? globalLevel;

                // Test indirectly via ApplyDcModeSwitchForCard:
                // Set up a transition from previousPerGame=0 (effective=0 when no
                // blocking flags) to the card's current configuration.
                // If expectedEffective > 0, we expect SyncGameFolder to be called
                // (DC is installed). If expectedEffective == 0, no shader ops.
                var tracker = new TrackingShaderPackService();
                var vm = CreateViewModelWithTracker(tracker);
                vm.DcModeLevel = globalLevel;

                var card = CreateCard(
                    "TestGame",
                    perGameDcMode: perGame,
                    dcInstalled: true,
                    dllOverrideEnabled: dllOverride,
                    isLumaMode: lumaMode,
                    isVulkan: vulkan);

                InjectCards(vm, new List<GameCardViewModel> { card });

                // Previous per-game was 0 → previous effective was 0
                // (because ComputeEffectiveLevel uses the CARD's current
                // DllOverride/Luma/Vulkan flags with the previous per-game value,
                // the previous effective may not be 0 if flags force it to 0.
                // Actually, if flags force effective to 0, then both previous and
                // new effective are 0, so no transition occurs — which is correct.)
                //
                // Compute what the previous effective would be with perGame=0:
                int previousEffective;
                if (dllOverride)
                    previousEffective = 0;
                else if (lumaMode)
                    previousEffective = 0;
                else if (vulkan)
                    previousEffective = 0; // perGame=0 has value, so Vulkan check passes, but value is 0
                else
                    previousEffective = 0; // perGame=0

                vm.ApplyDcModeSwitchForCard("TestGame", 0);

                // Determine expected behavior based on transition
                bool expectSync;
                if (previousEffective == 0 && expectedEffective > 0)
                    expectSync = true;  // 0→non-zero with DC installed → deploy
                else
                    expectSync = false; // no transition or both zero → no deploy

                var syncCalled = tracker.SyncGameFolderCalled;
                var removeCalled = tracker.RemoveFromGameFolderCalled;

                return (syncCalled == expectSync && !removeCalled)
                    .Label($"DllOverride={dllOverride}, Luma={lumaMode}, Vulkan={vulkan}, " +
                           $"PerGame={perGame}, Global={globalLevel} → " +
                           $"ExpectedEffective={expectedEffective}, PreviousEffective={previousEffective}, " +
                           $"SyncCalled={syncCalled} (expected {expectSync}), " +
                           $"RemoveCalled={removeCalled} (expected false)");
            });
    }

    // ── Property 7: Global mode switch only affects games with actual transitions ──

    /// <summary>
    /// Property 7: Global mode switch only affects games with actual transitions.
    ///
    /// For any set of game cards and any global DC mode level change, shader operations
    /// (SyncGameFolder, RemoveFromGameFolder) SHALL only be invoked for cards whose
    /// effective DC mode level actually changes between 0 and non-zero. Cards with
    /// per-game overrides, DLL overrides, Luma mode, or Vulkan defaults that prevent
    /// a transition SHALL not have shader operations invoked.
    ///
    /// **Validates: Requirements 5.3, 5.4**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property GlobalModeSwitch_OnlyAffectsGamesWithActualTransitions()
    {
        // Generate previous and new global levels that differ
        var genPreviousGlobal = Gen.Elements(0, 1, 2);
        var genNewGlobal = Gen.Elements(0, 1, 2);

        // Generate 1-5 card configs with mixed flags
        var genCardConfig = from dllOverride in Arb.From<bool>().Generator
                            from lumaMode in Arb.From<bool>().Generator
                            from vulkan in Arb.From<bool>().Generator
                            from perGame in Gen.OneOf(
                                Gen.Constant<int?>(null),
                                Gen.Elements<int?>(0, 1, 2))
                            from dcInstalled in Arb.From<bool>().Generator
                            select (dllOverride, lumaMode, vulkan, perGame, dcInstalled);

        var genCards = Gen.ListOf(Gen.Choose(1, 5).SelectMany(n => Gen.ListOf(n, genCardConfig)))
            .Select(outer => outer.SelectMany(x => x).ToList());

        // Simpler: generate a list of 1-5 card configs
        var genCardList = Gen.Choose(1, 5).SelectMany(count =>
            Gen.ListOf(count, genCardConfig).Select(list => list.ToList()));

        var genAll = from previousGlobal in genPreviousGlobal
                     from newGlobal in genNewGlobal
                     from cards in genCardList
                     select (previousGlobal, newGlobal, cards);

        return Prop.ForAll(
            Arb.From(genAll),
            config =>
            {
                var (previousGlobal, newGlobal, cardConfigs) = config;

                var tracker = new TrackingShaderPackService();
                var vm = CreateViewModelWithTracker(tracker);
                vm.DcModeLevel = newGlobal; // Set the new global level

                var cards = new List<GameCardViewModel>();
                var expectedSyncDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var expectedRemoveDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < cardConfigs.Count; i++)
                {
                    var (dllOverride, lumaMode, vulkan, perGame, dcInstalled) = cardConfigs[i];
                    var cardName = $"Game_{i}";

                    var card = CreateCard(
                        cardName,
                        perGameDcMode: perGame,
                        dcInstalled: dcInstalled,
                        dllOverrideEnabled: dllOverride,
                        isLumaMode: lumaMode,
                        isVulkan: vulkan);

                    cards.Add(card);

                    // Compute expected effective levels using the same logic as ComputeEffectiveLevel
                    int previousEffective = ComputeExpectedEffective(dllOverride, lumaMode, vulkan, perGame, previousGlobal);
                    int newEffective = ComputeExpectedEffective(dllOverride, lumaMode, vulkan, perGame, newGlobal);

                    // Determine expected shader operations
                    if (previousEffective == 0 && newEffective > 0 && dcInstalled)
                        expectedSyncDirs.Add(card.InstallPath!);
                    else if (previousEffective > 0 && newEffective == 0)
                        expectedRemoveDirs.Add(card.InstallPath!);
                }

                InjectCards(vm, cards);

                // Act
                vm.ApplyDcModeSwitch(previousGlobal);

                // Assert: shader ops only on expected directories
                var actualSyncDirs = new HashSet<string>(tracker.SyncGameFolderDirs, StringComparer.OrdinalIgnoreCase);
                var actualRemoveDirs = new HashSet<string>(tracker.RemoveFromGameFolderDirs, StringComparer.OrdinalIgnoreCase);

                var syncMatch = actualSyncDirs.SetEquals(expectedSyncDirs);
                var removeMatch = actualRemoveDirs.SetEquals(expectedRemoveDirs);

                return (syncMatch && removeMatch)
                    .Label($"PrevGlobal={previousGlobal}, NewGlobal={newGlobal}, Cards={cardConfigs.Count} → " +
                           $"SyncDirs: expected={expectedSyncDirs.Count} actual={actualSyncDirs.Count} match={syncMatch}, " +
                           $"RemoveDirs: expected={expectedRemoveDirs.Count} actual={actualRemoveDirs.Count} match={removeMatch}");
            });
    }

    /// <summary>
    /// Mirrors the ComputeEffectiveLevel logic from MainViewModel for test-side computation.
    /// </summary>
    private static int ComputeExpectedEffective(bool dllOverride, bool lumaMode, bool vulkan, int? perGame, int globalLevel)
    {
        if (dllOverride) return 0;
        if (lumaMode) return 0;
        if (vulkan && !perGame.HasValue) return 0;
        return perGame ?? globalLevel;
    }

    // ── Helper: Create MainViewModel with tracking shader service ─────────────

    private MainViewModel CreateViewModelWithTracker(TrackingShaderPackService tracker)
    {
        var auxInstaller = new StubAuxInstallService();
        var gameDetection = new StubGameDetectionService();
        var installer = new StubModInstallService();
        var lumaService = new StubLumaService();
        var settingsVm = new SettingsViewModel();
        var filterVm = new FilterViewModel();
        var updateOrch = new UpdateOrchestrationService(installer, auxInstaller);
        var dllOverride = new DllOverrideService(auxInstaller);
        var gameName = new GameNameService(gameDetection, installer, auxInstaller, lumaService);
        var rsUpdate = new StubReShadeUpdateService();
        var gameInit = new GameInitializationService(
            gameDetection, new StubWikiService(), new StubManifestService(),
            installer, auxInstaller, new StubGameLibraryService(),
            new StubPeHeaderService(), lumaService, rsUpdate, tracker);

        return new MainViewModel(
            new HttpClient(),
            installer,
            auxInstaller,
            new StubWikiService(),
            new StubManifestService(),
            new StubGameLibraryService(),
            gameDetection,
            new StubPeHeaderService(),
            new StubUpdateService(),
            tracker,
            lumaService,
            rsUpdate,
            settingsVm,
            filterVm,
            updateOrch,
            dllOverride,
            gameName,
            gameInit);
    }

    private static void InjectCards(MainViewModel vm, List<GameCardViewModel> cards)
    {
        var field = typeof(MainViewModel).GetField("_allCards", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(vm, cards);
    }

    private GameCardViewModel CreateCard(
        string name,
        int? perGameDcMode = null,
        bool dcInstalled = true,
        bool dllOverrideEnabled = false,
        bool isLumaMode = false,
        bool isVulkan = false)
    {
        var dir = Path.Combine(_tempRoot, name + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        var card = new GameCardViewModel
        {
            GameName = name,
            InstallPath = dir,
            DllOverrideEnabled = dllOverrideEnabled,
            IsLumaMode = isLumaMode,
            PerGameDcMode = perGameDcMode,
        };

        if (isVulkan)
        {
            card.GraphicsApi = GraphicsApiType.Vulkan;
            card.IsDualApiGame = false;
        }

        if (dcInstalled)
        {
            var dcFile = AuxInstallService.DcNormalName;
            File.WriteAllBytes(Path.Combine(dir, dcFile), new byte[] { 0x00 });
            card.DcStatus = GameStatus.Installed;
            card.DcRecord = new AuxInstalledRecord
            {
                GameName = name,
                InstallPath = dir,
                InstalledAs = dcFile,
                AddonType = "DisplayCommander"
            };
        }
        else
        {
            card.DcStatus = GameStatus.NotInstalled;
            card.DcRecord = null;
        }

        return card;
    }

    // ── Tracking IShaderPackService ───────────────────────────────────────────

    private class TrackingShaderPackService : IShaderPackService
    {
        public bool SyncGameFolderCalled { get; private set; }
        public string? SyncGameFolderDir { get; private set; }
        public bool RemoveFromGameFolderCalled { get; private set; }
        public string? RemoveFromGameFolderDir { get; private set; }
        public bool RestoreOriginalIfPresentCalled { get; private set; }
        public bool SyncDcFolderCalled { get; private set; }
        public bool DeployToDcFolderCalled { get; private set; }

        /// <summary>Records all directories passed to SyncGameFolder (for multi-card tracking).</summary>
        public List<string> SyncGameFolderDirs { get; } = new();
        /// <summary>Records all directories passed to RemoveFromGameFolder (for multi-card tracking).</summary>
        public List<string> RemoveFromGameFolderDirs { get; } = new();
        /// <summary>Records all directories passed to RestoreOriginalIfPresent (for multi-card tracking).</summary>
        public List<string> RestoreOriginalIfPresentDirs { get; } = new();

        public IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> AvailablePacks { get; } =
            new List<(string, string, ShaderPackService.PackCategory)>();

        public string? GetPackDescription(string packId) => null;
        public Task EnsureLatestAsync(IProgress<string>? progress = null) => Task.CompletedTask;
        public void DeployToDcFolder() => DeployToDcFolderCalled = true;
        public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null) { }

        public void RemoveFromGameFolder(string gameDir)
        {
            RemoveFromGameFolderCalled = true;
            RemoveFromGameFolderDir = gameDir;
            RemoveFromGameFolderDirs.Add(gameDir);
        }

        public bool IsManagedByRdxc(string gameDir) => false;

        public void RestoreOriginalIfPresent(string gameDir)
        {
            RestoreOriginalIfPresentCalled = true;
            RestoreOriginalIfPresentDirs.Add(gameDir);
        }

        public void SyncDcFolder(IEnumerable<string>? selectedPackIds = null)
            => SyncDcFolderCalled = true;

        public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null)
        {
            SyncGameFolderCalled = true;
            SyncGameFolderDir = gameDir;
            SyncGameFolderDirs.Add(gameDir);
        }

        public void SyncShadersToAllLocations(
            IEnumerable<(string installPath, bool dcInstalled, bool rsInstalled, bool dcMode, string? shaderModeOverride)> locations,
            IEnumerable<string>? selectedPackIds = null) { }
    }

    // ── Minimal stubs ────────────────────────────────────────────────────────

    private class StubModInstallService : IModInstallService
    {
        public event Action<InstalledModRecord>? InstallCompleted;
        public Task<InstalledModRecord> InstallAsync(GameMod mod, string gameInstallPath, IProgress<(string, double)>? progress = null, string? gameName = null) => Task.FromResult(new InstalledModRecord());
        public Task<bool> CheckForUpdateAsync(InstalledModRecord record) => Task.FromResult(false);
        public void Uninstall(InstalledModRecord record) { }
        public List<InstalledModRecord> LoadAll() => new();
        public InstalledModRecord? FindRecord(string gameName, string? installPath = null) => null;
        public void SaveRecordPublic(InstalledModRecord record) { }
        public void RemoveRecord(InstalledModRecord record) { }
    }

    private class StubAuxInstallService : IAuxInstallService
    {
        public Task<AuxInstalledRecord> InstallDcAsync(string gameName, string installPath, int dcModeLevel, AuxInstalledRecord? existingDcRecord = null, AuxInstalledRecord? existingRsRecord = null, string? shaderModeOverride = null, bool use32Bit = false, string? filenameOverride = null, IEnumerable<string>? selectedPackIds = null, IProgress<(string, double)>? progress = null) => Task.FromResult(new AuxInstalledRecord());
        public Task<AuxInstalledRecord> InstallReShadeAsync(string gameName, string installPath, bool dcMode, bool dcIsInstalled = false, string? shaderModeOverride = null, bool use32Bit = false, string? filenameOverride = null, IEnumerable<string>? selectedPackIds = null, IProgress<(string, double)>? progress = null) => Task.FromResult(new AuxInstalledRecord());
        public Task<bool> CheckForUpdateAsync(AuxInstalledRecord record) => Task.FromResult(false);
        public void Uninstall(AuxInstalledRecord record) { }
        public void UninstallDllOnly(AuxInstalledRecord record) { }
        public List<AuxInstalledRecord> LoadAll() => new();
        public AuxInstalledRecord? FindRecord(string gameName, string installPath, string addonType) => null;
        public void SaveAuxRecord(AuxInstalledRecord record) { }
        public void RemoveRecord(AuxInstalledRecord record) { }
    }

    private class StubWikiService : IWikiService
    {
        public Task<(List<GameMod> Mods, Dictionary<string, string> GenericNotes)> FetchAllAsync(IProgress<string>? progress = null) => Task.FromResult((new List<GameMod>(), new Dictionary<string, string>()));
        public Task<DateTime?> GetSnapshotLastModifiedAsync(string url) => Task.FromResult<DateTime?>(null);
    }

    private class StubManifestService : IManifestService
    {
        public Task<RemoteManifest?> FetchAsync() => Task.FromResult<RemoteManifest?>(null);
        public RemoteManifest? LoadCached() => null;
    }

    private class StubGameLibraryService : IGameLibraryService
    {
        public SavedGameLibrary? Load() => null;
        public void Save(List<DetectedGame> games, Dictionary<string, bool> addonCache, HashSet<string> hiddenGames, HashSet<string> favouriteGames, List<DetectedGame> manualGames, Dictionary<string, string>? engineTypeCache = null, Dictionary<string, string>? resolvedPathCache = null, Dictionary<string, string>? addonFileCache = null, Dictionary<string, MachineType>? bitnessCache = null) { }
        public List<DetectedGame> ToDetectedGames(SavedGameLibrary lib) => new();
        public List<DetectedGame> ToManualGames(SavedGameLibrary lib) => new();
    }

    private class StubGameDetectionService : IGameDetectionService
    {
        public List<DetectedGame> FindSteamGames() => new();
        public List<DetectedGame> FindGogGames() => new();
        public List<DetectedGame> FindEpicGames() => new();
        public List<DetectedGame> FindEaGames() => new();
        public List<DetectedGame> FindXboxGames() => new();
        public List<DetectedGame> FindUbisoftGames() => new();
        public List<DetectedGame> FindBattleNetGames() => new();
        public List<DetectedGame> FindRockstarGames() => new();
        public (string installPath, EngineType engine) DetectEngineAndPath(string rootPath) => (rootPath, EngineType.Unknown);
        public GameMod? MatchGame(DetectedGame game, IEnumerable<GameMod> mods, Dictionary<string, string>? nameMappings = null) => null;
        public string NormalizeName(string name) => name.ToLowerInvariant();
    }

    private class StubPeHeaderService : IPeHeaderService
    {
        public MachineType DetectArchitecture(string exePath) => MachineType.Native;
        public string? FindGameExe(string installPath) => null;
        public MachineType DetectGameArchitecture(string installPath) => MachineType.Native;
    }

    private class StubUpdateService : IUpdateService
    {
        public Version CurrentVersion => new(1, 0, 0);
        public Task<UpdateInfo?> CheckForUpdateAsync(bool betaOptIn = false) => Task.FromResult<UpdateInfo?>(null);
        public Task<string?> DownloadInstallerAsync(string downloadUrl, IProgress<(string, double)>? progress = null) => Task.FromResult<string?>(null);
        public void LaunchInstallerAndExit(string installerPath, Action closeApp) { }
    }

    private class StubLumaService : ILumaService
    {
        public Task<List<LumaMod>> FetchCompletedModsAsync(IProgress<string>? progress = null) => Task.FromResult(new List<LumaMod>());
        public Task<LumaInstalledRecord> InstallAsync(LumaMod mod, string gameInstallPath, IProgress<(string, double)>? progress = null) => Task.FromResult(new LumaInstalledRecord());
        public void Uninstall(LumaInstalledRecord record) { }
        public void SaveLumaRecord(LumaInstalledRecord record) { }
        public void RemoveLumaRecord(string gameName, string installPath) { }
    }

    private class StubReShadeUpdateService : IReShadeUpdateService
    {
        public Task<(string version, string url)?> CheckLatestVersionAsync() => Task.FromResult<(string, string)?>(null);
        public Task<bool> EnsureLatestAsync(IProgress<(string, double)>? progress = null) => Task.FromResult(false);
    }
}
