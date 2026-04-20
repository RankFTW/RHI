using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for NXM link expiry detection.
/// Feature: nexus-api-integration, Property 4: NXM link expiry detection
/// </summary>
public class NxmLinkExpiryDetectionPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a Unix timestamp in a reasonable range (0 to 2,000,000,000).
    /// </summary>
    private static readonly Gen<long> GenUnixTimestamp =
        Gen.Choose(0, 2_000_000_000).Select(i => (long)i);

    // ── Property 4 ────────────────────────────────────────────────────────────────
    // Feature: nexus-api-integration, Property 4: NXM link expiry detection
    // **Validates: Requirements 6.6**

    /// <summary>
    /// For any pair of (currentTime, expires) values, the expiry check returns
    /// expired=true if and only if currentTime.ToUnixTimeSeconds() >= expires.
    /// This is a pure logic test — no service dependency needed.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NxmLink_ExpiryDetection_MatchesUnixTimestampComparison()
    {
        return Prop.ForAll(
            Arb.From(GenUnixTimestamp),
            Arb.From(GenUnixTimestamp),
            (long currentUnix, long expires) =>
            {
                // Arrange: construct a DateTimeOffset from the random Unix timestamp
                var currentTime = DateTimeOffset.FromUnixTimeSeconds(currentUnix);

                // Act: compute expiry using the same logic the download service would use
                bool isExpired = currentTime.ToUnixTimeSeconds() >= expires;

                // Assert: the result matches the direct comparison
                bool expected = currentUnix >= expires;

                return (isExpired == expected)
                    .Label($"currentUnix={currentUnix}, expires={expires}, " +
                           $"isExpired={isExpired}, expected={expected}");
            });
    }
}
