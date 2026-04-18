using System.Collections.Concurrent;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Owns card building, game detection orchestration, and manifest application.
/// Extracted from MainViewModel per Requirement 1.6.
/// </summary>
public class GameInitializationService : IGameInitializationService
{
    private readonly IGameDetectionService _gameDetectionService;
    private readonly IWikiService _wikiService;
    private readonly IManifestService _manifestService;
    private readonly IModInstallService _installer;
    private readonly IAuxInstallService _auxInstaller;
    private readonly IGameLibraryService _gameLibraryService;
    private readonly IPeHeaderService _peHeaderService;
    private readonly ILumaService _lumaService;
    private readonly IReShadeUpdateService _rsUpdateService;
    private readonly IShaderPackService _shaderPackService;

    public GameInitializationService(
        IGameDetectionService gameDetectionService,
        IWikiService wikiService,
        IManifestService manifestService,
        IModInstallService installer,
        IAuxInstallService auxInstaller,
        IGameLibraryService gameLibraryService,
        IPeHeaderService peHeaderService,
        ILumaService lumaService,
        IReShadeUpdateService rsUpdateService,
        IShaderPackService shaderPackService)
    {
        _gameDetectionService = gameDetectionService;
        _wikiService = wikiService;
        _manifestService = manifestService;
        _installer = installer;
        _auxInstaller = auxInstaller;
        _gameLibraryService = gameLibraryService;
        _peHeaderService = peHeaderService;
        _lumaService = lumaService;
        _rsUpdateService = rsUpdateService;
        _shaderPackService = shaderPackService;
    }

    /// <summary>
    /// Detects games from all supported stores and deduplicates by name and install path.
    /// </summary>
    public async Task<List<DetectedGame>> DetectAllGamesDedupedAsync()
    {
        // Wrap each platform scan so that a failure in one does not abort others (Requirement 7.3).
        Task<List<DetectedGame>> SafeScan(Func<List<DetectedGame>> scan, string platform) =>
            Task.Run(() =>
            {
                try { return scan(); }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[GameInitializationService.DetectAllGamesDedupedAsync] {platform} scan failed — {ex.Message}");
                    return new List<DetectedGame>();
                }
            });

        var tasks = new[]
        {
            SafeScan(_gameDetectionService.FindSteamGames, "Steam"),
            SafeScan(_gameDetectionService.FindGogGames, "GOG"),
            SafeScan(_gameDetectionService.FindEpicGames, "Epic"),
            SafeScan(_gameDetectionService.FindEaGames, "EA"),
            SafeScan(_gameDetectionService.FindXboxGames, "Xbox"),
            SafeScan(_gameDetectionService.FindUbisoftGames, "Ubisoft"),
            SafeScan(_gameDetectionService.FindBattleNetGames, "Battle.net"),
            SafeScan(_gameDetectionService.FindRockstarGames, "Rockstar"),
        };
        var results = await Task.WhenAll(tasks);

        var all = results.SelectMany(r => r).ToList();

        // Step 1: deduplicate exact same name from multiple stores
        var byName = all
            .GroupBy(g => _gameDetectionService.NormalizeName(g.Name))
            .Select(grp => grp.First())
            .ToList();

        // Step 2: deduplicate by install path
        var byPath = byName
            .GroupBy(g => g.InstallPath.TrimEnd('\\', '/').ToLowerInvariant())
            .Select(grp => grp.OrderBy(g => g.Name.Length).First())
            .ToList();

