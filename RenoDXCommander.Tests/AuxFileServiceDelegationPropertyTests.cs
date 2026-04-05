using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

// Feature: static-service-interfaces, Property 3: IAuxFileService delegation equivalence

/// <summary>
/// Property-based tests verifying that IAuxFileService instance methods on
/// AuxInstallService return the same results as the corresponding static methods.
/// The instance methods are pure delegations.
///
/// **Validates: Requirements 4.2, 8.2**
/// </summary>
public class AuxFileServiceDelegationPropertyTests
{
    /// <summary>
    /// Creates an AuxInstallService instance cast to IAuxFileService for testing.
    /// </summary>
    private static IAuxFileService CreateService()
    {
        var http = new HttpClient();
        // Use a minimal stub for IShaderPackService — the file-identification
        // methods under test never touch it.
        var service = new AuxInstallService(http, new StubShaderPackService());
        return service;
    }

    // Feature: static-service-interfaces, Property 3: IAuxFileService delegation equivalence
    /// <summary>
    /// For any file path string, calling IdentifyDxgiFile through the IAuxFileService
    /// instance returns the same DxgiFileType as calling the static method directly.
    /// **Validates: Requirements 4.2, 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property IdentifyDxgiFile_Instance_Equals_Static()
    {
        return Prop.ForAll(Arb.Default.NonNull<string>(), nonNullPath =>
        {
            var path = nonNullPath.Get;
            var svc = CreateService();

            var instanceResult = svc.IdentifyDxgiFile(path);
            var staticResult = AuxInstallService.IdentifyDxgiFile(path);

            return (instanceResult == staticResult)
                .Label($"Instance returned {instanceResult}, static returned {staticResult} for path '{path}'");
        });
    }

    // Feature: static-service-interfaces, Property 3: IAuxFileService delegation equivalence
    /// <summary>
    /// For any file path string, calling IsReShadeFileStrict through the IAuxFileService
    /// instance returns the same bool as calling the static method directly.
    /// **Validates: Requirements 4.2, 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property IsReShadeFileStrict_Instance_Equals_Static()
    {
        return Prop.ForAll(Arb.Default.NonNull<string>(), nonNullPath =>
        {
            var path = nonNullPath.Get;
            var svc = CreateService();

            var instanceResult = svc.IsReShadeFileStrict(path);
            var staticResult = AuxInstallService.IsReShadeFileStrict(path);

            return (instanceResult == staticResult)
                .Label($"Instance returned {instanceResult}, static returned {staticResult} for path '{path}'");
        });
    }

    // Feature: static-service-interfaces, Property 3: IAuxFileService delegation equivalence
    /// <summary>
    /// For any file path string, calling IsReShadeFile through the IAuxFileService
    /// instance returns the same bool as calling the static method directly.
    /// **Validates: Requirements 4.2, 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property IsReShadeFile_Instance_Equals_Static()
    {
        return Prop.ForAll(Arb.Default.NonNull<string>(), nonNullPath =>
        {
            var path = nonNullPath.Get;
            var svc = CreateService();

            var instanceResult = svc.IsReShadeFile(path);
            var staticResult = AuxInstallService.IsReShadeFile(path);

            return (instanceResult == staticResult)
                .Label($"Instance returned {instanceResult}, static returned {staticResult} for path '{path}'");
        });
    }

    // Feature: static-service-interfaces, Property 3: IAuxFileService delegation equivalence
    /// <summary>
    /// For any pair of (installPath, fileName) strings, calling ReadInstalledVersion
    /// through the IAuxFileService instance returns the same result as the static method.
    /// **Validates: Requirements 4.2, 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ReadInstalledVersion_Instance_Equals_Static()
    {
        return Prop.ForAll(
            Arb.Default.NonNull<string>(),
            Arb.Default.NonNull<string>(),
            (nonNullInstallPath, nonNullFileName) =>
            {
                var installPath = nonNullInstallPath.Get;
                var fileName = nonNullFileName.Get;
                var svc = CreateService();

                var instanceResult = svc.ReadInstalledVersion(installPath, fileName);
                var staticResult = AuxInstallService.ReadInstalledVersion(installPath, fileName);

                return (instanceResult == staticResult)
                    .Label($"Instance returned '{instanceResult}', static returned '{staticResult}' " +
                           $"for installPath='{installPath}', fileName='{fileName}'");
            });
    }

    /// <summary>
    /// Minimal IShaderPackService stub — the file-identification methods never use it.
    /// </summary>
    private sealed class StubShaderPackService : IShaderPackService
    {
        public IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> AvailablePacks
            => Array.Empty<(string, string, ShaderPackService.PackCategory)>();

        public string? GetPackDescription(string packId) => null;
        public string[] GetRequiredPacks(string packId) => Array.Empty<string>();
        public Task EnsureLatestAsync(IProgress<string>? progress = null) => Task.CompletedTask;
        public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null) { }
        public void RemoveFromGameFolder(string gameDir) { }
        public bool IsManagedByRdxc(string gameDir) => false;
        public void RestoreOriginalIfPresent(string gameDir) { }
        public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null) { }
        public void SyncShadersToAllLocations(
            IEnumerable<(string installPath, bool rsInstalled, string? shaderModeOverride)> locations,
            IEnumerable<string>? selectedPackIds = null) { }
    }
}
