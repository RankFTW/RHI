// DetailPanelBuilder.Experimental.Urti.cs — Experimental › Ray Tracing row for URTI (Skynizz/URTI): ray-traced
// reflections for DirectX 12 games, as a ReShade add-on. Per game. Install / remove go through MainViewModel.Urti.cs
// (the effects through the shader sync, the "URTI" effect ticked in the game's preset), files and staging through
// UrtiService. URTI doesn't draw while HSF is in the same game (tested Nov 2026, Red Dead Redemption 2): warned both
// ways, and installing asks first (Install anyway / Cancel), like ReLimiter v4's warnings.

using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// One urti.ini setting in the ⚙. Names / Values = a dropdown, otherwise a text box checked by Check (returns the value
    /// to write, or null with the reason). Defaults are URTI's own (urti_addon.cpp ReadSettings, urti_live.inl
    /// LiveHotReload), shown when the key isn't in the file.
    /// </summary>
    private sealed record UrtiSetting(string Key, string Label, string Help, string UrtiDefault, bool AtStart,
        string[]? Names = null, string[]? Values = null, Func<string, (string? Value, string? Error)>? Check = null);

    private static readonly System.Globalization.CultureInfo UrtiInv = System.Globalization.CultureInfo.InvariantCulture;

    private static (string?, string?) UrtiInt(string text, int min, string what) =>
        int.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer, UrtiInv, out var n) && n >= min
            ? (n.ToString(UrtiInv), null) : (null, $"{what} must be a whole number, {min} or more.");

    private static (string?, string?) UrtiF0(string text) =>
        double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, UrtiInv, out var f) && f >= 0 && f <= 1
            ? (f.ToString("0.###", UrtiInv), null) : (null, "Reflection F0 must be a number from 0 to 1, such as 0.04.");

    /// <summary>"x0,y0,x1,y1" fractions of the screen, left / top smaller than right / bottom; empty = no zone (key removed).</summary>
    internal static (string?, string?) UrtiZone(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return ("", null);
        var parts = t.Split(',');
        var v = new double[4];
        bool ok = parts.Length == 4;
        for (int i = 0; ok && i < 4; i++)
            ok = double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, UrtiInv, out v[i]) && v[i] >= 0 && v[i] <= 1;
        if (!ok || v[0] >= v[2] || v[1] >= v[3])
            return (null, "UI zone must be four numbers from 0 to 1 (left, top, right, bottom), such as 0.012,0.69,0.16,0.97, or empty for none.");
        return (string.Join(",", v.Select(x => x.ToString("0.###", UrtiInv))), null);
    }

    private static readonly UrtiSetting[] UrtiSettings =
    {
        new("live", "Start on",
            "On: URTI's reflections start with the game. Off: they start off, and F7 turns them on.", "0", AtStart: true,
            Names: new[] { "Off", "On" }, Values: new[] { "0", "1" }),
        new("shadow_rays", "Shadow rays",
            "Rays per pixel in URTI's shadow pass, 1 to 4. More is smoother and costs more GPU time.", "2", AtStart: false,
            Names: new[] { "1", "2", "3", "4" }, Values: new[] { "1", "2", "3", "4" }),
        new("calibration_interval", "Calibration (frames)",
            "How often URTI measures the game's depth and camera again, in frames (it waits longer after a measurement fails). Minimum 30; URTI's default is 180.",
            "180", AtStart: true, Check: s => UrtiInt(s, 30, "Calibration")),
        new("capture_presents", "Capture length",
            "How many presented frames one geometry capture lasts (with frame generation, every other frame has no game draw). Minimum 1; URTI's default is 4.",
            "4", AtStart: true, Check: s => UrtiInt(s, 1, "Capture length")),
        new("reflect_f0", "Reflection F0",
            "How much a surface reflects when seen straight on (Fresnel F0), 0 to 1. 0.04 suits water, glass and polished floors; higher makes reflections stronger.",
            "0.04", AtStart: false, Check: UrtiF0),
        new("jitter", "Jitter correction",
            "Takes the game's sub-pixel jitter (DLSS / TAA) out of URTI's motion measurements. Off: none. Frame to frame: the change between frames. Full: the change and the absolute offset (URTI's default).",
            "2", AtStart: false, Names: new[] { "Off", "Frame to frame", "Full" }, Values: new[] { "0", "1", "2" }),
        new("ui_photo", "UI snapshot",
            "On: URTI keeps the game's interface clear of reflections using a copy of the image taken before the interface is drawn. Off: it doesn't (use UI zone to protect a fixed area instead).",
            "1", AtStart: false, Names: new[] { "Off", "On" }, Values: new[] { "0", "1" }),
        new("sun_measure", "Measure sunlight",
            "On: URTI measures how much of the game's light comes from the sun and adjusts by that. Off: it uses a fixed amount.",
            "1", AtStart: false, Names: new[] { "Off", "On" }, Values: new[] { "0", "1" }),
        new("ui_zone", "UI zone",
            "A screen area URTI never draws over, as fractions of the screen: left, top, right, bottom (Red Dead Redemption 2's minimap: 0.012,0.69,0.16,0.97). Empty: none.",
            "", AtStart: false, Check: UrtiZone),
    };

    /// <summary>Index of a dropdown setting's current value, read the way URTI reads it (a number; Off/On: not 0 = On; clamped to the list).</summary>
    internal static int UrtiChoiceIndex(string[] values, string value)
    {
        var t = value.Trim();
        int n = int.TryParse(t, System.Globalization.NumberStyles.Integer, UrtiInv, out var x) ? x
              : double.TryParse(t, System.Globalization.NumberStyles.Float, UrtiInv, out var d) ? (int)d : 0;
        var nums = values.Select(v => int.Parse(v, UrtiInv)).ToArray();
        if (nums.Length == 2 && nums[0] == 0 && nums[1] == 1) return n != 0 ? 1 : 0;
        n = Math.Clamp(n, nums[0], nums[^1]);
        int i = Array.IndexOf(nums, n);
        return i >= 0 ? i : 0;
    }

    private void AddUrtiSettings(CogDialog cog, string installPath, Action reopen)
    {
        var svc = App.Services.GetRequiredService<UrtiService>();
        cog.AddSection("Settings");
        cog.AddNote("Saved to the game's urti.ini as you change them. Start on, Calibration and Capture length apply the next time the game starts; "
            + "the others apply while it runs. Hover a setting for what it does.");

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        bool Save(UrtiSetting s, string? value)
        {
            // An empty UI zone removes the key: URTI then protects no zone
            var write = s.Key == "ui_zone" && value == "" ? null : value;
            if (svc.SetSetting(installPath, s.Key, write))
            {
                cog.SetSuccess($"{s.Label} saved{(s.AtStart ? ": applies the next time the game starts." : ".")}");
                return true;
            }
            cog.SetError("Couldn't write urti.ini. Check the folder's permissions, or try Admin Mode.");
            return false;
        }

        for (int i = 0; i < UrtiSettings.Length; i++)
        {
            var s = UrtiSettings[i];
            int row = i / 2, col = (i % 2) * 2;
            // The last setting (UI zone, a longer value) takes the rest of its row
            bool wide = i == UrtiSettings.Length - 1 && i % 2 == 0;
            if (col == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var help = s.Help + (s.AtStart ? " Applies the next time the game starts." : " Applies straight away while the game runs.");
            var current = UrtiService.GetSetting(installPath, s.Key) ?? s.UrtiDefault;

            var label = new TextBlock
            {
                Text = s.Label,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            };
            ToolTipService.SetToolTip(label, help);
            FrameworkElement control;
            if (s.Names != null)
            {
                var combo = CogDialog.MakeCombo(s.Names, UrtiChoiceIndex(s.Values!, current));
                int last = combo.SelectedIndex;
                bool reverting = false;
                combo.SelectionChanged += (_, _) =>
                {
                    if (reverting || combo.SelectedIndex < 0 || combo.SelectedIndex == last) return;
                    if (Save(s, s.Values![combo.SelectedIndex])) { last = combo.SelectedIndex; return; }
                    reverting = true;
                    combo.SelectedIndex = last;
                    reverting = false;
                };
                control = combo;
            }
            else
            {
                var box = CogDialog.MakeTextBox(current, s.Key == "ui_zone" ? "none" : s.UrtiDefault);
                string saved = current;
                // Saved on Enter and when the box loses focus; an invalid value is put back
                void Commit()
                {
                    if (box.Text.Trim() == saved.Trim()) return;
                    var (value, error) = s.Check!(box.Text);
                    if (value == null) { cog.SetError(error ?? "Not a valid value."); box.Text = saved; return; }
                    if (Save(s, value)) { saved = value; box.Text = value; }
                    else box.Text = saved;
                }
                box.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Commit(); e.Handled = true; } };
                box.LostFocus += (_, _) => Commit();
                control = box;
            }
            ToolTipService.SetToolTip(control, help);
            Grid.SetRow(label, row); Grid.SetColumn(label, col);
            Grid.SetRow(control, row); Grid.SetColumn(control, col + 1);
            if (wide) Grid.SetColumnSpan(control, 3);
            grid.Children.Add(label);
            grid.Children.Add(control);
        }
        cog.AddContent(grid);

        cog.AddButtons(cog.MakeButton("Use RHI's profile", CogButtonKind.Secondary, () =>
        {
            // The values in the urti.ini RHI installs (the author's Red Dead Redemption 2 profile); keys it doesn't set are removed
            bool ok = UrtiSettings.All(s => svc.SetSetting(installPath, s.Key, UrtiService.GetProfileValue(s.Key)));
            if (!ok) { cog.SetError("Couldn't write urti.ini. Check the folder's permissions, or try Admin Mode."); return; }
            reopen();
        }, "Put these settings back to the urti.ini RHI installs (the author's Red Dead Redemption 2 profile). Other lines in urti.ini are kept."));
    }

    internal async Task ShowUrtiSettingsAsync(GameCardViewModel card)
    {
        var installPath = card.InstallPath ?? "";
        bool isInstalled = UrtiService.IsInstalledIn(installPath);
        bool reopen = false;
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

        if (isInstalled && File.Exists(UrtiService.IniPath(installPath)))
            AddUrtiSettings(cog, installPath, () => { reopen = true; cog.Hide(); });
        else
        {
            cog.AddSection("Settings");
            cog.AddNote(isInstalled ? "This game has no urti.ini. Reinstall URTI to get one." : "Install URTI to change its settings.");
        }

        cog.AddSection("Files");
        cog.AddNote("RHI never replaces urti.ini once it's there. URTI's other settings (reflection time budget, glossy floors and more) are listed in its README.");
        var addonDir = UrtiService.AddonDir(installPath);
        cog.AddButtons(
            cog.MakeButton("Open GitHub page", CogButtonKind.Secondary,
                async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri(UrtiService.RepoUrl)); }),
            cog.MakeOpenFileButton("Open urti.ini", isInstalled ? UrtiService.IniPath(installPath) : null),
            cog.MakeOpenFileButton("Open log", isInstalled ? Path.Combine(addonDir, UrtiService.LogName) : null));
        await cog.ShowAsync();
        // "Use RHI's profile" closes the ⚙ so it opens again with the new values
        if (reopen) await ShowUrtiSettingsAsync(card);
    }
}
