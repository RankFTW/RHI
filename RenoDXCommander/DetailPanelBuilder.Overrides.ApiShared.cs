// DetailPanelBuilder.Overrides.ApiShared.cs — the Graphics API override, shared by the game settings ⚙ and the ReShade ⚙
// (two-places rule, like DetailPanelBuilder.Overrides.RsShared.cs): both read the saved value with GetApiDisplay and
// change it with ApplyApiOverride, so a change in one shows in the other.

using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    internal static readonly string[] ApiItems = { "Auto", "DirectX8", "DirectX9", "DirectX10", "DirectX11", "DirectX12", "Vulkan", "OpenGL" };

    /// <summary>The saved override as a dropdown item ("Auto" when none).</summary>
    internal string GetApiDisplay(GameCardViewModel card)
    {
        var saved = _window.ViewModel.GetApiOverride(card.GameName, card.Source);
        if (saved == null || saved.Count == 0) return "Auto";
        // Several saved APIs (older data): the most specific / newest wins, as before
        foreach (var api in new[] { "DirectX12", "DirectX11", "Vulkan", "OpenGL", "DirectX10", "DirectX9", "DirectX8" })
            if (saved.Contains(api, StringComparer.OrdinalIgnoreCase)) return api;
        return "Auto";
    }

    /// <summary>
    /// Saves the override ("Auto" clears it), updates the card's APIs and, when the game moves between DirectX and
    /// Vulkan, removes the ReShade install of the other kind. <paramref name="rebuildNow"/>: rebuild the detail panel
    /// straight away (Reset); the cogs pass false and refreshes its rows instead, so the open ⚙ stays.
    /// Returns true when ReShade was removed because of the switch.
    /// </summary>
    internal bool ApplyApiOverride(GameCardViewModel card, string capturedName, string selected, bool rebuildNow)
    {
        List<string>? apiEnumNames = selected is "DirectX8" or "DirectX9" or "DirectX10" or "DirectX11" or "DirectX12" or "Vulkan" or "OpenGL"
            ? new() { selected }
            : null; // "Auto" clears the override

        _window.ViewModel.SetApiOverride(capturedName, apiEnumNames, card.Source);

        var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
            c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
        if (targetCard == null) return false;

        bool wasVulkan = targetCard.RequiresVulkanInstall;
        if (apiEnumNames != null)
        {
            var newApis = new HashSet<GraphicsApiType>();
            foreach (var name in apiEnumNames)
                if (Enum.TryParse<GraphicsApiType>(name, out var apiType)) newApis.Add(apiType);
            targetCard.DetectedApis = newApis;
        }
        else
        {
            // "Auto": re-detect from scanning
            targetCard.DetectedApis = _window.ViewModel._DetectAllApisForCard(targetCard.InstallPath, capturedName, targetCard.Source);
        }
        targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
        targetCard.GraphicsApi = _window.ViewModel.DetectGraphicsApi(
            targetCard.InstallPath, EngineType.Unknown, capturedName, targetCard.Source);

        // Now that GraphicsApi is resolved, check if Vulkan state changed
        bool willBeVulkan = targetCard.GraphicsApi == GraphicsApiType.Vulkan && !targetCard.IsDualApiGame;
        bool removedReShade = false;

        // Switching TO Vulkan: a DX ReShade DLL (dxgi.dll, d3d12.dll...) left behind conflicts with the Vulkan layer
        if (!wasVulkan && willBeVulkan && targetCard.IsRsInstalled)
        {
            CrashReporter.Log($"[ApiOverride] Switching '{capturedName}' to Vulkan — uninstalling DX ReShade ({targetCard.RsInstalledFile})");
            _window.ViewModel.UninstallReShade(targetCard);
            removedReShade = true;
        }
        // Switching FROM Vulkan: remove the Vulkan ReShade footprint
        else if (wasVulkan && !willBeVulkan && targetCard.VulkanRsIniExists)
        {
            CrashReporter.Log($"[ApiOverride] Switching '{capturedName}' from Vulkan — uninstalling Vulkan ReShade");
            _window.ViewModel.UninstallVulkanReShadeCommand.Execute(targetCard);
            removedReShade = true;
        }

        // Re-evaluate Luma injection inline (synchronous) so LumaMod is updated before the panel rebuilds
        _window.ViewModel.ReevaluateLumaForCard(targetCard);
        targetCard.NotifyAll();
        CrashReporter.Log($"[ApiOverride] '{capturedName}' Graphics API = {selected}");

        if (rebuildNow)
        {
            // RequestReselect is a no-op when the game is already selected: rebuild directly
            _window.PopulateDetailPanel(targetCard);
            _window.RequestReselect(capturedName);
        }
        else
        {
            UpdateDetailComponentRows(targetCard);
            QueueOverridesRebuild(capturedName);
        }
        return removedReShade;
    }
}
