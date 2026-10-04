using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RenoDXCommander.Converters;
using RenoDXCommander.Localization;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

[CollectionDefinition("Localization", DisableParallelization = true)]
public class LocalizationCollection;

[Collection("Localization")]
public class LocalizationTests : IDisposable
{
    private readonly string _previousLanguage = Loc.Language;
    public void Dispose() => Loc.Initialize(_previousLanguage);

    [Fact]
    public void EnglishAndUnknownStringsKeepTheirOriginalText()
    {
        Loc.Initialize(Loc.English);
        Assert.Equal("Settings", Loc.Get("Settings"));
        Loc.Initialize(Loc.SimplifiedChinese);
        Assert.Equal("设置", Loc.Get("Settings"));
        Assert.Equal(@"D:\Games\My Game\dxgi.dll", Loc.Get(@"D:\Games\My Game\dxgi.dll"));
        Assert.Equal("An unknown server response", Loc.Get("An unknown server response"));
    }

    [Fact]
    public void InterpolationPreservesNamesPathsAndLiteralBraces()
    {
        Loc.Initialize(Loc.SimplifiedChinese);
        const string engine = "Unreal Engine 5.6";
        const string path = @"D:\Games\Example {Mod}\Binaries\Win64";
        Assert.Equal($"引擎：{engine}\n安装路径：{path}",
            Loc.Format($"Engine: {engine}\nInstall path: {path}"));
        Assert.Equal("已为 My Game 安装 ReShade。",
            Loc.Format($"{ "ReShade" } has been installed for { "My Game" }."));
    }

    [Fact]
    public void DropdownLabelsPreserveTheirInvariantSelectedValues()
    {
        Loc.Initialize(Loc.SimplifiedChinese);
        var converter = new LocalizedTextConverter();
        var options = new[] { "Stable", "Nightly", "Custom", "Default (310.8.2)" };
        var liveLabels = options.Select(option => (LocalizedValue)converter.Convert(option, typeof(string), null!, "")).ToArray();
        var labels = liveLabels.Select(label => label.Value).ToArray();
        Assert.Equal(new[] { "稳定版", "每日构建", "自定义", "默认（310.8.2）" }, labels);
        Assert.Equal(new[] { "Stable", "Nightly", "Custom", "Default (310.8.2)" }, options);
        Loc.Initialize(Loc.English);
        LocalizedValue.RefreshAll();
        Assert.Equal(options, liveLabels.Select(label => label.Value));
        Assert.Equal("Default (310.8.2)", Loc.Option(options[3]));
    }

    [Fact]
    public void LanguageChoiceRoundTripsThroughExistingSettings()
    {
        var settings = new SettingsViewModel { InterfaceLanguage = Loc.SimplifiedChinese };
        var saved = new Dictionary<string, string> { ["UnrelatedSetting"] = "keep" };
        settings.SaveSettingsToDict(saved);
        var reloaded = new SettingsViewModel();
        reloaded.LoadSettingsFromDict(saved);
        Assert.Equal(Loc.SimplifiedChinese, reloaded.InterfaceLanguage);
        Assert.Equal("keep", saved["UnrelatedSetting"]);
        saved["InterfaceLanguage"] = "invalid-language";
        reloaded.LoadSettingsFromDict(saved);
        Assert.Equal(Loc.English, reloaded.InterfaceLanguage);
    }

