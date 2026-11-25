// DetailPanelBuilder.Experimental.Crcgi.cs -- Experimental › Global Illumination row for CRCGI (screen-space global
// illumination for ReShade, with its CRCGI Injector add-on). Per game. Install / remove go through MainViewModel.Crcgi.cs
// (the effect through the shader sync, its technique ticked in the game's preset), files and staging through CrcgiService.
// Its settings live in ReShade's overlay (the effect's settings, and Add-ons › CRCGI Injector), so the ⚙ only explains
// and links.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void BuildCrcgiRow(GameCardViewModel card, StackPanel body)
    {
        var vm          = _window.ViewModel;
        var installPath = card.InstallPath ?? "";
        bool isInstalled = CrcgiService.IsInstalledIn(installPath);
        bool isOurs      = isInstalled && CrcgiService.IsRhiInstalled(installPath);
        bool pulled      = FeatureFlags.IsExperimentalHidden(FeatureFlags.ExperimentalCrcgi);
        // Taken out by the manifest: only shown where it's installed, and only to be removed
        var block = pulled
            ? ("Not offered right now", "CRCGI has been taken out of RHI for now. You can still remove it from this game.")
            : isInstalled ? null : vm.CrcgiBlock(card);

        string statusText  = !isInstalled ? "Ready"
                           : !isOurs ? "Manual"
                           : CrcgiService.InstalledVersion(installPath) is { } v ? "v" + v : "Installed";
        string statusColor = !isInstalled ? "#A0AABB" : isOurs ? "#5ECB7D" : "#E8C46A";

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelColW(1, 0, 350, _window.ExperimentalContainer.ActualWidth)) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var label = new TextBlock
        {
            Text = "CRCGI",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tip = "CRCGI by FlibbertigibbetLollygagger adds global illumination (bounced light and soft contact shadows) to the game "
            + "as a ReShade effect, built on radiance cascades. Its CRCGI Injector add-on hands it the game's own normals, colours, "
            + "motion and depth, and keeps the UI and fog out of it.\n"
            + "Game: single player only; never use it online.\n"
            + "API: DirectX 10, 11 or 12, Vulkan or OpenGL (DirectX 9 through DXVK), 32 or 64-bit, with ReShade with add-on support.";
        if (isInstalled && !isOurs) tip += "\n\nCRCGI's add-on is in this game's add-on folder, but RHI didn't install it. Install replaces it with the latest release (the file there is kept as a backup).";
        ToolTipService.SetToolTip(label, tip);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var statusBlock = new TextBlock
        {
            Text = statusText,
            FontSize = 12,
            Foreground = UIFactory.GetBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            TextDecorations = isOurs ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
        };
        if (isOurs)
        {
            ToolTipService.SetToolTip(statusBlock, "Click to open GitHub releases");
            statusBlock.PointerPressed += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(CrcgiService.ReleasesUrl));
            AttachLinkCursor(statusBlock);
        }
        Grid.SetColumn(statusBlock, 1);
        row.Children.Add(statusBlock);

        var infoBtn = new Button
        {
            Content = "Info", FontSize = 11, Padding = new Thickness(6, 2, 6, 2), Width = 36, Height = 32,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(infoBtn, "Open CRCGI's GitHub page");
        infoBtn.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(CrcgiService.RepoUrl));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        bool btnEnabled = block == null;
        var installBtn = new Button
        {
            Content = !btnEnabled ? "⚠  " + block!.Value.Item1
                    : isOurs ? "↺  Reinstall CRCGI" : "⬇  Install CRCGI",
            FontSize = 12, Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8),
            Background  = isOurs ? UIFactory.GetBrush("#182840") : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground  = isOurs ? UIFactory.GetBrush("#7AACDD") : UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = isOurs ? UIFactory.GetBrush("#2A4468") : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            IsEnabled = btnEnabled,
            Opacity = btnEnabled ? 1.0 : 0.35,
        };
        if (!btnEnabled)
        {
            ToolTipService.SetToolTip(installBtn, block!.Value.Item2);
            // Disabled buttons show no tooltip: put the reason on the label too
            ToolTipService.SetToolTip(label, $"{tip}\n\n{block.Value.Item2}");
        }
        else
            ToolTipService.SetToolTip(installBtn, "Install CRCGI in this game: the CRCGI Injector add-on next to ReShade, the effect in reshade-shaders, "
                + "ReShade's Generic Depth add-on on, and the effect turned on in the game's ReShade preset.");

        installBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            try
            {
                if (card.IsRunning)
                {
                    ToolTipService.SetToolTip(installBtn, "Close the game first: its files are in use.");
                    return;
                }
                var xamlRoot = installBtn.XamlRoot ?? _window.Content.XamlRoot;
                installBtn.IsEnabled = false;
                installBtn.Content = "Downloading...";
                var error = await vm.InstallCrcgiAsync(card);
                if (error != null)
                {
                    installBtn.Content = "Install failed";
                    installBtn.IsEnabled = true;
                    ToolTipService.SetToolTip(installBtn, error);
                    await DialogService.ShowSafeAsync(new ContentDialog
                    {
                        Title = "CRCGI wasn't installed",
                        Content = new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap },
                        CloseButtonText = "OK",
                        XamlRoot = xamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    });
                    return;
                }
                RequestExperimentalRebuild(card);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or HttpRequestException or TaskCanceledException or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[BuildCrcgiRow] Install failed — {ex.Message}");
                installBtn.Content = "Install failed";
                installBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        var cogBtn = CogDialog.MakeCogButton("CRCGI settings");
        cogBtn.Click += async (_, _) =>
        {
            try { await ShowCrcgiSettingsAsync(card); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[BuildCrcgiRow] Settings dialog failed — {ex.Message}");
            }
        };
        Grid.SetColumn(cogBtn, 4);
        row.Children.Add(cogBtn);

        var removeBtn = new Button
        {
            Width = 36, Height = 32, Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush) },
            Opacity = isOurs ? 1.0 : 0,
            IsHitTestVisible = isOurs,
        };
        ToolTipService.SetToolTip(removeBtn, "Remove CRCGI from this game (the add-on and the effect)");
        removeBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            if (card.IsRunning)
            {
                ToolTipService.SetToolTip(removeBtn, "Close the game first: its files are in use.");
                return;
            }
            try
            {
                removeBtn.IsEnabled = false;
                await vm.UninstallCrcgiAsync(card);
                RequestExperimentalRebuild(card);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                CrashReporter.Log($"[BuildCrcgiRow] Remove failed — {ex.Message}");
                removeBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }
}
