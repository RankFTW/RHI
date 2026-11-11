using RHI.ManifestEditor.Json;

namespace RHI.ManifestEditor;

public enum FieldKind
{
    /// <summary>Game name is in a list (true/false).</summary>
    Flag,
    /// <summary>Dictionary game → string.</summary>
    Text,
    /// <summary>Dictionary game → number.</summary>
    Number,
    /// <summary>Dictionary game → string picked from a list.</summary>
    Choice,
    /// <summary>Dictionary game → { notes, notesUrl, notesUrlLabel }.</summary>
    Note,
    /// <summary>Dictionary game → { url, label }.</summary>
    Link,
    /// <summary>Dictionary game → { reshade, dc }.</summary>
    DllNames,
    /// <summary>Dictionary game → { key: value }.</summary>
    KeyValues,
    /// <summary>Dictionary game → { component: warning }.</summary>
    Warnings,
    /// <summary>Dictionary game → [ { name, subPath } ].</summary>
    Split,
    /// <summary>thirtyTwoBitGames / sixtyFourBitGames.</summary>
    Bitness,
    /// <summary>ueExtendedGames / nativeHdrGames / noUeExtendedGames / ueExtendedCompatibility.</summary>
    UeMode,
    /// <summary>graphicsApiOverrides as tick boxes.</summary>
    Api,
}

public sealed class GameField
{
    public required string Key { get; init; }
    public required FieldKind Kind { get; init; }
    public required string Label { get; init; }
    public string Help { get; init; } = "";
    public string? Placeholder { get; init; }
    public Func<JsonDoc, IEnumerable<string>>? Options { get; init; }
    /// <summary>Text box is multi-line.</summary>
    public bool Multiline { get; init; }
}

public sealed record GameGroup(string Title, string Description, GameField[] Fields);

/// <summary>One manifest field as listed in the Fields view (bitness and UE-Extended are split into their real lists).</summary>
public sealed record FieldView(string Key, string Label, string Title, string Help, string Group, GameField Field, bool IsCompat = false)
{
    public bool IsList => Field.Kind == FieldKind.Flag;
}

public static class Catalog
{
    public static readonly string[] UeModeKeys = { "ueExtendedGames", "nativeHdrGames", "noUeExtendedGames", "ueExtendedCompatibility" };
    public static readonly string[] BitnessKeys = { "thirtyTwoBitGames", "sixtyFourBitGames" };

    public static readonly (string Key, string Label)[] WarningComponents =
    {
        ("reshade", "ReShade"), ("renodx", "RenoDX"), ("luma", "Luma"), ("optiscaler", "OptiScaler"),
        ("relimiter", "ReLimiter"), ("dc", "Display Commander"), ("reframework", "RE Framework"), ("dxvk", "DXVK"),
    };

    public static readonly (string Token, string Label)[] ApiTokens =
    {
        ("DX8", "DirectX 8"), ("DX9", "DirectX 9"), ("DX10", "DirectX 10"), ("DX11", "DirectX 11"),
        ("DX12", "DirectX 12"), ("VLK", "Vulkan"), ("OGL", "OpenGL"),
    };

