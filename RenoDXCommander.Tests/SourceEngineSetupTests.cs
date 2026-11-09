using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>RenoDX Source engine mod: folder layout detection and the DXVK proxy file edits.</summary>
public class SourceEngineSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi_source_" + Guid.NewGuid().ToString("N"));

    public SourceEngineSetupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A minimal file with a PE header for the given machine type.</summary>
    private static void WriteExe(string path, ushort machine)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[0x100];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        BitConverter.GetBytes(0x00004550u).CopyTo(bytes, 0x80);
        BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);
        File.WriteAllBytes(path, bytes);
    }

    private void Touch(string rel)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
    }

    [Fact]
    public void ThirtyTwoBitGame_UsesBin()
    {
        WriteExe(Path.Combine(_root, "hl2.exe"), 0x014C);
        Touch(@"bin\shaderapidx9.dll");
        var t = Assert.Single(SourceEngineSetup.DetectTargets(_root));
        Assert.Equal(Path.Combine(_root, "bin"), t.Dir);
        Assert.False(t.Is64Bit);
        Assert.Equal("bin", SourceEngineSetup.PrimarySubfolder(_root));
    }

    [Fact]
    public void SixtyFourBitGame_UsesMainFolder()
    {
        WriteExe(Path.Combine(_root, "tf_win64.exe"), 0x8664);
        Touch(@"bin\x64\shaderapidx9.dll");
        var t = Assert.Single(SourceEngineSetup.DetectTargets(_root));
        Assert.Equal(_root, t.Dir);
        Assert.True(t.Is64Bit);
        Assert.Null(SourceEngineSetup.PrimarySubfolder(_root));
    }

    [Fact]
    public void GarrysMod_GetsBothBuilds_NothingInTheMainFolder()
    {
        // The real layout: both exes in the main folder, each build's engine in its own bin folder
        WriteExe(Path.Combine(_root, "gmod.exe"), 0x014C);
        WriteExe(Path.Combine(_root, "gmod_win64.exe"), 0x8664);
        Touch(@"bin\engine.dll");
        Touch(@"bin\shaderapidx9.dll");
        Touch(@"bin\win64\engine.dll");
        Touch(@"bin\win64\shaderapidx9.dll");
        var targets = SourceEngineSetup.DetectTargets(_root);
        Assert.Equal(2, targets.Count);
        Assert.Equal(new SourceEngineSetup.Target(Path.Combine(_root, @"bin\win64"), true), targets[0]);
        Assert.Equal(new SourceEngineSetup.Target(Path.Combine(_root, "bin"), false), targets[1]);
        Assert.Equal(@"bin\win64", SourceEngineSetup.PrimarySubfolder(_root));
    }

    [Fact]
    public void SecondInstall_SharesTheMainInstallsShaders_KeepsUserPaths()
    {
        var bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllLines(Path.Combine(bin, "reshade.ini"), new[]
        {
            "[GENERAL]", @"EffectSearchPaths=.\reshade-shaders\Shaders\**", @"TextureSearchPaths=D:\MyTextures\**",
        });
        // The DXVK proxy's second runtime reads ReShade2.ini (a copy ReShade made before the paths were shared)
        File.WriteAllLines(Path.Combine(bin, "ReShade2.ini"), new[]
        {
            "[GENERAL]", @"EffectSearchPaths=.\reshade-shaders\Shaders\**", @"TextureSearchPaths=.\reshade-shaders\Textures\**",
        });
        File.WriteAllLines(Path.Combine(bin, "ReShadePreset.ini"), new[] { "Techniques=" });

        SourceEngineSetup.ShareShaderPaths(bin, Path.Combine(bin, "win64"));
        SourceEngineSetup.ShareShaderPaths(bin, Path.Combine(bin, "win64")); // runs again on update: no change
        var lines = File.ReadAllLines(Path.Combine(bin, "reshade.ini"));
        Assert.Contains(@"EffectSearchPaths=.\win64\reshade-shaders\Shaders\**", lines);
        Assert.Contains(@"TextureSearchPaths=D:\MyTextures\**", lines);
        var second = File.ReadAllLines(Path.Combine(bin, "ReShade2.ini"));
        Assert.Contains(@"EffectSearchPaths=.\win64\reshade-shaders\Shaders\**", second);
        Assert.Contains(@"TextureSearchPaths=.\win64\reshade-shaders\Textures\**", second);
        Assert.Equal(new[] { "Techniques=" }, File.ReadAllLines(Path.Combine(bin, "ReShadePreset.ini")));
    }

    [Fact]
    public void SecondRuntimeIni_MadeFromReShadeIni_WithoutProxyLines_NeverOverwritten()
    {
        File.WriteAllLines(Path.Combine(_root, "reshade.ini"), new[]
        {
            "[GENERAL]", @"EffectSearchPaths=.\reshade-shaders\Shaders\**", "Disable=1", "",
            "[SCREENSHOT]", @"SavePath=C:\Shots", "",
            "[PROXY]", "EnableProxyLibrary=1", "ProxyLibrary=dxgi_dxvk.dll",
        });
        SourceEngineSetup.EnsureSecondRuntimeIni(_root);
        var path = Path.Combine(_root, SourceEngineSetup.SecondRuntimeIni);
        var text = File.ReadAllText(path);
        Assert.Contains(@"SavePath=C:\Shots", text);
        Assert.Contains(@"EffectSearchPaths=.\reshade-shaders\Shaders\**", text);
        Assert.DoesNotContain("Disable=1", text);
        Assert.DoesNotContain("[PROXY]", text);
        Assert.True(SourceEngineSetup.IsOwned(_root, SourceEngineSetup.SecondRuntimeIni));

        File.WriteAllText(path, "user edits");
        SourceEngineSetup.EnsureSecondRuntimeIni(_root);
        Assert.Equal("user edits", File.ReadAllText(path));

        SourceEngineSetup.RemoveOwnedFile(_root, SourceEngineSetup.SecondRuntimeIni);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ThirtyTwoBitGame_WithA64BitBuildInBinWin64_GetsBoth()
    {
        WriteExe(Path.Combine(_root, "game.exe"), 0x014C);
        Touch(@"bin\engine.dll");
        WriteExe(Path.Combine(_root, @"bin\win64\game.exe"), 0x8664);
        Touch(@"bin\win64\shaderapidx9.dll");
        var targets = SourceEngineSetup.DetectTargets(_root);
        Assert.Equal(2, targets.Count);
        Assert.Equal(new SourceEngineSetup.Target(Path.Combine(_root, "bin"), false), targets[0]);
        Assert.Equal(new SourceEngineSetup.Target(Path.Combine(_root, @"bin\win64"), true), targets[1]);
    }

    [Fact]
    public void IgnoresUninstallerAndMissingEngine()
    {
        WriteExe(Path.Combine(_root, "unins000.exe"), 0x8664);
        WriteExe(Path.Combine(_root, "game.exe"), 0x014C);
        // No bin\ engine: stays in the main folder
        var t = Assert.Single(SourceEngineSetup.DetectTargets(_root));
        Assert.Equal(_root, t.Dir);
        Assert.False(t.Is64Bit);
    }

    [Fact]
    public void ReShadeIni_AddsAndRemovesProxyLines_KeepingTheRest()
    {
        var ini = Path.Combine(_root, "reshade.ini");
        File.WriteAllLines(ini, new[] { "[ADDON]", "DisabledAddons=", "", "[GENERAL]", "PresetPath=.\\ReShadePreset.ini", "", "[INPUT]", "KeyOverlay=36,0,0,0" });

        SourceEngineSetup.ApplyReShadeIni(_root);
        SourceEngineSetup.ApplyReShadeIni(_root); // twice: no duplicates
        var lines = File.ReadAllLines(ini);
        Assert.Single(lines, l => l == "Disable=1");
        Assert.Single(lines, l => l == "[PROXY]");
        Assert.Contains("EnableProxyLibrary=1", lines);
        Assert.Contains("ProxyLibrary=dxgi_dxvk.dll", lines);
        // Disable=1 sits in [GENERAL], before the blank line and [INPUT]
        int general = Array.IndexOf(lines, "[GENERAL]");
        Assert.Equal("Disable=1", lines[general + 2]);
        Assert.Equal("", lines[general + 3]);
        Assert.Equal("[INPUT]", lines[general + 4]);

        SourceEngineSetup.RemoveReShadeIni(_root);
        lines = File.ReadAllLines(ini);
        Assert.DoesNotContain("[PROXY]", lines);
        Assert.DoesNotContain("Disable=1", lines);
        Assert.Contains("PresetPath=.\\ReShadePreset.ini", lines);
        Assert.Contains("KeyOverlay=36,0,0,0", lines);
    }

    [Fact]
    public void ReShadeIni_CreatedWhenMissing()
    {
        SourceEngineSetup.ApplyReShadeIni(_root);
        var lines = File.ReadAllLines(Path.Combine(_root, "reshade.ini"));
        Assert.Equal(new[] { "[GENERAL]", "Disable=1", "", "[PROXY]", "EnableProxyLibrary=1", "ProxyLibrary=dxgi_dxvk.dll" }, lines);
    }

    [Fact]
    public void DxvkConf_MergesAndCleansUp()
    {
        var conf = Path.Combine(_root, "dxvk.conf");
        File.WriteAllLines(conf, new[] { "dxvk.hud = fps", "d3d9.deferSurfaceCreation = False" });
        SourceEngineSetup.EnsureDxvkConf(_root);
        var lines = File.ReadAllLines(conf);
        Assert.Equal(new[] { "dxvk.hud = fps", "d3d9.deferSurfaceCreation = True", "dxgi.deferSurfaceCreation = True" }, lines);

        SourceEngineSetup.RemoveDxvkConf(_root);
        Assert.Equal(new[] { "dxvk.hud = fps" }, File.ReadAllLines(conf));

        File.Delete(conf);
        SourceEngineSetup.EnsureDxvkConf(_root);
        SourceEngineSetup.RemoveDxvkConf(_root);
        Assert.False(File.Exists(conf)); // only our lines: file removed
    }

    [Fact]
    public void OwnedFile_RestoresWhatItReplaced()
    {
        var src = Path.Combine(_root, "src.dll");
        File.WriteAllText(src, "dxvk");
        var dir = Path.Combine(_root, "game");
        Directory.CreateDirectory(dir);

        // Nothing there before: removed completely
        SourceEngineSetup.DeployOwnedFile(dir, "dxgi_dxvk.dll", src);
        Assert.True(SourceEngineSetup.IsOwned(dir, "dxgi_dxvk.dll"));
        SourceEngineSetup.RemoveOwnedFile(dir, "dxgi_dxvk.dll");
        Assert.False(File.Exists(Path.Combine(dir, "dxgi_dxvk.dll")));
        Assert.False(SourceEngineSetup.IsOwned(dir, "dxgi_dxvk.dll"));

        // A user's file: put back
        File.WriteAllText(Path.Combine(dir, "d3d9.dll"), "user");
        SourceEngineSetup.DeployOwnedFile(dir, "d3d9.dll", src);
        SourceEngineSetup.DeployOwnedFile(dir, "d3d9.dll", src); // update keeps the first backup
        Assert.Equal("dxvk", File.ReadAllText(Path.Combine(dir, "d3d9.dll")));
        SourceEngineSetup.RemoveOwnedFile(dir, "d3d9.dll");
        Assert.Equal("user", File.ReadAllText(Path.Combine(dir, "d3d9.dll")));
    }

    [Theory]
    [InlineData(null, "-insecure")]
    [InlineData("", "-insecure")]
    [InlineData("-novid", "-novid -insecure")]
    [InlineData("-novid -insecure", "-novid -insecure")]
    [InlineData("-INSECURE", "-INSECURE")]
    public void LaunchArg_Added(string? before, string after) =>
        Assert.Equal(after, SourceEngineSetup.AddLaunchArg(before));

    [Theory]
    [InlineData("-insecure", "")]
    [InlineData("-novid -insecure -w 1920", "-novid -w 1920")]
    [InlineData("-novid", "-novid")]
    public void LaunchArg_Removed(string before, string after) =>
        Assert.Equal(after, SourceEngineSetup.RemoveLaunchArg(before));
}
