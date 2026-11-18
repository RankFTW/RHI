using System.Text;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Real names for Steam games whose app manifest only has "appid_123" (appinfo.vdf reader).</summary>
public class SteamAppInfoReaderTests
{
    [Theory]
    [InlineData("appid_244160", 244160, true)]
    [InlineData("APPID_244160", 244160, true)]
    [InlineData("", 244160, true)]
    [InlineData(null, 244160, true)]
    [InlineData("appid_244160", 999, false)]   // a different app's number: a real name that happens to look like one
    [InlineData("appid_", 244160, false)]
    [InlineData("appid_12ab", 244160, false)]
    [InlineData("Homeworld Remastered Collection", 244160, false)]
    public void IsPlaceholderName(string? name, int appId, bool expected) =>
        Assert.Equal(expected, SteamAppInfoReader.IsPlaceholderName(name, appId));

    // ── Test data: appinfo › { appid, common › { type, name } } ─────────────

    private static void Str(List<byte> b, string s) { b.AddRange(Encoding.UTF8.GetBytes(s)); b.Add(0); }

    private static byte[] AppKv(Action<List<byte>, string> key)
    {
        var b = new List<byte>();
        b.Add(0); key(b, "appinfo");
        b.Add(2); key(b, "appid"); b.AddRange(BitConverter.GetBytes(244160));
        b.Add(7); key(b, "changenumber"); b.AddRange(BitConverter.GetBytes(12345UL));
        b.Add(0); key(b, "common");
        b.Add(1); key(b, "type"); Str(b, "Game");
        b.Add(1); key(b, "name"); Str(b, "Homeworld Remastered Collection");
        b.Add(8);
        b.Add(8);
        b.Add(8);
        return b.ToArray();
    }

    [Fact]
    public void FindName_InlineKeys()
    {
        var kv = AppKv((b, k) => Str(b, k));
        Assert.Equal("Homeworld Remastered Collection", SteamAppInfoReader.FindName(kv, null));
    }

    [Fact]
    public void FindName_StringTableKeys()
    {
        var table = new List<string> { "appinfo", "appid", "changenumber", "common", "type", "name" };
        var kv = AppKv((b, k) => b.AddRange(BitConverter.GetBytes(table.IndexOf(k))));
        Assert.Equal("Homeworld Remastered Collection", SteamAppInfoReader.FindName(kv, table));
    }

    [Fact]
    public void FindName_IgnoresNameOutsideCommon()
    {
        var b = new List<byte>();
        b.Add(0); Str(b, "appinfo");
        b.Add(0); Str(b, "extended");
        b.Add(1); Str(b, "name"); Str(b, "Wrong");
        b.Add(8);
        b.Add(8);
        Assert.Null(SteamAppInfoReader.FindName(b.ToArray(), null));
    }

    [Fact]
    public void ReadNames_Version41File()
    {
        var table = new List<string> { "appinfo", "appid", "changenumber", "common", "type", "name" };
        var kv = AppKv((b, k) => b.AddRange(BitConverter.GetBytes(table.IndexOf(k))));

        var file = new List<byte>();
        file.AddRange(BitConverter.GetBytes(0x07564429u));
        file.AddRange(BitConverter.GetBytes(1u));
        int offsetAt = file.Count;
        file.AddRange(new byte[8]); // string table offset, filled in below
        // An app that isn't wanted, skipped by size
        file.AddRange(BitConverter.GetBytes(10u));
        file.AddRange(BitConverter.GetBytes(60u + 3));
        file.AddRange(new byte[60 + 3]);
        // The wanted app
        file.AddRange(BitConverter.GetBytes(244160u));
        file.AddRange(BitConverter.GetBytes((uint)(60 + kv.Length)));
        file.AddRange(new byte[60]);
        file.AddRange(kv);
        file.AddRange(BitConverter.GetBytes(0u)); // end
        long tableOffset = file.Count;
        file.AddRange(BitConverter.GetBytes((uint)table.Count));
        foreach (var s in table) Str(file, s);
        var bytes = file.ToArray();
        BitConverter.GetBytes(tableOffset).CopyTo(bytes, offsetAt);

        var path = Path.Combine(Path.GetTempPath(), $"rhi_appinfo_test_{Guid.NewGuid():N}.vdf");
        try
        {
            File.WriteAllBytes(path, bytes);
            var names = SteamAppInfoReader.ReadNames(path, new[] { 244160, 777 });
            Assert.Single(names);
            Assert.Equal("Homeworld Remastered Collection", names[244160]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadNames_MissingFile_Empty() =>
        Assert.Empty(SteamAppInfoReader.ReadNames(@"Z:\does\not\exist\appinfo.vdf", new[] { 1 }));
}
