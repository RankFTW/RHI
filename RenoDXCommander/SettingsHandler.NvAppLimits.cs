// SettingsHandler.NvAppLimits.cs -- "NVIDIA App" block in Driver Settings › Profiles: one button
// that stops the NVIDIA App resetting DLSS overrides (NvAppOverrideLimitService). Built in C#
// (XAML constraint), added once when Settings first opens.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private bool _nvAppLimitsSetupDone;
    private Button? _nvAppLimitsButton;

    private void EnsureNvAppLimitsPanel()
    {
        if (_nvAppLimitsSetupDone) return;
        _nvAppLimitsSetupDone = true;
        try
        {
            // The Profiles card's left column holds the WrapPanel with "Create missing profiles"
            var column = FindColumnOfButton(_window.SettingsCardsPanel, "Create missing profiles");
            if (column == null)
            {
                CrashReporter.Log("[SettingsHandler.EnsureNvAppLimitsPanel] Profiles column not found — NVIDIA App block not added");
                return;
            }

            column.Children.Add(new TextBlock
            {
                Text = "NVIDIA App",
                Style = FindStyle("SettingsHeadStyle"),
                Margin = new Thickness(0, 6, 0, 0),
            });
            column.Children.Add(new TextBlock
            {
                Text = "Stops the NVIDIA App resetting DLSS presets and versions on games it doesn't support. Run again after a driver or NVIDIA App update.",
                Style = FindStyle("SettingsHelpStyle"),
                TextWrapping = TextWrapping.Wrap,
            });

            _nvAppLimitsButton = new Button
            {
                Content = "Remove NVIDIA App override limits...",
                Style = FindStyle("SettingsButtonStyle"),
                Foreground = UIFactory.Brush(ResourceKeys.SettingsLabelBrush),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            ToolTipService.SetToolTip(_nvAppLimitsButton,
                "The NVIDIA App only allows DLSS overrides on games NVIDIA has approved, and undoes overrides set by RHI or " +
                "NVIDIA Profile Inspector on other games. This turns that limit off in the NVIDIA App's settings files " +
                "(a backup is kept) and restarts the NVIDIA services. Based on JPersson77's nVAppAppApp script.");
            _nvAppLimitsButton.Click += NvAppLimitsButton_Click;
            column.Children.Add(_nvAppLimitsButton);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureNvAppLimitsPanel] Failed — {ex.Message}");
        }
    }

    private async void NvAppLimitsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var xamlRoot = (sender as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
            if (!NvAppOverrideLimitService.IsNvidiaAppInstalled)
            {
                await ShowNvAppLimitsMessageAsync(xamlRoot, "NVIDIA App not found",
                    "The NVIDIA App isn't installed for this Windows user. Without it nothing resets your DLSS overrides, so there's nothing to change.");
                return;
            }

            var confirm = new ContentDialog
            {
                Title = "Remove NVIDIA App override limits?",
                Content = new TextBlock
                {
                    Text = "RHI will change the NVIDIA App's settings files so it stops undoing DLSS overrides on games NVIDIA hasn't approved. A backup of the files is kept.\n\n" +
                           "Then RHI restarts two NVIDIA services. Windows asks for admin permission, and your screen may go black for a second or two.\n\n" +
                           "Close any running games first. Driver and NVIDIA App updates can bring the limits back, so run this again after updating.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                },
                PrimaryButtonText = "Remove limits",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            if (await DialogService.ShowSafeAsync(confirm) != ContentDialogResult.Primary) return;

            if (_nvAppLimitsButton != null) { _nvAppLimitsButton.IsEnabled = false; _nvAppLimitsButton.Content = "Removing..."; }
            NvAppOverrideLimitService.Result result;
            try
            {
                result = await Task.Run(NvAppOverrideLimitService.RemoveLimits);
            }
            finally
            {
                if (_nvAppLimitsButton != null) { _nvAppLimitsButton.IsEnabled = true; _nvAppLimitsButton.Content = "Remove NVIDIA App override limits..."; }
            }
            CrashReporter.Log($"[SettingsHandler.NvAppLimitsButton_Click] Success={result.Success}: {result.Message}");
            if (_window.IsShuttingDown) return;
            await ShowNvAppLimitsMessageAsync(xamlRoot,
                result.Success ? "NVIDIA App limits removed" : "NVIDIA App limits", result.Message);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.NvAppLimitsButton_Click] {ex.Message}");
        }
    }

    private static async Task ShowNvAppLimitsMessageAsync(XamlRoot? xamlRoot, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, IsTextSelectionEnabled = true },
            CloseButtonText = "OK",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private static Style? FindStyle(string key)
        => Application.Current.Resources.TryGetValue(key, out var s) ? s as Style : null;

    /// <summary>Finds the Panel that contains the Panel holding a Button with this text.</summary>
    private static Panel? FindColumnOfButton(DependencyObject? root, string buttonText)
    {
        Panel? found = null;
        bool Walk(DependencyObject? node, Panel? grandparent, Panel? parent)
        {
            switch (node)
            {
                case Button b when b.Content is string s && string.Equals(s, buttonText, StringComparison.Ordinal):
                    found = grandparent;
                    return true;
                case Panel p:
                    foreach (var c in p.Children) if (Walk(c, parent, p)) return true;
                    return false;
                case Border bd: return Walk(bd.Child, grandparent, parent);
                case ContentControl cc when cc.Content is DependencyObject d: return Walk(d, grandparent, parent);
            }
            return false;
        }
        Walk(root, null, null);
        return found;
    }
}
