// DetailPanelBuilder.Experimental.FrameWarp.cs — Experimental › Reprojection row for FrameWarp / XPAR
// (xPoiler/Reshade-Asynchronous-Reprojection): asynchronous camera reprojection, so the camera moves at the display's
// refresh rate whatever the game renders at. Install / remove / Latewarp go through MainViewModel.FrameWarp.cs, files
// and staging through FrameWarpService. Installing warns first (Install anyway / Cancel) about frame limiters, the NVIDIA
// frame rate limit, GPU scheduling and the VC++ runtime, and can turn the NVIDIA limit off for the game.
// FrameWarp's own settings are in its XPAR panel in ReShade: it puts them back to its defaults after every update, so
// the ⚙ only has what RHI owns.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void BuildFrameWarpRow(GameCardViewModel card, StackPanel body)
    {
        var vm          = _window.ViewModel;
        var installPath = card.InstallPath ?? "";
        bool isInstalled = FrameWarpService.IsInstalledIn(installPath);
        bool isOurs      = isInstalled && FrameWarpService.IsRhiInstalled(installPath);
        bool pulled      = FeatureFlags.IsExperimentalHidden(FeatureFlags.ExperimentalFrameWarp);
        var block = pulled
            ? ("Not offered right now", "FrameWarp has been taken out of RHI for now. You can still remove it from this game.")
            : isInstalled && isOurs ? null : vm.FrameWarpBlock(card);

        string statusText  = !isInstalled ? "Ready"
                           : !isOurs ? "Manual"
                           : FrameWarpService.InstalledVersion(installPath) is { } v ? v : "Installed";
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
            Text = "FrameWarp",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tip = "FrameWarp (XPAR) by xPoiler moves the camera at your display's refresh rate whatever frame rate the game renders at: "
            + "each refresh re-projects the game's newest frame to where the camera is now. Its XPAR frame generation shows moving characters at the refresh rate too.\n"
            + "Game: borderless window, no frame rate limiter (ReLimiter, Display Commander or the NVIDIA limit). Uses the game's DLSS or FSR when it has them. Single player only.\n"
            + "API: DirectX 12, DirectX 11 and Vulkan, 64-bit, with ReShade (with add-on support).";
        if (card.IsUlInstalled || card.IsDcInstalled)
            tip += $"\nGame: {(card.IsUlInstalled ? "ReLimiter" : "Display Commander")} is installed here. FrameWarp asks for frame rate limiters to be off.";
        if (isInstalled && !isOurs) tip += "\n\nFrameWarp is in this game's add-on folder, but RHI didn't install it. Install replaces it with the latest release (the file there is kept as a backup).";
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
            statusBlock.PointerPressed += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(FrameWarpService.ReleasesUrl));
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
        ToolTipService.SetToolTip(infoBtn, "Open FrameWarp's GitHub page");
        infoBtn.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(FrameWarpService.RepoUrl));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        bool btnEnabled = block == null;
        var installBtn = new Button
        {
            Content = !btnEnabled ? "⚠  " + block!.Value.Item1
                    : isOurs ? "↺  Reinstall FrameWarp" : "⬇  Install FrameWarp",
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
            ToolTipService.SetToolTip(label, $"{tip}\n\n{block.Value.Item2}");
        }
        else
            ToolTipService.SetToolTip(installBtn, "Install FrameWarp in this game: the add-on and its presenter next to ReShade, and XPAR.addonfx in reshade-shaders. "
                + "Run the game in a borderless window and open ReShade's XPAR tab to see it work.");

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
                var (lines, limit) = await Task.Run(() => vm.FrameWarpWarnings(card));
                if (lines.Count > 0)
                {
                    var (go, turnOff) = await ConfirmFrameWarpWarningsAsync(card, lines, limit, xamlRoot);
                    if (!go) { installBtn.IsEnabled = true; return; }
                    if (turnOff) await Task.Run(() => vm.TurnOffDriverFpsLimitForGame(card));
                }
                installBtn.Content = "Downloading...";
                var error = await vm.InstallFrameWarpAsync(card);
                if (error != null)
                {
                    installBtn.Content = "Install failed";
                    installBtn.IsEnabled = true;
                    ToolTipService.SetToolTip(installBtn, error);
                    await DialogService.ShowSafeAsync(new ContentDialog
                    {
                        Title = "FrameWarp wasn't installed",
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
                CrashReporter.Log($"[BuildFrameWarpRow] Install failed — {ex.Message}");
                installBtn.Content = "Install failed";
                installBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        var cogBtn = CogDialog.MakeCogButton("FrameWarp settings");
        cogBtn.Click += async (_, _) =>
        {
            try { await ShowFrameWarpSettingsAsync(card); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[BuildFrameWarpRow] Settings dialog failed — {ex.Message}");
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
        ToolTipService.SetToolTip(removeBtn, "Remove FrameWarp from this game (the add-on, its FrameWarp folder with the presenter and logs, XPAR.addonfx, and the Engine.ini line RHI added)");
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
                await vm.UninstallFrameWarpAsync(card);
                RequestExperimentalRebuild(card);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                CrashReporter.Log($"[BuildFrameWarpRow] Remove failed — {ex.Message}");
                removeBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    /// <summary>
    /// The warnings before installing (Install anyway / Cancel, Cancel default), with a tick to turn the NVIDIA frame rate
    /// limit off for this game when it's on (ticked by default). Returns (go ahead, turn the limit off).
    /// </summary>
    private async Task<(bool Go, bool TurnOffLimit)> ConfirmFrameWarpWarningsAsync(GameCardViewModel card, List<string> lines, uint limit, XamlRoot? xamlRoot)
    {
        if (xamlRoot == null) return (true, false);
        var content = new StackPanel { Spacing = 8, MaxWidth = 480 };
        foreach (var l in lines)
            content.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap });
        CheckBox? turnOff = null;
        if (limit > 0)
        {
            turnOff = new CheckBox { Content = "Turn off the NVIDIA frame rate limit for this game", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
            content.Children.Add(turnOff);
        }
        var dialog = new ContentDialog
        {
            Title = "Before installing FrameWarp",
            Content = content,
            PrimaryButtonText = "Install anyway",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        bool go = await DialogService.ShowSafeAsync(dialog) == ContentDialogResult.Primary;
        bool off = go && turnOff?.IsChecked == true;
        CrashReporter.Log($"[BuildFrameWarpRow] '{card.GameName}': {lines.Count} warning(s) — {(go ? "installing anyway" : "cancelled")}{(off ? ", NVIDIA limit off" : "")}");
        return (go, off);
    }

    internal async Task ShowFrameWarpSettingsAsync(GameCardViewModel card)
    {
        var vm = _window.ViewModel;
        var installPath = card.InstallPath ?? "";
        bool isInstalled = FrameWarpService.IsInstalledIn(installPath);
        bool isOurs = isInstalled && FrameWarpService.IsRhiInstalled(installPath);
        bool reopen = false;
        var cog = new CogDialog("FrameWarp", _window.Content.XamlRoot);
        cog.AddIntro("FrameWarp (XPAR) re-projects the game's newest frame to where the camera is now on every display refresh, "
            + "so turning and moving answer at your refresh rate even when the game renders fewer frames. It uses the game's DLSS or FSR when it has them, "
            + "and finds depth, motion and the camera itself when it doesn't.");
        cog.AddWarning("Single player only: FrameWarp hooks the game's rendering. Never use it online.");
        cog.AddNote("Experimental: FrameWarp's author tests it in about 18 games (Cyberpunk 2077, DOOM Eternal, Elden Ring offline, Red Dead Redemption 2 and more). Others may work.");

        // ── This game: what gets in FrameWarp's way ──────────────────────────
        cog.AddSection("This game");
        var (lines, limit) = await Task.Run(() => vm.FrameWarpWarnings(card));
        if (lines.Count == 0) cog.AddNote("No frame rate limiter found in the way, GPU scheduling is on and the VC++ runtime is installed.");
        foreach (var l in lines) cog.AddWarning(l);
        if (limit > 0)
            cog.AddButtons(cog.MakeButton("Turn off the NVIDIA limit for this game", CogButtonKind.Secondary, async () =>
            {
                bool ok = await Task.Run(() => vm.TurnOffDriverFpsLimitForGame(card));
                if (ok) { cog.SetSuccess("NVIDIA frame rate limit off for this game."); reopen = true; cog.Hide(); }
                else cog.SetError("Couldn't change the NVIDIA setting.");
            }, "Sets this game's NVIDIA frame rate limit to Off (the global limit stays as it is)."));
        cog.AddNote("Also check Background Application Max Frame Rate in the NVIDIA Control Panel (Manage 3D settings): FrameWarp's window never has focus, "
            + "so that cap holds it back. Turn it off for all programs, or for FrameWarpPresenter.exe and the game. FrameWarp's panel warns when it's capped.");

        // ── Unreal Engine: Engine.ini and launch options ─────────────────────
        var engineIni = isOurs && FrameWarpService.ReadMarker(installPath).TryGetValue("engine_ini", out var ei) && ei.Length > 0 ? ei
                      : FrameWarpService.FindEngineIni(installPath);
        if (engineIni != null)
        {
            cog.AddSection("Unreal Engine");
            cog.AddNote(isOurs
                ? $"RHI added {FrameWarpService.EngineCvar} to the game's Engine.ini ({engineIni}), so the game hands its DLSS data to FrameWarp. Removing FrameWarp takes it out again."
                : $"Installing adds {FrameWarpService.EngineCvar} to the game's Engine.ini ({engineIni}), so the game hands its DLSS data to FrameWarp.");
            bool hasSl = card.HasStreamline || (!string.IsNullOrEmpty(installPath) && await Task.Run(() => HsfService.FindGameStreamline(installPath)));
            if (hasSl)
            {
                cog.AddNote($"Unreal Engine games with DLSS through Streamline also need the launch options {FrameWarpService.UeLaunchOptions}. "
                    + "Installing FrameWarp adds them to RHI's launch arguments (used when you start the game from RHI) and removing it takes them out again. "
                    + "If you start the game from its store, add them there too (Steam: Properties > Launch Options).");
                cog.AddButtons(
                    cog.MakeButton("Add to RHI's launch arguments", CogButtonKind.Secondary, () =>
                    {
                        var args = vm.AddFrameWarpLaunchOptions(card);
                        cog.SetSuccess($"Launch arguments: {args}");
                    }),
                    cog.MakeButton("Copy launch options", CogButtonKind.Secondary, () =>
                    {
                        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        dp.SetText(FrameWarpService.UeLaunchOptions);
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                        cog.SetSuccess("Copied.");
                    }));
            }
        }

        // ── NVIDIA Latewarp ──────────────────────────────────────────────────
        cog.AddSection("NVIDIA Latewarp");
        bool nvidia = GpuInfoService.Cached is { Count: > 0 };
        if (!isOurs)
            cog.AddNote("Install FrameWarp with RHI to use NVIDIA Latewarp.");
        else if (!nvidia)
            cog.AddNote("NVIDIA Latewarp (Reflex 2 Frame Warp) needs an NVIDIA graphics card. FrameWarp's own warp engine works on any card.");
        else
        {
            bool lwOn = FrameWarpService.IsLatewarpInstalled(installPath);
            cog.AddNote("Optional: NVIDIA's Reflex 2 Frame Warp engine (nvngx_latewarp.dll 310.2.0.0, the build FrameWarp is tested with) as an alternative "
                + "to FrameWarp's own engine. RHI downloads it once and only uses it when its SHA256 matches NVIDIA's file. It isn't part of FrameWarp's release.");
            ComboBox? lwCombo = null;
            bool lwReverting = false;
            lwCombo = cog.AddCombo("NVIDIA Latewarp", "On: installs the DLL and shows FrameWarp's full settings, so you can pick NVIDIA Latewarp under Warp in the XPAR panel. "
                + "FrameWarp goes back to its own engine after each of its updates: pick Latewarp again then.",
                new[] { "Off", "On" }, lwOn ? 1 : 0, idx =>
                {
                    if (lwReverting) return true;
                    if (card.IsRunning) { cog.SetError("Close the game first: its files are in use."); return false; }
                    if (idx == 1) cog.SetSuccess("Downloading NVIDIA Latewarp...");
                    _ = Task.Run(async () =>
                    {
                        var err = await vm.SetFrameWarpLatewarpAsync(card, idx == 1);
                        _window.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (err == null)
                            {
                                cog.SetSuccess(idx == 1 ? "NVIDIA Latewarp installed. In the game: ReShade > XPAR > Warp > Warp engine > NVIDIA Latewarp." : "NVIDIA Latewarp removed.");
                                return;
                            }
                            // Put the dropdown back to what's really installed
                            lwReverting = true;
                            if (lwCombo != null) lwCombo.SelectedIndex = FrameWarpService.IsLatewarpInstalled(installPath) ? 1 : 0;
                            lwReverting = false;
                            cog.SetError(err);
                        });
                    });
                    return true;
                });
        }

        // ── Using it ─────────────────────────────────────────────────────────
        cog.AddSection("Using it");
        cog.AddNote("Run the game in a borderless window (or tick Force a borderless window in the XPAR panel and restart the game). "
            + "Open ReShade's overlay (Home) and go to the XPAR tab: the presenter starts with the game, and the panel has FrameWarp's own settings and checks.");
        cog.AddNote("FrameWarp keeps its settings in the game's ReShade.ini and puts them back to its defaults after each of its updates, which RHI installs for you.");
        cog.AddNote("While it shows its frames the screen runs at its maximum refresh rate (VRR can't follow its window). The game's DLSS Frame Generation and FSR 3.1 frame generation work with it.");

        cog.AddSection("Files");
        cog.AddButtons(
            cog.MakeButton("Open GitHub page", CogButtonKind.Secondary,
                async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri(FrameWarpService.RepoUrl)); }),
            cog.MakeButton("Open logs folder", CogButtonKind.Secondary, () =>
            {
                var logs = FrameWarpService.LogsDir(installPath);
                if (!Directory.Exists(logs)) { cog.SetError("No FrameWarp logs yet (they're written while the game runs)."); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{logs}\"") { UseShellExecute = true });
            }),
            cog.MakeOpenFileButton("Open ReShade.ini", isInstalled ? Path.Combine(installPath, "ReShade.ini") : null));
        await cog.ShowAsync();
        if (reopen) await ShowFrameWarpSettingsAsync(card);
    }
}
