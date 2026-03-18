using System.Reflection;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property tests for DC uninstall shader removal.
/// Verifies that <c>UninstallDc</c> calls <c>RemoveFromGameFolder</c>
/// if and only if the computed effective DC mode level is greater than 0.
///
/// **Validates: Requirements 2.1, 2.3, 5.2**
/// </summary>
public class DcUninstallShaderPropertyTests : IDisposable
{
    private readonly string _tempRoot;

    public DcUninstallShaderPropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcUninstProp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    /// <summary>
    /// Property 2: DC Uninstall removes shaders iff effective level &gt; 0.
    ///
    /// For any game card with DC installed, calling <c>UninstallDc</c> SHALL call
    /// <c>RemoveFromGameFolder</c> if and only if the computed effective DC mode level &gt; 0.
    /// When the effective level is 0, <c>RemoveFromGameFolder</c> SHALL NOT be called.
    ///
    /// **Validates: Requirements 2.1, 2.3, 5.2**
    /// </summary>
    [Property(MaxTest = 10)]
    public Property UninstallDc_RemovesShaders_Iff_EffectiveLevel_GreaterThanZero()
    {
        var genDllOverride = Arb.From<bool>().Generator;
        var genLumaMode = Arb.From<bool>().Generator;
        var genVulkan = Arb.From<bool>().Generator;
        var genPerGame = Gen.Elements<int?>(null, 0, 1, 2);
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

                // Compute expected effective level using the same logic as UninstallDc
                var expectedEffective = dllOverride ? 0
                    : lumaMode ? 0
                    : (vulkan && !perGame.HasValue) ? 0
                    : perGame ?? globalLevel;

                var tracker = new TrackingShaderPackService();
                var vm = CreateViewModelWithTracker(tracker);
                vm.DcModeLevel = globalLevel;

                var card = CreateCardWithDcRecord(
                    "TestGame",
                    dllOverrideEnabled: dllOverride,
                    isLumaMode: lumaMode,
                    isVulkan: vulkan,
                    perGameDcMode: perGame);

                // Inject card into ViewModel
                InjectCards(vm, new List<GameCardViewModel> { card });

                // Act
                vm.UninstallDc(card);

                // Assert
                var expectRemove = expectedEffective > 0;
                var removeWasCalled = tracker.RemoveFromGameFolderCalled;

                return (removeWasCalled == expectRemove)
                    .Label($"DllOverride={dllOverride}, Luma={lumaMode}, Vulkan={vulkan}, " +
                           $"PerGame={perGame?.ToString() ?? "null"}, Global={globalLevel} → " +
                           $"effective={expectedEffective}: " +
                           $"RemoveFromGameFolderCalled={removeWasCalled} (expected {expectRemove})");
            });
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

    private GameCardViewModel CreateCardWithDcRecord(
        string name,
        bool dllOverrideEnabled = false,
        bool isLumaMode = false,
        bool isVulkan = false,
        int? perGameDcMode = null)
    {
        var dir = Path.Combine(_tempRoot, name + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);

        // Create a fake DC DLL so uninstall logic doesn't crash
        var dcFile = AuxInstallService.DcNormalName;
        File.WriteAllBytes(Path.Combine(dir, dcFile), new byte[] { 0x00 });

        var card = new GameCardViewModel
        {
            GameName = name,
            InstallPath = dir,
            DcStatus = GameStatus.Installed,
            DllOverrideEnabled = dllOverrideEnabled,
            IsLumaMode = isLumaMode,
            PerGameDcMode = perGameDcMode,
        };

        // Set Vulkan state via GraphicsApi
        if (isVulkan)
        {
            card.GraphicsApi = GraphicsApiType.Vulkan;
            card.IsDualApiGame = false; // pure Vulkan → IsVulkanOnly = true → RequiresVulkanInstall = true
        }

        // Attach a DcRecord so UninstallDc proceeds past the null check
        card.DcRecord = new AuxInstalledRecord
        {
            GameName = name,
            InstallPath = dir,
            InstalledAs = dcFile,
            AddonType = "DisplayCommander"
        };

        return card;
    }

    // ── Tracking IShaderPackService ───────────────────────────────────────────

    private class TrackingShaderPackService : IShaderPackService
    {
        public bool RemoveFromGameFolderCalled { get; private set; }
        public string? RemoveFromGameFolderDir { get; private set; }
        public bool SyncDcFolderCalled { get; private set; }
        public bool SyncGameFolderCalled { get; private set; }
        public bool RestoreOriginalIfPresentCalled { get; private set; }
        public bool DeployToDcFolderCalled { get; private set; }

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
        }

        public bool IsManagedByRdxc(string gameDir) => false;
        public void RestoreOriginalIfPresent(string gameDir) => RestoreOriginalIfPresentCalled = true;

        public void SyncDcFolder(IEnumerable<string>? selectedPackIds = null)
            => SyncDcFolderCalled = true;

        public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null)
            => SyncGameFolderCalled = true;

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
