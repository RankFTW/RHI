// SettingsHandler.TunePresets.cs -- Settings › "Tune presets" section (built in C#, XAML constraint), inserted before
// Components so it gets a jump button: the on/off switch for the Tune button on game pages, the preset list (edit,
// rename, move, hide, delete; the built-in RHI Tuned can be reset instead of deleted) and Add preset.
// Data: MainViewModel.Tune.cs. Editor: ShowTunePresetEditorAsync (a CogDialog).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private const string TuneSectionKey = "Tune";
    private StackPanel? _tuneBody;
    private TextBlock? _tuneStatus;
    private bool _tuneSectionFailed;

    private void EnsureTunePresetsSection()
    {
        if (_tuneBody != null) { RefreshTunePresetsCard(); return; }
        if (_tuneSectionFailed) return;
        try
        {
            var panel = _window.SettingsCardsPanel;
            int index = -1;
            for (int i = 0; i < panel.Children.Count; i++)
                if (panel.Children[i] is StackPanel sp && sp.Tag is string t && t == "Section:Components") { index = i; break; }
            if (index < 0)
            {
                _tuneSectionFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureTunePresetsSection] Components section not found — section not added");
                return;
            }

            var section = new StackPanel { Tag = "Section:" + TuneSectionKey, Spacing = 12 };
            var head = new StackPanel { Spacing = 3 };
            head.Children.Add(new TextBlock
            {
                Text = "Tune presets",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
            head.Children.Add(new TextBlock
            {
                Text = "One-click setups for the Tune button next to a game's name.",
                FontSize = 12,
                LineHeight = 17,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            });
            section.Children.Add(head);

            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var outer = new StackPanel { Spacing = 10 };
            card.Child = outer;
            var cardHead = new StackPanel { Spacing = 3 };
            cardHead.Children.Add(new TextBlock { Text = "Presets", Style = FindStyle("SettingsCardTitleStyle") });
            cardHead.Children.Add(new TextBlock
            {
                Text = "A preset is a list of things to install and settings to apply: ReShade, an HDR mod (RenoDX or Luma), a frame limiter "
                     + "(ReLimiter or Display Commander), DLSS and Streamline versions, DLSS presets and render scale, and NVIDIA driver settings. "
                     + "Tune shows what it will do in that game first and skips what doesn't apply. Click the game's Tuned button to undo it: "
                     + "only what tuning added or changed is removed or put back.",
                Style = FindStyle("SettingsCardDescStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            outer.Children.Add(cardHead);

            _tuneBody = new StackPanel { Spacing = 10 };
            outer.Children.Add(_tuneBody);
            _tuneStatus = new TextBlock { Style = FindStyle("SettingsNoteStyle"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            outer.Children.Add(_tuneStatus);

            section.Children.Add(card);
            panel.Children.Insert(index, section);
            ViewModel.TunePresetsChanged += () => _window.DispatcherQueue.TryEnqueue(() =>
            {
                if (_window.SettingsPanel.Visibility == Visibility.Visible) RefreshTunePresetsCard();
            });
            RefreshTunePresetsCard();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            _tuneSectionFailed = true;
            _tuneBody = null;
            CrashReporter.Log($"[SettingsHandler.EnsureTunePresetsSection] Failed — {ex.Message}");
        }
    }

    /// <summary>Opens the Tune presets section (from a game page's Tune menu).</summary>
    internal void JumpToTunePresets() => JumpToSection(TuneSectionKey, animate: true, saveAsLast: false);

    private void RefreshTunePresetsCard()
    {
        if (_tuneBody == null) return;
        _tuneBody.Children.Clear();
        var vm = ViewModel;
        var store = vm.TunePresetStore;
        var presets = store.Presets;

        var onCombo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
        onCombo.Items.Add(new ComboBoxItem { Content = "Off" });
        onCombo.Items.Add(new ComboBoxItem { Content = "On" });
        onCombo.SelectedIndex = vm.TunePresetsOn ? 1 : 0;
        ToolTipService.SetToolTip(onCombo, "On shows the Tune button next to the game's name (Classic+, Tabs and Spotlight). "
            + "Off hides it, including the Tuned button of games you tuned; turn it back on to remove their tuning.");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(onCombo, "Tune button on game pages");
        onCombo.SelectionChanged += (s, e) =>
        {
            if (onCombo.SelectedIndex < 0) return;
            vm.SetTunePresetsEnabled(onCombo.SelectedIndex == 1);
        };
        _tuneBody.Children.Add(SettingRow("Show on game pages", onCombo));

        _tuneBody.Children.Add(new TextBlock { Text = "Presets", Style = FindStyle("SettingsHeadStyle") });
        for (int i = 0; i < presets.Count; i++)
        {
            var p = presets[i];
            var id = p.Id;
            var name = p.Name;
            bool builtIn = TunePresetStore.IsBuiltIn(id);
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int steps = p.StepCount;
            int tuned = store.CountTunedWith(id);
            var text = $"{name}  ({(steps == 0 ? "nothing set" : $"{steps} step{(steps == 1 ? "" : "s")}")})";
            if (p.Hidden) text += "  hidden";
            if (tuned > 0) text += $"  · {tuned} game{(tuned == 1 ? "" : "s")} tuned";
            var label = new TextBlock
            {
                Text = text,
                Style = FindStyle("SettingsLabelStyle"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = p.Hidden ? 0.6 : 1.0,
            };
            ToolTipService.SetToolTip(label, TuneSummary(p));
            row.Children.Add(label);

            var buttons = new Controls.WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(DmButton("Edit...", DmKind.Normal, $"Choose what {name} installs and applies.",
                async () => await ShowTunePresetEditorAsync(id)));
            buttons.Children.Add(DmButton("Rename", DmKind.Normal, $"Rename {name}", async () =>
            {
                var newName = await PromptTuneNameAsync("Rename preset", $"New name for {name}:", "Rename", name, id);
                if (newName != null && vm.RenameTunePreset(id, newName)) SetTuneStatus($"Renamed {name} to {newName}.", false);
            }));
            var up = DmButton("Move up", DmKind.Normal, "Move this preset up in the game page's Tune menu",
                async () => { vm.MoveTunePreset(id, -1); await Task.CompletedTask; });
            up.IsEnabled = i > 0;
            buttons.Children.Add(up);
            var down = DmButton("Move down", DmKind.Normal, "Move this preset down in the game page's Tune menu",
                async () => { vm.MoveTunePreset(id, 1); await Task.CompletedTask; });
            down.IsEnabled = i < presets.Count - 1;
            buttons.Children.Add(down);
            buttons.Children.Add(DmButton(p.Hidden ? "Show" : "Hide", DmKind.Normal,
                p.Hidden ? $"Offer {name} on game pages again." : $"Don't offer {name} on game pages (it's kept here, and games tuned with it can still be untuned).",
                async () => { vm.SetTunePresetHidden(id, !p.Hidden); await Task.CompletedTask; }));
            if (builtIn)
                buttons.Children.Add(DmButton("Reset", DmKind.Danger, $"Put {name}'s settings back to what RHI ships: ReShade, RenoDX, ReLimiter, newest DLSS and Streamline, SR preset M, RR preset F, FG preset B.", async () =>
                {
                    if (!await ConfirmAsync("Reset preset", $"Put {name}'s settings back to RHI's defaults? Your changes to it are lost. Games already tuned with it aren't changed.", "Reset")) return;
                    if (vm.ResetBuiltInTunePreset()) SetTuneStatus($"{name} is back to RHI's defaults.", false);
                }));
            else
                buttons.Children.Add(DmButton("Delete", DmKind.Danger, $"Delete {name}. Games tuned with it stay tuned and can still be untuned.", async () =>
                {
                    var note = tuned > 0 ? $" {tuned} game{(tuned == 1 ? " is" : "s are")} tuned with it: they stay tuned, and their Tuned button still removes it." : "";
                    if (!await ConfirmAsync("Delete preset", $"Delete {name}?{note}", "Delete")) return;
                    if (vm.DeleteTunePreset(id)) SetTuneStatus($"Deleted {name}.", false);
                }));
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);
            _tuneBody.Children.Add(row);
        }

        var addRow = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        var addBtn = DmButton("Add preset", DmKind.Primary, $"Add a preset (up to {TunePresetStore.MaxPresets}). It starts as a copy of RHI Tuned's settings.", async () =>
        {
            var newName = await PromptTuneNameAsync("Add preset", "Name for the new preset:", "Add", "", null);
            if (newName == null) return;
            var newId = vm.AddTunePreset(newName);
            if (newId == null) return;
            SetTuneStatus($"Added {newName}. Choose what it does with Edit...", false);
            await ShowTunePresetEditorAsync(newId);
        });
        addBtn.IsEnabled = presets.Count < TunePresetStore.MaxPresets;
        addRow.Children.Add(addBtn);
        _tuneBody.Children.Add(addRow);
        _tuneBody.Children.Add(new TextBlock
        {
            Text = "With more than one preset shown, the game page's Tune button opens a menu to pick one.",
            Style = FindStyle("SettingsHelpStyle"),
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private void SetTuneStatus(string text, bool error)
    {
        if (_tuneStatus == null) return;
        _tuneStatus.Text = text;
        _tuneStatus.Foreground = UIFactory.Brush(error ? ResourceKeys.AccentRedBrush : ResourceKeys.TextSecondaryBrush);
        _tuneStatus.Visibility = Visibility.Visible;
    }

    /// <summary>What a preset does, one line per step (the row's tooltip).</summary>
    private static string TuneSummary(TunePreset p)
    {
        var lines = new List<string>();
        if (p.ReShade) lines.Add("Install ReShade");
        if (!string.IsNullOrEmpty(p.HdrMod)) lines.Add("Install " + HdrLabel(p.HdrMod));
        if (!string.IsNullOrEmpty(p.FrameLimiter)) lines.Add("Install " + LimiterLabel(p.FrameLimiter));
        if (p.OptiScaler) lines.Add("Install OptiScaler" + (string.IsNullOrEmpty(p.OsDllName) ? "" : $" as {p.OsDllName}"));
        foreach (var id in TuneComponentIds.ExtraOrder)
            if (p.Extras.TryGetValue(id, out var opt))
                lines.Add($"Install {DetailPanelBuilder.TuneExtraLabel(id)}" + (string.IsNullOrEmpty(opt) ? "" : $" ({opt})"));
        if (!string.IsNullOrEmpty(p.NrMethod))
            lines.Add("Neural Rendering: " + (p.NrMethod == TuneNr.Auto ? "Auto" : DetailPanelBuilder.NrMethodLabel(p.NrMethod))
                + (string.IsNullOrEmpty(p.NrAddonVersion) ? "" : $" {p.NrAddonVersion}")
                + $", NR DLL {(string.IsNullOrEmpty(p.NrDllVersion) ? "latest" : p.NrDllVersion)}");
        void Dll(string label, string v) { if (!string.IsNullOrEmpty(v)) lines.Add($"{label}: {(v == TuneDll.Newest ? "newest" : v)}"); }
        Dll("DLSS Super Resolution", p.DlssSr);
        Dll("DLSS Ray Reconstruction", p.DlssRr);
        Dll("DLSS Frame Generation", p.DlssFg);
        Dll("Streamline", p.Streamline);
        foreach (var item in TuneItems.All)
            if (p.Nvidia.TryGetValue(item.Key, out var v)) lines.Add($"{TunePlanner.NvidiaLabel(item)}: {item.Format(v)}");
        return lines.Count == 0 ? "Does nothing yet. Use Edit... to choose what it does." : string.Join("\n", lines);
    }

    private static string HdrLabel(string v) => v switch
    {
        TuneHdrMod.RenoDx => "RenoDX",
        TuneHdrMod.Luma => "Luma",
        TuneHdrMod.RenoDxElseLuma => "RenoDX, else Luma",
        _ => "None",
    };

    private static string LimiterLabel(string v) => v switch
    {
        TuneLimiter.ReLimiter => "ReLimiter",
        TuneLimiter.DisplayCommander => "Display Commander",
        _ => "None",
    };

    /// <summary>Asks for a preset name, checked as it's typed. Null when cancelled.</summary>
    private async Task<string?> PromptTuneNameAsync(string title, string prompt, string primary, string initial, string? renamingId)
    {
        var store = ViewModel.TunePresetStore;
        var box = new TextBox { Text = initial, MaxLength = TunePresetStore.MaxNameLength, Width = 340, PlaceholderText = "e.g. Competitive" };
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

    // ── Editor ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A preset's settings: every choice saves at once. "Leave" = Tune doesn't touch it. Laid out two per row
    /// (dense settings, user preference), help in tooltips.
    /// </summary>
    private async Task ShowTunePresetEditorAsync(string id)
    {
        var vm = ViewModel;
        var preset = vm.TunePresetStore.Get(id);
        if (preset == null) return;
        var cog = new CogDialog($"{preset.Name} Preset", _window.Content.XamlRoot);
        cog.AddIntro("What Tune does with this preset. Leave = not touched. Changes save straight away; games already tuned "
                   + "with it keep what was done to them. Steps that don't fit a game (no RenoDX mod, no DLSS file, no NVIDIA card...) are skipped there.");

        void Save()
        {
            if (!vm.UpdateTunePreset(preset)) cog.SetError("Couldn't save: the preset no longer exists.");
            else cog.ClearStatus();
        }

        var dlss = App.Services.GetRequiredService<IDlssStreamlineService>();

        // ── Components ──
        cog.AddSection("Components");
        var hdrValues = new[] { TuneHdrMod.None, TuneHdrMod.RenoDx, TuneHdrMod.Luma, TuneHdrMod.RenoDxElseLuma };
        var limValues = new[] { TuneLimiter.None, TuneLimiter.ReLimiter, TuneLimiter.DisplayCommander };
        cog.AddCompactGrid(
            cog.CompactCombo("ReShade", "Install ReShade (needed by RenoDX, ReLimiter and Display Commander). Uses the game's ReShade channel.",
                new[] { "Leave", "Install" }, preset.ReShade ? 1 : 0, i => { preset.ReShade = i == 1; Save(); return true; }),
            cog.CompactCombo("HDR mod", "RenoDX or Luma, when the game has one. \"RenoDX, else Luma\" uses Luma only for games without a RenoDX mod. Skipped when the other one is installed.",
                new[] { "None", "RenoDX", "Luma", "RenoDX, else Luma" }, Math.Max(0, Array.IndexOf(hdrValues, preset.HdrMod ?? "")),
                i => { preset.HdrMod = hdrValues[i]; Save(); return true; }),
            cog.CompactCombo("Frame limiter", "ReLimiter or Display Commander (one per game). Skipped when the other one is installed.",
                new[] { "None", "ReLimiter", "Display Commander" }, Math.Max(0, Array.IndexOf(limValues, preset.FrameLimiter ?? "")),
                i => { preset.FrameLimiter = limValues[i]; Save(); return true; }),
            null);

        // ── OptiScaler ──
        cog.AddSection("OptiScaler");
        var osNames = new List<string> { "" };
        osNames.AddRange(OptiScalerService.SupportedDllNames);
        if (!string.IsNullOrEmpty(preset.OsDllName) && !osNames.Contains(preset.OsDllName, StringComparer.OrdinalIgnoreCase)) osNames.Add(preset.OsDllName);
        cog.AddCompactGrid(
            cog.CompactCombo("OptiScaler", "Install OptiScaler (Extras) with your OptiScaler settings from Settings › Components. Skipped when it's already installed.",
                new[] { "Leave", "Install" }, preset.OptiScaler ? 1 : 0, i => { preset.OptiScaler = i == 1; Save(); return true; }),
            cog.CompactCombo("DLL name", "The file name OptiScaler goes in as. Default = OptiScaler's usual name for the game. Skipped in games where ReShade or Display Commander already uses that name.",
                osNames.Select(n => n.Length == 0 ? "Default" : n).ToList(),
                Math.Max(0, osNames.FindIndex(n => n.Equals(preset.OsDllName ?? "", StringComparison.OrdinalIgnoreCase))),
                i => { preset.OsDllName = osNames[i]; Save(); return true; }));

        // ── Other Extras ──
        cog.AddSection("More Extras");
        var extraCells = new List<CogDialog.CompactCell?>();
        foreach (var extraId in TuneComponentIds.ExtraOrder)
        {
            var xid = extraId;
            string label = DetailPanelBuilder.TuneExtraLabel(xid);
            string optLabel = DetailPanelBuilder.TuneExtraOptionLabel(xid);
            preset.Extras.TryGetValue(xid, out var curOpt);
            bool on = preset.Extras.ContainsKey(xid);
            var options = new List<string> { "" };
            if (optLabel.Length > 0)
            {
                options.AddRange(_window.DetailPanelBuilderInstance.TuneExtraOptions(xid));
                if (!string.IsNullOrEmpty(curOpt) && !options.Contains(curOpt, StringComparer.OrdinalIgnoreCase)) options.Add(curOpt);
            }
            var optCombo = CogDialog.MakeCombo(options.Select(o => o.Length == 0 ? "Default" : o).ToList(),
                Math.Max(0, options.FindIndex(o => o.Equals(curOpt ?? "", StringComparison.OrdinalIgnoreCase))), isEnabled: on && optLabel.Length > 0);
            optCombo.SelectionChanged += (s, e) =>
            {
                if (optCombo.SelectedIndex < 0 || !preset.Extras.ContainsKey(xid)) return;
                preset.Extras[xid] = options[optCombo.SelectedIndex];
                Save();
            };
            extraCells.Add(cog.CompactCombo(label, $"Install {label}, as its row in the game's Extras section does (same warnings). "
                    + "Skipped where the row can't install it (wrong API or bitness, a conflicting mod, anti-cheat...).",
                new[] { "Leave", "Install" }, on ? 1 : 0, i =>
                {
                    if (i == 1) preset.Extras[xid] = options[Math.Max(0, optCombo.SelectedIndex)];
                    else preset.Extras.Remove(xid);
                    optCombo.IsEnabled = i == 1 && optLabel.Length > 0;
                    Save();
                    return true;
                }));
            extraCells.Add(optLabel.Length > 0
                ? new CogDialog.CompactCell(optLabel, optLabel == "DLL name"
                    ? "The file name it goes in as. Default = the name its row recommends; a name another mod already uses in the game is skipped there."
                    : optLabel == "Variant" ? "Which DXVK build. Default = the game's current choice (or your global one)."
                    : "Which version. Default = the newest.", optCombo)
                : null);
        }
        cog.AddCompactGrid(extraCells.ToArray());

        // ── Neural Rendering ──
        cog.AddSection("Neural Rendering");
        var nrMethods = new List<string> { TuneNr.None, TuneNr.Auto };
        nrMethods.AddRange(DetailPanelBuilder.TuneNrMethods);
        var nrLabels = nrMethods.Select(m => m == TuneNr.None ? "Leave" : m == TuneNr.Auto ? "Auto (RHI's pick)" : DetailPanelBuilder.NrMethodLabel(m)).ToList();
        var nrDlls = new List<string> { "" };
        nrDlls.AddRange(dlss.DlssnrVersions);
        if (!string.IsNullOrEmpty(preset.NrDllVersion) && !nrDlls.Contains(preset.NrDllVersion, StringComparer.OrdinalIgnoreCase)) nrDlls.Add(preset.NrDllVersion);

        // Add-on version list follows the method (Auto / Leave = Latest only)
        var addonValues = new List<string>();
        var addonCombo = CogDialog.MakeCombo(new List<string>(), 0);
        bool addonFilling = false;
        void FillAddonVersions()
        {
            addonFilling = true;
            addonValues.Clear();
            addonValues.Add("");
            string m = preset.NrMethod ?? "";
            bool fixedMethod = m.Length > 0 && m != TuneNr.Auto;
            if (fixedMethod) addonValues.AddRange(DetailPanelBuilder.NrAddonVersionsFor(m));
            if (!string.IsNullOrEmpty(preset.NrAddonVersion) && !addonValues.Contains(preset.NrAddonVersion, StringComparer.OrdinalIgnoreCase))
                addonValues.Add(preset.NrAddonVersion);
            addonCombo.ItemsSource = addonValues.Select(v => v.Length == 0 ? "Latest" : v).ToList();
            addonCombo.SelectedIndex = Math.Max(0, addonValues.FindIndex(v => v.Equals(preset.NrAddonVersion ?? "", StringComparison.OrdinalIgnoreCase)));
            addonCombo.IsEnabled = fixedMethod;
            addonFilling = false;
        }
        addonCombo.SelectionChanged += (s, e) =>
        {
            if (addonFilling || addonCombo.SelectedIndex < 0 || addonCombo.SelectedIndex >= addonValues.Count) return;
            preset.NrAddonVersion = addonValues[addonCombo.SelectedIndex];
            Save();
        };
        FillAddonVersions();
        const string addonHelp = "The DLSS5 Tool / ShortFuse / GITC Uplift version the method installs. Latest = the newest, kept up to date. Pick a method (not Auto) to choose a version.";
        ToolTipService.SetToolTip(addonCombo, addonHelp);

        cog.AddCompactGrid(
            cog.CompactCombo("Method", "Neural Rendering method, as in the game page's Neural Rendering section. Auto = the method the section picks for the game. "
                + "Skipped where the method doesn't fit the game (32-bit, no DLSS, API) or another method is installed. DLSS5 Feeder may need shader setup in ReShade.",
                nrLabels, Math.Max(0, nrMethods.FindIndex(m => m.Equals(preset.NrMethod ?? "", StringComparison.OrdinalIgnoreCase))),
                i =>
                {
                    if (preset.NrMethod != nrMethods[i]) preset.NrAddonVersion = ""; // versions belong to one method
                    preset.NrMethod = nrMethods[i];
                    Save();
                    FillAddonVersions();
                    return true;
                }),
            new CogDialog.CompactCell("Add-on version", addonHelp, addonCombo),
            cog.CompactCombo("NR DLL version", "The nvngx_dlssnr.dll version the method puts in. Latest = the newest.",
                nrDlls.Select(v => v.Length == 0 ? "Latest" : v).ToList(),
                Math.Max(0, nrDlls.FindIndex(v => v.Equals(preset.NrDllVersion ?? "", StringComparison.OrdinalIgnoreCase))),
                i => { preset.NrDllVersion = nrDlls[i]; Save(); return true; }),
            null);

        // ── DLSS / Streamline files ──
        cog.AddSection("DLSS and Streamline files");
        CogDialog.CompactCell DllCombo(string label, string help, IReadOnlyList<string> versions, Func<string> get, Action<string> set)
        {
            var values = new List<string> { TuneDll.Leave, TuneDll.Newest };
            values.AddRange(versions);
            var cur = get() ?? "";
            if (cur.Length > 0 && !values.Contains(cur, StringComparer.OrdinalIgnoreCase)) values.Add(cur); // a version no longer listed
            var labels = values.Select(v => v == TuneDll.Leave ? "Leave" : v == TuneDll.Newest ? "Newest" : v).ToList();
            int idx = Math.Max(0, values.FindIndex(v => v.Equals(cur, StringComparison.OrdinalIgnoreCase)));
            return cog.CompactCombo(label, help, labels, idx, i => { set(values[i]); Save(); return true; });
        }
        cog.AddCompactGrid(
            DllCombo("Super Resolution", "nvngx_dlss.dll. Newest = the newest version RHI offers when you tune.", dlss.DlssVersions, () => preset.DlssSr, v => preset.DlssSr = v),
            DllCombo("Ray Reconstruction", "nvngx_dlssd.dll, in games with ray reconstruction.", dlss.DlssdVersions, () => preset.DlssRr, v => preset.DlssRr = v),
            DllCombo("Frame Generation", "nvngx_dlssg.dll, in games with DLSS frame generation.", dlss.DlssgVersions, () => preset.DlssFg, v => preset.DlssFg = v),
            DllCombo("Streamline", "The game's sl.*.dll files (only those it already has).", dlss.StreamlineVersions, () => preset.Streamline, v => preset.Streamline = v));
        cog.AddNote("Files are skipped in games where the NVIDIA Override is on for them, and DLSS 1 games.");

        // ── NVIDIA settings ──
        foreach (var group in new[] { TuneItems.GroupDlssPresets, TuneItems.GroupDlssScale, TuneItems.GroupDriver })
        {
            cog.AddSection(group);
            var cells = new List<CogDialog.CompactCell?>();
            foreach (var item in TuneItems.All.Where(i => i.Group == group))
            {
                var key = item.Key;
                var choices = item.Choices?.Invoke() ?? Array.Empty<(string, string)>();
                var values = new List<string?> { null };
                var labels = new List<string> { "Leave" };
                foreach (var (l, v) in choices) { labels.Add(l); values.Add(v); }
                preset.Nvidia.TryGetValue(key, out var cur);
                if (cur != null && !values.Contains(cur)) { labels.Add(item.Format(cur)); values.Add(cur); }
                int idx = Math.Max(0, values.IndexOf(cur));
                cells.Add(cog.CompactCombo(item.Label, item.Help, labels, idx, i =>
                {
                    if (values[i] == null) preset.Nvidia.Remove(key);
                    else preset.Nvidia[key] = values[i]!;
                    Save();
                    return true;
                }));
            }
            cog.AddCompactGrid(cells.ToArray());
            if (group == TuneItems.GroupDriver)
                cog.AddNote("Driver settings are written to the game's NVIDIA profile, like the Driver Settings section of its page. "
                          + "ReBAR and Smooth Motion APIs / flip pacing need Admin Mode.");
        }

        await cog.ShowAsync();
        RefreshTunePresetsCard();
        _window.RefreshTuneButtons();
    }
}
