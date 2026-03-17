using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using RenoDXCommander.Services;
using ShaderDeployMode = RenoDXCommander.Services.ShaderPackService.DeployMode;

namespace RenoDXCommander.ViewModels;

/// <summary>
/// Owns settings persistence (load/save settings file), theme, density,
/// verbose logging, shader deploy mode, and related computed UI properties.
/// Extracted from MainViewModel per Requirement 1.1.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    // Settings stored as JSON — ApplicationData.Current throws in unpackaged WinUI 3
    private static readonly string _settingsFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenoDXCommander", "settings.json");

    [ObservableProperty] private ShaderDeployMode _shaderDeployMode = ShaderDeployMode.Minimum;
    [ObservableProperty] private bool _skipUpdateCheck;
    [ObservableProperty] private bool _betaOptIn;
    [ObservableProperty] private bool _verboseLogging;
    [ObservableProperty] private string _lastSeenVersion = "";
    [ObservableProperty] private List<string> _selectedShaderPacks = new();

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

    // ── Shader deploy mode ────────────────────────────────────────────────────────

    partial void OnShaderDeployModeChanged(ShaderDeployMode value)
    {
        ShaderPackService.CurrentMode = value;
        SettingsChanged?.Invoke();
        // Notify computed button properties
        OnPropertyChanged(nameof(ShadersBtnLabel));
        OnPropertyChanged(nameof(ShadersBtnBackground));
        OnPropertyChanged(nameof(ShadersBtnForeground));
        OnPropertyChanged(nameof(ShadersBtnBorder));
    }

    /// <summary>Cycles Off → Minimum → All → User → Select → Off and returns new mode.</summary>
    public ShaderDeployMode CycleShaderDeployMode()
    {
        ShaderDeployMode = ShaderDeployMode switch
        {
            ShaderDeployMode.Off     => ShaderDeployMode.Minimum,
            ShaderDeployMode.Minimum => ShaderDeployMode.All,
            ShaderDeployMode.All     => ShaderDeployMode.User,
            ShaderDeployMode.User    => ShaderDeployMode.Select,
            _                        => ShaderDeployMode.Off,
        };
        return ShaderDeployMode;
    }

    // Shaders button label / colours — shown in the header bar
    public string ShadersBtnLabel => ShaderDeployMode switch
    {
        ShaderDeployMode.Off     => "Shaders: Off",
        ShaderDeployMode.Minimum => "Shaders: Minimum",
        ShaderDeployMode.All     => "Shaders: All",
        ShaderDeployMode.User    => "Shaders: User",
        ShaderDeployMode.Select  => "Shaders: Select",
        _                        => "Shaders",
    };
    public string ShadersBtnBackground => ShaderDeployMode switch
    {
        ShaderDeployMode.Off     => "#1E242C",
        ShaderDeployMode.Minimum => "#201838",
        ShaderDeployMode.All     => "#201838",
        ShaderDeployMode.User    => "#122830",
        ShaderDeployMode.Select  => "#2A1830",
        _                        => "#1E242C",
    };
    public string ShadersBtnForeground => ShaderDeployMode switch
    {
        ShaderDeployMode.Off     => "#6B7A8E",
        ShaderDeployMode.Minimum => "#B898E8",
        ShaderDeployMode.All     => "#B898E8",
        ShaderDeployMode.User    => "#4DC9E6",
        ShaderDeployMode.Select  => "#E898C8",
        _                        => "#6B7A8E",
    };
    public string ShadersBtnBorder => ShaderDeployMode switch
    {
        ShaderDeployMode.Off     => "#283240",
        ShaderDeployMode.Minimum => "#3A2860",
        ShaderDeployMode.All     => "#3A2860",
        ShaderDeployMode.User    => "#1E4858",
        ShaderDeployMode.Select  => "#4A2858",
        _                        => "#283240",
    };

    /// <summary>The current ShaderDeployMode for AuxInstallService calls.</summary>
    public ShaderDeployMode CurrentShaderMode => ShaderDeployMode;

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
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void SaveSettingsFile(Dictionary<string, string> settings)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_settingsFilePath)!);
        var json = JsonSerializer.Serialize(settings);

        // Retry with short delays to handle file contention from concurrent background tasks
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.IO.File.WriteAllText(_settingsFilePath, json);
                return;
            }
            catch (System.IO.IOException) when (attempt < 2)
            {
                System.Threading.Thread.Sleep(50 * (attempt + 1)); // 50ms, 100ms
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SettingsViewModel.SaveSettingsFile] Failed to save settings — {ex.Message}");
                return;
            }
        }
    }

    /// <summary>
    /// Loads settings-specific values (ShaderDeployMode, SkipUpdateCheck,
    /// VerboseLogging, LastSeenVersion) from the given settings dictionary.
    /// Called by MainViewModel during LoadNameMappings.
    /// </summary>
    public void LoadSettingsFromDict(Dictionary<string, string> s)
    {
        if (s.TryGetValue("ShaderDeployMode", out var sdm) &&
            Enum.TryParse<ShaderDeployMode>(sdm, out var parsedSdm))
            ShaderDeployMode = parsedSdm;
        ShaderPackService.CurrentMode = ShaderDeployMode;

        if (s.TryGetValue("SkipUpdateCheck", out var sucVal))
            SkipUpdateCheck = sucVal == "true";

        if (s.TryGetValue("BetaOptIn", out var boVal))
            BetaOptIn = boVal == "true";

        if (s.TryGetValue("VerboseLogging", out var vlVal))
            VerboseLogging = vlVal == "true";

        if (s.TryGetValue("LastSeenVersion", out var lsvVal))
            LastSeenVersion = lsvVal ?? "";

        if (s.TryGetValue("SelectedShaderPacks", out var sspVal))
        {
            try
            {
                SelectedShaderPacks = JsonSerializer.Deserialize<List<string>>(sspVal) ?? new();
            }
            catch
            {
                SelectedShaderPacks = new();
            }
        }
        else
        {
            SelectedShaderPacks = new();
        }
    }

    /// <summary>
    /// Writes settings-specific values into the given settings dictionary.
    /// Called by MainViewModel during SaveNameMappings.
    /// </summary>
    public void SaveSettingsToDict(Dictionary<string, string> s)
    {
        s["ShaderDeployMode"]  = ShaderDeployMode.ToString();
        s["SkipUpdateCheck"]   = SkipUpdateCheck ? "true" : "false";
        s["BetaOptIn"]         = BetaOptIn ? "true" : "false";
        s["VerboseLogging"]    = VerboseLogging ? "true" : "false";
        s["LastSeenVersion"]   = LastSeenVersion;
        s["SelectedShaderPacks"] = JsonSerializer.Serialize(SelectedShaderPacks);
    }

    public void LoadThemeAndDensity()
    {
        // Theme/density removed — no longer used
    }
}
