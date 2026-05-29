using System.Globalization;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class LocalizationServiceTests
{
    [Fact]
    public void AutoPreference_UsesChineseForChineseUiCulture()
    {
        var resolved = LocalizationService.ResolveEffectiveLanguage(
            LocalizationService.AutoLanguage,
            CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal(LocalizationService.SimplifiedChineseLanguage, resolved);
    }

    [Fact]
    public void AutoPreference_FallsBackToEnglishForUnsupportedCulture()
    {
        var resolved = LocalizationService.ResolveEffectiveLanguage(
            LocalizationService.AutoLanguage,
            CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal(LocalizationService.EnglishLanguage, resolved);
    }

    [Fact]
    public void ChinesePreference_TranslatesCoreUiTextAndFormatsCounts()
    {
        try
        {
            LocalizationService.SetLanguagePreference(LocalizationService.SimplifiedChineseLanguage);

            Assert.Equal("刷新", LocalizationService.Text("Refresh"));
            Assert.Equal("显示 12 个", LocalizationService.Format("{0} shown", 12));
            Assert.Equal("已安装 3 个", LocalizationService.Format("{0} installed", 3));
        }
        finally
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);
        }
    }

    [Fact]
    public void ChinesePreference_TranslatesMultilineDynamicStatusText()
    {
        try
        {
            LocalizationService.SetLanguagePreference(LocalizationService.SimplifiedChineseLanguage);

            var source = "A new version of RHI is available!\n\n"
                + "Installed:  v1.0.0\n"
                + "Available:  v1.1.0\n\n"
                + "Would you like to update now?";

            var translated = LocalizationService.Text(source);

            Assert.Contains("RHI 有新版本可用！", translated);
            Assert.Contains("当前版本：v1.0.0", translated);
            Assert.Contains("可用版本：v1.1.0", translated);
            Assert.Contains("是否现在更新？", translated);
        }
        finally
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);
        }
    }

    [Fact]
    public void ChinesePreference_TranslatesBatchDeploymentReportLines()
    {
        try
        {
            LocalizationService.SetLanguagePreference(LocalizationService.SimplifiedChineseLanguage);

            Assert.Equal("DLSS SR 已部署到 2 个游戏", LocalizationService.Text("DLSS SR deployed to 2 game(s)"));
            Assert.Equal("已跳过：1（已经是所选版本）", LocalizationService.Text("Skipped: 1 (already at selected version)"));
            Assert.Equal("已将 3 个游戏还原为默认 DLL。", LocalizationService.Text("Restored 3 game(s) to default DLLs."));
        }
        finally
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);
        }
    }

    [Fact]
    public void ChinesePreference_TranslatesDialogAndActionMessagePatterns()
    {
        try
        {
            LocalizationService.SetLanguagePreference(LocalizationService.SimplifiedChineseLanguage);

            Assert.Equal("在以下位置发现 dxgi.dll 文件：", LocalizationService.Text("A dxgi.dll file was found in:"));
            Assert.Equal("“bundle.zip”中有多个插件", LocalizationService.Text("Multiple Addons in 'bundle.zip'"));
            Assert.Equal("正在安装 OptiScaler...", LocalizationService.Text("Installing OptiScaler..."));
            Assert.Equal("✅ DXVK 已更新！", LocalizationService.Text("✅ DXVK updated!"));
            Assert.Equal("名为“HDR games”的筛选已存在。", LocalizationService.Text("A filter named \"HDR games\" already exists."));
            Assert.Equal("选择游戏 — preset.ini", LocalizationService.Text("Select Games — preset.ini"));
        }
        finally
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);
        }
    }

    [Fact]
    public void ChinesePreference_TranslatesDetailPanelDynamicText()
    {
        try
        {
            LocalizationService.SetLanguagePreference(LocalizationService.SimplifiedChineseLanguage);

            Assert.Equal("全局", LocalizationService.Text("Global"));
            Assert.Equal("自动", LocalizationService.Text("Auto"));
            Assert.Equal("默认", LocalizationService.Text("Default"));
            Assert.Equal("已包含", LocalizationService.Text("Included"));
            Assert.Equal("无插件", LocalizationService.Text("No Addons"));
            Assert.Equal("光线重建", LocalizationService.Text("Ray Reconstruction"));
            Assert.Equal("启用 UE Extended", LocalizationService.Text("Enable UE Extended"));
            Assert.Equal("隐藏", LocalizationService.Text("Hide"));
            Assert.Equal("自动检测（或粘贴路径）", LocalizationService.Text("Auto-detect (or paste path)"));
            Assert.Equal("⬇  安装 ReShade", LocalizationService.Text("⬇  Install ReShade"));
            Assert.Equal("↺  重装 RenoDX", LocalizationService.Text("↺  Reinstall RenoDX"));
            Assert.Equal("v1.9.8  ·  RankFTW 制作的 HDR 模组管理器", LocalizationService.Text("v1.9.8  ·  HDR mod manager by RankFTW"));
            Assert.Equal("覆盖启动此游戏时使用的可执行文件。留空则自动检测（安装文件夹中最大的 exe）。",
                LocalizationService.Text("Override the executable used when launching this game. Leave blank for auto-detection (largest exe in install folder)."));
            Assert.Equal("更改此游戏的渠道会影响所有 Vulkan 游戏。",
                LocalizationService.Text("Changing the channel for this game will change it for ALL Vulkan games."));

            var multiline = "Vulkan games share a global ReShade layer.\n\n"
                + "Changing the channel for this game will change it for ALL Vulkan games.";

            var translated = LocalizationService.Text(multiline);

            Assert.Contains("Vulkan 游戏共享一个全局 ReShade 层。", translated);
            Assert.Contains("更改此游戏的渠道会影响所有 Vulkan 游戏。", translated);
        }
        finally
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);
        }
    }

    [Fact]
    public void EnglishPreference_ReturnsSourceText()
    {
        try
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);

            Assert.Equal("Refresh", LocalizationService.Text("Refresh"));
            Assert.Equal("12 shown", LocalizationService.Format("{0} shown", 12));
        }
        finally
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);
        }
    }

    [Fact]
    public void ResolveSourceText_RecoversEnglishSourceFromChineseDisplayText()
    {
        try
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);

            Assert.Equal("All Games", LocalizationService.ResolveSourceText("全部游戏"));
            Assert.Equal("Installed", LocalizationService.ResolveSourceText("已安装"));
            Assert.Equal("Hide", LocalizationService.ResolveSourceText("隐藏"));
            Assert.Equal("Browse", LocalizationService.ResolveSourceText("浏览"));
            Assert.Equal("Patch Notes", LocalizationService.ResolveSourceText("更新说明"));
            Assert.Equal("Shaders/Addons", LocalizationService.ResolveSourceText("着色器/插件"));
            Assert.Equal("Update Inclusion", LocalizationService.ResolveSourceText("更新范围"));
            Assert.Equal("⬇ Install ReShade", LocalizationService.ResolveSourceText("⬇  安装 ReShade"));
            Assert.Equal("Refresh", LocalizationService.Text(LocalizationService.ResolveSourceText("刷新")));
        }
        finally
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);
        }
    }

    [Fact]
    public void ResolveSourceText_UsesPreferredSourceForAmbiguousChineseText()
    {
        try
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);

            Assert.Equal("Close", LocalizationService.ResolveSourceText("关闭", "Close"));
            Assert.Equal("Off", LocalizationService.ResolveSourceText("关闭", "Off"));
        }
        finally
        {
            LocalizationService.SetLanguagePreference(LocalizationService.EnglishLanguage);
        }
    }
}
