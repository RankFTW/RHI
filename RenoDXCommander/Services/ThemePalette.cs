// ThemePalette.cs — Colour themes (Settings › General › Colour theme): the colours a theme picks, the built-in
// presets, and ThemeColorMapper, which turns any colour of RHI's default dark look into the theme's version.
// Pure logic (no WinUI objects) so it can be unit-tested; ThemeService applies it to the live brushes.

using System.Globalization;
using Windows.UI;

namespace RenoDXCommander.Services;

/// <summary>
/// A colour theme: five neutral stops (background to text) and six accent roles. Every colour in RHI is
/// recoloured relative to the nearest of these in the default palette, so a theme only needs these eleven.
/// </summary>
public sealed record ThemePalette(
    Color Background, Color Panel, Color Border, Color Muted, Color Text,
    Color Accent, Color Buttons, Color Installed, Color Update, Color Warning, Color Error)
{
    /// <summary>RHI's own dark look (the colours in Themes/DarkTheme.xaml).</summary>
    public static readonly ThemePalette Default = new(
        Hex("#121518"), Hex("#181D23"), Hex("#283240"), Hex("#6B7A8E"), Hex("#E8ECF2"),
        Hex("#4DC9E6"), Hex("#7AACDD"), Hex("#5ECB7D"), Hex("#B898E8"), Hex("#D4A856"), Hex("#E07070"));

    /// <summary>The colours Custom lets the user pick, in the order shown in Settings (internal name, label).</summary>
    public static readonly (string Key, string Label)[] CustomKeys =
    {
        ("Background", "Background"),
        ("Panel", "Panels"),
        ("Text", "Text"),
        ("Accent", "Accent"),
        ("Buttons", "Buttons"),
        ("Installed", "Installed"),
        ("Update", "Update available"),
        ("Warning", "Warning"),
        ("Error", "Error"),
    };

    public Color Get(string key) => key switch
    {
        "Background" => Background,
        "Panel" => Panel,
        "Border" => Border,
        "Muted" => Muted,
        "Text" => Text,
        "Accent" => Accent,
        "Buttons" => Buttons,
        "Installed" => Installed,
        "Update" => Update,
        "Warning" => Warning,
        "Error" => Error,
        _ => throw new ArgumentException($"Unknown theme colour '{key}'", nameof(key)),
    };

    /// <summary>
    /// Returns a copy with one picked colour changed. Changing Background, Panel or Text re-derives the border and
    /// muted-text colours, which sit between the panel and text colours at the same spots as in the default theme.
    /// </summary>
    public ThemePalette With(string key, Color c)
    {
        var p = key switch
        {
            "Background" => this with { Background = c },
            "Panel" => this with { Panel = c },
            "Text" => this with { Text = c },
            "Accent" => this with { Accent = c },
            "Buttons" => this with { Buttons = c },
            "Installed" => this with { Installed = c },
            "Update" => this with { Update = c },
            "Warning" => this with { Warning = c },
            "Error" => this with { Error = c },
            _ => throw new ArgumentException($"Unknown theme colour '{key}'", nameof(key)),
        };
        return key is "Background" or "Panel" or "Text" ? p.WithDerivedNeutrals() : p;
    }

    /// <summary>Sets Border and Muted between Panel and Text, at the default theme's proportions.</summary>
    public ThemePalette WithDerivedNeutrals()
    {
        var d = Default;
        double pl = ThemeColorMapper.Lightness(d.Panel), tl = ThemeColorMapper.Lightness(d.Text);
        double bt = (ThemeColorMapper.Lightness(d.Border) - pl) / (tl - pl);
        double mt = (ThemeColorMapper.Lightness(d.Muted) - pl) / (tl - pl);
        return this with
        {
            Border = ThemeColorMapper.Lerp(Panel, Text, bt),
            Muted = ThemeColorMapper.Lerp(Panel, Text, mt),
        };
    }

    /// <summary>Saved form of a Custom theme: "Background=#RRGGBB;Panel=#RRGGBB;…" (only the picked colours).</summary>
    public string ToCustomString() =>
        string.Join(";", CustomKeys.Select(k => $"{k.Key}={ToHex(Get(k.Key))}"));

    /// <summary>Reads <see cref="ToCustomString"/>; colours that are missing or unreadable come from <paramref name="fallback"/>.</summary>
    public static ThemePalette ParseCustom(string? saved, ThemePalette fallback)
    {
        var p = fallback;
        if (string.IsNullOrWhiteSpace(saved)) return p;
        foreach (var part in saved.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part[..eq].Trim();
            if (!CustomKeys.Any(k => k.Key == key)) continue;
            if (TryHex(part[(eq + 1)..].Trim(), out var c)) p = p.With(key, c);
        }
        return p;
    }

    public static Color Hex(string hex) =>
        TryHex(hex, out var c) ? c : throw new FormatException($"Not a colour: {hex}");

    /// <summary>Parses "#RRGGBB" or "#AARRGGBB" (alpha is ignored: theme colours are opaque).</summary>
    public static bool TryHex(string? hex, out Color c)
    {
        c = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 8) h = h[2..];
        if (h.Length != 6 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        c = Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

/// <summary>A built-in theme shown in the Colour theme list.</summary>
public sealed record ThemePreset(string Id, string Label, ThemePalette Palette);

public static class ThemePresets
{
    public const string DefaultId = "Default";
    public const string CustomId = "Custom";

    private static ThemePalette P(string bg, string panel, string border, string muted, string text,
        string accent, string buttons, string installed, string update, string warning, string error) =>
        new(ThemePalette.Hex(bg), ThemePalette.Hex(panel), ThemePalette.Hex(border), ThemePalette.Hex(muted), ThemePalette.Hex(text),
            ThemePalette.Hex(accent), ThemePalette.Hex(buttons), ThemePalette.Hex(installed), ThemePalette.Hex(update),
            ThemePalette.Hex(warning), ThemePalette.Hex(error));

    /// <summary>Built-in themes, in list order. All dark; status colours keep their meaning (green installed, purple update, …)
    /// except in Colour-blind Friendly, which swaps them for colours that stay distinct with red-green colour blindness.</summary>
    public static readonly IReadOnlyList<ThemePreset> All = new[]
    {
        new ThemePreset(DefaultId, "Default", ThemePalette.Default),
        new ThemePreset("OledBlack", "OLED Black",
            P("#000000", "#0A0C0F", "#1E242C", "#66717F", "#E8ECF2", "#4DC9E6", "#7AACDD", "#5ECB7D", "#B898E8", "#D4A856", "#E07070")),
        new ThemePreset("MidnightBlue", "Midnight Blue",
            P("#0B1020", "#111830", "#22305A", "#6A7BA8", "#E6ECFF", "#6AA8FF", "#8AB4FF", "#5ECB7D", "#B898E8", "#D4A856", "#E07070")),
        new ThemePreset("Ember", "Ember",
            P("#161210", "#1E1814", "#3A2E26", "#8A7A6E", "#F2EAE4", "#FF9A4D", "#E8A070", "#5ECB7D", "#C8A0E8", "#E8D060", "#E87070")),
        new ThemePreset("Forest", "Forest",
            P("#0F1512", "#141C17", "#24332A", "#6E8577", "#E6F0EA", "#7FD6C2", "#9CC4A8", "#5ECB7D", "#B898E8", "#D4A856", "#E07070")),
        // Kiro: the colours of the Kiro IDE's dark theme (replaced Violet; a saved "Violet" choice opens as Kiro)
        new ThemePreset("Kiro", "Kiro",
            P("#19161D", "#211D25", "#38343B", "#938F9B", "#F4F2F7", "#A97AF4", "#9E61FF", "#80FFB5", "#F38BC8", "#F1C587", "#E5737A")),
        new ThemePreset("Slate", "Slate",
            P("#141414", "#1C1C1C", "#303030", "#7A7A7A", "#ECECEC", "#9AB8D0", "#A8B4C4", "#5ECB7D", "#B898E8", "#D4A856", "#E07070")),
        new ThemePreset("HighContrast", "High Contrast",
            P("#000000", "#0A0A0A", "#5A6475", "#A8B2C0", "#FFFFFF", "#5FE0FF", "#9CC8FF", "#6CF08E", "#D2B4FF", "#FFD060", "#FF7A7A")),
        new ThemePreset("ColourBlind", "Colour-blind Friendly",
            P("#121518", "#181D23", "#283240", "#6B7A8E", "#E8ECF2", "#4DC9E6", "#A8B4D0", "#4FA3E8", "#CC79A7", "#F0E442", "#E8743B")),
    };

    public static ThemePreset? Find(string? id)
    {
        if (string.Equals(id, "Violet", StringComparison.OrdinalIgnoreCase)) id = "Kiro"; // renamed
        return All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The palette for a saved theme choice (Custom uses <paramref name="customColours"/>, unknown ids fall back to Default).</summary>
    public static ThemePalette Resolve(string? id, string? customColours)
    {
        if (string.Equals(id, CustomId, StringComparison.OrdinalIgnoreCase))
            return ThemePalette.ParseCustom(customColours, ThemePalette.Default);
        return Find(id)?.Palette ?? ThemePalette.Default;
    }
}

/// <summary>
/// Recolours a colour of RHI's default look into a theme. Greys (backgrounds, borders, text) follow the theme's
/// neutral stops by lightness; coloured tones follow the nearest accent role by hue (teal → Accent, blue → Buttons,
/// green → Installed, …), keeping their offset from it, so dimmed and tinted variants stay dimmed and tinted.
/// The default palette maps every colour to itself.
/// </summary>
public sealed class ThemeColorMapper
{
    /// <summary>Hue of the default theme's blue-grey neutrals.</summary>
    private const double NeutralHue = 217;
    private readonly bool _identity;
    private readonly double[] _stopL;
    private readonly Color[] _stopSrc;
    private readonly Color[] _stopDst;
    private readonly (Hsl Src, Hsl Dst)[] _accents;

    public ThemeColorMapper(ThemePalette to)
    {
        var from = ThemePalette.Default;
        _identity = to == from;
        var black = Color.FromArgb(255, 0, 0, 0);
        var white = Color.FromArgb(255, 255, 255, 255);
        _stopSrc = new[] { black, from.Background, from.Panel, from.Border, from.Muted, from.Text, white };
        _stopDst = new[] { black, to.Background, to.Panel, to.Border, to.Muted, to.Text, white };
        _stopL = _stopSrc.Select(Lightness).ToArray();
        _accents = new[]
        {
            (Hsl.From(from.Accent), Hsl.From(to.Accent)),
            (Hsl.From(from.Buttons), Hsl.From(to.Buttons)),
            (Hsl.From(from.Installed), Hsl.From(to.Installed)),
            (Hsl.From(from.Update), Hsl.From(to.Update)),
            (Hsl.From(from.Warning), Hsl.From(to.Warning)),
            (Hsl.From(from.Error), Hsl.From(to.Error)),
        };
    }

    public bool IsIdentity => _identity;

    public Color Map(Color c)
    {
        if (_identity || c.A == 0) return c;
        var hc = Hsl.From(c);
        var neutral = MapNeutral(hc);

        // How "coloured" it is. The default greys (and the Tabs / Spotlight navies like #0B0F17) are blue-tinted
        // (hue ~210-225), so near that hue a colour must be clearly coloured to count as an accent: light colours by
        // HSV saturation, dark ones by chroma (dark navies look very saturated in HSV but are greys); away from that
        // hue any clear tint counts. Faded out for near-black, where hue means nothing.
        int max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        double sv = max == 0 ? 0 : (max - min) / (double)max;
        double dark = 1 - SmoothStep(0.25, 0.4, hc.L);
        double inBand = SmoothStep(0.27, 0.38, sv) * (1 - dark) + SmoothStep(26, 36, max - min) * dark;
        double far = SmoothStep(10, 22, Math.Abs(HueDiff(hc.H, NeutralHue)));
        double w = (inBand + (SmoothStep(0.12, 0.25, sv) - inBand) * far) * SmoothStep(8, 20, max);
        Color result = w <= 0 ? neutral : Lerp(neutral, MapAccent(hc), w);
        return Color.FromArgb(c.A, result.R, result.G, result.B);
    }

    private Color MapNeutral(Hsl hc)
    {
        int i = 0;
        while (i < _stopL.Length - 2 && hc.L > _stopL[i + 1]) i++;
        double span = _stopL[i + 1] - _stopL[i];
        double t = span <= 0 ? 0 : Math.Clamp((hc.L - _stopL[i]) / span, 0, 1);
        var hs = Hsl.From(Lerp(_stopSrc[i], _stopSrc[i + 1], t));
        var ht = Hsl.From(Lerp(_stopDst[i], _stopDst[i + 1], t));

        double h = ht.S < 0.03 ? hc.H : ht.H + HueDiff(hc.H, hs.H);
        double s = Math.Clamp(ht.S + (hc.S - hs.S), 0, 1);
        double l = Math.Clamp(ht.L + (hc.L - hs.L), 0, 1);
        return new Hsl(h, s, l).ToColor();
    }

    private Color MapAccent(Hsl hc)
    {
        var best = _accents[0];
        double bestD = double.MaxValue;
        foreach (var a in _accents)
        {
            double d = Math.Abs(HueDiff(hc.H, a.Src.H));
            if (d < bestD) { bestD = d; best = a; }
        }
        var (src, dst) = best;
        double h = dst.H + HueDiff(hc.H, src.H);
        double s = Math.Clamp(src.S <= 0 ? dst.S : hc.S * dst.S / src.S, 0, 1);
        double l = hc.L <= src.L
            ? (src.L <= 0 ? hc.L : hc.L * dst.L / src.L)
            : dst.L + (hc.L - src.L) * (1 - dst.L) / Math.Max(1e-6, 1 - src.L);
        return new Hsl(h, s, Math.Clamp(l, 0, 1)).ToColor();
    }

    // ── Colour helpers ────────────────────────────────────────────────────

    public static double Lightness(Color c) =>
        (Math.Max(c.R, Math.Max(c.G, c.B)) + Math.Min(c.R, Math.Min(c.G, c.B))) / 510.0;

    public static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t),
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>WCAG contrast ratio (1 to 21) between two opaque colours.</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Ch(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    private static double HueDiff(double a, double b)
    {
        double d = (a - b) % 360;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }

    private static double SmoothStep(double e0, double e1, double x)
    {
        double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private readonly record struct Hsl(double H, double S, double L)
    {
        public static Hsl From(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, d = max - min;
            if (d <= 0) return new Hsl(0, 0, l);
            double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
                     : max == g ? (b - r) / d + 2
                     : (r - g) / d + 4;
            return new Hsl(h * 60, s, l);
        }

        public Color ToColor()
        {
            double h = ((H % 360) + 360) % 360 / 360.0;
            if (S <= 0)
            {
                byte v = ToByte(L);
                return Color.FromArgb(255, v, v, v);
            }
            double q = L < 0.5 ? L * (1 + S) : L + S - L * S;
            double p = 2 * L - q;
            return Color.FromArgb(255, ToByte(Hue(p, q, h + 1.0 / 3)), ToByte(Hue(p, q, h)), ToByte(Hue(p, q, h - 1.0 / 3)));
        }

        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    }
}
