// MainViewModel.VulkanLayer.cs — keeps the shared Vulkan ReShade layer DLL on the chosen channel.

using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// The channel the shared Vulkan layer should run. <paramref name="preferred"/>'s own override
    /// wins, then any other Vulkan game's (they're kept the same), else Stable.
    /// </summary>
    public string ResolveVulkanLayerChannel(GameCardViewModel? preferred = null)
    {
        var overrides = new List<string?>();
        if (preferred != null)
            overrides.Add(GetReShadeChannelOverride(preferred.GameName, preferred.Source ?? ""));
        overrides.AddRange(_allCards
            .Where(c => c.RequiresVulkanInstall)
            .Select(c => GetReShadeChannelOverride(c.GameName, c.Source ?? "")));
        return VulkanLayerService.ResolveLayerChannel(overrides);
    }

    /// <summary>
    /// Copies the build for the Vulkan channel into C:\ProgramData\ReShade\ when it differs
    /// (downloads a legacy version first if needed), then refreshes every Vulkan card's version.
    /// </summary>
    public async Task SyncVulkanLayerAsync(GameCardViewModel? preferred = null)
    {
        var channel = ResolveVulkanLayerChannel(preferred);
        try
        {
            if (IsLegacyVersion(channel) && !AuxInstallService.IsLegacyVersionCached(channel))
            {
                if (!await AuxInstallService.DownloadLegacyReShadeAsync(channel, _http))
                {
                    CrashReporter.Log($"[MainViewModel.SyncVulkanLayerAsync] Legacy {channel} download failed — layer left unchanged");
                    return;
                }
            }

            bool changed = await Task.Run(() => VulkanLayerService.SyncLayerToChannel(channel));
            var version = await Task.Run(() => AuxInstallService.ReadInstalledVersion(
                VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName));
            CrashReporter.Log($"[MainViewModel.SyncVulkanLayerAsync] channel={channel}, changed={changed}, layer version={version ?? "?"}");

            void Apply()
            {
                foreach (var vCard in _allCards.Where(c => c.RequiresVulkanInstall && c.IsRsInstalled))
                {
                    vCard.RsInstalledVersion = version;
                    vCard.SetVulkanLayerInstalledVersion(version); // the Components row reads this one
                    if (vCard.RsStatus == GameStatus.UpdateAvailable) vCard.RsStatus = GameStatus.Installed;
                    vCard.NotifyAll();
                }
                NotifyUpdateButtonChanged();
            }
            if (DispatchUiAction != null) DispatchUiAction(Apply);
            else DispatcherQueue?.TryEnqueue(Apply);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            CrashReporter.Log($"[MainViewModel.SyncVulkanLayerAsync] Failed for {channel} — {ex.Message}");
        }
    }
}
