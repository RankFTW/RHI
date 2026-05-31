using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander.Services;

/// <summary>
/// Centralized UI localization for the application.
/// English strings remain the source text; Simplified Chinese is resolved at runtime
/// from the current language preference.
/// </summary>
public static class LocalizationService
{
    public const string AutoLanguage = "auto";
    public const string EnglishLanguage = "en-US";
    public const string SimplifiedChineseLanguage = "zh-CN";

    private static string _languagePreference = AutoLanguage;
    private static string _effectiveLanguage = ResolveEffectiveLanguage(AutoLanguage, CultureInfo.CurrentUICulture);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Lazy<IReadOnlyDictionary<string, string>> SimplifiedChineseSourceLookup =
        new(BuildSimplifiedChineseSourceLookup);

    public static event EventHandler? LanguageChanged;

    public static string LanguagePreference => _languagePreference;
    public static string EffectiveLanguage => _effectiveLanguage;
    public static bool IsSimplifiedChinese => _effectiveLanguage == SimplifiedChineseLanguage;

    public static readonly DependencyProperty OriginalTextProperty =
        DependencyProperty.RegisterAttached("OriginalText", typeof(string), typeof(LocalizationService), new PropertyMetadata(null));

    public static readonly DependencyProperty OriginalContentProperty =
        DependencyProperty.RegisterAttached("OriginalContent", typeof(string), typeof(LocalizationService), new PropertyMetadata(null));

    public static readonly DependencyProperty OriginalPlaceholderTextProperty =
        DependencyProperty.RegisterAttached("OriginalPlaceholderText", typeof(string), typeof(LocalizationService), new PropertyMetadata(null));

    public static readonly DependencyProperty OriginalHeaderProperty =
        DependencyProperty.RegisterAttached("OriginalHeader", typeof(string), typeof(LocalizationService), new PropertyMetadata(null));

    public static readonly DependencyProperty OriginalOnContentProperty =
        DependencyProperty.RegisterAttached("OriginalOnContent", typeof(string), typeof(LocalizationService), new PropertyMetadata(null));

    public static readonly DependencyProperty OriginalOffContentProperty =
        DependencyProperty.RegisterAttached("OriginalOffContent", typeof(string), typeof(LocalizationService), new PropertyMetadata(null));

    public static readonly DependencyProperty OriginalToolTipProperty =
        DependencyProperty.RegisterAttached("OriginalToolTip", typeof(string), typeof(LocalizationService), new PropertyMetadata(null));

    public static string NormalizePreference(string? preference)
    {
        if (string.IsNullOrWhiteSpace(preference))
            return AutoLanguage;

        var value = preference.Trim();
        if (value.Equals(AutoLanguage, StringComparison.OrdinalIgnoreCase)
            || value.Equals("system", StringComparison.OrdinalIgnoreCase))
            return AutoLanguage;

        if (value.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase)
            || value.Equals("en", StringComparison.OrdinalIgnoreCase)
            || value.Equals("english", StringComparison.OrdinalIgnoreCase))
            return EnglishLanguage;

        if (value.Equals(SimplifiedChineseLanguage, StringComparison.OrdinalIgnoreCase)
            || value.Equals("zh", StringComparison.OrdinalIgnoreCase)
            || value.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
            || value.Equals("zh_CN", StringComparison.OrdinalIgnoreCase)
            || value.Equals("chinese", StringComparison.OrdinalIgnoreCase)
            || value.Equals("simplified-chinese", StringComparison.OrdinalIgnoreCase))
            return SimplifiedChineseLanguage;

