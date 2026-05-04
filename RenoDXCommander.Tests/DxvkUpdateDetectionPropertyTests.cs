using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DXVK update detection logic.
/// Feature: dxvk-integration, Property 11: Update detection
/// </summary>
public class DxvkUpdateDetectionPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates realistic DXVK version tag strings (e.g. "v2.7.1", "v1.10.3")
    /// plus edge cases (mixed case, empty-ish, arbitrary).
    /// </summary>
    private static readonly Gen<string> GenVersionString =
        Gen.OneOf(
            // Realistic DXVK version tags
            Gen.Elements(
                "v2.7.1", "v2.6.0", "v1.10.3", "v2.5", "v2.7.1-1",
                "v2.0", "v1.0.0", "v2.7", "v2.6.1", "v2.8.0",
                "v1.9.4", "v2.3.1", "unknown"),
            // Structured version strings: v{major}.{minor}.{patch}
            from major in Gen.Choose(0, 9)
            from minor in Gen.Choose(0, 20)
            from patch in Gen.Choose(0, 15)
            select $"v{major}.{minor}.{patch}",
            // Arbitrary strings to cover case-sensitivity and edge cases
            from len in Gen.Choose(0, 30)
            from chars in Gen.ArrayOf(len, Gen.Elements(
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-_+v ".ToCharArray()))
            select new string(chars));

    // ── Property 11: Update detection ─────────────────────────────────────────
    // Feature: dxvk-integration, Property 11: Update detection
    // **Validates: Requirements 2.1, 2.2**

    /// <summary>
    /// For any pair of version strings (cached, remote), CheckHasUpdate returns
    /// true if and only if the remote version differs from the cached version
    /// (case-sensitive, ordinal comparison).
    /// This mirrors DxvkService.CheckForUpdateAsync:
    ///   HasUpdate = !string.Equals(cachedTag, remoteTag, StringComparison.Ordinal)
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UpdateDetection_ReturnsTrueIffVersionsDiffer()
    {
        return Prop.ForAll(
            GenVersionString.ToArbitrary(),
            GenVersionString.ToArbitrary(),
            (string cached, string remote) =>
            {
                var hasUpdate = DxvkService.CheckHasUpdate(cached, remote);

                // Expected: update iff the strings are not identical (case-sensitive)
                var expected = !string.Equals(cached, remote, StringComparison.Ordinal);

                return (hasUpdate == expected)
                    .Label($"Cached='{cached}', Remote='{remote}', HasUpdate={hasUpdate}, Expected={expected}");
            });
    }

    /// <summary>
    /// For any version string, comparing it with itself always yields no update.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UpdateDetection_SameVersion_NeverHasUpdate()
    {
        return Prop.ForAll(GenVersionString.ToArbitrary(), version =>
        {
            var hasUpdate = DxvkService.CheckHasUpdate(version, version);

            return (!hasUpdate)
                .Label($"Version='{version}', HasUpdate={hasUpdate} (should be false)");
        });
    }

    // ── Unit tests for case-sensitivity and edge cases ────────────────────────
    // Validates: Requirements 2.1, 2.2

    [Theory]
    [InlineData("v2.7.1", "v2.7.1", false)]   // Same version — no update
    [InlineData("v2.6.0", "v2.7.1", true)]     // Different version — update
    [InlineData("v2.7.1", "v2.6.0", true)]     // Downgrade also detected as different
    [InlineData("V2.7.1", "v2.7.1", true)]     // Case-sensitive: V != v
    [InlineData("v1.0.0", "V1.0.0", true)]     // Case-sensitive
    [InlineData("", "", false)]                  // Both empty — no update
    [InlineData("v2.7.1", "", true)]            // Cached has value, remote empty — different
    [InlineData("", "v2.7.1", true)]            // Cached empty, remote has value — different
    public void UpdateDetection_KnownPairs_ReturnsExpected(
        string cached, string remote, bool expectedHasUpdate)
    {
        var hasUpdate = DxvkService.CheckHasUpdate(cached, remote);

        Assert.Equal(expectedHasUpdate, hasUpdate);
    }
}
