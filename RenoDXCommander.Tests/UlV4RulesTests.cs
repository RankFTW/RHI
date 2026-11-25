using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>ReLimiter v4 rules: who gets v4, reading the API from ReShade.log, versions and the fresh relimiter.ini.</summary>
public class UlV4RulesTests
{
    private static UlV4Verdict Check(bool is32 = false, bool vulkan = false, bool dxvk = false, bool openGl = false,
        UlGpuState gpu = UlGpuState.Nvidia, UlDx12State dx12 = UlDx12State.Yes) =>
        UlV4Rules.Check(is32, vulkan, dxvk, openGl, gpu, dx12);

    [Fact]
    public void Dx12_On_Nvidia_Is_Automatic()
    {
        var v = Check();
        Assert.True(v.Allowed);
        Assert.True(v.Auto);
        Assert.Null(v.Reason);
        Assert.Null(v.Note);
    }

    [Fact]
    public void Not_Nvidia_Is_Blocked()
    {
        var v = Check(gpu: UlGpuState.NotNvidia);
        Assert.False(v.Allowed);
        Assert.False(v.Auto);
        Assert.False(string.IsNullOrEmpty(v.Reason));
    }

    // Since 23 Nov 2026 (user decision) v4 is the default for every game on an NVIDIA card
    [Theory]
    [InlineData(true, false, false, false)]   // 32-bit
    [InlineData(false, true, false, false)]   // Vulkan
    [InlineData(false, false, true, false)]   // DXVK
    [InlineData(false, false, false, true)]   // OpenGL
    public void Untested_Setups_Are_Automatic_With_A_Note(bool is32, bool vulkan, bool dxvk, bool openGl)
    {
        var v = Check(is32, vulkan, dxvk, openGl);
        Assert.True(v.Allowed);
        Assert.True(v.Auto);
        Assert.Null(v.Reason);
        Assert.Contains("untested", v.Note);
        Assert.Contains("pick v3", v.Note);
    }

    [Fact]
    public void Not_Dx12_Is_Automatic_With_A_Note()
    {
        var v = Check(dx12: UlDx12State.No);
        Assert.True(v.Auto);
        Assert.Contains("DirectX 12", v.Note);
    }

    [Fact]
    public void Unknown_Api_Is_Automatic_Without_A_Note()
    {
        var v = Check(dx12: UlDx12State.Unknown);
        Assert.True(v.Auto);
        Assert.Null(v.Note);
    }

    [Fact]
    public void Unknown_Gpu_Is_Allowed_But_Never_Automatic()
    {
        var v = Check(gpu: UlGpuState.Unknown);
        Assert.True(v.Allowed);
        Assert.False(v.Auto);
        Assert.Contains("graphics card", v.Note);
    }

    [Fact]
    public void WantsV4_Follows_Choice_Within_The_Verdict()
    {
        var auto = Check();
        var manualOnly = Check(gpu: UlGpuState.Unknown);   // GPU not detected: never moved automatically
        var blocked = Check(gpu: UlGpuState.NotNvidia);
        var untested = Check(is32: true);

        Assert.True(UlV4Rules.WantsV4(null, auto));
        Assert.False(UlV4Rules.WantsV4(null, manualOnly));
        Assert.True(UlV4Rules.WantsV4("4", manualOnly));
        Assert.False(UlV4Rules.WantsV4("4", blocked));   // a v4 choice can't beat a block
        Assert.False(UlV4Rules.WantsV4("3", auto));
        Assert.True(UlV4Rules.WantsV4(null, untested));    // moved automatically since 23 Nov 2026
        Assert.False(UlV4Rules.WantsV4("3", untested));    // the user can go back to v3
    }

