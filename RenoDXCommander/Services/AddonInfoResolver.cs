using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Centralizes the three-tier content resolution logic for per-addon Info buttons.
/// Stateless — all data is passed in via method parameters.
///
/// Tier 1: Per-addon manifest dictionary entry for the game name.
/// Tier 2: Wiki-scraped content (RenoDX wiki for RenoDX, OptiScaler wiki for OptiScaler,
///          Luma wiki/manifest for Luma).
/// Tier 3: Static generic fallback text describing the addon.
/// </summary>
public class AddonInfoResolver
{
    // ── Generic fallback text constants (Tier 3) ──────────────────────────────────

    public const string FallbackREFramework =
        "RE Framework is a modding framework for RE Engine games. It enables ReShade injection and other mods by hooking into the game's rendering pipeline.";

    public const string FallbackReShade =
        "ReShade is a post-processing injector that adds visual effects to games. It is required by RenoDX to apply HDR tone mapping and color grading.";

    public const string FallbackRenoDX =
        "RenoDX is an HDR mod framework that upgrades SDR games to HDR using ReShade. It provides per-game tone mapping and color space conversion.";

    public const string FallbackReLimiter =
        "ReLimiter is a frame limiter that works alongside ReShade to reduce input lag and improve frame pacing in games.";

    public const string FallbackDisplayCommander =
        "Display Commander is a frame limiter that works alongside ReShade to reduce input lag and improve frame pacing.";

    public const string FallbackOptiScaler =
        "OptiScaler is an upscaling compatibility layer that enables FSR, XeSS, or DLSS across different GPU vendors, allowing you to use any upscaler regardless of your hardware.";

    public const string FallbackLuma =
        "Luma is an alternative HDR mod framework that provides native HDR output for supported games, bypassing the need for ReShade-based injection.";

    /// <summary>
    /// Returns the generic fallback text for the given addon type.
    /// </summary>
    public static string GetFallbackText(AddonType addon) => addon switch
    {
        AddonType.REFramework     => FallbackREFramework,
        AddonType.ReShade         => FallbackReShade,
        AddonType.RenoDX          => FallbackRenoDX,
        AddonType.ReLimiter       => FallbackReLimiter,
        AddonType.DisplayCommander => FallbackDisplayCommander,
        AddonType.OptiScaler      => FallbackOptiScaler,
        AddonType.Luma            => FallbackLuma,
        _ => ""
    };

    /// <summary>
    /// Resolves the info content for a game + addon combination using the three-tier priority system.
    /// </summary>
    public AddonInfoResult Resolve(
        GameCardViewModel card,
        AddonType addon,
        RemoteManifest? manifest,
        OptiScalerWikiData? osWikiData)
    {
        // ── Tier 1: Per-addon manifest dictionary ─────────────────────────────
        var manifestResult = TryResolveManifest(card.GameName, addon, manifest);
        if (manifestResult != null)
            return manifestResult;

        // ── Tier 2: Wiki-scraped content ──────────────────────────────────────
        var wikiResult = TryResolveWiki(card, addon, manifest, osWikiData);
        if (wikiResult != null)
            return wikiResult;

        // ── Tier 3: Generic fallback ──────────────────────────────────────────
        return new AddonInfoResult
        {
            Content = GetFallbackText(addon),
            Source = InfoSourceType.Fallback
        };
    }

    /// <summary>
    /// Returns the tooltip text for the Info button based on the resolved source type.
    /// </summary>
    public string GetTooltip(
        GameCardViewModel card,
        AddonType addon,
        RemoteManifest? manifest,
        OptiScalerWikiData? osWikiData)
    {
        var source = GetSourceType(card, addon, manifest, osWikiData);
        return source switch
        {
            InfoSourceType.Manifest => "Per-game notes available",
            InfoSourceType.Wiki     => "Wiki info available",
            InfoSourceType.Fallback => "General addon info",
            _ => "General addon info"
        };
    }

    /// <summary>
    /// Returns the InfoSourceType for the given game + addon combination,
    /// used for styling decisions (highlighted vs muted).
    /// </summary>
    public InfoSourceType GetSourceType(
        GameCardViewModel card,
        AddonType addon,
        RemoteManifest? manifest,
        OptiScalerWikiData? osWikiData)
    {
        // Tier 1: manifest
        if (HasManifestEntry(card.GameName, addon, manifest))
            return InfoSourceType.Manifest;

        // Tier 2: wiki
        if (HasWikiContent(card, addon, manifest, osWikiData))
            return InfoSourceType.Wiki;

        // Tier 3: fallback
        return InfoSourceType.Fallback;
    }

