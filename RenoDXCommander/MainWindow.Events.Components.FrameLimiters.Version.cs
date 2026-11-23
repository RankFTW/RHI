// MainWindow.Events.Components.FrameLimiters.Version.cs — the Version section of the ReLimiter ⚙.
// Per game ("GameName|Store"): Automatic (v4 where it qualifies, else the last v3), v4 (latest), v3 (final), or a pinned
// release. Pinned games are skipped by the update check, Update All and automatic updates. Releases come from
// UlReleaseService; picking a version on a game with ReLimiter installed swaps the addon straight away. relimiter.ini is
// kept, except across v3 ↔ v4, where the game gets a fresh one (RHI's lines only) and the old one is backed up.
// The v3 / v4 options only appear once a v4 release exists; the rules are in UlV4Rules.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private const string UlLatestItem = "Latest (auto-update)";
    private const string UlAutoItem = "Automatic";
    private const string UlV4Item = "v4 (latest)";
    private const string UlV3Item = "v3 (final)";

    private void AddUlVersionSection(CogDialog cog, GameCardViewModel card)
    {
        var store = card.Source ?? "";
        var pinned = ViewModel.GetUlPinnedVersion(card.GameName, store);
        var choice = ViewModel.GetUlMajorChoice(card.GameName, store);
        bool is32 = card.Is32Bit;

        cog.AddSection("Version");

        var combo = CogDialog.MakeCombo(new List<string> { pinned ?? UlLatestItem }, 0);
        cog.AddRow("Version", combo,
            "Pick a ReLimiter release for this game. Automatic gives every game on an NVIDIA card ReLimiter v4 (pick v3 if " +
            "it doesn't work well in this game) and other graphics cards the last v3 release. A picked release stays: updates " +
            "skip this game. Moving between v3 and v4 sets up relimiter.ini again from your template (the old one is kept as a backup)." +
            (is32 ? " 32-bit builds start at v3.3.1." : ""));

        var installedLine = cog.AddNote(InstalledText());
        var verdictLine = cog.AddNote("");
        verdictLine.Visibility = Visibility.Collapsed;
        var warning = cog.AddWarning("");
        warning.Visibility = Visibility.Collapsed;
        // ReLimiter v4 doesn't support NVIDIA Dynamic MFG: shown when DMFG is on and v4 is installed or can be picked
        var dmfgWarning = cog.AddWarning(MainViewModel.UlDmfgWarning);
        dmfgWarning.Visibility = Visibility.Collapsed;
        bool dmfgOn = false;
        // ...and doesn't work as intended with the Extras MFG mods
        var mfgModsWarning = cog.AddWarning("");
        mfgModsWarning.Visibility = Visibility.Collapsed;
        var switchBtn = CogDialog.StyledButton("Switch to v3", CogButtonKind.Primary);
        var switchRow = cog.AddButtons(switchBtn);
        switchRow.Visibility = Visibility.Collapsed;

        string InstalledText() => card.UlStatus == Models.GameStatus.NotInstalled
            ? "ReLimiter isn't installed on this game. The version you pick is used when you install it."
            : $"Installed: {(string.IsNullOrEmpty(card.UlInstalledVersion) ? "unknown" : "v" + card.UlInstalledVersion.TrimStart('v', 'V'))}";

        bool filling = false;
        bool hasV4 = false;
        UlV4Verdict verdict = ViewModel.UlV4VerdictFor(card);
        switchBtn.Click += async (s, ev) => await SwitchToV3Async();

        // Combo entry for the game's current setting
        string CurrentItem() => pinned ?? (!hasV4 ? UlLatestItem : choice switch
        {
            "4" when verdict.Allowed => UlV4Item,
            "3" => UlV3Item,
            _ => UlAutoItem,
        });

        void RefreshNotes()
        {
            string? text = !hasV4 ? null
                : !verdict.Allowed ? verdict.Reason
                : verdict.Note ?? (verdict.Auto ? "Automatic gives this game ReLimiter v4." : null);
            if (MainViewModel.UlDevTestVersion() is { } dev)
                text = $"Dev test: v{UlV4Rules.DisplayVersion(dev)} from relimiter-v4-test is used as the latest ReLimiter. " + (text ?? "");
            verdictLine.Text = text ?? "";
            verdictLine.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;

            // Safety net: v4 in a game that can't use it (GPU changed, DXVK added, 32-bit override...)
            bool onV4 = card.UlStatus != Models.GameStatus.NotInstalled
                && UlV4Rules.MajorOf(card.UlInstalledVersion) >= UlV4Rules.V4Major;
            bool bad = onV4 && !verdict.Allowed;
            warning.Text = bad ? $"This game has ReLimiter v4, which won't work here: {verdict.Reason} Switch back to v3 (with a fresh relimiter.ini)." : "";
            warning.Visibility = bad ? Visibility.Visible : Visibility.Collapsed;
            switchRow.Visibility = bad ? Visibility.Visible : Visibility.Collapsed;

            bool showDmfg = dmfgOn && (onV4 || (hasV4 && verdict.Allowed));
            dmfgWarning.Visibility = showDmfg ? Visibility.Visible : Visibility.Collapsed;
            bool v4Relevant = onV4 || (hasV4 && verdict.Allowed);
            mfgModsWarning.Visibility = v4Relevant && mfgModsWarning.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // The driver profile is read through NVAPI, off the UI thread
        _ = ReadDmfgAsync();
        async Task ReadDmfgAsync()
        {
            try
            {
                var (on, mods) = await Task.Run(() => (ViewModel.IsDmfgOn(card), ViewModel.InstalledMfgModsForUl(card)));
                dmfgOn = on;
                mfgModsWarning.Text = MainViewModel.UlMfgModsWarning(mods) ?? "";
                if (dmfgOn || mods.Count > 0) RefreshNotes();
            }
            catch (Exception ex) { CrashReporter.Log($"[MainWindow.AddUlVersionSection] DMFG / MFG mods read failed — {ex.Message}"); }
        }

        void Fill(List<UlRelease> releases)
        {
            filling = true;
            try
            {
                verdict = ViewModel.UlV4VerdictFor(card);
                var devBuild = MainViewModel.UlDevTestVersion();
                hasV4 = releases.Any(r => UlV4Rules.MajorOf(r.Tag) >= UlV4Rules.V4Major)
                    || devBuild?.Major >= UlV4Rules.V4Major;
                var tags = releases
                    .Where(r => r.UrlFor(is32) != null)
                    .Where(r => verdict.Allowed || !(UlV4Rules.MajorOf(r.Tag) >= UlV4Rules.V4Major))
                    .Select(r => r.Tag).ToList();
                if (pinned != null && !tags.Contains(pinned, StringComparer.OrdinalIgnoreCase)) tags.Insert(0, pinned);

                var list = new List<string>();
                if (hasV4)
                {
                    list.Add(UlAutoItem);
                    if (verdict.Allowed) list.Add(UlV4Item);
                    list.Add(UlV3Item);
                }
                else list.Add(UlLatestItem);
                list.AddRange(tags);
                combo.ItemsSource = list;
                var idx = list.FindIndex(t => t.Equals(CurrentItem(), StringComparison.OrdinalIgnoreCase));
                combo.SelectedIndex = idx < 0 ? 0 : idx;
                RefreshNotes();
            }
            finally { filling = false; }
        }

        if (UlReleaseService.Cached is { } known && GpuInfoService.Cached != null) Fill(known);
        _ = Task.Run(async () =>
        {
            try
            {
                await GpuInfoService.GetNvidiaGpusAsync().ConfigureAwait(false);
                var releases = await UlReleaseService.ListAsync().ConfigureAwait(false);
                if (releases == null)
                {
                    DispatcherQueue.TryEnqueue(() => cog.SetError("Couldn't load ReLimiter's releases from GitHub. Try again later."));
                    return;
                }
                DispatcherQueue.TryEnqueue(() => Fill(releases));
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MainWindow.UlVersion] Loading releases failed — {ex.Message}");
            }
        });

        // Puts the combo back on the game's current setting without running the handler
        void SelectCurrent()
        {
            if (combo.ItemsSource is not List<string> list) return;
            filling = true;
            try
            {
                var idx = list.FindIndex(t => t.Equals(CurrentItem(), StringComparison.OrdinalIgnoreCase));
                combo.SelectedIndex = idx < 0 ? 0 : idx;
            }
            finally { filling = false; }
        }

        // Saves the setting and installs it when ReLimiter is installed. Returns false when the install failed.
        async Task<bool> ApplyAsync(string? newChoice, string? newPin, string label)
        {
            var (prevChoice, prevPin) = (choice, pinned);
            ViewModel.SetUlMajorChoice(card.GameName, newChoice, store);
            ViewModel.SetUlPinnedVersion(card.GameName, newPin, store);
            (choice, pinned) = (newChoice, newPin);

            if (card.UlStatus == Models.GameStatus.NotInstalled)
            {
                if (newPin == null) { cog.SetSuccess($"This game will get {label} when you install ReLimiter."); return true; }
                // Download it now so the install doesn't wait
                cog.SetStatus($"Downloading ReLimiter {newPin}...");
                var (path, err) = await Task.Run(() => UlReleaseService.EnsurePinnedAsync(newPin, is32, null, LifetimeToken));
                if (path != null) cog.SetSuccess($"ReLimiter {newPin} is ready. It will be installed when you install ReLimiter.");
                else cog.SetError($"Couldn't download {newPin} ({err}). It will be tried again when you install.");
                return true;
            }

            cog.SetStatus($"Installing ReLimiter {label}...");
            var (error, iniReset) = await ViewModel.SwitchUlVersionAsync(card);
            if (error == null)
            {
                var ini = iniReset
                    ? " v3 and v4 settings don't mix, so this game got a fresh relimiter.ini; the old one is saved next to it as a backup."
                    : " Your relimiter.ini settings were kept.";
                cog.SetSuccess(newPin != null
                    ? $"Installed ReLimiter {newPin}. Updates will skip this game.{ini}"
                    : $"Installed {label}. This game gets updates.{ini}");
                // The card's version is set on the dispatcher; read it after that has run
                DispatcherQueue.TryEnqueue(() => { installedLine.Text = InstalledText(); RefreshNotes(); });
                return true;
            }

            // Keep the setting in line with what's in the game folder
            ViewModel.SetUlMajorChoice(card.GameName, prevChoice, store);
            ViewModel.SetUlPinnedVersion(card.GameName, prevPin, store);
            (choice, pinned) = (prevChoice, prevPin);
            SelectCurrent();
            cog.SetError($"Couldn't install ReLimiter {label}: {error} If the game is running, close it and try again.");
            return false;
        }

        async Task SwitchToV3Async()
        {
            switchBtn.IsEnabled = false;
            try { await ApplyAsync("3", null, "ReLimiter v3"); SelectCurrent(); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MainWindow.UlVersion] Switch to v3 failed for '{card.GameName}' — {ex.Message}");
                cog.SetError($"Couldn't switch to v3: {ex.Message}");
            }
            finally { switchBtn.IsEnabled = true; }
        }

        bool busy = false;
        combo.SelectionChanged += async (s, ev) =>
        {
            if (filling || busy || combo.SelectedItem is not string sel) return;
            if (sel.Equals(CurrentItem(), StringComparison.OrdinalIgnoreCase)) return;

            (string? newChoice, string? newPin, string label) = sel switch
            {
                UlLatestItem or UlAutoItem => ((string?)null, (string?)null, hasV4 ? "the automatic ReLimiter version" : "the latest ReLimiter"),
                UlV4Item => ("4", null, "ReLimiter v4"),
                UlV3Item => ("3", null, "ReLimiter v3"),
                _ => (null, sel, sel),
            };

            busy = true;
            combo.IsEnabled = false;
            try
            {
                await ApplyAsync(newChoice, newPin, label);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MainWindow.UlVersion] Version change failed for '{card.GameName}' — {ex.Message}");
                cog.SetError($"Couldn't change the version: {ex.Message}");
            }
            finally
            {
                busy = false;
                combo.IsEnabled = true;
            }
        };
    }
}
