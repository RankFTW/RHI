// DetailPanelBuilder.NeuralRendering.Uplift.cs — installs the GITC Uplift Neural Rendering method
// (UpliftService does the file work). Its version picker is the section's middle (addon version)
// combo: "Latest" auto-updates, a picked version stays pinned.

using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>Addon type for the middle version combo: GITC Uplift's own releases.</summary>
    private const string UpliftAddonType = "uplift";

    /// <summary>Which version list the middle combo shows for a method.</summary>
    private static string AddonTypeFor(string methodKey) => methodKey switch
    {
        NrMethodShortFuse => "dlsstool",
        NrMethodUplift    => UpliftAddonType,
        _                 => "dlss5tool",
    };

    /// <summary>Addon-version combo item → pinned version, or null for "Latest (…)".</summary>
    private static string? PinnedFromCombo(string? item) =>
        string.IsNullOrEmpty(item) || item.StartsWith("Latest", StringComparison.Ordinal) ? null : item;

    /// <summary>
    /// Downloads GITC Uplift (the version picked in the Uplift Version combo, or Latest), deploys it
    /// with the NR DLL picked in NR DLL Version, then syncs the game's shaders (the sync hook adds
    /// Uplift.fx / UpliftMask.fx from the same version).
    /// </summary>
    private async Task InstallUpliftAsync(GameCardViewModel card, Button statusBtn, ComboBox addonVersionCombo,
        ComboBox nrVersionCombo, IDlssStreamlineService dlssSvc, UpliftService upliftSvc)
    {
        var installPath = card.InstallPath!;
        var gameName = card.GameName;
        var store = card.Source ?? "";

        var verItem = await DispatchAsync<string?>(_window.DispatcherQueue!, () => addonVersionCombo.SelectedItem as string).ConfigureAwait(false);
        var wanted = PinnedFromCombo(verItem);
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = wanted == null ? "Downloading GITC Uplift..." : $"Downloading GITC Uplift v{wanted}...");
        var (sourceDir, pinned) = await upliftSvc.EnsureSourceAsync(wanted).ConfigureAwait(false);
        if (sourceDir == null)
            throw new InvalidOperationException("GITC Uplift couldn't be downloaded");
        _window.ViewModel.SetNrAddonVersion(gameName, pinned, store);

        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Getting the NR DLL...");
        var sel = await DispatchAsync<string?>(_window.DispatcherQueue!, () => nrVersionCombo.SelectedItem as string).ConfigureAwait(false);
        var nrSrc = await ResolveNrDllSourceAsync(sel, dlssSvc).ConfigureAwait(false);
        if (nrSrc == null)
            CrashReporter.Log($"[NeuralRendering.Uplift] No NR DLL available for '{gameName}' — installing without it");

        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Installing GITC Uplift...");
        await Task.Run(() =>
        {
            upliftSvc.Install(installPath, card.Is32Bit, nrSrc, sourceDir, pinned);
            // OpenGL: NR can't read a multisampled back buffer at Present, so the Uplift technique must be on
            upliftSvc.EnsureTechniqueFor(installPath, card.GraphicsApi == GraphicsApiType.OpenGL);
        }).ConfigureAwait(false);

        // Shaders come from the shader sync (UpliftService.ShaderFilesFor), so they survive later syncs.
        // Awaited so the status tags see Uplift.fx straight after the install.
        await _window.ViewModel.DeployShadersForCardAsync(gameName).ConfigureAwait(false);
        CrashReporter.Log($"[NeuralRendering.Uplift] Installed {(pinned != null ? $"v{pinned} (pinned)" : "latest")} for '{gameName}'");
    }

    /// <summary>
    /// Version picked while Uplift is installed: swaps the addon, helper and shaders to it in place
    /// (the NR DLL stays), and saves the pin. "Latest" goes back to auto-updating.
    /// </summary>
    private async Task SwapUpliftVersionAsync(GameCardViewModel card, string? wanted, UpliftService upliftSvc)
    {
        var installPath = card.InstallPath!;
        var (sourceDir, pinned) = await upliftSvc.EnsureSourceAsync(wanted).ConfigureAwait(false);
        if (sourceDir == null) { CrashReporter.Log($"[NeuralRendering.Uplift] v{wanted ?? "latest"} not available — swap skipped"); return; }
        await Task.Run(() => upliftSvc.Install(installPath, card.Is32Bit, nrDllSource: null, sourceDir, pinned)).ConfigureAwait(false);
        await _window.ViewModel.DeployShadersForCardAsync(card.GameName).ConfigureAwait(false);
        CrashReporter.Log($"[NeuralRendering.Uplift] Swapped to {(pinned != null ? $"v{pinned} (pinned)" : "latest")} for '{card.GameName}'");
    }

    /// <summary>
    /// The cached nvngx_dlssnr.dll for an NR DLL Version combo value ("Latest (…)" or a version),
    /// downloading it if needed. Null if none can be had.
    /// </summary>
    private static async Task<string?> ResolveNrDllSourceAsync(string? selection, IDlssStreamlineService dlssSvc)
    {
        bool useLatest = string.IsNullOrEmpty(selection) || selection.StartsWith("Latest", StringComparison.Ordinal);
        if (useLatest) return await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
        var cached = ResolveCachedNrDllPath(selection!);
        if (cached != null && File.Exists(cached)) return cached;
        return await dlssSvc.EnsureSpecificDlssnrCachedAsync(selection!).ConfigureAwait(false)
            ?? await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
    }
}
