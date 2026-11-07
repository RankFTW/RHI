using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Xbox achievements: building the list from the Xbox app's cache JSON and the id helpers.</summary>
public class XboxAchievementTests
{
    private const string Img = "https://images-eds-ssl.xboxlive.com/image?url=abc";

    private static string Ach(string id, string name, bool unlocked = false, string? time = null, bool secret = false,
        double rarity = 50, int score = 10, double percent = 0, bool revoked = false, string img = Img) =>
        "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"gamerscore\":" + score
        + ",\"isSecret\":" + (secret ? "true" : "false") + ",\"isUnlocked\":" + (unlocked ? "true" : "false")
        + ",\"isRevoked\":" + (revoked ? "true" : "false")
        + (time != null ? ",\"timeUnlocked\":\"" + time + "\"" : "")
        + ",\"description\":{\"locked\":\"L-" + id + "\",\"unlocked\":\"U-" + id + "\"}"
        + ",\"imageUrl\":\"" + img + "\",\"percentComplete\":" + percent
        + ",\"rarity\":{\"currentCategory\":\"Common\",\"currentPercentage\":" + rarity + "}}";

    [Fact]
    public void Build_UnlockedNewestFirstThenLockedInGameOrder()
    {
        var json = "[" + string.Join(",",
            Ach("1", "One"),
            Ach("2", "Two", unlocked: true, time: "2025-12-10T23:06:44.537Z"),
            Ach("3", "Three", unlocked: true, time: "2026-01-02T10:00:00Z", score: 50, rarity: 3.25),
            Ach("4", "Four", secret: true, percent: 45.5)) + "]";
        var set = XboxAchievementService.Build("1610974574", json)!;

        Assert.Equal(AchievementStore.Xbox, set.Store);
        Assert.Equal("1610974574", set.GameId);
        Assert.Equal(new[] { "3", "2", "1", "4" }, set.Items.Select(a => a.ApiName));
        Assert.Equal(new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc), set.Items[0].UnlockedUtc);
        Assert.Equal(DateTimeKind.Utc, set.Items[1].UnlockedUtc!.Value.Kind);
        Assert.Equal("U-3", set.Items[0].Description);
        Assert.Equal("L-1", set.Items[2].Description);
        Assert.Equal(3.25, set.Items[0].Percent);
        Assert.Equal(50, set.Items[0].Gamerscore);
        Assert.True(set.Items[3].Hidden);
        Assert.Equal(45, set.Items[3].ProgressPercent);
        Assert.Null(set.Items[2].ProgressPercent);
        Assert.Equal(2, set.Unlocked);
        Assert.Equal(60, set.GamerscoreEarned);
        Assert.Equal(80, set.GamerscoreTotal);
        Assert.True(set.HasRarity);
    }

    [Fact]
    public void Build_RevokedCountsAsLocked()
    {
        var set = XboxAchievementService.Build("1", "[" + Ach("1", "One", unlocked: true, time: "2026-01-01T00:00:00Z", revoked: true) + "]")!;
        Assert.False(set.Items[0].Unlocked);
        Assert.Null(set.Items[0].UnlockedUtc);
    }

    [Fact]
    public void Build_EmptyOrBadDataIsNull()
    {
        Assert.Null(XboxAchievementService.Build("1", "[]"));
        Assert.Null(XboxAchievementService.Build("1", "not json"));
        Assert.Null(XboxAchievementService.Build("1", "{\"a\":1}"));
        Assert.Null(XboxAchievementService.Build("..\\evil", "[" + Ach("1", "One") + "]"));
    }

    [Fact]
    public void Build_DropsIconsFromOtherHosts()
    {
        var set = XboxAchievementService.Build("1", "[" + Ach("1", "One", img: "http://images-eds-ssl.xboxlive.com/x")
            + "," + Ach("2", "Two", img: "https://evil.example.com/x.png") + "," + Ach("3", "Three") + "]")!;
        Assert.Null(set.Items[0].Icon);
        Assert.Null(set.Items[1].Icon);
        Assert.Equal(Img, set.Items[2].Icon);
        Assert.Null(XboxAchievementService.TryGetCachedIcon("..", Img));
    }

    [Fact]
    public void TitleIdsFromSummary_ReadsXboxTitleIds()
    {
        var json = "{\"type\":2,\"data\":{\"alternateIds\":[{\"idType\":\"XBOXTITLEID\",\"id\":\"1826390998\"},"
                 + "{\"idType\":\"PACKAGEFAMILYNAME\",\"id\":\"A.B_x\"},{\"idType\":\"XBOXTITLEID\",\"id\":\"bad id\"}]}}";
        Assert.Equal(new[] { "1826390998" }, XboxAchievementService.TitleIdsFromSummary(json));
        Assert.Empty(XboxAchievementService.TitleIdsFromSummary("oops"));
        Assert.Empty(XboxAchievementService.TitleIdsFromSummary(null));
    }

    [Theory]
    [InlineData("Microsoft.Avowed_8wekyb3d8bbwe!Game", "Microsoft.Avowed_8wekyb3d8bbwe")]
    [InlineData("Microsoft.Avowed_8wekyb3d8bbwe", "Microsoft.Avowed_8wekyb3d8bbwe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("!App", null)]
    public void PfnFromAumid(string? aumid, string? pfn) =>
        Assert.Equal(pfn, XboxAchievementService.PfnFromAumid(aumid));

    [Fact]
    public void NormaliseName_IgnoresSymbolsAndCase() =>
        Assert.Equal(XboxAchievementService.NormaliseName("avowed"), XboxAchievementService.NormaliseName("Avowed™ "));
}