    // Lines as ReShade 6.8 / ReLimiter write them (taken from real logs)
    private const string Start = "08:15:48:106 [43216] | INFO  | Initializing crosire's ReShade version '6.8.0.2155' (64-bit) loaded from 'C:\\Games\\X\\dxgi.dll' ...\n";
    private const string D3D12 = "16:44:43:361 [27800] | INFO  | Redirecting D3D12CreateDevice(pAdapter = 0000017F8A5ADE20, MinimumFeatureLevel = 0xc000, riid = {189819F1-1DB6-4B57-BE54-1821339B85F7}, ppDevice = 0000003D6E2FE5B8) ...\n";
    private const string D3D11 = "22:47:05:883 [25368] | INFO  | Redirecting D3D11CreateDevice(pAdapter = 00000235A2D75FB0, DriverType = 0, Software = 0000000000000000, Flags = 0x20, pFeatureLevels = 00000035D2EFE3F0, FeatureLevels = 4, SDKVersion = 7, ppDevice = 00000035D2EFE528, pFeatureLevel = 00000035D2EFE4D0, ppImmediateContext = 00000035D2EFE520) ...\n";
    private const string D3D9 = "15:44:50:125 [34424] | INFO  | Redirecting Direct3DCreate9Ex(SDKVersion = 0x20, ppD3D = 000000EF7C37EE88) ...\n";
    private const string Gl = "16:02:55:017 [ 3000] | INFO  | Redirecting wglCreateContext(hdc = 000000006E0130E3) ...\n";
    private const string VkInstance = "12:00:01:000 [ 1234] | INFO  | Redirecting vkCreateInstance(pCreateInfo = 000000C0FFEE0000, pAllocator = 0000000000000000, pInstance = 000000C0FFEE0010) ...\n";
    private const string SwapChain = "18:22:28:150 [26028] | INFO  | Redirecting IDXGIFactory2::CreateSwapChainForHwnd(this = 000001B6174C1220, pDevice = 000001B617E16B60) ...\n";
    private const string Running = "18:22:28:417 [26028] | INFO  | Running on NVIDIA GeForce RTX 5090 Driver 617.42.\n";
    private const string Unregistered = "18:22:28:100 [26028] | INFO  | Unregistered add-on \"ReLimiter\".\n";
    private static string Rl(string version) => $"18:22:27:855 [26028] | INFO  | Registered add-on \"ReLimiter\" v{version} using ReShade API version 18.\n";
    private static string RlApi(int api) => $"16:44:47:000 [27800] | WARN  | [ReLimiter] on_present: captured presenting swapchain 00000186FF7493B0 (hwnd=000000000010167E, api={api}, ptr_changed=1, api_changed=1)\n";

    [Fact]
    public void ReShadeLog_ReLimiter_Api_New_Numbering()
    {
        // v3.3.1+: None, DX9, DX11, DX12, Vulkan, OpenGL (Quake: DX12, then a DX11 launcher resize)
        Assert.Equal(UlLogApi.Dx12, UlV4Rules.ApiFromReShadeLog(Start + D3D12 + Rl("3.3.4.0") + RlApi(3) + D3D11 + RlApi(2)));
        // Rogue Waters: DX11 game, DLSS 5 Bridge makes a D3D12 device after ReShade starts
        Assert.Equal(UlLogApi.Older, UlV4Rules.ApiFromReShadeLog(Start + D3D11 + Rl("3.3.5.0") + SwapChain + Running + RlApi(2) + D3D12));
    }

    [Fact]
    public void ReShadeLog_ReLimiter_Api_Old_Numbering_Before_3_3_1()
    {
        // Before v3.3.1: None, DX11, DX12, Vulkan, OpenGL (GreedFall: The Dying World on 3.2.9, Star Trek Voyager on 3.3.0)
        Assert.Equal(UlLogApi.Dx12, UlV4Rules.ApiFromReShadeLog(Start + D3D12 + Rl("3.2.9.0") + RlApi(2)));
        Assert.Equal(UlLogApi.Dx12, UlV4Rules.ApiFromReShadeLog(Start + D3D12 + Rl("3.3.0.0") + RlApi(2)));
        Assert.Equal(UlLogApi.Older, UlV4Rules.ApiFromReShadeLog(Start + D3D11 + Rl("3.2.9.0") + RlApi(1)));
    }