        return AutoLanguage;
    }

    public static string ResolveEffectiveLanguage(string? preference, CultureInfo culture)
    {
        var normalized = NormalizePreference(preference);
        if (normalized != AutoLanguage)
            return normalized;

        var cultureName = culture.Name;
        if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(culture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase))
            return SimplifiedChineseLanguage;

        return EnglishLanguage;
    }

    public static void SetLanguagePreference(string? preference)
    {
        var normalized = NormalizePreference(preference);
        var effective = ResolveEffectiveLanguage(normalized, CultureInfo.CurrentUICulture);
        var changed = !_languagePreference.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || !_effectiveLanguage.Equals(effective, StringComparison.OrdinalIgnoreCase);

        _languagePreference = normalized;
        _effectiveLanguage = effective;

        var culture = CultureInfo.GetCultureInfo(_effectiveLanguage);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        if (changed)
            LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Text(string? english)
    {
        if (string.IsNullOrEmpty(english) || !IsSimplifiedChinese)
            return english ?? "";

        var normalized = NormalizeSourceText(english);
        if (SimplifiedChinese.TryGetValue(normalized, out var translated))
            return PreserveEdgeWhitespace(english, translated);

        return TranslateDynamicEnglish(english);
    }

    public static string Format(string englishFormat, params object[] args)
    {
        var format = Text(englishFormat);
        return string.Format(CultureInfo.CurrentCulture, format, args);
    }

    public static string LanguageDisplayName(string preference)
    {
        return NormalizePreference(preference) switch
        {
            AutoLanguage => Text("Automatic (system language)"),
            EnglishLanguage => Text("English"),
            SimplifiedChineseLanguage => Text("Simplified Chinese"),
            _ => Text("Automatic (system language)"),
        };
    }

    internal static string ResolveSourceText(string? displayedText, params string[] preferredSources)
    {
        if (string.IsNullOrEmpty(displayedText))
            return displayedText ?? "";

        var normalizedDisplayed = NormalizeSourceText(displayedText);
        foreach (var preferredSource in preferredSources)
        {
            if (string.IsNullOrWhiteSpace(preferredSource))
                continue;

            var normalizedPreferred = NormalizeSourceText(preferredSource);
            if (string.Equals(normalizedDisplayed, normalizedPreferred, StringComparison.Ordinal))
                return PreserveEdgeWhitespace(displayedText, preferredSource);

            if (SimplifiedChinese.TryGetValue(normalizedPreferred, out var preferredTranslation)
                && string.Equals(normalizedDisplayed, NormalizeSourceText(preferredTranslation), StringComparison.Ordinal))
                return PreserveEdgeWhitespace(displayedText, preferredSource);
        }

        if (SimplifiedChineseSourceLookup.Value.TryGetValue(normalizedDisplayed, out var sourceText))
            return PreserveEdgeWhitespace(displayedText, sourceText);

        return displayedText;
    }

    public static void SetText(TextBlock textBlock, string? sourceText, params string[] preferredSources)
    {
        var source = ResolveSourceText(sourceText, preferredSources);
        textBlock.SetValue(OriginalTextProperty, source);
        textBlock.Text = Text(source);
    }

    public static void SetText(Run run, string? sourceText, params string[] preferredSources)
    {
        var source = ResolveSourceText(sourceText, preferredSources);
        run.SetValue(OriginalTextProperty, source);
        run.Text = Text(source);
    }

    public static void SetContent(ContentControl contentControl, string? sourceText, params string[] preferredSources)
    {
        var source = ResolveSourceText(sourceText, preferredSources);
        contentControl.SetValue(OriginalContentProperty, source);
        contentControl.Content = Text(source);
    }

    public static void SetToolTip(DependencyObject element, string? sourceText, params string[] preferredSources)
    {
        var source = ResolveSourceText(sourceText, preferredSources);
        element.SetValue(OriginalToolTipProperty, source);
        ToolTipService.SetToolTip(element, Text(source));
    }

    public static void ApplyTo(ContentDialog dialog)
    {
        if (dialog.Title is string title)
            dialog.Title = Text(ResolveSourceText(title));
        if (dialog.Content is string content)
            dialog.Content = Text(ResolveSourceText(content));
        else if (dialog.Content is DependencyObject contentObject)
            ApplyTo(contentObject);

        dialog.PrimaryButtonText = Text(ResolveSourceText(dialog.PrimaryButtonText));
        dialog.SecondaryButtonText = Text(ResolveSourceText(dialog.SecondaryButtonText));
        dialog.CloseButtonText = Text(ResolveSourceText(dialog.CloseButtonText, "Close"));
    }

    public static void ApplyTo(DependencyObject? root)
    {
        if (root == null)
            return;

        ApplyElement(root);

        if (root is TextBlock textBlock)
        {
            foreach (var inline in textBlock.Inlines)
                ApplyInline(inline);
        }

        if (root is Button button && button.Flyout is { } flyout)
            ApplyFlyout(flyout);

        if (root is FrameworkElement { ContextFlyout: { } contextFlyout })
            ApplyFlyout(contextFlyout);

        if (root is ContentControl { Content: DependencyObject contentObject })
            ApplyTo(contentObject);

        if (root is ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is DependencyObject dependencyObject)
                    ApplyTo(dependencyObject);
            }
        }

        try
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
                ApplyTo(VisualTreeHelper.GetChild(root, i));
        }
        catch
        {
            // Some XAML text elements are DependencyObjects but not visual tree nodes.
        }
    }

    private static void ApplyInline(Inline inline)
    {
        if (inline is Run run)
            TranslateRun(run);
        else if (inline is Span span)
        {
            foreach (var child in span.Inlines)
                ApplyInline(child);
        }
    }

    private static void ApplyFlyout(FlyoutBase flyout)
    {
        if (flyout is MenuFlyout menuFlyout)
        {
            foreach (var item in menuFlyout.Items)
                ApplyMenuFlyoutItem(item);
        }
        else if (flyout is Flyout contentFlyout && contentFlyout.Content is DependencyObject content)
        {
            ApplyTo(content);
        }
    }

    private static void ApplyMenuFlyoutItem(MenuFlyoutItemBase item)
    {
        if (item is MenuFlyoutItem menuItem)
            TranslateMenuText(menuItem);
        else if (item is MenuFlyoutSubItem subItem)
        {
            TranslateMenuText(subItem);
            foreach (var child in subItem.Items)
                ApplyMenuFlyoutItem(child);
        }
    }

    private static void ApplyElement(DependencyObject element)
    {
        switch (element)
        {
            case TextBlock textBlock:
                TranslateTextBlock(textBlock);
                break;
            case Run run:
                TranslateRun(run);
                break;
            case TextBox textBox:
                TranslateHeader(textBox, textBox.Header, value => textBox.Header = value);
                TranslatePlaceholder(textBox);
                break;
            case ComboBox comboBox:
                TranslateHeader(comboBox, comboBox.Header, value => comboBox.Header = value);
                TranslatePlaceholder(comboBox);
                break;
            case ToggleSwitch toggleSwitch:
                TranslateToggleSwitch(toggleSwitch);
                break;
            case ContentControl contentControl:
                TranslateContent(contentControl);
                break;
            case MenuFlyoutItem menuItem:
                TranslateMenuText(menuItem);
                break;
            case MenuFlyoutSubItem subItem:
                TranslateMenuText(subItem);
                break;
        }

        TranslateToolTip(element);
    }

    private static void TranslateTextBlock(TextBlock textBlock)
    {
        if (string.IsNullOrWhiteSpace(textBlock.Text))
            return;

        var original = GetOrSetOriginal(textBlock, OriginalTextProperty, textBlock.Text);
        textBlock.Text = Text(original);
    }

    private static void TranslateRun(Run run)
    {
        if (string.IsNullOrWhiteSpace(run.Text))
            return;

        var original = GetOrSetOriginal(run, OriginalTextProperty, run.Text);
        run.Text = Text(original);
    }

    private static void TranslatePlaceholder(TextBox textBox)
    {
        if (string.IsNullOrWhiteSpace(textBox.PlaceholderText))
            return;

        var original = GetOrSetOriginal(textBox, OriginalPlaceholderTextProperty, textBox.PlaceholderText);
        textBox.PlaceholderText = Text(original);
    }

    private static void TranslatePlaceholder(ComboBox comboBox)
    {
        if (string.IsNullOrWhiteSpace(comboBox.PlaceholderText))
            return;

        var original = GetOrSetOriginal(comboBox, OriginalPlaceholderTextProperty, comboBox.PlaceholderText);
        comboBox.PlaceholderText = Text(original);
    }

    private static void TranslateHeader(DependencyObject element, object? header, Action<object?> setHeader)
    {
        if (header is not string text || string.IsNullOrWhiteSpace(text))
            return;

        var original = GetOrSetOriginal(element, OriginalHeaderProperty, text);
        setHeader(Text(original));
    }

    private static void TranslateContent(ContentControl contentControl)
    {
        if (contentControl.Content is not string content || string.IsNullOrWhiteSpace(content))
            return;

        var original = GetOrSetOriginal(contentControl, OriginalContentProperty, content);
        contentControl.Content = Text(original);
    }

    private static void TranslateToggleSwitch(ToggleSwitch toggleSwitch)
    {
        TranslateTogglePart(toggleSwitch, TogglePart.Header);
        TranslateTogglePart(toggleSwitch, TogglePart.OnContent);
        TranslateTogglePart(toggleSwitch, TogglePart.OffContent);
    }

    private static void TranslateTogglePart(ToggleSwitch toggleSwitch, TogglePart part)
    {
        var (value, property) = part switch
        {
            TogglePart.Header => (toggleSwitch.Header, OriginalHeaderProperty),
            TogglePart.OnContent => (toggleSwitch.OnContent, OriginalOnContentProperty),
            _ => (toggleSwitch.OffContent, OriginalOffContentProperty),
        };

        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return;

        var preferredSources = part switch
        {
            TogglePart.OnContent => new[] { "On" },
            TogglePart.OffContent => new[] { "Off" },
            _ => Array.Empty<string>(),
        };

        var original = GetOrSetOriginal(toggleSwitch, property, text, preferredSources);
        var translated = Text(original);
        switch (part)
        {
            case TogglePart.Header:
                toggleSwitch.Header = translated;
                break;
            case TogglePart.OnContent:
                toggleSwitch.OnContent = translated;
                break;
            case TogglePart.OffContent:
                toggleSwitch.OffContent = translated;
                break;
        }
    }

    private static void TranslateMenuText(MenuFlyoutItem menuItem)
    {
        if (string.IsNullOrWhiteSpace(menuItem.Text))
            return;

        var original = GetOrSetOriginal(menuItem, OriginalTextProperty, menuItem.Text);
        menuItem.Text = Text(original);
    }

    private static void TranslateMenuText(MenuFlyoutSubItem subItem)
    {
        if (string.IsNullOrWhiteSpace(subItem.Text))
            return;

        var original = GetOrSetOriginal(subItem, OriginalTextProperty, subItem.Text);
        subItem.Text = Text(original);
    }

    private static void TranslateToolTip(DependencyObject element)
    {
        var tooltip = ToolTipService.GetToolTip(element);
        if (tooltip is string tooltipText && !string.IsNullOrWhiteSpace(tooltipText))
        {
            var original = GetOrSetOriginal(element, OriginalToolTipProperty, tooltipText);
            ToolTipService.SetToolTip(element, Text(original));
        }
        else if (tooltip is ToolTip { Content: string content } tooltipElement)
        {
            var original = GetOrSetOriginal(tooltipElement, OriginalContentProperty, content);
            tooltipElement.Content = Text(original);
        }
        else if (tooltip is DependencyObject tooltipObject)
        {
            ApplyTo(tooltipObject);
        }
    }

    private static string GetOrSetOriginal(
        DependencyObject element,
        DependencyProperty property,
        string displayedText,
        params string[] preferredSources)
    {
        var original = (string?)element.GetValue(property);
        if (original != null)
            return original;

        original = ResolveSourceText(displayedText, preferredSources);
        element.SetValue(property, original);
        return original;
    }

    private static string TranslateDynamicEnglish(string english)
    {
        if (!IsSimplifiedChinese)
            return english;

        if (english.Contains('\n'))
        {
            var normalizedLines = english.Replace("\r\n", "\n").Split('\n');
            return string.Join("\n", normalizedLines.Select(line =>
                string.IsNullOrWhiteSpace(line) ? line : Text(line)));
        }

        var trimmed = english.Trim();

        var match = Regex.Match(trimmed, @"^v(?<version>.+?)\s+·\s+HDR mod manager by RankFTW$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"v{match.Groups["version"].Value}  ·  {Text("HDR mod manager by RankFTW")}";

        match = Regex.Match(trimmed, @"^Updated (?<count>\d+) (?<file>.+?) file(?<plural>s?)\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已更新 {match.Groups["count"].Value} 个 {match.Groups["file"].Value} 文件。";

        match = Regex.Match(trimmed, @"^Downloading (?<name>.+?)\.\.\.(?<detail>.*)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"正在下载 {match.Groups["name"].Value}...{match.Groups["detail"].Value}";

        match = Regex.Match(trimmed, @"^Restoring (?<name>.+?)\.\.\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"正在还原 {match.Groups["name"].Value}...";

        match = Regex.Match(trimmed, @"^Installed:\s+v(?<version>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"当前版本：v{match.Groups["version"].Value}";

        match = Regex.Match(trimmed, @"^Installed:\s+(?<version>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已安装：{match.Groups["version"].Value}";

        match = Regex.Match(trimmed, @"^Available:\s+v(?<version>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"可用版本：v{match.Groups["version"].Value}";

        match = Regex.Match(trimmed, @"^(?<component>.+?) deployed to (?<count>\d+) game\(s\)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"{match.Groups["component"].Value} 已部署到 {match.Groups["count"].Value} 个游戏";

        match = Regex.Match(trimmed, @"^(?<component>.+?) applied to (?<count>\d+) game\(s\)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"{match.Groups["component"].Value} 已应用到 {match.Groups["count"].Value} 个游戏";

        match = Regex.Match(trimmed, @"^NVIDIA profiles created: (?<count>\d+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已创建 NVIDIA 配置文件：{match.Groups["count"].Value}";

        match = Regex.Match(trimmed, @"^Skipped: (?<count>\d+) \((?<reason>.+)\)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已跳过：{match.Groups["count"].Value}（{Text(match.Groups["reason"].Value)}）";

        match = Regex.Match(trimmed, @"^Presets missed: (?<count>\d+) game\(s\) \(no NVIDIA profile found\)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"预设未应用：{match.Groups["count"].Value} 个游戏（未找到 NVIDIA 配置文件）";

        match = Regex.Match(trimmed, @"^Restored (?<count>\d+) game\(s\) to default DLLs\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已将 {match.Groups["count"].Value} 个游戏还原为默认 DLL。";

        match = Regex.Match(trimmed, @"^Reset presets to Default on (?<count>\d+) game\(s\)\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已将 {match.Groups["count"].Value} 个游戏的预设重置为默认值。";

        match = Regex.Match(trimmed, @"^DXVK variant changed to (?<variant>.+)\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"DXVK 变体已更改为 {Text(match.Groups["variant"].Value)}。";

        match = Regex.Match(trimmed, @"^Switching (?<count>\d+) game\(s\) to the (?<variant>.+?) build\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"正在将 {match.Groups["count"].Value} 个游戏切换到 {Text(match.Groups["variant"].Value)} 构建。";

        match = Regex.Match(trimmed, @"^ReShade build channel changed to (?<channel>.+)\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"ReShade 构建渠道已更改为 {Text(match.Groups["channel"].Value)}。";

        match = Regex.Match(trimmed, @"^(?<count>\d+) Vulkan game\(s\) updated via global layer\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已通过全局层更新 {match.Groups["count"].Value} 个 Vulkan 游戏。";

        match = Regex.Match(trimmed, @"^(?<count>\d+) games?$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"{match.Groups["count"].Value} 个游戏";

        match = Regex.Match(trimmed, @"^(?<count>\d+) shown$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"显示 {match.Groups["count"].Value} 个";

        match = Regex.Match(trimmed, @"^(?<count>\d+) installed$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已安装 {match.Groups["count"].Value} 个";

        match = Regex.Match(trimmed, @"^· (?<count>\d+) hidden$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"· 已隐藏 {match.Groups["count"].Value} 个";

        match = Regex.Match(trimmed, @"^Library loaded \((?<count>\d+) games, scanned (?<age>.+)\)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已加载游戏库（{match.Groups["count"].Value} 个游戏，扫描于 {Text(match.Groups["age"].Value)}）";

        match = Regex.Match(trimmed, @"^(?<count>\d+) games detected · offline mode \(mod info unavailable\)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"检测到 {match.Groups["count"].Value} 个游戏 · 离线模式（模组信息不可用）";

        match = Regex.Match(trimmed, @"^(?<count>\d+) games detected · (?<mods>\d+) mods installed$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"检测到 {match.Groups["count"].Value} 个游戏 · 已安装 {match.Groups["mods"].Value} 个模组";

        match = Regex.Match(trimmed, @"^(?<count>\d+)m ago$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"{match.Groups["count"].Value} 分钟前";

        match = Regex.Match(trimmed, @"^(?<count>\d+)h ago$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"{match.Groups["count"].Value} 小时前";

        match = Regex.Match(trimmed, @"^(?<count>\d+)d ago$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"{match.Groups["count"].Value} 天前";

        match = Regex.Match(trimmed, @"^Mod author: (?<author>.+?) — click to open Ko-fi donation page$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"模组作者：{match.Groups["author"].Value} — 点击打开 Ko-fi 捐赠页面";

        match = Regex.Match(trimmed, @"^Mod author: (?<author>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"模组作者：{match.Groups["author"].Value}";

        match = Regex.Match(trimmed, @"^Open (?<source>.+?)'s ultrawide fix page$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"打开 {match.Groups["source"].Value} 的超宽屏修复页面";

        match = Regex.Match(trimmed, @"^A (?<file>dxgi\.dll|winmm\.dll) file was found in:$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"在以下位置发现 {match.Groups["file"].Value} 文件：";

        match = Regex.Match(trimmed, @"^File size: (?<size>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"文件大小：{match.Groups["size"].Value}";

        match = Regex.Match(trimmed, @"^Failed to extract '(?<name>.+?)'\. The file may be corrupt or in an unsupported format\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"无法解压“{match.Groups["name"].Value}”。文件可能已损坏，或格式不受支持。";

        match = Regex.Match(trimmed, @"^No \.addon64 or \.addon32 files were found inside '(?<name>.+?)'\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"“{match.Groups["name"].Value}”中没有找到 .addon64 或 .addon32 文件。";

        match = Regex.Match(trimmed, @"^Multiple Addons in '(?<name>.+?)'$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"“{match.Groups["name"].Value}”中有多个插件";

        match = Regex.Match(trimmed, @"^Install (?<name>.+?) to a game folder\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"将 {match.Groups["name"].Value} 安装到游戏文件夹。";

        match = Regex.Match(trimmed, @"^Are you sure you want to install (?<addon>.+?) for (?<game>.+?)\?$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"确定要为 {match.Groups["game"].Value} 安装 {match.Groups["addon"].Value} 吗？";

        match = Regex.Match(trimmed, @"^This will replace the existing addon: (?<addon>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"这会替换现有插件：{match.Groups["addon"].Value}";

        match = Regex.Match(trimmed, @"^Install path: (?<path>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"安装路径：{match.Groups["path"].Value}";

        match = Regex.Match(trimmed, @"^(?<addon>.+?) has been installed for (?<game>.+?)\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"{match.Groups["addon"].Value} 已为 {match.Groups["game"].Value} 安装。";

        match = Regex.Match(trimmed, @"^Failed to install addon: (?<message>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"插件安装失败：{match.Groups["message"].Value}";

        match = Regex.Match(trimmed, @"^Luma mod detected: (?<name>.+?) Select game to install to:$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"检测到 Luma 模组：{match.Groups["name"].Value} 请选择要安装到的游戏：";

        match = Regex.Match(trimmed, @"^Downloading (?<name>.+?)\.\.\. (?<kb>\d+) KB(?: \((?<pct>.+?)\))?$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var suffix = match.Groups["pct"].Success ? $"（{match.Groups["pct"].Value}）" : "";
            return $"正在下载 {match.Groups["name"].Value}... {match.Groups["kb"].Value} KB{suffix}";
        }

        match = Regex.Match(trimmed, @"^The server returned HTTP (?<code>\d+)\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"服务器返回 HTTP {match.Groups["code"].Value}。";

        match = Regex.Match(trimmed, @"^URL: (?<url>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"URL：{match.Groups["url"].Value}";

        match = Regex.Match(trimmed, @"^A network error occurred while downloading the addon\. (?<message>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"下载插件时发生网络错误。{match.Groups["message"].Value}";

        match = Regex.Match(trimmed, @"^Only \.addon64 and \.addon32 files are supported\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return "仅支持 .addon64 和 .addon32 文件。";

        match = Regex.Match(trimmed, @"^The URL points to: (?<name>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"该 URL 指向：{match.Groups["name"].Value}";

        match = Regex.Match(trimmed, @"^""(?<game>.+?)"" is already in your library at: (?<path>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"“{match.Groups["game"].Value}”已在游戏库中：{match.Groups["path"].Value}";

        match = Regex.Match(trimmed, @"^Engine: (?<engine>.+?) Install path: (?<path>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"引擎：{match.Groups["engine"].Value} 安装路径：{match.Groups["path"].Value}";

        match = Regex.Match(trimmed, @"^Failed to (?<verb>read|save|copy) (?<target>.+?): (?<message>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var verb = match.Groups["verb"].Value.ToLowerInvariant() switch
            {
                "read" => "读取",
                "save" => "保存",
                _ => "复制"
            };
            return $"{verb}{Text(match.Groups["target"].Value)}失败：{match.Groups["message"].Value}";
        }

        match = Regex.Match(trimmed, @"^(?<operation>Installing|Removing|Updating) (?<component>.+?)\.\.\.$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var operation = match.Groups["operation"].Value.ToLowerInvariant() switch
            {
                "installing" => "正在安装",
                "removing" => "正在移除",
                _ => "正在更新"
            };
            return $"{operation} {match.Groups["component"].Value}...";
        }

        match = Regex.Match(trimmed, @"^Starting (?<component>.+?) download\.\.\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"正在开始下载 {match.Groups["component"].Value}...";

        match = Regex.Match(trimmed, @"^✅ (?<component>.+?) installed!$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"✅ {match.Groups["component"].Value} 已安装！";

        match = Regex.Match(trimmed, @"^✅ (?<component>.+?) updated!$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"✅ {match.Groups["component"].Value} 已更新！";

        match = Regex.Match(trimmed, @"^✖ (?<component>.+?) removed\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"✖ {match.Groups["component"].Value} 已移除。";

        match = Regex.Match(trimmed, @"^❌ (?<operation>Install|Uninstall|Update) failed: (?<message>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var operation = match.Groups["operation"].Value.ToLowerInvariant() switch
            {
                "install" => "安装",
                "uninstall" => "卸载",
                _ => "更新"
            };
            return $"❌ {operation}失败：{match.Groups["message"].Value}";
        }

        match = Regex.Match(trimmed, @"^❌ Failed: (?<message>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"❌ 失败：{match.Groups["message"].Value}";

        match = Regex.Match(trimmed, @"^❌ (?<component>.+?) Failed: (?<message>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"❌ {match.Groups["component"].Value} 失败：{match.Groups["message"].Value}";

        match = Regex.Match(trimmed, @"^Failed to read the file: (?<message>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"读取文件失败：{match.Groups["message"].Value}";

        match = Regex.Match(trimmed, @"^Failed to save preset to the presets folder: (?<message>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"保存预设到预设文件夹失败：{match.Groups["message"].Value}";

        match = Regex.Match(trimmed, @"^Failed to copy preset to game folder: (?<message>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"复制预设到游戏文件夹失败：{match.Groups["message"].Value}";

        match = Regex.Match(trimmed, @"^Presets deployed to (?<count>\d+) game\(s\)\. Also install the required shader packs for these games\?$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"预设已部署到 {match.Groups["count"].Value} 个游戏。是否也为这些游戏安装所需的着色器包？";

        match = Regex.Match(trimmed, @"^Presets deployed to (?<count>\d+) game\(s\)\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"预设已部署到 {match.Groups["count"].Value} 个游戏。";

        match = Regex.Match(trimmed, @"^Select Games — (?<items>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"选择游戏 — {match.Groups["items"].Value}";

        match = Regex.Match(trimmed, @"^Save the current search ""(?<query>.*)"" as a custom filter:$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"将当前搜索“{match.Groups["query"].Value}”保存为自定义筛选：";

        match = Regex.Match(trimmed, @"^A filter named ""(?<name>.+)"" already exists\.$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"名为“{match.Groups["name"].Value}”的筛选已存在。";

        match = Regex.Match(trimmed, @"^Selected: (?<path>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"已选择：{match.Groups["path"].Value}";

        match = Regex.Match(trimmed, @"^Upscalers: (?<items>.+)$", RegexOptions.IgnoreCase);
        if (match.Success)
            return $"升频器：{match.Groups["items"].Value}";

        return english;
    }

    private static string NormalizeSourceText(string value)
    {
        var normalized = value.Replace("\r\n", "\n").Trim();
        normalized = WhitespaceRegex.Replace(normalized, " ");
        return normalized;
    }

    private static string PreserveEdgeWhitespace(string source, string translated)
    {
        var leading = source.Length - source.TrimStart().Length;
        var trailing = source.Length - source.TrimEnd().Length;
        return source[..leading] + translated + source[(source.Length - trailing)..];
    }

    private static IReadOnlyDictionary<string, string> BuildSimplifiedChineseSourceLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (source, translated) in SimplifiedChinese)
        {
            var normalizedTranslated = NormalizeSourceText(translated);
            if (!string.IsNullOrWhiteSpace(normalizedTranslated) && !lookup.ContainsKey(normalizedTranslated))
                lookup[normalizedTranslated] = source;
        }

        return lookup;
    }

    private enum TogglePart
    {
        Header,
        OnContent,
        OffContent,
    }

    private static readonly Dictionary<string, string> SimplifiedChinese = new(StringComparer.Ordinal)
    {
        // Language
        ["Language"] = "语言",
        ["Automatic (system language)"] = "自动（跟随系统语言）",
        ["English"] = "English",
        ["Simplified Chinese"] = "简体中文",
        ["Choose the display language. Automatic uses Windows display language at launch and falls back to English for unsupported languages."] = "选择界面显示语言。自动模式会在启动时跟随 Windows 显示语言；不支持的语言会回退到英文。",

        // Global navigation and toolbar
        ["RHI"] = "RHI",
        ["ReShade HDR Installer"] = "ReShade HDR 安装器",
        ["HDR mod manager by RankFTW"] = "RankFTW 制作的 HDR 模组管理器",
        ["Refresh"] = "刷新",
        ["Rescan game library and fetch latest mod info"] = "重新扫描游戏库并获取最新模组信息",
        ["Shaders/Addons"] = "着色器/插件",
        ["Manage global shaders and ReShade addons"] = "管理全局着色器和 ReShade 插件",
        ["Global Shaders"] = "全局着色器",
        ["ReShade Addons"] = "ReShade 插件",
        ["Update All"] = "全部更新",
        ["Update ReShade, RenoDX, ReLimiter, Display Commander, and RE Framework for all games"] = "为所有游戏更新 ReShade、RenoDX、ReLimiter、Display Commander 和 RE Framework",
        ["Links"] = "链接",
        ["Useful links and resources"] = "常用链接和资源",
        ["Help"] = "帮助",
        ["Support and help resources"] = "支持与帮助资源",
        ["Guide"] = "指南",
        ["About"] = "关于",
        ["Views"] = "视图",
        ["Switch between view layouts"] = "切换视图布局",
        ["Compact"] = "紧凑",
        ["Detail"] = "详情",
        ["Grid"] = "网格",
        ["Settings"] = "设置",
        ["Open settings"] = "打开设置",
        ["Loading..."] = "正在加载...",
        ["Patch Notes"] = "更新说明",
        ["View recent patch notes"] = "查看最近更新说明",
        ["⚠ Single-player only — ReShade with addon support and OptiScaler may trigger anti-cheat in online/multiplayer games"] = "⚠ 仅建议单人游戏使用。带插件支持的 ReShade 和 OptiScaler 可能触发在线/多人游戏的反作弊系统",

        // Sidebar and filters
        ["Filter games..."] = "筛选游戏...",
        ["Save current search as a custom filter"] = "将当前搜索保存为自定义筛选",
        ["All Games"] = "全部游戏",
        ["Installed"] = "已安装",
        ["Favourites"] = "收藏",
        ["Hidden"] = "已隐藏",
        ["Unreal"] = "Unreal",
        ["Unity"] = "Unity",
        ["Other"] = "其他",
        ["0 shown"] = "显示 0 个",
        ["0 installed"] = "已安装 0 个",
        ["▶ Launch"] = "▶ 启动",
        ["Launch this game"] = "启动此游戏",
        ["Open Nexus Mods page"] = "打开 Nexus Mods 页面",
        ["Open PCGamingWiki page"] = "打开 PCGamingWiki 页面",
        ["Open ultrawide fix page"] = "打开超宽屏修复页面",
        ["Open Ultra+ page for this game"] = "打开此游戏的 Ultra+ 页面",
        ["Hide this game from the sidebar (find it again in the Hidden filter)"] = "从侧边栏隐藏此游戏（可在“已隐藏”筛选中找回）",
        ["Toggle favourite"] = "切换收藏状态",
        ["Favourite"] = "收藏",
        ["Show"] = "显示",
        ["Hide"] = "隐藏",

        // Detail panel and component controls
        ["Browse"] = "浏览",
        ["Open game folder in Explorer"] = "在资源管理器中打开游戏文件夹",
        ["Components"] = "组件",
        ["Install All"] = "全部安装",
        ["Info"] = "信息",
        ["Remove RE Framework"] = "移除 RE Framework",
        ["Copy ReShade.ini & ReShadePreset.ini"] = "复制 ReShade.ini 和 ReShadePreset.ini",
        ["Remove ReShade"] = "移除 ReShade",
        ["Toggle UE Extended"] = "切换 UE Extended",
        ["Remove RenoDX mod"] = "移除 RenoDX 模组",
        ["Copy ReShade.ini to game folder"] = "复制 ReShade.ini 到游戏文件夹",
        ["Remove Luma mod"] = "移除 Luma 模组",
        ["——— Frame limiters — Choose one ———"] = "———  帧率限制器：请选择一个  ———",
        ["Copy relimiter.ini to game folder"] = "复制 relimiter.ini 到游戏文件夹",
        ["Remove ReLimiter"] = "移除 ReLimiter",
        ["Copy DisplayCommander.ini to game folder"] = "复制 DisplayCommander.ini 到游戏文件夹",
        ["Remove Display Commander"] = "移除 Display Commander",
        ["── Optional ──"] = "──  可选组件  ──",
        ["Copy OptiScaler.ini to game folder"] = "复制 OptiScaler.ini 到游戏文件夹",
        ["Remove OptiScaler"] = "移除 OptiScaler",
        ["Copy dxvk.conf to game folder"] = "复制 dxvk.conf 到游戏文件夹",
        ["Remove DXVK"] = "移除 DXVK",
        ["No RenoDX mod available for this game"] = "此游戏没有可用的 RenoDX 模组",
        ["Open discussion / instructions"] = "打开讨论或安装说明",
        ["View notes"] = "查看备注",
        ["Overrides"] = "覆盖设置",
        ["Wiki"] = "百科",

        // Settings page
        ["← Back to Games"] = "← 返回游戏",
        ["Add Game"] = "添加游戏",
        ["Manually add a game that wasn't automatically detected. Select the game's exe and you'll be asked to name it."] = "手动添加未被自动检测到的游戏。选择游戏的 exe 文件后，程序会要求你为它命名。",
        ["Manually add a game folder"] = "手动添加游戏文件夹",
        ["Full Refresh"] = "完整刷新",
        ["Clears all caches, re-scans everything from disk, and forces a fresh update check for all components."] = "清除所有缓存，从磁盘重新扫描所有内容，并强制为全部组件重新检查更新。",
        ["Full refresh — clears all caches, re-scans from disk, and forces update checks"] = "完整刷新：清除所有缓存、重新扫描磁盘并强制检查更新",
        ["Screenshots"] = "截图",
        ["Set a screenshot save path that will be written to all managed reshade.ini files."] = "设置截图保存路径；该路径会写入所有由 RHI 管理的 reshade.ini 文件。",
        ["Hotkeys"] = "热键",
        ["ReShade key bindings."] = "ReShade 按键绑定。",
        ["Overlay toggle key"] = "覆盖层开关键",
        ["Screenshot key"] = "截图键",
        ["Pick a folder for screenshots"] = "选择截图保存文件夹",
        ["Open"] = "打开",
        ["Open the screenshot folder in Explorer"] = "在资源管理器中打开截图文件夹",
        ["Press a key..."] = "按下一个按键...",
        ["Reset to Print Screen"] = "重置为 Print Screen",
        ["Per-Game Subfolder"] = "按游戏建立子文件夹",
        ["Apply to All Games"] = "应用到所有游戏",
        ["Enabled — each game gets its own subfolder"] = "已启用：每个游戏使用独立子文件夹",
        ["Disabled — all screenshots in one folder"] = "已禁用：所有截图保存到同一个文件夹",
        ["ReLimiter OSD toggle key"] = "ReLimiter 屏显开关键",
        ["Shared OSD Presets"] = "共享屏显预设",
        ["On"] = "开启",
        ["Off"] = "关闭",
        ["Mass DLSS & Streamline Deployment"] = "批量部署 DLSS 和 Streamline",
        ["Deploy DLSS and Streamline DLL versions and DLSS presets to multiple games at once. Backs up originals automatically. Games without DLSS/Streamline or with v1.x versions are skipped."] = "一次性为多个游戏部署 DLSS、Streamline DLL 版本和 DLSS 预设。原文件会自动备份；没有 DLSS/Streamline 或使用 v1.x 版本的游戏会被跳过。",
        ["Batch Deploy"] = "批量部署",
        ["DLSS On-Screen Indicator"] = "DLSS 屏幕指示器",
        ["Controls the DLSS text overlay that NVIDIA shows in the corner of games when DLSS is active. This is a global system setting — it affects all games. Requires admin privileges to change."] = "控制 NVIDIA 在 DLSS 启用时显示在游戏角落的 DLSS 文本叠加层。这是全局系统设置，会影响所有游戏。修改时需要管理员权限。",
        ["Enabled"] = "启用",
        ["Disabled"] = "禁用",
        ["OptiScaler Settings"] = "OptiScaler 设置",
        ["Configure GPU type and DLSS input settings for OptiScaler installations. DLSS input toggle is for AMD/Intel GPUs only — NVIDIA users do not need it."] = "配置 OptiScaler 安装使用的 GPU 类型和 DLSS 输入设置。DLSS 输入开关仅适用于 AMD/Intel GPU；NVIDIA 用户通常不需要。",
        ["Hotkey"] = "热键",
        ["OptiScaler overlay toggle key"] = "OptiScaler 覆盖层开关键",
        ["GPU Type"] = "GPU 类型",
        ["Use DLSS Inputs"] = "使用 DLSS 输入",
        ["Yes"] = "是",
        ["No"] = "否",
        ["Global Update Checks"] = "全局更新检查",
        ["Disable update checks for individual components. When disabled, the component will not be checked for updates during startup or when using Update All."] = "可单独禁用某些组件的更新检查。禁用后，该组件在启动和执行“全部更新”时都不会检查更新。",
        ["Addon Watch Folder"] = "插件监视文件夹",
        ["RHI watches this folder for RenoDX addon files (.addon64/.addon32) and archives containing them. Defaults to your Downloads folder."] = "RHI 会监视此文件夹中的 RenoDX 插件文件（.addon64/.addon32）以及包含这些文件的压缩包。默认使用你的下载文件夹。",
        ["Update Inclusion"] = "更新范围",
        ["Downloads folder (default)"] = "下载文件夹（默认）",
        ["Reset"] = "重置",
        ["Custom Shaders"] = "自定义着色器",
        ["Use your own shader and texture files from a custom directory instead of the built-in shader packs. Files are sourced from %LocalAppData%\\RHI\\reshade\\Custom\\."] = "使用自定义目录中的着色器和纹理文件，而不是内置着色器包。文件来源为 %LocalAppData%\\RHI\\reshade\\Custom\\。",
        ["Use Custom Shaders"] = "使用自定义着色器",
        ["Enabled — deploying from custom directories"] = "已启用：从自定义目录部署",
        ["Disabled — using shader packs"] = "已禁用：使用着色器包",
        ["Shader Cache"] = "着色器缓存",
        ["When enabled, all shader packs are downloaded and cached on startup. When disabled, shader packs are only downloaded when needed (e.g. when you select them or install ReShade). Existing cached shaders are kept either way."] = "启用后，启动时会下载并缓存所有着色器包。禁用后，只在需要时下载，例如你选择某个包或安装 ReShade 时。已有缓存不会被删除。",
        ["Cache All Shaders"] = "缓存全部着色器",
        ["Build Channels"] = "构建渠道",
        ["Choose the build source for ReShade and DXVK. Stable uses official tagged releases. Nightly/Development uses the latest builds from GitHub Actions (may be unstable but includes the newest fixes)."] = "选择 ReShade 和 DXVK 的构建来源。稳定版使用官方标记发布；夜间版/开发版使用 GitHub Actions 最新构建，可能不稳定，但包含最新修复。",
        ["ReShade Build Channel"] = "ReShade 构建渠道",
        ["Stable (reshade.me releases)"] = "稳定版（reshade.me 发布）",
        ["Nightly (GitHub Actions builds)"] = "夜间版（GitHub Actions 构建）",
        ["DXVK Variant"] = "DXVK 变体",
        ["Development (nightly builds)"] = "开发版（夜间构建）",
        ["Stable (tagged releases)"] = "稳定版（标记发布）",
        ["Lilium HDR"] = "Lilium HDR",
        ["Mass Deployment"] = "批量部署",
        ["Deploy INI files or ReShade presets to multiple games at once. INI deployment overwrites existing files in game folders — custom hotkey and screenshot path settings are preserved."] = "一次性将 INI 文件或 ReShade 预设部署到多个游戏。INI 部署会覆盖游戏文件夹中的现有文件，但会保留自定义热键和截图路径设置。",
        ["Deploy reshade.ini to All Games"] = "将 reshade.ini 部署到所有游戏",
        ["Deploy relimiter.ini to All Games"] = "将 relimiter.ini 部署到所有游戏",
        ["Deploy DisplayCommander.ini to All Games"] = "将 DisplayCommander.ini 部署到所有游戏",
        ["Deploy OptiScaler.ini to All Games"] = "将 OptiScaler.ini 部署到所有游戏",
        ["Mass Preset Install"] = "批量安装预设",
        ["Data & Custom Files"] = "数据和自定义文件",
        ["All RHI data is stored in AppData. Drop custom ReShade, DLSS, or Streamline DLLs into the Custom folder to use them as per-game overrides. The Logs folder contains session usage logs useful for troubleshooting."] = "所有 RHI 数据都存储在 AppData 中。把自定义 ReShade、DLSS 或 Streamline DLL 放入 Custom 文件夹，即可作为按游戏覆盖文件使用。Logs 文件夹包含会话日志，便于排查问题。",
        ["Open AppData Folder"] = "打开 AppData 文件夹",
        ["Open Custom Folder"] = "打开 Custom 文件夹",
        ["Open Logs Folder"] = "打开日志文件夹",

        // About page
        ["A desktop manager for HDR game mods on Windows. Auto-detects your game libraries and installs ReShade, RenoDX, and Luma Framework mods in a few clicks."] = "一个面向 Windows HDR 游戏模组的桌面管理器。它会自动检测你的游戏库，并用少量点击安装 ReShade、RenoDX 和 Luma Framework 模组。",
        ["What RHI does"] = "RHI 的功能",
        ["Scans Steam, GOG, Epic, EA App, Ubisoft, Xbox/Game Pass, Battle.net, and Rockstar for installed games. Provides one-click install, update, and uninstall for ReShade, RenoDX addons, and Luma Framework mods. Manages shader packs and per-game overrides."] = "扫描 Steam、GOG、Epic、EA App、Ubisoft、Xbox/Game Pass、Battle.net 和 Rockstar 中已安装的游戏。为 ReShade、RenoDX 插件和 Luma Framework 模组提供一键安装、更新和卸载，并管理着色器包和按游戏覆盖设置。",
        ["Disclaimer"] = "免责声明",
        ["RHI is an unofficial third-party tool,"] = "RHI 是非官方第三方工具，",
        ["not affiliated with or endorsed by the RenoDX project, Crosire, pmnoxx, or the Luma Framework."] = "不隶属于 RenoDX 项目、Crosire、pmnoxx 或 Luma Framework，也不代表它们背书。",
        ["All mod files are fetched directly from their official sources and are not modified. RenoDX addons come from official GitHub snapshots. ReShade with full addon support is downloaded from reshade.me. 7-Zip is bundled under the LGPL licence for archive extraction."] = "所有模组文件都会直接从官方来源获取且不会被修改。RenoDX 插件来自官方 GitHub 快照；带完整插件支持的 ReShade 从 reshade.me 下载；7-Zip 按 LGPL 许可证随附，用于解压安装包。",
        ["Single-player only"] = "仅限单人游戏",
        ["RHI installs ReShade with full addon support and OptiScaler, which may be flagged by anti-cheat in online or multiplayer games. Uninstall ReShade and OptiScaler before playing online."] = "RHI 会安装带完整插件支持的 ReShade 和 OptiScaler，它们可能在在线或多人游戏中被反作弊系统标记。进行在线游戏前请卸载 ReShade 和 OptiScaler。",
        ["Credits & Acknowledgements"] = "鸣谢",
        ["This app would not exist without the work of the following people and projects."] = "没有以下人员和项目的工作，这个应用不会存在。",
        ["by clshortfuse & contributors"] = "由 clshortfuse 和贡献者制作",
        ["MIT Licence"] = "MIT 许可证",
        ["HDR mod framework powering 150+ games. The entire reason this app exists."] = "为 150 多款游戏提供支持的 HDR 模组框架，也是这个应用存在的核心原因。",
        ["by Crosire"] = "由 Crosire 制作",
        ["BSD 3-Clause Licence"] = "BSD 3-Clause 许可证",
        ["Post-processing injection framework. Downloaded with full addon support from reshade.me and cached locally. Copyright © Crosire."] = "后处理注入框架。RHI 会从 reshade.me 下载带完整插件支持的版本并在本地缓存。版权所有 © Crosire。",
        ["by Pumbo (Filoppi)"] = "由 Pumbo (Filoppi) 制作",
        ["Source-available"] = "源码可获取",
        ["DX11 modding framework adding HDR support and graphics improvements via the ReShade addon system. Mods are downloaded from official GitHub releases. Experimental integration — not fully supported."] = "通过 ReShade 插件系统为 DX11 游戏提供 HDR 支持和图形增强的模组框架。模组会从官方 GitHub 发布页下载。此集成为实验性功能，尚非完整支持。",
        ["by Igor Pavlov"] = "由 Igor Pavlov 制作",
        ["LGPL + BSD 3-Clause"] = "LGPL + BSD 3-Clause",
        ["Archive utility used to extract ReShade DLLs from the NSIS installer. 7z.exe and 7z.dll are bundled under the LGPL licence. Copyright © Igor Pavlov."] = "用于从 NSIS 安装器中提取 ReShade DLL 的压缩工具。7z.exe 和 7z.dll 按 LGPL 许可证随附。版权所有 © Igor Pavlov。",
        ["by ZZZ Projects"] = "由 ZZZ Projects 制作",
        ["HTML parser used to scrape game data from the RenoDX wiki. Copyright © ZZZ Projects Inc."] = "用于从 RenoDX Wiki 抓取游戏数据的 HTML 解析器。版权所有 © ZZZ Projects Inc.",
        ["by Microsoft"] = "由 Microsoft 制作",
        ["MVVM helpers (ObservableObject, RelayCommand, etc.) used throughout the app. Copyright © .NET Foundation."] = "应用中使用的 MVVM 辅助库，包括 ObservableObject、RelayCommand 等。版权所有 © .NET Foundation。",
        ["by pmnox"] = "由 pmnox 制作",
        ["Frame rate limiter addon available as an alternative to ReLimiter. The LITE variant is downloaded from GitHub on demand."] = "可替代 ReLimiter 的帧率限制器插件。LITE 变体会在需要时从 GitHub 下载。",
        ["UI Design"] = "界面设计",
        ["by Lazorr"] = "由 Lazorr 制作",
        ["Somehow I have become a UI guy in multiple aspects of life lol."] = "不知怎么，我在生活的多个方面都成了做界面的人。",
        ["by RankFTW"] = "由 RankFTW 制作",
        ["Unofficial companion app. Not affiliated with RenoDX, Crosire, or pmnoxx."] = "非官方配套应用。不隶属于 RenoDX、Crosire 或 pmnoxx。",

        // Status and action labels
        ["Installing..."] = "正在安装...",
        ["Installing…"] = "正在安装…",
        ["Ready"] = "就绪",
        ["Update"] = "有更新",
        ["⬆ Update"] = "⬆ 更新",
        ["↺ Reinstall"] = "↺ 重装",
        ["⬇ Install"] = "⬇ 安装",
        ["⬇ Vulkan RS"] = "⬇ Vulkan RS",
        ["⬆ Manage"] = "⬆  管理",
        ["↺ Manage"] = "↺  管理",
        ["⬇ Install"] = "⬇  安装",
        ["⚠ ReShade required"] = "⚠  需要 ReShade",
        ["⚠ RE Framework required"] = "⚠  需要 RE Framework",
        ["⚠ Not supported on 32-bit"] = "⚠  不支持 32 位",
        ["⬆ Update RenoDX"] = "⬆  更新 RenoDX",
        ["↺ Reinstall RenoDX"] = "↺  重装 RenoDX",
        ["⬇ Install RenoDX"] = "⬇  安装 RenoDX",
        ["⬆ Update ReShade"] = "⬆  更新 ReShade",
        ["↺ Reinstall ReShade"] = "↺  重装 ReShade",
        ["⬇ Install ReShade"] = "⬇  安装 ReShade",
        ["⬆ Update Vulkan ReShade"] = "⬆  更新 Vulkan ReShade",
        ["↺ Reinstall Vulkan ReShade"] = "↺  重装 Vulkan ReShade",
        ["⬇ Install Vulkan ReShade"] = "⬇  安装 Vulkan ReShade",
        ["⬇ Install Vulkan Layer"] = "⬇  安装 Vulkan 层",
        ["⬆ Update All"] = "⬆  全部更新",
        ["↺ Reinstall All"] = "↺  全部重装",
        ["⬇ Install All"] = "⬇  全部安装",
        ["⬆ Update ReLimiter"] = "⬆  更新 ReLimiter",
        ["↺ Reinstall ReLimiter"] = "↺  重装 ReLimiter",
        ["⬇ Install ReLimiter"] = "⬇  安装 ReLimiter",
        ["⬆ Update DC"] = "⬆  更新 DC",
        ["↺ Reinstall DC"] = "↺  重装 DC",
        ["⬇ Install DC"] = "⬇  安装 DC",
        ["⬆ Update OptiScaler"] = "⬆  更新 OptiScaler",
        ["↺ Reinstall OptiScaler"] = "↺  重装 OptiScaler",
        ["⬇ Install OptiScaler"] = "⬇  安装 OptiScaler",
        ["⬆ Update DXVK"] = "⬆  更新 DXVK",
        ["↺ Reinstall DXVK"] = "↺  重装 DXVK",
        ["⬇ Install DXVK"] = "⬇  安装 DXVK",
        ["⬆ Update Luma"] = "⬆  更新 Luma",
        ["↺ Reinstall Luma"] = "↺  重装 Luma",
        ["⬇ Install Luma"] = "⬇  安装 Luma",
        ["⬆ Update RE Framework"] = "⬆  更新 RE Framework",
        ["↺ Reinstall RE Framework"] = "↺  重装 RE Framework",
        ["⬇ Install RE Framework"] = "⬇  安装 RE Framework",
        ["Generic Unity"] = "通用 Unity",
        ["UE Extended Native HDR"] = "UE Extended 原生 HDR",
        ["UE Extended"] = "UE Extended",
        ["Generic UE"] = "通用 UE",
        ["⚡ UE Extended ON"] = "⚡ UE Extended 已启用",
        ["⚡ UE Extended"] = "⚡ UE Extended",
        ["Luma ON"] = "Luma 已启用",
        ["Luma OFF"] = "Luma 已关闭",
        ["✅ Working"] = "✅ 可用",
        ["🚧 In Progress"] = "🚧 开发中",
        ["⚠️ May Work"] = "⚠️ 可能可用",
        ["💬 Discord"] = "💬 Discord",
        ["🌐 Nexus"] = "🌐 Nexus",
        ["👁 Show"] = "👁 显示",
        ["🚫 Hide"] = "🚫 隐藏",
        ["Detail View"] = "详情视图",
        ["Grid View"] = "网格视图",
        ["Compact View"] = "紧凑视图",
        ["Build {0}"] = "构建 {0}",
        ["{0} installed"] = "已安装 {0} 个",
        ["{0} shown"] = "显示 {0} 个",
        ["· {0} hidden"] = "· 已隐藏 {0} 个",
        ["DXVK is blocked for this game due to anti-cheat software."] = "由于反作弊软件限制，此游戏禁止启用 DXVK。",
        ["DXVK cannot be enabled because the game's DirectX version could not be determined."] = "无法启用 DXVK，因为无法确定此游戏使用的 DirectX 版本。",
        ["DXVK does not support {0}. It only translates DirectX 8/9/10/11 to Vulkan."] = "DXVK 不支持 {0}。它只会将 DirectX 8/9/10/11 转换为 Vulkan。",

        // Runtime status
        ["Scanning game library..."] = "正在扫描游戏库...",
        ["Running store scans + wiki fetch simultaneously..."] = "正在同时扫描商店库并获取 Wiki 信息...",
        ["Checking for new games and fetching latest mod info..."] = "正在检查新游戏并获取最新模组信息...",
        ["Matching mods and checking install status..."] = "正在匹配模组并检查安装状态...",
        ["Scanning for changes..."] = "正在扫描变更...",
        ["Error loading"] = "加载失败",
        ["just now"] = "刚刚",

        // Dialogs and common buttons
        ["OK"] = "确定",
        ["Cancel"] = "取消",
        ["Close"] = "关闭",
        ["Save"] = "保存",
        ["Delete"] = "删除",
        ["Install"] = "安装",
        ["Deploy"] = "部署",
        ["Repository"] = "仓库",
        ["How to use"] = "使用说明",
        ["Select Addons"] = "选择插件",
        ["No addons available."] = "没有可用插件。",
        ["No addons available. Try refreshing."] = "没有可用插件。请尝试刷新。",
        ["ReShade Addon Manager"] = "ReShade 插件管理器",
        ["Include components in Update All globally:"] = "全局纳入“全部更新”的组件：",
        ["Global Update Inclusion"] = "全局更新范围",
        ["ReShade UI Hotkey"] = "ReShade 界面热键",
        ["ReShade Hotkeys"] = "ReShade 热键",
        ["ReShade Screenshot Hotkey"] = "ReShade 截图热键",
        ["OptiScaler Hotkey"] = "OptiScaler 热键",
        ["DXVK Variant Changed"] = "DXVK 变体已更改",
        ["ReShade Build Channel Changed"] = "ReShade 构建渠道已更改",
        ["No games currently have DXVK installed."] = "当前没有游戏安装 DXVK。",
        ["No games currently have ReShade installed."] = "当前没有游戏安装 ReShade。",
        ["Install Luma Mod"] = "安装 Luma 模组",
        ["Select game to install to:"] = "选择要安装到的游戏：",
        ["ℹ DXVK Info"] = "ℹ DXVK 信息",
        ["Continue"] = "继续",
        ["Later"] = "稍后",
        ["Update Now"] = "立即更新",
        ["Confirm"] = "确认",
        ["Next"] = "下一步",
        ["Overwrite"] = "覆盖",
        ["Restore"] = "还原",
        ["Open Folder"] = "打开文件夹",
        ["No changes made."] = "没有进行任何更改。",
        ["already at selected version"] = "已经是所选版本",
        ["component not present"] = "组件不存在",
        ["v1.x incompatible"] = "v1.x 不兼容",
        ["None"] = "无",
        ["Default (Restore)"] = "默认（还原）",
        ["Custom"] = "自定义",
        ["Development"] = "开发版",
        ["Stable"] = "稳定版",
        ["Nightly"] = "夜间版",

        // Selection popups
        ["Select Shader Packs"] = "选择着色器包",
        ["No shader packs available."] = "没有可用的着色器包。",
        ["Essential"] = "必要",
        ["Recommended"] = "推荐",
        ["Extra"] = "额外",
        ["Select ReShade Presets"] = "选择 ReShade 预设",
        ["No preset files found."] = "没有找到预设文件。",
        ["Place .ini files in:"] = "请将 .ini 文件放入：",
        ["Presets from:"] = "预设来源：",
        ["Select ReShade Preset"] = "选择 ReShade 预设",
        ["Also install the required shaders and textures?"] = "是否同时安装所需的着色器和纹理？",

        // Per-game overrides and flyouts
        ["Components"] = "组件",
        ["Install All"] = "全部安装",
        ["——— Frame limiters — Choose one ———"] = "——— 帧率限制器：请选择一个 ———",
        ["——— Optional ———"] = "——— 可选 ———",
        ["Reset Overrides"] = "重置覆盖设置",
        ["Reset all overrides for this game to defaults."] = "将此游戏的所有覆盖设置重置为默认值。",
        ["Game name (editable)"] = "游戏名称（可编辑）",
        ["Wiki mod name"] = "Wiki 模组名称",
        ["Exact wiki name"] = "精确的 Wiki 名称",
        ["↩ Reset"] = "↩ 重置",
        ["DLL naming overrides"] = "DLL 命名覆盖",
        ["Custom filenames enabled"] = "已启用自定义文件名",
        ["Override DLL filenames"] = "覆盖 DLL 文件名",
        ["Select ReShade DLL name"] = "选择 ReShade DLL 名称",
        ["Select DC DLL name"] = "选择 DC DLL 名称",
        ["Select OptiScaler DLL name"] = "选择 OptiScaler DLL 名称",
        ["Bitness"] = "位数",
        ["Graphics API"] = "图形 API",
        ["RS Channel"] = "RS 渠道",
        ["Global update inclusion"] = "全局更新范围",
        ["Shaders and Addons"] = "着色器和插件",
        ["Shaders"] = "着色器",
        ["Addons"] = "插件",
        ["Launch executable"] = "启动程序",
        ["Auto-detect (or paste path)"] = "自动检测（或粘贴路径）",
        ["Launch arguments"] = "启动参数",
        ["Browse"] = "浏览",
        ["Override the executable used when launching this game. Leave blank for auto-detection (largest exe in install folder)."] = "覆盖启动此游戏时使用的可执行文件。留空则自动检测（安装文件夹中最大的 exe）。",
        ["Choose which components are included in Update All for this game."] = "选择此游戏中哪些组件会被纳入“全部更新”。",
        ["Include this game in Update All for:"] = "将此游戏的以下组件纳入“全部更新”：",
        ["Global"] = "全局",
        ["Auto"] = "自动",
        ["Default"] = "默认",
        ["Select"] = "选择",
        ["Included"] = "已包含",
        ["Excluded"] = "已排除",
        ["No Addons"] = "无插件",
        ["Legacy..."] = "旧版...",
        ["32-bit"] = "32 位",
        ["64-bit"] = "64 位",
        ["Ray Reconstruction"] = "光线重建",
        ["Frame Generation"] = "帧生成",
        ["Download"] = "下载",
        ["Redownload"] = "重新下载",
        ["Download from Discord"] = "从 Discord 下载",
        ["Download from Nexus Mods"] = "从 Nexus Mods 下载",
        ["Redownload from Discord"] = "从 Discord 重新下载",
        ["Redownload from Nexus Mods"] = "从 Nexus Mods 重新下载",
        ["No RenoDX mod available"] = "没有可用的 RenoDX 模组",
        ["The display name for this game. Edit and press Enter to rename. Reset reverts to the auto-detected store name."] = "此游戏的显示名称。编辑后按 Enter 可重命名。重置会恢复为自动检测到的商店名称。",
        ["Override the name used to look up this game on the RenoDX/Luma wiki. Leave blank to use the game name. Press Enter to save."] = "覆盖在 RenoDX/Luma Wiki 上查找此游戏时使用的名称。留空则使用游戏名称。按 Enter 保存。",
        ["Override the filenames ReShade is installed as. When enabled, existing RS files are renamed to the custom filenames."] = "覆盖 ReShade 安装时使用的文件名。启用后，现有 RS 文件会重命名为自定义文件名。",
        ["ReShade DLL name is controlled by OptiScaler. Uninstall OptiScaler to change the ReShade DLL name."] = "ReShade DLL 名称由 OptiScaler 控制。请卸载 OptiScaler 后再更改 ReShade DLL 名称。",
        ["Could not revert ReShade to dxgi.dll — the filename is occupied by another file. ReShade was renamed to a fallback name instead."] = "无法将 ReShade 还原为 dxgi.dll，因为该文件名已被其他文件占用。ReShade 已改用备用文件名。",
        ["Could not revert Display Commander to its default name — the filename is occupied by another file. DC was kept under its current name."] = "无法将 Display Commander 还原为默认名称，因为该文件名已被其他文件占用。DC 已保留当前名称。",
        ["Included = this game is looked up on the RenoDX and Luma wikis. Excluded = skip wiki lookups for this game."] = "已包含 = 会在 RenoDX 和 Luma Wiki 上查找此游戏。已排除 = 跳过此游戏的 Wiki 查找。",
        ["Global = use global shader selection. Custom = use custom shader directories. Select = pick per-game packs. Off = no shaders."] = "全局 = 使用全局着色器选择。自定义 = 使用自定义着色器目录。选择 = 选择此游戏专用包。关闭 = 不使用着色器。",
        ["Override the auto-detected bitness for this game. Auto uses PE header detection. 32-bit or 64-bit forces the value."] = "覆盖此游戏自动检测到的位数。自动会使用 PE 头检测。32 位或 64 位会强制指定该值。",
        ["Override the detected graphics API for this game."] = "覆盖此游戏检测到的图形 API。",
        ["Auto uses the auto-detected value from PE header scanning."] = "自动会使用 PE 头扫描检测到的值。",
        ["User overrides set here take precedence over manifest and auto-detected values."] = "这里设置的用户覆盖会优先于清单和自动检测结果。",
        ["Reset Overrides reverts to auto-detection."] = "重置覆盖设置会恢复为自动检测。",
        ["Auto uses PE header scanning. Reset Overrides reverts to auto-detection."] = "自动会使用 PE 头扫描。重置覆盖设置会恢复为自动检测。",
        ["Override the global ReShade build channel for this game."] = "覆盖此游戏的全局 ReShade 构建渠道。",
        ["Vulkan games: changing this affects ALL Vulkan games."] = "Vulkan 游戏：更改此项会影响所有 Vulkan 游戏。",
        ["Global = use Settings default. Vulkan games: changing this affects ALL Vulkan games."] = "全局 = 使用设置中的默认值。Vulkan 游戏：更改此项会影响所有 Vulkan 游戏。",
        ["⚠ Older ReShade versions may not support newer addons."] = "⚠ 较旧的 ReShade 版本可能不支持较新的插件。",
        ["The game will be excluded from automatic ReShade updates."] = "此游戏会从自动 ReShade 更新中排除。",
        ["No custom ReShade DLLs found."] = "没有找到自定义 ReShade DLL。",
        ["Place your ReShade64.dll and/or ReShade32.dll in:"] = "请将 ReShade64.dll 和/或 ReShade32.dll 放入：",
        ["Changing the channel for this game will change it for ALL Vulkan games."] = "更改此游戏的渠道会影响所有 Vulkan 游戏。",
        ["Global = use global addon set. Select = pick per-game addons. Off = no addons for this game."] = "全局 = 使用全局插件集。选择 = 选择此游戏专用插件。关闭 = 此游戏不使用插件。",
        ["Pick .ini preset files to copy to this game's folder. Place presets in the reshade-presets folder."] = "选择要复制到此游戏文件夹的 .ini 预设文件。请将预设放入 reshade-presets 文件夹。",
        ["Enable UE Extended"] = "启用 UE Extended",
        ["Disable UE Extended"] = "禁用 UE Extended",
        ["Off = DXVK disabled. Global = use global variant setting."] = "关闭 = 禁用 DXVK。全局 = 使用全局变体设置。",
        ["Development/Stable/Lilium HDR = per-game variant override."] = "开发版/稳定版/Lilium HDR = 此游戏专用变体覆盖。",
        ["DXVK translates DirectX to Vulkan — enables compute shaders."] = "DXVK 会将 DirectX 转译为 Vulkan，并启用计算着色器。",
        ["Change the install folder for this game. Use when auto-detection picked the wrong directory."] = "更改此游戏的安装文件夹。适用于自动检测选错目录的情况。",

        // Update and warning dialogs
        ["🔄 Update Available"] = "🔄 有可用更新",
        ["A new version of RHI is available!"] = "RHI 有新版本可用！",
        ["Would you like to update now?"] = "是否现在更新？",
        ["⬇ Downloading Update"] = "⬇ 正在下载更新",
        ["Starting download..."] = "正在开始下载...",
        ["❌ Download failed. Please try again later or download manually from GitHub."] = "❌ 下载失败。请稍后重试，或从 GitHub 手动下载。",
        ["📋 Patch Notes — What's New"] = "📋 更新说明：新增内容",
        ["📢 Message from RHI"] = "📢 来自 RHI 的消息",
        ["⚠ Install Note"] = "⚠ 安装提示",
        ["⚠ DXVK Warning"] = "⚠ DXVK 警告",
        ["Don't show this warning again"] = "不再显示此警告",
        ["⚠ ADVANCED FEATURE — USE AT YOUR OWN RISK DXVK is an unofficial DirectX-to-Vulkan translation layer. No support will be provided if a game is not compatible. WHO SHOULD USE THIS: • Primarily benefits older DX8/DX9 games (e.g. FFXIV, Morrowind) • Enables ReShade compute shaders on games that don't support them natively • Can reduce CPU-bound stuttering in older titles IMPORTANT WARNINGS: • Anti-cheat games may ban players using DXVK • Game overlays (Steam, NVIDIA, RTSS) may conflict or stop working • Exclusive fullscreen is blocked — use borderless windowed • First launch will be slow due to shader compilation (improves on subsequent runs) • Some games may crash or have graphical glitches with DXVK Do you want to continue?"] = "⚠ 高级功能，请自行承担使用风险\n\nDXVK 是非官方的 DirectX 到 Vulkan 转译层。\n如果游戏不兼容，本工具不提供支持。\n\n适合使用的场景：\n• 主要有利于较旧的 DX8/DX9 游戏（例如 FFXIV、Morrowind）\n• 可在原生不支持的游戏中启用 ReShade 计算着色器\n• 可减少旧游戏中由 CPU 瓶颈导致的卡顿\n\n重要警告：\n• 反作弊游戏可能会封禁使用 DXVK 的玩家\n• 游戏覆盖层（Steam、NVIDIA、RTSS）可能冲突或停止工作\n• 独占全屏会被阻止，请使用无边框窗口\n• 首次启动会因着色器编译而较慢，后续会改善\n• 部分游戏使用 DXVK 后可能崩溃或出现图形错误\n\n是否继续？",

        // DLSS and Streamline mass deployment
        ["No DLSS/Streamline Games"] = "没有 DLSS/Streamline 游戏",
        ["No games with DLSS or Streamline DLLs were detected. Run a Full Refresh to scan for them."] = "未检测到带有 DLSS 或 Streamline DLL 的游戏。请运行“完整刷新”进行扫描。",
        ["Skipped — v1.x DLSS/Streamline not compatible with newer versions"] = "已跳过：v1.x DLSS/Streamline 与较新版本不兼容",
        ["Select All"] = "全选",
        ["Deselect All"] = "取消全选",
        ["DLSS Super Resolution"] = "DLSS 超分辨率",
        ["DLSS Ray Reconstruction"] = "DLSS 光线重建",
        ["DLSS Frame Generation"] = "DLSS 帧生成",
        ["Streamline"] = "Streamline",
        ["SR Preset"] = "SR 预设",
        ["RR Preset"] = "RR 预设",
        ["FG Preset"] = "FG 预设",
        ["Auto-create NVIDIA profiles"] = "自动创建 NVIDIA 配置文件",
        ["Batch DLSS & Streamline Deploy"] = "批量部署 DLSS 和 Streamline",
        ["Deploying to selected games..."] = "正在部署到所选游戏...",
        ["Deploying..."] = "正在部署...",
        ["Batch Deploy Complete"] = "批量部署完成",
        ["Restoring selected games..."] = "正在还原所选游戏...",
        ["Restoring..."] = "正在还原...",
        ["Restore Complete"] = "还原完成",
        ["No games had backups to restore or presets to reset."] = "没有可还原备份或可重置预设的游戏。",

        // Additional coverage from full UI audit
        ["Reset game name back to auto-detected and clear wiki name mapping."] = "将游戏名称重置为自动检测结果，并清除 Wiki 名称映射。",
        ["⚠ Older ReShade versions may not support newer addons. The game will be excluded from automatic ReShade updates."] = "⚠ 较旧的 ReShade 版本可能不支持较新的插件。此游戏会从自动 ReShade 更新中排除。",
        ["Select Legacy ReShade Version"] = "选择旧版 ReShade 版本",
        ["No custom ReShade DLLs found. Place your ReShade64.dll and/or ReShade32.dll in:"] = "没有找到自定义 ReShade DLL。请将 ReShade64.dll 和/或 ReShade32.dll 放入：",
        ["Custom ReShade Not Found"] = "未找到自定义 ReShade",
        ["Vulkan ReShade Channel Override"] = "Vulkan ReShade 渠道覆盖",
        ["Vulkan games share a global ReShade layer."] = "Vulkan 游戏共享一个全局 ReShade 层。",
        ["This will change the ReShade build channel for every Vulkan game."] = "这会更改所有 Vulkan 游戏的 ReShade 构建渠道。",
        ["Apply to All Vulkan Games"] = "应用到所有 Vulkan 游戏",
        ["Addon service is not yet wired. Complete Task 9.1 to enable addon selection."] = "插件服务尚未接入。完成任务 9.1 后即可启用插件选择。",
        ["🔧 Install Shaders?"] = "🔧 安装着色器？",
        ["Command-line arguments passed to the game on launch. Saves on focus lost."] = "启动游戏时传入的命令行参数。失去焦点时会自动保存。",
        ["Note: Setting arguments disables Epic protocol launch. EOS-protected games may fail to launch with arguments."] = "注意：设置启动参数会禁用 Epic 协议启动。受 EOS 保护的游戏可能无法携带参数启动。",
        ["Select Game Executable"] = "选择游戏可执行文件",
        ["Browse for a game executable to use as the launch target."] = "浏览并选择要作为启动目标的游戏可执行文件。",
        ["Clear the launch executable override and revert to auto-detection."] = "清除启动程序覆盖设置，并恢复为自动检测。",
        ["DLSS / Streamline"] = "DLSS / Streamline",
        ["Restore All"] = "全部还原",
        ["Restore all DLSS and Streamline DLLs to their original game versions and reset presets to Default."] = "将所有 DLSS 和 Streamline DLL 还原为游戏原始版本，并把预设重置为默认值。",
        ["Also install the required shader packs for these games?"] = "是否也为这些游戏安装所需的着色器包？",
        ["Change install folder"] = "更改安装文件夹",
        ["Reset / Remove game"] = "重置/移除游戏",
        ["Reset the install folder to auto-detected, or remove a manually added game entirely."] = "将安装文件夹重置为自动检测结果，或彻底移除手动添加的游戏。",
        ["Reset all per-game overrides back to defaults (DLL names, channels, shaders, addons, DXVK, launch settings, update inclusion)."] = "将此游戏的全部覆盖设置恢复为默认值，包括 DLL 名称、渠道、着色器、插件、DXVK、启动设置和更新范围。",
        ["Copy Report"] = "复制报告",
        ["Copy a diagnostic report for this game to the clipboard. Useful for Discord or GitHub support."] = "将此游戏的诊断报告复制到剪贴板，便于在 Discord 或 GitHub 获取支持。",
        ["⚠ Unknown dxgi.dll Detected"] = "⚠ 检测到未知 dxgi.dll",
        ["⚠ Unknown winmm.dll Detected"] = "⚠ 检测到未知 winmm.dll",
        ["RHI cannot identify this file as ReShade or Display Commander."] = "RHI 无法识别此文件是否为 ReShade 或 Display Commander。",
        ["It may belong to another mod (e.g. DXVK, Special K, ENB)."] = "它可能属于其他模组，例如 DXVK、Special K 或 ENB。",
        ["RHI cannot identify this file as Display Commander."] = "RHI 无法识别此文件是否为 Display Commander。",
        ["It may belong to another mod or DLL injector."] = "它可能属于其他模组或 DLL 注入器。",
        ["Overwriting it may break the existing mod. Do you want to proceed?"] = "覆盖它可能会破坏现有模组。是否继续？",
        ["No additional RenoDX notes for this game."] = "此游戏没有额外的 RenoDX 备注。",
        ["No additional Luma notes for this game."] = "此游戏没有额外的 Luma 备注。",
        ["No OptiScaler compatibility data available for this game."] = "此游戏没有可用的 OptiScaler 兼容性数据。",
        ["None listed"] = "未列出",
        ["View details"] = "查看详情",
        ["View wiki page"] = "查看 Wiki 页面",
        ["Also available on Nexus Mods"] = "也可在 Nexus Mods 获取",
        ["HDR Analysis — HDR Gaming Database"] = "HDR 分析 — HDR Gaming Database",
        ["⚠ UE-Extended Compatibility Warning"] = "⚠ UE-Extended 兼容性警告",
        ["Not all Unreal Engine games are compatible with UE-Extended."] = "并非所有 Unreal Engine 游戏都兼容 UE-Extended。",
        ["UE-Extended uses a different injection method that works better with some games but may cause crashes or issues with others."] = "UE-Extended 使用不同的注入方式；它在某些游戏中效果更好，但也可能在其他游戏中导致崩溃或问题。",
        ["Check the Notes section for any additional compatibility information for this game."] = "请查看备注区域，了解此游戏的额外兼容性信息。",
        ["No specific notes are available for this game — check the RDXC Discord for community reports."] = "此游戏没有特定备注，请查看 RDXC Discord 上的社区反馈。",
        ["OK, I understand"] = "知道了",
        ["7-Zip Not Found"] = "未找到 7-Zip",
        ["Cannot extract archive — 7-Zip was not found. Please reinstall RDXC."] = "无法解压压缩包，因为未找到 7-Zip。请重新安装 RDXC。",
        ["Archive Extraction Failed"] = "压缩包解压失败",
        ["No Addon Found"] = "未找到插件",
        ["Select addon to install..."] = "选择要安装的插件...",
        ["No Games Available"] = "没有可用游戏",
        ["No games are currently detected. Add a game first."] = "当前未检测到游戏。请先添加一个游戏。",
        ["Select a game..."] = "选择游戏...",
        ["📦 Install RenoDX Addon"] = "📦 安装 RenoDX 插件",
        ["No Game Selected"] = "未选择游戏",
        ["Please select a game to install the addon to."] = "请选择要安装插件的游戏。",
        ["⚠ Confirm Addon Install"] = "⚠ 确认安装插件",
        ["✅ Addon Installed"] = "✅ 插件已安装",
        ["❌ Install Failed"] = "❌ 安装失败",
        ["❌ Invalid URL"] = "❌ 无效 URL",
        ["The dropped URL could not be parsed. Please check the link and try again."] = "无法解析拖放的 URL。请检查链接后重试。",
        ["Could not determine a filename from the dropped URL."] = "无法从拖放的 URL 中确定文件名。",
        ["❌ Unsupported File Type"] = "❌ 不支持的文件类型",
        ["⬇ Downloading Addon"] = "⬇ 正在下载插件",
        ["❌ Download Failed"] = "❌ 下载失败",
        ["❌ Download Timed Out"] = "❌ 下载超时",
        ["The download timed out. Please check your connection and try again."] = "下载超时。请检查网络连接后重试。",
        ["❌ Invalid Addon File"] = "❌ 无效插件文件",
        ["The downloaded file is not a valid addon binary. The server may have returned an error page."] = "下载的文件不是有效的插件二进制文件。服务器可能返回了错误页面。",
        ["Game Already Exists"] = "游戏已存在",
        ["Game name:"] = "游戏名称：",
        ["➕ Add Dropped Game"] = "➕ 添加拖放的游戏",
        ["Select Folder"] = "选择文件夹",
        ["This archive contains multiple game folders. Select the folder to install:"] = "此压缩包包含多个游戏文件夹。请选择要安装的文件夹：",
        ["❌ Read Error"] = "❌ 读取错误",
        ["❌ Not a ReShade Preset"] = "❌ 不是 ReShade 预设",
        ["This file is not a recognised ReShade preset. A valid preset must contain a Techniques= line with at least one @.fx entry."] = "此文件不是可识别的 ReShade 预设。有效预设必须包含 Techniques= 行，且至少有一个 @.fx 条目。",
        ["❌ Storage Error"] = "❌ 存储错误",
        ["🎨 Install ReShade Preset"] = "🎨 安装 ReShade 预设",
        ["Please select a game to install the preset to."] = "请选择要安装预设的游戏。",
        ["❌ Deploy Failed"] = "❌ 部署失败",
        ["Before you submit"] = "提交前请确认",
        ["Please use the overrides on this panel to correct any wrong values"] = "请先使用此面板上的覆盖设置修正任何错误值",
        ["Yes, continue"] = "是，继续",
        ["Go back"] = "返回",
        ["Describe the issue (optional)"] = "描述问题（可选）",
        ["Copy Game Report"] = "复制游戏报告",
        ["This saves a report file and copies it to your clipboard."] = "这会保存报告文件，并将内容复制到剪贴板。",
        ["Copy to Clipboard"] = "复制到剪贴板",
        ["⚠ OptiScaler Setup"] = "⚠ OptiScaler 设置",
        ["Before installing OptiScaler, please configure your GPU type and DLSS input settings in the OptiScaler Settings section on the Settings page. This ensures OptiScaler is configured correctly for your hardware."] = "安装 OptiScaler 前，请先在“设置”页的 OptiScaler 设置中配置 GPU 类型和 DLSS 输入设置。这样可以确保 OptiScaler 按你的硬件正确配置。",
        ["❌ No OptiScaler.ini found in INIs folder."] = "❌ INIs 文件夹中没有找到 OptiScaler.ini。",
        ["✅ OptiScaler.ini copied to game folder."] = "✅ 已将 OptiScaler.ini 复制到游戏文件夹。",
        ["⚡ UE-Extended enabled — check Discord to confirm this game is compatible."] = "⚡ UE-Extended 已启用，请查看 Discord 确认此游戏是否兼容。",
        ["UE-Extended disabled."] = "UE-Extended 已禁用。",
        ["✅ reshade.ini merged into game folder."] = "✅ 已将 reshade.ini 合并到游戏文件夹。",
        ["✅ relimiter.ini copied to game folder."] = "✅ 已将 relimiter.ini 复制到游戏文件夹。",
        ["✅ DisplayCommander.ini copied to game folder."] = "✅ 已将 DisplayCommander.ini 复制到游戏文件夹。",
        ["📂 Open Folder"] = "📂 打开文件夹",
        ["ℹ Discussion / Instructions"] = "ℹ 讨论/说明",
        ["💬 View Notes"] = "💬 查看备注",
        ["Filter name"] = "筛选名称",
        ["Save Custom Filter"] = "保存自定义筛选",
        ["Please enter a filter name."] = "请输入筛选名称。",
        ["Select Game Executable"] = "选择游戏可执行文件",
        ["Name This Game"] = "为此游戏命名",
        ["Enter the game name:"] = "输入游戏名称：",
        ["⚠ ReShade Addons"] = "⚠ ReShade 插件",
        ["ReShade addons are advanced features intended for experienced users who understand what they are."] = "ReShade 插件是面向有经验用户的高级功能，使用者应了解它们的作用和风险。",
        ["DXVK translates DirectX 8/9/10/11 API calls into Vulkan."] = "DXVK 会将 DirectX 8/9/10/11 API 调用转换为 Vulkan。",
        ["⚠ Administrator privileges are required for Vulkan layer installation. Restart RHI as admin."] = "⚠ 安装 Vulkan 层需要管理员权限。请以管理员身份重新启动 RHI。",
        ["⚠ Administrator privileges are required for GAC symlink installation. Restart RHI as admin."] = "⚠ 安装 GAC 符号链接需要管理员权限。请以管理员身份重新启动 RHI。",
        ["—— Frame limiters — Choose one ——"] = "—— 帧率限制器：请选择一个 ——",
        ["Mass INI Deployment"] = "批量部署 INI",
        ["No games with ReShade installed were found. Install ReShade on at least one game first."] = "没有找到已安装 ReShade 的游戏。请先至少为一个游戏安装 ReShade。",
        ["✅ Updated!"] = "✅ 已更新！",
        ["✅ Up to date"] = "✅ 已是最新",
        ["Updating Vulkan ReShade..."] = "正在更新 Vulkan ReShade...",
        ["Installing DXVK..."] = "正在安装 DXVK...",
        ["✅ DXVK installed!"] = "✅ DXVK 已安装！",
        ["Removing DXVK..."] = "正在移除 DXVK...",
        ["✖ DXVK removed."] = "✖ DXVK 已移除。",
        ["Updating DXVK..."] = "正在更新 DXVK...",
        ["✅ DXVK updated!"] = "✅ DXVK 已更新！",
        ["✅ dxvk.conf copied to game folder."] = "✅ 已将 dxvk.conf 复制到游戏文件夹。",
        ["No install path — use 📁 to pick the game folder."] = "没有安装路径，请使用 📁 选择游戏文件夹。",
        ["✅ Installed! Press Home in-game to open ReShade."] = "✅ 已安装！在游戏中按 Home 打开 ReShade。",
        ["✖ Mod removed."] = "✖ 模组已移除。",
        ["⚠ Install RE Framework first."] = "⚠ 请先安装 RE Framework。",
        ["⚠ Skipped — unknown dxgi.dll found. Use Overrides to proceed."] = "⚠ 已跳过：发现未知 dxgi.dll。请使用覆盖设置继续。",
        ["⚠ Skipped — unknown dxgi.dll found."] = "⚠ 已跳过：发现未知 dxgi.dll。",
        ["Vulkan layer install cancelled."] = "Vulkan 层安装已取消。",
        ["Installing Vulkan ReShade layer..."] = "正在安装 Vulkan ReShade 层...",
        ["✅ ReShade installed (Vulkan Layer)!"] = "✅ ReShade 已安装（Vulkan 层）！",
        ["Installing ReShade (GAC symlink)..."] = "正在安装 ReShade（GAC 符号链接）...",
        ["✅ ReShade installed (GAC symlink)!"] = "✅ ReShade 已安装（GAC 符号链接）！",
        ["✖ ReShade removed."] = "✖ ReShade 已移除。",
        ["✖ Vulkan ReShade removed."] = "✖ Vulkan ReShade 已移除。",
        ["Luma installed!"] = "Luma 已安装！",
        ["✖ Luma removed."] = "✖ Luma 已移除。",
        ["Normal ReShade selected — click Install to deploy."] = "已选择普通 ReShade，点击“安装”进行部署。",
        ["Addon ReShade selected — click Install to deploy."] = "已选择插件版 ReShade，点击“安装”进行部署。",
    };
}
