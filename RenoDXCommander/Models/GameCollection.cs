namespace RenoDXCommander.Models;

/// <summary>
/// A user-made game collection ("Racing", "Souls-likes"): a name and the games in it, as
/// composite keys "GameName|Store" (<see cref="GameKey.ToKey"/>). Saved in settings.json
/// ("GameCollections"), in the user's order. A game can be in several collections.
/// </summary>
public sealed class GameCollection
{
    public string Name { get; set; } = "";

    /// <summary>Member game keys ("GameName|Store"), in the order they were added.</summary>
    public List<string> Games { get; set; } = new();
}