    [Fact]
    public void ReShadeLog_Uses_The_Device_In_Use_When_ReShade_Started()
    {
        // ZERO PARADES: a D3D12 test device (add-ons unregister when it's released), then it renders on D3D11
        Assert.Equal(UlLogApi.Older, UlV4Rules.ApiFromReShadeLog(Start + D3D12 + Rl("4.0.0.0") + Unregistered + D3D11 + SwapChain + Running));
        // DX12 games open D3D11 / D3D9 devices after starting (video, UI) or before (RDR2's D3D9)
        Assert.Equal(UlLogApi.Dx12, UlV4Rules.ApiFromReShadeLog(Start + D3D9 + D3D12 + SwapChain + Running + D3D11));
        // No "Running on" line yet: the last device
        Assert.Equal(UlLogApi.Older, UlV4Rules.ApiFromReShadeLog(Start + D3D9));
        Assert.Equal(UlLogApi.Older, UlV4Rules.ApiFromReShadeLog(Start + Gl));
    }

    private const string D3D11On12 = "06:35:03:010 [34068] | INFO  | Redirecting D3D11On12CreateDevice(pDevice = 000001094EC48B30, Flags = 0x20, pFeatureLevels = 0000000000000000, FeatureLevels = 0, ppCommandQueues = 0000003D6E2FE5B8, NumQueues = 1, NodeMask = 0, ppDevice = 0000003D6E2FE5C0, ppImmediateContext = 0000000000000000, pChosenFeatureLevel = 0000000000000000) ...\n";

    [Fact]
    public void ReShadeLog_D3D11On12_Counts_As_D3D12()
    {
        // Nice Day for Fishing / BLACKWOOD: D3D12, D3D11-on-12 (with its D3D11 lines), then ReShade starts
        Assert.Equal(UlLogApi.Dx12, UlV4Rules.ApiFromReShadeLog(Start + D3D12 + D3D11On12 + D3D11 + SwapChain + Running));
    }

    [Fact]
    public void ReShadeLog_Dx11_Splash_Then_Dx12_Is_Dx12()
    {
        // Hogwarts Legacy: a D3D11 window first, then the game on D3D12
        Assert.Equal(UlLogApi.Dx12, UlV4Rules.ApiFromReShadeLog(Start + D3D11 + SwapChain + Running + Unregistered + D3D12 + SwapChain + Running + D3D11));
        // Silver Pines: DX11 game, GITC Uplift's D3D12 device between two DX11 starts
        Assert.Equal(UlLogApi.Older, UlV4Rules.ApiFromReShadeLog(Start + D3D11 + Running + D3D11 + D3D12 + D3D11 + Running + D3D11));
    }

    [Fact]
    public void ReShadeLog_Vulkan_Only_Is_Vulkan()
    {
        // Ryubing: RHI's card says DX12, but the log shows only Vulkan
        Assert.Equal(UlLogApi.Vulkan, UlV4Rules.ApiFromReShadeLog(Start + VkInstance));
        Assert.Equal(UlLogApi.Dx12, UlV4Rules.ApiFromReShadeLog(Start + VkInstance + D3D12 + Running));
    }

    [Fact]
    public void ReShadeLog_Without_A_Device_Is_Unknown()
    {
        Assert.Null(UlV4Rules.ApiFromReShadeLog(Start));
        Assert.Null(UlV4Rules.ApiFromReShadeLog(Start + Rl("3.3.5.0")));
        Assert.Null(UlV4Rules.ApiFromReShadeLog(""));
        Assert.Null(UlV4Rules.ApiFromReShadeLog(null));
    }

    [Theory]
    [InlineData("v3.3.5", 3)]
    [InlineData("4.0.0.0", 4)]
    [InlineData("V4.1", 4)]
    [InlineData("v4.0.0-beta", 4)]
    [InlineData("4", 4)]
    public void MajorOf_Parses_Tags_And_File_Versions(string s, int major) => Assert.Equal(major, UlV4Rules.MajorOf(s));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void MajorOf_Rejects_Junk(string? s) => Assert.Null(UlV4Rules.MajorOf(s));

