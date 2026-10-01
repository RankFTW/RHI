using System.Text;
using System.Text.Json;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class DlssTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-dlss-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    private readonly string? _oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    public DlssTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_root, "data"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(_root, "cache"));
    }
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", _oldData);
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", _oldCache);
        Directory.Delete(_root, true);
    }
    private string FileAt(string path, byte[] content)
    {
        var full = Path.Combine(_root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllBytes(full, content); return full;
    }

    // A minimal PE32+ DLL whose only section holds an RT_VERSION resource.
    private static byte[] PeWithVersion(ushort major, ushort minor, ushort build, ushort revision, ushort machine = 0x8664)
    {
        var pe = new byte[0x400];
        pe[0] = (byte)'M'; pe[1] = (byte)'Z'; BitConverter.GetBytes(0x80).CopyTo(pe, 0x3c);
        "PE\0\0"u8.ToArray().CopyTo(pe, 0x80);
        var coff = 0x84;
        BitConverter.GetBytes(machine).CopyTo(pe, coff); BitConverter.GetBytes((ushort)1).CopyTo(pe, coff + 2);
        BitConverter.GetBytes((ushort)240).CopyTo(pe, coff + 16); BitConverter.GetBytes((ushort)0x2022).CopyTo(pe, coff + 18);
        var opt = coff + 20;
        BitConverter.GetBytes((ushort)0x20B).CopyTo(pe, opt);
        BitConverter.GetBytes(0x180000000UL).CopyTo(pe, opt + 24);
        BitConverter.GetBytes(0x1000).CopyTo(pe, opt + 32); BitConverter.GetBytes(0x200).CopyTo(pe, opt + 36);
        BitConverter.GetBytes((ushort)6).CopyTo(pe, opt + 40); BitConverter.GetBytes((ushort)6).CopyTo(pe, opt + 48);
        BitConverter.GetBytes(0x2000).CopyTo(pe, opt + 56); BitConverter.GetBytes(0x200).CopyTo(pe, opt + 60);
        BitConverter.GetBytes((ushort)2).CopyTo(pe, opt + 68); BitConverter.GetBytes(16).CopyTo(pe, opt + 108);
        BitConverter.GetBytes(0x1000).CopyTo(pe, opt + 112 + 16); BitConverter.GetBytes(0x100).CopyTo(pe, opt + 112 + 20);
        var section = opt + 240;
        ".rsrc"u8.ToArray().CopyTo(pe, section);
        BitConverter.GetBytes(0x200).CopyTo(pe, section + 8); BitConverter.GetBytes(0x1000).CopyTo(pe, section + 12);
        BitConverter.GetBytes(0x200).CopyTo(pe, section + 16); BitConverter.GetBytes(0x200).CopyTo(pe, section + 20);
        BitConverter.GetBytes(0x40000040).CopyTo(pe, section + 36);
        var rsrc = 0x200;
        void Directory(int at, uint id, uint target) { BitConverter.GetBytes((ushort)1).CopyTo(pe, rsrc + at + 14); BitConverter.GetBytes(id).CopyTo(pe, rsrc + at + 16); BitConverter.GetBytes(target).CopyTo(pe, rsrc + at + 20); }
        Directory(0x00, 16, 0x80000018); Directory(0x18, 1, 0x80000030); Directory(0x30, 0x409, 0x48);
        BitConverter.GetBytes(0x1000 + 0x58).CopyTo(pe, rsrc + 0x48); BitConverter.GetBytes(0x60).CopyTo(pe, rsrc + 0x4C);
        var info = rsrc + 0x58 + 40;
        BitConverter.GetBytes(0xFEEF04BD).CopyTo(pe, info); BitConverter.GetBytes(0x00010000).CopyTo(pe, info + 4);
        BitConverter.GetBytes((uint)(major << 16 | minor)).CopyTo(pe, info + 8); BitConverter.GetBytes((uint)(build << 16 | revision)).CopyTo(pe, info + 12);
        return pe;
    }
    private static Game GameAt(string root, string exe) => new() { Name = "Fixture", Root = root, Executable = exe, Executables = [exe] };

    [Fact] public void ReadsWindowsFileVersionFromPeResources()
    {
        var dll = FileAt("nvngx_dlss.dll", PeWithVersion(310, 9, 1, 0));
        Assert.Equal("310.9.1.0", PeVersion.Read(dll));
        Assert.Equal("310.9.1", PeVersion.Format(PeVersion.Read(dll)));
        Assert.Equal("2.7.32.1", PeVersion.Format("2.7.32.1"));
        Assert.Null(PeVersion.Read(FileAt("text.dll", Encoding.UTF8.GetBytes("not a pe"))));
    }

    [Fact] public void DriverSettingsMergeIntoLaunchOptionsAndKeepUserEntries()
    {
        var prefs = new GamePreferences();
        DlssProfile.SetPreset(prefs, DlssKind.SR, 0x0C);
        DlssProfile.SetRenderScale(prefs, DlssKind.SR, 67);
        var extras = DlssProfile.Extras(prefs, false);
        var existing = "FOO=bar DXVK_NVAPI_DRS_SETTINGS=0x12345678=1,0x10E41DF7=0x5 %command% -dx12";
        var options = Proton.LaunchOptions(existing, "dxgi.dll", extras);
        var drs = Proton.ParseDriverSettings(Proton.ReadVariable(options, Proton.DrsVariable));
        Assert.Equal(1u, drs[0x12345678]);
        Assert.Equal(0x0Cu, drs[DlssProfile.SrPresetId]);
        Assert.Equal(2u, drs[DlssProfile.SrPresetOverrideId]);
        Assert.Equal(DlssProfile.ScaleCustom, drs[DlssProfile.SrScaleModeId]);
        Assert.Equal(67u, drs[DlssProfile.SrScaleId]);
        Assert.False(drs.ContainsKey(DlssProfile.RrPresetId)); // RHI-managed value no longer selected
        Assert.Contains("FOO=bar", options); Assert.Contains("PROTON_ENABLE_NVAPI=1", options); Assert.EndsWith("%command% -dx12", options);
        Assert.Equal(options, Proton.LaunchOptions(options, "dxgi.dll", extras));
        Assert.True(InstallationStatus.HasLaunchOverrides(options, "dxgi.dll", extras));
        Assert.True(InstallationStatus.HasExtras(options, extras));

        DlssProfile.Reset(prefs);
        var cleared = Proton.LaunchOptions(options, "dxgi.dll", DlssProfile.Extras(prefs, false));
        Assert.Equal("0x12345678=1", Proton.ReadVariable(cleared, Proton.DrsVariable));
        Assert.False(InstallationStatus.HasExtras(options, DlssProfile.Extras(prefs, false)));
    }

    [Fact] public void DriverOverrideUsesProtonNgxUpdaterAndRemovesItWhenOff()
    {
        var prefs = new GamePreferences();
        DlssProfile.SetDriverOverride(prefs, DlssKind.SR, true);
        var on = Proton.LaunchOptions("", null, DlssProfile.Extras(prefs, false));
        Assert.Contains("PROTON_ENABLE_NGX_UPDATER=1", on);
        Assert.Equal(1u, Proton.ParseDriverSettings(Proton.ReadVariable(on, Proton.DrsVariable))[DlssProfile.SrLatestId]);
        Assert.DoesNotContain("WINEDLLOVERRIDES", on); // DLSS settings alone need no ReShade DLL override
        DlssProfile.SetDriverOverride(prefs, DlssKind.SR, false);
        var off = Proton.LaunchOptions(on, null, DlssProfile.Extras(prefs, false));
        Assert.DoesNotContain("NGX_UPDATER", off); Assert.DoesNotContain(Proton.DrsVariable, off);
        Assert.Contains("PROTON_ENABLE_NVAPI=1", off); // never removes a variable the user may rely on
    }

    [Fact] public void MultiFrameGenerationAndPresetsFollowWindowsValues()
    {
        var prefs = new GamePreferences();
        DlssProfile.SetMfg(prefs, DlssProfile.MfgDynamic, 3, DlssProfile.TargetFpsMaxRefresh);
        Assert.Equal(DlssProfile.MfgDynamic, DlssProfile.Get(prefs, DlssProfile.MfgModeId));
        Assert.Equal(3u, DlssProfile.Get(prefs, DlssProfile.MfgDynamicMaxId));
        Assert.False(DlssProfile.Has(prefs, DlssProfile.MfgFactorId));
        DlssProfile.SetMfg(prefs, DlssProfile.MfgFixed, 2);
        Assert.Equal(2u, DlssProfile.Get(prefs, DlssProfile.MfgFactorId));
        Assert.False(DlssProfile.Has(prefs, DlssProfile.MfgTargetFpsId));
        DlssProfile.SetPreset(prefs, DlssKind.SR, 0x00FFFFFF);
        Assert.Equal(1u, DlssProfile.Get(prefs, DlssProfile.SrPresetOverrideId));
        Assert.Throws<ArgumentOutOfRangeException>(() => DlssProfile.SetRenderScale(prefs, DlssKind.RR, 20));
        DlssProfile.ApplyManifestPresets(JsonDocument.Parse("""{ "sr": [ { "name": "F - CNN", "value": 6 }, { "name": "J - TF1", "value": 10, "disabled": true } ] }""").RootElement);
        Assert.Contains(DlssProfile.SrPresets, p => p.Name == "F - CNN" && p.Value == 6);
        Assert.DoesNotContain(DlssProfile.SrPresets, p => p.Name == "J - TF1");
        Assert.Equal("NVIDIA Recommended", DlssProfile.SrPresets[^1].Name);
    }

    [Fact] public void SentinelBackupsMatchTheWindowsConvention()
    {
        var source = FileAt("new.dll", Encoding.UTF8.GetBytes("new"));
        var existing = FileAt("game/nvngx_dlss.dll", Encoding.UTF8.GetBytes("game original"));
        Sentinel.Deploy(source, existing); Sentinel.Deploy(source, existing);
        Assert.Equal("game original", File.ReadAllText(existing + ".original"));
        Sentinel.Restore(existing);
        Assert.Equal("game original", File.ReadAllText(existing)); Assert.False(File.Exists(existing + ".original"));

        var placed = Path.Combine(_root, "game/nvngx_dlssd.dll");
        Assert.True(Sentinel.DeployIfAbsent(source, placed));
        Assert.Equal(0, new FileInfo(placed + ".original").Length);
        Sentinel.Restore(placed);
        Assert.False(File.Exists(placed)); Assert.False(File.Exists(placed + ".original"));
        Assert.False(Sentinel.DeployIfAbsent(source, existing)); // the game's own file is left alone
    }

    [Fact] public void DetectionFindsPluginDllsAndSkipsRhiFolders()
    {
        FileAt("game/Engine/Plugins/DLSS/NVNGX_DLSS.DLL", PeWithVersion(310, 2, 0, 0));
        FileAt("game/Game/Binaries/Win64/nvngx_dlssg.dll", PeWithVersion(310, 1, 0, 0));
        FileAt("game/Game/Binaries/Win64/host64/nvngx_dlssnr.dll", PeWithVersion(310, 8, 0, 0));
        FileAt("game/Game/Binaries/Win64/sl.interposer.dll", PeWithVersion(2, 7, 32, 0));
        FileAt("game/Game/Binaries/Win64/sl.common.dll", PeWithVersion(2, 4, 0, 0));
        var detection = DlssScanner.Detect(Path.Combine(_root, "game"));
        Assert.EndsWith("NVNGX_DLSS.DLL", detection.Path(DlssKind.SR));
        Assert.Equal("310.2.0", detection.Version(DlssKind.SR));
        Assert.Equal("310.1.0", detection.Version(DlssKind.FG));
        Assert.False(detection.Has(DlssKind.NR));
        Assert.Equal("2.7.32", detection.Version(DlssKind.Streamline)); // highest sl.*.dll
    }

    [Fact] public async Task VersionSwapsSupportCustomAndDefault()
    {
        var target = FileAt("game/nvngx_dlssd.dll", PeWithVersion(310, 1, 0, 0));
        Directory.CreateDirectory(DlssFiles.CustomDirectory);
        File.WriteAllBytes(Path.Combine(DlssFiles.CustomDirectory, "nvngx_dlssd.dll"), PeWithVersion(399, 0, 0, 0));
        using var http = new HttpClient();
        var swap = new DlssSwap(new DlssCatalog(http, new Downloads(http)));
        await swap.Apply(DlssKind.RR, target, "Custom");
        var detection = DlssScanner.Detect(Path.Combine(_root, "game"));
        Assert.True(detection.IsCustom(DlssKind.RR)); Assert.Equal("399.0.0", detection.Version(DlssKind.RR)); Assert.Equal("310.1.0", detection.Original(DlssKind.RR));
        await swap.Apply(DlssKind.RR, target, "Default (310.1.0)");
        Assert.Equal("310.1.0.0", PeVersion.Read(target)); Assert.False(File.Exists(target + ".rhi_custom"));
        await Assert.ThrowsAsync<IOException>(() => swap.Apply(DlssKind.FG, target, "Custom"));
    }

    [Fact] public void ReleaseListsParseTagsAssetsAndSortPreReleases()
    {
        var json = JsonDocument.Parse("""
            [
              { "tag_name": "renodx-dlss5-8.5.0-rc5", "assets": [ { "name": "renodx-dlss5_8.5.0-rc5.zip", "browser_download_url": "https://x/rc5.zip" } ] },
              { "tag_name": "renodx-dlss5-8.5.0-rc10", "assets": [ { "name": "renodx-dlss5_8.5.0-rc10.zip", "browser_download_url": "https://x/rc10.zip" } ] },
              { "tag_name": "renodx-dlss5-4.70", "assets": [ { "name": "renodx-dlss5.addon64", "browser_download_url": "https://x/4.70.addon64" }, { "name": "a.zip", "browser_download_url": "https://x/a.zip" } ] },
              { "tag_name": "renodx-dlss-SF-0.55", "assets": [ { "name": "renodx-dlss_SF_0.55.zip", "browser_download_url": "https://x/sf.zip" } ] },
              { "tag_name": "renodx-dlss5-9.0", "draft": true, "assets": [ { "name": "d.zip", "browser_download_url": "https://x/d.zip" } ] },
              { "tag_name": "renodx-dlss5-1.0", "assets": [] }
            ]
            """).RootElement;
        var releases = AddonReleases.Sort(AddonReleases.Parse(json, "renodx-dlss5-", "renodx-dlss5.addon64"));
        Assert.Equal(["8.5.0-rc10", "8.5.0-rc5", "4.70"], releases.Select(r => r.Version));
        Assert.Equal("https://x/4.70.addon64", releases[2].Url);
        var comparer = AddonReleases.VersionComparer.Instance;
        Assert.True(comparer.Compare("v1.17.0", "v1.17.0-beta.3") > 0);
        Assert.True(comparer.Compare("v1.17.0-beta.10", "v1.17.0-beta.3") > 0);
        Assert.True(comparer.Compare("26.0922.0041", "0.55") > 0);
        Assert.Equal("310.8.0", DlssCatalog.StripSuffix("310.8.0 (50xx)"));
    }

    [Fact] public void OwnerScopedIniEditsRestoreExactlyAndIndependently()
    {
        var ini = FileAt("ReShade.ini", Encoding.UTF8.GetBytes("[GENERAL]\nPresetPath=.\\ReShadePreset.ini\n[renodx]\nSet_Path=1\n"));
        var preset = FileAt("ReShadePreset.ini", Encoding.UTF8.GetBytes("Techniques=A@A.fx\n\n[A.fx]\nX=1\n"));
        var original = File.ReadAllText(preset); var originalIni = File.ReadAllText(ini);
        IniSettings.Apply(ini, [new("renodx", "Set_Path", "0")]);
        IniSettings.Apply(ini, [new("GENERAL", "PreprocessorDefinitions", "DLSS5_MV_PROVIDER=3")], owner: "neural-rendering");
        IniSettings.Apply(preset, [new("", "Techniques", "A@A.fx,DLSS5_Feed@DLSS5_Feed.fx"), new("", "TechniqueSorting", "DLSS5_Feed@DLSS5_Feed.fx"), new("DLSS5_Feed.fx", "MV_SCALE", "1.0")], owner: "neural-rendering");
        Assert.Equal("DLSS5_Feed@DLSS5_Feed.fx", IniSettings.Get(File.ReadAllText(preset), "", "TechniqueSorting"));
        Assert.Equal("1.0", IniSettings.Get(File.ReadAllText(preset), "DLSS5_Feed.fx", "MV_SCALE"));
        IniSettings.Restore(ini, "neural-rendering"); IniSettings.Restore(preset, "neural-rendering");
        Assert.Equal(original, File.ReadAllText(preset));
        Assert.Equal("0", IniSettings.Get(File.ReadAllText(ini), "renodx", "Set_Path")); // HDR edit untouched
        IniSettings.Restore(ini);
        Assert.Equal(originalIni, File.ReadAllText(ini));
    }

    [Fact] public void MethodRulesMatchTheWindowsPicker()
    {
        Assert.Equal(NrMethod.Feeder, NrMethod.Recommended(true, GraphicsApiType.DirectX12, true));
        Assert.Equal(NrMethod.Feeder, NrMethod.Recommended(false, GraphicsApiType.DirectX12, false));
        Assert.Equal(NrMethod.Bridge, NrMethod.Recommended(false, GraphicsApiType.Vulkan, true));
        Assert.Equal(NrMethod.ShortFuse, NrMethod.Recommended(false, GraphicsApiType.DirectX12, true));
        Assert.False(NrMethod.Available(NrMethod.ShortFuse, true, GraphicsApiType.DirectX9, false));
        Assert.False(NrMethod.Available(NrMethod.Bridge, false, GraphicsApiType.DirectX12, true));
        Assert.False(NrMethod.Available(NrMethod.Dlss5Tool, false, GraphicsApiType.DirectX12, false));
        Assert.True(NrMethod.Available(NrMethod.Feeder, true, GraphicsApiType.OpenGL, false));
    }

    [Fact] public async Task RemovesNeuralRenderingPlacedByTheWindowsApp()
    {
        var dir = Path.Combine(_root, "shared/Game"); var exe = FileAt("shared/Game/Game.exe", PeWithVersion(1, 0, 0, 0));
        FileAt("shared/Game/renodx-dlss5.addon64", Encoding.UTF8.GetBytes("addon"));
        FileAt("shared/Game/nvngx_dlssnr.dll", PeWithVersion(310, 8, 0, 0)); FileAt("shared/Game/nvngx_dlssnr.dll.original", []);
        FileAt("shared/Game/nvngx_dlss.dll", PeWithVersion(310, 9, 1, 0)); FileAt("shared/Game/nvngx_dlss.dll.original", PeWithVersion(310, 1, 0, 0));
        var game = GameAt(Path.Combine(_root, "shared"), exe);
        var state = NeuralRenderingSetup.Read(game);
        Assert.Equal(NrMethod.Dlss5Tool, state.Method);
        Assert.Contains(state.Tags(NrMethod.Dlss5Tool, false), t => t.Text == "✓ NR DLL 310.8.0" && t.Ok);
        using var http = new HttpClient(); var downloads = new Downloads(http);
        var setup = new NeuralRenderingSetup(downloads, new DlssCatalog(http, downloads), new AddonReleases(http, downloads), new Catalog(http));
        await setup.Remove(game);
        Assert.False(File.Exists(Path.Combine(dir, "renodx-dlss5.addon64")));
        Assert.False(File.Exists(Path.Combine(dir, "nvngx_dlssnr.dll")));
        Assert.Equal("310.1.0.0", PeVersion.Read(Path.Combine(dir, "nvngx_dlss.dll")));
        Assert.False(NeuralRenderingSetup.Read(game).AnyInstalled);
    }

    [Fact] public void NeuralRenderingLaunchExtrasFollowTheInstallRecord()
    {
        var exe = FileAt("nr/Game.exe", PeWithVersion(1, 0, 0, 0)); var game = GameAt(Path.Combine(_root, "nr"), exe);
        Assert.False(NeuralRenderingSetup.Extras(game, new()).Environment.ContainsKey("PROTON_ENABLE_NVAPI")); // nothing installed, nothing to add
        LinuxPaths.WriteJson(Path.Combine(_root, "nr/.rhi-linux/neural-rendering.json"), new NrRecord { Method = NrMethod.Feeder, DgVoodoo = true, Host64 = true });
        var extras = NeuralRenderingSetup.Extras(game, new());
        Assert.Equal(["d3d9", "dxgi"], extras.Dlls);
        var options = Proton.LaunchOptions("%command%", "dxgi.dll", extras);
        Assert.Contains("d3d9=n,b", options); Assert.Contains("PROTON_ENABLE_NVAPI=1", options);
        Assert.True(InstallationStatus.HasLaunchOverrides(options, "dxgi.dll", extras));
        Assert.False(InstallationStatus.HasLaunchOverrides("WINEDLLOVERRIDES='dxgi=n,b;d3dcompiler_47=n,b' %command%", "dxgi.dll", extras));
    }
}
