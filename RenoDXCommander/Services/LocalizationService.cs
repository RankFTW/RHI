using WinUI3Localizer;

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

    public static string Resolve(string? language)
    {
        var normalized = Normalize(language);
        if (normalized != Auto)
            return normalized;

        var uiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        if (string.Equals(uiCulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(uiCulture.Name, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uiCulture.Name, "zh-Hans", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uiCulture.Name, "zh-Hans-CN", StringComparison.OrdinalIgnoreCase)))
        {
            return SimplifiedChinese;
        }

        return English;
    }
}

public static class LocalizationService
{
    public static string StringsFolderPath { get; private set; } =
        System.IO.Path.Combine(GetExecutableDirectory(), "Strings");

    private static string GetExecutableDirectory() =>
        System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static async Task<bool> InitializeAsync(string? language)
    {
        try
        {
            _ = await new LocalizerBuilder()
                .AddStringResourcesFolderForLanguageDictionaries(StringsFolderPath, ignoreExceptions: true)
                .SetOptions(options => options.DefaultLanguage = UiLanguage.Resolve(language))
                .Build();
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.WriteCrashReport("LocalizationService.InitializeAsync", ex);
            return false;
        }
    }

    public static string GetString(string key, string fallback = "")
    {
        try
        {
            var value = Localizer.Get().GetLocalizedString(key);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