    public static readonly GameGroup[] Groups =
    {
        new("Detection and names", "How RHI finds the game and matches it to mods.", new GameField[]
        {
            new() { Key = "blacklist", Kind = FieldKind.Flag, Label = "Hide this from RHI",
                Help = "For things that aren't games (launchers, tools, soundtracks). It won't appear in RHI at all." },
            new() { Key = "wikiUnlinks", Kind = FieldKind.Flag, Label = "No RenoDX mod",
                Help = "Keeps the game in RHI but removes every RenoDX option, including the generic Unreal and Unity mods." },
            new() { Key = "wikiNameOverrides", Kind = FieldKind.Text, Label = "RenoDX wiki name",
                Help = "Use when the name RHI detects differs from the game's name on the RenoDX wiki.", Placeholder = "Name exactly as on the wiki" },
            new() { Key = "installPathOverrides", Kind = FieldKind.Text, Label = "Install subfolder",
                Help = "Folder inside the game folder where mods go. Separate alternatives with | (first one that exists wins). Use .. to go up a folder.",
                Placeholder = @"e.g. bin\x64 or Ship|.." },
            new() { Key = "installPathMigrations", Kind = FieldKind.Text, Label = "Move install subfolder",
                Help = "Like Install subfolder, but for games whose mod folder changes: wins over Install subfolder, and RHI moves the game's existing installs from the old folder to this one. Older RHI versions ignore it.",
                Placeholder = @"e.g. platform\steam" },
            new() { Key = "splitGames", Kind = FieldKind.Split, Label = "Split into several games",
                Help = "For collections with several games in one folder. Each one gets its own card. The subfolder may be empty (same folder)." },
            new() { Key = "steamAppIdOverrides", Kind = FieldKind.Number, Label = "Steam AppID",
                Help = "Forces the Steam AppID used for PCGamingWiki data and launching through Steam.", Placeholder = "e.g. 1091500" },
        }),
        new("Engine and graphics API", "Override what RHI detects about the game's engine, API and bitness.", new GameField[]
        {
            new() { Key = "engineHintOverrides", Kind = FieldKind.Text, Label = "Engine name or version",
                Help = "Shown on the engine badge exactly as typed, for any engine (e.g. KojiPro Engine). For Unreal games, write \"Unreal Engine\" plus the version: " +
                       "that decides UE4 vs UE5 behaviour (Engine.ini HDR keys, DOF Fix). It only changes the label: it doesn't move the game between the Unreal, Unity and Other filters.",
                Placeholder = "e.g. Unreal Engine 5.4.3 or KojiPro Engine" },
            new() { Key = "graphicsApiOverrides", Kind = FieldKind.Api, Label = "Graphics API",
                Help = "Replaces RHI's API detection. Tick more than one for games with several APIs. Decides ReShade's DLL name and whether DXVK is offered." },
            new() { Key = "bitness", Kind = FieldKind.Bitness, Label = "32 or 64-bit",
                Help = "Use when RHI detects it wrongly. Decides which DLLs are installed." },
            new() { Key = "engineIniPathOverrides", Kind = FieldKind.Text, Label = "Engine.ini project name",
                Help = "Unreal project folder name used to find Engine.ini under %LocalAppData%, or a full path. Separate alternatives with |." },
            new() { Key = "engineIniFiles", Kind = FieldKind.Text, Label = "Custom Engine.ini file",
                Help = "File name in rhi-repo's engine-files folder. RHI merges it into the game's Engine.ini instead of the standard HDR keys.",
                Placeholder = "e.g. black-myth-wukong.ini" },
            new() { Key = "dlssSkipGames", Kind = FieldKind.Flag, Label = "Don't scan for DLSS files",
                Help = "Skips the DLSS/Streamline file scan (false positives or very slow scans)." },
            new() { Key = "dlssPathOverrides", Kind = FieldKind.Text, Label = "DLSS files folder",
                Help = "Folder (inside the game folder) that holds the DLSS DLLs, when the scan misses them or is too slow." },
            new() { Key = "streamlinePathOverrides", Kind = FieldKind.Text, Label = "Streamline files folder",
                Help = "Folder (inside the game folder) that holds the Streamline DLLs (sl.common.dll etc.)." },
        }),
        new("RenoDX and Unreal", "RenoDX mod download, UE-Extended and RenoDX settings.", new GameField[]
        {
            new() { Key = "ueMode", Kind = FieldKind.UeMode, Label = "UE-Extended",
                Help = "Which RenoDX addon Unreal games use." },
            new() { Key = "snapshotOverrides", Kind = FieldKind.Text, Label = "RenoDX download link",
                Help = "Direct link to the game's addon, when the wiki has the wrong link or none. Don't use for generic Unreal games: it turns off UE-Extended.",
                Placeholder = "https://...addon64" },
            new() { Key = "forceExternalOnly", Kind = FieldKind.Link, Label = "Send to an external page",
                Help = "The install button opens this page instead of downloading (e.g. a Nexus or Discord mod). Uses the name RHI detects, not the wiki name." },
            new() { Key = "renodxIniOverrides", Kind = FieldKind.KeyValues, Label = "[renodx] settings",
                Help = "Written to the [renodx] section of reshade.ini on install and update. Values the user has already set are kept." },
            new() { Key = "wikiStatusOverrides", Kind = FieldKind.Text, Label = "Wiki status",
                Help = "Replaces the status icon from the wiki.", Placeholder = "e.g. ✅" },
            new() { Key = "authorOverrides", Kind = FieldKind.Text, Label = "Mod author",
                Help = "Author shown for mods that aren't on the wiki." },
            new() { Key = "dofFixForceGames", Kind = FieldKind.Flag, Label = "Always offer DOF Fix",
                Help = "For Unreal 5 games where version detection fails. The game must still be 64-bit." },
            new() { Key = "dofFixSkipGames", Kind = FieldKind.Flag, Label = "Never offer DOF Fix" },
        }),
        new("Luma", "Luma mod matching and compatibility.", new GameField[]
        {
            new() { Key = "lumaNameOverrides", Kind = FieldKind.Text, Label = "Luma name",
                Help = "Use when the name RHI detects differs from the name in Luma's mod list." },
            new() { Key = "lumaRenodxCompat", Kind = FieldKind.Flag, Label = "RenoDX works alongside Luma",
                Help = "Installing Luma normally removes RenoDX. Tick if both can be used together." },
            new() { Key = "lumaRequiresDgVoodoo", Kind = FieldKind.Flag, Label = "Luma needs dgVoodoo2",
                Help = "For DirectX 9 games: RHI installs dgVoodoo2 (D3D9.dll + dgVoodoo.conf) with Luma." },
        }),
        new("ReShade, DLLs and launching", "File names, ReShade version, exe and NVIDIA profile.", new GameField[]
        {
            new() { Key = "dllNameOverrides", Kind = FieldKind.DllNames, Label = "DLL names",
                Help = "File names ReShade and Display Commander are installed as. Leave a box empty for RHI's default." },
            new() { Key = "rtxEncoreDllNames", Kind = FieldKind.Choice, Label = "RTX Encore DLL name",
                Help = "Name RHI recommends (and lists first) when installing RTX Encore. Only a recommendation; version.dll otherwise.",
                // Same names as RtxEncoreService.KnownProxyNames; RHI ignores anything else
                Options = _ => new[] { "version.dll", "dinput8.dll", "winmm.dll", "dxgi.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll",
                                       "d3d12.dll", "dsound.dll", "wininet.dll", "winhttp.dll", "binkw64.dll", "bink2w64.dll",
                                       "xinput1_1.dll", "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll",
                                       "xinputuap.dll", "rtx-encore.asi" } },
            new() { Key = "legacyReShadeVersions", Kind = FieldKind.Choice, Label = "Legacy ReShade version",
                Help = "Locks the game to an older ReShade. Doesn't replace a ReShade version the user picked.",
                Options = d => (d.Get("legacyReShadeAvailable")?.Items ?? new()).Where(i => i.IsString).Select(i => i.Str!) },
            new() { Key = "launchExeOverrides", Kind = FieldKind.Text, Label = "Game exe",
                Help = "Exe path inside the install folder. Used to launch the game and to find its NVIDIA profile.", Placeholder = @"e.g. bin\game.exe" },
            new() { Key = "profileNameOverrides", Kind = FieldKind.Text, Label = "NVIDIA profile name",
                Help = "Exact name of the game's NVIDIA driver profile, when RHI can't find it." },
            new() { Key = "gacSymlinkGames", Kind = FieldKind.Text, Label = "GAC folder (XNA games)",
                Help = "Installs ReShade through a link in this .NET GAC folder (e.g. Terraria). Needs admin." },
            new() { Key = "pdUpscalerGames", Kind = FieldKind.Text, Label = "PD-Upscaler REFramework",
                Help = "RE Engine games: the REFramework nightly build RHI switches to when OptiScaler is installed.", Placeholder = "e.g. RE2" },
        }),
        new("DXVK", "DXVK availability.", new GameField[]
        {
            new() { Key = "dxvkBlacklist", Kind = FieldKind.Flag, Label = "Block DXVK",
                Help = "Greys out DXVK for this game (e.g. anti-cheat)." },
            new() { Key = "dxvkApiOverrides", Kind = FieldKind.Choice, Label = "DXVK API",
                Help = "Offers DXVK for games whose API RHI can't detect.", Options = _ => new[] { "DX8", "DX9", "DX10", "DX11" } },
        }),
        new("Links", "Pages RHI links to for this game.", new GameField[]
        {
            new() { Key = "nexusUrlOverrides", Kind = FieldKind.Text, Label = "Nexus Mods page", Placeholder = "https://www.nexusmods.com/..." },
            new() { Key = "pcgwUrlOverrides", Kind = FieldKind.Text, Label = "PCGamingWiki page", Placeholder = "https://www.pcgamingwiki.com/wiki/..." },
            new() { Key = "uwFixUrlOverrides", Kind = FieldKind.Text, Label = "Ultrawide fix page" },
            new() { Key = "ultraPlusUrlOverrides", Kind = FieldKind.Text, Label = "Ultra+ page" },
            new() { Key = "optiScalerWikiNames", Kind = FieldKind.Text, Label = "OptiScaler wiki name",
                Help = "Name in the OptiScaler compatibility list, when it differs." },
        }),
        new("Install warnings", "A message the user must confirm before installing a component.", new GameField[]
        {
            new() { Key = "installWarnings", Kind = FieldKind.Warnings, Label = "Warnings" },
        }),
        new("Info button text", "Extra text in each component's Info window for this game.", new GameField[]
        {
            new() { Key = "gameNotes", Kind = FieldKind.Note, Label = "RenoDX" },
            new() { Key = "reshadeGameInfo", Kind = FieldKind.Note, Label = "ReShade" },
            new() { Key = "lumaGameInfo", Kind = FieldKind.Note, Label = "Luma" },
            new() { Key = "lumaGameNotes", Kind = FieldKind.Note, Label = "Luma (extra notes)" },
            new() { Key = "optiScalerGameInfo", Kind = FieldKind.Note, Label = "OptiScaler" },
            new() { Key = "relimiterGameInfo", Kind = FieldKind.Note, Label = "ReLimiter" },
            new() { Key = "displayCommanderGameInfo", Kind = FieldKind.Note, Label = "Display Commander" },
            new() { Key = "reframeworkGameInfo", Kind = FieldKind.Note, Label = "RE Framework" },
            new() { Key = "dxvkGameNotes", Kind = FieldKind.Note, Label = "DXVK" },
        }),
    };

