// SettingsHandler.ThemeShare.cs -- Share theme / Import theme in the Colour theme card (Custom). A theme travels as
// one line of text ("RHI-THEME:180213-23141B-…", ThemePalette.ToShareCode): Share copies it to the clipboard and
// shows it; Import takes a pasted code (surrounding text is fine) and makes it the Custom theme. No files.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private void ShowShareThemeFlyout(Button anchor)
    {
        var code = CurrentCustom().ToShareCode();
        bool copied = false;
        try
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(code);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            copied = true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SettingsHandler.ShareTheme] Clipboard failed — {ex.Message}");
        }
        CrashReporter.Log($"[SettingsHandler.ShareTheme] {code}");

        var panel = new StackPanel { Spacing = 8, Width = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = copied
                ? "Theme code copied. Paste it anywhere to share your colours; others use Import theme to apply it."
                : "Copy this code to share your colours; others use Import theme to apply it.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        });
        var box = new TextBox
        {
            Text = code,
            IsReadOnly = true,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
        };
        panel.Children.Add(box);
        var flyout = new Flyout { Content = panel };
        flyout.Opened += (_, _) => box.SelectAll();
        flyout.ShowAt(anchor);
    }

    private void ShowImportThemeFlyout(Button anchor)
    {
        var panel = new StackPanel { Spacing = 8, Width = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = "Paste a theme code (it starts with RHI-THEME:). It replaces your custom colours.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        });
        var box = new TextBox
        {
            PlaceholderText = ThemePalette.SharePrefix + "…",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
        };
        panel.Children.Add(box);
        var note = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
        panel.Children.Add(note);
        var importBtn = new Button { Content = "Import", Style = FindStyle("SettingsPrimaryButtonStyle") };
        panel.Children.Add(importBtn);
        var flyout = new Flyout { Content = panel };

        void Import()
        {
            if (!ThemePalette.TryParseShareCode(box.Text, out var palette))
            {
                note.Text = "That isn't a theme code. Copy the whole code, from RHI-THEME: to the last colour.";
                note.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
                note.Visibility = Visibility.Visible;
                return;
            }
            SetCustom(palette, save: true);
            SelectSavedTheme();
            CrashReporter.Log($"[SettingsHandler.ImportTheme] Imported {palette.ToShareCode()}");
            flyout.Hide();
        }

        importBtn.Click += (_, _) =>
        {
            try { Import(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                CrashReporter.Log($"[SettingsHandler.ImportTheme] {ex.Message}");
            }
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            try { Import(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
            {
                CrashReporter.Log($"[SettingsHandler.ImportTheme] {ex.Message}");
            }
        };
        box.TextChanged += (_, _) => note.Visibility = Visibility.Collapsed;

        // A code already on the clipboard is filled in
        flyout.Opened += async (_, _) =>
        {
            try
            {
                box.Focus(FocusState.Programmatic);
                var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
                var text = await content.GetTextAsync();
                if (string.IsNullOrEmpty(box.Text) && ThemePalette.TryParseShareCode(text, out _))
                {
                    box.Text = text.Trim();
                    box.SelectAll();
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
            {
                CrashReporter.Log($"[SettingsHandler.ImportTheme] Clipboard read failed — {ex.Message}");
            }
        };
        flyout.ShowAt(anchor);
    }
}
