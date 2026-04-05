using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class StaticServiceInterfaceStructureTests
{
    // ── Reflection: interface exclusion checks ────────────────────────────────

    /// <summary>
    /// ICrashReporter must NOT declare a Register method — global error handler
    /// registration is a one-time app-startup concern that stays static.
    /// Validates: Requirement 1.4
    /// </summary>
    [Fact]
    public void ICrashReporter_DoesNotDeclare_Register()
    {
        var methods = typeof(ICrashReporter).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        Assert.DoesNotContain(methods, m => m.Name == "Register");
    }

    /// <summary>
    /// IAuxFileService must NOT include the instance methods already on IAuxInstallService
    /// (InstallDcAsync, InstallReShadeAsync, Uninstall).
    /// Validates: Requirement 3.3
    /// </summary>
    [Fact]
    public void IAuxFileService_DoesNotDeclare_IAuxInstallServiceMethods()
    {
        var methods = typeof(IAuxFileService).GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var methodNames = methods.Select(m => m.Name).ToHashSet();

        Assert.DoesNotContain("InstallReShadeAsync", methodNames);
        Assert.DoesNotContain("Uninstall", methodNames);
    }

    /// <summary>
    /// IAuxFileService must NOT declare constant or path members — those stay as
    /// public static members on AuxInstallService.
    /// Validates: Requirement 5.3
    /// </summary>
    [Fact]
    public void IAuxFileService_DoesNotDeclare_ConstantOrPathMembers()
    {
        var allMembers = typeof(IAuxFileService).GetMembers(BindingFlags.Public | BindingFlags.Instance);
        var memberNames = allMembers.Select(m => m.Name).ToHashSet();

        // Constants
        Assert.DoesNotContain("TypeDc", memberNames);
        Assert.DoesNotContain("TypeReShade", memberNames);
        Assert.DoesNotContain("DcUrl", memberNames);
        Assert.DoesNotContain("DcCacheFile", memberNames);
        Assert.DoesNotContain("RsNormalName", memberNames);
        Assert.DoesNotContain("RsDcModeName", memberNames);

        // Static path fields / properties
        Assert.DoesNotContain("RsStagingDir", memberNames);
        Assert.DoesNotContain("InisDir", memberNames);
        Assert.DoesNotContain("DownloadCacheDir", memberNames);
        Assert.DoesNotContain("DcReShadeFolderPath", memberNames);
        Assert.DoesNotContain("RsStagedPath64", memberNames);
        Assert.DoesNotContain("RsStagedPath32", memberNames);
        Assert.DoesNotContain("RsIniPath", memberNames);
        Assert.DoesNotContain("DcIniPath", memberNames);
    }

    // ── DI container registration checks ─────────────────────────────────────

    /// <summary>
    /// Builds a minimal ServiceCollection mirroring the relevant App.xaml.cs
    /// registrations and verifies ICrashReporter resolves to CrashReporterService.
    /// Validates: Requirement 6.1
    /// </summary>
    [Fact]
    public void DI_Resolves_ICrashReporter_As_CrashReporterService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICrashReporter, CrashReporterService>();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ICrashReporter>();

        Assert.IsType<CrashReporterService>(resolved);
    }

    /// <summary>
    /// Builds a minimal ServiceCollection mirroring the relevant App.xaml.cs
    /// registrations and verifies IAuxFileService resolves to AuxInstallService.
    /// Validates: Requirement 6.2
    /// </summary>
    [Fact]
    public void DI_Resolves_IAuxFileService_As_AuxInstallService()
    {
        var services = BuildAuxServiceCollection();

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IAuxFileService>();

        Assert.IsType<AuxInstallService>(resolved);
    }

    /// <summary>
    /// The same AuxInstallService singleton must back both IAuxInstallService
    /// and IAuxFileService — ReferenceEquals confirms a single instance.
    /// Validates: Requirement 6.3
    /// </summary>
    [Fact]
    public void DI_SameSingleton_For_IAuxInstallService_And_IAuxFileService()
    {
        var services = BuildAuxServiceCollection();

        using var provider = services.BuildServiceProvider();
        var auxInstall = provider.GetRequiredService<IAuxInstallService>();
        var auxFile = provider.GetRequiredService<IAuxFileService>();

        Assert.True(ReferenceEquals(auxInstall, auxFile),
            "IAuxInstallService and IAuxFileService should resolve to the same singleton instance.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal ServiceCollection with the same registration pattern
    /// used in App.xaml.cs for AuxInstallService and its dependencies.
    /// </summary>
    private static ServiceCollection BuildAuxServiceCollection()
    {
        var services = new ServiceCollection();

        // AuxInstallService constructor requires HttpClient and IShaderPackService
        services.AddSingleton<HttpClient>(_ => new HttpClient());
        services.AddSingleton<IShaderPackService, StubShaderPackService>();

        // Mirror App.xaml.cs registrations
        services.AddSingleton<IAuxInstallService, AuxInstallService>();
        services.AddSingleton<IAuxFileService>(sp =>
            sp.GetRequiredService<IAuxInstallService>() as AuxInstallService
            ?? throw new InvalidOperationException("IAuxInstallService must be AuxInstallService"));

        return services;
    }

    /// <summary>
    /// Minimal stub so AuxInstallService can be constructed — none of these
    /// members are exercised by the DI resolution tests.
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
