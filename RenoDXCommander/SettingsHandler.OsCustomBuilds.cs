// SettingsHandler.OsCustomBuilds.cs -- "Custom OptiScaler builds" card at the end of Settings › Components:
// add any OptiScaler build from a download link (GitHub, Discord, anywhere), an archive, a folder or an
// OptiScaler.dll, then pick it per game in the OptiScaler ⚙ or as the Default version. Each build is a
// fixed copy RHI never updates (OsCustomBuildService). Built in C# (XAML constraint), inserted the first
// time Settings opens. Plan: docs/custom-optiscaler-builds-plan.md.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private StackPanel? _osCustomList;
    private TextBlock? _osCustomStatus;
    private TextBox? _osCustomLinkBox;
    private bool _osCustomBusy;
    private bool _osCustomCardFailed;

    private void EnsureOsCustomBuildsCard()
    {
        if (_osCustomList != null) { RefreshOsCustomBuildsCard(); return; }
        if (_osCustomCardFailed) return;
        try
        {
            StackPanel? section = null;
            foreach (var child in _window.SettingsCardsPanel.Children)
                if (child is StackPanel sp && sp.Tag is string t && t == "Section:Components") { section = sp; break; }
            if (section == null)
            {
                _osCustomCardFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureOsCustomBuildsCard] Components section not found — card not added");
                return;
            }

            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var body = new StackPanel { Spacing = 10 };
            card.Child = body;

            var head = new StackPanel { Spacing = 3 };
            head.Children.Add(new TextBlock { Text = "Custom OptiScaler builds", Style = FindStyle("SettingsCardTitleStyle") });
            head.Children.Add(new TextBlock
            {
                Text = "Add any OptiScaler build: a download link (GitHub, Discord or anywhere else), a zip, 7z or rar, a folder, "
                     + "or an OptiScaler.dll. Then pick it in a game's OptiScaler settings, or as the Default version above. "
                     + "RHI installs it like the other versions but never updates it: add the new one yourself when it changes. "
                     + "Only add builds from people you trust.",
                Style = FindStyle("SettingsCardDescStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(head);

            _osCustomList = new StackPanel { Spacing = 8 };
            body.Children.Add(_osCustomList);

            // Link row: [ paste a link ............ ] [Add link]
            var linkRow = new Grid { ColumnSpacing = 8 };
            linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            linkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _osCustomLinkBox = new TextBox
            {
                PlaceholderText = "Paste a download link (GitHub release file, Discord attachment, ...)",
                Style = FindStyle("SettingsTextBoxStyle"),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_osCustomLinkBox, "Custom OptiScaler download link");
            var addLink = MakeCustomFoldersButton("Add link", primary: true,
                "Downloads the build once and adds it. Use the file's direct link: for Discord, right-click the attachment › Copy Link.");
            addLink.Click += async (s, e) =>
            {
                var link = _osCustomLinkBox.Text.Trim();
                if (link.Length == 0) { SetOsCustomStatus("Paste a download link first.", error: true); return; }
                if (!OsCustomBuildService.IsUrl(link, out _)) { SetOsCustomStatus("That isn't a web link (it should start with https://).", error: true); return; }
                if (await AddOsCustomBuildAsync(link, replace: null)) _osCustomLinkBox.Text = "";
            };
            _osCustomLinkBox.KeyDown += (s, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter) return;
                e.Handled = true;
                // Buttons have no click helper: press it through its automation peer
                (Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(addLink)
                    as Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer)?.Invoke();
            };
            linkRow.Children.Add(_osCustomLinkBox);
            Grid.SetColumn(addLink, 1);
            linkRow.Children.Add(addLink);
            body.Children.Add(linkRow);

            var buttons = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            var addFile = MakeCustomFoldersButton("Add file", primary: false,
                "A zip, 7z or rar with an OptiScaler build in it, or an OptiScaler.dll on its own.");
            addFile.Click += async (s, e) =>
            {
                var file = await PickOsCustomFileAsync();
                if (file != null) await AddOsCustomBuildAsync(file, replace: null);
            };
            buttons.Children.Add(addFile);
            var addFolder = MakeCustomFoldersButton("Add folder", primary: false,
                "A folder with an unpacked OptiScaler build (OptiScaler.dll in it, or up to 3 folders down).");
            addFolder.Click += async (s, e) =>
            {
                var folder = await _window.PickFolderAsync();
                if (!string.IsNullOrEmpty(folder)) await AddOsCustomBuildAsync(folder, replace: null);
            };
            buttons.Children.Add(addFolder);
            body.Children.Add(buttons);

            _osCustomStatus = new TextBlock
            {
                Style = FindStyle("SettingsNoteStyle"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            body.Children.Add(_osCustomStatus);

            section.Children.Add(card);
            OsCustomBuildService.Changed += () => _window.DispatcherQueue.TryEnqueue(RefreshOsCustomBuildsCard);
            RefreshOsCustomBuildsCard();
        }
        catch (Exception ex)
        {
            _osCustomCardFailed = true;
            CrashReporter.Log($"[SettingsHandler.EnsureOsCustomBuildsCard] Failed — {ex.Message}");
        }
    }

    /// <summary>Installed games whose OptiScaler version is this build.</summary>
    private List<GameCardViewModel> GamesUsingOsBuild(OsCustomBuild build) =>
        ViewModel.AllCards
            .Where(c => c.IsOsInstalled && ViewModel.GetOsVariant(c.GameName, c.Source ?? "") == build.Id)
            .ToList();

    private void RefreshOsCustomBuildsCard()
    {
        if (_osCustomList == null) return;
        try
        {
            _osCustomList.Children.Clear();
            var builds = OsCustomBuildService.All;
            if (builds.Count == 0)
            {
                _osCustomList.Children.Add(new TextBlock { Text = "No custom builds yet.", Style = FindStyle("SettingsNoteStyle") });
                return;
            }
            foreach (var build in builds)
                _osCustomList.Children.Add(MakeOsCustomRow(build));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.RefreshOsCustomBuildsCard] {ex.Message}");
        }
    }

    private FrameworkElement MakeOsCustomRow(OsCustomBuild build)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = build.Name,
            Style = FindStyle("SettingsLabelStyle"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        int using_ = GamesUsingOsBuild(build).Count;
        var facts = new List<string>();
        if (!string.IsNullOrWhiteSpace(build.DllVersion)) facts.Add(build.DllVersion!);
        facts.Add(build.IsOs10 ? "OptiScaler 10 layout" : "flat layout");
        facts.Add("added " + build.AddedUtc.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture));
        if (using_ > 0) facts.Add(using_ == 1 ? "used by 1 game" : $"used by {using_} games");
        var detail = new TextBlock
        {
            Text = string.Join(" · ", facts),
            Style = FindStyle("SettingsHelpStyle"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTipService.SetToolTip(detail, $"From: {build.Origin}\nFolder: {build.Dir}"
            + (build.LooksLikeOptiScaler ? "" : "\nThis OptiScaler.dll doesn't identify itself as OptiScaler."));
        text.Children.Add(detail);
        row.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var nr = new CheckBox
        {
            Content = "DLSS NR DLL",
            IsChecked = build.NeedsNrDll,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(nr,
            "Also install NVIDIA's nvngx_dlssnr.dll with this build, and show the Neural Rendering settings in its OptiScaler ⚙. "
          + "Turn it on for Neural Rendering forks (like DLSS NR and F5). Ticked automatically when the build has the F5-style forwarder. "
          + "Applies to the next install.");
        nr.Click += (s, e) =>
        {
            if (!OsCustomBuildService.SetNeedsNrDll(build.Slug, nr.IsChecked == true))
                SetOsCustomStatus("Couldn't save that change.", error: true);
        };
        actions.Children.Add(nr);

        var replace = MakeCustomFoldersButton("Replace", primary: false,
            "Swap this build's files for a newer version (from the link box, a file or a folder). Games using it can be updated straight after.");
        var flyout = new MenuFlyout();
        var fromLink = new MenuFlyoutItem { Text = "From the link above" };
        fromLink.Click += async (s, e) =>
        {
            var link = _osCustomLinkBox?.Text.Trim() ?? "";
            if (!OsCustomBuildService.IsUrl(link, out _)) { SetOsCustomStatus("Paste the new download link in the box first.", error: true); return; }
            if (await AddOsCustomBuildAsync(link, replace: build) && _osCustomLinkBox != null) _osCustomLinkBox.Text = "";
        };
        var fromFile = new MenuFlyoutItem { Text = "From a file..." };
        fromFile.Click += async (s, e) =>
        {
            var file = await PickOsCustomFileAsync();
            if (file != null) await AddOsCustomBuildAsync(file, replace: build);
        };
        var fromFolder = new MenuFlyoutItem { Text = "From a folder..." };
        fromFolder.Click += async (s, e) =>
        {
            var folder = await _window.PickFolderAsync();
            if (!string.IsNullOrEmpty(folder)) await AddOsCustomBuildAsync(folder, replace: build);
        };
        flyout.Items.Add(fromLink);
        flyout.Items.Add(fromFile);
        flyout.Items.Add(fromFolder);
        replace.Flyout = flyout;
        actions.Children.Add(replace);

        var rename = MakeCustomFoldersButton("Rename", primary: false, "Change the name shown in the version lists.");
        rename.Click += async (s, e) =>
        {
            var name = await AskOsCustomNameAsync("Rename custom build", build.Name);
            if (name == null || name == build.Name) return;
            if (!OsCustomBuildService.Rename(build.Slug, name)) SetOsCustomStatus("Couldn't rename it.", error: true);
        };
        actions.Children.Add(rename);

        var remove = MakeCustomFoldersButton("Remove", primary: false, "Delete this build from RHI. Games that have it installed keep their files.");
        remove.Click += async (s, e) => await RemoveOsCustomBuildAsync(build);
        actions.Children.Add(remove);

        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private void SetOsCustomStatus(string text, bool error)
    {
        if (_osCustomStatus == null) return;
        _osCustomStatus.Text = text;
        _osCustomStatus.Foreground = UIFactory.Brush(error ? ResourceKeys.AccentRedBrush : ResourceKeys.TextSecondaryBrush);
        _osCustomStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task<string?> PickOsCustomFileAsync()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        return await Task.Run(() =>
        {
            var ofn = new NativeInterop.OpenFileName();
            ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwnd;
            ofn.filter = "OptiScaler builds (*.zip;*.7z;*.rar;*.dll)\0*.zip;*.7z;*.rar;*.dll\0All Files (*.*)\0*.*\0";
            ofn.file = new string(new char[260]);
            ofn.maxFile = ofn.file.Length;
            ofn.title = "Choose an OptiScaler build";
            ofn.flags = 0x00080000 | 0x00001000; // OFN_EXPLORER | OFN_FILEMUSTEXIST
            return NativeInterop.GetOpenFileName(ref ofn) ? ofn.file.TrimEnd('\0') : null;
        });
    }

    private Task<string?> AskOsCustomNameAsync(string title, string suggested) =>
        OsCustomBuildDialogs.AskNameAsync(_window.Content.XamlRoot, title, suggested);

    /// <summary>Adds (or, with <paramref name="replace"/>, replaces) a build. True when it worked.</summary>
    private async Task<bool> AddOsCustomBuildAsync(string source, OsCustomBuild? replace)
    {
        if (_osCustomBusy) { SetOsCustomStatus("Still working on the last one...", error: false); return false; }
        string name;
        if (replace != null) name = replace.Name;
        else
        {
            var asked = await AskOsCustomNameAsync("Name this OptiScaler build", OsCustomBuildService.SuggestName(source));
            if (asked == null) return false;
            name = asked;
        }

        _osCustomBusy = true;
        try
        {
            var progress = new Progress<string>(m => SetOsCustomStatus(m, error: false));
            SetOsCustomStatus(replace != null ? $"Replacing {replace.Name}..." : $"Adding {name}...", error: false);
            var result = await Task.Run(() => OsCustomBuildService.ImportAsync(source, name, replace?.Slug, progress, _window.LifetimeToken));
            if (result.Build == null)
            {
                SetOsCustomStatus(result.Error ?? "It couldn't be added.", error: true);
                return false;
            }
            var done = replace != null ? $"Replaced {result.Build.Name}." : $"Added {result.Build.Name}. Pick it in a game's OptiScaler settings.";
            SetOsCustomStatus(result.Note != null ? done + " " + result.Note : done, error: result.Note != null);
            if (replace != null) await OfferOsCustomRedeployAsync(result.Build);
            return true;
        }
        finally { _osCustomBusy = false; }
    }

    /// <summary>After a build is replaced: offer to put the new files into the games that use it.</summary>
    private async Task OfferOsCustomRedeployAsync(OsCustomBuild build)
    {
        var games = GamesUsingOsBuild(build).Where(c => !c.IsHidden && !string.IsNullOrEmpty(c.InstallPath)).ToList();
        if (games.Count == 0) return;
        var dialog = new ContentDialog
        {
            Title = "Update the games using it?",
            Content = new TextBlock
            {
                Text = (games.Count == 1 ? $"{games[0].GameName} has" : $"{games.Count} games have")
                     + $" {build.Name} installed. Put the new files in now? Your OptiScaler.ini settings are kept.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Update",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _window.Content.XamlRoot,
        };
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return;
        var os = App.Services.GetRequiredService<IOptiScalerService>();
        int ok = 0;
        foreach (var card in games)
        {
            SetOsCustomStatus($"Updating {card.GameName}...", error: false);
            try
            {
                await os.UpdateAsync(card, variantHint: build.Id);
                ok++;
            }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.OsCustomRedeploy] {card.GameName}: {ex.Message}"); }
        }
        ViewModel.SaveLibraryPublic();
        SetOsCustomStatus(ok == games.Count ? $"Updated {ok} game{(ok == 1 ? "" : "s")}." : $"Updated {ok} of {games.Count} games. See the log for the others.",
            error: ok != games.Count);
    }

    private async Task RemoveOsCustomBuildAsync(OsCustomBuild build)
    {
        var games = GamesUsingOsBuild(build);
        var dialog = new ContentDialog
        {
            Title = $"Remove {build.Name}?",
            Content = new TextBlock
            {
                Text = games.Count == 0
                    ? "Its files are deleted from RHI."
                    : $"{games.Count} game{(games.Count == 1 ? " has" : "s have")} it installed. They keep their files, but can't be updated or reinstalled with it. Pick another version in their OptiScaler settings.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _window.Content.XamlRoot,
        };
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return;
        // The Default version can't point at a build that's gone
        if (ViewModel.Settings.OsDefaultVariant == build.Id) ViewModel.SetOsDefaultVariant(OsVariants.Stable);
        if (!OsCustomBuildService.Remove(build.Slug)) SetOsCustomStatus("Couldn't delete its files (in use?). Try again.", error: true);
        else SetOsCustomStatus($"Removed {build.Name}.", error: false);
    }
}