    /// <summary>Every per-game field as its own list entry, for the Fields view.</summary>
    public static readonly FieldView[] FieldViews = BuildFieldViews().ToArray();

    private static IEnumerable<FieldView> BuildFieldViews()
    {
        static GameField ListField(string key, string label, string help) =>
            new() { Key = key, Kind = FieldKind.Flag, Label = label, Help = help };

        foreach (var g in Groups)
            foreach (var f in g.Fields)
            {
                if (f.Kind == FieldKind.Bitness)
                {
                    yield return new("thirtyTwoBitGames", "32-bit games", "Forced 32-bit", "Games RHI treats as 32-bit, when detection gets it wrong. Decides which DLLs are installed.", g.Title,
                        ListField("thirtyTwoBitGames", "32-bit", ""));
                    yield return new("sixtyFourBitGames", "64-bit games", "Forced 64-bit", "Games RHI treats as 64-bit, when detection gets it wrong. Decides which DLLs are installed.", g.Title,
                        ListField("sixtyFourBitGames", "64-bit", ""));
                }
                else if (f.Kind == FieldKind.UeMode)
                {
                    yield return new("nativeHdrGames", "UE-Extended, native HDR", "UE-Extended, native HDR",
                        "For games with their own HDR: forces UE-Extended (Set_Path=0) even if the game has a named RenoDX mod, and hides the toggle.", g.Title,
                        ListField("nativeHdrGames", "Native HDR", ""));
                    yield return new("ueExtendedGames", "UE-Extended (SDR to HDR)", "UE-Extended (SDR to HDR)",
                        "Uses the UE-Extended addon to upgrade SDR to HDR (Set_Path=1). A named RenoDX mod for the game still wins.", g.Title,
                        ListField("ueExtendedGames", "UE-Extended", ""));
                    yield return new("ueExtendedCompatibility", "UE-Extended Engine.ini choices", "UE-Extended with Engine.ini choices",
                        "Forces UE-Extended like native HDR, and chooses which Engine.ini keys RHI writes. Games added here are also added to native HDR.", g.Title,
                        new GameField { Key = "ueExtendedCompatibility", Kind = FieldKind.UeMode, Label = "Engine.ini choices" }, IsCompat: true);
                    yield return new("noUeExtendedGames", "Never UE-Extended", "Never use UE-Extended",
                        "Blocks UE-Extended completely, even if the user turns it on.", g.Title,
                        ListField("noUeExtendedGames", "Never UE-Extended", ""));
                }
                else if (f.Kind == FieldKind.Warnings)
                    yield return new(f.Key, "Install warnings", "Install warnings", g.Description, g.Title, f);
                else if (f.Kind == FieldKind.Note)
                    yield return new(f.Key, $"{f.Label} info", $"{f.Label} info text", $"Extra text in the {f.Label} Info window for these games.", g.Title, f);
                else
                    yield return new(f.Key, f.Label, LabelFor(f.Key), f.Help, g.Title, f);
            }
    }