    [Fact]
    public void Versions_Compare_With_Missing_Parts_As_Zero()
    {
        Assert.True(UlV4Rules.SameVersion(new Version(4, 0, 0, 0), UlV4Rules.ParseVersion("v4.0.0")!));
        Assert.True(UlV4Rules.IsOlder(new Version(3, 3, 5, 0), UlV4Rules.ParseVersion("v4.0.0")!));
        Assert.False(UlV4Rules.IsOlder(new Version(4, 0, 0, 0), UlV4Rules.ParseVersion("v4.0.0")!));
        Assert.True(UlV4Rules.IsOlder(new Version(3, 3, 4, 0), new Version(3, 3, 5)));
    }

    [Fact]
    public void DisplayVersion_Drops_A_Zero_Revision()
    {
        Assert.Equal("4.0.0", UlV4Rules.DisplayVersion(new Version(4, 0, 0, 0)));
        Assert.Equal("3.3.5.1", UlV4Rules.DisplayVersion(new Version(3, 3, 5, 1)));
    }

    [Fact]
    public void NewestV3_Is_By_Version_Not_Order()
    {
        var tags = new[] { "v4.0.1", "v4.0.0", "v3.3.10", "v3.3.9", "v3.4.0-junk", "nightly" };
        Assert.Equal("v3.4.0-junk", UlV4Rules.NewestTagWithMajor(tags, 3));
        Assert.Equal("v3.3.10", UlV4Rules.NewestTagWithMajor(new[] { "v3.3.9", "v3.3.10" }, 3));
        Assert.Null(UlV4Rules.NewestTagWithMajor(new[] { "v4.0.0" }, 3));
    }

    [Fact]
    public void FreshIni_Has_Only_Rhi_Lines()
    {
        var ini = UlV4Rules.BuildFreshIni("PageUp", sharedPresets: true, dlssHooks: false, targetFps: 117);
        Assert.Equal("[FrameLimiter]\r\nosd_toggle_key=PageUp\r\nshared_presets=true\r\ndlss_info_hooks=false\r\ntarget_fps=117\r\n", ini);
    }

    [Fact]
    public void FreshIni_Leaves_Out_An_Off_Cap_And_An_Empty_Key()
    {
        var ini = UlV4Rules.BuildFreshIni("", sharedPresets: false, dlssHooks: true, targetFps: 0);
        Assert.Equal("[FrameLimiter]\r\nshared_presets=false\r\ndlss_info_hooks=true\r\n", ini);
    }

    [Fact]
    public void FreshIni_For_V4_Turns_On_Native_Pcl()
    {
        var ini = UlV4Rules.BuildFreshIni("PageUp", sharedPresets: true, dlssHooks: true, targetFps: 0, nativePcl: true);
        Assert.Equal("[FrameLimiter]\r\nosd_toggle_key=PageUp\r\nshared_presets=true\r\ndlss_info_hooks=true\r\nnative_pcl_enabled=true\r\n", ini);
    }

    [Fact]
    public void BackupName_Names_The_Old_Major() =>
        Assert.Equal("relimiter.ini.v3-backup", UlV4Rules.BackupName(3));

    private const string Template = "[FrameLimiter]\nosd_toggle_key=F12\nadaptive_smoothing=true\nsmoothing_window=dual\n"
        + "pacing_preference=latency\nnative_pcl_enabled=true\nconfig_version=6\ntarget_fps=0\n\n"
        + "[OSD_Preset_1]\nshow_gpu_power=true\nshow_jitter=true\nshow_fps=true\n";

    [Fact]
    public void SeededIni_V4_DropsV3OnlyKeys_KeepsTheRest()
    {
        var ini = UlV4Rules.BuildSeededIni(Template, 4, "PageUp", sharedPresets: false, dlssHooks: false, targetFps: 0);
        Assert.DoesNotContain("adaptive_smoothing", ini);
        Assert.DoesNotContain("smoothing_window", ini);
        Assert.DoesNotContain("show_gpu_power", ini);
        Assert.Contains("pacing_preference=latency", ini);
        Assert.Contains("show_jitter=true", ini);
        Assert.Contains("show_fps=true", ini);
        Assert.Contains("osd_toggle_key=F12", ini); // the template's key wins over RHI's setting
        Assert.DoesNotContain("PageUp", ini);
    }

