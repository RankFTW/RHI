// MainWindow.Events.Components.FrameLimiters.Version.cs — the Version section of the ReLimiter ⚙:
// pin a game to a ReLimiter release (per game, "GameName|Store"). Pinned games are skipped by the update
// check, Update All and automatic updates. Releases come from UlReleaseService; picking a version on a game
// with ReLimiter installed swaps the addon straight away (relimiter.ini is kept).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private const string UlLatestItem = "Latest (auto-update)";

    private void AddUlVersionSection(CogDialog cog, GameCardViewModel card)
    {
        var store = card.Source ?? "";
        var pinned = ViewModel.GetUlPinnedVersion(card.GameName, store);
        bool is32 = card.Is32Bit;

        cog.AddSection("Version");

        var items = new List<string> { UlLatestItem };
        if (pinned != null) items.Add(pinned);
        var combo = CogDialog.MakeCombo(items, pinned != null ? 1 : 0);
        cog.AddRow("Version", combo,
            "Pick a ReLimiter release for this game. A picked version stays: updates skip this game. " +
            "Latest always installs the newest release." + (is32 ? " 32-bit builds start at v3.3.1." : ""));

        var installedLine = cog.AddNote(InstalledText());
        string InstalledText() => card.UlStatus == Models.GameStatus.NotInstalled
            ? "ReLimiter isn't installed on this game. The version you pick is used when you install it."
            : $"Installed: {(string.IsNullOrEmpty(card.UlInstalledVersion) ? "unknown" : "v" + card.UlInstalledVersion.TrimStart('v', 'V'))}";

        bool filling = false;
        void Fill(List<UlRelease> releases)
        {
            filling = true;
            try
            {
                var tags = releases.Where(r => r.UrlFor(is32) != null).Select(r => r.Tag).ToList();
                if (pinned != null && !tags.Contains(pinned, StringComparer.OrdinalIgnoreCase)) tags.Insert(0, pinned);
                var list = new List<string> { UlLatestItem };
                list.AddRange(tags);
                combo.ItemsSource = list;
                combo.SelectedIndex = pinned != null
                    ? 1 + tags.FindIndex(t => t.Equals(pinned, StringComparison.OrdinalIgnoreCase))
                    : 0;
            }
            finally { filling = false; }
        }

        if (UlReleaseService.Cached is { } known) Fill(known);
        else
        {
            _ = Task.Run(async () =>
            {
                var releases = await UlReleaseService.ListAsync().ConfigureAwait(false);
                if (releases == null)
                {
                    DispatcherQueue.TryEnqueue(() => cog.SetError("Couldn't load ReLimiter's releases from GitHub. Try again later."));
                    return;
                }
                DispatcherQueue.TryEnqueue(() => Fill(releases));
            });
        }

        // Puts the combo back on a version without running the handler
        void Select(string? tag)
        {
            if (combo.ItemsSource is not List<string> list) return;
            filling = true;
            try
            {
                var idx = tag == null ? 0 : list.FindIndex(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
                combo.SelectedIndex = idx < 0 ? 0 : idx;
            }
            finally { filling = false; }
        }

        bool busy = false;
        combo.SelectionChanged += async (s, ev) =>
        {
            if (filling || busy || combo.SelectedItem is not string sel) return;
            var tag = sel == UlLatestItem ? null : sel;
            if (string.Equals(tag, pinned, StringComparison.OrdinalIgnoreCase)) return;
            var previous = pinned;
            busy = true;
            combo.IsEnabled = false;
            try
            {
                ViewModel.SetUlPinnedVersion(card.GameName, tag, store);
                pinned = tag;
                var label = tag ?? "the latest version";

                if (card.UlStatus == Models.GameStatus.NotInstalled)
                {
                    if (tag == null) { cog.SetSuccess("This game will get the latest ReLimiter when you install it."); return; }
                    // Download it now so the install doesn't wait
                    cog.SetStatus($"Downloading ReLimiter {tag}...");
                    var (path, err) = await Task.Run(() => UlReleaseService.EnsurePinnedAsync(tag, is32, null, LifetimeToken));
                    if (path != null) cog.SetSuccess($"ReLimiter {tag} is ready. It will be installed when you install ReLimiter.");
                    else cog.SetError($"Couldn't download {tag} ({err}). It will be tried again when you install.");
                    return;
                }

                cog.SetStatus($"Installing ReLimiter {label}...");
                var error = await ViewModel.SwitchUlVersionAsync(card);
                if (error == null)
                {
                    cog.SetSuccess(tag == null
                        ? "Installed the latest ReLimiter. This game gets updates again. Your relimiter.ini settings were kept."
                        : $"Installed ReLimiter {tag}. Updates will skip this game. Your relimiter.ini settings were kept.");
                    // The card's version is set on the dispatcher; read it after that has run
                    DispatcherQueue.TryEnqueue(() => installedLine.Text = InstalledText());
                }
                else
                {
                    // Keep the setting in line with what's in the game folder
                    ViewModel.SetUlPinnedVersion(card.GameName, previous, store);
                    pinned = previous;
                    Select(previous);
                    cog.SetError($"Couldn't install ReLimiter {label}: {error} If the game is running, close it and try again.");
                }
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