    [Fact]
    public void XamlCatalogSwitchesImmediatelyAndKeepsResourceKeysPaired()
    {
        HashSet<string> Keys(string language)
        {
            using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"RenoDXCommander.Localization.{language}.resw")!;
            return XDocument.Load(stream).Root!.Elements("data").Select(entry => entry.Attribute("name")!.Value).ToHashSet();
        }
        Assert.True(Keys(Loc.English).SetEquals(Keys(Loc.SimplifiedChinese)));
        var catalog = new XamlCatalog();
        var notified = false;
        catalog.PropertyChanged += (_, args) => notified |= args.PropertyName == "Item[]";
        Loc.Initialize(Loc.English);
        Assert.Equal("Language changes take effect immediately.", catalog["InterfaceLanguageRestart_Text"]);
        Loc.Initialize(Loc.SimplifiedChinese);
        catalog.Refresh();
        Assert.True(notified);
        Assert.Equal("切换语言后立即生效。", catalog["InterfaceLanguageRestart_Text"]);
        Loc.Initialize(Loc.English);
        catalog.Refresh();
        Assert.Equal("Language changes take effect immediately.", catalog["InterfaceLanguageRestart_Text"]);
    }

    [Fact]
    public void ExistingDisplayValuesSwitchBothWaysWithoutChangingUserData()
    {
        Loc.Initialize(Loc.English);
        var label = Loc.Get("Settings");
        var message = Loc.Format($"{ "ReShade" } has been installed for { "Settings" }.");
        // Equal content is deliberately a different object: it is a game name, not a UI label.
        var gameName = new string("Settings".AsSpan());
        Loc.Initialize(Loc.SimplifiedChinese);
        Assert.Equal("设置", Loc.Relocalize(label));
        Assert.Equal("已为 Settings 安装 ReShade。", Loc.Relocalize(message));
        Assert.Same(gameName, Loc.Relocalize(gameName));
        Loc.Initialize(Loc.English);
        Assert.Equal("Settings", Loc.Relocalize(label));
        Assert.Equal("ReShade has been installed for Settings.", Loc.Relocalize(message));
    }

    [Fact]
    public void LiveBindingsNotifyAndKeepConcatenatedMessageSources()
    {
        Loc.Initialize(Loc.SimplifiedChinese);
        var text = Loc.Concat(Loc.Get("Settings"), " · ", Loc.Get("Cancel"));
        var value = new LocalizedValue(() => Loc.Relocalize(text));
        var notifications = new List<string?>();
        value.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.Equal("设置 · 取消", value.Value);
        Loc.Initialize(Loc.English);
        LocalizedValue.RefreshAll();
        Assert.Equal("Settings · Cancel", value.Value);
        Assert.Contains(nameof(LocalizedValue.Value), notifications);
        Loc.Initialize(Loc.SimplifiedChinese);
        LocalizedValue.RefreshAll();
        Assert.Equal("设置 · 取消", value.Value);
    }

    [Fact]
    public void LiveCardRefreshPreservesInstallationAndSelectionState()
    {
        Loc.Initialize(Loc.English);
        var card = new GameCardViewModel
        {
            GameName = "Settings", InstallPath = @"D:\Games\Settings", IsSelected = true,
            IsInstalling = true, InstallProgress = 0.42, ActionMessage = Loc.Get("Installing..."),
        };
        Loc.Initialize(Loc.SimplifiedChinese);
        card.RefreshLocalizedText();
        Assert.Equal(Loc.Get("Installing..."), card.ActionMessage);
        Assert.Equal("Settings", card.GameName);
        Assert.Equal(@"D:\Games\Settings", card.InstallPath);
        Assert.True(card.IsSelected);
        Assert.True(card.IsInstalling);
        Assert.Equal(0.42, card.InstallProgress);
        Loc.Initialize(Loc.English);
        card.RefreshLocalizedText();
        Assert.Equal("Installing...", card.ActionMessage);
    }

    [Fact]
    public void CatalogTemplatesAcceptTheOriginalArguments()
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream("RenoDXCommander.Localization.zh-CN.json")!;
        var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        foreach (var (source, translated) in translations)
        {
            Assert.False(string.IsNullOrWhiteSpace(translated), source);
            if (!Regex.IsMatch(source, @"\{\d+(?:[,}:])")) continue;
            var original = CompositeFormat.Parse(source);
            var localized = CompositeFormat.Parse(translated);
            Assert.True(localized.MinimumArgumentCount <= original.MinimumArgumentCount, source);
            var arguments = Enumerable.Range(0, original.MinimumArgumentCount).Select(_ => (object)42).ToArray();
            _ = string.Format(CultureInfo.InvariantCulture, localized, arguments);
        }
    }
}
