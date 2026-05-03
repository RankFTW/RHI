using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DxvkService.ResolveDeploymentPaths.
/// Feature: dxvk-integration, Property 4: OptiScaler coexistence routing
/// **Validates: Requirements 6.1, 6.5, 6.6, 6.7**
/// </summary>
public class DxvkOptiScalerCoexistenceRoutingPropertyTests
{
    // ── Known DXVK DLL names ──────────────────────────────────────────────────────

    private static readonly string[] KnownDxvkDlls =
        { "d3d8.dll", "d3d9.dll", "d3d10core.dll", "d3d11.dll", "dxgi.dll" };

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty distinct subset of known DXVK DLL names.
    /// </summary>
    private static Gen<List<string>> GenDllNameList =>
        from count in Gen.Choose(1, KnownDxvkDlls.Length)
        from indices in Gen.ArrayOf(count, Gen.Choose(0, KnownDxvkDlls.Length - 1))
        let distinct = indices.Distinct().Select(i => KnownDxvkDlls[i]).ToList()
        where distinct.Count > 0
        select distinct;

    /// <summary>
    /// Generates an optional OptiScaler installed filename: either null (not installed)
    /// or one of the known DXVK DLL names (to simulate a filename conflict scenario).
    /// </summary>
    private static Gen<string?> GenOptiScalerFilename =>
        Gen.Frequency(
            Tuple.Create(3, Gen.Elements(KnownDxvkDlls).Select<string, string?>(s => s)),
            Tuple.Create(2, Gen.Constant<string?>(null)));

    // ── Configurable OptiScaler stub ──────────────────────────────────────────────

    /// <summary>
    /// A minimal IOptiScalerService implementation whose DetectInstallation returns
    /// a configurable filename (or null when OptiScaler is not installed).
    /// All other members are no-op stubs.
    /// </summary>
    private class ConfigurableOptiScalerStub : IOptiScalerService
    {
        private readonly string? _installedFilename;

        public ConfigurableOptiScalerStub(string? installedFilename)
            => _installedFilename = installedFilename;

        public string? DetectInstallation(string installPath) => _installedFilename;

