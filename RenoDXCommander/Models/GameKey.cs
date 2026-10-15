namespace RenoDXCommander.Models;

/// <summary>
/// Composite key for uniquely identifying a game installation.
/// Combines game name with store/platform for multi-store support.
/// </summary>
public readonly record struct GameKey(string Name, string Store)
{
    /// <summary>Creates a GameKey from a GameCardViewModel or DetectedGame.</summary>
    public static GameKey From(string name, string store) => new(name, store ?? "");

    /// <summary>Creates a GameKey with empty store (for legacy/migration scenarios).</summary>
    public static GameKey NameOnly(string name) => new(name, "");

    /// <summary>Converts to pipe-separated string format for persistence.</summary>
    public string ToKey() => $"{Name}|{Store}";

    /// <summary>Parses a pipe-separated key string.</summary>
    public static GameKey Parse(string key)
    {
        var idx = key.LastIndexOf('|');
        if (idx < 0) return new GameKey(key, ""); // Legacy key without store
        return new GameKey(key[..idx], key[(idx + 1)..]);
    }

    /// <summary>Creates a GameKey from card properties.</summary>
    public static GameKey FromCard(string gameName, string? source) => new(gameName, source ?? "");

    /// <summary>Checks if this key matches by name only (ignoring store).</summary>
    public bool MatchesName(string name) =>
        Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Checks if this key matches another key (name + store, case-insensitive).</summary>
    public bool Matches(GameKey other) =>
        Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase) &&
        Store.Equals(other.Store, StringComparison.OrdinalIgnoreCase);

    /// <summary>Checks if this key matches name and store strings.</summary>
    public bool Matches(string name, string store) =>
        Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
        Store.Equals(store ?? "", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => string.IsNullOrEmpty(Store) ? Name : $"{Name} ({Store})";
}
