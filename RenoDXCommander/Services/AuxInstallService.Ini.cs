// AuxInstallService.Ini.cs — INI parsing, writing, merging, and preset copying
namespace RenoDXCommander.Services;

public partial class AuxInstallService
{
    /// <summary>
    /// Merges the template reshade.ini into the game directory's existing reshade.ini.
    /// Template keys always overwrite existing values (template wins). Sections and keys
    /// already in the game's INI that are not in the template are preserved untouched.
    /// If no reshade.ini exists in the game folder, the template is copied as-is.
    /// </summary>
    public static void MergeRsIni(string gameDir, string? screenshotSavePath = null, string? overlayHotkey = null, string? screenshotHotkey = null, string? gameName = null, int peakNits = 0)
    {
        // Use global setting if no explicit value passed
        if (peakNits <= 0) peakNits = GlobalPeakNits;
        // Determine which template to use — RDR2/Max Payne 3 use a dedicated template
        var templatePath = (gameName != null && IsRdr2(gameName) && File.Exists(RsRdr2IniPath))
            ? RsRdr2IniPath
            : RsIniPath;

        if (!File.Exists(templatePath))
            throw new FileNotFoundException("reshade.ini not found in inis folder.", templatePath);

        var gamePath = Path.Combine(gameDir, "ReShade.ini");

        if (!File.Exists(gamePath))
        {
            // No existing INI — just copy the template
            File.Copy(templatePath, gamePath, overwrite: true);

            // Strip Vulkan-only PreprocessorDefinitions when deploying RDR2 template to DX games
            if (templatePath == RsRdr2IniPath)
                StripPreprocessorDefinitions(gamePath);

            // Apply screenshot path to the freshly copied file
            if (screenshotSavePath != null)
                ApplyScreenshotPath(gamePath, screenshotSavePath);

            // Apply overlay hotkey if non-default
            if (overlayHotkey != null && !HotkeyManager.IsDefaultHotkey(overlayHotkey))
                ApplyOverlayHotkey(gamePath, overlayHotkey);

            // Apply screenshot hotkey if non-default
            if (screenshotHotkey != null && screenshotHotkey != "44,0,0,0")
                ApplyScreenshotHotkey(gamePath, screenshotHotkey);

            // Apply peak nits if configured
            if (peakNits > 0)
                ApplyPeakNits(gamePath, peakNits);
            return;
        }

        // Parse both files
        var gameIni     = ParseIni(File.ReadAllLines(gamePath));
        var templateIni = ParseIni(File.ReadAllLines(templatePath));

        // Merge: template keys overwrite, game-only keys preserved
        foreach (var (section, templateKeys) in templateIni)
        {
            if (!gameIni.TryGetValue(section, out var gameKeys))
            {
                // Entire section is new — add it
                gameIni[section] = new OrderedDict(templateKeys);
            }
            else
            {
                // Section exists — overwrite matching keys, add new ones
                foreach (var (key, value) in templateKeys)
                    gameKeys[key] = value;
            }
        }

        // Write merged INI back
        WriteIni(gamePath, gameIni);

        // Strip Vulkan-only PreprocessorDefinitions when deploying RDR2 template to DX games
        if (templatePath == RsRdr2IniPath)
            StripPreprocessorDefinitions(gamePath);

        // Apply screenshot path after merge
        if (screenshotSavePath != null)
            ApplyScreenshotPath(gamePath, screenshotSavePath);

        // Apply overlay hotkey if non-default
        if (overlayHotkey != null && !HotkeyManager.IsDefaultHotkey(overlayHotkey))
            ApplyOverlayHotkey(gamePath, overlayHotkey);

        // Apply screenshot hotkey if non-default
        if (screenshotHotkey != null && screenshotHotkey != "44,0,0,0")
            ApplyScreenshotHotkey(gamePath, screenshotHotkey);

        // Apply peak nits if configured
        if (peakNits > 0)
            ApplyPeakNits(gamePath, peakNits);
    }

