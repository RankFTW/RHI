using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Renaming a game moves its install records (DXVK, RE Framework, OptiScaler, DC, ASI Loader...).</summary>
public class RecordRenameTests
{
    private static int Rename(List<AuxInstalledRecord> db, string oldName, string newName, string? path = null) =>
        RecordRename.Apply(db, oldName, newName, path, r => r.GameName, (r, v) => r.GameName = v, r => r.InstallPath,
            (a, b) => RecordRename.SamePath(a.InstallPath, b.InstallPath) && a.AddonType.Equals(b.AddonType, StringComparison.OrdinalIgnoreCase));

    private static AuxInstalledRecord Rec(string name, string path, string type) =>
        new() { GameName = name, InstallPath = path, AddonType = type };

    [Fact]
    public void Rename_MovesEveryRecordOfTheOldName()
    {
        var db = new List<AuxInstalledRecord>
        {
            Rec("Old Game", @"D:\Games\Old", "OptiScaler"),
            Rec("old game", @"D:\Games\Old", "DisplayCommander"),
            Rec("Other", @"D:\Games\Other", "OptiScaler"),
        };
        Assert.Equal(2, Rename(db, "Old Game", "New Game"));
        Assert.DoesNotContain(db, r => r.GameName.Equals("Old Game", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, db.Count(r => r.GameName == "New Game"));
        Assert.Equal("Other", db[2].GameName);
    }

    [Fact]
    public void Rename_WithPath_LeavesTheOtherStoresCopy()
    {
        var db = new List<AuxInstalledRecord>
        {
            Rec("Game", @"D:\Steam\Game", "OptiScaler"),
            Rec("Game", @"E:\Xbox\Game\Content", "OptiScaler"),
        };
        Assert.Equal(1, Rename(db, "Game", "Game (Steam)", @"d:/steam/game/"));
        Assert.Equal("Game (Steam)", db[0].GameName);
        Assert.Equal("Game", db[1].GameName);
    }

    [Fact]
    public void Rename_RecordAlreadyUnderNewName_WinsAndOldIsDropped()
    {
        var db = new List<AuxInstalledRecord>
        {
            Rec("Old", @"D:\G", "OptiScaler"),
            Rec("New", @"D:\G", "OptiScaler"),
            Rec("Old", @"D:\G", "AsiLoader"),
        };
        Assert.Equal(2, Rename(db, "Old", "New"));
        Assert.Equal(2, db.Count);
        Assert.All(db, r => Assert.Equal("New", r.GameName));
    }
}
