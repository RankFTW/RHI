// DetailPanelBuilder.Experimental.Urti.cs — Experimental › Ray Tracing row for URTI (Skynizz/URTI): ray-traced
// reflections for DirectX 12 games, as a ReShade add-on. Per game. Install / remove go through MainViewModel.Urti.cs
// (the effects through the shader sync, the "URTI" effect ticked in the game's preset), files and staging through
// UrtiService. URTI doesn't draw while HSF is in the same game (tested Nov 2026, Red Dead Redemption 2): warned both
// ways, and installing asks first (Install anyway / Cancel), like ReLimiter v4's warnings.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>The URTI / HSF clash, as a tooltip line ("Game:" lines show under the name in Tabs / Spotlight).</summary>
    internal const string UrtiHsfClashLine =
        "Game: URTI doesn't draw its reflections while HSF is installed in the same game. Remove HSF to use URTI.";

    /// <summary>The URTI / HSF clash, for dialogs and ⚙ warnings.</summary>
    internal const string UrtiHsfClashText =
        "URTI and HSF are both in this game. URTI doesn't draw its reflections while HSF is installed in the same game: remove HSF to use URTI.";

    private void BuildUrtiRow(GameCardViewModel card, StackPanel body)
    {
        var vm          = _window.ViewModel;
        var installPath = card.InstallPath ?? "";
        bool isInstalled = UrtiService.IsInstalledIn(installPath);
        bool isOurs      = isInstalled && UrtiService.IsRhiInstalled(installPath);
        bool hsfHere     = !string.IsNullOrEmpty(installPath) && HsfService.IsInstalledIn(installPath, card.Is32Bit);
        bool pulled      = FeatureFlags.IsExperimentalHidden(FeatureFlags.ExperimentalUrti);
        // Taken out by the manifest: only shown where it's installed, and only to be removed
        var block = pulled
            ? ("Not offered right now", "URTI has been taken out of RHI for now. You can still remove it from this game.")
            : isInstalled ? null : vm.UrtiBlock(card);

        string statusText  = !isInstalled ? "Ready"
                           : !isOurs ? "Manual"
                           : UrtiService.InstalledVersion(installPath) is { } v ? "v" + v : "Installed";
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
            Text = "URTI",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tip = "URTI by Skynizz adds ray-traced reflections to DirectX 12 games that don't have them, as a ReShade add-on: "
            + "puddles, wet streets, windows and polished floors. Only tested in Red Dead Redemption 2 so far.\n"
            + "GPU: DirectX 12 ray tracing (DXR 1.1): NVIDIA RTX 20 or newer, AMD RX 6000 or newer.\n"
            + "Game: single player only; never use it online.\n"
            + (hsfHere ? UrtiHsfClashLine + "\n" : "")
            + "API: DirectX 12, 64-bit, with ReShade 6.8 or newer (with add-on support).";
        if (isInstalled && !isOurs) tip += "\n\nURTI is in this game's add-on folder, but RHI didn't install it. Install replaces it with the latest release (the file there is kept as a backup).";
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
            statusBlock.PointerPressed += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(UrtiService.ReleasesUrl));
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
        ToolTipService.SetToolTip(infoBtn, "Open URTI's GitHub page");
        infoBtn.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(UrtiService.RepoUrl));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        bool btnEnabled = block == null;
        var installBtn = new Button
        {
            Content = !btnEnabled ? "⚠  " + block!.Value.Item1
                    : isOurs ? "↺  Reinstall URTI" : "⬇  Install URTI",
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
            ToolTipService.SetToolTip(installBtn, "Install URTI in this game: the add-on and urti.ini next to ReShade, its effects in reshade-shaders, "
                + "and the URTI effect turned on in the game's ReShade preset. F7 turns it on and off in the game."
                + (hsfHere ? "\n\n" + UrtiHsfClashText : ""));

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
                if (HsfService.IsInstalledIn(installPath, card.Is32Bit) && !await ConfirmUrtiWithHsfAsync(card, xamlRoot)) return;
                installBtn.IsEnabled = false;
                installBtn.Content = "Downloading...";
                var error = await vm.InstallUrtiAsync(card);
                if (error != null)
                {
                    installBtn.Content = "Install failed";
                    installBtn.IsEnabled = true;
                    ToolTipService.SetToolTip(installBtn, error);
                    await DialogService.ShowSafeAsync(new ContentDialog
                    {
                        Title = "URTI wasn't installed",
                        Content = new TextBlock { Text = error, TextWrapping = TextWrapping.Wrap },
                        CloseButtonText = "OK",
                        XamlRoot = xamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    });
                    return;
                }
                RequestExperimentalRebuild(card);
                RequestExtrasRebuild(card); // HSF's row warns about URTI
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or HttpRequestException or TaskCanceledException or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[BuildUrtiRow] Install failed — {ex.Message}");
                installBtn.Content = "Install failed";
                installBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        var cogBtn = CogDialog.MakeCogButton("URTI settings");
        cogBtn.Click += async (_, _) =>
        {
            try { await ShowUrtiSettingsAsync(card); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                          or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[BuildUrtiRow] Settings dialog failed — {ex.Message}");
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
        ToolTipService.SetToolTip(removeBtn, "Remove URTI from this game (the add-on, its effects, and urti.ini if RHI put it there)");
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
                await vm.UninstallUrtiAsync(card);
                RequestExperimentalRebuild(card);
                RequestExtrasRebuild(card); // HSF's row stops warning about URTI
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                CrashReporter.Log($"[BuildUrtiRow] Remove failed — {ex.Message}");
                removeBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    /// <summary>HSF is in the game: URTI won't draw while it is. Install anyway / Cancel (Cancel is the default). True = go ahead.</summary>
    private async Task<bool> ConfirmUrtiWithHsfAsync(GameCardViewModel card, XamlRoot? xamlRoot)
    {
        if (xamlRoot == null) return true;
        var dialog = new ContentDialog
        {
            Title = "HSF is installed in this game",
            Content = new TextBlock
            {
                Text = "URTI doesn't draw its reflections while HSF is installed in the same game. Remove HSF (Extras) to use URTI.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
            },
            PrimaryButtonText = "Install anyway",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        bool go = await DialogService.ShowSafeAsync(dialog) == ContentDialogResult.Primary;
        CrashReporter.Log($"[BuildUrtiRow] '{card.GameName}': HSF installed — {(go ? "installing URTI anyway" : "cancelled")}");
        return go;
    }

    private async Task ShowUrtiSettingsAsync(GameCardViewModel card)
    {
        var installPath = card.InstallPath ?? "";
        bool isInstalled = UrtiService.IsInstalledIn(installPath);
        var cog = new CogDialog("URTI", _window.Content.XamlRoot);
        cog.AddIntro("URTI adds ray-traced reflections to DirectX 12 games: puddles, wet streets, windows and polished floors. "
            + "It rebuilds the scene from the game's draws and casts real rays into it, so it costs GPU time.");
        cog.AddWarning("Single player only: URTI hooks the game's rendering. Never use it online.");
        if (!string.IsNullOrEmpty(installPath) && HsfService.IsInstalledIn(installPath, card.Is32Bit)) cog.AddWarning(UrtiHsfClashText);
        cog.AddNote("Experimental: only tested in Red Dead Redemption 2 so far. In other games it may show nothing, show wrong reflections or crash.");
        if (MainViewModel.UrtiReShadeVersionNote(card) is { } rsNote) cog.AddWarning(rsNote);

        cog.AddSection("Using it");
        cog.AddNote("RHI turns on the URTI effect (ReShade's Home tab) and ReShade's Generic Depth add-on, which URTI reads the game's depth through. "
            + "F7 turns URTI on and off in the game, F4 shows before / after full screen, F5 shows a split screen without and with URTI, F8 saves a measurement burst.");
        cog.AddNote("The urti.ini RHI installs is the author's Red Dead Redemption 2 profile, which the README says to start from in other games too. "
            + "RHI never replaces urti.ini once it's there: other settings (reflection time budget, glossy floors and more) are listed in URTI's README.");

        cog.AddSection("Files");
        var addonDir = UrtiService.AddonDir(installPath);
        cog.AddButtons(
            cog.MakeButton("Open GitHub page", CogButtonKind.Secondary,
                async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri(UrtiService.RepoUrl)); }),
            cog.MakeOpenFileButton("Open urti.ini", isInstalled ? UrtiService.IniPath(installPath) : null),
            cog.MakeOpenFileButton("Open log", isInstalled ? Path.Combine(addonDir, UrtiService.LogName) : null));
        await cog.ShowAsync();
    }
}
