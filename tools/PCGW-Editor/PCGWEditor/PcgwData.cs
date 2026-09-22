using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace PCGWEditor;

// ── File-level model (what we serialize to/from pcgw_data.json) ───────────────

public class PcgwDataFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    [JsonPropertyName("game_count")]
    public int GameCount { get; set; }

    [JsonPropertyName("generated")]
    public string Generated { get; set; } = "";

    /// <summary>
    /// Name overrides: detected game name → PCGW page title.
    /// RHI uses this to map e.g. "AFOP" → "Avatar: Frontiers of Pandora"
    /// then builds the wiki URL from the page title.
    /// Replaces manifest pcgwUrlOverrides from v2.7.6 onwards.
    /// </summary>
    [JsonPropertyName("name_overrides")]
    public Dictionary<string, string> NameOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-game data keyed by PCGW page title.
    /// </summary>
    [JsonPropertyName("games")]
    public Dictionary<string, PcgwEntryRaw> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Raw JSON model for a single game entry.</summary>
public class PcgwEntryRaw
{
    [JsonPropertyName("steam_appid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SteamAppId { get; set; }

    [JsonPropertyName("dx9")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Dx9 { get; set; }

    [JsonPropertyName("dx10")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Dx10 { get; set; }

    [JsonPropertyName("dx11")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Dx11 { get; set; }

    [JsonPropertyName("dx12")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Dx12 { get; set; }

    [JsonPropertyName("vulkan")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Vulkan { get; set; }

    [JsonPropertyName("opengl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OpenGL { get; set; }

    [JsonPropertyName("config_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfigPath { get; set; }

    [JsonPropertyName("config_path_xbox")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfigPathXbox { get; set; }
}

// ── View model (bound to the list and editor) ─────────────────────────────────

public class PcgwEntryVm : INotifyPropertyChanged
{
    private string  _pageName = "";
    private int     _steamAppId;
    private bool    _dx9, _dx10, _dx11, _dx12, _vulkan, _openGL;
    private string? _configPath, _configPathXbox;

    public string PageName
    {
        get => _pageName;
        set { _pageName = value; OnPropertyChanged(nameof(PageName)); OnPropertyChanged(nameof(DisplayName)); }
    }

    public int SteamAppId
    {
        get => _steamAppId;
        set { _steamAppId = value; OnPropertyChanged(nameof(SteamAppId)); }
    }

    public bool Dx9   { get => _dx9;   set { _dx9   = value; OnPropertyChanged(nameof(Dx9));   } }
    public bool Dx10  { get => _dx10;  set { _dx10  = value; OnPropertyChanged(nameof(Dx10));  } }
    public bool Dx11  { get => _dx11;  set { _dx11  = value; OnPropertyChanged(nameof(Dx11));  } }
    public bool Dx12  { get => _dx12;  set { _dx12  = value; OnPropertyChanged(nameof(Dx12));  } }
    public bool Vulkan{ get => _vulkan;set { _vulkan = value; OnPropertyChanged(nameof(Vulkan));} }
    public bool OpenGL{ get => _openGL;set { _openGL = value; OnPropertyChanged(nameof(OpenGL));} }

    public string? ConfigPath
    {
        get => _configPath;
        set { _configPath = value; OnPropertyChanged(nameof(ConfigPath)); }
    }

    public string? ConfigPathXbox
    {
        get => _configPathXbox;
        set { _configPathXbox = value; OnPropertyChanged(nameof(ConfigPathXbox)); }
    }

    /// <summary>Display label shown in the list — API flags summary.</summary>
    public string DisplayName
    {
        get
        {
            var apis = new List<string>();
            if (Dx9)    apis.Add("DX9");
            if (Dx10)   apis.Add("DX10");
            if (Dx11)   apis.Add("DX11");
            if (Dx12)   apis.Add("DX12");
            if (Vulkan) apis.Add("VK");
            if (OpenGL) apis.Add("OGL");
            var suffix = apis.Count > 0 ? $"  [{string.Join(" ", apis)}]" : "";
            return PageName + suffix;
        }
    }

    public static PcgwEntryVm FromRaw(string name, PcgwEntryRaw raw) => new()
    {
        PageName      = name,
        SteamAppId    = raw.SteamAppId,
        Dx9           = raw.Dx9,
        Dx10          = raw.Dx10,
        Dx11          = raw.Dx11,
        Dx12          = raw.Dx12,
        Vulkan        = raw.Vulkan,
        OpenGL        = raw.OpenGL,
        ConfigPath    = raw.ConfigPath,
        ConfigPathXbox = raw.ConfigPathXbox,
    };

    public PcgwEntryRaw ToRaw() => new()
    {
        SteamAppId    = SteamAppId,
        Dx9           = Dx9,
        Dx10          = Dx10,
        Dx11          = Dx11,
        Dx12          = Dx12,
        Vulkan        = Vulkan,
        OpenGL        = OpenGL,
        ConfigPath    = string.IsNullOrWhiteSpace(ConfigPath)     ? null : ConfigPath.Trim(),
        ConfigPathXbox = string.IsNullOrWhiteSpace(ConfigPathXbox) ? null : ConfigPathXbox.Trim(),
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One row in the Name Mappings list.</summary>
public class NameMappingVm : INotifyPropertyChanged
{
    private string _detectedName = "";
    private string _pcgwPageName = "";

    public string DetectedName
    {
        get => _detectedName;
        set { _detectedName = value; OnPropertyChanged(nameof(DetectedName)); }
    }

    public string PcgwPageName
    {
        get => _pcgwPageName;
        set { _pcgwPageName = value; OnPropertyChanged(nameof(PcgwPageName)); OnPropertyChanged(nameof(DerivedUrl)); }
    }

    public string DerivedUrl =>
        string.IsNullOrWhiteSpace(PcgwPageName)
            ? ""
            : $"https://www.pcgamingwiki.com/wiki/{PcgwPageName.Replace(" ", "_")}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
