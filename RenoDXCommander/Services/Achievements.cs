// Achievements.cs -- the store-neutral achievement list shown in the Spotlight hero and side panel.
// Filled by SteamAchievementService (Steam's local cache) and XboxAchievementService (the Xbox app's
// local cache).

namespace RenoDXCommander.Services;

internal enum AchievementStore { Steam, Xbox }

/// <summary>One achievement as shown in RHI.</summary>
internal sealed record Achievement(
    string ApiName,
    string Name,
    string Description,
    bool Hidden,
    bool Unlocked,
    DateTime? UnlockedUtc,
    string? Icon,
    string? IconGray)
{
    /// <summary>Percentage of all players who have it, or null when unknown.</summary>
    public double? Percent { get; init; }

    /// <summary>Xbox gamerscore it's worth, or null (Steam).</summary>
    public int? Gamerscore { get; init; }

    /// <summary>Progress towards a locked achievement (1-99), or null when there's none to show.</summary>
    public int? ProgressPercent { get; init; }

    /// <summary>
    /// Shown as "Hidden achievement" without details: a locked hidden one, unless the user chose to see hidden
    /// details (Spotlight / overlay toggle). Still masked when the store gave no name for it.
    /// </summary>
    public bool IsMasked(bool showHidden) => Hidden && !Unlocked && (!showHidden || string.IsNullOrWhiteSpace(Name));
}

/// <summary>
/// A game's achievements: unlocked first (newest first), then locked in the game's order.
/// <see cref="GameId"/> is the Steam AppID or the Xbox title id (digits only).
/// </summary>
internal sealed record AchievementSet(AchievementStore Store, string GameId, IReadOnlyList<Achievement> Items)
{
    public int Total => Items.Count;
    public int Unlocked => Items.Count(a => a.Unlocked);
    /// <summary>Whole percent, rounded down like Steam's hero strip.</summary>
    public int PercentDone => Total == 0 ? 0 : (int)Math.Floor(Unlocked * 100.0 / Total);
    public bool HasRarity => Items.Any(a => a.Percent.HasValue);
    public bool HasGamerscore => Items.Any(a => a.Gamerscore.HasValue);
    public int GamerscoreEarned => Items.Where(a => a.Unlocked).Sum(a => a.Gamerscore ?? 0);
    public int GamerscoreTotal => Items.Sum(a => a.Gamerscore ?? 0);
}
