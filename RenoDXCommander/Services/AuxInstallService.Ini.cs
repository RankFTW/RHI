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

        var gamePath = Path.Combine(gameDir, "reshade.ini");

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

        var gamePath = Path.Combine(gameDir, "reshade.ini");

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
                    gameKeys[key] = value;
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
    }

    /// <summary>Returns true if the game name matches Red Dead Redemption 2 or Max Payne 3 (case-insensitive).
    /// These games use the dedicated reshade.rdr2.ini template.</summary>
    internal static bool IsRdr2(string gameName) =>
        gameName.Contains("Red Dead Redemption 2", StringComparison.OrdinalIgnoreCase) ||
        gameName.Equals("RDR2", StringComparison.OrdinalIgnoreCase) ||
        gameName.Contains("Max Payne 3", StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies reshade.ini from the inis folder to the given game directory (full overwrite, no merge).</summary>
    public static void CopyRsIni(string gameDir)
    {
        if (!File.Exists(RsIniPath))
            throw new FileNotFoundException("reshade.ini not found in inis folder.", RsIniPath);
        File.Copy(RsIniPath, Path.Combine(gameDir, "reshade.ini"), overwrite: true);
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
    public static void ApplyRenoDxNativeHdrSettings(string gameDir, bool usesSdrPath = false)
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

            if (!ini.TryGetValue(section, out var existingKeys))
            {
                ini[section] = new OrderedDict(requiredKeys);
            }
            else
            {
                // Only add keys that are missing — never overwrite user-modified values
                bool changed = false;
                foreach (var (key, value) in requiredKeys)
                {
                    if (!existingKeys.ContainsKey(key))
                    {
                        existingKeys[key] = value;
                        changed = true;
                    }
                }
                if (!changed) return; // All keys already present — no write needed
            }

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
    public static void RemoveEngineIniHdrSettings(string installPath, string? projectNameOverride = null, string? gameName = null)
    {
        try
        {
            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName);
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
    internal static string? ResolveEngineIniDir(string installPath, string? projectNameOverride = null, string? gameName = null)
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
        var windows = Path.Combine(configBase, "Windows");
        Directory.CreateDirectory(windows);
        return windows;
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
    public static void ApplyEngineIniHdrSettings(string installPath, string? projectNameOverride = null, string? gameName = null)
    {
        try
        {
            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName);
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
            var existingText = string.Join("\n", existingLines);

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

            // Check which keys are already present (case-insensitive key search)
            var linesToAppend = new List<string>();
            var sectionsNeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (section, key, value) in requiredEntries)
            {
                // Check if key=value already exists anywhere in the file
                bool found = existingLines.Any(l =>
                    l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));

                if (!found)
                    sectionsNeeded.Add(section);
            }

            if (sectionsNeeded.Count == 0)
            {
                // All keys already present — just ensure read-only
                if (File.Exists(engineIniPath))
                    File.SetAttributes(engineIniPath, File.GetAttributes(engineIniPath) | FileAttributes.ReadOnly);
                return;
            }

            // Build lines to append per section
            var appendBuilder = new System.Text.StringBuilder();
            var groupedBySection = requiredEntries
                .Where(e => !existingLines.Any(l => l.TrimStart().StartsWith(e.Key + "=", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(e => e.Section, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedBySection)
            {
                var sectionHeader = $"[{group.Key}]";
                // Check if section already exists in file
                bool sectionExists = existingLines.Any(l =>
                    l.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));

                if (!sectionExists)
                {
                    appendBuilder.AppendLine();
                    appendBuilder.AppendLine(sectionHeader);
                }
                else
                {
                    // Find the section and insert keys after it — but since we're appending,
                    // UE INI allows keys under a repeated section header at the end
                    appendBuilder.AppendLine();
                    appendBuilder.AppendLine(sectionHeader);
                }

                foreach (var entry in group)
                    appendBuilder.AppendLine($"{entry.Key}={entry.Value}");
            }

            // Append to file (preserves all existing content)
            var appendText = appendBuilder.ToString();
            if (!existingText.EndsWith("\n") && !existingText.EndsWith("\r\n") && existingText.Length > 0)
                appendText = "\n" + appendText;

            File.AppendAllText(engineIniPath, appendText);

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
    /// Unconditionally ensures r.LUT.UpdateEveryFrame=1 exists in Engine.ini for UE-Extended games.
    /// This is always deployed regardless of the EngineIniHdr toggle state.
    /// </summary>
    public static void ApplyEngineIniLutSetting(string installPath, string? projectNameOverride = null, string? gameName = null)
    {
        try
        {
            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName);
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
    public static void RemoveEngineIniLutSetting(string installPath, string? projectNameOverride = null, string? gameName = null)
    {
        try
        {
            var configDir = ResolveEngineIniDir(installPath, projectNameOverride, gameName);
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
}
