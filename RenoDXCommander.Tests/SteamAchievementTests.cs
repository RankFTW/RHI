using System.Text;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Steam achievements: the binary KeyValues reader, building the list, rarity and account parsing.</summary>
public class SteamAchievementTests
{
    // ── Tiny binary KeyValues writer for test data ───────────────────────────

    private static void Str(List<byte> b, string s) { b.AddRange(Encoding.UTF8.GetBytes(s)); b.Add(0); }

    private static void Write(List<byte> b, Dictionary<string, object> node)
    {
        foreach (var (k, v) in node)
        {
            switch (v)
            {
                case Dictionary<string, object> d: b.Add(0); Str(b, k); Write(b, d); b.Add(8); break;
                case string s: b.Add(1); Str(b, k); Str(b, s); break;
                case int i: b.Add(2); Str(b, k); b.AddRange(BitConverter.GetBytes(i)); break;
                case float f: b.Add(3); Str(b, k); b.AddRange(BitConverter.GetBytes(f)); break;
                case ulong u: b.Add(7); Str(b, k); b.AddRange(BitConverter.GetBytes(u)); break;
            }
        }
    }

    private static byte[] Bytes(Dictionary<string, object> root)
    {
        var b = new List<byte>();
        Write(b, root);
        b.Add(8);
        return b.ToArray();
    }

    private static Dictionary<string, object> D(params (string K, object V)[] items) =>
        items.ToDictionary(x => x.K, x => x.V);

    private const string Icon1 = "b348dd6703d7634fad4cba22f3728ea6db8d0dd8.jpg";
    private const string Icon2 = "7d925f1866baeed9d9ce8fbc0f58f905e15d7610.jpg";

    private static Dictionary<string, object> Bit(string api, string en, string desc, int hidden = 0, string? de = null) =>
        D(("name", api), ("display", D(
            ("name", de == null ? D(("english", en)) : D(("english", en), ("german", de))),
            ("desc", D(("english", desc))),
            ("hidden", hidden),
            ("icon", Icon1),
            ("icon_gray", Icon2))));

    private static Dictionary<string, object> Schema() => D(("100", D(
        ("stats", D(
            ("1", D(("bits", D(
                ("0", Bit("ACH_A", "First", "Do the first thing", de: "Erste")),
                ("1", Bit("ACH_B", "Second", "Do the second thing")),
                ("2", Bit("ACH_C", "Secret", "Spoiler", hidden: 1)),
                ("31", Bit("ACH_D", "Bit31", "Top bit")))), ("type", "ACHIEVEMENTS"))),
            ("2", D(("type", "INT"), ("name", "kills"))),
            ("3", D(("bits", D(("0", Bit("ACH_E", "Block3", "In another block")))), ("type", "ACHIEVEMENTS"))))),
        ("gamename", "Test Game"))));

    // Bits 0 and 31 of block 1 unlocked (data stored as a negative int32), bit 0 of block 3 unlocked
    private static Dictionary<string, object> Stats() => D(("cache", D(
        ("crc", 1),
        ("1", D(("data", unchecked((int)0x80000001)), ("AchievementTimes", D(("0", 1_700_000_000), ("31", 1_800_000_000))))),
        ("2", D(("data", 42))),
        ("3", D(("data", 1), ("AchievementTimes", D(("0", 1_750_000_000))))))));

    [Fact]
    public void BinaryKv_RoundTripsTypes()
    {
        var parsed = SteamBinaryKv.Parse(Bytes(D(("a", D(("s", "héllo"), ("i", -5), ("f", 1.5f), ("u", 9UL))))));
        var a = (Dictionary<string, object>)parsed["A"]; // keys ignore case
        Assert.Equal("héllo", a["s"]);
        Assert.Equal(-5, a["i"]);
        Assert.Equal(1.5f, a["f"]);
        Assert.Equal(9UL, a["u"]);
    }

