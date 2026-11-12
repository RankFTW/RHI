// DetailPanelBuilder.Extras.Hsf.cs — Extras section (MFG Unlocks): HSF (Hunter's Sloppy Frames) row, its install
// notice and its ⚙ dialog. The work is in MainViewModel.Hsf.cs / HsfService.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private const string HsfTooltip =
        "HSF (Hunter's Sloppy Frames) — DLSS Frame Generation (up to 6x) for DirectX 9, 11 and 12 games, as a ReShade add-on. "
        + "Uses the game's own DLSS data in DirectX 12 games that run DLSS, optical flow from iMMERSE LaunchPad everywhere else. "
        + "Needs an RTX 40 or newer GPU (RTX 20 / 30 through Universal MFG Unlocker) and ReShade with add-on support.";

    private void BuildHsfRow(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildHsfRow({card.GameName})");
        var svc         = App.Services.GetRequiredService<HsfService>();
        var installPath = card.InstallPath ?? "";

        bool isInstalled  = HsfService.IsInstalledIn(installPath, card.Is32Bit);
        bool rhiInstalled = isInstalled && HsfService.IsRhiInstalled(installPath);
        var installedVer  = rhiInstalled ? HsfService.ReadMarker(installPath).Version : null;
        var block         = MainViewModel.HsfBlock(card);

        string statusText  = !isInstalled ? "Ready" : rhiInstalled ? (installedVer ?? "Installed") : "Manual";
        string statusColor = isInstalled ? "#5ECB7D" : "#A0AABB";

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelColW(1, 0, 350, _window.ExtrasContainer.ActualWidth)) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        // Col 0 — label
        var label = new TextBlock
        {
            Text = "HSF",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(label, HsfTooltip);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        // Col 1 — status
        var statusBlock = new TextBlock
        {
            Text = statusText,
            FontSize = 12,
            Foreground = UIFactory.GetBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            TextDecorations = isInstalled ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
        };
        if (isInstalled)
        {
            ToolTipService.SetToolTip(statusBlock, rhiInstalled
                ? $"HSF {installedVer} installed by RHI\nClick to open HSF's GitHub page"
                : "HSF was installed by hand. Reinstall it with RHI to get updates.\nClick to open HSF's GitHub page");
            statusBlock.PointerPressed += (s, e) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(HsfService.ReleasesPage));
            AttachLinkCursor(statusBlock);
        }
        Grid.SetColumn(statusBlock, 1);
        row.Children.Add(statusBlock);

        // Col 2 — Info button
        var infoBtn = new Button
        {
            Content = "Info",
            FontSize = 11,
            Padding = new Thickness(6, 2, 6, 2),
            Width = 36,
            Height = 32,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(infoBtn, "Open HSF's GitHub page");
        infoBtn.Click += (s, e) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(HsfService.ReleasesPage));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        var installBtn = new Button
        {
            Content = isInstalled ? "↺  Reinstall HSF" : "⬇  Install HSF",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            Background = isInstalled ? UIFactory.GetBrush("#182840") : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = isInstalled ? UIFactory.GetBrush("#7AACDD") : UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = isInstalled ? UIFactory.GetBrush("#2A4468") : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
        };
        if (block is { } b)
        {
            installBtn.IsEnabled = false;
            installBtn.Opacity   = 0.35;
            installBtn.Content   = b.Short;
            // Disabled buttons show no tooltip: the label carries the reason too
            ToolTipService.SetToolTip(label, $"{HsfTooltip}\n\n{b.Long}");
        }
        else
        {
            var versionNote = MainViewModel.HsfReShadeVersionNote(card);
            ToolTipService.SetToolTip(installBtn, (isInstalled
                ? "Reinstall HSF (newest release). HSF.ini, with HSF's and your settings, is kept."
                : "Install HSF: the add-on, its HSF_streamline folder, HSF.ini and HSF.fx, plus iMMERSE LaunchPad for optical flow.")
                + (versionNote != null ? "\n\n" + versionNote : ""));
        }

        installBtn.Click += async (s, e) =>
        {
            try
            {
                if (string.IsNullOrEmpty(installPath)) return;
                if (!await ShowHsfNoticeAsync(card, installBtn.XamlRoot)) return;
                var mfg = await AskHsfMfgUnlockerAsync(card, installBtn.XamlRoot);
                if (mfg.Cancelled) return;

                installBtn.IsEnabled = false;
                installBtn.Content   = "Installing...";
                // Reinstall keeps the game's LaunchPad choice; a new install starts with it on
                bool launchpad = !HsfService.IsRhiInstalled(installPath) || HsfService.ReadMarker(installPath).LaunchpadOn;
                var error = await _window.ViewModel.InstallHsfAsync(card, launchpad);
                if (error == null)
                {
                    if (mfg.Install)
                    {
                        var mfgError = await _window.ViewModel.InstallMfgForHsfAsync(card, mfg.ProxyName);
                        if (mfgError != null)
                            await ShowHsfErrorAsync(mfgError, installBtn.XamlRoot, "Universal MFG Unlocker wasn't installed");
                    }
                    RequestExtrasRebuild(card);
                    return;
                }
                installBtn.Content   = "Install failed";
                installBtn.IsEnabled = true;
                statusBlock.Text = "Failed";
                statusBlock.Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush);
                ToolTipService.SetToolTip(statusBlock, error);
                await ShowHsfErrorAsync(error, installBtn.XamlRoot);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildHsfRow] Install failed — {ex.Message}");
                installBtn.Content   = "Install failed";
                installBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        // Col 4 — Cog button
        var cogBtn = CogDialog.MakeCogButton("HSF settings");
        cogBtn.Click += async (s, e) =>
        {
            try { await ShowHsfSettingsAsync(card, isInstalled, rhiInstalled, svc); }
            catch (Exception ex) { CrashReporter.Log($"[BuildHsfRow] Settings dialog failed — {ex.Message}"); }
        };
        Grid.SetColumn(cogBtn, 4);
        row.Children.Add(cogBtn);

        // Col 5 — Remove button
        var removeBtn = new Button
        {
            Width = 36, Height = 32, Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush) },
            Opacity = isInstalled ? 1.0 : 0,
            IsHitTestVisible = isInstalled,
        };
        ToolTipService.SetToolTip(removeBtn, "Remove HSF from this game (add-on, HSF.ini, HSF_streamline and HSF.fx)");
        removeBtn.Click += async (s, e) =>
        {
            try
            {
                removeBtn.IsEnabled = false;
                await _window.ViewModel.UninstallHsfAsync(card);
                RequestExtrasRebuild(card);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildHsfRow] Remove failed — {ex.Message}");
                removeBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    /// <summary>
    /// Shown before an install: anti-cheat, the game's own / OptiScaler frame generation, and how to open
    /// HSF's menu. "Don't show this again" hides it, except when OptiScaler is installed in the game.
    /// Returns true to continue.
    /// </summary>
    private async Task<bool> ShowHsfNoticeAsync(GameCardViewModel card, XamlRoot? xamlRoot)
    {
        xamlRoot ??= _window.Content.XamlRoot;
        bool osInstalled = card.IsOsInstalled;
        if (xamlRoot == null || (_window.ViewModel.Settings.HsfNoticeDismissed && !osInstalled)) return true;

        var content = new StackPanel { Spacing = 8 };
        void Para(string text) => content.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        Para("Never use HSF in a game with online anti-cheat: it can get your account banned.");
        Para("Turn off the game's own frame generation. HSF's menu is a tab in ReShade's overlay (Home key): set the multiplier there and turn FRAME GEN on.");
        if (osInstalled)
            Para("OptiScaler is installed in this game. HSF and OptiScaler's frame generation can clash: use only one of them, and if the game crashes, remove OptiScaler and try HSF alone.");
        var dontShow = new CheckBox { Content = "Don't show this again", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        if (!osInstalled) content.Children.Add(dontShow);

        var dialog = new ContentDialog
        {
            Title = "Before installing HSF",
            Content = content,
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        var result = await DialogService.ShowSafeAsync(dialog);
        if (dontShow.IsChecked == true)
        {
            _window.ViewModel.Settings.HsfNoticeDismissed = true;
            _window.ViewModel.SaveSettingsPublic();
        }
        return result == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Asked on every HSF install: should Universal MFG Unlocker go in too (RTX 20 / 30 / 40 cards need it)?
    /// Not asked when it's already in the game, MFG Unlock (add-on) is, or the card is RTX 50 or newer.
    /// The detected card sets the default answer. A yes in a 64-bit DirectX 12 game goes on to the DLL name
    /// picker; other games get it in HSF_streamline\mods. Cancelled = install nothing.
    /// </summary>
    private async Task<(bool Cancelled, bool Install, string? ProxyName)> AskHsfMfgUnlockerAsync(GameCardViewModel card, XamlRoot? xamlRoot)
    {
        xamlRoot ??= _window.Content.XamlRoot;
        if (xamlRoot == null || card.Rtx40MfgInstalled || card.MfgAdaInstalled) return (false, false, null);
        var gpu = GpuInfoService.Primary(await GpuInfoService.GetNvidiaGpusAsync());
        if (gpu is { Family: GpuFamily.Rtx50 or GpuFamily.Newer }) return (false, false, null);
        bool needs = gpu != null && GpuInfoService.NeedsMfgUnlocker(gpu.Family);
        bool inGame = MainViewModel.HsfRunsInGame(card);

        var content = new StackPanel { Spacing = 8 };
        void Para(string text) => content.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        Para("RTX 20, 30 and 40 cards need Universal MFG Unlocker for HSF: RTX 20 and 30 for any frame generation, RTX 40 for more than 2x. RTX 50 cards don't need it.");
        Para(gpu == null
            ? "RHI couldn't find an NVIDIA graphics card in this PC."
            : $"Your graphics card: {gpu.Name} ({gpu.FamilyLabel}). {(needs ? "It needs the unlocker." : "It isn't an RTX card, so it can't run DLSS frame generation.")}");
        Para(inGame
            ? "Next you choose the DLL name it's installed as, so it doesn't clash with other mods."
            : "In this game HSF runs frame generation in its helper, so the unlocker goes in HSF_streamline\\mods, where the helper loads it.");

        var dialog = new ContentDialog
        {
            Title = "Install Universal MFG Unlocker too?",
            Content = content,
            PrimaryButtonText = "Install both",
            SecondaryButtonText = "HSF only",
            CloseButtonText = "Cancel",
            DefaultButton = needs ? ContentDialogButton.Primary : ContentDialogButton.Secondary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        var result = await DialogService.ShowSafeAsync(dialog);
        if (result == ContentDialogResult.Secondary) return (false, false, null);
        if (result != ContentDialogResult.Primary) return (true, false, null);
        if (!inGame) return (false, true, null);

        var name = await ShowMfgDllPickerAsync(card, null);
        return name == null ? (true, false, null) : (false, true, name);
    }

    private static async Task ShowHsfErrorAsync(string error, XamlRoot? xamlRoot, string title = "HSF wasn't installed")
    {
        if (xamlRoot == null) return;
        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
            CloseButtonText = "OK",
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        });
    }

    private async Task ShowHsfSettingsAsync(GameCardViewModel card, bool isInstalled, bool rhiInstalled, HsfService svc)
    {
        var installPath = card.InstallPath ?? "";
        var cog = new CogDialog("HSF", _window.Content.XamlRoot);
        cog.AddIntro("HSF adds DLSS Frame Generation to the game. Open ReShade's overlay (Home key) and go to the HSF tab: "
            + "turn FRAME GEN on and pick the multiplier (F7 cycles it in game). Set an FPS cap there for smoother frames.");

        cog.AddSection("This game");
        if (!isInstalled)
            cog.AddNote("HSF isn't installed in this game.");
        else
        {
            cog.AddNote(rhiInstalled
                ? $"Installed: HSF {HsfService.ReadMarker(installPath).Version ?? "(unknown version)"}. RHI updates it when a new release comes out."
                : "Installed by hand. Reinstall it with RHI to get updates.");
            cog.AddNote($"DirectX 12 route: {HsfService.RouteText(installPath)}.");
        }
        if (MainViewModel.HsfReShadeVersionNote(card) is { } rsNote) cog.AddWarning(rsNote);
        if (MainViewModel.HsfBlockReason(card) is { } reason) cog.AddWarning(reason);

        cog.AddSection("Motion");
        cog.AddIntro("DirectX 12 games that run DLSS give HSF their own motion data. Every other game needs optical flow from a ReShade effect: "
            + "RHI installs iMMERSE LaunchPad with HSF. LumeniteFX Kernel also works; pick it in HSF's FLOW FROM row if it's in your shaders.");
        if (rhiInstalled)
        {
            bool lpOn = HsfService.ReadMarker(installPath).LaunchpadOn;
            cog.AddCombo("iMMERSE LaunchPad",
                "Off only if this game always gives HSF its own DLSS data, or you use LumeniteFX instead. Without a flow source, DirectX 9 and 11 games say \"no motion vectors\".",
                new[] { "On", "Off" }, lpOn ? 0 : 1,
                i =>
                {
                    bool on = i == 0;
                    cog.SetStatus(on ? "Adding LaunchPad..." : "Removing LaunchPad...");
                    _ = Task.Run(async () =>
                    {
                        bool ok = await _window.ViewModel.SetHsfLaunchpadAsync(card, on);
                        _window.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (ok) cog.SetSuccess(on ? "LaunchPad is in the game's shaders." : "LaunchPad removed from the game's shaders.");
                            else cog.SetError("Couldn't change LaunchPad. Check the log.");
                        });
                    });
                });
        }

        cog.AddSection("Good to know");
        var gpu = GpuInfoService.Primary(await GpuInfoService.GetNvidiaGpusAsync());
        cog.AddNote(gpu == null
            ? "Graphics card: no NVIDIA card found. HSF needs an NVIDIA RTX card."
            : gpu.Family switch
            {
                GpuFamily.Rtx50 or GpuFamily.Newer => $"Graphics card: {gpu.Name}. It does up to 6x by itself; Universal MFG Unlocker isn't needed.",
                GpuFamily.Rtx40 => $"Graphics card: {gpu.Name}. It does 2x by itself; Universal MFG Unlocker (in the same section) takes it up to 6x.",
                GpuFamily.Rtx20 or GpuFamily.Rtx30 => $"Graphics card: {gpu.Name}. It needs Universal MFG Unlocker (in the same section) for frame generation.",
                _ => $"Graphics card: {gpu.Name}. It isn't an RTX card, so it can't run DLSS frame generation.",
            });
        cog.AddNote("DirectX 9 and 11 games: play in windowed or borderless mode; HSF shows the generated frames in a window of its own over the game.");
        cog.AddNote("Turn off the game's own frame generation, and don't use HSF together with OptiScaler's frame generation.");
        cog.AddWarning("Never use HSF in a game with online anti-cheat.");

        var addonDir = HsfService.AddonDir(installPath);
        cog.AddButtons(
            cog.MakeButton("Open GitHub page", CogButtonKind.Secondary,
                async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri(HsfService.ReleasesPage)); }),
            cog.MakeOpenFileButton("Open HSF.ini", isInstalled ? Path.Combine(addonDir, HsfService.IniName) : null),
            cog.MakeOpenFileButton("Open helper log", isInstalled ? Path.Combine(addonDir, HsfService.StreamlineFolder, "hsf_dlssg.log") : null));
        await cog.ShowAsync();
    }
}
