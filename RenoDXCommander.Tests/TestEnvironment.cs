using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Keeps test runs out of the real RHI data folder (%LocalAppData%\RHI): session logs go to a temp
/// folder, and services that take a GitHub ETag cache get one backed by a temp file.
/// </summary>
internal static class TestEnvironment
{
    private static readonly string DataDir = Path.Combine(Path.GetTempPath(), "rhi-test-data");

    // Runs when the test assembly loads, before any test touches CrashReporter.
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("RHI_LOG_DIR", Path.Combine(Path.GetTempPath(), "rhi-test-logs"));

    /// <summary>An ETag cache that never reads or writes the user's github_etag_cache.json.</summary>
    internal static GitHubETagCache NewETagCache()
    {
        Directory.CreateDirectory(DataDir);
        return new GitHubETagCache(Path.Combine(DataDir, "github_etag_cache.json"));
    }
}
