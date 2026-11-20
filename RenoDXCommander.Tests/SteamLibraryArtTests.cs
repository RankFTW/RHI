using System.Text;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class SteamLibraryArtTests
{
    // Binary KeyValues as Steam writes shortcuts.vdf
    private static byte[] Vdf(params (int AppId, string Name, string Exe)[] shortcuts)
    {
        var b = new List<byte>();
        void Str(string s) { b.AddRange(Encoding.UTF8.GetBytes(s)); b.Add(0); }
        b.Add(0x00); Str("shortcuts");
        for (int i = 0; i < shortcuts.Length; i++)
        {
            b.Add(0x00); Str(i.ToString());
            b.Add(0x02); Str("appid"); b.AddRange(BitConverter.GetBytes(shortcuts[i].AppId));
            b.Add(0x01); Str("AppName"); Str(shortcuts[i].Name);
            b.Add(0x01); Str("Exe"); Str("\"" + shortcuts[i].Exe + "\"");
            b.Add(0x01); Str("StartDir"); Str("\"" + Path.GetDirectoryName(shortcuts[i].Exe) + "\\\"");
            b.Add(0x02); Str("IsHidden"); b.AddRange(BitConverter.GetBytes(0));
            b.Add(0x07); Str("LastPlayTime"); b.AddRange(BitConverter.GetBytes(0L));
            b.Add(0x00); Str("tags"); b.Add(0x01); Str("0"); Str("Favorite"); b.Add(0x08);
            b.Add(0x08);
        }
        b.Add(0x08); b.Add(0x08);
        return b.ToArray();
    }

    [Fact]
    public void ParseShortcuts_ReadsIdsNamesAndExes()
    {
        var list = SteamLibraryArtService.ParseShortcuts(Vdf(
            (-1234567890, "Assassin's Creed Black Flag Resynced", @"D:\Ubisoft\ACBF\ACBlackFlag.exe"),
            (987654321, "Some Game", @"E:\Games\Some Game\game.exe")));

        Assert.Equal(2, list.Count);
        Assert.Equal(unchecked((uint)-1234567890), list[0].AppId); // grid files use the unsigned id
        Assert.Equal("Assassin's Creed Black Flag Resynced", list[0].AppName);
        Assert.Equal("\"D:\\Ubisoft\\ACBF\\ACBlackFlag.exe\"", list[0].Exe);
        Assert.Equal(987654321u, list[1].AppId);
    }

    [Fact]
    public void ParseShortcuts_DamagedFileGivesWhatWasRead()
    {
        var data = Vdf((5, "A", @"C:\A\a.exe"), (6, "B", @"C:\B\b.exe"));
        for (int cut = 1; cut < data.Length; cut++)
        {
            var list = SteamLibraryArtService.ParseShortcuts(data[..(data.Length - cut)]); // never throws
            if (list.Count > 0) Assert.Equal(5u, list[0].AppId);
        }
        Assert.Empty(SteamLibraryArtService.ParseShortcuts(Array.Empty<byte>()));
    }

    [Fact]
    public void MatchShortcuts_ByFolderOrLooseName()
    {
        var list = SteamLibraryArtService.ParseShortcuts(Vdf(
            (1, "AC Black Flag (Ubisoft)", @"D:\Ubisoft\ACBF\ACBlackFlag.exe"),
            (2, "Assassins Creed: Black Flag Resynced", @"C:\Program Files (x86)\Ubisoft\UbisoftConnect.exe"),
            (3, "Other", @"D:\Ubisoft\ACBF2\other.exe")));

        var ids = SteamLibraryArtService.MatchShortcuts(list, "Assassin's Creed Black Flag Resynced", @"D:\Ubisoft\ACBF\")
            .Select(s => s.AppId).ToList();
        Assert.Equal(new uint[] { 1, 2 }, ids); // ACBF2 isn't inside ACBF

        Assert.Empty(SteamLibraryArtService.MatchShortcuts(list, "Unrelated", null));
    }
}
