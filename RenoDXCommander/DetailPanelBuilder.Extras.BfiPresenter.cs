// DetailPanelBuilder.Extras.BfiPresenter.cs — Extras row for BFI Presenter (artzox/BFI-Presenter): black frame
// insertion clocked by the display. Per game only (never for every game: it flickers by design and needs a fixed
// refresh rate). The route follows the game: the DX add-on next to ReShade for Direct3D 12 / 11 / 9 and OpenGL,
// the Vulkan layer for Vulkan games and games run through DXVK. The install dialog offers the NVIDIA profile
// settings the author's setup asks for (as his Manager does). Settings: DetailPanelBuilder.Extras.BfiPresenterSettings.cs.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>The route a new install uses, or why it can't be installed (Route null).</summary>
    private (BfiRoute? Route, string? Blocked) BfiRouteFor(GameCardViewModel card)
    {
        if (card.IsDxvkInstalled)
        {
            // Vulkan layer through DXVK, but not for 32-bit games (the author: frames don't reach the screen one per
            // refresh there; use the 32-bit add-on for DX11 games instead)
            if (card.Is32Bit)
                return (null, "32-bit games through DXVK can't strobe cleanly (BFI Presenter's own testing). For a 32-bit DirectX 11 game, remove DXVK and use the DX add-on.");
            return (BfiRoute.Vulkan, null);
        }
        bool vulkan = card.RequiresVulkanInstall || card.GraphicsApi == GraphicsApiType.Vulkan;
        if (vulkan) return (BfiRoute.Vulkan, null);

        switch (card.GraphicsApi)
        {
            case GraphicsApiType.DirectX8:
                return (null, "DirectX 8 games need crosire's d3d8to9 first (the game then runs as DirectX 9 with ReShade as d3d9.dll), or DXVK.");
            case GraphicsApiType.DirectX10:
                return (null, "The DX add-on covers Direct3D 12, 11 and 9 and OpenGL. DirectX 10 games can use it through DXVK (install DXVK, then BFI Presenter's Vulkan layer).");
        }
        if (!card.IsRsInstalled)
            return (null, "Install ReShade first: the DX add-on runs inside ReShade (the build with add-on support).");
        if (card.UseNormalReShade)
            return (null, "This game uses the \"No Addons\" ReShade. BFI Presenter needs ReShade with add-on support.");
        if (card.GraphicsApi == GraphicsApiType.DirectX9
            && Version.TryParse(card.RsInstalledVersion ?? "", out var rs) && rs < new Version(6, 8))
            return (null, $"Direct3D 9 games need ReShade 6.8 or newer (this game has {card.RsInstalledVersion}).");
        if (card.GraphicsApi == GraphicsApiType.OpenGL && GpuInfoService.Cached is { Count: 0 })
            return (null, "OpenGL games need an NVIDIA graphics card (BFI Presenter shares their frames through WGL_NV_DX_interop2).");
        return (BfiRoute.Dx, null);
    }

    private static string BfiRouteLabel(BfiRoute route, bool is32Bit) => route == BfiRoute.Vulkan
        ? "Vulkan layer"
        : (is32Bit ? "DX add-on (32-bit)" : "DX add-on");

    private void BuildBfiPresenterRow(GameCardViewModel card, StackPanel body)
    {
        var vm          = _window.ViewModel;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var installPath = card.InstallPath ?? "";
        var svc         = App.Services.GetRequiredService<BfiPresenterService>();

        var installedAs = vm.GetBfiPresenterInstalledAs(gameName, store);
        bool isInstalled = BfiPresenterService.IsInstalledIn(installPath, installedAs);
        var (route, blocked) = isInstalled ? (BfiPresenterService.RouteOf(installedAs!), null) : BfiRouteFor(card);

        string statusText  = isInstalled ? (svc.AnyStagedVersion is { } v ? "v" + v : "Installed") : "Ready";
        string statusColor = isInstalled ? "#5ECB7D" : "#A0AABB";

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelColW(1, 0, 350, _window.ExtrasContainer.ActualWidth)) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var label = new TextBlock
        {
            Text = "BFI Presenter",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tip = "BFI Presenter by artzox: black frame insertion for sharper motion on fixed-refresh displays (OLED and LCD; built on a 120 Hz OLED, "
            + "higher refresh rates work too). Variable MPRT keeps full brightness. SDR and HDR. Flickers by design.\n"
            + "Game: needs a fixed refresh rate (G-SYNC / VRR off) and no frame limiter. Works with DLSS, FSR and XeSS frame generation, not Lossless Scaling.\n"
            + "API: Direct3D 12, 11 and 9 and OpenGL (NVIDIA) through a ReShade add-on; Vulkan and DXVK games through a Vulkan layer.";
        if (isInstalled) tip += $"\n\nInstalled as the {BfiRouteLabel(route!.Value, card.Is32Bit)}.";
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
            TextDecorations = isInstalled ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
        };
        if (isInstalled)
        {
            ToolTipService.SetToolTip(statusBlock, "Click to open GitHub releases");
            statusBlock.PointerPressed += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(BfiPresenterService.ReleasesUrl));
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
        ToolTipService.SetToolTip(infoBtn, "Open BFI Presenter's GitHub page");
        infoBtn.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(BfiPresenterService.RepoUrl));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        string btnLabel = isInstalled ? "↺  Reinstall BFI Presenter" : "⬇  Install BFI Presenter";
        bool btnEnabled = isInstalled || route != null;
        var installBtn = new Button
        {
            Content = btnEnabled ? btnLabel : "⚠  Not available here",
            FontSize = 12, Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8),
            Background  = isInstalled ? UIFactory.GetBrush("#182840") : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground  = isInstalled ? UIFactory.GetBrush("#7AACDD") : UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = isInstalled ? UIFactory.GetBrush("#2A4468") : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            IsEnabled = btnEnabled,
            Opacity = btnEnabled ? 1.0 : 0.35,
        };
        if (!btnEnabled && blocked != null)
        {
            ToolTipService.SetToolTip(installBtn, blocked);
            // Disabled buttons show no tooltip: put the reason on the label too
            ToolTipService.SetToolTip(label, $"{tip}\n\n{blocked}");
        }
        else if (route != null)
            ToolTipService.SetToolTip(installBtn, $"Install the {BfiRouteLabel(route.Value, card.Is32Bit)} in this game");

        installBtn.Click += async (_, _) =>
        {
            if (string.IsNullOrEmpty(installPath) || route == null) return;
            try
            {
                var xamlRoot = installBtn.XamlRoot ?? _window.Content.XamlRoot;
                var choice = await ShowBfiInstallDialogAsync(card, route.Value, isInstalled, xamlRoot);
                if (choice == null) return;
                installBtn.IsEnabled = false;
                installBtn.Content = "Downloading...";
                var error = await InstallBfiPresenterAsync(card, route.Value, choice);
                if (error != null)
                {
                    installBtn.Content = "Install failed";
                    installBtn.IsEnabled = true;
                    ToolTipService.SetToolTip(installBtn, error);
                    await DialogService.ShowSafeAsync(new ContentDialog
                    {
                        Title = "BFI Presenter wasn't installed",
                        Content = new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap },
                        CloseButtonText = "OK",
                        XamlRoot = xamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    });
                    return;
                }
                RequestExtrasRebuild(card);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or HttpRequestException or TaskCanceledException or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[BuildBfiPresenterRow] Install failed — {ex.Message}");
                installBtn.Content = "Install failed";
                installBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        var cogBtn = CogDialog.MakeCogButton("BFI Presenter settings");
        cogBtn.Click += async (_, _) =>
        {
            try { await ShowBfiPresenterSettingsAsync(card); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[BuildBfiPresenterRow] Settings dialog failed — {ex.Message}");
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
            Opacity = isInstalled ? 1.0 : 0,
            IsHitTestVisible = isInstalled,
        };
        ToolTipService.SetToolTip(removeBtn, route == BfiRoute.Vulkan
            ? "Remove BFI Presenter from this game (deletes its BFIPresenter.ini, which switches the Vulkan layer on here). The layer stays installed for other games; remove it in the ⚙."
            : "Remove BFI Presenter from this game (the add-on and its BFIPresenter.ini)");
        removeBtn.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(installedAs)) return;
            if (card.IsRunning)
            {
                ToolTipService.SetToolTip(removeBtn, "Close the game first: its files are in use.");
                return;
            }
            if (svc.Uninstall(installPath, installedAs))
            {
                vm.SetBfiPresenterInstalledAs(gameName, null, store);
                RequestExtrasRebuild(card);
            }
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    /// <summary>What the install dialog asked for.</summary>
    private sealed record BfiInstallChoice(
        bool GsyncOff, bool VsyncForceOn, bool FpsLimitOff, bool MaxPerformance, bool OpenGlLayered,
        bool FullBrightness, bool Lcd, bool AllUsers);

    /// <summary>
    /// Shown before every install: flicker and anti-cheat, the setup the author lists, and the NVIDIA profile settings
    /// RHI can set for the game (ticked by default, as his Manager offers them). Null = cancelled.
    /// </summary>
    private async Task<BfiInstallChoice?> ShowBfiInstallDialogAsync(GameCardViewModel card, BfiRoute route, bool reinstall, XamlRoot? xamlRoot)
    {
        xamlRoot ??= _window.Content.XamlRoot;
        if (xamlRoot == null) return null;
        bool nvidia = _dlssPresetService.IsSupported;
        bool openGl = card.GraphicsApi == GraphicsApiType.OpenGL;

        var content = new StackPanel { Spacing = 8, MaxWidth = 560 };
        void Para(string text, string brush = ResourceKeys.TextPrimaryBrush) => content.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20,
            Foreground = UIFactory.Brush(brush),
        });
        CheckBox Tick(string text, bool on, string tooltip)
        {
            var cb = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = on, FontSize = 12 };
            ToolTipService.SetToolTip(cb, tooltip);
            content.Children.Add(cb);
            return cb;
        }

        Para("BFI Presenter strobes the screen: it flickers by design. If you're sensitive to flicker or flashing images, take care. Ctrl+Shift+B turns the strobe off while playing.",
            ResourceKeys.AccentAmberBrush);
        Para("ReShade add-ons and Vulkan layers can count as cheating in online games with anti-cheat. Don't use it there.");
        Para(route == BfiRoute.Vulkan
            ? "This is a Vulkan game, so BFI Presenter is installed as its Vulkan layer (once for your Windows user), and BFIPresenter.ini goes next to the game's exe, which switches the layer on for this game only."
              + (card.IsDxvkInstalled ? " The game runs through DXVK; leave DXVK's frame limit off." : "")
            : $"BFI Presenter goes next to ReShade as {(card.Is32Bit ? BfiPresenterService.Addon32 : BfiPresenterService.Addon64)}. BFI starts when the game first resizes its window; if it doesn't, change resolution or display mode once."
              + (card.GraphicsApi is GraphicsApiType.DirectX11 or GraphicsApiType.DirectX9 ? " Use borderless or windowed mode: exclusive fullscreen isn't supported in DirectX 11 and 9 games." : ""));
        Para("Needed in every game: a fixed refresh rate (no G-SYNC / VRR) and no frame limiter on the game (NVIDIA, RivaTuner, ReLimiter, Display Commander). In-game settings are in the BFI Presenter tab of the ReShade overlay.",
            ResourceKeys.TextSecondaryBrush);

        CheckBox? gsync = null, vsync = null, fps = null, power = null, ogl = null;
        if (nvidia)
        {
            content.Children.Add(new TextBlock { Text = "NVIDIA settings for this game", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            gsync = Tick("G-SYNC off", true, "Sets G-SYNC off in this game's NVIDIA profile. BFI needs a fixed refresh rate; G-SYNC can pull the display below it.");
            vsync = Tick("Vertical sync Force on", true, "Sets Vertical Sync to Force on in this game's NVIDIA profile. Never Force off or Fast Sync with BFI.");
            fps   = Tick("NVIDIA frame rate limit off", true, "Sets Max Frame Rate off for this game (overrides a global limit). Any limiter acting on the game breaks the strobe; BFI Presenter has its own frame cap.");
            power = Tick("Power management: prefer maximum performance", true, "So the graphics card doesn't clock down between frames (the BFI Presenter Manager offers this at install too).");
            if (openGl)
                ogl = Tick("Vulkan/OpenGL present method: prefer layered on DXGI swapchain", true, "Old OpenGL games that present by exchange block the presenter without it (DOOM 3 BFG ran at about 30 fps).");
        }

        content.Children.Add(new TextBlock { Text = "BFI Presenter settings", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        var bright = Tick("Full brightness (Gain 1.0, recommended)", true,
            "BFI Presenter starts at Gain 0.5, where the lit refresh is unchanged and the picture is half as bright. The author recommends Gain 1.0 for full brightness, especially in HDR. Change it later in the ⚙ or the ReShade overlay.");
        var lcd = Tick("My display is an LCD (IPS, VA or TN)", false,
            "Turns on \"LCD: prevent image retention\". With BFI every lit refresh gets the same LCD drive voltage, which can leave a ghost image after long sessions. Not needed on OLED.");
        CheckBox? allUsers = null;
        if (route == BfiRoute.Vulkan && !VulkanLayerService.IsRunningAsAdmin() && !BfiPresenterService.IsLayerRegisteredForAllUsers)
            allUsers = Tick("Also for games run as administrator (asks for administrator rights)", false,
                "Windows' Vulkan loader ignores per-user layers in programs run as administrator: User Account Control off, Steam or the game set to run as administrator. Tick this if that applies here.");

        var dialog = new ContentDialog
        {
            Title = reinstall ? "Reinstall BFI Presenter" : "Install BFI Presenter",
            Content = new ScrollViewer { Content = content, MaxHeight = 560 },
            PrimaryButtonText = reinstall ? "Reinstall" : "Install",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return null;
        return new BfiInstallChoice(
            gsync?.IsChecked == true, vsync?.IsChecked == true, fps?.IsChecked == true, power?.IsChecked == true,
            ogl?.IsChecked == true, bright.IsChecked == true, lcd.IsChecked == true, allUsers?.IsChecked == true);
    }

    /// <summary>Downloads, installs and sets up BFI Presenter in the game. Returns an error message, or null.</summary>
    private async Task<string?> InstallBfiPresenterAsync(GameCardViewModel card, BfiRoute route, BfiInstallChoice choice)
    {
        var vm = _window.ViewModel;
        var svc = App.Services.GetRequiredService<BfiPresenterService>();
        var gameName = card.GameName;
        var store = card.Source ?? "";
        var installPath = card.InstallPath!;
        bool is32 = card.Is32Bit;
        var existing = vm.GetBfiPresenterInstalledAs(gameName, store);

        // The tested-games list: games that never resize their swap chain get PresenterResize (DX), as the Manager does
        var tested = BfiPresenterService.FindTested(await svc.GetTestedGamesAsync(), gameName);
        int? presenterResize = tested?.PresenterResize is int r && !(tested.PresenterResizeOnlyWithoutRenoDx && card.IsRdxInstalled) ? r : null;

        var result = await Task.Run(async () =>
        {
            await svc.CheckForUpdateAsync(forceRefresh: true).ConfigureAwait(false);
            if (!await svc.EnsureStagingAsync(route).ConfigureAwait(false))
                return (Rel: (string?)null, Error: (string?)"BFI Presenter couldn't be downloaded. Check your connection and try again.");

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (choice.FullBrightness) values["Gain"] = "1.0";
            if (choice.Lcd) values["LcdPhaseSwap"] = "1";

            string? rel;
            if (route == BfiRoute.Dx)
            {
                // A Vulkan-route install in the same game would leave its ini switching the layer on: remove it
                if (existing != null && BfiPresenterService.RouteOf(existing) == BfiRoute.Vulkan) svc.Uninstall(installPath, existing);
                rel = svc.InstallDx(installPath, is32);
                if (rel == null) return (null, "Couldn't copy the add-on into the game's ReShade folder. Close the game and check the folder isn't read-only.");
                var ini = BfiPresenterService.IniPathOf(installPath, rel)!;
                bool hasIni = File.Exists(ini);
                // Keep the user's own settings on a reinstall: only fill in what isn't there yet
                foreach (var kv in values)
                    if (!hasIni || BfiPresenterService.GetIni(ini, kv.Key) == null) BfiPresenterService.SetIni(ini, kv.Key, kv.Value);
                if (presenterResize is int pr && BfiPresenterService.GetIni(ini, "PresenterResize") is null or "0")
                    BfiPresenterService.SetIni(ini, "PresenterResize", pr.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                if (existing != null && BfiPresenterService.RouteOf(existing) == BfiRoute.Dx)
                    svc.Uninstall(installPath, existing, keepSettings: true);
                var layerError = svc.InstallOrUpdateLayer();
                if (layerError != null) return (null, layerError);
                var exe = App.Services.GetRequiredService<IPeHeaderService>().FindGameExe(installPath);
                var exeDir = exe != null ? Path.GetDirectoryName(exe)! : installPath;
                bool newIni = !File.Exists(Path.Combine(exeDir, BfiPresenterService.IniFileName));
                rel = svc.InstallVulkan(installPath, exeDir, newIni ? values : null);
                if (rel == null) return (null, $"Couldn't put BFIPresenter.ini in '{exeDir}'. Check the folder isn't read-only.");
            }
            return (rel, null);
        });
        if (result.Error != null) return result.Error;

        vm.SetBfiPresenterInstalledAs(gameName, result.Rel, store);
        if (route == BfiRoute.Vulkan && choice.AllUsers && !await svc.SetAllUsersAsync(remove: false))
            CrashReporter.Log("[InstallBfiPresenter] Not registered for all users (declined or failed)");

        // NVIDIA profile, off the UI thread (NVAPI)
        if (_dlssPresetService.IsSupported && (choice.GsyncOff || choice.VsyncForceOn || choice.FpsLimitOff || choice.MaxPerformance || choice.OpenGlLayered))
        {
            await Task.Run(() =>
            {
                var p = _dlssPresetService;
                if (choice.GsyncOff && !p.SetPerGameGSyncEnabled(gameName, installPath, false)) CrashReporter.Log("[InstallBfiPresenter] G-SYNC off not set");
                if (choice.VsyncForceOn && !p.SetVSyncMode(gameName, installPath, VSyncForceOn)) CrashReporter.Log("[InstallBfiPresenter] VSync not set");
                if (choice.FpsLimitOff && !p.SetPerGameFpsLimit(gameName, installPath, 0)) CrashReporter.Log("[InstallBfiPresenter] FPS limit not set");
                if (choice.MaxPerformance && !p.SetPowerManagementMode(gameName, installPath, PowerMaxPerformance)) CrashReporter.Log("[InstallBfiPresenter] Power mode not set");
                if (choice.OpenGlLayered && !p.SetVulkanPresentMethod(gameName, installPath, PresentLayeredOnDxgi)) CrashReporter.Log("[InstallBfiPresenter] Present method not set");
            });
            RefreshDriverSettingsIfShown(card);
        }
        CrashReporter.Log($"[InstallBfiPresenter] '{gameName}': {route} → '{result.Rel}' (tested: {tested?.Status ?? "not listed"})");
        return null;
    }

    private const uint VSyncForceOn        = 0x47814940;
    private const uint VSyncForceOff       = 0x08416747;
    private const uint VSyncFastSync       = 0x18888888;
    private const uint PowerMaxPerformance = 0x00000001;
    private const uint PresentLayeredOnDxgi = 0x00000001;
}
