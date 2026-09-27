using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class OptiScalerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-os-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    private readonly string? _oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
    private readonly OptiScalerSettings _settings = new() { Gpu = "NVIDIA", Hotkey = "Delete" };

    public OptiScalerTests()
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

    private static byte[] Pe(string marker = "")
    {
        var bytes = new byte[512 + marker.Length]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; BitConverter.GetBytes(128).CopyTo(bytes, 0x3c);
        bytes[128] = (byte)'P'; bytes[129] = (byte)'E'; BitConverter.GetBytes((ushort)MachineType.x64).CopyTo(bytes, 132);
        Encoding.ASCII.GetBytes(marker).CopyTo(bytes, 512); return bytes;
    }
    private Game Game(string name)
    {
        var root = Path.Combine(_root, name); Directory.CreateDirectory(root);
        var exe = Path.Combine(root, name.Replace(' ', '_') + "-os.exe"); File.WriteAllBytes(exe, Pe());
        return new() { Name = name, Root = root, Executable = exe, Executables = [exe] };
    }
    private static string Read(Game game, string relative) => File.ReadAllText(LinuxPaths.ResolveCase(game.InstallDirectory, relative));
    private static bool Exists(Game game, string relative) => File.Exists(LinuxPaths.ResolveCase(game.InstallDirectory, relative));

    // A cached release as Stage() leaves it, and the release list naming it as the latest.
    private static void Release(string variant, string version, string ini = "[General]\nLoadReshade=false\nShortcutKey=0x2D\n[Upscalers]\nDx12Upscaler=auto\n")
    {
        var cache = Path.Combine(LinuxPaths.Cache, "optiscaler");
        var dir = Path.Combine(cache, variant, version);
        Directory.CreateDirectory(Path.Combine(dir, "D3D12_Optiscaler")); Directory.CreateDirectory(Path.Combine(dir, "Licenses"));
        File.WriteAllBytes(Path.Combine(dir, "OptiScaler.dll"), Pe("OptiScaler " + version));
        File.WriteAllBytes(Path.Combine(dir, "fakenvapi.dll"), Pe("fakenvapi"));
        File.WriteAllText(Path.Combine(dir, "fakenvapi.ini"), "[fakenvapi]\n");
        File.WriteAllText(Path.Combine(dir, "OptiScaler.ini"), ini);
        File.WriteAllText(Path.Combine(dir, "setup_linux.sh"), "#!/bin/sh");
        File.WriteAllText(Path.Combine(dir, "!! EXTRACT ALL FILES TO GAME FOLDER !!"), "");
        File.WriteAllBytes(Path.Combine(dir, "D3D12_Optiscaler", "D3D12Core.dll"), Pe("d3d12core"));
        File.WriteAllText(Path.Combine(dir, "Licenses", "XeSS_LICENSE.txt"), "licence");
        File.WriteAllText(Path.Combine(dir, ".complete"), "test");
        var latest = OsVariant.All.ToDictionary(v => v, v => new NrRelease(v == variant ? version : "0", "https://example.invalid/" + v + ".7z"));
        var file = Path.Combine(cache, "latest.json");
        if (File.Exists(file))
            foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, NrRelease>>(File.ReadAllText(file), LinuxPaths.Json)!)
                if (key != variant) latest[key] = value;
        LinuxPaths.WriteJson(file, latest);
    }

    private sealed class Stub(Func<Uri, byte[]?> serve) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(serve(request.RequestUri!) is { } body ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) } : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    // OptiPatcher and a DLSS SR zip are served; everything else (other DLSS DLLs) is unavailable.
    private OptiScaler Service()
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
        using (var entry = archive.CreateEntry("nvngx_dlss.dll").Open()) entry.Write(Pe("dlss 310"));
        Directory.CreateDirectory(Path.Combine(LinuxPaths.Cache, "dlss"));
        File.WriteAllText(Path.Combine(LinuxPaths.Cache, "dlss", "dlss_manifest.json"), """{"dlss":[{"version":"310.9.1","url":"https://example.invalid/sr.zip"}]}""");
        var http = new HttpClient(new Stub(uri => uri.AbsolutePath.EndsWith("OptiPatcher.asi") ? Pe("optipatcher") : uri.AbsolutePath.EndsWith("sr.zip") ? zip.ToArray() : null));
        var downloads = new Downloads(http);
        return new(http, downloads, new DlssCatalog(http, downloads));
    }

    [Fact] public void ParsesStableNightlyAndDlssNrReleases()
    {
        using var stable = JsonDocument.Parse("""{"tag_name":"v0.9.4","assets":[{"name":"Optiscaler_0.9.4.7z","browser_download_url":"https://x/s.7z"}]}""");
        Assert.Equal(new NrRelease("v0.9.4", "https://x/s.7z"), OptiScaler.Parse(stable.RootElement, OsVariant.Stable));
        using var nightly = JsonDocument.Parse("""[{"tag_name":"nightly-20260927","draft":false,"assets":[{"name":"OptiScaler_v10.7z","browser_download_url":"https://x/n.7z"}]}]""");
        Assert.Equal(new NrRelease("20260927", "https://x/n.7z"), OptiScaler.Parse(nightly.RootElement, OsVariant.Nightly));
        using var nr = JsonDocument.Parse("""[{"tag_name":"v0.8.91","assets":[{"name":"OptiScaler-NR-v0.8.91-rtx40-mfg.zip","browser_download_url":"https://x/mfg.zip"},{"name":"OptiScaler-NR-v0.8.91.zip","browser_download_url":"https://x/nr.zip"}]}]""");
        Assert.Equal(new NrRelease("0.8.91", "https://x/nr.zip"), OptiScaler.Parse(nr.RootElement, OsVariant.DlssNr));
        using var none = JsonDocument.Parse("""[{"tag_name":"v1","assets":[{"name":"notes.txt","browser_download_url":"https://x/n.txt"}]}]""");
        Assert.Null(OptiScaler.Parse(none.RootElement, OsVariant.Stable));
    }

    [Fact] public void IniEditsMatchTheWindowsHelpers()
    {
        var text = "[Upscalers]\r\nDx12Upscaler=auto\r\n; Dx11Upscaler=fsr22\r\n\r\n[FrameGen]\r\nFGInput=auto\r\n";
        text = OptiScaler.IniSet(text, "Upscalers", "Dx12Upscaler", "xess");
        text = OptiScaler.IniSet(text, "Upscalers", "Dx11Upscaler", "fsr31");
        text = OptiScaler.IniSet(text, "DLSS", "RenderPresetForAll", "11");
        Assert.Equal("xess", OptiScaler.IniGet(text, "upscalers", "DX12UPSCALER"));
        Assert.Equal("fsr31", OptiScaler.IniGet(text, "Upscalers", "Dx11Upscaler"));
        Assert.True(text.IndexOf("Dx11Upscaler=fsr31", StringComparison.Ordinal) < text.IndexOf("[FrameGen]", StringComparison.Ordinal));
        Assert.Equal("11", OptiScaler.IniGet(text, "DLSS", "RenderPresetForAll"));
        Assert.DoesNotContain("\n\n\n", text.Replace("\r\n", "\n"));
        Assert.Contains("\r\n", text);
        var forced = OptiScaler.IniForce("[A]\nLoadReshade=false\n[B]\nLoadReshade=auto\n", "LoadReshade", "true");
        Assert.Single(forced.Split('\n'), l => l.StartsWith("LoadReshade"));
        Assert.Equal("true", OptiScaler.IniGet(forced, "A", "LoadReshade"));
        Assert.Contains("ShortcutKey=0x2E", OptiScaler.IniForce("", "ShortcutKey", "0x2E"));
        var merged = OptiScaler.MergeIni("[FrameGen]\nFGInput=dlssg\nFGOutput=auto\n[Old]\nGone=1\n", "[FrameGen]\nFGInput=auto\nFGOutput=auto\nNewKey=yes\n");
        Assert.Equal("dlssg", OptiScaler.IniGet(merged, "FrameGen", "FGInput"));
        Assert.Equal("yes", OptiScaler.IniGet(merged, "FrameGen", "NewKey"));
        Assert.Equal("1", OptiScaler.IniGet(merged, "Old", "Gone"));
    }

    [Fact] public void TemplatesAndDllNamesFollowTheWindowsRules()
    {
        Assert.Equal("OptiScaler.nvidia.ini", OptiScaler.TemplateName("NVIDIA", false, OsVariant.Stable));
        Assert.Equal("OptiScaler_nightly.amd-dlss.ini", OptiScaler.TemplateName("AMD", true, OsVariant.Nightly));
        Assert.Equal("OptiScaler_dlssnr.amd-nodlss.ini", OptiScaler.TemplateName("AMD", false, OsVariant.DlssNr));
        foreach (var variant in OsVariant.All)
            foreach (var (gpu, inputs) in new[] { ("NVIDIA", true), ("AMD", true), ("AMD", false) })
                Assert.True(File.Exists(OptiScaler.Template(gpu, inputs, variant)), OptiScaler.TemplateName(gpu, inputs, variant));
        Assert.Contains("Dxgi=false", File.ReadAllText(OptiScaler.Template("AMD", false, OsVariant.Stable)!));
        Assert.Equal("dxgi.dll", OptiScaler.DllFor(new(), GraphicsApiType.DirectX12));
        Assert.Equal("winmm.dll", OptiScaler.DllFor(new(), GraphicsApiType.Vulkan));
        Assert.Equal("version.dll", OptiScaler.DllFor(new() { OsDllName = "version.dll" }, GraphicsApiType.Vulkan));
        Assert.Equal("dxgi.dll", OptiScaler.DllFor(new() { OsDllName = "evil/../x.dll" }, GraphicsApiType.DirectX11));
    }

    [Fact] public async Task InstallsBesideReShadeAndRemovesExactly()
    {
        var game = Game("Coexist");
        var dir = game.InstallDirectory;
        File.WriteAllText(Path.Combine(dir, "OptiScaler.ini"), "user's own ini");
        File.WriteAllBytes(Path.Combine(dir, "nvngx_dlss.dll"), Pe("game dlss"));
        var install = new Installation(dir);
        install.Install("ReShade", "6.8.0", [new("dxgi.dll", Pe("reshade")), Installation.DefaultIni()], proxy: "dxgi.dll");
        Release(OsVariant.Stable, "v0.9.4");
        var prefs = new GamePreferences();

        await Service().Install(game, prefs, _settings, GraphicsApiType.DirectX12);

        Assert.Contains("OptiScaler v0.9.4", Read(game, "dxgi.dll"));
        Assert.Contains("reshade", Read(game, "ReShade64.dll"));
        Assert.Equal("ReShade64.dll", install.ReShadeFile("dxgi.dll"));
        Assert.Equal("dxgi.dll", install.ReadState().Proxy);
        Assert.True(Exists(game, "D3D12_Optiscaler/D3D12Core.dll") && Exists(game, "fakenvapi.dll") && Exists(game, "plugins/OptiPatcher.asi"));
        Assert.False(Exists(game, "setup_linux.sh") || Exists(game, "Licenses/XeSS_LICENSE.txt") || Exists(game, "OptiScaler.dll") || Exists(game, "!! EXTRACT ALL FILES TO GAME FOLDER !!"));
        var ini = Read(game, "OptiScaler.ini");
        Assert.Equal("true", OptiScaler.IniGet(ini, "Plugins", "LoadReshade") ?? ini.Split('\n').First(l => l.StartsWith("LoadReshade=")).Split('=')[1]);
        Assert.Contains("LoadAsiPlugins=true", ini); Assert.Contains("ShortcutKey=0x2E", ini);
        Assert.Contains("dlss 310", Read(game, "nvngx_dlss.dll"));
        var status = InstallationStatus.Read(game);
        Assert.True(status.Get(OptiScaler.Component).Installed); Assert.Equal("v0.9.4", status.Get(OptiScaler.Component).Version);
        Assert.True(status.Get("ReShade").Installed);
        Assert.Contains("dxgi", GameLaunch.Extras(game, prefs).Dlls);
        Assert.False(Service().UpdateAvailable(game));

        // A ReShade update while OptiScaler owns dxgi.dll goes to ReShade64.dll.
        Assert.Equal("ReShade64.dll", install.ReShadeFile("dxgi.dll"));
        install.Install("ReShade", "6.8.1", [new(install.ReShadeFile("dxgi.dll"), Pe("reshade 2")), Installation.DefaultIni()], proxy: "dxgi.dll");
        Assert.Contains("reshade 2", Read(game, "ReShade64.dll"));

        await OptiScaler.Remove(game);
        Assert.Contains("reshade 2", Read(game, "dxgi.dll"));
        Assert.False(Exists(game, "ReShade64.dll") || Exists(game, "fakenvapi.dll") || Exists(game, "plugins") || Exists(game, "D3D12_Optiscaler"));
        Assert.False(Directory.Exists(Path.Combine(dir, "D3D12_Optiscaler")) || Directory.Exists(Path.Combine(dir, "plugins")));
        Assert.Equal("user's own ini", Read(game, "OptiScaler.ini"));
        Assert.Contains("game dlss", Read(game, "nvngx_dlss.dll"));
        Assert.False(Exists(game, "OptiScaler.ini.original") || Exists(game, "nvngx_dlss.dll.original"));
        Assert.Null(OptiScaler.Record(game));
        Assert.DoesNotContain(OptiScaler.Component, InstallationStatus.Read(game).Components.Keys);
        Assert.Equal("dxgi.dll", install.ReShadeFile("dxgi.dll"));
    }

    [Fact] public async Task UpdatesKeepIniChangesAndDllNameChangesReturnReShade()
    {
        var game = Game("Update");
        var install = new Installation(game.InstallDirectory);
        install.Install("ReShade", "6.8.0", [new("dxgi.dll", Pe("reshade"))], proxy: "dxgi.dll");
        Release(OsVariant.Nightly, "20260926");
        var prefs = new GamePreferences { OsVariant = OsVariant.Nightly, OsFgInput = "dlssg", OsFgOutput = "dlssg", OsFgNvngx = "Nukems" };
        var service = Service();
        await service.Install(game, prefs, _settings, GraphicsApiType.DirectX12);
        Assert.Equal("dlssg", OptiScaler.GetSetting(game, "FrameGen", "FGInput"));
        Assert.Equal("Nukems", OptiScaler.GetSetting(game, "FrameGen", "FGNvngxReplacement"));
        OptiScaler.SetSetting(game, "Upscalers", "Dx12Upscaler", "xess");

        Release(OsVariant.Nightly, "20260927", "[General]\nLoadReshade=false\n[Upscalers]\nDx12Upscaler=auto\nNewDefault=on\n");
        service = Service();
        Assert.True(service.UpdateAvailable(game));
        await service.Install(game, prefs, _settings, GraphicsApiType.DirectX12);
        Assert.Contains("OptiScaler 20260927", Read(game, "dxgi.dll"));
        Assert.Equal("xess", OptiScaler.GetSetting(game, "Upscalers", "Dx12Upscaler"));
        Assert.Equal("on", OptiScaler.GetSetting(game, "Upscalers", "NewDefault"));
        Assert.Equal("Nightly 20260927", InstallationStatus.Read(game).Get(OptiScaler.Component).Version);

        prefs.OsDllName = "winmm.dll";
        await service.Install(game, prefs, _settings, GraphicsApiType.DirectX12);
        Assert.Contains("reshade", Read(game, "dxgi.dll"));
        Assert.Contains("OptiScaler", Read(game, "winmm.dll"));
        Assert.False(Exists(game, "ReShade64.dll"));
        var extras = GameLaunch.Extras(game, prefs);
        Assert.Contains("winmm", extras.Dlls); Assert.DoesNotContain("dxgi", extras.Dlls);
        Assert.True(InstallationStatus.HasLaunchOverrides(Proton.LaunchOptions("%command%", "dxgi.dll", extras), "dxgi.dll", extras));
        Assert.Equal("xess", OptiScaler.GetSetting(game, "Upscalers", "Dx12Upscaler"));

        // Switching variant starts from RHI's template.
        prefs.OsVariant = null; Release(OsVariant.Stable, "v0.9.4");
        await Service().Install(game, prefs, _settings, GraphicsApiType.DirectX12);
        Assert.NotEqual("xess", OptiScaler.GetSetting(game, "Upscalers", "Dx12Upscaler"));
        Assert.Contains("OptiScaler v0.9.4", Read(game, "winmm.dll"));
    }

    [Fact] public async Task RejectsThirtyTwoBitGamesAndMovingBackedUpFiles()
    {
        var game = Game("Old");
        var bytes = Pe(); BitConverter.GetBytes((ushort)MachineType.I386).CopyTo(bytes, 132); File.WriteAllBytes(game.Executable!, bytes);
        await Assert.ThrowsAsync<IOException>(() => Service().Install(game, new(), _settings, GraphicsApiType.DirectX11));
        var other = Game("Backed up");
        File.WriteAllText(Path.Combine(other.InstallDirectory, "dxgi.dll"), "game dxgi");
        var install = new Installation(other.InstallDirectory);
        install.Install("ReShade", "6.8.0", [new("dxgi.dll", Pe("reshade"))], replaceForeign: true, proxy: "dxgi.dll");
        Assert.Throws<IOException>(() => install.Move("ReShade", "dxgi.dll", "ReShade64.dll"));
        Assert.Contains("reshade", Read(other, "dxgi.dll"));
    }

    [Fact] public async Task RemovesAnInstallMadeByWindowsRhi()
    {
        var game = Game("Shared");
        var dir = game.InstallDirectory;
        void Write(string relative, string text) { var path = Path.Combine(dir, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        Write("dxgi.dll", "optiscaler"); Write("dxgi.dll.original", "");
        Write("ReShade64.dll", "reshade");
        Write("OptiScaler.ini", "ini"); Write("fakenvapi.dll", "fake"); Write("fakenvapi.dll.original", "");
        Write("amd_fidelityfx_dx12.dll", "os ffx"); Write("amd_fidelityfx_dx12.dll.original", "game ffx");
        Write("nvngx_dlss.dll", "shared dlss");
        Write("D3D12_Optiscaler/D3D12Core.dll", "core"); Write("plugins/OptiPatcher.asi", "patcher");
        File.WriteAllText(Path.Combine(dir, "rhi_install.txt"), JsonSerializer.Serialize(new
        {
            Component = "OptiScaler", Variant = "Stable", Version = "v0.9.4", InstalledAs = "dxgi.dll",
            Files = new[] { "dxgi.dll", "fakenvapi.dll", "amd_fidelityfx_dx12.dll", "OptiScaler.ini", "nvngx_dlss.dll" },
            Folders = new[] { "D3D12_Optiscaler", "plugins" }, SharedFiles = new Dictionary<string, string[]> { ["nvngx_dlss.dll"] = ["OptiScaler", "ShortFuse"] },
        }));
        Assert.True(OptiScaler.FromWindows(game)); Assert.True(OptiScaler.Installed(game));
        Assert.Equal("Windows RHI", OptiScaler.StatusOf(game, InstallationStatus.Read(game)).Version);
        Assert.Contains("dxgi", GameLaunch.Extras(game, new()).Dlls);

        await OptiScaler.Remove(game);
        Assert.Equal("reshade", Read(game, "dxgi.dll"));
        Assert.Equal("game ffx", Read(game, "amd_fidelityfx_dx12.dll"));
        Assert.Equal("shared dlss", Read(game, "nvngx_dlss.dll"));
        Assert.False(Exists(game, "ReShade64.dll") || Exists(game, "OptiScaler.ini") || Exists(game, "fakenvapi.dll") || Exists(game, "rhi_install.txt") || Exists(game, "dxgi.dll.original"));
        Assert.False(Directory.Exists(Path.Combine(dir, "D3D12_Optiscaler")) || Directory.Exists(Path.Combine(dir, "plugins")));
        Assert.False(OptiScaler.Installed(game));
    }

    [Fact] public async Task NeuralRenderingAndOptiScalerEachKeepTheOthersDlssDlls()
    {
        var game = Game("Shared dlls");
        var dir = game.InstallDirectory;
        var dll = LinuxPaths.ResolveCase(dir, "nvngx_dlss.dll");
        File.WriteAllBytes(dll, Pe("placed")); File.WriteAllBytes(dll + ".original", []);
        LinuxPaths.WriteJson(Path.Combine(dir, ".rhi-linux", "neural-rendering.json"), new NrRecord { Method = NrMethod.Dlss5Tool, Dlls = [dll] });
        LinuxPaths.WriteJson(Path.Combine(dir, ".rhi-linux", "optiscaler.json"), new OptiScalerRecord { Dlls = [dll] });
        using var http = new HttpClient(new Stub(_ => null)); var downloads = new Downloads(http);
        await new NeuralRenderingSetup(downloads, new DlssCatalog(http, downloads), new AddonReleases(http, downloads), new Catalog(http)).Remove(game);
        Assert.True(File.Exists(dll));
        await OptiScaler.Remove(game);
        Assert.False(File.Exists(dll) || File.Exists(dll + ".original"));
    }
}