    /// <summary>
    /// Merges the Vulkan-specific reshade.vulkan.ini template into the game directory
    /// as reshade.ini. Uses the same merge logic as <see cref="MergeRsIni"/> — template
    /// keys overwrite, game-only keys are preserved. Falls back to the standard
    /// reshade.ini if the Vulkan template doesn't exist.
    /// For Red Dead Redemption 2, uses the dedicated reshade.rdr2.ini template instead.
    /// </summary>
    public static void MergeRsVulkanIni(string gameDir, string? gameName = null, string? screenshotSavePath = null, string? overlayHotkey = null, string? screenshotHotkey = null, int peakNits = 0)
    {
        // Use global setting if no explicit value passed
        if (peakNits <= 0) peakNits = GlobalPeakNits;
        // Red Dead Redemption 2 uses a dedicated ini template
        string templatePath;
        if (gameName != null && IsRdr2(gameName) && File.Exists(RsRdr2IniPath))
            templatePath = RsRdr2IniPath;
        else
            templatePath = File.Exists(RsVulkanIniPath) ? RsVulkanIniPath : RsIniPath;

        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Neither reshade.vulkan.ini nor reshade.ini found in inis folder.", templatePath);

        var gamePath = Path.Combine(gameDir, "ReShade.ini");

        if (!File.Exists(gamePath))
        {
            File.Copy(templatePath, gamePath, overwrite: true);

            // Apply screenshot path to the freshly copied file
            if (screenshotSavePath != null)
                ApplyScreenshotPath(gamePath, screenshotSavePath);

            // Apply overlay hotkey if non-default
            if (overlayHotkey != null && !HotkeyManager.IsDefaultHotkey(overlayHotkey))
                ApplyOverlayHotkey(gamePath, overlayHotkey);

            // Apply screenshot hotkey if non-default
            if (screenshotHotkey != null && screenshotHotkey != "44,0,0,0")
                ApplyScreenshotHotkey(gamePath, screenshotHotkey);

            // Apply peak nits if configured
            if (peakNits > 0)
                ApplyPeakNits(gamePath, peakNits);
            return;
        }

        var gameIni     = ParseIni(File.ReadAllLines(gamePath));
        var templateIni = ParseIni(File.ReadAllLines(templatePath));

        foreach (var (section, templateKeys) in templateIni)
        {
            if (!gameIni.TryGetValue(section, out var gameKeys))
            {
                gameIni[section] = new OrderedDict(templateKeys);
            }
            else
            {
                foreach (var (key, value) in templateKeys)
                {
                    gameKeys[key] = value;
                }
            }
        }

        WriteIni(gamePath, gameIni);

        // Apply screenshot path after merge
        if (screenshotSavePath != null)
            ApplyScreenshotPath(gamePath, screenshotSavePath);

        // Apply overlay hotkey if non-default
        if (overlayHotkey != null && !HotkeyManager.IsDefaultHotkey(overlayHotkey))
            ApplyOverlayHotkey(gamePath, overlayHotkey);

        // Apply screenshot hotkey if non-default
        if (screenshotHotkey != null && screenshotHotkey != "44,0,0,0")
            ApplyScreenshotHotkey(gamePath, screenshotHotkey);

        // Apply peak nits if configured
        if (peakNits > 0)
            ApplyPeakNits(gamePath, peakNits);

        // ── Propagate to ReShade2/3/4... ini files ────────────────────────────
        // Some Vulkan games (e.g. KOA: Reckoning) create multiple swapchains
        // (main window + tiny UI overlay). ReShade assigns each swapchain its own
        // numbered config file (ReShade2.ini, ReShade3.ini, etc.). Sync the same
        // [ADDON], [GENERAL], [SCREENSHOT], and [INPUT] hotkeys so all instances
        // behave consistently. Hotkeys in numbered files are always overwritten with
        // the primary reshade.ini value — these files are ephemeral ReShade state.
        try
        {
            var numberedInis = Directory.GetFiles(gameDir, "reshade*.ini")
                .Where(f =>
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    return name.Length > "reshade".Length
                           && name.StartsWith("reshade", StringComparison.OrdinalIgnoreCase)
                           && int.TryParse(name["reshade".Length..], out _);
                })
                .ToList();

            if (numberedInis.Count > 0)
            {
                // Re-read gamePath so we propagate the final merged state
                var mergedIni = ParseIni(File.ReadAllLines(gamePath));
                string[] sectionsToSync = ["ADDON", "GENERAL", "SCREENSHOT"];

                foreach (var numberedPath in numberedInis)
                {
                    try
                    {
                        var numberedIni = File.Exists(numberedPath)
                            ? ParseIni(File.ReadAllLines(numberedPath))
                            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

                        foreach (var section in sectionsToSync)
                        {
                            if (mergedIni.TryGetValue(section, out var srcKeys))
                                numberedIni[section] = new OrderedDict(srcKeys);
                        }

                        // Always sync hotkeys from primary ini — numbered inis are ephemeral
                        // and the user's hotkey preference should be consistent across all swapchains.
                        if (mergedIni.TryGetValue("INPUT", out var inputKeys))
                        {
                            if (!numberedIni.ContainsKey("INPUT"))
                                numberedIni["INPUT"] = new OrderedDict();
                            if (inputKeys.TryGetValue("KeyOverlay", out var ko))
                                numberedIni["INPUT"]["KeyOverlay"] = ko;
                            if (inputKeys.TryGetValue("KeyScreenshot", out var ks))
                                numberedIni["INPUT"]["KeyScreenshot"] = ks;
                        }

                        WriteIni(numberedPath, numberedIni);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[AuxInstallService.MergeRsVulkanIni] Failed to sync '{Path.GetFileName(numberedPath)}' — {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.MergeRsVulkanIni] Numbered ini sync failed — {ex.Message}");
        }
    }

    internal static bool IsRdr2(string gameName) =>
        gameName.Contains("Red Dead Redemption 2", StringComparison.OrdinalIgnoreCase) ||
        gameName.Equals("RDR2", StringComparison.OrdinalIgnoreCase) ||
        gameName.Contains("Max Payne 3", StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies reshade.ini from the inis folder to the given game directory (full overwrite, no merge).</summary>
    public static void CopyRsIni(string gameDir)
    {
        if (!File.Exists(RsIniPath))
            throw new FileNotFoundException("reshade.ini not found in inis folder.", RsIniPath);
        File.Copy(RsIniPath, Path.Combine(gameDir, "ReShade.ini"), overwrite: true);
    }

    /// <summary>
    /// Copies ReShadePreset.ini from the inis folder to the given game directory if the file exists.
    /// Silent no-op when the file is absent — the preset is optional.
    /// </summary>
    public static void CopyRsPresetIniIfPresent(string gameDir)
    {
        if (!File.Exists(RsPresetIniPath)) return;
        try
        {
            File.Copy(RsPresetIniPath, Path.Combine(gameDir, "ReShadePreset.ini"), overwrite: true);
            CrashReporter.Log($"[AuxInstallService.CopyRsPresetIniIfPresent] Copied to {gameDir}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.CopyRsPresetIniIfPresent] Failed for '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Copies relimiter.ini from the inis folder to the game directory (addon deploy path)
    /// only when the file does not already exist at the destination. Never throws.
    /// </summary>
    public static void DeployUlIniIfAbsent(string gameInstallPath)
    {
        try
        {
            var deployPath = ModInstallService.GetAddonDeployPath(gameInstallPath);
            var destFile = Path.Combine(deployPath, "relimiter.ini");

            if (File.Exists(destFile))
                return;

            if (!File.Exists(UlIniPath))
            {
                CrashReporter.Log($"[AuxInstallService.DeployUlIniIfAbsent] Source relimiter.ini not found at '{UlIniPath}' — skipping");
                return;
            }

            File.Copy(UlIniPath, destFile);
            CrashReporter.Log($"[AuxInstallService.DeployUlIniIfAbsent] Deployed relimiter.ini to '{deployPath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.DeployUlIniIfAbsent] Failed for '{gameInstallPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Copies DisplayCommander.ini from the inis folder to the game directory (addon deploy path)
    /// only when the file does not already exist at the destination. Never throws.
    /// </summary>
    public static void DeployDcIniIfAbsent(string gameInstallPath)
    {
        try
        {
            var deployPath = ModInstallService.GetAddonDeployPath(gameInstallPath);
            var destFile = Path.Combine(deployPath, "DisplayCommander.ini");

            if (File.Exists(destFile))
                return;

            if (!File.Exists(DcIniPath))
            {
                CrashReporter.Log($"[AuxInstallService.DeployDcIniIfAbsent] Source DisplayCommander.ini not found at '{DcIniPath}' — skipping");
                return;
            }

            File.Copy(DcIniPath, destFile);
            CrashReporter.Log($"[AuxInstallService.DeployDcIniIfAbsent] Deployed DisplayCommander.ini to '{deployPath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.DeployDcIniIfAbsent] Failed for '{gameInstallPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Copies relimiter.ini from the inis folder to the game directory (addon deploy path).
    /// </summary>
    public static void CopyUlIni(string gameInstallPath)
    {
        if (!File.Exists(UlIniPath))
            throw new FileNotFoundException("relimiter.ini not found in inis folder.", UlIniPath);
        var deployPath = ModInstallService.GetAddonDeployPath(gameInstallPath);
        File.Copy(UlIniPath, Path.Combine(deployPath, "relimiter.ini"), overwrite: true);
    }

    /// <summary>
    /// Copies DisplayCommander.ini from the inis folder to the game directory (addon deploy path).
    /// </summary>
    public static void CopyDcIni(string gameInstallPath)
    {
        if (!File.Exists(DcIniPath))
            throw new FileNotFoundException("DisplayCommander.ini not found in inis folder.", DcIniPath);
        var deployPath = ModInstallService.GetAddonDeployPath(gameInstallPath);
        File.Copy(DcIniPath, Path.Combine(deployPath, "DisplayCommander.ini"), overwrite: true);
    }

    // ── Native HDR / UE-Extended [renodx] section ───────────────────────────────

    /// <summary>
    /// Ensures the [renodx] section exists in the game's reshade.ini with all Native HDR
    /// settings disabled. This is required for games flagged as nativeHdrGames or ueExtendedGames
    /// when UE-Extended is installed, so the user doesn't need to configure these manually.
    /// If the [renodx] section already exists with the correct keys, no changes are made.
    /// Other sections in the file are preserved untouched.
    /// </summary>
    /// <param name="gameDir">Game install directory.</param>
    /// <param name="usesSdrPath">When true writes Set_Path=1 (SDR upgrade); false writes Set_Path=0 (HDR).</param>
    /// <param name="upgradeOverride">
    /// Optional db-driven upgrade: (key, value) pair to force-write into the [renodx] section,
    /// overwriting any existing value. e.g. ("Upgrade_B8G8R8A8_TYPELESS", "1").
    /// Used when the RenoDX db specifies Method="upgrade" for this game.
    /// </param>
    public static void ApplyRenoDxNativeHdrSettings(string gameDir, bool usesSdrPath = false,
        (string Key, string Value)? upgradeOverride = null)
    {
        var iniFilePath = Path.Combine(gameDir, "reshade.ini");
        if (!File.Exists(iniFilePath)) return;

        try
        {
            var ini = ParseIni(File.ReadAllLines(iniFilePath));
            const string section = "renodx";

            var requiredKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DumpLUTShaders"] = "0",
                ["ForceBorderless"] = "1",
                ["FxUpgradeRender"] = "1",
                ["PreventFullscreen"] = "1",
                ["SettingsMode"] = "2",
                ["Set_Path"] = usesSdrPath ? "1" : "0",
                ["Upgrade_B10G10R10A2_UNORM"] = "",
                ["Upgrade_B8G8R8A8_TYPELESS"] = "",
                ["Upgrade_B8G8R8A8_UNORM"] = "",
                ["Upgrade_B8G8R8A8_UNORM_SRGB"] = "",
                ["Upgrade_CopyDestinations"] = "",
                ["Upgrade_R10G10B10A2_TYPELESS"] = "",
                ["Upgrade_R10G10B10A2_UNORM"] = "",
                ["Upgrade_R11G11B10_FLOAT"] = "",
                ["Upgrade_R16G16B16A16_TYPELESS"] = "",
                ["Upgrade_R8G8B8A8_SNORM"] = "",
                ["Upgrade_R8G8B8A8_TYPELESS"] = "",
                ["Upgrade_R8G8B8A8_UNORM"] = "",
                ["Upgrade_R8G8B8A8_UNORM_SRGB"] = "",
                ["Upgrade_SwapChainCompatibility"] = "",
                ["Upgrade_UseSCRGB"] = "",
            };

            bool changed = false;

            if (!ini.TryGetValue(section, out var existingKeys))
            {
                existingKeys = new OrderedDict(requiredKeys);
                ini[section] = existingKeys;
                changed = true;
            }
            else
            {
                // Only add keys that are missing — never overwrite user-modified values
                foreach (var (key, value) in requiredKeys)
                {
                    if (!existingKeys.ContainsKey(key))
                    {
                        existingKeys[key] = value;
                        changed = true;
                    }
                }
            }

            // Force-write the db-specified upgrade key, overwriting any stale existing value.
            // This is the only key we authoratively set — all others respect user modifications.
            if (upgradeOverride.HasValue
                && !string.IsNullOrEmpty(upgradeOverride.Value.Key)
                && !string.IsNullOrEmpty(upgradeOverride.Value.Value))
            {
                var (ovKey, ovVal) = upgradeOverride.Value;
                if (!existingKeys.TryGetValue(ovKey, out var existing) || existing != ovVal)
                {
                    existingKeys[ovKey] = ovVal;
                    changed = true;
                    CrashReporter.Log($"[AuxInstallService.ApplyRenoDxNativeHdrSettings] DB upgrade override: {ovKey}={ovVal} for '{gameDir}'");
                }
            }

            if (!changed) return; // All keys already present and correct — no write needed

            WriteIni(iniFilePath, ini);
            CrashReporter.Log($"[AuxInstallService.ApplyRenoDxNativeHdrSettings] Applied [renodx] section to '{iniFilePath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.ApplyRenoDxNativeHdrSettings] Failed for '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Pre-populates [renodx] section keys with empty values for generic UE or Unity addons.
    /// The addon fills in game-specific defaults on first launch. Only adds missing keys.
    /// </summary>
    public static void ApplyRenodxKeyPlaceholders(string gameDir, string addonType)
    {
        var iniFilePath = Path.Combine(gameDir, "reshade.ini");
        if (!File.Exists(iniFilePath)) return;

        string[] keys;
        if (addonType == "Unity")
        {
            keys = new[]
            {
                "Blit_Copy_Hack", "DumpUberShaders", "ForceBorderless", "Force_Pipeline_Cloning",
                "PreventFullscreen", "Scaling_Offset", "SettingsMode", "Swapchain_Encoding", "Tonemap_Offset",
                "Upgrade_CopyDestinations", "Upgrade_R10G10B10A2_TYPELESS", "Upgrade_R10G10B10A2_UNORM",
                "Upgrade_R11G11B10_FLOAT", "Upgrade_R16G16B16A16_TYPELESS",
                "Upgrade_R8G8B8A8_TYPELESS", "Upgrade_R8G8B8A8_UNORM", "Upgrade_R8G8B8A8_UNORM_SRGB",
                "Upgrade_UseSCRGB", "Use_Resource_Cloning", "Use_Swapchain_Proxy",
            };
        }
        else // Generic UE
        {
            keys = new[]
            {
                "DumpLUTShaders", "ForceBorderless", "PreventFullscreen", "SettingsMode",
                "Upgrade_B10G10R10A2_UNORM", "Upgrade_B8G8R8A8_TYPELESS", "Upgrade_B8G8R8A8_UNORM",
                "Upgrade_B8G8R8A8_UNORM_SRGB", "Upgrade_CopyDestinations",
                "Upgrade_R10G10B10A2_TYPELESS", "Upgrade_R10G10B10A2_UNORM",
                "Upgrade_R11G11B10_FLOAT", "Upgrade_R16G16B16A16_TYPELESS",
                "Upgrade_R8G8B8A8_SNORM", "Upgrade_R8G8B8A8_TYPELESS",
                "Upgrade_R8G8B8A8_UNORM", "Upgrade_R8G8B8A8_UNORM_SRGB",
                "Upgrade_SwapChainCompatibility", "Upgrade_UseSCRGB",
            };
        }

        try
        {
            var ini = ParseIni(File.ReadAllLines(iniFilePath));
            const string section = "renodx";

            if (!ini.TryGetValue(section, out var existingKeys))
            {
                var newSection = new OrderedDict();
                foreach (var key in keys)
                    newSection[key] = "";
                ini[section] = newSection;
            }
            else
            {
                bool changed = false;
                foreach (var key in keys)
                {
                    if (!existingKeys.ContainsKey(key))
                    {
                        existingKeys[key] = "";
                        changed = true;
                    }
                }
                if (!changed) return;
            }

            WriteIni(iniFilePath, ini);
            CrashReporter.Log($"[AuxInstallService.ApplyRenodxKeyPlaceholders] Applied {addonType} placeholders to '{iniFilePath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.ApplyRenodxKeyPlaceholders] Failed for '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the [renodx] section from reshade.ini when UE-Extended is uninstalled.
    /// </summary>
    public static void RemoveRenoDxNativeHdrSettings(string gameDir)
    {
        var iniFilePath = Path.Combine(gameDir, "reshade.ini");
        if (!File.Exists(iniFilePath)) return;

        try
        {
            var ini = ParseIni(File.ReadAllLines(iniFilePath));
            if (ini.Remove("renodx"))
            {
                WriteIni(iniFilePath, ini);
                CrashReporter.Log($"[AuxInstallService.RemoveRenoDxNativeHdrSettings] Removed [renodx] section from '{iniFilePath}'");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.RemoveRenoDxNativeHdrSettings] Failed for '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the HDR settings from Engine.ini that were deployed by ApplyEngineIniHdrSettings.
    /// Removes read-only, filters out the specific keys, removes empty section headers,
    /// and deletes the file if nothing meaningful remains.
    /// </summary>
    public static void RemoveEngineIniHdrSettings(string installPath, string? projectNameOverride = null, string? gameName = null, string? store = null)
    {
        try
        {
            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName, store);
            if (configDir == null) return;

            var engineIniPath = Path.Combine(configDir, "Engine.ini");
            if (!File.Exists(engineIniPath)) return;

            // Remove read-only so we can modify
            var attrs = File.GetAttributes(engineIniPath);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(engineIniPath, attrs & ~FileAttributes.ReadOnly);

            var keysToRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "r.AllowHDR",
                "r.HDR.EnableHDROutput",
                "r.HDR.Display.OutputDevice",
                "r.HDR.Display.ColorGamut",
                "r.HDR.UI.CompositeMode",
                // r.LUT.UpdateEveryFrame is NOT removed here — it's always deployed with UE-Extended
            };

            var lines = File.ReadAllLines(engineIniPath).ToList();
            var filtered = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                // Check if this line is one of our HDR keys
                var isHdrKey = keysToRemove.Any(k =>
                    trimmed.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase));
                if (!isHdrKey)
                    filtered.Add(line);
            }

            // Remove empty section headers (section header followed by nothing or another section header)
            var cleaned = new List<string>();
            for (int i = 0; i < filtered.Count; i++)
            {
                var line = filtered[i];
                // Is this a section header?
                if (line.TrimStart().StartsWith('[') && line.Contains(']'))
                {
                    // Check if next non-empty line is another section header or end of file
                    bool hasContent = false;
                    for (int j = i + 1; j < filtered.Count; j++)
                    {
                        if (string.IsNullOrWhiteSpace(filtered[j])) continue;
                        if (filtered[j].TrimStart().StartsWith('[')) break;
                        hasContent = true;
                        break;
                    }
                    if (!hasContent) continue; // Skip empty section header
                }
                cleaned.Add(line);
            }

            // Trim trailing empty lines
            while (cleaned.Count > 0 && string.IsNullOrWhiteSpace(cleaned[^1]))
                cleaned.RemoveAt(cleaned.Count - 1);

            if (cleaned.Count == 0 || cleaned.All(string.IsNullOrWhiteSpace))
            {
                // File is empty — delete it
                File.Delete(engineIniPath);
                CrashReporter.Log($"[AuxInstallService.RemoveEngineIniHdrSettings] Deleted empty Engine.ini at '{engineIniPath}'");
            }
            else
            {
                File.WriteAllLines(engineIniPath, cleaned);
                // Don't set read-only — let the game manage its own config now
                CrashReporter.Log($"[AuxInstallService.RemoveEngineIniHdrSettings] Removed HDR settings from '{engineIniPath}'");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.RemoveEngineIniHdrSettings] Failed for '{installPath}' — {ex.Message}");
        }
    }

    // ── Engine.ini HDR auto-deployment ────────────────────────────────────────────

    /// <summary>
    /// Resolves the UE project name from the game install path by finding the folder
    /// immediately above "Binaries" in the path hierarchy.
    /// If that folder doesn't have a matching AppData directory, tries the grandparent
    /// (handles cases like SMT5V where path is {Game}\Project\Binaries\Win64 and AppData uses {Game}).
    /// Returns null if Binaries is not found in the path.
    /// </summary>
    internal static string? ResolveUeProjectName(string installPath)
    {
        var normalized = installPath.Replace('/', '\\').TrimEnd('\\');
        var parts = normalized.Split('\\');

        for (int i = parts.Length - 1; i > 0; i--)
        {
            if (parts[i].Equals("Binaries", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = parts[i - 1];

                // Verify the candidate has a matching AppData folder
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var candidateDir = Path.Combine(localAppData, candidate);

                if (Directory.Exists(candidateDir))
                    return candidate;

                // Fallback: try grandparent (folder above the candidate)
                if (i - 2 >= 0)
                {
                    var grandparent = parts[i - 2];
                    var grandparentDir = Path.Combine(localAppData, grandparent);
                    if (Directory.Exists(grandparentDir))
                        return grandparent;
                }

                // No AppData folder found — return the candidate anyway (will create on deploy)
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the Engine.ini config directory for a UE game.
    /// Checks %LocalAppData%\{ProjectName}\Saved\Config\ first, then
    /// %USERPROFILE%\Documents\My Games\{GameName}\Saved\Config\ as fallback.
    /// Priority: WinGDK > Windows > WindowsNoEditor. Creates Windows if none exist.
    /// Returns null if project name cannot be resolved.
    /// </summary>
    internal static string? ResolveEngineIniDir(string installPath, string? projectNameOverride = null, string? gameName = null, string? store = null)
    {
        // If the override contains a path separator, treat it as a direct config directory path (or pipe-separated list)
        if (!string.IsNullOrEmpty(projectNameOverride) && (projectNameOverride.Contains('\\') || projectNameOverride.Contains('/')))
        {
            // Support pipe-separated multiple paths (for games with store-specific config locations)
            var candidates = projectNameOverride.Split('|');
            foreach (var candidate in candidates)
            {
                var expandedPath = Environment.ExpandEnvironmentVariables(candidate.Trim());
                if (Directory.Exists(expandedPath)) return expandedPath;
            }
            // None exist yet — try creating the first one
            var firstExpanded = Environment.ExpandEnvironmentVariables(candidates[0].Trim());
            var parent = Path.GetDirectoryName(firstExpanded);
            if (parent != null && Directory.Exists(parent))
            {
                Directory.CreateDirectory(firstExpanded);
                return firstExpanded;
            }
            return null;
        }

        var projectName = projectNameOverride ?? ResolveUeProjectName(installPath);
        if (string.IsNullOrEmpty(projectName)) return null;

        // Try %LocalAppData%\{ProjectName}\Saved\Config\ first
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var configBase = Path.Combine(localAppData, projectName, "Saved", "Config");

        var result = FindPlatformConfigDir(configBase);
        if (result != null) return result;

        // Fallback: Documents\My Games\{GameName}\Saved\Config\
        if (!string.IsNullOrEmpty(gameName))
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var myGamesConfig = Path.Combine(docs, "My Games", gameName, "Saved", "Config");
            result = FindPlatformConfigDir(myGamesConfig);
            if (result != null) return result;

            // Also try with stripped ® ™ symbols
            var stripped = gameName.Replace("®", "").Replace("™", "").Replace("©", "").Trim();
            if (stripped != gameName)
            {
                myGamesConfig = Path.Combine(docs, "My Games", stripped, "Saved", "Config");
                result = FindPlatformConfigDir(myGamesConfig);
                if (result != null) return result;
            }
        }

        // Fallback: Config stored inside the game directory itself
        // Pattern: {GameRoot}\{ProjectName}\Saved\Config\{Platform}\
        // Navigate up from installPath to find Saved\Config
        {
            var normalized = installPath.Replace('/', '\\').TrimEnd('\\');
            var pathParts = normalized.Split('\\');
            for (int i = pathParts.Length - 1; i > 0; i--)
            {
                if (pathParts[i].Equals("Binaries", StringComparison.OrdinalIgnoreCase))
                {
                    // The project folder is immediately above Binaries
                    var projectDir = string.Join('\\', pathParts.Take(i));
                    var inGameConfig = Path.Combine(projectDir, "Saved", "Config");
                    result = FindPlatformConfigDir(inGameConfig);
                    if (result != null) return result;

                    // Also check the game root (parent of project folder)
                    if (i - 1 > 0)
                    {
                        var gameRoot = string.Join('\\', pathParts.Take(i - 1));
                        // Scan for any subfolder with Saved\Config
                        try
                        {
                            foreach (var subDir in Directory.EnumerateDirectories(gameRoot))
                            {
                                var subConfig = Path.Combine(subDir, "Saved", "Config");
                                result = FindPlatformConfigDir(subConfig);
                                if (result != null) return result;
                            }
                        }
                        catch { }
                    }
                    break;
                }
            }
        }

        // Nothing found — create in LocalAppData as default
        // Use WinGDK for Game Pass/Xbox games, Windows for all others
        bool isGamePass = string.Equals(store, "Xbox", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(store, "Game Pass", StringComparison.OrdinalIgnoreCase);
        var platformFolder = isGamePass ? "WinGDK" : "Windows";
        var fallbackDir = Path.Combine(configBase, platformFolder);
        Directory.CreateDirectory(fallbackDir);
        return fallbackDir;
    }

    /// <summary>Checks a config base path for existing platform subfolders (WinGDK > Windows > WindowsNoEditor).</summary>
    private static string? FindPlatformConfigDir(string configBase)
    {
        var winGdk = Path.Combine(configBase, "WinGDK");
        if (Directory.Exists(winGdk)) return winGdk;

        var windows = Path.Combine(configBase, "Windows");
        if (Directory.Exists(windows)) return windows;

        var windowsNoEditor = Path.Combine(configBase, "WindowsNoEditor");
        if (Directory.Exists(windowsNoEditor)) return windowsNoEditor;

        return null;
    }

    /// <summary>
    /// Deploys HDR settings to the game's Engine.ini in %LocalAppData%.
    /// Uses a line-based approach to preserve duplicate keys (common in UE Engine.ini).
    /// Only appends missing sections/keys — never modifies existing content.
    /// Sets the file to read-only after writing to prevent the engine from overwriting.
    /// </summary>
    public static void ApplyEngineIniHdrSettings(string installPath, string? projectNameOverride = null, string? gameName = null, string? store = null)
    {
        try
        {
            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName, store);
            if (configDir == null)
            {
                CrashReporter.Log($"[AuxInstallService.ApplyEngineIniHdrSettings] Could not resolve config dir for '{installPath}'");
                return;
            }

            var engineIniPath = Path.Combine(configDir, "Engine.ini");

            // Remove read-only if present so we can write
            if (File.Exists(engineIniPath))
            {
                var attrs = File.GetAttributes(engineIniPath);
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(engineIniPath, attrs & ~FileAttributes.ReadOnly);
            }

            // Read existing content as raw lines (preserves duplicate keys)
            var existingLines = File.Exists(engineIniPath) ? File.ReadAllLines(engineIniPath) : Array.Empty<string>();

            // HDR settings to ensure exist — grouped by section
            var requiredEntries = new (string Section, string Key, string Value)[]
            {
                ("SystemSettings", "r.AllowHDR", "1"),
                ("SystemSettings", "r.HDR.EnableHDROutput", "1"),
                ("SystemSettings", "r.HDR.Display.OutputDevice", "3"),
                ("SystemSettings", "r.HDR.Display.ColorGamut", "2"),
                ("SystemSettings", "r.HDR.UI.CompositeMode", "1"),
                ("/Script/Engine.RendererSettings", "r.LUT.UpdateEveryFrame", "1"),
            };

            // Check which keys are already present — if all present, just ensure read-only
            bool anyMissing = requiredEntries.Any(e =>
                !existingLines.Any(l => l.TrimStart().StartsWith(e.Key + "=", StringComparison.OrdinalIgnoreCase)));

            if (!anyMissing)
            {
                // All keys already present — just ensure read-only
                if (File.Exists(engineIniPath))
                    File.SetAttributes(engineIniPath, File.GetAttributes(engineIniPath) | FileAttributes.ReadOnly);
                return;
            }

            // Build lines to write per section, merging into existing sections where possible
            var groupedBySection = requiredEntries
                .Where(e => !existingLines.Any(l => l.TrimStart().StartsWith(e.Key + "=", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(e => e.Section, StringComparer.OrdinalIgnoreCase);

            var lines = existingLines.ToList();
            foreach (var group in groupedBySection)
            {
                var sectionHeader = $"[{group.Key}]";
                int sectionStart = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionStart = i;
                        break;
                    }
                }

                if (sectionStart >= 0)
                {
                    // Find end of this section
                    int insertAt = sectionStart + 1;
                    while (insertAt < lines.Count && !lines[insertAt].TrimStart().StartsWith("["))
                        insertAt++;
                    // Step back over trailing blank lines within the section
                    int insertPos = insertAt;
                    while (insertPos > sectionStart + 1 && string.IsNullOrWhiteSpace(lines[insertPos - 1]))
                        insertPos--;
                    lines.InsertRange(insertPos, group.Select(e => $"{e.Key}={e.Value}"));
                }
                else
                {
                    // Section doesn't exist — append it
                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                        lines.Add("");
                    lines.Add(sectionHeader);
                    foreach (var entry in group)
                        lines.Add($"{entry.Key}={entry.Value}");
                }
            }

            File.WriteAllLines(engineIniPath, lines);

            // Set read-only to prevent engine from overwriting on launch
            File.SetAttributes(engineIniPath, File.GetAttributes(engineIniPath) | FileAttributes.ReadOnly);

            CrashReporter.Log($"[AuxInstallService.ApplyEngineIniHdrSettings] Applied HDR settings to '{engineIniPath}' (read-only)");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.ApplyEngineIniHdrSettings] Failed for '{installPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Removes specific (Section, Key) pairs from Engine.ini previously written by ApplyEngineIniCustomKeys.
    /// Makes the file writable before editing, then sets it read-only again after.
    /// </summary>
    public static void RemoveEngineIniCustomKeys(
        string installPath,
        IEnumerable<string> keysToRemove,
        string? projectNameOverride = null,
        string? gameName = null,
        string? store = null)
    {
        try
        {
            var keySet = new HashSet<string>(keysToRemove, StringComparer.OrdinalIgnoreCase);
            if (keySet.Count == 0) return;

            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName, store);
            if (configDir == null) return;

            var engineIniPath = Path.Combine(configDir, "Engine.ini");
            if (!File.Exists(engineIniPath)) return;

            var attrs = File.GetAttributes(engineIniPath);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(engineIniPath, attrs & ~FileAttributes.ReadOnly);

            var lines = File.ReadAllLines(engineIniPath).ToList();
            // Remove lines where the key (before =) matches any key in the set
            lines.RemoveAll(l =>
            {
                var trimmed = l.TrimStart();
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx <= 0) return false;
                var key = trimmed[..eqIdx].Trim();
                return keySet.Contains(key);
            });
            // Remove section headers that are now empty (no key lines before the next header or EOF)
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                var trimmed = lines[i].Trim();
                if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]")) continue;
                // Check if there are any non-blank, non-header lines between this header and the next
                int next = i + 1;
                while (next < lines.Count && string.IsNullOrWhiteSpace(lines[next])) next++;
                bool isEmpty = next >= lines.Count || (lines[next].Trim().StartsWith("[") && lines[next].Trim().EndsWith("]"));
                if (isEmpty)
                {
                    // Remove the header and any blank lines immediately after it
                    int end = i + 1;
                    while (end < lines.Count && string.IsNullOrWhiteSpace(lines[end])) end++;
                    lines.RemoveRange(i, end - i);
                }
            }
            // Remove trailing empty lines
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
                lines.RemoveAt(lines.Count - 1);

            File.WriteAllLines(engineIniPath, lines);
            File.SetAttributes(engineIniPath, File.GetAttributes(engineIniPath) | FileAttributes.ReadOnly);
            CrashReporter.Log($"[AuxInstallService.RemoveEngineIniCustomKeys] Removed {keySet.Count} key(s) from '{engineIniPath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.RemoveEngineIniCustomKeys] Failed for '{installPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Writes arbitrary (Section, Key, Value) entries to Engine.ini.
    /// Only appends keys that are not already present. Sets the file read-only after writing.
    /// Used by generic Luma UE install to apply game-specific Engine.ini tweaks scraped from the wiki.
    /// </summary>
    public static void ApplyEngineIniCustomKeys(
        string installPath,
        IEnumerable<(string Section, string Key, string Value)> entries,
        string? projectNameOverride = null,
        string? gameName = null,
        string? store = null)
    {
        try
        {
            var entryList = entries.ToList();
            if (entryList.Count == 0) return;

            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName, store);
            if (configDir == null)
            {
                CrashReporter.Log($"[AuxInstallService.ApplyEngineIniCustomKeys] Could not resolve config dir for '{installPath}'");
                return;
            }

            var engineIniPath = Path.Combine(configDir, "Engine.ini");

            if (File.Exists(engineIniPath))
            {
                var attrs = File.GetAttributes(engineIniPath);
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(engineIniPath, attrs & ~FileAttributes.ReadOnly);
            }

            var existingLines = File.Exists(engineIniPath) ? File.ReadAllLines(engineIniPath) : Array.Empty<string>();

            var toWrite = entryList.Where(e =>
                !existingLines.Any(l => l.TrimStart().StartsWith(e.Key + "=", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (toWrite.Count == 0)
            {
                if (File.Exists(engineIniPath))
                    File.SetAttributes(engineIniPath, File.GetAttributes(engineIniPath) | FileAttributes.ReadOnly);
                return;
            }

            // Merge into existing sections rather than always appending new section headers.
            // For each group, find the last line of the existing section and insert there.
            // If the section doesn't exist, append it at the end.
            var lines = existingLines.ToList();
            var grouped = toWrite.GroupBy(e => e.Section, StringComparer.OrdinalIgnoreCase);
            foreach (var group in grouped)
            {
                var sectionHeader = $"[{group.Key}]";
                // Find the section start index
                int sectionStart = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionStart = i;
                        break;
                    }
                }

                if (sectionStart >= 0)
                {
                    // Find the end of this section (next section header or EOF)
                    int insertAt = sectionStart + 1;
                    while (insertAt < lines.Count && !lines[insertAt].TrimStart().StartsWith("["))
                        insertAt++;
                    // Insert before the next section (or at end), skip back over trailing blanks
                    int insertPos = insertAt;
                    while (insertPos > sectionStart + 1 && string.IsNullOrWhiteSpace(lines[insertPos - 1]))
                        insertPos--;
                    var newLines = group.Select(e => $"{e.Key}={e.Value}").ToList();
                    lines.InsertRange(insertPos, newLines);
                }
                else
                {
                    // Section doesn't exist — append it
                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                        lines.Add("");
                    lines.Add(sectionHeader);
                    foreach (var entry in group)
                        lines.Add($"{entry.Key}={entry.Value}");
                }
            }

            File.WriteAllLines(engineIniPath, lines);
            File.SetAttributes(engineIniPath, File.GetAttributes(engineIniPath) | FileAttributes.ReadOnly);

            CrashReporter.Log($"[AuxInstallService.ApplyEngineIniCustomKeys] Wrote {toWrite.Count} key(s) to '{engineIniPath}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.ApplyEngineIniCustomKeys] Failed for '{installPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Unconditionally ensures r.LUT.UpdateEveryFrame=1 exists in Engine.ini for UE-Extended games.
    /// This is always deployed regardless of the EngineIniHdr toggle state.
    /// </summary>
    public static void ApplyEngineIniLutSetting(string installPath, string? projectNameOverride = null, string? gameName = null, string? store = null)
    {
        try
        {
            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName, store);
            if (configDir == null)
            {
                CrashReporter.Log($"[AuxInstallService.ApplyEngineIniLutSetting] Could not resolve config dir for '{installPath}'");
                return;
            }

            var engineIniPath = Path.Combine(configDir, "Engine.ini");

            // Remove read-only if present so we can write
            if (File.Exists(engineIniPath))
            {
                var attrs = File.GetAttributes(engineIniPath);
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(engineIniPath, attrs & ~FileAttributes.ReadOnly);
            }

            var existingLines = File.Exists(engineIniPath) ? File.ReadAllLines(engineIniPath) : Array.Empty<string>();

            // Check if key already exists
            bool found = existingLines.Any(l =>
                l.TrimStart().StartsWith("r.LUT.UpdateEveryFrame=", StringComparison.OrdinalIgnoreCase));

            if (found)
            {
                // Already present — just ensure read-only
                if (File.Exists(engineIniPath))
                    File.SetAttributes(engineIniPath, File.GetAttributes(engineIniPath) | FileAttributes.ReadOnly);
                return;
            }

            // Append the section + key
            var existingText = string.Join("\n", existingLines);
            var sectionHeader = "[/Script/Engine.RendererSettings]";
            bool sectionExists = existingLines.Any(l =>
                l.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));

            var appendBuilder = new System.Text.StringBuilder();
            appendBuilder.AppendLine();
            appendBuilder.AppendLine(sectionHeader);
            appendBuilder.AppendLine("r.LUT.UpdateEveryFrame=1");

            var appendText = appendBuilder.ToString();
            if (!existingText.EndsWith("\n") && !existingText.EndsWith("\r\n") && existingText.Length > 0)
                appendText = "\n" + appendText;

            File.AppendAllText(engineIniPath, appendText);

            // Set read-only
            File.SetAttributes(engineIniPath, File.GetAttributes(engineIniPath) | FileAttributes.ReadOnly);
            CrashReporter.Log($"[AuxInstallService.ApplyEngineIniLutSetting] Applied r.LUT.UpdateEveryFrame=1 to '{engineIniPath}' (read-only)");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.ApplyEngineIniLutSetting] Failed for '{installPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Removes r.LUT.UpdateEveryFrame from Engine.ini. Uses line filtering (preserves duplicate keys).
    /// Removes read-only first, removes empty section headers left behind, re-sets read-only if file still has content.
    /// </summary>
    public static void RemoveEngineIniLutSetting(string installPath, string? projectNameOverride = null, string? gameName = null, string? store = null)
    {
        try
        {
            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName, store);
            if (configDir == null) return;

            var engineIniPath = Path.Combine(configDir, "Engine.ini");
            if (!File.Exists(engineIniPath)) return;

            // Remove read-only so we can modify
            var attrs = File.GetAttributes(engineIniPath);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(engineIniPath, attrs & ~FileAttributes.ReadOnly);

            var lines = File.ReadAllLines(engineIniPath).ToList();
            var filtered = lines.Where(line =>
                !line.TrimStart().StartsWith("r.LUT.UpdateEveryFrame=", StringComparison.OrdinalIgnoreCase)).ToList();

            // Remove empty section headers
            var cleaned = new List<string>();
            for (int i = 0; i < filtered.Count; i++)
            {
                var line = filtered[i];
                if (line.TrimStart().StartsWith('[') && line.Contains(']'))
                {
                    bool hasContent = false;
                    for (int j = i + 1; j < filtered.Count; j++)
                    {
                        if (string.IsNullOrWhiteSpace(filtered[j])) continue;
                        if (filtered[j].TrimStart().StartsWith('[')) break;
                        hasContent = true;
                        break;
                    }
                    if (!hasContent) continue;
                }
                cleaned.Add(line);
            }

            // Trim trailing empty lines
            while (cleaned.Count > 0 && string.IsNullOrWhiteSpace(cleaned[^1]))
                cleaned.RemoveAt(cleaned.Count - 1);

            if (cleaned.Count == 0 || cleaned.All(string.IsNullOrWhiteSpace))
            {
                File.Delete(engineIniPath);
                CrashReporter.Log($"[AuxInstallService.RemoveEngineIniLutSetting] Deleted empty Engine.ini at '{engineIniPath}'");
            }
            else
            {
                File.WriteAllLines(engineIniPath, cleaned);
                File.SetAttributes(engineIniPath, File.GetAttributes(engineIniPath) | FileAttributes.ReadOnly);
                CrashReporter.Log($"[AuxInstallService.RemoveEngineIniLutSetting] Removed r.LUT.UpdateEveryFrame from '{engineIniPath}'");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.RemoveEngineIniLutSetting] Failed for '{installPath}' — {ex.Message}");
        }
    }

    // ── Screenshot path application ───────────────────────────────────────────────

    /// <summary>
    /// Removes the PreprocessorDefinitions line from a reshade.ini file.
    /// This line contains Vulkan-specific depth buffer settings that should not be
    /// present when deploying the RDR2 template to DX games.
    /// </summary>
    private static void StripPreprocessorDefinitions(string iniFilePath)
    {
        try
        {
            var lines = File.ReadAllLines(iniFilePath);
            var filtered = lines.Where(l => !l.TrimStart().StartsWith("PreprocessorDefinitions=", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (filtered.Length != lines.Length)
            {
                File.WriteAllLines(iniFilePath, filtered);
                CrashReporter.Log("[AuxInstallService.StripPreprocessorDefinitions] Removed Vulkan-only PreprocessorDefinitions from DX deployment");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.StripPreprocessorDefinitions] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Writes or updates the [SCREENSHOT] section in the given reshade.ini file,
    /// setting SavePath to the specified value. All other sections/keys are preserved.
    /// </summary>
    public static void ApplyScreenshotPath(string iniFilePath, string savePath)
    {
        var ini = File.Exists(iniFilePath)
            ? ParseIni(File.ReadAllLines(iniFilePath))
            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

        const string section = "SCREENSHOT";

        if (!ini.ContainsKey(section))
            ini[section] = new OrderedDict();

        ini[section]["SavePath"] = savePath;

        WriteIni(iniFilePath, ini);
    }

    /// <summary>
    /// Writes the ToneMapPeakNits value to all [renodx-preset*] sections in the given
    /// reshade.ini file. If no preset section exists, creates [renodx-preset1].
    /// </summary>
    public static void ApplyPeakNits(string iniFilePath, int peakNits)
    {
        if (peakNits <= 0 || !File.Exists(iniFilePath)) return;
        if (!GlobalPeakNitsEnabled) return;

        var ini = ParseIni(File.ReadAllLines(iniFilePath));

        // Write to existing preset sections that are checked
        var written = new HashSet<int>();
        foreach (var section in ini)
        {
            if (section.Key.StartsWith("renodx-preset", StringComparison.OrdinalIgnoreCase))
            {
                var numPart = section.Key.Substring("renodx-preset".Length);
                if (int.TryParse(numPart, out var presetNum))
                {
                    if (!GlobalPeakNitsPresets.Contains(presetNum))
                        continue; // Skip presets the user didn't check

                    section.Value["ToneMapPeakNits"] = peakNits.ToString();
                    written.Add(presetNum);
                }
            }
        }

        // Auto-create missing preset sections that are checked
        foreach (var presetNum in GlobalPeakNitsPresets)
        {
            if (!written.Contains(presetNum))
            {
                ini[$"renodx-preset{presetNum}"] = new OrderedDict { ["ToneMapPeakNits"] = peakNits.ToString() };
            }
        }

        WriteIni(iniFilePath, ini);
    }

    /// <summary>
    /// Writes per-game [renodx] INI keys from manifest overrides to the game's reshade.ini.
    /// Only adds/updates keys — never removes existing user-set values.
    /// </summary>
    public static void ApplyRenodxIniOverrides(string gameDir, Dictionary<string, string> overrides, bool forceOverwrite = false)
    {
        if (overrides == null || overrides.Count == 0) return;

        var iniPath = Path.Combine(gameDir, "reshade.ini");
        if (!File.Exists(iniPath)) return;

        var ini = ParseIni(File.ReadAllLines(iniPath));

        if (!ini.TryGetValue("renodx", out var renodxSection))
        {
            renodxSection = new OrderedDict();
            ini["renodx"] = renodxSection;
        }

        foreach (var (key, value) in overrides)
        {
            if (forceOverwrite || !renodxSection.ContainsKey(key))
                renodxSection[key] = value;
        }

        WriteIni(iniPath, ini);
    }

    // ── Overlay hotkey application ───────────────────────────────────────────────

    /// <summary>
    /// Writes or updates the [INPUT] section in the given reshade*.ini file,
    /// setting KeyOverlay to the specified value. All other sections/keys are preserved.
    /// </summary>
    public static void ApplyOverlayHotkey(string iniFilePath, string keyOverlayValue)
    {
        var ini = File.Exists(iniFilePath)
            ? ParseIni(File.ReadAllLines(iniFilePath))
            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

        const string section = "INPUT";

        if (!ini.ContainsKey(section))
            ini[section] = new OrderedDict();

        CrashReporter.Log($"[AuxInstallService.ApplyOverlayHotkey] Writing KeyOverlay='{keyOverlayValue}' to '{Path.GetFileName(iniFilePath)}'");
        ini[section]["KeyOverlay"] = keyOverlayValue;

        WriteIni(iniFilePath, ini);
    }

    /// <summary>
    /// Writes or updates the [INPUT] section in the given reshade*.ini file,
    /// setting KeyScreenshot to the specified value. All other sections/keys are preserved.
    /// </summary>
    public static void ApplyScreenshotHotkey(string iniFilePath, string keyScreenshotValue)
    {
        var ini = File.Exists(iniFilePath)
            ? ParseIni(File.ReadAllLines(iniFilePath))
            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

        const string section = "INPUT";

        if (!ini.ContainsKey(section))
            ini[section] = new OrderedDict();

        ini[section]["KeyScreenshot"] = keyScreenshotValue;

        WriteIni(iniFilePath, ini);
    }

    /// <summary>
    /// Removes the KeyOverlay key from the [INPUT] section of the given reshade*.ini file,
    /// allowing the game to fall back to its template default. If no [INPUT] section or
    /// KeyOverlay key exists, the file is left unchanged.
    /// </summary>
    public static void RemoveOverlayHotkey(string iniFilePath)
    {
        if (!File.Exists(iniFilePath)) return;

        var ini = ParseIni(File.ReadAllLines(iniFilePath));

        if (ini.TryGetValue("INPUT", out var inputSection) && inputSection.ContainsKey("KeyOverlay"))
        {
            inputSection.Remove("KeyOverlay");
            WriteIni(iniFilePath, ini);
        }
    }

    /// <summary>
    /// Writes or updates VariableListUseTabs in the [OVERLAY] section of the given reshade.ini file.
    /// When true, the ReShade overlay groups effect files into tabs instead of a tree.
    /// </summary>
    public static void ApplyVariableListUseTabs(string iniFilePath, bool useTabs)
    {
        var ini = File.Exists(iniFilePath)
            ? ParseIni(File.ReadAllLines(iniFilePath))
            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

        const string section = "OVERLAY";
        if (!ini.ContainsKey(section))
            ini[section] = new OrderedDict();

        ini[section]["VariableListUseTabs"] = useTabs ? "1" : "0";
        WriteIni(iniFilePath, ini);
    }

    /// <summary>
    /// Writes the osd_toggle_key value to the [FrameLimiter] section of a relimiter.ini file.
    /// Format: [Ctrl+][Alt+][Shift+]KeyName (e.g. "Ctrl+F12", "F12")
    /// </summary>
    public static void ApplyUlOsdHotkey(string iniFilePath, string hotkeyValue)
    {
        var ini = File.Exists(iniFilePath)
            ? ParseIni(File.ReadAllLines(iniFilePath))
            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

        const string section = "FrameLimiter";

        if (!ini.ContainsKey(section))
            ini[section] = new OrderedDict();

        ini[section]["osd_toggle_key"] = hotkeyValue;

        WriteIni(iniFilePath, ini);
    }

    /// <summary>
    /// Writes the shared_presets value to the [FrameLimiter] section of a relimiter.ini file.
    /// When true, ReLimiter reads OSD presets from the shared presets.ini instead of per-game.
    /// </summary>
    public static void ApplyUlSharedPresets(string iniFilePath, bool enabled)
    {
        var ini = File.Exists(iniFilePath)
            ? ParseIni(File.ReadAllLines(iniFilePath))
            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

        const string section = "FrameLimiter";

        if (!ini.ContainsKey(section))
            ini[section] = new OrderedDict();

        ini[section]["shared_presets"] = enabled ? "true" : "false";

        WriteIni(iniFilePath, ini);
    }

    /// <summary>
    /// Writes the dlss_info_hooks value to the [FrameLimiter] section of a relimiter.ini file.
    /// When true, ReLimiter hooks DLSS to display version/preset info on the OSD.
    /// </summary>
    public static void ApplyUlDlssHooks(string iniFilePath, bool enabled)
    {
        var ini = File.Exists(iniFilePath)
            ? ParseIni(File.ReadAllLines(iniFilePath))
            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

        const string section = "FrameLimiter";

        if (!ini.ContainsKey(section))
            ini[section] = new OrderedDict();

        ini[section]["dlss_info_hooks"] = enabled ? "true" : "false";

        WriteIni(iniFilePath, ini);
    }

    /// <summary>
    /// Writes the target_fps value to the [FrameLimiter] section of a relimiter.ini file.
    /// When 0, the key is removed (disabled/off). Otherwise sets the FPS cap value.
    /// </summary>
    public static void ApplyUlTargetFps(string iniFilePath, int targetFps)
    {
        var ini = File.Exists(iniFilePath)
            ? ParseIni(File.ReadAllLines(iniFilePath))
            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

        const string section = "FrameLimiter";

        if (!ini.ContainsKey(section))
            ini[section] = new OrderedDict();

        if (targetFps > 0)
            ini[section]["target_fps"] = targetFps.ToString();
        else
            ini[section].Remove("target_fps");

        WriteIni(iniFilePath, ini);
    }

    // ── INI parsing / writing helpers ─────────────────────────────────────────────

    /// <summary>Simple alias for an ordered key-value dictionary (preserves insertion order).</summary>
    internal class OrderedDict : Dictionary<string, string>
    {
        public OrderedDict() : base(StringComparer.OrdinalIgnoreCase) { }
        public OrderedDict(IDictionary<string, string> d) : base(d, StringComparer.OrdinalIgnoreCase) { }
    }

    /// <summary>
    /// Parses an INI file into sections → key-value pairs.
    /// Preserves all keys within each section in order. Lines that aren't
    /// key=value pairs (comments, blank lines) are stored under a special "" key
    /// with a numeric suffix to preserve them on write-back.
    /// </summary>
    internal static Dictionary<string, OrderedDict> ParseIni(string[] lines)
    {
        var result = new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);
        var currentSection = ""; // keys before any section header go under ""
        result[currentSection] = new OrderedDict();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            // Section header
            if (line.StartsWith('[') && line.Contains(']'))
            {
                currentSection = line.Trim('[', ']', ' ');
                if (!result.ContainsKey(currentSection))
                    result[currentSection] = new OrderedDict();
                continue;
            }

            // Key=Value
            var eqIdx = line.IndexOf('=');
            if (eqIdx > 0)
            {
                var key   = line[..eqIdx].Trim();
                var value = line[(eqIdx + 1)..];
                result[currentSection][key] = value;
            }
            // else: blank line or comment — skip (not preserved in merge output)
        }

        return result;
    }

    /// <summary>Writes a parsed INI structure back to a file.</summary>
    internal static void WriteIni(string path, Dictionary<string, OrderedDict> ini)
    {
        using var writer = new StreamWriter(path, append: false, encoding: new System.Text.UTF8Encoding(false));

        // Write the anonymous section first (keys before any [section])
        if (ini.TryGetValue("", out var anon) && anon.Count > 0)
        {
            foreach (var (key, value) in anon)
                writer.WriteLine($"{key}={value}");
            writer.WriteLine();
        }

        // Write named sections
        foreach (var (section, keys) in ini)
        {
            if (section == "") continue; // already written
            writer.WriteLine($"[{section}]");
            foreach (var (key, value) in keys)
                writer.WriteLine($"{key}={value}");
            writer.WriteLine();
        }
    }

    // ── Luma reshade.ini helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Writes a key=value pair to the [Luma] section of the game's reshade.ini.
    /// Creates the section if it doesn't exist. Overwrites existing key if present.
    /// </summary>
    public static void SetLumaReshadeIniValue(string gameDir, string key, string value)
    {
        try
        {
            var iniPath = Path.Combine(gameDir, "reshade.ini");
            if (!File.Exists(iniPath)) return;

            var lines = File.ReadAllLines(iniPath).ToList();
            bool inLumaSection = false;
            int keyLineIndex = -1;
            int lumaHeaderIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith('['))
                {
                    inLumaSection = trimmed.Equals("[Luma]", StringComparison.OrdinalIgnoreCase);
                    if (inLumaSection) lumaHeaderIndex = i;
                    continue;
                }
                if (inLumaSection && trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    keyLineIndex = i;
                    break;
                }
            }

            if (keyLineIndex >= 0)
            {
                lines[keyLineIndex] = $"{key}={value}";
            }
            else if (lumaHeaderIndex >= 0)
            {
                // Insert after [Luma] header
                lines.Insert(lumaHeaderIndex + 1, $"{key}={value}");
            }
            else
            {
                // Append new [Luma] section
                if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                    lines.Add("");
                lines.Add("[Luma]");
                lines.Add($"{key}={value}");
            }

            File.WriteAllLines(iniPath, lines);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.SetLumaReshadeIniValue] Failed for '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Reads a key from the [Luma] section of the game's reshade.ini.
    /// Returns null if the file or key doesn't exist.
    /// </summary>
    public static string? GetLumaReshadeIniValue(string gameDir, string key)
    {
        try
        {
            var iniPath = Path.Combine(gameDir, "reshade.ini");
            if (!File.Exists(iniPath)) return null;
            bool inLumaSection = false;
            foreach (var line in File.ReadAllLines(iniPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    inLumaSection = trimmed.Equals("[Luma]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (inLumaSection && trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                    return trimmed[(key.Length + 1)..].Trim();
            }
        }
        catch { /* best-effort */ }
        return null;
    }

    /// <summary>
    /// Removes a key from the [Luma] section of the game's reshade.ini.
    /// No-op if the file or key doesn't exist.
    /// </summary>
    public static void RemoveLumaReshadeIniValue(string gameDir, string key)
    {
        try
        {
            var iniPath = Path.Combine(gameDir, "reshade.ini");
            if (!File.Exists(iniPath)) return;

            var lines = File.ReadAllLines(iniPath).ToList();
            bool inLumaSection = false;
            int keyLineIndex = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith('['))
                {
                    inLumaSection = trimmed.Equals("[Luma]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (inLumaSection && trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    keyLineIndex = i;
                    break;
                }
            }

            if (keyLineIndex >= 0)
            {
                lines.RemoveAt(keyLineIndex);
                File.WriteAllLines(iniPath, lines);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.RemoveLumaReshadeIniValue] Failed for '{gameDir}' — {ex.Message}");
        }
    }

    // ── Engine.ini file override (fetched from GitHub) ────────────────────────

    private const string EngineIniFilesBaseUrl = "https://raw.githubusercontent.com/RankFTW/rhi-repo/main/engine-files/";
    private static readonly string EngineIniFilesCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "engine-files");

    /// <summary>
    /// Fetches a custom Engine.ini file from the rhi-repo engine-files/ folder.
    /// Caches to disk at %LocalAppData%\RHI\engine-files\{filename} for the session.
    /// Returns null on failure — callers should fall back to ApplyEngineIniHdrSettings.
    /// </summary>
    public static async Task<string?> FetchEngineIniFileAsync(HttpClient http, string filename)
    {
        try
        {
            Directory.CreateDirectory(EngineIniFilesCacheDir);
            var cachePath = Path.Combine(EngineIniFilesCacheDir, filename);
            var url = EngineIniFilesBaseUrl + Uri.EscapeDataString(filename);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await http.GetAsync(url, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[AuxInstallService.FetchEngineIniFileAsync] HTTP {(int)response.StatusCode} for '{filename}'");
                // Return cached version if available
                if (File.Exists(cachePath)) return await File.ReadAllTextAsync(cachePath).ConfigureAwait(false);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            await File.WriteAllTextAsync(cachePath, content).ConfigureAwait(false);
            CrashReporter.Log($"[AuxInstallService.FetchEngineIniFileAsync] Fetched '{filename}' ({content.Length} chars)");
            return content;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.FetchEngineIniFileAsync] Failed for '{filename}' — {ex.Message}");
            // Try disk cache as fallback
            try
            {
                var cachePath = Path.Combine(EngineIniFilesCacheDir, filename);
                if (File.Exists(cachePath)) return await File.ReadAllTextAsync(cachePath).ConfigureAwait(false);
            }
            catch { }
            return null;
        }
    }

    /// <summary>
    /// Parses a raw Engine.ini text into (Section, Key, Value) tuples.
    /// Handles standard INI format: [SectionName] headers and Key=Value lines.
    /// Blank lines and lines starting with ; or // are ignored.
    /// </summary>
    public static List<(string Section, string Key, string Value)> ParseEngineIniEntries(string iniText)
    {
        var result = new List<(string Section, string Key, string Value)>();
        var currentSection = "SystemSettings"; // default section if file starts without a header

        foreach (var rawLine in iniText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith("//"))
                continue;

            if (line.StartsWith('[') && line.Contains(']'))
            {
                var end = line.IndexOf(']');
                currentSection = line[1..end].Trim();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key   = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (!string.IsNullOrEmpty(key))
                result.Add((currentSection, key, value));
        }

        return result;
    }

    /// <summary>
    /// Fetches a custom Engine.ini file and merges it into the game's Engine.ini.
    /// Uses the same section-aware merge algorithm as ApplyEngineIniCustomKeys.
    /// Falls back to ApplyEngineIniHdrSettings on any fetch/parse failure.
    /// Returns true if the custom file was applied, false if fallback was used.
    /// </summary>
    public static async Task<bool> ApplyEngineIniFromFileAsync(
        HttpClient http,
        string filename,
        string installPath,
        string? projectNameOverride = null,
        string? gameName = null,
        string? store = null)
    {
        var content = await FetchEngineIniFileAsync(http, filename).ConfigureAwait(false);
        if (content == null)
        {
            CrashReporter.Log($"[AuxInstallService.ApplyEngineIniFromFileAsync] Fetch failed for '{filename}', falling back to standard HDR keys");
            ApplyEngineIniHdrSettings(installPath, projectNameOverride, gameName, store);
            return false;
        }

        var entries = ParseEngineIniEntries(content);
        if (entries.Count == 0)
        {
            CrashReporter.Log($"[AuxInstallService.ApplyEngineIniFromFileAsync] No entries parsed from '{filename}', falling back");
            ApplyEngineIniHdrSettings(installPath, projectNameOverride, gameName, store);
            return false;
        }

        ApplyEngineIniCustomKeys(installPath, entries, projectNameOverride, gameName, store);
        CrashReporter.Log($"[AuxInstallService.ApplyEngineIniFromFileAsync] Applied {entries.Count} key(s) from '{filename}' to '{gameName ?? installPath}'");
        return true;
    }
}
