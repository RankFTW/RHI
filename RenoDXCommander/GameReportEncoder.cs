using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace RenoDXCommander;

/// <summary>
/// Builds a base64-encoded game report from a GameCardViewModel,
/// shows a dialog for an optional user note, and copies to clipboard.
/// </summary>
public static class GameReportEncoder
{
    public static async Task ShowAndCopyAsync(XamlRoot xamlRoot, GameCardViewModel card, MainViewModel vm)
    {
        // Gatekeep: ask user to correct overrides first
        var gateDlg = new ContentDialog
        {
            Title = "Before you submit",
            Content = new TextBlock
            {
                Text = "Please use the overrides on this panel to correct any wrong values " +
                       "(bitness, graphics API, game name, etc.) before generating a report. " +
                       "This helps us update the manifest faster.\n\n" +
                       "Have you corrected everything you can?",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            },
            PrimaryButtonText = "Yes, continue",
            CloseButtonText = "Go back",
            XamlRoot = xamlRoot,
        };

        var gateResult = await gateDlg.ShowAsync();
        if (gateResult != ContentDialogResult.Primary) return;

        // Show dialog with optional note
        var noteBox = new TextBox
        {
            PlaceholderText = "Describe the issue (optional)",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            MaxHeight = 160,
        };

        var dlg = new ContentDialog
        {
            Title = "Copy Game Report",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "This copies a diagnostic code to your clipboard containing " +
                               "game info, overrides, and component status. " +
                               "Paste it in Discord or a GitHub issue.",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12,
                        Opacity = 0.7,
                    },
                    noteBox,
                },
            },
            PrimaryButtonText = "Copy to Clipboard",
            CloseButtonText = "Cancel",
            XamlRoot = xamlRoot,
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var report = BuildReport(card, vm, noteBox.Text?.Trim() ?? "");
        var json = JsonSerializer.Serialize(report);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        var dp = new DataPackage();
        dp.SetText(base64);
        Clipboard.SetContent(dp);

        CrashReporter.Log($"[GameReportEncoder] Report copied for '{card.GameName}' ({base64.Length} chars)");
    }

    private static Dictionary<string, object?> BuildReport(GameCardViewModel card, MainViewModel vm, string userNote)
    {
        var gameName = card.GameName;
        var gns = vm.GameNameServiceInstance;

        // Readable API strings (these reflect overrides already applied to the card)
        var apiStr = card.GraphicsApi.ToString();
        var detectedApisStr = card.DetectedApis.Count > 0
            ? string.Join(", ", card.DetectedApis.Select(a => a.ToString()))
            : card.GraphicsApiLabel;

        // ── Raw auto-detected values (before user overrides) ─────────────────
        // Bitness: re-detect from PE header to get the raw value
        bool autoIs32Bit = card.Is32Bit; // fallback
        if (!string.IsNullOrEmpty(card.InstallPath))
        {
            var rawMachine = vm.PeHeaderServiceInstance.DetectGameArchitecture(card.InstallPath);
            autoIs32Bit = rawMachine == Services.MachineType.I386;
        }

        // Graphics API: if user has an API override, the card already shows the overridden value.
        // Re-detect the raw API to get the original auto-detected value.
        var autoApiStr = apiStr; // fallback
        if (gns.ApiOverrides.ContainsKey(gameName) && !string.IsNullOrEmpty(card.InstallPath))
        {
            // Temporarily ignore the override by detecting fresh
            var rawApi = vm.DetectGraphicsApi(card.InstallPath, Models.EngineType.Unknown, null);
            autoApiStr = rawApi.ToString();
        }

        // Detected (auto) values — raw, before any user overrides
        var detected = new Dictionary<string, object?>
        {
            ["installPath"] = card.InstallPath,
            ["engine"] = card.EngineHint,
            ["is32Bit"] = autoIs32Bit,
            ["graphicsApi"] = autoApiStr,
            ["detectedApis"] = detectedApisStr,
            ["wikiMatch"] = card.NameUrl != null ? card.GameName : null,
        };

        // Corrected (user override) values
        var bitnessOv = gns.BitnessOverrides.TryGetValue(gameName, out var bv) ? bv : "Auto";
        var apiOv = gns.ApiOverrides.TryGetValue(gameName, out var av) ? string.Join(", ", av) : "Auto";
        var folderOv = gns.FolderOverrides.TryGetValue(gameName, out var fv) ? fv : "";
        var wikiOv = vm.GetNameMapping(gameName);
        var dllOv = card.DllOverrideEnabled ? (card.RsInstalledFile ?? "") : "";
        var dcDllOv = "";

        var corrected = new Dictionary<string, object?>
        {
            ["installPath"] = card.InstallPath,
            ["engine"] = card.EngineHint,
            ["is32Bit"] = card.Is32Bit,
            ["graphicsApi"] = apiStr,
            ["detectedApis"] = detectedApisStr,
            ["wikiMatch"] = card.NameUrl != null ? card.GameName : null,
            ["bitnessOverride"] = bitnessOv,
            ["apiOverride"] = apiOv,
            ["folderOverride"] = folderOv,
            ["dllOverride"] = dllOv,
            ["dcDllOverride"] = dcDllOv,
            ["wikiNameOverride"] = wikiOv,
            ["renderingPath"] = card.RequiresVulkanInstall ? "Vulkan" : "DirectX",
        };

        // Components
        var components = new List<Dictionary<string, string?>>
        {
            new() { ["name"] = "ReShade", ["status"] = card.RsStatusText, ["version"] = card.RsInstalledVersion ?? "", ["filename"] = card.RsInstalledFile ?? "" },
            new() { ["name"] = "RenoDX", ["status"] = card.RdxStatusText, ["version"] = "", ["filename"] = card.InstalledAddonFileName },
            new() { ["name"] = "ReLimiter", ["status"] = card.UlStatusText, ["version"] = "", ["filename"] = "" },
            new() { ["name"] = "Display Commander", ["status"] = card.DcStatusText, ["version"] = "", ["filename"] = "" },
        };

        if (card.IsREEngineGame)
            components.Add(new() { ["name"] = "RE Framework", ["status"] = card.RefStatusText, ["version"] = "", ["filename"] = "" });

        if (card.IsLumaMode)
            components.Add(new() { ["name"] = "Luma", ["status"] = card.LumaStatusText, ["version"] = "", ["filename"] = "" });

        // Overrides
        var shaderMode = vm.GetPerGameShaderMode(gameName);
        var addonMode = vm.GetPerGameAddonMode(gameName);
        var overrides = new Dictionary<string, object?>
        {
            ["shaderMode"] = shaderMode,
            ["addonMode"] = addonMode,
            ["bitnessOverride"] = bitnessOv,
            ["apiOverride"] = apiOv,
            ["folderOverride"] = folderOv,
            ["dllOverride"] = dllOv,
            ["dcDllOverride"] = dcDllOv,
            ["wikiNameOverride"] = wikiOv,
            ["wikiExcluded"] = vm.IsWikiExcluded(gameName),
            ["updateExcludedRS"] = vm.IsUpdateAllExcludedReShade(gameName),
            ["updateExcludedRDX"] = vm.IsUpdateAllExcludedRenoDx(gameName),
            ["updateExcludedUL"] = vm.IsUpdateAllExcludedUl(gameName),
            ["updateExcludedDC"] = vm.IsUpdateAllExcludedDc(gameName),
        };

        // Addons
        var enabledAddons = vm.Settings.EnabledGlobalAddons;
        List<string>? perGameAddons = null;
        gns.PerGameAddonSelection.TryGetValue(gameName, out perGameAddons);
        var addons = new Dictionary<string, object?>
        {
            ["mode"] = addonMode,
            ["enabled"] = enabledAddons,
            ["perGameSelection"] = perGameAddons,
        };

        // Original store name
        var originalName = gns.GetOriginalStoreName(gameName);

        return new Dictionary<string, object?>
        {
            ["gameName"] = gameName,
            ["originalStoreName"] = originalName ?? gameName,
            ["installPath"] = card.InstallPath,
            ["store"] = card.Source,
            ["engine"] = card.EngineHint,
            ["is32Bit"] = card.Is32Bit,
            ["graphicsApi"] = apiStr,
            ["detectedApis"] = detectedApisStr,
            ["renderingPath"] = card.RequiresVulkanInstall ? "Vulkan" : "DirectX",
            ["isLumaMode"] = card.IsLumaMode,
            ["lumaMod"] = card.LumaMod?.Name,
            ["ueExtended"] = card.UseUeExtended,
            ["nativeHdr"] = card.IsNativeHdrGame,
            ["isREEngine"] = card.IsREEngineGame,
            ["isBlacklisted"] = false,
            ["isHidden"] = card.IsHidden,
            ["isFavourite"] = card.IsFavourite,
            ["wikiMatch"] = card.NameUrl != null ? card.GameName : null,
            ["wikiExcluded"] = vm.IsWikiExcluded(gameName),
            ["wikiNameOverride"] = wikiOv,
            ["detected"] = detected,
            ["corrected"] = corrected,
            ["components"] = components,
            ["overrides"] = overrides,
            ["addons"] = addons,
            ["userNote"] = userNote,
            ["rhiVersion"] = vm.UpdateServiceInstance.CurrentVersion.ToString(),
            ["timestamp"] = DateTime.UtcNow.ToString("O"),
        };
    }
}