    /// <summary>Game names in a field, in file order (duplicates removed).</summary>
    public static List<string> GamesIn(JsonDoc doc, string key)
    {
        var node = doc.Get(key);
        if (node is { IsArray: true }) return node.Items!.Where(i => i.IsString).Select(i => i.Str!).Distinct().ToList();
        if (node is { IsObject: true }) return node.Members!.Select(m => m.Key).Distinct().ToList();
        return new();
    }

    /// <summary>Fields that are lists of game names.</summary>
    public static readonly string[] ListKeys =
    {
        "blacklist", "wikiUnlinks", "thirtyTwoBitGames", "sixtyFourBitGames", "dlssSkipGames",
        "nativeHdrGames", "ueExtendedGames", "noUeExtendedGames", "lumaRenodxCompat", "lumaRequiresDgVoodoo",
        "dxvkBlacklist", "dofFixForceGames", "dofFixSkipGames",
    };

    /// <summary>Fields that are dictionaries keyed by game name.</summary>
    public static readonly string[] DictKeys = Groups.SelectMany(g => g.Fields)
        .Where(f => f.Kind is not (FieldKind.Flag or FieldKind.Bitness or FieldKind.UeMode))
        .Select(f => f.Key)
        .Append("ueExtendedCompatibility")
        .Distinct()
        .ToArray();

