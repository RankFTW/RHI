using System.Globalization;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace RenoDXCommander.Localization;

/// <summary>Application-owned display strings. Never translate persisted identifiers or file contents.</summary>
public static class Loc
{
    public const string English = "en-US";
    public const string SimplifiedChinese = "zh-CN";
    public static string Language { get; private set; } = English;
    // Track display strings by object identity, so a game name that happens to
    // match a UI label is never translated. Entries disappear with their strings.
    private static readonly ConditionalWeakTable<string, DisplayExpression> Expressions = new();
    private sealed record DisplayExpression(Func<string> Render);
    private static readonly Lazy<Dictionary<string, string>> Chinese = new(() =>
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream(
            "RenoDXCommander.Localization.zh-CN.json")
            ?? throw new InvalidOperationException("The Simplified Chinese translation catalog is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    public static string NormalizeLanguage(string? language) => language switch
    {
        SimplifiedChinese or "zh-Hans" or "zh-SG" => SimplifiedChinese,
        _ => English,
    };

    public static string DefaultLanguage =>
        CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? SimplifiedChinese : English;

    /// <summary>Set the display language; refresh existing bindings separately on the UI thread.</summary>
    public static void Initialize(string? language) =>
        Language = NormalizeLanguage(language ?? DefaultLanguage);

    private static string GetRaw(string source) =>
        Language == SimplifiedChinese && Chinese.Value.TryGetValue(source, out var translated)
            ? translated : source;

    private static string Remember(DisplayExpression expression)
    {
        var result = new string(expression.Render().AsSpan());
        if (result.Length > 0) Expressions.Add(result, expression);
        return result;
    }

    public static string Get(string source) => Expressions.TryGetValue(source, out var expression)
        ? Remember(expression)
        : Chinese.Value.ContainsKey(source) ? Remember(new(() => GetRaw(source))) : source;

    /// <summary>Re-render only text previously produced by this localizer.</summary>
    public static string Relocalize(string source) =>
        Expressions.TryGetValue(source, out var expression) ? Remember(expression) : source;

    /// <summary>Keep inserted names, paths, numbers and error details intact; translate only the template.</summary>
    public static string Format(FormattableString source) =>
        Remember(new(() => string.Format(CultureInfo.CurrentCulture, GetRaw(source.Format),
            source.GetArguments().Select(argument => argument is string text ? Relocalize(text) : argument).ToArray())));

    /// <summary>Keep the provenance of translated pieces when building a longer message.</summary>
    public static string Concat(params object?[] parts) => Remember(new(() => string.Concat(
        parts.Select(part => part is string text ? Relocalize(text) : part))));

    /// <summary>Only the visual template uses this; the original option remains the SelectedItem.</summary>
    public static string Option(string source)
    {
        if (source.StartsWith("Default (", StringComparison.Ordinal) && source.EndsWith(')'))
            return Format($"Default ({Get(source[9..^1])})");
        if (source.StartsWith("Global (", StringComparison.Ordinal) && source.EndsWith(')'))
            return Format($"Global ({Get(source[8..^1])})");
        return Get(source);
    }
}