    // ── Private helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the per-addon manifest dictionary for the given addon type, or null.
    /// </summary>
    private static Dictionary<string, GameNoteEntry>? GetManifestDict(AddonType addon, RemoteManifest? manifest)
    {
        if (manifest == null) return null;
        return addon switch
        {
            AddonType.REFramework      => manifest.ReframeworkGameInfo,
            AddonType.ReShade          => manifest.ReshadeGameInfo,
            AddonType.RenoDX           => manifest.GameNotes,
            AddonType.ReLimiter        => manifest.RelimiterGameInfo,
            AddonType.DisplayCommander => manifest.DisplayCommanderGameInfo,
            AddonType.OptiScaler       => manifest.OptiScalerGameInfo,
            AddonType.Luma             => manifest.LumaGameInfo,
            _ => null
        };
    }

    /// <summary>
    /// Checks whether a per-addon manifest entry exists for the game.
    /// </summary>
    private static bool HasManifestEntry(string gameName, AddonType addon, RemoteManifest? manifest)
    {
        var dict = GetManifestDict(addon, manifest);
        return dict != null
            && dict.TryGetValue(gameName, out var entry)
            && !string.IsNullOrWhiteSpace(entry.Notes);
    }

    /// <summary>
    /// Attempts to resolve content from the per-addon manifest dictionary (Tier 1).
    /// </summary>
    private static AddonInfoResult? TryResolveManifest(string gameName, AddonType addon, RemoteManifest? manifest)
    {
        var dict = GetManifestDict(addon, manifest);
        if (dict == null) return null;

        if (!dict.TryGetValue(gameName, out var entry)) return null;
        if (string.IsNullOrWhiteSpace(entry.Notes)) return null;

        return new AddonInfoResult
        {
            Content = entry.Notes,
            Url = entry.NotesUrl,
            UrlLabel = entry.NotesUrlLabel,
            Source = InfoSourceType.Manifest
        };
    }

    /// <summary>
    /// Checks whether wiki content is available for the game + addon combination.
    /// </summary>
    private static bool HasWikiContent(
        GameCardViewModel card,
        AddonType addon,
        RemoteManifest? manifest,
        OptiScalerWikiData? osWikiData)
    {
        return addon switch
        {
            AddonType.RenoDX    => HasRenoDXWikiContent(card),
            AddonType.OptiScaler => HasOptiScalerWikiContent(card.GameName, manifest, osWikiData),
            AddonType.Luma      => HasLumaWikiContent(card, manifest),
            _ => false
        };
    }

    /// <summary>
    /// Attempts to resolve content from wiki sources (Tier 2).
    /// </summary>
    private static AddonInfoResult? TryResolveWiki(
        GameCardViewModel card,
        AddonType addon,
        RemoteManifest? manifest,
        OptiScalerWikiData? osWikiData)
    {
        return addon switch
        {
            AddonType.RenoDX    => TryResolveRenoDXWiki(card),
            AddonType.OptiScaler => TryResolveOptiScalerWiki(card.GameName, manifest, osWikiData),
            AddonType.Luma      => TryResolveLumaWiki(card, manifest),
            _ => null
        };
    }

    // ── RenoDX wiki (Tier 2a) ─────────────────────────────────────────────────

    private static bool HasRenoDXWikiContent(GameCardViewModel card)
    {
        // RenoDX wiki data is stored on the card via the Mod property (wiki-matched mod)
        if (card.Mod != null && !string.IsNullOrWhiteSpace(card.Mod.Notes))
            return true;
        // Also check the NameUrl (wiki page link)
        if (!string.IsNullOrEmpty(card.NameUrl))
            return true;
        return false;
    }

    private static AddonInfoResult? TryResolveRenoDXWiki(GameCardViewModel card)
    {
        if (!HasRenoDXWikiContent(card)) return null;

        var content = card.Mod?.Notes ?? "";
        return new AddonInfoResult
        {
            Content = content,
            Url = card.NameUrl,
            UrlLabel = !string.IsNullOrEmpty(card.NameUrl) ? "View wiki page" : null,
            Source = InfoSourceType.Wiki,
            WikiStatusLabel = card.WikiStatusLabel,
            WikiStatusBadgeBg = card.WikiStatusBadgeBackground,
            WikiStatusBadgeFg = card.WikiStatusBadgeForeground,
            WikiStatusBadgeBorder = card.WikiStatusBadgeBorderBrush
        };
    }

    // ── OptiScaler wiki (Tier 2b) ─────────────────────────────────────────────

    private static bool HasOptiScalerWikiContent(
        string gameName,
        RemoteManifest? manifest,
        OptiScalerWikiData? osWikiData)
    {
        if (osWikiData == null) return false;
        var wikiName = ResolveOptiScalerWikiName(gameName, manifest);
        return osWikiData.StandardCompat.ContainsKey(wikiName)
            || osWikiData.Fsr4Compat.ContainsKey(wikiName);
    }

