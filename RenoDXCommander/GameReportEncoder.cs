using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace RenoDXCommander;

/// <summary>
/// Builds a readable markdown game report from a GameCardViewModel,
/// saves it to disk, and places the file on the clipboard so it can
/// be pasted directly into Discord as a file attachment.
/// </summary>
public static class GameReportEncoder
{
    private static readonly string ReportsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "reports");

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
            RequestedTheme = ElementTheme.Dark,
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
                        Text = "This saves a report file and copies it to your clipboard. " +
                               "Paste it directly into Discord or attach it to a GitHub issue.",
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
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var report = BuildReport(card, vm, noteBox.Text?.Trim() ?? "");
        var markdown = FormatMarkdown(report);

        // Write to disk
        Directory.CreateDirectory(ReportsDir);
        var safeName = SanitizeFileName(card.GameName);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var fileName = $"{safeName}_{timestamp}.md";
        var filePath = Path.Combine(ReportsDir, fileName);
        await File.WriteAllTextAsync(filePath, markdown, Encoding.UTF8);

        // Place file on clipboard so Discord receives it as an attachment
        var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
        var dp = new DataPackage();
        dp.SetStorageItems(new[] { storageFile });
        dp.SetText(markdown); // fallback: plain text for apps that don't support file paste
        Clipboard.SetContent(dp);

        CrashReporter.Log($"[GameReportEncoder] Report file copied for '{card.GameName}' → {filePath}");
    }

    private static string FormatMarkdown(Dictionary<string, object?> r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# RHI Game Report — {r["gameName"]}");
        sb.AppendLine();
        sb.AppendLine($"- **Date:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **RHI Version:** {r["rhiVersion"]}");
        sb.AppendLine($"- **Store:** {r["store"]}");
        if (r["originalStoreName"] is string osn && osn != (string?)r["gameName"])
            sb.AppendLine($"- **Original Store Name:** {osn}");
        sb.AppendLine();

        sb.AppendLine("## Game Info");
        sb.AppendLine();
        sb.AppendLine($"| Field | Value |");
        sb.AppendLine($"|-------|-------|");
        sb.AppendLine($"| Install Path | `{r["installPath"]}` |");
        sb.AppendLine($"| Engine | {r["engine"]} |");
        sb.AppendLine($"| Bitness | {((bool?)r["is32Bit"] == true ? "32-bit" : "64-bit")} |");
        sb.AppendLine($"| Graphics API | {r["graphicsApi"]} |");
        sb.AppendLine($"| Detected APIs | {r["detectedApis"]} |");
        sb.AppendLine($"| Rendering Path | {r["renderingPath"]} |");
        if (r["wikiMatch"] is string wm) sb.AppendLine($"| Wiki Match | {wm} |");
        if ((bool?)r["isLumaMode"] == true) sb.AppendLine($"| Luma Mode | Yes ({r["lumaMod"]}) |");
        if ((bool?)r["ueExtended"] == true) sb.AppendLine($"| UE-Extended | Yes |");
        if ((bool?)r["nativeHdr"] == true) sb.AppendLine($"| Native HDR | Yes |");
        if ((bool?)r["isREEngine"] == true) sb.AppendLine($"| RE Engine | Yes |");
        sb.AppendLine();

        // Detected vs Corrected diff
        if (r["detected"] is Dictionary<string, object?> det && r["corrected"] is Dictionary<string, object?> cor)
        {
            var diffs = new List<string>();
            if ($"{det["is32Bit"]}" != $"{cor["is32Bit"]}") diffs.Add($"Bitness: {(det["is32Bit"] is true ? "32-bit" : "64-bit")} → {(cor["is32Bit"] is true ? "32-bit" : "64-bit")}");
            if ($"{det["graphicsApi"]}" != $"{cor["graphicsApi"]}") diffs.Add($"API: {det["graphicsApi"]} → {cor["graphicsApi"]}");
            if (diffs.Count > 0)
            {
                sb.AppendLine("## Corrections (Detected → Overridden)");
                sb.AppendLine();
                foreach (var d in diffs) sb.AppendLine($"- {d}");
                sb.AppendLine();
            }
        }

        // Components
        if (r["components"] is List<Dictionary<string, string?>> comps)
        {
            sb.AppendLine("## Components");
            sb.AppendLine();
            sb.AppendLine("| Component | Status | Version | Filename |");
            sb.AppendLine("|-----------|--------|---------|----------|");
            foreach (var c in comps)
                sb.AppendLine($"| {c["name"]} | {c["status"]} | {c["version"]} | {c["filename"]} |");
            sb.AppendLine();
        }

        // Overrides
        if (r["overrides"] is Dictionary<string, object?> ov)
        {
            var ovLines = new List<string>();
            if (ov["bitnessOverride"] is string bov && bov != "Auto") ovLines.Add($"Bitness: {bov}");
            if (ov["apiOverride"] is string aov && aov != "Auto") ovLines.Add($"API: {aov}");
            if (ov["folderOverride"] is string fov && !string.IsNullOrEmpty(fov)) ovLines.Add($"Folder: `{fov}`");
            if (ov["dllOverride"] is string dov && !string.IsNullOrEmpty(dov)) ovLines.Add($"ReShade DLL: {dov}");
            if (ov["dcDllOverride"] is string dcov && !string.IsNullOrEmpty(dcov)) ovLines.Add($"DC DLL: {dcov}");
            if (ov["wikiNameOverride"] is string wnov && !string.IsNullOrEmpty(wnov)) ovLines.Add($"Wiki Name: {wnov}");
            if ((bool?)ov["wikiExcluded"] == true) ovLines.Add("Wiki Excluded: Yes");
            if (ov["shaderMode"] is string sm && !string.IsNullOrEmpty(sm)) ovLines.Add($"Shader Mode: {sm}");
            if (ov["addonMode"] is string am && !string.IsNullOrEmpty(am)) ovLines.Add($"Addon Mode: {am}");

            if (ovLines.Count > 0)
            {
                sb.AppendLine("## Overrides");
                sb.AppendLine();
                foreach (var l in ovLines) sb.AppendLine($"- {l}");
                sb.AppendLine();
            }
        }

        // User note
        if (r["userNote"] is string note && !string.IsNullOrEmpty(note))
        {
            sb.AppendLine("## User Note");
            sb.AppendLine();
            sb.AppendLine(note);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim();
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
