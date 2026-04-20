using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for NexusUrlParser covering concrete URL examples and edge cases.
/// Validates: Requirements 2.1, 2.2, 2.3, 2.4
/// </summary>
public class NexusUrlParserUnitTests
{
    // ── Concrete URL examples ─────────────────────────────────────────────────────

    [Fact]
    public void Parse_ModUrl_ReturnsGameDomainAndModId()
    {
        // Requirement 2.1: parse game domain and mod ID from /{game_domain}/mods/{mod_id}
        var result = NexusUrlParser.Parse("https://www.nexusmods.com/cyberpunk2077/mods/123");

        Assert.NotNull(result);
        Assert.Equal("cyberpunk2077", result.GameDomain);
        Assert.Equal(123, result.ModId);
    }

    [Fact]
    public void Parse_CatalogueUrl_ReturnsGameDomainWithNullModId()
    {
        // Requirement 2.2: parse game domain from /games/{game_domain}
        var result = NexusUrlParser.Parse("https://www.nexusmods.com/games/eldenring");

        Assert.NotNull(result);
        Assert.Equal("eldenring", result.GameDomain);
        Assert.Null(result.ModId);
    }

    // ── Edge cases: trailing slashes, query strings, fragments ─────────────────────

    [Fact]
    public void Parse_ModUrlWithTrailingSlash_StillParses()
    {
        var result = NexusUrlParser.Parse("https://www.nexusmods.com/cyberpunk2077/mods/123/");

        Assert.NotNull(result);
        Assert.Equal("cyberpunk2077", result.GameDomain);
        Assert.Equal(123, result.ModId);
    }

    [Fact]
    public void Parse_ModUrlWithQueryString_StillParses()
    {
        var result = NexusUrlParser.Parse("https://www.nexusmods.com/cyberpunk2077/mods/123?tab=files");

        Assert.NotNull(result);
        Assert.Equal("cyberpunk2077", result.GameDomain);
        Assert.Equal(123, result.ModId);
    }

    [Fact]
    public void Parse_ModUrlWithFragment_StillParses()
    {
        var result = NexusUrlParser.Parse("https://www.nexusmods.com/cyberpunk2077/mods/123#description");

        Assert.NotNull(result);
        Assert.Equal("cyberpunk2077", result.GameDomain);
        Assert.Equal(123, result.ModId);
    }

    // ── Edge cases: empty, null, malformed ────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        var result = NexusUrlParser.Parse("");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_Null_ReturnsNull()
    {
        var result = NexusUrlParser.Parse(null);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_NotAUrl_ReturnsNull()
    {
        var result = NexusUrlParser.Parse("not a url");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_WrongDomain_ReturnsNull()
    {
        // Malformed: valid URL but not nexusmods.com
        var result = NexusUrlParser.Parse("https://google.com/mods/123");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_ModIdZero_ReturnsNull()
    {
        // Non-positive mod ID should be rejected
        var result = NexusUrlParser.Parse("https://www.nexusmods.com/cyberpunk2077/mods/0");

        Assert.Null(result);
    }

    [Fact]
    public void Parse_ModIdNegative_ReturnsNull()
    {
        // Negative mod ID should be rejected
        var result = NexusUrlParser.Parse("https://www.nexusmods.com/cyberpunk2077/mods/-5");

        Assert.Null(result);
    }
}
