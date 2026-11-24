// AuxInstallService.ShippedAddons.cs — ReShade's built-in add-ons that RHI's reshade.ini templates
// disable ([ADDON] DisabledAddons=Generic Depth,Effect Runtime Sync). Settings › ReShade can keep them
// enabled: every template write then removes the two names again, and "Write these to all games"
// writes the setting both ways.
namespace RenoDXCommander.Services;

public partial class AuxInstallService
{
    /// <summary>Built-in ReShade add-ons RHI's templates disable.</summary>
    public static readonly string[] ShippedAddons = ["Generic Depth", "Effect Runtime Sync"];

    /// <summary>True = keep <see cref="ShippedAddons"/> enabled (Settings › ReShade). Null = RHI default (disabled).</summary>
    public static Func<bool>? ShippedAddonsEnabledResolver { get; set; }

    private static bool ShippedAddonsEnabled => ShippedAddonsEnabledResolver?.Invoke() == true;

    /// <summary>ReShade's Generic Depth add-on, which URTI reads the game's depth through.</summary>
    public const string GenericDepth = "Generic Depth";

    /// <summary>
    /// True when a component RHI installed in the game folder needs Generic Depth (URTI): it then stays enabled
    /// there whatever the Settings choice. Set at startup (App.xaml.cs).
    /// </summary>
    public static Func<string, bool>? GenericDepthNeededResolver { get; set; }

    /// <summary>
    /// After a template write: the templates already disable the add-ons, so this only acts when
    /// the user keeps them enabled, or when a component in the game needs Generic Depth.
    /// Covers reshade.ini and ReShade's numbered ReShadeN.ini files.
    /// </summary>
    internal static void ApplyShippedAddonsSettingToGame(string gameDir)
    {
        if (ShippedAddonsEnabled) WriteShippedAddonsSetting(gameDir, enabled: true);
        else if (GenericDepthNeededResolver?.Invoke(gameDir) == true) WriteShippedAddonsSetting(gameDir, enabled: true, [GenericDepth]);
    }

    /// <summary>
    /// A component that needed Generic Depth left the game: RHI's default (disabled) comes back, unless the user keeps
    /// the built-in add-ons on. Only configs that already have a DisabledAddons line are touched.
    /// </summary>
    public static void RestoreGenericDepthDefault(string gameDir)
    {
        if (ShippedAddonsEnabled || GenericDepthNeededResolver?.Invoke(gameDir) == true) return;
        foreach (var ini in ReShadeConfigFiles(gameDir))
        {
            try
            {
                var text = File.ReadAllText(ini);
                if (!text.Contains("DisabledAddons", StringComparison.OrdinalIgnoreCase)) continue;
                var updated = SetShippedAddonsInText(text, enabled: false, [GenericDepth]);
                if (updated != text) File.WriteAllText(ini, updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[AuxInstallService.RestoreGenericDepthDefault] Failed for '{ini}' — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Writes the setting into every ReShade config in <paramref name="gameDir"/> (<paramref name="names"/>: only
    /// these built-in add-ons; default both). Returns files changed.
    /// </summary>
    public static int WriteShippedAddonsSetting(string gameDir, bool enabled, IReadOnlyCollection<string>? names = null)
    {
        int changed = 0;
        foreach (var ini in ReShadeConfigFiles(gameDir))
        {
            try
            {
                var text = File.ReadAllText(ini);
                var updated = SetShippedAddonsInText(text, enabled, names);
                if (updated == text) continue;
                File.WriteAllText(ini, updated);
                changed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[AuxInstallService.WriteShippedAddonsSetting] Failed for '{ini}' — {ex.Message}");
            }
        }
        if (changed > 0)
            CrashReporter.Log($"[AuxInstallService.WriteShippedAddonsSetting] Built-in add-ons {(enabled ? "enabled" : "disabled")} in {changed} file(s) in '{gameDir}'");
        return changed;
    }

    /// <summary>reshade.ini plus ReShade2.ini, ReShade3.ini… (never ReShadePreset.ini).</summary>
    internal static IEnumerable<string> ReShadeConfigFiles(string gameDir)
    {
        if (!Directory.Exists(gameDir)) yield break;
        foreach (var f in Directory.EnumerateFiles(gameDir, "reshade*.ini"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (name.Equals("reshade", StringComparison.OrdinalIgnoreCase)
                || (name.Length > "reshade".Length && int.TryParse(name["reshade".Length..], out _)))
                yield return f;
        }
    }

    /// <summary>
    /// Pure: edits the [ADDON] DisabledAddons line only, keeping every other line and any other
    /// disabled add-ons. Entries may carry ReShade's "@file" suffix; the name before it is compared.
    /// Enabling with no DisabledAddons line changes nothing; disabling adds the line (and the
    /// section) when missing.
    /// </summary>
    public static string SetShippedAddonsInText(string text, bool enabled, IReadOnlyCollection<string>? names = null)
    {
        var addons = names ?? ShippedAddons;
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

        int section = lines.FindIndex(l => l.Trim().Equals("[ADDON]", StringComparison.OrdinalIgnoreCase));
        int keyLine = -1;
        if (section >= 0)
        {
            for (int i = section + 1; i < lines.Count && !lines[i].TrimStart().StartsWith('['); i++)
            {
                var t = lines[i].TrimStart();
                int eq = t.IndexOf('=');
                if (eq > 0 && t[..eq].Trim().Equals("DisabledAddons", StringComparison.OrdinalIgnoreCase))
                { keyLine = i; break; }
            }
        }

        static string NameOf(string entry)
        {
            var at = entry.IndexOf('@');
            return (at >= 0 ? entry[..at] : entry).Trim();
        }
        bool IsShipped(string entry) =>
            addons.Any(a => a.Equals(NameOf(entry), StringComparison.OrdinalIgnoreCase));

        if (keyLine < 0)
        {
            if (enabled) return text;
            var line = "DisabledAddons=" + string.Join(",", addons);
            if (section >= 0) lines.Insert(section + 1, line);
            else lines.InsertRange(0, ["[ADDON]", line, ""]);
            return string.Join(nl, lines);
        }

        var raw = lines[keyLine];
        var value = raw[(raw.IndexOf('=') + 1)..];
        var entries = value.Split(',').Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
        if (enabled)
        {
            entries.RemoveAll(IsShipped);
        }
        else
        {
            foreach (var a in addons)
                if (!entries.Any(e => NameOf(e).Equals(a, StringComparison.OrdinalIgnoreCase)))
                    entries.Add(a);
        }

        var newLine = raw[..(raw.IndexOf('=') + 1)] + string.Join(",", entries);
        if (newLine == raw) return text;
        lines[keyLine] = newLine;
        return string.Join(nl, lines);
    }
}