    private static AddonInfoResult? TryResolveOptiScalerWiki(
        string gameName,
        RemoteManifest? manifest,
        OptiScalerWikiData? osWikiData)
    {
        if (osWikiData == null) return null;
        var wikiName = ResolveOptiScalerWikiName(gameName, manifest);

        osWikiData.StandardCompat.TryGetValue(wikiName, out var stdEntry);
        osWikiData.Fsr4Compat.TryGetValue(wikiName, out var fsr4Entry);

        if (stdEntry == null && fsr4Entry == null) return null;

        // Build content from available entries
        var content = FormatOptiScalerContent(stdEntry, fsr4Entry);
        var url = stdEntry?.DetailPageUrl ?? fsr4Entry?.DetailPageUrl;

        return new AddonInfoResult
        {
            Content = content,
            Url = url,
            UrlLabel = url != null ? "View wiki page" : null,
            Source = InfoSourceType.Wiki,
            OptiScalerCompat = stdEntry,
            OptiScalerFsr4Compat = fsr4Entry
        };
    }

    /// <summary>
    /// Resolves the wiki name for OptiScaler lookup, using manifest overrides if available.
    /// </summary>
    internal static string ResolveOptiScalerWikiName(string gameName, RemoteManifest? manifest)
    {
        if (manifest?.OptiScalerWikiNames != null
            && manifest.OptiScalerWikiNames.TryGetValue(gameName, out var wikiName)
            && !string.IsNullOrWhiteSpace(wikiName))
        {
            return wikiName;
        }
        // Strip trademark symbols so "Borderlands® 4" matches wiki "Borderlands 4"
        return StripTrademarks(gameName);
    }

    /// <summary>
    /// Strips ™ ® © symbols from a game name so Steam names match wiki names.
    /// </summary>
    private static string StripTrademarks(string name) =>
        name.Replace("™", "").Replace("®", "").Replace("©", "").Trim();

    private static string FormatOptiScalerContent(
        OptiScalerCompatEntry? stdEntry,
        OptiScalerCompatEntry? fsr4Entry)
    {
        var parts = new List<string>();

        if (stdEntry != null)
        {
            var upscalers = stdEntry.Upscalers.Count > 0
                ? string.Join(", ", stdEntry.Upscalers)
                : "None listed";
            parts.Add($"OptiScaler Compatibility: {stdEntry.Status}\nUpscalers: {upscalers}");
            if (!string.IsNullOrWhiteSpace(stdEntry.Notes))
                parts.Add(stdEntry.Notes);
        }

        if (fsr4Entry != null)
        {
            var upscalers = fsr4Entry.Upscalers.Count > 0
                ? string.Join(", ", fsr4Entry.Upscalers)
                : "None listed";
            parts.Add($"FSR4 Compatibility: {fsr4Entry.Status}\nUpscalers: {upscalers}");
            if (!string.IsNullOrWhiteSpace(fsr4Entry.Notes))
                parts.Add(fsr4Entry.Notes);
        }

        return string.Join("\n\n", parts);
    }

    // ── Luma wiki (Tier 2c) ───────────────────────────────────────────────────

    private static bool HasLumaWikiContent(GameCardViewModel card, RemoteManifest? manifest)
    {
        // Check LumaMod wiki data on the card
        if (card.LumaMod != null &&
            (!string.IsNullOrWhiteSpace(card.LumaMod.SpecialNotes) ||
             !string.IsNullOrWhiteSpace(card.LumaMod.FeatureNotes)))
            return true;

        // Check lumaGameNotes manifest section
        if (manifest?.LumaGameNotes != null
            && manifest.LumaGameNotes.TryGetValue(card.GameName, out var lumaNote)
            && !string.IsNullOrWhiteSpace(lumaNote.Notes))
            return true;

        return false;
    }

    private static AddonInfoResult? TryResolveLumaWiki(GameCardViewModel card, RemoteManifest? manifest)
    {
        if (!HasLumaWikiContent(card, manifest)) return null;

        var parts = new List<string>();
        string? url = null;
        string? urlLabel = null;

        // LumaMod wiki data
        if (card.LumaMod != null)
        {
            if (!string.IsNullOrWhiteSpace(card.LumaMod.SpecialNotes))
                parts.Add(card.LumaMod.SpecialNotes);
            if (!string.IsNullOrWhiteSpace(card.LumaMod.FeatureNotes))
                parts.Add(card.LumaMod.FeatureNotes);
        }

        // lumaGameNotes manifest section
        if (manifest?.LumaGameNotes != null
            && manifest.LumaGameNotes.TryGetValue(card.GameName, out var lumaNote)
            && !string.IsNullOrWhiteSpace(lumaNote.Notes))
        {
            parts.Add(lumaNote.Notes);
            url = lumaNote.NotesUrl;
            urlLabel = lumaNote.NotesUrlLabel;
        }

        if (parts.Count == 0) return null;

        return new AddonInfoResult
        {
            Content = string.Join("\n\n", parts),
            Url = url,
            UrlLabel = urlLabel,
            Source = InfoSourceType.Wiki
        };
    }
}
