using System.IO.Compression;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>RenoDX mods whose snapshot is a .zip (Batman: Arkham City: addon32 + a 64-bit helper exe).</summary>
public class RenoDxZipPackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-zip-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _game;

    public RenoDxZipPackageTests()
    {
        _game = Path.Combine(_dir, "game");
        Directory.CreateDirectory(_game);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string MakeZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_dir, "renodx-batmanac.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(content);
        }
        return path;
    }

    [Theory]
    [InlineData("https://github.com/ldavid1994/renodx/releases/download/batmanac-snapshot/renodx-batmanac.zip", true)]
    [InlineData("https://example.com/x/mod.ZIP?raw=1", true)]
    [InlineData("https://clshortfuse.github.io/renodx/renodx-batmanak.addon64", false)]
    [InlineData(null, false)]
    public void IsZip(string? url, bool expected) => Assert.Equal(expected, RenoDxZipPackage.IsZip(url));

    [Fact]
    public void AddonName_Known_And_Default()
    {
        Assert.Equal("renodx-batmanac.addon32", RenoDxZipPackage.AddonNameFor("renodx-batmanac.zip"));
        Assert.Equal("renodx-other.addon64", RenoDxZipPackage.AddonNameFor("renodx-other.zip"));
    }

    [Fact]
    public void GameMod_AddonFileName_Is_The_Addon_Inside_The_Zip()
    {
        var mod = new GameMod { SnapshotUrl = "https://github.com/ldavid1994/renodx/releases/download/batmanac-snapshot/renodx-batmanac.zip" };
        Assert.Equal("renodx-batmanac.addon32", mod.AddonFileName);
        var plain = new GameMod { SnapshotUrl = "https://clshortfuse.github.io/renodx/renodx-batmanak.addon64" };
        Assert.Equal("renodx-batmanak.addon64", plain.AddonFileName);
    }

    [Fact]
    public void Extract_Deploys_Runtime_Files_Skips_Docs_And_Lists_Extras()
    {
        var zip = MakeZip(("renodx-batmanac.addon32", "addon"), ("renodx-batmanac-dlaa-host64.exe", "exe"), ("README.md", "docs"));
        var addon = RenoDxZipPackage.Extract(zip, _game);

        Assert.Equal("renodx-batmanac.addon32", addon);
        Assert.True(File.Exists(Path.Combine(_game, "renodx-batmanac.addon32")));
        Assert.True(File.Exists(Path.Combine(_game, "renodx-batmanac-dlaa-host64.exe")));
        Assert.False(File.Exists(Path.Combine(_game, "README.md")));
        Assert.True(RenoDxZipPackage.IsListed(_game, addon, "renodx-batmanac-dlaa-host64.exe"));
        Assert.False(RenoDxZipPackage.IsListed(_game, addon, "renodx-batmanac.addon32")); // the addon itself is the record's
    }

    [Fact]
    public void Extract_Rejects_Paths_Outside_The_Folder()
    {
        var zip = MakeZip(("renodx-batmanac.addon32", "addon"), ("..\\..\\evil.dll", "x"));
        RenoDxZipPackage.Extract(zip, _game);
        Assert.False(File.Exists(Path.Combine(_dir, "evil.dll")));
    }

    [Fact]
    public void Extract_Without_An_Addon_Throws()
    {
        var zip = MakeZip(("helper.exe", "exe"));
        Assert.Throws<InvalidOperationException>(() => RenoDxZipPackage.Extract(zip, _game));
    }

    [Fact]
    public void Reinstall_Keeps_Files_Other_Steps_Listed()
    {
        var zip = MakeZip(("renodx-batmanac.addon32", "addon"), ("renodx-batmanac-dlaa-host64.exe", "exe"));
        var addon = RenoDxZipPackage.Extract(zip, _game);
        RenoDxZipPackage.AddToList(_game, addon, new[] { "nvngx_dlss.dll" });
        RenoDxZipPackage.Extract(zip, _game); // an update re-extracts
        Assert.True(RenoDxZipPackage.IsListed(_game, addon, "nvngx_dlss.dll"));
    }

    [Fact]
    public void RemoveFiles_Deletes_Listed_Files_And_Restores_Backups()
    {
        var zip = MakeZip(("renodx-batmanac.addon32", "addon"), ("renodx-batmanac-dlaa-host64.exe", "exe"));
        var addon = RenoDxZipPackage.Extract(zip, _game);

        // RHI placed nvngx_dlss.dll from scratch (0-byte sentinel); the game's own d3dx.dll was backed up first
        File.WriteAllText(Path.Combine(_game, "nvngx_dlss.dll"), "dlss");
        File.WriteAllBytes(Path.Combine(_game, "nvngx_dlss.dll.original"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(_game, "d3dx.dll"), "rhi copy");
        File.WriteAllText(Path.Combine(_game, "d3dx.dll.original"), "game's own");
        RenoDxZipPackage.AddToList(_game, addon, new[] { "nvngx_dlss.dll", "d3dx.dll" });
        File.WriteAllText(Path.Combine(_game, "unrelated.dll"), "user");

        RenoDxZipPackage.RemoveFiles(_game, addon);

        Assert.False(File.Exists(Path.Combine(_game, "renodx-batmanac-dlaa-host64.exe")));
        Assert.False(File.Exists(Path.Combine(_game, "nvngx_dlss.dll")));
        Assert.False(File.Exists(Path.Combine(_game, "nvngx_dlss.dll.original")));
        Assert.Equal("game's own", File.ReadAllText(Path.Combine(_game, "d3dx.dll")));
        Assert.True(File.Exists(Path.Combine(_game, "unrelated.dll")));
        Assert.False(File.Exists(RenoDxZipPackage.ListPath(_game, addon)));
    }
}
