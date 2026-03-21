using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

/// <summary>
/// Owns settings persistence (load/save settings file), theme, density,
/// verbose logging, shader pack selection, and related computed UI properties.
/// Extracted from MainViewModel per Requirement 1.1.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    // Settings stored as JSON — ApplicationData.Current throws in unpackaged WinUI 3
    private static readonly string _settingsFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenoDXCommander", "settings.json");

    [ObservableProperty] private bool _skipUpdateCheck;
    [ObservableProperty] private bool _betaOptIn;
    [ObservableProperty] private bool _verboseLogging;
    [ObservableProperty] private string _lastSeenVersion = "";
    [ObservableProperty] private List<string> _selectedShaderPacks = new();
    [ObservableProperty] private string _addonWatchFolder = "";

    /// <summary>
    /// Optional callback invoked after any settings-specific property changes,
    /// so that MainViewModel can persist the full settings bundle.
    /// </summary>
    public Action? SettingsChanged { get; set; }

    /// <summary>
    /// Guard flag — true while settings are being loaded so that
    /// property-change handlers don't trigger saves mid-load.
    /// </summary>
    public bool IsLoadingSettings { get; set; }

    // ── Verbose logging ───────────────────────────────────────────────────────────

    partial void OnVerboseLoggingChanged(bool value)
    {
        CrashReporter.VerboseLogging = value;
    }

    // ── Settings file I/O ─────────────────────────────────────────────────────────

    public static Dictionary<string, string> LoadSettingsFile()
    {
        try
        {
            if (!System.IO.File.Exists(_settingsFilePath)) return new(StringComparer.OrdinalIgnoreCase);
            var json = System.IO.File.ReadAllText(_settingsFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { CrashReporter.Log($"[SettingsViewModel.LoadSettingsFile] Failed to load settings — {ex.Message}"); return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void SaveSettingsFile(Dictionary<string, string> settings)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_settingsFilePath)!);
        var json = JsonSerializer.Serialize(settings);
        FileHelper.WriteAllTextWithRetry(_settingsFilePath, json, "SettingsViewModel.SaveSettingsFile");
    }

    /// <summary>
    /// Loads settings-specific values (SkipUpdateCheck, VerboseLogging,
    /// LastSeenVersion, SelectedShaderPacks) from the given settings dictionary.
    /// Called by MainViewModel during LoadNameMappings.
    /// </summary>
    public void LoadSettingsFromDict(Dictionary<string, string> s)
    {
        if (s.TryGetValue("SkipUpdateCheck", out var sucVal))
            SkipUpdateCheck = sucVal == "true";

        if (s.TryGetValue("BetaOptIn", out var boVal))
            BetaOptIn = boVal == "true";

        if (s.TryGetValue("VerboseLogging", out var vlVal))
            VerboseLogging = vlVal == "true";

        if (s.TryGetValue("LastSeenVersion", out var lsvVal))
            LastSeenVersion = lsvVal ?? "";

        // Migration: retain SelectedShaderPacks only when the persisted mode
        // was "Select".  Any other value (or absent key) means the user was on
        // an old mode — start with an empty selection.
        var wasSelectMode = s.TryGetValue("ShaderDeployMode", out var sdm)
                            && string.Equals(sdm, "Select", StringComparison.OrdinalIgnoreCase);

        if (wasSelectMode && s.TryGetValue("SelectedShaderPacks", out var sspVal))
        {
            try
            {
                SelectedShaderPacks = JsonSerializer.Deserialize<List<string>>(sspVal) ?? new();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsViewModel.LoadSettingsFromDict] Failed to deserialize SelectedShaderPacks — {ex.Message}");
                SelectedShaderPacks = new();
            }
        }
        else
        {
            SelectedShaderPacks = new();
        }

        if (s.TryGetValue("AddonWatchFolder", out var awfVal))
            AddonWatchFolder = awfVal ?? "";
    }

    /// <summary>
    /// Writes settings-specific values into the given settings dictionary.
    /// Called by MainViewModel during SaveNameMappings.
    /// </summary>
    public void SaveSettingsToDict(Dictionary<string, string> s)
    {
        s["SkipUpdateCheck"]   = SkipUpdateCheck ? "true" : "false";
        s["BetaOptIn"]         = BetaOptIn ? "true" : "false";
        s["VerboseLogging"]    = VerboseLogging ? "true" : "false";
        s["LastSeenVersion"]   = LastSeenVersion;
        s["ShaderDeployMode"]  = SelectedShaderPacks.Count > 0 ? "Select" : "Off";
        s["SelectedShaderPacks"] = JsonSerializer.Serialize(SelectedShaderPacks);
        if (!string.IsNullOrWhiteSpace(AddonWatchFolder))
            s["AddonWatchFolder"] = AddonWatchFolder;
    }

    public void LoadThemeAndDensity()
    {
        // Theme/density removed — no longer used
    }
}
