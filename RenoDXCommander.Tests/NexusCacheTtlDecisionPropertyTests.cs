using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for cache TTL decision logic.
/// Feature: nexus-api-integration, Property 9: Cache TTL decision
/// </summary>
public class NexusCacheTtlDecisionPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reasonable tick range for DateTime generation.
    /// From 2020-01-01 to 2030-01-01 UTC.
    /// </summary>
    private static readonly long MinTicks = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
    private static readonly long MaxTicks = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

    /// <summary>
    /// Generates a random DateTime (UTC) within a reasonable range.
    /// </summary>
    private static readonly Gen<DateTime> GenDateTimeUtc =
        Gen.Choose((int)(MinTicks / TimeSpan.TicksPerSecond), (int)(MaxTicks / TimeSpan.TicksPerSecond))
           .Select(seconds => new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
               .AddSeconds(seconds - (int)(MinTicks / TimeSpan.TicksPerSecond)));

    /// <summary>
    /// Generates a random positive TimeSpan representing the elapsed time (0 to 48 hours).
    /// </summary>
    private static readonly Gen<TimeSpan> GenPositiveElapsed =
        Gen.Choose(0, 48 * 3600) // 0 to 48 hours in seconds
           .Select(seconds => TimeSpan.FromSeconds(seconds));

    /// <summary>
    /// Generates a random positive TimeSpan for TTL (1 second to 24 hours).
    /// </summary>
    private static readonly Gen<TimeSpan> GenTtl =
        Gen.Choose(1, 24 * 3600) // 1 second to 24 hours
           .Select(seconds => TimeSpan.FromSeconds(seconds));

    // ── Property 9 ────────────────────────────────────────────────────────────────
    // Feature: nexus-api-integration, Property 9: Cache TTL decision
    // **Validates: Requirements 3.4**

    /// <summary>
    /// For any (lastCheckUtc, currentUtc, ttl) triple where currentUtc >= lastCheckUtc
    /// and ttl is a positive TimeSpan (1 second to 24 hours),
    /// NexusUpdateChecker.IsCacheHit returns true if and only if
    /// (currentUtc - lastCheckUtc) &lt; ttl.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CacheTtlDecision_HitIfAndOnlyIf_ElapsedLessThanTtl()
    {
        return Prop.ForAll(
            Arb.From(GenDateTimeUtc),
            Arb.From(GenPositiveElapsed),
            Arb.From(GenTtl),
            (DateTime lastCheckUtc, TimeSpan elapsed, TimeSpan ttl) =>
            {
                // Derive currentUtc from lastCheckUtc + elapsed so current >= lastCheck
                var currentUtc = lastCheckUtc + elapsed;

                // Act
                var isCacheHit = NexusUpdateChecker.IsCacheHit(lastCheckUtc, currentUtc, ttl);

                // Expected: cache hit when elapsed time is less than TTL
                var expected = elapsed < ttl;

                return (isCacheHit == expected)
                    .Label($"isCacheHit={isCacheHit}, expected={expected} " +
                           $"(elapsed={elapsed}, ttl={ttl})");
            });
    }
}