    [Fact]
    public void SeededIni_V3_DropsV4OnlyKeys()
    {
        var ini = UlV4Rules.BuildSeededIni(Template, 3, null, sharedPresets: true, dlssHooks: true, targetFps: 141);
        Assert.DoesNotContain("pacing_preference", ini);
        Assert.DoesNotContain("native_pcl_enabled", ini);
        Assert.DoesNotContain("config_version", ini);
        Assert.DoesNotContain("show_jitter", ini);
        Assert.Contains("adaptive_smoothing=true", ini);
        Assert.Contains("show_gpu_power=true", ini);
        Assert.Contains("shared_presets=true", ini);
        Assert.Contains("dlss_info_hooks=true", ini);
        Assert.Contains("target_fps=141", ini);
        Assert.DoesNotContain("target_fps=0", ini);
    }

    [Fact]
    public void SeededIni_NoTemplateOrEmpty_IsTheFreshIni()
    {
        var fresh = UlV4Rules.BuildFreshIni("F1", true, true, 60, nativePcl: true);
        Assert.Equal(fresh, UlV4Rules.BuildSeededIni(null, 4, "F1", true, true, 60, nativePcl: true));
        Assert.Equal(fresh, UlV4Rules.BuildSeededIni("[UltraLimiter]", 4, "F1", true, true, 60, nativePcl: true));
    }

    [Fact]
    public void SeededIni_V4WithPcl_AddsNativePcl() =>
        Assert.Contains("native_pcl_enabled=true",
            UlV4Rules.BuildSeededIni("[FrameLimiter]\nosd_toggle_key=F12\n", 4, null, false, false, 0, nativePcl: true));

    [Fact]
    public void ApplyNativePcl_TurnsItOnAndOff_KeepingOtherKeys()
    {
        var path = Path.Combine(Path.GetTempPath(), "rhi-pcl-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllText(path, "[FrameLimiter]\r\ntarget_fps=116\r\nnative_pcl_enabled=true\r\n");
            AuxInstallService.ApplyUlNativePcl(path, on: false);
            var off = File.ReadAllText(path);
            Assert.Contains("native_pcl_enabled=false", off);
            Assert.Contains("target_fps=116", off);
            AuxInstallService.ApplyUlNativePcl(path, on: true);
            Assert.Contains("native_pcl_enabled=true", File.ReadAllText(path));
            Assert.DoesNotContain("native_pcl_enabled=false", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("smooth", "smooth")]
    [InlineData("Latency", "latency")]
    [InlineData(" balanced ", "balanced")]
    [InlineData("smoother", null)]
    [InlineData(null, null)]
    public void NormalizePacing_AcceptsOnlyReLimitersValues(string? input, string? expected)
        => Assert.Equal(expected, UlV4Rules.NormalizePacing(input));

    [Fact]
    public void Seeding_WritesPacingForV4Only_AndNotWhenBalanced()
    {
        const string template = "[FrameLimiter]\r\ntarget_fps=116\r\npacing_preference=latency\r\n";
        var v4 = UlV4Rules.BuildSeededIni(template, 4, null, false, false, 0, pacingPreference: "smooth");
        Assert.Contains("pacing_preference=smooth", v4);
        Assert.DoesNotContain("pacing_preference=latency", v4);

        var v3 = UlV4Rules.BuildSeededIni(template, 3, null, false, false, 0, pacingPreference: "smooth");
        Assert.DoesNotContain("pacing_preference", v3);

        // Balanced = ReLimiter's default: the template's own value is left as it is
        var balanced = UlV4Rules.BuildSeededIni(template, 4, null, false, false, 0, pacingPreference: "balanced");
        Assert.Contains("pacing_preference=latency", balanced);

        Assert.Contains("pacing_preference=smooth", UlV4Rules.BuildSeededIni(null, 4, null, false, false, 0, pacingPreference: "smooth"));
        Assert.DoesNotContain("pacing_preference", UlV4Rules.BuildSeededIni(null, 3, null, false, false, 0, pacingPreference: "smooth"));
    }
}
