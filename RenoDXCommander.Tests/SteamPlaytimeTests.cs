using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Steam playtime: reading an app's "Playtime" from localconfig.vdf text.</summary>
public class SteamPlaytimeTests
{
    private const string Vdf = """
        "UserLocalConfigStore"
        {
            "Software"
            {
                "Valve"
                {
                    "Steam"
                    {
                        "apps"
                        {
                            "570"
                            {
                                "cloud"
                                {
                                    "Playtime"      "999"
                                }
                                "LastPlayed"        "1700000000"
                                "Playtime"      "1234"
                            }
                            "1091500"
                            {
                                "Playtime2wks"      "60"
                                "Playtime"      "75"
                            }
                            "730"
                            {
                                "LastPlayed"        "1700000000"
                            }
                        }
                    }
                }
            }
        }
        """;

    [Fact]
    public void ReadsTheAppsOwnPlaytime_NotANestedOne() =>
        Assert.Equal(1234, SteamPlaytimeService.ParseMinutes(Vdf, 570));

    [Fact]
    public void IgnoresSimilarKeys() =>
        Assert.Equal(75, SteamPlaytimeService.ParseMinutes(Vdf, 1091500));

    [Fact]
    public void NullWhenTheAppHasNoPlaytime() =>
        Assert.Null(SteamPlaytimeService.ParseMinutes(Vdf, 730));

    [Fact]
    public void NullWhenTheAppIsMissing() =>
        Assert.Null(SteamPlaytimeService.ParseMinutes(Vdf, 12345));

    [Fact]
    public void NullWithoutAnAppsBlock() =>
        Assert.Null(SteamPlaytimeService.ParseMinutes("\"UserLocalConfigStore\" { }", 570));

    [Fact]
    public void AppIdInsideAnotherAppsBlockDoesNotCount() =>
        Assert.Null(SteamPlaytimeService.ParseMinutes(
            "\"apps\" { \"10\" { \"friends\" { \"570\" { \"Playtime\" \"5\" } } } }", 570));
}