        // Permanently exclude specific non-game entries
        var permanentExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Lossless Scaling",
            "Steamworks Common Redistributables",
            "QuickPasta",
            "Apple Music",
            "DSX",
            "PlayStation®VR2 App",
            "SteamVR",
            "Telegram Desktop",
            "Windows",
        };
        byPath = byPath.Where(g => !permanentExclusions.Contains(g.Name)).ToList();

        // Exclude system/OS paths
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                         .TrimEnd('\\', '/').ToLowerInvariant();
        var sysRoot = (Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\WINDOWS")
                         .TrimEnd('\\', '/').ToLowerInvariant();
        byPath = byPath.Where(g =>
        {
            var p = g.InstallPath.TrimEnd('\\', '/').ToLowerInvariant();
            return p != winDir && p != sysRoot && !p.StartsWith(winDir + "\\");
        }).ToList();

        return byPath;
    }

    // ── Manifest application ──────────────────────────────────────────────────

    /// <summary>
    /// Seeds the working sets with values from the remote manifest.
    /// Local user overrides always take priority.
    /// Must be called BEFORE BuildCards.
    /// </summary>
    public void ApplyManifest(
        RemoteManifest? manifest,
        IGameNameService gameNameService,
        IDllOverrideService dllOverrideService,
        HashSet<string> manifestNativeHdrGames,
        HashSet<string> manifestBlacklist,
        HashSet<string> manifest32BitGames,
        HashSet<string> manifest64BitGames,
        Dictionary<string, string> manifestEngineOverrides,
        Dictionary<string, ManifestDllNames> manifestDllNameOverrides,
        HashSet<string> manifestWikiUnlinks,
        Dictionary<string, string> installPathOverrides,
        Func<string, string> normalizeForLookup)
    {
        manifestNativeHdrGames.Clear();
        manifestWikiUnlinks.Clear();
        manifest32BitGames.Clear();
        manifest64BitGames.Clear();
        manifestEngineOverrides.Clear();
        manifestDllNameOverrides.Clear();

        dllOverrideService.ClearManifestOverrides();
        if (manifest == null) return;

        if (manifest.WikiNameOverrides != null)
        {
            foreach (var (key, value) in manifest.WikiNameOverrides)
            {
                // Always mark as manifest-origin so the UI hides it and saves exclude it
                gameNameService.MarkManifestNameMapping(key);
                if (!gameNameService.NameMappings.ContainsKey(key))
                    gameNameService.NameMappings[key] = value;
            }
        }

        if (manifest.UeExtendedGames != null)
            foreach (var game in manifest.UeExtendedGames)
                gameNameService.UeExtendedGames.Add(game);

        if (manifest.NativeHdrGames != null)
            foreach (var game in manifest.NativeHdrGames)
                manifestNativeHdrGames.Add(game);

        if (manifest.ThirtyTwoBitGames != null)
            foreach (var game in manifest.ThirtyTwoBitGames)
                manifest32BitGames.Add(game);

        if (manifest.SixtyFourBitGames != null)
            foreach (var game in manifest.SixtyFourBitGames)
                manifest64BitGames.Add(game);

        manifestBlacklist.Clear();
        if (manifest.Blacklist != null)
            foreach (var game in manifest.Blacklist)
                manifestBlacklist.Add(game);

        if (manifest.InstallPathOverrides != null)
            foreach (var (key, value) in manifest.InstallPathOverrides)
                installPathOverrides.TryAdd(key, value);

        if (manifest.WikiUnlinks != null)
            foreach (var game in manifest.WikiUnlinks)
                manifestWikiUnlinks.Add(game);

        if (manifest.EngineOverrides != null)
            foreach (var (key, value) in manifest.EngineOverrides)
                manifestEngineOverrides[key] = value;

        if (manifest.DllNameOverrides != null)
            foreach (var (key, value) in manifest.DllNameOverrides)
                manifestDllNameOverrides[key] = value;

        if (manifest.DllNameOverrides != null)
        {
            var manifestKeys = new HashSet<string>(
                manifest.DllNameOverrides.Keys, StringComparer.OrdinalIgnoreCase);
            dllOverrideService.PruneOptOuts(manifestKeys, normalizeForLookup);
        }

        CrashReporter.Log($"[GameInitializationService.ApplyManifest] v{manifest.Version}, " +
            $"+{manifest.WikiNameOverrides?.Count ?? 0} name overrides, " +
            $"+{manifest.UeExtendedGames?.Count ?? 0} UE-Ext, " +
            $"+{manifest.NativeHdrGames?.Count ?? 0} NativeHDR, " +
            $"+{manifest.ThirtyTwoBitGames?.Count ?? 0} 32-bit, " +
            $"+{manifest.SixtyFourBitGames?.Count ?? 0} 64-bit, " +
            $"+{manifest.Blacklist?.Count ?? 0} blacklisted, " +
            $"+{manifest.InstallPathOverrides?.Count ?? 0} path overrides, " +
            $"+{manifest.WikiStatusOverrides?.Count ?? 0} status overrides, " +
            $"+{manifest.SnapshotOverrides?.Count ?? 0} snapshot overrides, " +
            $"+{manifest.WikiUnlinks?.Count ?? 0} wiki unlinks, " +
            $"+{manifest.EngineOverrides?.Count ?? 0} engine overrides, " +
            $"+{manifest.DllNameOverrides?.Count ?? 0} DLL name overrides");
    }

    /// <summary>
    /// Applies wiki status overrides from the remote manifest to the fetched mod list.
    /// </summary>
    public void ApplyManifestStatusOverrides(RemoteManifest? manifest, List<GameMod> allMods)
    {
        var overrides = manifest?.WikiStatusOverrides;
        if (overrides == null || overrides.Count == 0) return;

        foreach (var mod in allMods)
        {
            if (overrides.TryGetValue(mod.Name, out var status))
                mod.Status = status;
        }
    }

    /// <summary>
    /// Applies manifest-driven card overrides AFTER the hardcoded ApplyCardOverrides pass.
    /// </summary>
    public static void ApplyManifestCardOverrides(RemoteManifest? manifest, List<GameCardViewModel> cards)
    {
        if (manifest == null) return;

        if (manifest.GameNotes != null)
        {
            foreach (var card in cards)
            {
                if (!manifest.GameNotes.TryGetValue(card.GameName, out var noteEntry)) continue;
                if (!string.IsNullOrWhiteSpace(noteEntry.Notes))
                    card.Notes = noteEntry.Notes;
                if (!string.IsNullOrEmpty(noteEntry.NotesUrl) && string.IsNullOrEmpty(card.NotesUrl))
                {
                    card.NotesUrl      = noteEntry.NotesUrl;
                    card.NotesUrlLabel = noteEntry.NotesUrlLabel;
                }
            }
        }

        if (manifest.ForceExternalOnly != null)
        {
            foreach (var card in cards)
            {
                if (!manifest.ForceExternalOnly.TryGetValue(card.GameName, out var ext)) continue;
                // Always apply manifest URL/label — even if already external-only from wiki matching,
                // the manifest entry should override the default Discord fallback.
                if (card.Mod != null)
                    card.Mod.SnapshotUrl = null;
                card.IsExternalOnly = true;
                card.ExternalUrl    = ext.Url ?? "https://discord.gg/gF4GRJWZ2A";
                card.ExternalLabel  = ext.Label ?? "Download from Discord";
                // Set WikiStatus based on the actual download source
                bool isNexus = ext.Url != null && ext.Url.Contains("nexusmods", StringComparison.OrdinalIgnoreCase);
                card.WikiStatus     = isNexus ? "🌐" : "💬";
                // Clear DiscordUrl if the manifest points to a non-Discord source,
                // so the Discord badge doesn't show when the download is from Nexus etc.
                if (ext.Url != null && !ext.Url.Contains("discord", StringComparison.OrdinalIgnoreCase))
                    card.DiscordUrl = null;
            }
        }

        if (manifest.LumaGameNotes != null)
        {
            foreach (var card in cards)
            {
                if (!manifest.LumaGameNotes.TryGetValue(card.GameName, out var lumaNote)) continue;
                if (!string.IsNullOrWhiteSpace(lumaNote.Notes))
                    card.LumaNotes = lumaNote.Notes;
                if (!string.IsNullOrEmpty(lumaNote.NotesUrl))
                {
                    card.LumaNotesUrl      = lumaNote.NotesUrl;
                    card.LumaNotesUrlLabel = lumaNote.NotesUrlLabel;
                }
            }
        }

        if (manifest.AuthorOverrides != null)
        {
            foreach (var card in cards)
            {
                if (!manifest.AuthorOverrides.TryGetValue(card.GameName, out var author)) continue;
                if (string.IsNullOrWhiteSpace(card.Maintainer))
                    card.Maintainer = author;
            }
        }
    }

    /// <summary>
    /// Applies hardcoded per-game card overrides after BuildCards completes.
    /// </summary>
    public static void ApplyCardOverrides(List<GameCardViewModel> cards)
    {
        var overrides = new Dictionary<string, CardOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cyberpunk 2077"] = new CardOverride(
                Notes: "⚠️ The RenoDX mod for Cyberpunk 2077 is a WIP. " +
                       "Always get the latest build directly from the RenoDX Discord — " +
                       "it is updated more frequently than any wiki download.\n\n" +
                       "See Creepy's Cyberpunk RenoDX Guide for setup instructions:",
                DiscordUrl: "https://discord.gg/gF4GRJWZ2A",
                ForceDiscord: true,
                NameUrl:       "https://www.hdrmods.com/Cyberpunk",
                NotesUrl:      "https://www.hdrmods.com/Cyberpunk",
                NotesUrlLabel: "Creepy's Cyberpunk RenoDX Guide"),

            ["Dishonored"] = new CardOverride(
                Notes: "ℹ️ This game has both 32-bit and 64-bit RenoDX builds.\n\n" +
                       "• 64-bit (default) — for the Microsoft Store and Epic Games Store versions.\n" +
                       "• 32-bit — for the Steam version. Enable 32-bit mode in 🎯 Overrides to use this.",
                DiscordUrl: null, ForceDiscord: false),
        };

        foreach (var card in cards)
        {
            if (!overrides.TryGetValue(card.GameName, out var ov)) continue;

            if (ov.ForceDiscord)
            {
                if (card.Mod != null)
                    card.Mod.SnapshotUrl = null;
                card.IsExternalOnly  = true;
                card.ExternalUrl     = ov.DiscordUrl ?? "https://discord.gg/gF4GRJWZ2A";
                card.ExternalLabel   = "Download from Discord";
                card.DiscordUrl      = ov.DiscordUrl ?? "https://discord.gg/gF4GRJWZ2A";
                card.WikiStatus      = "💬";
            }

            if (!string.IsNullOrEmpty(ov.Notes))
                card.Notes = ov.Notes;

            if (!string.IsNullOrEmpty(ov.NameUrl))
                card.NameUrl = ov.NameUrl;

            if (!string.IsNullOrEmpty(ov.NotesUrl))
            {
                card.NotesUrl      = ov.NotesUrl;
                card.NotesUrlLabel = ov.NotesUrlLabel;
            }
        }
    }

    private record CardOverride(
        string? Notes,
        string? DiscordUrl,
        bool ForceDiscord,
        string? NameUrl       = null,
        string? NotesUrl      = null,
        string? NotesUrlLabel = null);
}
