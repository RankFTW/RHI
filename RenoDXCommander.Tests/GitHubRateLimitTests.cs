using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>The rate-limit flag clears at GitHub's reset time instead of lasting until restart (review item 5.1).</summary>
public class GitHubRateLimitTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    private static string Unix(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds().ToString();

    [Fact]
    public void UsesTheResetHeader()
    {
        var reset = GitHubETagCache.RateLimitResetUtc(Unix(Now.AddMinutes(20)), Now);
        Assert.Equal(Now.AddMinutes(20).AddSeconds(5), reset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("0")]
    public void MissingOrBadHeader_WaitsAnHour(string? header)
    {
        Assert.Equal(Now.AddHours(1), GitHubETagCache.RateLimitResetUtc(header, Now));
    }

    [Fact]
    public void ResetInThePast_WaitsAnHour()
    {
        Assert.Equal(Now.AddHours(1), GitHubETagCache.RateLimitResetUtc(Unix(Now.AddMinutes(-10)), Now));
    }

    [Fact]
    public void ResetFarAhead_IsCapped()
    {
        // A wrong clock or header must not block GitHub for a day
        Assert.Equal(Now.AddHours(1), GitHubETagCache.RateLimitResetUtc(Unix(Now.AddHours(30)), Now));
    }
}
