using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Records left in a game's old folder (each Game Pass update made one) are dropped only when safe.</summary>
public class StaleRecordsTests
{
    private sealed record Rec(string Game, string Type, string Path);

    private static readonly HashSet<string> OnDisk = new(StringComparer.OrdinalIgnoreCase)
    {
        @"D:\xbox\Forza Horizon 6\Content", @"D:\Steam\Portal 2", @"D:\Steam\Portal 2\bin\win64",
    };

    private static List<Rec> Clean(List<Rec> list, out List<Rec> removed) =>
        StaleRecords.WithoutStaleDuplicates(list, r => r.Game + "|" + r.Type, r => r.Path, OnDisk.Contains, out removed);

    [Fact]
    public void OldVersionFolders_DroppedWhenCurrentFolderHasARecord()
    {
        var current = new Rec("Forza Horizon 6", "ReShade", @"D:\xbox\Forza Horizon 6\Content");
        var list = new List<Rec>
        {
            new("Forza Horizon 6", "ReShade", @"C:\Program Files\WindowsApps\Microsoft.ForteBaseGame_3.440.853.0_x64__8wekyb3d8bbwe"),
            new("Forza Horizon 6", "ReShade", @"C:\Program Files\WindowsApps\Microsoft.ForteBaseGame_3.461.691.0_x64__8wekyb3d8bbwe"),
            current,
        };
        var kept = Clean(list, out var removed);
        Assert.Equal(2, removed.Count);
        Assert.Equal(current, Assert.Single(kept));
    }

    [Fact]
    public void OnlyRecordForAComponent_KeptEvenIfFolderIsGone()
    {
        // Drive unplugged, or the game moved: reconciliation / the user decides, not this cleanup
        var list = new List<Rec>
        {
            new("Valor Mortis", "UltimateAsiLoader", @"C:\Program Files\WindowsApps\LyricalGamesLLC.ValorMortis_1.3.4861.0_x64__72fag5ekhk96y"),
            new("Valor Mortis", "ReShade", @"E:\Unplugged\Valor Mortis"),
            new("Forza Horizon 6", "DisplayCommander", @"C:\Gone"),
            new("Forza Horizon 6", "ReShade", @"D:\xbox\Forza Horizon 6\Content"),
        };
        Clean(list, out var removed);
        Assert.Empty(removed);
    }

    [Fact]
    public void TwoLiveFolders_BothKept()
    {
        // Garry's Mod style: the same component in the game folder and a subfolder
        var list = new List<Rec>
        {
            new("Portal 2", "ReShade", @"D:\Steam\Portal 2"),
            new("Portal 2", "ReShade", @"D:\Steam\Portal 2\bin\win64"),
            new("Portal 2", "ReShade", ""),
        };
        Clean(list, out var removed);
        Assert.Empty(removed);
    }
}
