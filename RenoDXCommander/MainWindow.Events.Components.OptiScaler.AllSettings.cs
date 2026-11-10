// MainWindow.Events.Components.OptiScaler.AllSettings.cs — "All Settings" section of the OptiScaler cog.
// Lists every setting in the game's OptiScaler.ini with the options the installed build documents in
// its own ini (OsIniEditor), so new values from custom builds can be picked without an RHI update.
// Changes wait for Apply (nothing is written by accident); applied changes are saved per game and
// written again after a reinstall or update.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private const int OsAllSearchLimit = 60;

    // Kept across the cog reopening after Apply, so the section opens where the user left it
    private string? _osAllOpenFor;
    private string? _osAllSection;
    private string? _osAllAppliedMessage;

    private void BuildOsAllSettingsSection(OsCogContext ctx)
    {
        var card = ctx.Card;
        var cog = ctx.Cog;
        var store = ctx.Store;
        if (!card.IsOsInstalled || string.IsNullOrEmpty(card.InstallPath)) return;
        var gameDir = card.InstallPath;
        if (!File.Exists(Path.Combine(gameDir, OptiScalerService.IniFileName))) return;
        var openKey = $"{card.GameName}|{store}";

        cog.AddSection("All Settings");
        cog.AddNote("Every setting in this game's OptiScaler.ini, with the options the installed OptiScaler build lists. " +
                    "Nothing is written until you press Apply. Applied changes are kept when OptiScaler is reinstalled or updated.");

        var settings = new List<OsIniSetting>();
        var pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool loaded = false;

        var host = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        var sectionCombo = CogDialog.MakeCombo(Array.Empty<string>(), -1);
        var search = CogDialog.MakeTextBox("", "Search all settings");
        var filterRow = new Grid { ColumnSpacing = 12 };
        filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ToolTipService.SetToolTip(sectionCombo, "Section of OptiScaler.ini to show.");
        Grid.SetColumn(search, 1);
        filterRow.Children.Add(sectionCombo);
        filterRow.Children.Add(search);
        var listHost = new StackPanel { Spacing = 8 };
        var info = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
        };

        Button? applyBtn = null, revertBtn = null, forgetBtn = null;
        void UpdateButtons()
        {
            if (applyBtn != null)
            {
                applyBtn.Content = pending.Count == 0 ? "Apply" : $"Apply {pending.Count} change{(pending.Count == 1 ? "" : "s")}";
                applyBtn.IsEnabled = pending.Count > 0;
            }
            if (revertBtn != null) revertBtn.IsEnabled = pending.Count > 0;
            var saved = ViewModel.GetOsIniEdits(card.GameName, store).Count;
            if (forgetBtn != null) forgetBtn.IsEnabled = saved > 0;
            info.Text = saved > 0
                ? $"{saved} applied edit{(saved == 1 ? " is" : "s are")} saved for this game and written again after a reinstall or update. Hover a setting for its description."
                : "Hover a setting for its description. Settings not in the file yet are shown with auto and added when you change them.";
        }

        // One cell per setting: dropdown for documented options, text box for anything else
        OsCell MakeCell(OsIniSetting s, bool showSection)
        {
            var id = s.Id;
            string Current() => pending.TryGetValue(id, out var p) ? p : s.Value;
            void Changed(string value)
            {
                if (string.Equals(value, s.Value, StringComparison.Ordinal)) pending.Remove(id);
                else pending[id] = value;
                UpdateButtons();
            }

            var help = s.Help;
            if (!string.IsNullOrEmpty(s.Default)) help += (help.Length > 0 ? "\n" : "") + $"Default (auto): {s.Default}";
            help += (help.Length > 0 ? "\n" : "") + $"[{s.Section}] {s.Key}" + (s.InFile ? "" : " (not in the file yet)");
            var label = showSection ? $"{s.Key} ({s.Section})" : s.Key;

            FrameworkElement control;
            if (s.Kind == OsIniKind.Text)
            {
                var tb = CogDialog.MakeTextBox(Current(), "auto");
                tb.TextChanged += (_, _) => Changed(tb.Text.Trim());
                control = tb;
            }
            else
            {
                var values = s.Options.Select(o => o.Value).ToList();
                var labels = s.Options.Select(o => o.Label).ToList();
                // A value the build doesn't list (typed by hand, or from another build) stays selectable
                foreach (var v in new[] { s.Value, Current() })
                    if (!values.Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase)))
                    {
                        values.Add(v);
                        labels.Add(v.Length == 0 ? "(empty)" : v);
                    }
                int sel = values.FindIndex(x => x.Equals(Current(), StringComparison.OrdinalIgnoreCase));
                var combo = CogDialog.MakeCombo(labels, sel);
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedIndex >= 0) Changed(values[combo.SelectedIndex]);
                };
                control = combo;
            }
            return new OsCell(label, help, control);
        }

        bool Matches(OsIniSetting s, string q) =>
            s.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
            || s.Section.Contains(q, StringComparison.OrdinalIgnoreCase)
            || s.Help.Contains(q, StringComparison.OrdinalIgnoreCase);

        void Render()
        {
            listHost.Children.Clear();
            var q = search.Text.Trim();
            List<OsIniSetting> shown;
            bool searching = q.Length > 0;
            if (searching)
            {
                var all = settings.Where(s => Matches(s, q)).ToList();
                shown = all.Take(OsAllSearchLimit).ToList();
                listHost.Children.Add(MakeOsFooterNote(all.Count == 0
                    ? "No settings match."
                    : all.Count > shown.Count
                        ? $"Showing the first {shown.Count} of {all.Count} matches."
                        : $"{all.Count} match{(all.Count == 1 ? "" : "es")}."));
            }
            else
            {
                var section = sectionCombo.SelectedItem as string;
                shown = settings.Where(s => s.Section.Equals(section, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            sectionCombo.IsEnabled = !searching;
            if (shown.Count > 0)
            {
                var grid = MakeOsGrid(null, shown.Select(s => (OsCell?)MakeCell(s, searching)).ToArray());
                listHost.Children.Add(grid);
                TabsTheme.Restyle(grid);
            }
        }

        async Task LoadAsync()
        {
            try
            {
                settings = await Task.Run(() => OsIniEditor.Load(gameDir));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[MainWindow.BuildOsAllSettingsSection] Couldn't read OptiScaler.ini — {ex.Message}");
                cog.SetError($"Couldn't read OptiScaler.ini: {ex.Message}");
                return;
            }
            var sections = settings.Select(s => s.Section).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            sectionCombo.ItemsSource = sections;
            var start = sections.FirstOrDefault(s => s.Equals(_osAllSection, StringComparison.OrdinalIgnoreCase))
                        ?? sections.FirstOrDefault();
            sectionCombo.SelectedItem = start;
            loaded = true; // after the selection, so its SelectionChanged doesn't render a second time
            Render();
            UpdateButtons();
        }

        sectionCombo.SelectionChanged += (_, _) =>
        {
            if (!loaded) return;
            _osAllSection = sectionCombo.SelectedItem as string;
            Render();
        };

        DispatcherQueueTimer? searchTimer = null;
        search.TextChanged += (_, _) =>
        {
            if (!loaded) return;
            searchTimer ??= DispatcherQueue.CreateTimer();
            searchTimer.Interval = TimeSpan.FromMilliseconds(200);
            searchTimer.IsRepeating = false;
            searchTimer.Tick -= OnSearchTick;
            searchTimer.Tick += OnSearchTick;
            searchTimer.Stop();
            searchTimer.Start();
        };
        void OnSearchTick(DispatcherQueueTimer t, object o) => Render();

        applyBtn = cog.MakeButton("Apply", CogButtonKind.Primary, async () =>
        {
            if (pending.Count == 0) return;
            var values = pending.Select(kv => { var (s, k) = OsIniEditor.SplitId(kv.Key); return (s, k, kv.Value); }).ToList();
            var err = await Task.Run(() => OsIniEditor.WriteValues(gameDir, values));
            if (err != null) { cog.SetError($"Couldn't write OptiScaler.ini: {err}"); return; }

            var saved = ViewModel.GetOsIniEdits(card.GameName, store);
            foreach (var kv in pending) saved[kv.Key] = kv.Value;
            ViewModel.SetOsIniEdits(card.GameName, saved, store);

            // The Frame Generation dropdowns above keep their own saved values; keep them in step
            foreach (var (s, k, v) in values)
            {
                if (!s.Equals("FrameGen", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.Equals("FGInput", StringComparison.OrdinalIgnoreCase)) ViewModel.SetOsFgInput(card.GameName, v, store);
                else if (k.Equals("FGOutput", StringComparison.OrdinalIgnoreCase)) ViewModel.SetOsFgOutput(card.GameName, v, store);
                else if (k.Equals("FGNvngxReplacement", StringComparison.OrdinalIgnoreCase)) ViewModel.SetOsFgNvngxReplacement(card.GameName, v, store);
            }

            CrashReporter.Log($"[MainWindow.BuildOsAllSettingsSection] Applied {values.Count} OptiScaler.ini change(s) for '{card.GameName}'");
            _osAllOpenFor = openKey;
            _osAllAppliedMessage = $"Applied {values.Count} change{(values.Count == 1 ? "" : "s")} to OptiScaler.ini.";
            ReopenOsCog(ctx); // the settings above show the new values too
        }, "Write the changed settings to this game's OptiScaler.ini. The old file is backed up first.");

        revertBtn = cog.MakeButton("Revert", CogButtonKind.Secondary, () =>
        {
            pending.Clear();
            Render();
            UpdateButtons();
        }, "Forget the changes you haven't applied yet.");

        forgetBtn = cog.MakeButton("Forget saved edits", CogButtonKind.Secondary, () =>
        {
            ViewModel.SetOsIniEdits(card.GameName, null, store);
            UpdateButtons();
            cog.SetSuccess("Saved edits forgotten. OptiScaler.ini is unchanged; a reinstall will use the default settings.");
        }, "Stop writing your applied edits again after a reinstall or update. Doesn't change the current OptiScaler.ini.");

        var backupsBtn = cog.MakeButton("Open backups", CogButtonKind.Secondary, async () =>
        {
            var dir = OsIniEditor.BackupDir(gameDir);
            if (!Directory.Exists(dir)) { cog.SetStatus("No backups yet. One is made each time you press Apply."); return; }
            await Windows.System.Launcher.LaunchFolderPathAsync(dir);
        }, "Copies of OptiScaler.ini from before each Apply (newest 5).");

        var actions = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        foreach (var b in new[] { applyBtn, revertBtn, forgetBtn, backupsBtn }) actions.Children.Add(b);
        host.Children.Add(filterRow);
        host.Children.Add(listHost);
        host.Children.Add(info);
        host.Children.Add(actions);

        Button? showBtn = null;
        async Task ToggleAsync()
        {
            bool open = host.Visibility != Visibility.Visible;
            host.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            showBtn!.Content = open ? "Hide all settings" : "Show all settings";
            _osAllOpenFor = open ? openKey : null;
            if (open && !loaded) await LoadAsync();
        }
        showBtn = cog.MakeButton("Show all settings", CogButtonKind.Secondary, ToggleAsync);
        cog.AddButtons(showBtn);
        cog.AddContent(host);
        UpdateButtons();

        // Reopened after Apply: open again and scroll to it
        if (_osAllOpenFor == openKey)
        {
            var message = _osAllAppliedMessage;
            _osAllAppliedMessage = null;
            host.Loaded += async (_, _) =>
            {
                try
                {
                    if (host.Visibility == Visibility.Visible) return;
                    await ToggleAsync();
                    host.StartBringIntoView();
                    if (message != null) cog.SetSuccess(message);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[MainWindow.BuildOsAllSettingsSection] Reopen failed — {ex.Message}");
                }
            };
        }
    }
}
