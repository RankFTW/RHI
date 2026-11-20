// SettingsHandler.ReLimiterPcl.cs -- "PC Latency (PCL)" row in Settings › Components › ReLimiter, under DLSS hooks.
// Built in C# (XAML constraint), added the first time Settings opens. Adds or removes the Windows account's
// Performance Log Users membership (PclPermissionService), which ReLimiter v4 needs to read NVIDIA PC Latency.
// Both directions show a warning first: the permission covers the whole Windows account, not just ReLimiter.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private Button? _pclButton;
    private TextBlock? _pclStatusText;
    private bool _pclRowFailed;
    private bool _pclBusy;
    private bool _pclMember; // what the button does next: true = Remove

    private void EnsureReLimiterPclSetting()
    {
        if (_pclButton != null) { _ = RefreshPclRowAsync(); return; }
        if (_pclRowFailed) return;
        try
        {
            if (FindRowWith(_window.SettingsCardsPanel, _window.UlDlssHooksCombo, null) is not (Grid hooksRow, StackPanel column))
            {
                _pclRowFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureReLimiterPclSetting] DLSS hooks row not found — setting not added");
                return;
            }

            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
            var label = new TextBlock
            {
                Text = "PC Latency (PCL)",
                Style = FindStyle("SettingsLabelStyle"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(label,
                "ReLimiter v4 can show NVIDIA PC Latency (PCL) readings on its overlay.\n"
              + "Windows only allows that when your account is in its Performance Log Users group.\n"
              + "Once it's allowed, RHI turns PCL on in a game's relimiter.ini when it installs ReLimiter v4 there.");
            row.Children.Add(label);

            var btn = new Button
            {
                Content = "Checking...",
                Style = FindStyle("SettingsButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = false,
            };
            btn.Click += PclButton_Click;
            Grid.SetColumn(btn, 1);
            row.Children.Add(btn);

            var status = new TextBlock
            {
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            };

            int at = column.Children.IndexOf(hooksRow) + 1;
            column.Children.Insert(at, row);
            column.Children.Insert(at + 1, status);
            _pclButton = btn;
            _pclStatusText = status;
            _ = RefreshPclRowAsync();
        }
        catch (Exception ex)
        {
            _pclRowFailed = true;
            CrashReporter.Log($"[SettingsHandler.EnsureReLimiterPclSetting] Failed — {ex.Message}");
        }
    }

    private async Task RefreshPclRowAsync()
    {
        if (_pclButton == null || _pclStatusText == null || _pclBusy) return;
        var s = await Task.Run(PclPermissionService.GetStatus);
        _pclMember = s.DirectMember == true;
        _pclButton.Content = _pclMember ? "Remove PCL..." : "Allow PCL...";
        _pclButton.IsEnabled = true;

        (string text, string brush) = s switch
        {
            { DirectMember: true, InToken: true } => ("Allowed. ReLimiter v4 can read PC Latency.", ResourceKeys.AccentGreenBrush),
            { DirectMember: true } => ("Allowed. Sign out of Windows and back in to finish.", ResourceKeys.AccentAmberBrush),
            { DirectMember: false, InToken: true } => ("Removed. Sign out of Windows and back in to finish.", ResourceKeys.AccentAmberBrush),
            { DirectMember: false } => ("Not allowed. ReLimiter v4 can't read PC Latency.", ResourceKeys.TextTertiaryBrush),
            _ => ("RHI couldn't check this permission.", ResourceKeys.TextTertiaryBrush),
        };
        _pclStatusText.Text = text;
        _pclStatusText.Foreground = UIFactory.Brush(brush);
    }

    private async void PclButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_pclBusy || _pclButton == null || _pclStatusText == null) return;
            bool enable = !_pclMember;
            bool admin = VulkanLayerService.IsRunningAsAdmin();

            var body = new StackPanel { Spacing = 10, MaxWidth = 480 };
            void Para(string t) => body.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
            if (enable)
            {
                Para("ReLimiter v4 can show NVIDIA PC Latency (PCL) on its overlay, the delay from your input to the frame on screen. "
                   + "Windows only lets it read that when your account is in the Performance Log Users group.");
                Para("This adds your Windows account to that group. It applies to your whole account, not just ReLimiter: "
                   + "any program you run will be able to record Windows performance data, which normally needs admin rights.");
                Para((admin ? "" : "Windows will ask for admin permission. ")
                   + "Sign out of Windows and back in afterwards for it to take effect. You can remove it here at any time.");
            }
            else
            {
                Para("This removes your Windows account from the Performance Log Users group. ReLimiter v4 will no longer be able to read PC Latency.");
                Para((admin ? "" : "Windows will ask for admin permission. ")
                   + "Sign out of Windows and back in afterwards for it to take effect. "
                   + "If another program added you to this group, it loses the permission too.");
            }

            var dialog = new ContentDialog
            {
                Title = enable ? "Allow PC Latency for ReLimiter?" : "Remove the PC Latency permission?",
                Content = body,
                PrimaryButtonText = enable ? "Allow" : "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return;

            _pclBusy = true;
            _pclButton.IsEnabled = false;
            _pclStatusText.Text = admin ? "Working..." : "Waiting for the Windows admin prompt...";
            _pclStatusText.Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush);
            var (ok, error) = await PclPermissionService.SetAsync(enable);
            _pclBusy = false;
            await RefreshPclRowAsync();
            if (!ok && error != null)
            {
                _pclStatusText.Text = error;
                _pclStatusText.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
            }
        }
        catch (Exception ex)
        {
            _pclBusy = false;
            if (_pclButton != null) _pclButton.IsEnabled = true;
            CrashReporter.Log($"[SettingsHandler.PclButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }
}
