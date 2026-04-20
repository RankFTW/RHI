using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for version comparison update availability.
/// Feature: nexus-api-integration, Property 8: Version comparison determines update availability
/// </summary>
public class NexusVersionComparisonPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty version string (printable ASCII, 1-30 chars).
    /// Printable ASCII range: 0x20 (space) to 0x7E (~).
    /// </summary>
    private static readonly Gen<string> GenVersionString =
        from length in Gen.Choose(1, 30)
        from chars in Gen.ArrayOf(length, Gen.Choose(0x20, 0x7E).Select(i => (char)i))
        select new string(chars);

    // ── Property 8 ────────────────────────────────────────────────────────────────
    // Feature: nexus-api-integration, Property 8: Version comparison determines update availability
    // **Validates: Requirements 3.2**

    /// <summary>
    /// For any two non-empty version strings (localVersion, remoteVersion),
    /// NexusUpdateChecker.IsUpdateAvailable returns true if and only if
    /// localVersion != remoteVersion (string inequality).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property VersionComparison_UpdateAvailable_EqualsStringInequality()
    {
        return Prop.ForAll(
            Arb.From(GenVersionString),
            Arb.From(GenVersionString),
            (string localVersion, string remoteVersion) =>
            {
                // Act
                var updateAvailable = NexusUpdateChecker.IsUpdateAvailable(localVersion, remoteVersion);

                // Expected: update available when versions differ
                var expected = !string.Equals(localVersion, remoteVersion, StringComparison.Ordinal);

                return (updateAvailable == expected)
                    .Label($"updateAvailable={updateAvailable}, expected={expected} " +
                           $"(local=\"{localVersion}\", remote=\"{remoteVersion}\")");
            });
    }
}
