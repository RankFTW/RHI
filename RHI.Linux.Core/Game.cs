using RenoDXCommander.Models;
using System.Text.Json.Serialization;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public sealed class Game
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "Manual";
    public string Root { get; set; } = "";
    public string? SteamRoot { get; set; }
    public string? AppId { get; set; }
    public string? Prefix { get; set; }
    public string? Executable { get; set; }
    public List<string> Executables { get; set; } = [];
    [JsonIgnore] public string Id => AppId is null ? LinuxPaths.Canonical(Root) : $"steam:{AppId}:{LinuxPaths.Canonical(Root)}";
    public override string ToString() => Name;
    [JsonIgnore] public string InstallDirectory => Executable is null ? throw new InvalidOperationException("Choose the game's Windows executable first.") : Path.GetDirectoryName(Executable)!;
    [JsonIgnore] public MachineType Architecture => Executable is null ? MachineType.Native : new PeHeaderService().DetectArchitecture(Executable);
    [JsonIgnore] public GraphicsApiType Api => Executable is null ? GraphicsApiType.Unknown : GraphicsApiDetector.Detect(Executable);
}

public sealed class GamePreferences
{
    public string? Executable { get; set; }
    public string? Prefix { get; set; }
    public string Api { get; set; } = "Auto";
    // Current Proton D3D12 device extensions need fixes newer than ReShade 6.8.0.
    public string Channel { get; set; } = "Nightly";
    public string? ModName { get; set; }
    public bool Favourite { get; set; }
    public bool Hidden { get; set; }
    public string? SteamConfig { get; set; }
}

public sealed class Settings
{
    public string? LastGameId { get; set; }
    public List<string> SteamRoots { get; set; } = [];
    public List<Game> ManualGames { get; set; } = [];
    public Dictionary<string, GamePreferences> Games { get; set; } = [];
    private static string FilePath => Path.Combine(LinuxPaths.Data, "settings.json");
    public static Settings Load() => File.Exists(FilePath)
        ? System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), LinuxPaths.Json) ?? new()
        : new();
    public void Save() => LinuxPaths.WriteJson(FilePath, this);
    public GamePreferences For(Game game)
    {
        if (!Games.TryGetValue(game.Id, out var preferences)) Games[game.Id] = preferences = new();
        return preferences;
    }
}