        // ── Stub members (not exercised by ResolveDeploymentPaths) ────────────
        public bool IsStagingReady => false;
        public bool HasUpdate => false;
        public string? StagedVersion => null;
        public bool FirstTimeWarningAcknowledged { get; set; }
        public Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null) => Task.CompletedTask;
        public Task CheckForUpdateAsync() => Task.CompletedTask;
        public void ClearStaging() { }
        public Task EnsureDlssStagingAsync(IProgress<(string message, double percent)>? progress = null) => Task.CompletedTask;
        public Task<AuxInstalledRecord?> InstallAsync(GameCardViewModel card, IProgress<(string message, double percent)>? progress = null, string gpuType = "NVIDIA", bool dlssInputs = true, string? hotkey = null) => Task.FromResult<AuxInstalledRecord?>(null);
        public void Uninstall(GameCardViewModel card) { }
        public Task UpdateAsync(GameCardViewModel card, IProgress<(string message, double percent)>? progress = null) => Task.CompletedTask;
        public void CopyIniToGame(GameCardViewModel card) { }
        public bool IsOptiScalerFile(string filePath) => false;
        public List<AuxInstalledRecord> LoadAllRecords() => new();
        public AuxInstalledRecord? FindRecord(string gameName, string installPath) => null;
        public string GetEffectiveOsDllName(string gameName) => "dxgi.dll";
        public void SetHotkey(string hotkeyValue) { }
        public void ApplyHotkeyToAllGames(string hotkeyValue) { }
    }

    /// <summary>
    /// Minimal IAuxInstallService stub for DxvkService constructor.
    /// </summary>
    private class MinimalAuxInstallStub : IAuxInstallService
    {
        public Task<AuxInstalledRecord> InstallReShadeAsync(string gameName, string installPath, string? shaderModeOverride = null, bool use32Bit = false, string? filenameOverride = null, IEnumerable<string>? selectedPackIds = null, IProgress<(string, double)>? progress = null, string? screenshotSavePath = null, bool useNormalReShade = false, string? overlayHotkey = null, string? screenshotHotkey = null) => Task.FromResult(new AuxInstalledRecord());
        public Task<bool> CheckForUpdateAsync(AuxInstalledRecord record) => Task.FromResult(false);
        public void Uninstall(AuxInstalledRecord record) { }
        public void UninstallDllOnly(AuxInstalledRecord record) { }
        public List<AuxInstalledRecord> LoadAll() => new();
        public AuxInstalledRecord? FindRecord(string gameName, string installPath, string addonType) => null;
        public void SaveAuxRecord(AuxInstalledRecord record) { }
        public void RemoveRecord(AuxInstalledRecord record) { }
    }

    /// <summary>
    /// Creates a DxvkService with a configurable OptiScaler stub that returns
    /// the given filename from DetectInstallation.
    /// </summary>
    private static DxvkService CreateServiceWithOptiScaler(string? optiScalerFilename)
    {
        return new DxvkService(
            new HttpClient(),
            new MinimalAuxInstallStub(),
            new ConfigurableOptiScalerStub(optiScalerFilename),
            new GitHubETagCache());
    }

    // ── Property 4: OptiScaler coexistence routing ────────────────────────────────
    // Feature: dxvk-integration, Property 4: OptiScaler coexistence routing
    // **Validates: Requirements 6.1, 6.5, 6.6, 6.7**

    /// <summary>
    /// For any combination of required DXVK DLL names and an OptiScaler installed
    /// filename (or null if OptiScaler is not installed), ResolveDeploymentPaths
    /// SHALL route each DLL such that: DLLs whose filename matches OptiScaler's
    /// installed filename go to PluginFolderDlls, and all other DLLs go to
    /// InstalledDlls (game root). When OptiScaler is not installed, all DLLs go
    /// to game root.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ResolveDeploymentPaths_Correctly_Partitions_Dlls()
    {
        return Prop.ForAll(
            Arb.From(GenDllNameList),
            Arb.From(GenOptiScalerFilename),
            (List<string> requiredDlls, string? optiScalerFilename) =>
            {
                var service = CreateServiceWithOptiScaler(optiScalerFilename);
                var (rootDlls, pluginDlls) = service.ResolveDeploymentPaths(
                    requiredDlls, @"C:\Games\TestGame");

                // 1. Every input DLL must appear in exactly one output list
                var allOutput = rootDlls.Concat(pluginDlls).ToList();
                bool allAccountedFor = requiredDlls.Count == allOutput.Count
                    && requiredDlls.All(d => allOutput.Contains(d, StringComparer.OrdinalIgnoreCase));

                // 2. When OptiScaler is not installed, all DLLs go to root
                bool noOptiScalerCorrect = optiScalerFilename != null
                    || pluginDlls.Count == 0;

                // 3. When OptiScaler IS installed, only the matching filename goes to plugins
                bool routingCorrect = true;
                if (optiScalerFilename != null)
                {
                    foreach (var dll in requiredDlls)
                    {
                        bool isConflict = string.Equals(dll, optiScalerFilename,
                            StringComparison.OrdinalIgnoreCase);
                        bool inPlugins = pluginDlls.Contains(dll, StringComparer.OrdinalIgnoreCase);
                        bool inRoot = rootDlls.Contains(dll, StringComparer.OrdinalIgnoreCase);

                        if (isConflict && !inPlugins) { routingCorrect = false; break; }
                        if (!isConflict && !inRoot) { routingCorrect = false; break; }
                    }
                }

                // 4. No duplicates across the two lists
                bool noDuplicates = rootDlls.Count + pluginDlls.Count == allOutput.Count;

                return (allAccountedFor && noOptiScalerCorrect && routingCorrect && noDuplicates)
                    .Label($"dlls=[{string.Join(", ", requiredDlls)}], " +
                           $"optiScaler={optiScalerFilename ?? "null"}, " +
                           $"root=[{string.Join(", ", rootDlls)}], " +
                           $"plugins=[{string.Join(", ", pluginDlls)}], " +
                           $"allAccountedFor={allAccountedFor}, " +
                           $"noOptiScalerCorrect={noOptiScalerCorrect}, " +
                           $"routingCorrect={routingCorrect}, " +
                           $"noDuplicates={noDuplicates}");
            });
    }
}
