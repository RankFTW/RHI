using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Static helper for extracting game domain and mod ID from Nexus Mods URLs,
/// and formatting NexusModReference values back into canonical URL strings.
/// </summary>
public static class NexusUrlParser
{
    private const string NexusBaseUrl = "https://www.nexusmods.com";

    /// <summary>
    /// Parses a Nexus Mods URL into game domain and mod ID.
    /// Supports both formats:
    ///   https://www.nexusmods.com/{game_domain}/mods/{mod_id}
    ///   https://www.nexusmods.com/games/{game_domain}
    /// Returns null if the URL doesn't match either pattern.
    /// </summary>
    public static NexusModReference? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (!string.Equals(uri.Host, "www.nexusmods.com", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Host, "nexusmods.com", StringComparison.OrdinalIgnoreCase))
            return null;

        // Split the path into non-empty segments.
        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            return null;

        // Format: /games/{game_domain}
        if (segments.Length >= 2
            && string.Equals(segments[0], "games", StringComparison.OrdinalIgnoreCase))
        {
            var gameDomain = segments[1];
            if (string.IsNullOrEmpty(gameDomain))
                return null;

            return new NexusModReference(gameDomain, null);
        }

        // Format: /{game_domain}/mods/{mod_id}
        if (segments.Length >= 3
            && string.Equals(segments[1], "mods", StringComparison.OrdinalIgnoreCase))
        {
            var gameDomain = segments[0];
            if (string.IsNullOrEmpty(gameDomain))
                return null;

            if (!int.TryParse(segments[2], out var modId) || modId <= 0)
                return null;

            return new NexusModReference(gameDomain, modId);
        }

        return null;
    }

    /// <summary>
    /// Formats a NexusModReference back into a canonical URL string.
    /// If ModId is non-null: https://www.nexusmods.com/{GameDomain}/mods/{ModId}
    /// If ModId is null:     https://www.nexusmods.com/games/{GameDomain}
    /// </summary>
    public static string Format(NexusModReference reference)
    {
        if (reference.ModId is not null)
            return $"{NexusBaseUrl}/{reference.GameDomain}/mods/{reference.ModId}";

        return $"{NexusBaseUrl}/games/{reference.GameDomain}";
    }
}
