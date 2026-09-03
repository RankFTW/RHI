using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData(null, UiLanguage.Auto)]
    [InlineData("", UiLanguage.Auto)]
    [InlineData("AUTO", UiLanguage.Auto)]
    [InlineData("en", UiLanguage.English)]
    [InlineData("EN-US", UiLanguage.English)]
    [InlineData("zh", UiLanguage.SimplifiedChinese)]
    [InlineData("zh-HANS", UiLanguage.SimplifiedChinese)]
    [InlineData("zh-CN", UiLanguage.SimplifiedChinese)]
    public void Normalize_Aliases_ReturnCanonicalLanguage(string? input, string expected)
    {
        Assert.Equal(expected, UiLanguage.Normalize(input));
    }

    [Fact]
    public void SupportedLanguages_ContainsEnglishAndSimplifiedChinese()
    {
        Assert.Contains(UiLanguage.English, UiLanguage.SupportedLanguages);
        Assert.Contains(UiLanguage.SimplifiedChinese, UiLanguage.SupportedLanguages);
    }
}
