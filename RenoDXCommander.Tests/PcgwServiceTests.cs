using System.Net.Http;
using System.Net.Http.Headers;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Tests for PcgwService circuit-breaker helpers (issue #98 — PCGW load).
/// Pure logic only — no filesystem, no network, no DI container.
/// </summary>
public class PcgwServiceTests
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // ParseRetryAfter — 429 Retry-After header (delta-seconds and HTTP-date forms)
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParseRetryAfter_NoHeader_ReturnsNull()
    {
        using var response = new HttpResponseMessage();
        Assert.Null(PcgwService.ParseRetryAfter(response));
    }

    [Fact]
    public void ParseRetryAfter_DeltaSeconds_ReturnsValue()
    {
        using var response = new HttpResponseMessage();
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));

        var result = PcgwService.ParseRetryAfter(response);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromSeconds(42), result.Value);
    }

    [Fact]
    public void ParseRetryAfter_HttpDate_ReturnsPositiveDelta()
    {
        using var response = new HttpResponseMessage();
        response.Headers.RetryAfter =
            new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(60));

        var result = PcgwService.ParseRetryAfter(response);

        Assert.NotNull(result);
        // Allow slack for test execution time — must be positive and not exceed 60s.
        Assert.InRange(result.Value, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(60));
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // ClampBreakDuration — bounds so a bogus Retry-After can't lock PCGW out for hours
    // ═══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClampBreakDuration_BelowMinimum_RaisedToMinimum()
    {
        // e.g. Retry-After: 0 or 1 second must not allow an instant hot-loop retry
        var result = PcgwService.ClampBreakDuration(TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(10), result);
    }

    [Fact]
    public void ClampBreakDuration_AboveMaximum_LoweredToMaximum()
    {
        // e.g. Retry-After: 86400 must not disable PCGW for a whole day
        var result = PcgwService.ClampBreakDuration(TimeSpan.FromHours(24));
        Assert.Equal(TimeSpan.FromMinutes(15), result);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(60)]
    [InlineData(900)]
    public void ClampBreakDuration_WithinBounds_Unchanged(int seconds)
    {
        var requested = TimeSpan.FromSeconds(seconds);
        Assert.Equal(requested, PcgwService.ClampBreakDuration(requested));
    }
}
