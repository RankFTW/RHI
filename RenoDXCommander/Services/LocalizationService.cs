using Microsoft.Windows.ApplicationModel.Resources;

namespace RenoDXCommander.Services;

public static class UiLanguage
{
    public const string Auto = "auto";
    public const string English = "en-US";
    public const string SimplifiedChinese = "zh-CN";

    public static readonly IReadOnlyList<string> SupportedLanguages =
    [
        English,
        SimplifiedChinese,
    ];

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return Auto;

        return language.Trim().ToLowerInvariant() switch
        {
            "auto" or "" => Auto,
            "en" or "english" or "en-us" => English,
            "zh" or "zh-hans" or "zh-cn" or "simplified chinese" => SimplifiedChinese,
            _ => language.Trim(),
        };
    }

    public static void Apply(string? language)
    {
        var normalized = Normalize(language);
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
            normalized == Auto ? string.Empty : normalized;
    }
}

public static class LocalizationService
{
    private static ResourceLoader? _resourceLoader;

    public static string GetString(string key, string fallback = "")
    {
        try
        {
            _resourceLoader ??= new ResourceLoader();
            var value = _resourceLoader.GetString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
