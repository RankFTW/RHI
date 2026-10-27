using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// The folder fingerprint lets BuildCards reuse an "Unknown engine" result between launches.
/// It must stay the same for an unchanged folder and change when the layout engine detection
/// looks at changes (e.g. a still-downloading game gaining Binaries\Win64).
/// </summary>
public class EngineFolderStampTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-stamp-" + Guid.NewGuid().ToString("N"));

    public EngineFolderStampTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Game", "Content"));
        File.WriteAllText(Path.Combine(_root, "game.exe"), "x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    [Fact]
    public void Unchanged_SameStamp()
    {
        var a = EngineFolderStamp.Compute(_root);
        Assert.NotNull(a);
        Assert.Equal(a, EngineFolderStamp.Compute(_root));
    }

    [Fact]
    public void NewFolderTwoLevelsDown_ChangesStamp()
    {
        var before = EngineFolderStamp.Compute(_root);
        Directory.CreateDirectory(Path.Combine(_root, "Game", "Binaries"));
        Assert.NotEqual(before, EngineFolderStamp.Compute(_root));
    }

    [Fact]
    public void NewExe_ChangesStamp()
    {
        var before = EngineFolderStamp.Compute(_root);
        File.WriteAllText(Path.Combine(_root, "Game", "launcher.exe"), "x");
        Assert.NotEqual(before, EngineFolderStamp.Compute(_root));
    }

    [Fact]
    public void RhiShaderFoldersAndFileWrites_DoNotChangeStamp()
    {
        var before = EngineFolderStamp.Compute(_root);
        // RHI rewrites reshade-shaders every launch; ReShade writes logs/ini next to the exe.
        Directory.CreateDirectory(Path.Combine(_root, "reshade-shaders", "Shaders"));
        Directory.CreateDirectory(Path.Combine(_root, "Game", "reshade-shaders", "Textures"));
        File.WriteAllText(Path.Combine(_root, "ReShade.log"), "x");
        File.WriteAllText(Path.Combine(_root, "reshade.ini"), "x");
        Assert.Equal(before, EngineFolderStamp.Compute(_root));
    }

    [Fact]
    public void MissingFolder_ReturnsNull()
    {
        Assert.Null(EngineFolderStamp.Compute(Path.Combine(_root, "nope")));
    }
}
