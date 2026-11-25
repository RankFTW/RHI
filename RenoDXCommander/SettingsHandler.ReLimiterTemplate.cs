// SettingsHandler.ReLimiterTemplate.cs: Settings › Components › ReLimiter and the global relimiter.ini
// (%LocalAppData%\RHI\inis\relimiter.ini, AuxInstallService.UlIniPath), the template new installs are seeded from.
// The template is the source of truth for the ReLimiter boxes: each time Settings opens, every value it holds is
// shown (and copied into settings.json, so seeding agrees). "Save to global relimiter.ini" (added in C# beside
// "Write to all ReLimiter games", XAML constraint) writes the boxes into the template only; games' relimiter.ini
// files are left alone. "Write to all ReLimiter games" is unchanged (games + template).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private const string UlTemplateSection = "FrameLimiter";
    private Button? _ulSaveTemplateBtn;
    private TextBlock? _ulSaveTemplateStatus;

    /// <summary>The template's [FrameLimiter] values, or an empty dictionary when it's missing or unreadable.</summary>
    private static Dictionary<string, string> ReadUlTemplate()
    {
        try
        {
            if (!File.Exists(AuxInstallService.UlIniPath)) return new(StringComparer.OrdinalIgnoreCase);
            var ini = AuxInstallService.ParseIni(File.ReadAllLines(AuxInstallService.UlIniPath));
            return ini.TryGetValue(UlTemplateSection, out var s)
                ? new Dictionary<string, string>(s, StringComparer.OrdinalIgnoreCase)
                : new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SettingsHandler.ReadUlTemplate] {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Before the ReLimiter boxes are filled: copies the template's OSD key, shared presets, DLSS hooks and frame rate
    /// cap into settings.json. A key the template doesn't have leaves the setting as it is.
    /// </summary>
    private void SyncUlSettingsFromTemplate()
    {
        var t = ReadUlTemplate();
        if (t.Count == 0) return;
        var s = ViewModel.Settings;
        bool changed = false;
        if (t.TryGetValue("osd_toggle_key", out var osd) && !string.IsNullOrWhiteSpace(osd)
            && !string.Equals(s.UlOsdHotkey, osd.Trim(), StringComparison.Ordinal))
        { s.UlOsdHotkey = osd.Trim(); changed = true; }
        if (TryBool(t, "shared_presets", out bool shared) && s.UlSharedPresets != shared) { s.UlSharedPresets = shared; changed = true; }
        if (TryBool(t, "dlss_info_hooks", out bool hooks) && s.UlDlssHooks != hooks) { s.UlDlssHooks = hooks; changed = true; }
        if (t.TryGetValue("target_fps", out var fpsText) && int.TryParse(fpsText.Trim(), out int fps) && fps >= 0 && s.UlTargetFps != fps)
        { s.UlTargetFps = fps; changed = true; }
        if (t.TryGetValue(UlV4Rules.PacingKey, out var pacingText) && UlV4Rules.NormalizePacing(pacingText) is { } pacing
            && !string.Equals(s.UlPacingPreference, pacing, StringComparison.Ordinal))
        { s.UlPacingPreference = pacing; changed = true; }
        if (!changed) return;
        ViewModel.SaveSettingsPublic();
        CrashReporter.Log($"[SettingsHandler.SyncUlSettingsFromTemplate] From global relimiter.ini: osd={s.UlOsdHotkey}, shared={s.UlSharedPresets}, hooks={s.UlDlssHooks}, fps={s.UlTargetFps}, pacing={s.UlPacingPreference}");

        static bool TryBool(Dictionary<string, string> d, string key, out bool value)
        {
            value = false;
            if (!d.TryGetValue(key, out var v)) return false;
            v = v.Trim();
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1") { value = true; return true; }
            if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0") return true;
            return false;
        }
    }

    /// <summary>The preset / Move OSD / blackout / OLED Care keys: the template's value where it has one (by preset key name).</summary>
    private static void OverlayUlPresetKeysFromTemplate(Dictionary<string, string> values)
    {
        var t = ReadUlTemplate();
        foreach (var bind in ReLimiterPresetKeys.Keys)
            if (t.TryGetValue(bind.GameKey, out var v)) values[bind.PresetKey] = v.Trim();
    }

    /// <summary>Adds "Save to global relimiter.ini" beside "Write to all ReLimiter games" (once).</summary>
    private void EnsureUlSaveTemplateButton()
    {
        if (_ulSaveTemplateBtn != null) { if (_ulSaveTemplateStatus != null) _ulSaveTemplateStatus.Visibility = Visibility.Collapsed; return; }
        try
        {
            var writeAll = _window.ApplyUlHotkeyBtn;
            if (FindParentPanel(_window.SettingsCardsPanel, writeAll) is not { } column)
            {
                CrashReporter.Log("[SettingsHandler.EnsureUlSaveTemplateButton] Write to all button's panel not found");
                return;
            }
            int at = column.Children.IndexOf(writeAll);
            column.Children.RemoveAt(at);

            var btn = new Button { Content = "Save to global relimiter.ini", Style = FindStyle("SettingsButtonStyle") };
            if (Application.Current.Resources.TryGetValue(ResourceKeys.SettingsLabelBrush, out var v) && v is Microsoft.UI.Xaml.Media.Brush brush)
            {
                btn.Foreground = brush;
                btn.Resources["ButtonForegroundPointerOver"] = brush;
                btn.Resources["ButtonForegroundPressed"] = brush;
            }
            ToolTipService.SetToolTip(btn, "Saves the settings above into RHI's global relimiter.ini only "
                + "(%LocalAppData%\\RHI\\inis\\relimiter.ini), the file new ReLimiter installs are set up from. "
                + "Games that already have ReLimiter keep their own relimiter.ini. These boxes always show what the global file holds.");
            btn.Click += UlSaveTemplate_Click;
            _ulSaveTemplateBtn = btn;

            var row = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            row.Children.Add(writeAll);
            row.Children.Add(btn);
            column.Children.Insert(at, row);

            _ulSaveTemplateStatus = new TextBlock
            {
                Style = FindStyle("SettingsHelpStyle"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            column.Children.Insert(at + 1, _ulSaveTemplateStatus);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureUlSaveTemplateButton] Failed — {ex.Message}");
        }
    }

    /// <summary>The Panel that directly holds <paramref name="target"/>, found by walking down (Parent can be null on first open).</summary>
    private static Panel? FindParentPanel(DependencyObject node, UIElement target)
    {
        if (node is Panel panel)
        {
            if (panel.Children.Contains(target)) return panel;
            foreach (var child in panel.Children)
                if (FindParentPanel(child, target) is { } found) return found;
        }
        else if (node is Border { Child: { } bc }) return FindParentPanel(bc, target);
        else if (node is ScrollViewer { Content: DependencyObject sc }) return FindParentPanel(sc, target);
        else if (node is ContentControl { Content: DependencyObject cc }) return FindParentPanel(cc, target);
        return null;
    }

    private void UlSaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = ViewModel.Settings;
            s.UlOsdHotkey = _currentUlHotkeyString;
            ViewModel.SaveSettingsPublic();

            var presetKeys = ReLimiterPresetKeys.Keys.ToDictionary(
                k => k.PresetKey,
                k => _ulPresetKeyBoxes.TryGetValue(k.PresetKey, out var box) ? box.Text.Trim() : "",
                StringComparer.OrdinalIgnoreCase);

            var path = AuxInstallService.UlIniPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AuxInstallService.ApplyUlOsdHotkey(path, _currentUlHotkeyString);
            AuxInstallService.ApplyUlSharedPresets(path, s.UlSharedPresets);
            AuxInstallService.ApplyUlDlssHooks(path, s.UlDlssHooks);
            AuxInstallService.ApplyUlTargetFps(path, s.UlTargetFps);
            ReLimiterPresetKeys.WriteToGameIni(path, presetKeys);
            AuxInstallService.ApplyUlPacingPreference(path, s.UlPacingPreference);
            CrashReporter.Log($"[SettingsHandler.UlSaveTemplate_Click] Saved to '{path}': osd={_currentUlHotkeyString}, shared={s.UlSharedPresets}, hooks={s.UlDlssHooks}, fps={s.UlTargetFps}, pacing={s.UlPacingPreference}");
            SetUlSaveTemplateStatus($"Saved to the global relimiter.ini at {DateTime.Now:HH:mm}. Games' relimiter.ini files weren't changed.", false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SettingsHandler.UlSaveTemplate_Click] Failed — {ex.Message}");
            SetUlSaveTemplateStatus($"Couldn't save the global relimiter.ini: {ex.Message}", true);
        }
    }

    private void SetUlSaveTemplateStatus(string text, bool error)
    {
        if (_ulSaveTemplateStatus == null) return;
        _ulSaveTemplateStatus.Text = text;
        _ulSaveTemplateStatus.Foreground = UIFactory.Brush(error ? ResourceKeys.AccentRedBrush : ResourceKeys.AccentGreenBrush);
        _ulSaveTemplateStatus.Visibility = Visibility.Visible;
    }
}
