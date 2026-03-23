using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test verifying that SaveNameMappings never persists DC keys.
/// Feature: dc-removal, Property 5: Settings persistence excludes DC keys
/// **Validates: Requirements 4.5, 7.3**
/// </summary>
[Collection("SettingsFile")]
public class SettingsPersistenceNoDcKeysPropertyTests : IDisposable
{
    /// <summary>
    /// DC settings keys that must never appear in the persisted settings file
    /// after SaveNameMappings runs.
    /// </summary>
    private static readonly string[] DcKeys =
    [
        "DcModeEnabled",
        "DcDllFileName",
        "PerGameDcModeOverride",
        "DcCustomDllFileNames",
        "UpdateAllExcludedDc",
        "DcLegacyMode",
    ];

    private static readonly Gen<string> GenDcKey = Gen.Elements(DcKeys);

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UPST");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private readonly string? _originalContent;

    public SettingsPersistenceNoDcKeysPropertyTests()
    {
        // Back up existing settings file
        if (File.Exists(SettingsPath))
            _originalContent = File.ReadAllText(SettingsPath);
    }

    public void Dispose()
    {
        // Restore original settings file
        if (_originalContent != null)
            File.WriteAllText(SettingsPath, _originalContent);
        else if (File.Exists(SettingsPath))
            File.Delete(SettingsPath);
    }

    // ── Property 5: Settings persistence excludes DC keys ─────────────────────
    // Feature: dc-removal, Property 5: Settings persistence excludes DC keys
    // **Validates: Requirements 4.5, 7.3**
    [Property(MaxTest = 100)]
    public Property SaveNameMappings_Never_Persists_DC_Keys()
    {
        return Prop.ForAll(
            Arb.From(GenDcKey),
            (string dcKey) =>
            {
                // Arrange: seed the settings file with the DC key present
                Directory.CreateDirectory(SettingsDir);
                var seeded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [dcKey] = "test_value",
                    ["SkipUpdateCheck"] = "false",
                };

                // Retry file writes to handle transient file locks from prior iterations
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(seeded));
                        break;
                    }
                    catch (IOException) when (attempt < 2)
                    {
                        Thread.Sleep(50 * (attempt + 1));
                    }
                }

                // Build minimal service graph
                var gameDetection = new StubGameDetectionService();
                var installer = new StubModInstallService();
                var auxInstaller = new StubAuxInstallService();
                var lumaService = new StubLumaService();
                var dllOverride = new DllOverrideService(auxInstaller);
                var settingsVm = new SettingsViewModel();
                var gameNameService = new GameNameService(
                    gameDetection, installer, auxInstaller, lumaService);

                // Act: call SaveNameMappings (writes to the real settings path)
                gameNameService.SaveNameMappings(
                    dllOverride,
                    settingsVm,
                    isGridLayout: false,
                    isLoadingSettings: false,
                    filterMode: "All");

                // Assert: read back the persisted file and check the DC key is gone
                string json = "";
                for (int readAttempt = 0; readAttempt < 3; readAttempt++)
                {
                    try
                    {
                        json = File.ReadAllText(SettingsPath);
                        break;
                    }
                    catch (IOException) when (readAttempt < 2)
                    {
                        Thread.Sleep(50 * (readAttempt + 1));
                    }
                }
                var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                            ?? new();

                return (!saved.ContainsKey(dcKey))
                    .Label($"DC key '{dcKey}' still present in saved settings");
            });
    }

    // ── Minimal stubs (only what SaveNameMappings needs) ──────────────────────

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

    private class StubAuxInstallService : IAuxInstallService, IAuxFileService
    {
        public Task<AuxInstalledRecord> InstallReShadeAsync(string gameName, string installPath, string? shaderModeOverride = null, bool use32Bit = false, string? filenameOverride = null, IEnumerable<string>? selectedPackIds = null, IProgress<(string, double)>? progress = null) => Task.FromResult(new AuxInstalledRecord());
        public Task<bool> CheckForUpdateAsync(AuxInstalledRecord record) => Task.FromResult(false);
        public void Uninstall(AuxInstalledRecord record) { }
        public void UninstallDllOnly(AuxInstalledRecord record) { }
        public List<AuxInstalledRecord> LoadAll() => new();
        public AuxInstalledRecord? FindRecord(string gameName, string installPath, string addonType) => null;
        public void SaveAuxRecord(AuxInstalledRecord record) { }
        public void RemoveRecord(AuxInstalledRecord record) { }
        public bool EnsureReShadeStaging() => false;
        public AuxInstallService.DxgiFileType IdentifyDxgiFile(string filePath) => AuxInstallService.DxgiFileType.Unknown;
        public bool BackupForeignDll(string dllPath) => false;
        public void RestoreForeignDll(string dllPath) { }
        public bool IsReShadeFileStrict(string filePath) => false;
        public bool IsReShadeFile(string filePath) => false;
        public void EnsureInisDir() { }
        public void MergeRsIni(string gameDir) { }
        public void MergeRsVulkanIni(string gameDir) { }
        public void CopyRsIni(string gameDir) { }
        public void CopyRsPresetIniIfPresent(string gameDir) { }
        public string? ReadInstalledVersion(string installPath, string fileName) => null;
        public bool CheckReShadeUpdateLocal(AuxInstalledRecord record) => false;
    }

    private class StubLumaService : ILumaService
    {
        public Task<List<LumaMod>> FetchCompletedModsAsync(IProgress<string>? progress = null) => Task.FromResult(new List<LumaMod>());
        public Task<LumaInstalledRecord> InstallAsync(LumaMod mod, string gameInstallPath, IProgress<(string, double)>? progress = null) => Task.FromResult(new LumaInstalledRecord());
        public void Uninstall(LumaInstalledRecord record) { }
        public void SaveLumaRecord(LumaInstalledRecord record) { }
        public void RemoveLumaRecord(string gameName, string installPath) { }
    }
}
