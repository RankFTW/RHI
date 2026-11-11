// SettingsHandler.DisplayModes.cs -- Settings › Display › "Display modes" card (built in C#, XAML constraint):
// the on/off switch for the game page mode switch, the modes (add / rename / reorder / delete, default values per
// mode), the default list of what games swap, and "Switch all games". Appended to the Display section the first
// time Settings opens; rebuilt when modes change. Data: MainViewModel.DisplayModes.cs.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private StackPanel? _dmBody;
    private TextBlock? _dmStatus;
    private bool _dmCardFailed;
    private bool _dmBusy;

    private void EnsureDisplayModesCard()
    {
        if (_dmBody != null) { RefreshDisplayModesCard(); return; }
        if (_dmCardFailed) return;
        try
        {
            StackPanel? section = null;
            foreach (var child in _window.SettingsCardsPanel.Children)
                if (child is StackPanel sp && sp.Tag is string t && t == "Section:Display") { section = sp; break; }
            if (section == null)
            {
                _dmCardFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureDisplayModesCard] Display section not found — card not added");
                return;
            }

            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var outer = new StackPanel { Spacing = 10 };
            card.Child = outer;
            var head = new StackPanel { Spacing = 3 };
            head.Children.Add(new TextBlock { Text = "Display modes", Style = FindStyle("SettingsCardTitleStyle") });
            head.Children.Add(new TextBlock
            {
                Text = "Save sets of the settings RHI applies to a game and switch between them from its page, e.g. one for your monitor and one for your TV, "
                     + "or for trying different setups. Modes don't change your display themselves. Each game swaps only what you choose "
                     + "(peak brightness, frame rate caps, keys, DLSS settings, HDR and resolution on launch, ...) and remembers its own values per mode.",
                Style = FindStyle("SettingsCardDescStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            outer.Children.Add(head);
            outer.Children.Add(new TextBlock
            {
                Text = "A game set to a mode keeps its own values for what it swaps, so settings elsewhere in RHI that write to every game "
                     + "(peak brightness, frame rate cap, keys) skip it. Click the game's active mode again (or pick Off) to turn its mode off: "
                     + "it goes back to your global settings. New games start with their mode off.",
                Style = FindStyle("SettingsNoteStyle"),
                Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush),
                TextWrapping = TextWrapping.Wrap,
            });

            _dmBody = new StackPanel { Spacing = 10 };
            outer.Children.Add(_dmBody);
            _dmStatus = new TextBlock { Style = FindStyle("SettingsNoteStyle"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            outer.Children.Add(_dmStatus);

            section.Children.Add(card);
            ViewModel.DisplayModesChanged += () => _window.DispatcherQueue.TryEnqueue(() =>
            {
                if (_window.SettingsPanel.Visibility == Visibility.Visible) RefreshDisplayModesCard();
            });
            RefreshDisplayModesCard();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            _dmCardFailed = true;
            _dmBody = null;
            CrashReporter.Log($"[SettingsHandler.EnsureDisplayModesCard] Failed — {ex.Message}");
        }
    }

    private void RefreshDisplayModesCard()
    {
        if (_dmBody == null) return;
        _dmBody.Children.Clear();
        var vm = ViewModel;
        var store = vm.DisplayModeStore;
        var modes = store.Modes;

        // ── On / off ──
        var onCombo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
        onCombo.Items.Add(new ComboBoxItem { Content = "Off" });
        onCombo.Items.Add(new ComboBoxItem { Content = "On" });
        onCombo.SelectedIndex = vm.DisplayModesOn ? 1 : 0;
        ToolTipService.SetToolTip(onCombo, "On shows the mode switch on game pages (Classic+, Tabs and Spotlight), beside the HDR and resolution buttons, "
            + "and in the game menu. Off hides it; games keep the settings they have now.");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(onCombo, "Display modes");
        onCombo.SelectionChanged += (s, e) =>
        {
            if (onCombo.SelectedIndex < 0) return;
            vm.SetDisplayModesEnabled(onCombo.SelectedIndex == 1);
        };
        _dmBody.Children.Add(SettingRow("Show on game pages", onCombo));

        // ── Modes ──
        _dmBody.Children.Add(new TextBlock { Text = "Modes", Style = FindStyle("SettingsHeadStyle") });
        for (int i = 0; i < modes.Count; i++)
        {
            var m = modes[i];
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int setCount = DisplayModeItems.All.Count(it => store.ModeDefault(m.Id, it.Key) != null);
            var label = new TextBlock
            {
                Text = setCount == 0 ? $"{m.Name}  (no defaults set)" : $"{m.Name}  ({setCount} default{(setCount == 1 ? "" : "s")})",
                Style = FindStyle("SettingsLabelStyle"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            row.Children.Add(label);
            var buttons = new Controls.WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
            var id = m.Id;
            var name = m.Name;
            buttons.Children.Add(DmButton("Defaults...", DmKind.Normal, $"The values games use in {name} mode until they have their own.",
                async () => await ShowModeDefaultsAsync(id)));
            buttons.Children.Add(DmButton("Rename", DmKind.Normal, $"Rename {name}", async () =>
            {
                var newName = await PromptModeNameAsync("Rename mode", $"New name for {name}:", "Rename", name, id);
                if (newName != null && vm.RenameDisplayMode(id, newName)) SetDmStatus($"Renamed {name} to {newName}.", false);
            }));
            // Both always shown (greyed at the ends) so the rows line up
            var up = DmButton("Move up", DmKind.Normal, "Move this mode left on game pages", async () => { vm.MoveDisplayMode(id, -1); await Task.CompletedTask; });
            up.IsEnabled = i > 0;
            buttons.Children.Add(up);
            var down = DmButton("Move down", DmKind.Normal, "Move this mode right on game pages", async () => { vm.MoveDisplayMode(id, 1); await Task.CompletedTask; });
            down.IsEnabled = i < modes.Count - 1;
            buttons.Children.Add(down);
            if (modes.Count > 1)
                buttons.Children.Add(DmButton("Delete", DmKind.Danger, $"Delete {name}. Games in it have their mode turned off (back to your global settings).", async () =>
                {
                    if (!await ConfirmAsync("Delete mode",
                            $"Delete {name}? Games in {name} mode have their mode turned off: they go back to your global settings " +
                            $"(running games are just set to Off), and the values games remembered for {name} are forgotten.",
                            "Delete")) return;
                    var (deleted, turnedOff) = await vm.DeleteDisplayModeAsync(id);
                    if (deleted)
                        SetDmStatus(turnedOff > 0 ? $"Deleted {name}. {turnedOff} game{(turnedOff == 1 ? "" : "s")} turned off." : $"Deleted {name}.", false);
                }));
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            _dmBody.Children.Add(row);
        }
        var addRow = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        var addBtn = DmButton("Add mode", DmKind.Primary, $"Add another mode (up to {DisplayModeStore.MaxModes}), e.g. Handheld or Projector.", async () =>
        {
            var newName = await PromptModeNameAsync("Add mode", "Name for the new mode:", "Add", "", null);
            if (newName == null) return;
            var newId = vm.AddDisplayMode(newName);
            if (newId != null) SetDmStatus($"Added {newName}. Set its defaults with Defaults..., or give games their own values from their page.", false);
        });
        addBtn.IsEnabled = modes.Count < DisplayModeStore.MaxModes;
        addRow.Children.Add(addBtn);
        _dmBody.Children.Add(addRow);

        // ── Default list ──
        _dmBody.Children.Add(new TextBlock { Text = "What games swap", Style = FindStyle("SettingsHeadStyle") });
        var defaults = store.DefaultItems.Select(DisplayModeItems.Get).Where(i => i != null).Select(i => i!.Label).ToList();
        var listBtn = DmButton("Choose...", DmKind.Normal, "Pick what games swap unless you choose for a game on its page (the ⚙ beside the mode switch).",
            async () => await ShowDefaultItemsAsync());
        var listRow = new Grid { ColumnSpacing = 10 };
        listRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        listRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        listRow.Children.Add(new TextBlock
        {
            Text = defaults.Count == 0 ? "Nothing (choose for each game on its page)" : string.Join(", ", defaults),
            Style = FindStyle("SettingsLabelStyle"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Grid.SetColumn(listBtn, 1);
        listRow.Children.Add(listBtn);
        _dmBody.Children.Add(listRow);
        _dmBody.Children.Add(new TextBlock
        {
            Text = "The default for every game. A game can have its own list (the ⚙ beside its mode switch).",
            Style = FindStyle("SettingsHelpStyle"),
            TextWrapping = TextWrapping.Wrap,
        });

        // ── Switch all ──
        _dmBody.Children.Add(new TextBlock { Text = "Switch all games", Style = FindStyle("SettingsHeadStyle") });
        var allCombo = new ComboBox { Style = FindStyle("SettingsComboStyle"), MinWidth = 140 };
        foreach (var m in modes) allCombo.Items.Add(new ComboBoxItem { Content = m.Name });
        allCombo.Items.Add(new ComboBoxItem { Content = "Off" }); // last: turns every game's mode off
        allCombo.SelectedIndex = 0;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(allCombo, "Mode to switch all games to");
        var allBtn = DmButton("Switch", DmKind.Primary, "Switch every game to this mode, e.g. for an evening on the TV, or turn every game's mode off. Running and hidden games are skipped.",
            async () =>
            {
                int i = allCombo.SelectedIndex;
                if (i < 0 || i > modes.Count) return;
                if (i == modes.Count) await SwitchAllAsync(null, "Off");
                else await SwitchAllAsync(modes[i].Id, modes[i].Name);
            });
        allBtn.IsEnabled = vm.DisplayModesOn;
        var allGrid = new Grid { ColumnSpacing = 8 };
        allGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        allGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        allGrid.Children.Add(allCombo);
        Grid.SetColumn(allBtn, 1);
        allGrid.Children.Add(allBtn);
        _dmBody.Children.Add(SettingRow("Switch every game to", allGrid, maxWidth: 300));
        _dmBody.Children.Add(new TextBlock
        {
            Text = vm.DisplayModesOn
                ? "Writes each game's values for that mode (Off: your global settings). Running and hidden games are skipped."
                : "Turn on Show on game pages first.",
            Style = FindStyle("SettingsHelpStyle"),
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private Grid SettingRow(string label, FrameworkElement control, double maxWidth = 210)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = maxWidth });
        row.Children.Add(new TextBlock { Text = label, Style = FindStyle("SettingsLabelStyle"), VerticalAlignment = VerticalAlignment.Center });
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private enum DmKind { Normal, Primary, Danger }

    private Button DmButton(string text, DmKind kind, string tooltip, Func<Task> onClick)
    {
        var style = kind switch
        {
            DmKind.Primary => "SettingsPrimaryButtonStyle",
            DmKind.Danger => "SettingsDangerButtonStyle",
            _ => "SettingsButtonStyle",
        };
        var btn = new Button { Content = text, Style = FindStyle(style) };
        var key = kind switch
        {
            DmKind.Primary => "SettingsButtonBlueBrush",
            DmKind.Danger => ResourceKeys.AccentRedBrush,
            _ => ResourceKeys.SettingsLabelBrush,
        };
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is Brush brush)
        {
            btn.Foreground = brush;
            // Keep the text colour on hover / press (WinUI hover colour sticks otherwise)
            btn.Resources["ButtonForegroundPointerOver"] = brush;
            btn.Resources["ButtonForegroundPressed"] = brush;
        }
        ToolTipService.SetToolTip(btn, tooltip);
        btn.Click += async (s, e) =>
        {
            if (_dmBusy) return;
            _dmBusy = true;
            try { await onClick(); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                CrashReporter.Log($"[SettingsHandler.DisplayModes] '{text}' failed — {ex.Message}");
                SetDmStatus($"{text} failed: {ex.Message}", true);
            }
            finally { _dmBusy = false; }
        };
        return btn;
    }

    private void SetDmStatus(string text, bool error)
    {
        if (_dmStatus == null) return;
        _dmStatus.Text = text;
        _dmStatus.Foreground = UIFactory.Brush(error ? ResourceKeys.AccentRedBrush : ResourceKeys.TextSecondaryBrush);
        _dmStatus.Visibility = Visibility.Visible;
    }

    // ── Dialogs ───────────────────────────────────────────────────────────────

    /// <summary>A mode's default values: one row per item, grouped like the per-game list.</summary>
    private async Task ShowModeDefaultsAsync(string modeId)
    {
        var vm = ViewModel;
        var store = vm.DisplayModeStore;
        var name = store.NameOf(modeId);
        if (name == null) return;
        var cog = new CogDialog($"{name} Mode", _window.Content.XamlRoot);
        cog.AddIntro($"Values games use in {name} mode until they have their own. A game gets its own value when you switch it away from {name} " +
                     "(what it has then is kept) or when you set one on its page. Empty = not set: the game uses its normal value " +
                     "(your global setting, or what it had before display modes; left as it is when there's neither).");
        foreach (var group in DisplayModeItems.Groups)
        {
            cog.AddSection(group);
            foreach (var item in DisplayModeItems.All.Where(i => i.Group == group))
            {
                var key = item.Key;
                var editor = DisplayModeEditor.Build(item, store.ModeDefault(modeId, key), "Not set",
                    v => { vm.SetDisplayModeDefault(modeId, key, v); return Task.FromResult<string?>(null); },
                    (text, error) => { if (error) cog.SetError(text); else cog.SetSuccess(text); });
                cog.AddRow(item.Label, editor, item.Help);
            }
        }
        await cog.ShowAsync();
        RefreshDisplayModesCard();
    }

    /// <summary>The default list of what games swap (games without a list of their own).</summary>
    private async Task ShowDefaultItemsAsync()
    {
        var vm = ViewModel;
        var store = vm.DisplayModeStore;
        var cog = new CogDialog("Display Modes", _window.Content.XamlRoot);
        cog.AddIntro("What games swap when you switch modes, unless a game has its own list (the ⚙ beside its mode switch). " +
                     "Items a game can't use (e.g. ReLimiter isn't installed) are skipped for that game.");
        var checks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        var current = store.DefaultItems;
        foreach (var group in DisplayModeItems.Groups)
        {
            cog.AddSection(group);
            var grid = new Controls.AutoFitGrid { MinColumnWidth = 270 };
            foreach (var item in DisplayModeItems.All.Where(i => i.Group == group))
            {
                var cb = new CheckBox { Content = item.Label, FontSize = 12, MinWidth = 0, IsChecked = current.Contains(item.Key, StringComparer.OrdinalIgnoreCase) };
                ToolTipService.SetToolTip(cb, item.Help);
                cb.Checked += (_, _) => Save();
                cb.Unchecked += (_, _) => Save();
                checks[item.Key] = cb;
                grid.Children.Add(cb);
            }
            cog.AddContent(grid);
        }
        void Save() => vm.SetDisplayModeDefaultItems(checks.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList());
        await cog.ShowAsync();
        RefreshDisplayModesCard();
    }

    /// <summary>Switches every game to a mode, or with a null <paramref name="modeId"/> turns every game's mode off.</summary>
    private async Task SwitchAllAsync(string? modeId, string modeName)
    {
        var vm = ViewModel;
        bool off = modeId == null;
        if (!await ConfirmAsync(off ? "Turn off all games" : "Switch all games",
                off
                    ? "Turn every game's display mode off? Each game goes back to your global settings (and what it had before modes, " +
                      "for settings without a global value). What it has now is kept for its current mode. Running and hidden games are skipped."
                    : $"Switch every game to {modeName} mode? Each game gets its values for {modeName} (what it has now is kept for its current mode). " +
                      "Running and hidden games are skipped.",
                off ? "Turn off" : "Switch")) return;

        var text = new TextBlock { Text = "Starting...", TextWrapping = TextWrapping.Wrap };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 6 };
        var dialog = new ContentDialog
        {
            Title = off ? "Turning display modes off" : $"Switching to {modeName}",
            Content = new StackPanel { Spacing = 12, Width = 380, Children = { text, bar } },
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        (int Switched, int Running) result;
        await using (var session = await DialogService.ShowProgressAsync(dialog))
        {
            var progress = new Progress<(string msg, double pct)>(p => { text.Text = p.msg; bar.Value = p.pct; });
            result = await vm.SwitchAllDisplayModesAsync(modeId, progress, _window.LifetimeToken);
        }
        var msg = off
            ? $"Turned off {result.Switched} game{(result.Switched == 1 ? "" : "s")}."
            : $"Switched {result.Switched} game{(result.Switched == 1 ? "" : "s")} to {modeName}.";
        if (result.Running > 0) msg += $" {result.Running} running game{(result.Running == 1 ? " was" : "s were")} skipped.";
        SetDmStatus(msg, false);
        _window.ShowStatusBarMessage(msg, TimeSpan.FromSeconds(8));
    }

    private async Task<bool> ConfirmAsync(string title, string body, string primary)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        return await DialogService.ShowSafeAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>Asks for a mode name, checked as it's typed. Null when cancelled.</summary>
    private async Task<string?> PromptModeNameAsync(string title, string prompt, string primary, string initial, string? renamingId)
    {
        var store = ViewModel.DisplayModeStore;
        var box = new TextBox { Text = initial, MaxLength = DisplayModeStore.MaxNameLength, Width = 340, PlaceholderText = "e.g. Handheld" };
        var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = TabsTheme.B("#FF9AA8"), Visibility = Visibility.Collapsed };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        string? Check() => store.ValidateName(box.Text, renamingId);
        void ShowCheck(bool showEmpty)
        {
            var problem = Check();
            dialog.IsPrimaryButtonEnabled = problem == null;
            error.Text = problem ?? "";
            error.Visibility = problem != null && (showEmpty || !string.IsNullOrWhiteSpace(box.Text)) ? Visibility.Visible : Visibility.Collapsed;
        }
        box.TextChanged += (s, e) => ShowCheck(false);
        dialog.PrimaryButtonClick += (s, e) => { if (Check() != null) { e.Cancel = true; ShowCheck(true); } };
        box.Loaded += (s, e) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        ShowCheck(false);
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return null;
        return box.Text.Trim();
    }
}
