// OverlayWindow.Display.cs -- two RHI overlay tiles that read live system state:
//   HDR: whether Windows HDR is on for the monitor the overlay (so the game) is on, from Windows' own
//        advanced-colour info for the window (HdrToggleService.IsHdrEnabled gives false positives).
//   DLSS: the DLL versions plus the presets and render scale in the game's NVIDIA profile, read off the UI
//        thread with a time limit, like the DLSS / Streamline section (DlssPresetService isn't thread-safe
//        and serialises its own calls).
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Overlay;

internal sealed partial class OverlayWindow
{
    /// <summary>The HDR tile for the game's monitor, or null when Windows can't say.</summary>
    private Border? MakeHdrTile()
    {
        try
        {
            var info = Microsoft.Graphics.Display.DisplayInformation.CreateForWindowId(AppWindow.Id);
            var color = info.GetAdvancedColorInfo();
            bool hdr = color.CurrentAdvancedColorKind == Microsoft.Graphics.Display.DisplayAdvancedColorKind.HighDynamicRange;
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(BigText(hdr ? "On" : "Off"));
            double peak = color.MaxLuminanceInNits;
            body.Children.Add(SmallText(hdr
                ? peak > 0 ? $"Peak {Math.Round(peak):0} nits" : "Windows HDR is on"
                : "Windows HDR is off on this monitor"));
            return MakeCard("HDR", body, stretch: true);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OverlayWindow.HdrTile] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The DLSS / Streamline table: versions now, then presets and render scale from the NVIDIA profile when
    /// they've been read (null when the game has no DLSS files). Goes in the "This game" card.
    /// </summary>
    private FrameworkElement? MakeDlssSection(GameCardViewModel card)
    {
        bool hasSr = !string.IsNullOrEmpty(card.DlssInstalledVersion);
        bool hasRr = !string.IsNullOrEmpty(card.DlssdInstalledVersion);
        bool hasFg = !string.IsNullOrEmpty(card.DlssgInstalledVersion);
        bool hasNr = !string.IsNullOrEmpty(card.DlssnrInstalledVersion);
        bool hasSl = !string.IsNullOrEmpty(card.StreamlineInstalledVersion);
        if (!hasSr && !hasRr && !hasFg && !hasNr && !hasSl) return null;

        var holder = new Border();
        var note = SmallText("");
        note.Visibility = Visibility.Collapsed;
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(holder);
        body.Children.Add(note);

        void Render(DlssPresets? p)
        {
            var rows = new List<(string Name, string Value, bool Update)>();
            void Row(string name, bool has, string? version, uint? preset, uint? scale)
            {
                if (!has) return;
                var parts = new List<string> { version! };
                if (preset is uint pr) parts.Add(PresetText(pr));
                if (scale is uint sc && sc > 0) parts.Add($"{sc}% scale");
                rows.Add((name, string.Join("   ·   ", parts), false));
            }
            Row("Super Resolution", hasSr, card.DlssInstalledVersion, p?.Sr, p?.SrScale);
            Row("Ray Reconstruction", hasRr, card.DlssdInstalledVersion, p?.Rr, p?.RrScale);
            Row("Frame Generation", hasFg, card.DlssgInstalledVersion, p?.Fg, null);
            Row("Neural Rendering", hasNr, card.DlssnrInstalledVersion, p?.Nr, null);
            Row("Streamline", hasSl, card.StreamlineInstalledVersion, null, null);
            holder.Child = MakeTable(rows);
        }
        Render(null);

        // Presets come from the NVIDIA profile: NVIDIA cards only, read in the background
        var svc = App.Services?.GetService<DlssPresetService>();
        if (svc?.IsSupported == true && (hasSr || hasRr || hasFg || hasNr))
        {
            string name = card.GameName, path = card.InstallPath;
            bool readNr = hasNr && FeatureFlags.DlssNr;
            int openCount = _openCount;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    var read = Task.Run(() =>
                    {
                        svc.PrimeProfileCache(name, path, cts.Token);
                        return new DlssPresets(
                            hasSr ? svc.GetSrPreset(name, path) : null,
                            hasRr ? svc.GetRrPreset(name, path) : null,
                            hasFg ? svc.GetFgPreset(name, path) : null,
                            readNr ? svc.GetNrPreset(name, path) : null,
                            hasSr ? svc.GetSrRenderScale(name, path) : null,
                            hasRr ? svc.GetRrRenderScale(name, path) : null);
                    }, cts.Token);
                    var done = await Task.WhenAny(read, Task.Delay(5000)).ConfigureAwait(false);
                    if (done != read)
                    {
                        CrashReporter.Log($"[OverlayWindow.DlssPresets] NVIDIA profile read timed out for '{name}'");
                        return;
                    }
                    var presets = await read.ConfigureAwait(false);
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (openCount != _openCount || !IsOpen) return;
                        Render(presets);
                        note.Text = "Presets from the NVIDIA profile. Default: the game picks.";
                        note.Visibility = Visibility.Visible;
                    });
                }
                catch (Exception ex) { CrashReporter.Log($"[OverlayWindow.DlssPresets] '{name}': {ex.GetType().Name}: {ex.Message}"); }
            });
        }
        return body;
    }

    private sealed record DlssPresets(uint? Sr, uint? Rr, uint? Fg, uint? Nr, uint? SrScale, uint? RrScale);

    /// <summary>"Preset K", "Default preset", "NVIDIA recommended preset".</summary>
    internal static string PresetText(uint value) => value switch
    {
        0 => "Default preset",
        0x00FFFFFF or 0x00FFFFFE => "NVIDIA recommended preset",
        >= 1 and <= 26 => $"Preset {(char)('A' + value - 1)}",
        _ => $"Preset 0x{value:X}",
    };
}
