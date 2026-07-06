using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

/// <summary>
/// Shows a selection dialog listing .ini files from the reshade-presets folder.
/// Selected presets are copied to the game's install folder on Deploy.
/// </summary>
public static class PresetPopupHelper
{
    public static readonly string PresetsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "inis", "reshade-presets");

    /// <summary>
    /// Shows the preset selection popup.
    /// Returns the list of selected preset filenames, or null if cancelled.
    /// </summary>
    public static async Task<List<string>?> ShowAsync(XamlRoot xamlRoot)
    {
        Directory.CreateDirectory(PresetsDir);

        var iniFiles = Directory.GetFiles(PresetsDir, "*.ini")
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Cast<string>()
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (iniFiles.Count == 0)
        {
            var emptyPathLink = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.6,
                Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                TextWrapping = TextWrapping.Wrap,
            };
            var emptyLinkRun = new Microsoft.UI.Xaml.Documents.Hyperlink();
            emptyLinkRun.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = PresetsDir });
            emptyLinkRun.Click += (s, ev) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", PresetsDir); }
                catch { }
            };
            emptyPathLink.Inlines.Add(emptyLinkRun);

            var emptyDlg = new ContentDialog
            {
                Title = "Select ReShade Presets",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "No preset files found.",
                            FontSize = 13,
                            Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                        },
                        new TextBlock
                        {
                            Text = "Place .ini files in:",
                            FontSize = 11,
                            Opacity = 0.6,
                            Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                        },
                        emptyPathLink,
                    },
                },
                PrimaryButtonText = "Open Folder",
                CloseButtonText = "Cancel",
                XamlRoot = xamlRoot,
                Background = Brush(ResourceKeys.SurfaceOverlayBrush),
                RequestedTheme = ElementTheme.Dark,
                MinWidth = 500,
            };

            var emptyResult = await DialogService.ShowSafeAsync(emptyDlg);
            if (emptyResult == ContentDialogResult.Primary)
            {
                try { System.Diagnostics.Process.Start("explorer.exe", PresetsDir); }
                catch { }
            }
            return null;
        }

        var panel = new StackPanel { Spacing = 4 };
        var checkBoxes = new List<(string FileName, CheckBox Box)>();

        var pathLink = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.6,
            Foreground = Brush(ResourceKeys.TextPrimaryBrush),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };
        pathLink.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Presets from: " });
        var linkRun = new Microsoft.UI.Xaml.Documents.Hyperlink();
        linkRun.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = PresetsDir });
        linkRun.Click += (s, e) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", PresetsDir); }
            catch { }
        };
        pathLink.Inlines.Add(linkRun);
        panel.Children.Add(pathLink);

        foreach (var file in iniFiles)
        {
            var cb = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = file,
                    FontSize = 13,
                    Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                },
                IsChecked = false,
            };
            checkBoxes.Add((file, cb));
            panel.Children.Add(cb);
        }

        var scrollViewer = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dlg = new ContentDialog
        {
            Title = "Select ReShade Presets",
            Content = scrollViewer,
            PrimaryButtonText = "Deploy",
            IsPrimaryButtonEnabled = false,
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
            Background = Brush(ResourceKeys.SurfaceOverlayBrush),
            RequestedTheme = ElementTheme.Dark,
            MinWidth = 500,
        };

        // Enable Deploy only when at least one preset is ticked
        foreach (var (_, box) in checkBoxes)
        {
            box.Checked += (s, e) => dlg.IsPrimaryButtonEnabled = checkBoxes.Any(cb => cb.Box.IsChecked == true);
            box.Unchecked += (s, e) => dlg.IsPrimaryButtonEnabled = checkBoxes.Any(cb => cb.Box.IsChecked == true);
        }

        var result = await DialogService.ShowSafeAsync(dlg);
        if (result != ContentDialogResult.Primary)
            return null;

        var selected = new List<string>();
        foreach (var (fileName, box) in checkBoxes)
        {
            if (box.IsChecked == true)
                selected.Add(fileName);
        }
        return selected;
    }

    /// <summary>
    /// Copies the selected preset .ini files to the game's install folder.
    /// </summary>
    public static int DeployPresets(List<string> selectedFiles, string installPath)
    {
        int deployed = 0;
        foreach (var fileName in selectedFiles)
        {
            var src = Path.Combine(PresetsDir, fileName);
            if (!File.Exists(src)) continue;
            var dest = Path.Combine(installPath, fileName);
            try
            {
                File.Copy(src, dest, overwrite: true);
                deployed++;
                CrashReporter.Log($"[PresetPopupHelper.DeployPresets] Copied '{fileName}' to '{installPath}'");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[PresetPopupHelper.DeployPresets] Failed to copy '{fileName}' — {ex.Message}");
            }
        }
        return deployed;
    }

    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];
}