    [Fact]
    public void BinaryKv_BadDataThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => SteamBinaryKv.Parse(new byte[] { 1, (byte)'k' })); // unterminated key
        Assert.Throws<FormatException>(() => SteamBinaryKv.Parse(new byte[] { 9, (byte)'k', 0 })); // unknown type
        Assert.Throws<FormatException>(() => SteamBinaryKv.Parse(new byte[] { 2, (byte)'k', 0, 1 })); // truncated int
    }

    [Fact]
    public void Build_UnlockedNewestFirstThenLockedInOrder()
    {
        var set = SteamAchievementService.Build(100, SteamBinaryKv.Parse(Bytes(Schema())),
            SteamBinaryKv.Parse(Bytes(Stats())), "english")!;
        Assert.Equal(5, set.Total);
        Assert.Equal(3, set.Unlocked);
        Assert.Equal(60, set.PercentDone);
        Assert.Equal(new[] { "ACH_D", "ACH_E", "ACH_A", "ACH_B", "ACH_C" }, set.Items.Select(a => a.ApiName));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000).UtcDateTime, set.Items[0].UnlockedUtc);
        Assert.True(set.Items[4].Hidden);
        Assert.False(set.Items[4].Unlocked);
        Assert.Equal(Icon1, set.Items[0].Icon);
        Assert.Equal(Icon2, set.Items[0].IconGray);
    }

    [Fact]
    public void Build_UsesSteamLanguageWithEnglishFallback()
    {
        var schema = SteamBinaryKv.Parse(Bytes(Schema()));
        var set = SteamAchievementService.Build(100, schema, null, "german")!;
        Assert.Equal("Erste", set.Items.First(a => a.ApiName == "ACH_A").Name);
        Assert.Equal("Second", set.Items.First(a => a.ApiName == "ACH_B").Name);
    }

    [Fact]
    public void Build_NoStatsFileMeansNothingUnlocked()
    {
        var set = SteamAchievementService.Build(100, SteamBinaryKv.Parse(Bytes(Schema())), null, "english")!;
        Assert.Equal(0, set.Unlocked);
        Assert.Equal(new[] { "ACH_A", "ACH_B", "ACH_C", "ACH_D", "ACH_E" }, set.Items.Select(a => a.ApiName));
    }

    [Fact]
    public void Build_NoAchievementBlocksReturnsNull()
    {
        var schema = SteamBinaryKv.Parse(Bytes(D(("100", D(("stats", D(("1", D(("type", "INT"))))))))));
        Assert.Null(SteamAchievementService.Build(100, schema, null, "english"));
    }

    [Fact]
    public void Build_RejectsIconNamesThatArentSteamHashes()
    {
        var bit = Bit("ACH_X", "X", "x");
        ((Dictionary<string, object>)bit["display"])["icon"] = @"..\..\evil.jpg";
        var schema = SteamBinaryKv.Parse(Bytes(D(("100", D(("stats", D(("1", D(("bits", D(("0", bit))))))))))));
        var set = SteamAchievementService.Build(100, schema, null, "english")!;
        Assert.Null(set.Items[0].Icon);
        Assert.Null(SteamAchievementService.TryGetCachedIcon(100, @"..\..\evil.jpg"));
    }

    [Fact]
    public void Rarity_ParsesStringAndNumberPercents()
    {
        var map = SteamAchievementService.ParseRarity(
            "{\"achievementpercentages\":{\"achievements\":[{\"name\":\"ACH_A\",\"percent\":\"34.8\"},{\"name\":\"ACH_B\",\"percent\":5.25}]}}")!;
        Assert.Equal(34.8, map["ACH_A"], 3);
        Assert.Equal(5.25, map["ACH_B"], 3);
        Assert.Null(SteamAchievementService.ParseRarity("not json"));

        var set = SteamAchievementService.Build(100, SteamBinaryKv.Parse(Bytes(Schema())), null, "english")!;
        var withRarity = SteamAchievementService.WithRarity(set, map);
        Assert.True(withRarity.HasRarity);
        Assert.Equal(34.8, withRarity.Items.First(a => a.ApiName == "ACH_A").Percent!.Value, 3);
        Assert.Null(withRarity.Items.First(a => a.ApiName == "ACH_C").Percent);
    }

    [Fact]
    public void LoginUsers_PicksMostRecentAccount()
    {
        const string vdf = "\"users\"\n{\n\t\"76561197960265729\"\n\t{\n\t\t\"AccountName\"\t\t\"a\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t}\n"
                         + "\t\"76561197966273714\"\n\t{\n\t\t\"AccountName\"\t\t\"b\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t}\n}";
        Assert.Equal(6007986u, SteamAchievementService.ParseLoginUsers(vdf));
        Assert.Null(SteamAchievementService.ParseLoginUsers("\"users\"\n{\n}"));
    }
}