    /// <summary>Every (field, game) pair in the manifest.</summary>
    public static IEnumerable<(string Field, string Game)> GameEntries(JsonDoc doc)
    {
        foreach (var key in ListKeys)
            if (doc.Get(key) is { IsArray: true } arr)
                foreach (var i in arr.Items!.Where(i => i.IsString))
                    yield return (key, i.Str!);
        foreach (var key in DictKeys)
            if (doc.Get(key) is { IsObject: true } obj)
                foreach (var m in obj.Members!)
                    yield return (key, m.Key);
    }

    /// <summary>Plain-English name of a manifest field, for summaries.</summary>
    public static string LabelFor(string key)
    {
        if (key is "thirtyTwoBitGames") return "32-bit";
        if (key is "sixtyFourBitGames") return "64-bit";
        if (key is "ueExtendedGames") return "UE-Extended";
        if (key is "nativeHdrGames") return "UE-Extended (native HDR)";
        if (key is "noUeExtendedGames") return "Never UE-Extended";
        if (key is "ueExtendedCompatibility") return "UE-Extended (Engine.ini settings)";
        foreach (var g in Groups)
            foreach (var f in g.Fields)
                if (f.Key == key) return g.Title == "Info button text" ? $"{f.Label} info text" : f.Label;
        return key;
    }
}
