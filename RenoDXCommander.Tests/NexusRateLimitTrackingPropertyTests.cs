// Feature: nexus-api-integration, Property 10: Rate limit header tracking
using System.Net;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for rate limit header tracking.
/// For any non-negative integer value in the X-RL-Daily-Remaining response header,
/// DailyRequestsRemaining equals that value, and IsRateLimited is false when value > 0
/// after a successful 200 response.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
///
/// **Validates: Requirements 10.1, 10.2**
/// </summary>
public class NexusRateLimitTrackingPropertyTests
{
    /// <summary>
    /// Generator for non-negative integers representing the X-RL-Daily-Remaining header value.
    /// Range 0 to 10_000 covers realistic daily quota values.
    /// </summary>
    private static readonly Gen<int> GenNonNegativeRemaining =
        Gen.Choose(0, 10_000);

    // ── Property 10 ───────────────────────────────────────────────────────────────
    // Feature: nexus-api-integration, Property 10: Rate limit header tracking
    // **Validates: Requirements 10.1, 10.2**

    /// <summary>
    /// For any non-negative integer value received in the X-RL-Daily-Remaining
    /// response header, after calling TrackRateLimit with a 200 OK response:
    ///   - DailyRequestsRemaining equals the header value
    ///   - IsRateLimited is false when the value is > 0
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TrackRateLimit_SetsRemainingAndNotRateLimited_ForSuccessResponse()
    {
        return Prop.ForAll(
            Arb.From(GenNonNegativeRemaining),
            (int remaining) =>
            {
                // Arrange: create a fresh client with a mock HttpClient
                var httpClient = new HttpClient();
                var client = new NexusApiClient(httpClient, "1.0.0-test");

                // Build a 200 OK response with the X-RL-Daily-Remaining header
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.TryAddWithoutValidation("X-RL-Daily-Remaining", remaining.ToString());

                // Act: call the internal TrackRateLimit method
                client.TrackRateLimit(response);

                // Assert
                bool remainingMatches = client.DailyRequestsRemaining == remaining;
                bool notRateLimited = remaining > 0 ? !client.IsRateLimited : true;

                // Clean up
                response.Dispose();
                httpClient.Dispose();

                return (remainingMatches && notRateLimited)
                    .Label($"remaining={remaining}, " +
                           $"DailyRequestsRemaining={client.DailyRequestsRemaining}, " +
                           $"IsRateLimited={client.IsRateLimited}, " +
                           $"remainingMatches={remainingMatches}, notRateLimited={notRateLimited}");
            });
    }
}
