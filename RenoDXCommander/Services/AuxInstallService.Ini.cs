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
    public static void MergeRsIni(string gameDir, string? screenshotSavePath = null, string? overlayHotkey = null, string? screenshotHotkey = null)
    {
        if (!File.Exists(RsIniPath))
            throw new FileNotFoundException("reshade.ini not found in inis folder.", RsIniPath);

        var gamePath = Path.Combine(gameDir, "reshade.ini");

        if (!File.Exists(gamePath))
        {
            // No existing INI — just copy the template
            File.Copy(RsIniPath, gamePath, overwrite: true);

            // Apply screenshot path to the freshly copied file
            if (screenshotSavePath != null)
                ApplyScreenshotPath(gamePath, screenshotSavePath);

            // Apply overlay hotkey if non-default
            if (overlayHotkey != null && !HotkeyManager.IsDefaultHotkey(overlayHotkey))
                ApplyOverlayHotkey(gamePath, overlayHotkey);

            // Apply screenshot hotkey if non-default
            if (screenshotHotkey != null && screenshotHotkey != "44,0,0,0")
                ApplyScreenshotHotkey(gamePath, screenshotHotkey);
            return;
        }

        // Parse both files
        var gameIni     = ParseIni(File.ReadAllLines(gamePath));
        var templateIni = ParseIni(File.ReadAllLines(RsIniPath));

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

        // Apply screenshot path after merge
        if (screenshotSavePath != null)
            ApplyScreenshotPath(gamePath, screenshotSavePath);

        // Apply overlay hotkey if non-default
        if (overlayHotkey != null && !HotkeyManager.IsDefaultHotkey(overlayHotkey))
            ApplyOverlayHotkey(gamePath, overlayHotkey);

        // Apply screenshot hotkey if non-default
        if (screenshotHotkey != null && screenshotHotkey != "44,0,0,0")
            ApplyScreenshotHotkey(gamePath, screenshotHotkey);
    }

    /// <summary>
    /// Merges the Vulkan-specific reshade.vulkan.ini template into the game directory
    /// as reshade.ini. Uses the same merge logic as <see cref="MergeRsIni"/> — template
    /// keys overwrite, game-only keys are preserved. Falls back to the standard
    /// reshade.ini if the Vulkan template doesn't exist.
    /// For Red Dead Redemption 2, uses the dedicated reshade.rdr2.ini template instead.
    /// </summary>
    public static void MergeRsVulkanIni(string gameDir, string? gameName = null, string? screenshotSavePath = null, string? overlayHotkey = null, string? screenshotHotkey = null)
    {
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
    }

    /// <summary>Returns true if the game name matches Red Dead Redemption 2 (case-insensitive).</summary>
    internal static bool IsRdr2(string gameName) =>
        gameName.Contains("Red Dead Redemption 2", StringComparison.OrdinalIgnoreCase) ||
        gameName.Equals("RDR2", StringComparison.OrdinalIgnoreCase);

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

    // ── Screenshot path application ───────────────────────────────────────────────

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
