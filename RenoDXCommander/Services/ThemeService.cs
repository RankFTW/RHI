// ThemeService.cs — Applies a colour theme to RHI live. Every brush RHI draws with is a shared object
// (DarkTheme.xaml resources, UIFactory.GetBrush's cache, UIFactory.ThemedBrush), so a theme change sets each
// tracked brush's colour from its original default colour and the whole window repaints without rebuilding.
// UI thread only. Mapping: ThemePalette.cs.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace RenoDXCommander.Services;

public static class ThemeService
{
    private static ThemeColorMapper _mapper = new(ThemePalette.Default);
    // Held strongly on purpose: a C# brush is only a wrapper around the native brush, and .NET can collect the
    // wrapper while the brush is still on screen (weak references lost SurfaceBaseBrush, so the page kept its
    // default colour). The set is bounded: resource brushes plus UIFactory's caches.
    private static readonly Dictionary<SolidColorBrush, Color> _originals = new(ReferenceEqualityComparer.Instance);
    private static readonly List<(ResourceDictionary Dict, object Key, Color Original)> _colorResources = new();
    private static bool _resourcesCaptured;
    private static readonly HashSet<object> _overriddenColourKeys = new();

    /// <summary>The palette in use (Default until <see cref="Apply"/> is called).</summary>
    public static ThemePalette Current { get; private set; } = ThemePalette.Default;

    /// <summary>Raised on the UI thread after a theme is applied, for colours that aren't brushes (title bar, bound strings).</summary>
    public static event Action? ThemeChanged;

    /// <summary>The theme's version of a default-look colour.</summary>
    public static Color Map(Color c) => _mapper.Map(c);

    /// <summary>The theme's version of a default-look hex colour, as hex ("Transparent" and unreadable values are returned as-is).</summary>
    public static string MapHex(string hex)
    {
        if (_mapper.IsIdentity || string.IsNullOrEmpty(hex) || hex[0] != '#') return hex;
        try
        {
            var c = _mapper.Map(UIFactory.ParseColor(hex));
            return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        catch (FormatException) { return hex; }
        catch (ArgumentOutOfRangeException) { return hex; }
    }

    /// <summary>Starts following the theme with a brush whose default-look colour is <paramref name="original"/>, and sets its colour now.</summary>
    /// <remarks>The brush is kept alive for the rest of the session: track only shared (cached) brushes.</remarks>
    public static void Track(SolidColorBrush brush, Color original)
    {
        if (!_originals.TryAdd(brush, original)) return;
        var mapped = _mapper.Map(original);
        if (brush.Color != mapped) brush.Color = mapped;
    }

    /// <summary>
    /// The default-look colour of a brush (its current colour if RHI doesn't track it). Use this for logic that
    /// reads meaning from a colour (e.g. green = installed), so themes that change the colours don't change the meaning.
    /// </summary>
    public static Color OriginalColor(SolidColorBrush brush) =>
        _originals.TryGetValue(brush, out var o) ? o : brush.Color;

    /// <summary>Applies a palette to every tracked brush and colour resource (<paramref name="log"/> false while a colour picker is dragged).</summary>
    /// <remarks>Each brush and resource is set on its own: one WinUI refusal (logged once per step) never stops the rest.</remarks>
    public static void Apply(ThemePalette palette, bool log = true)
    {
        Current = palette;
        _mapper = new ThemeColorMapper(palette);
        if (!_resourcesCaptured) Step("capture", CaptureResources);

        int live = 0, failed = 0;
        string? firstError = null;
        foreach (var (brush, o) in _originals)
        {
            try
            {
                var mapped = _mapper.Map(o);
                if (brush.Color != mapped) brush.Color = mapped;
                live++;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                failed++;
                firstError ??= $"brush {ThemePalette.ToHex(o)}: {ex.Message}";
            }
        }
        foreach (var (dict, key, original) in _colorResources)
        {
            try
            {
                // Dictionaries loaded from a file (DarkTheme.xaml) refuse new values ("Local values are not allowed in
                // resource dictionary with Source set"), so the theme's colour goes into the app's own dictionary, which
                // lookups check first. Replacing an existing key throws: remove it first (TabsTheme.SetResource).
                var target = Application.Current.Resources;
                if (ReferenceEquals(dict, target) || _overriddenColourKeys.Contains(key)) target.Remove(key);
                target[key] = _mapper.Map(original);
                _overriddenColourKeys.Add(key);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                failed++;
                firstError ??= $"colour {key}: {ex.Message}";
            }
        }

        if (log || failed > 0)
            CrashReporter.Log($"[ThemeService.Apply] Applied theme ({live} brushes, {_colorResources.Count} colours"
                + (failed > 0 ? $", {failed} failed, first: {firstError})" : ")"));
        Step("ThemeChanged", () => ThemeChanged?.Invoke());
    }

    private static void Step(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            CrashReporter.Log($"[ThemeService.Apply] {name} failed — {ex.GetType().Name}: {ex.Message} {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Registers RHI's own resources (App.xaml, DarkTheme.xaml): their brushes, the brushes inside their styles,
    /// and raw Color resources (read by code such as the title bar). WinUI's own control resources are left alone.
    /// </summary>
    private static void CaptureResources()
    {
        _resourcesCaptured = true;
        if (Application.Current?.Resources is not { } root) return;
        CaptureDictionary(root, 0);
    }

    private static void CaptureDictionary(ResourceDictionary dict, int depth)
    {
        if (depth > 8 || dict is Microsoft.UI.Xaml.Controls.XamlControlsResources) return;
        var colourKeys = new List<(object Key, Color Value)>();
        foreach (var kv in dict)
        {
            try
            {
                switch (kv.Value)
                {
                    case SolidColorBrush b:
                        Track(b, b.Color);
                        break;
                    case Color c:
                        colourKeys.Add((kv.Key, c));
                        break;
                    case Style s:
                        foreach (var setterBase in s.Setters)
                            if (setterBase is Setter { Value: SolidColorBrush sb })
                                Track(sb, sb.Color);
                        break;
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                CrashReporter.Log($"[ThemeService.CaptureDictionary] {kv.Key}: {ex.Message}");
            }
        }
        foreach (var (key, value) in colourKeys)
            _colorResources.Add((dict, key, value));
        foreach (var merged in dict.MergedDictionaries)
            CaptureDictionary(merged, depth + 1);
    }
}
