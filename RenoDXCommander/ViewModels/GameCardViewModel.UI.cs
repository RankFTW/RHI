using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

// UI state: IsSelected, CardHighlighted, ComponentExpanded, sidebar props, visibility, display
public partial class GameCardViewModel
{
    // ── Sidebar item styling (computed from IsSelected + managed state) ────────────
    public string SidebarItemBackground => IsSelected ? "#1A2840" : "Transparent";
    public string SidebarItemBorderBrush => IsSelected ? "#2A4060" : "Transparent";
    public string SidebarItemForeground => IsSelected ? "#E2E8FF"
        : IsManaged ? "#C8D4E8"   // brighter — something is installed
        : "#5A6880";              // dimmer — untouched game

    // Card highlight styling (computed from CardHighlighted)
    public string CardBackground => CardHighlighted ? "#1A2840" : "#141820";
    public string CardBorderBrush => CardHighlighted ? "#2A4060" : "#1E2430";

    // ── Card grid: component status dot colors ────────────────────────────────────
    private static string StatusDotColor(GameStatus s, bool installing) =>
        installing   ? "#2196F3"
        : s == GameStatus.Installed       ? "#4CAF50"
        : s == GameStatus.UpdateAvailable ? "#FF9800"
        : "#5A6880";

    public string CardRdxStatusDot  => StatusDotColor(Status, IsInstalling);
    public string CardRsStatusDot   => RequiresVulkanInstall
        ? (RsIsInstalling ? "#2196F3" : IsLayerInstalledFunc() ? "#4CAF50" : "#5A6880")
        : StatusDotColor(RsStatus, RsIsInstalling);
    public string CardLumaStatusDot => StatusDotColor(LumaStatus, IsLumaInstalling);

    /// <summary>True when the Luma status dot should be visible on the card grid.</summary>
    public bool CardLumaVisible => LumaFeatureEnabled && IsLumaMode && LumaMod != null;

    // ── Card grid: action and info properties ─────────────────────────────────────
    /// <summary>Label for the card's primary action button.</summary>
    public string CardPrimaryActionLabel
    {
        get
        {
            var effectiveStatus = (LumaFeatureEnabled && IsLumaMode && LumaMod != null)
                ? LumaStatus : Status;
            var effectiveInstalling = (LumaFeatureEnabled && IsLumaMode && LumaMod != null)
                ? IsLumaInstalling : IsInstalling;

            if (effectiveInstalling) return "Installing...";
            if (IsManaged)
            {
                // Any component has an update available → show update icon
                if (effectiveStatus == GameStatus.UpdateAvailable
                    || RsStatus == GameStatus.UpdateAvailable
                    || LumaStatus == GameStatus.UpdateAvailable)
                    return "⬆  Manage";
                return "↺  Manage";
            }
            return "⬇  Install";
        }
    }

    /// <summary>True when the game has notes or a wiki/name link — shows info indicator on card.</summary>
    public bool HasInfoIndicator => HasNotes || HasNameUrl;

    /// <summary>False when any component is currently installing — disables card install button.</summary>
    public bool CanCardInstall => !IsInstalling && !RsIsInstalling && !IsLumaInstalling && !UlIsInstalling && !DcIsInstalling;

    // ── Per-component install enabled (card install flyout) ───────────────────────
    public bool CardRdxInstallEnabled  => !IsInstalling && Mod?.SnapshotUrl != null && !IsExternalOnly && IsRsInstalled;
    public bool CardRsInstallEnabled   => !RsIsInstalling;
    public bool CardLumaInstallEnabled => !IsLumaInstalling && LumaMod?.DownloadUrl != null;

    private void NotifySidebarProps()
    {
        OnPropertyChanged(nameof(SidebarItemBackground));
        OnPropertyChanged(nameof(SidebarItemBorderBrush));
        OnPropertyChanged(nameof(SidebarItemForeground));
        OnPropertyChanged(nameof(IsManaged));
    }

    partial void OnIsSelectedChanged(bool value) => NotifySidebarProps();
    partial void OnCardHighlightedChanged(bool value)
    {
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
    }

    /// <summary>Visibility for the individual component detail section.</summary>
    public Visibility ComponentDetailVisibility => ComponentExpanded ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Chevron glyph for expand/collapse.</summary>
    public string ExpandChevron => ComponentExpanded ? "▲" : "▼";

    partial void OnComponentExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ComponentDetailVisibility));
        OnPropertyChanged(nameof(ExpandChevron));
    }

    // ── Derived display ───────────────────────────────────────────────────────────

    public string WikiStatusLabel => WikiStatus == "✅" ? "✅ Working"
                                   : WikiStatus == "🚧" ? "🚧 In Progress"
                                   : WikiStatus == "?"  ? "⚠️ May Work"
                                   : WikiStatus == "💬" ? "💬 Discord"
                                   : WikiStatus == "🌐" ? "🌐 Nexus"
                                   : WikiStatus == "—" && IsGenericMod ? "⚠️ May Work"
                                   : "❓ Unknown";

    /// <summary>
    /// Returns just the wiki status icon for grid card display.
    /// </summary>
    public string WikiStatusIcon => EffectiveLumaMode ? ""
                                  : WikiStatus == "✅" ? ""
                                  : WikiStatus == "🚧" ? "🚧"
                                  : WikiStatus == "?"  ? "⚠️"
                                  : WikiStatus == "💬" ? "💬"
                                  : WikiStatus == "🌐" ? "🌐"
                                  : WikiStatus == "—" && IsGenericMod ? "⚠️"
                                  : "❓";

    /// <summary>Whether the wiki status icon should be visible on grid cards (hidden in Luma mode).</summary>
    public bool WikiStatusIconVisible => !EffectiveLumaMode;

    // Badge colours change per status to make them visually distinct
    public string WikiStatusBadgeBackground  => WikiStatus == "💬" ? "#201838"
                                              : WikiStatus == "🌐" ? "#182840"
                                              : WikiStatus == "?"  ? "#201C10"
                                              : "#1A2030";
    public string WikiStatusBadgeBorderBrush => WikiStatus == "💬" ? "#3A2860"
                                              : WikiStatus == "🌐" ? "#2A4468"
                                              : WikiStatus == "?"  ? "#403018"
                                              : "#283240";
    public string WikiStatusBadgeForeground  => WikiStatus == "💬" ? "#B898E8"
                                              : WikiStatus == "🌐" ? "#7AACDD"
                                              : WikiStatus == "?"  ? "#D4A856"
                                              : "#A0AABB";

    public string SourceIcon => Source switch
    {
        "Steam" => "🟦", "GOG" => "🟣", "Epic" => "🟤", "EA App" => "🟧",
        "Ubisoft" => "🟠", "Manual" => "🔧", _ => "🎮"
    };

    public string? SourceIconPath => Source switch
    {
        "Steam"      => "Assets/icons/steam.ico",
        "GOG"        => "Assets/icons/gog.ico",
        "Epic"       => "Assets/icons/epic.ico",
        "EA App"     => "Assets/icons/ea.ico",
        "Xbox"       => "Assets/icons/xbox.ico",
        "Ubisoft"    => "Assets/icons/ubisoft.ico",
        "Battle.net" => "Assets/icons/battlenet.ico",
        "Rockstar"   => "Assets/icons/rockstar.ico",
        _            => null
    };

    /// <summary>
    /// Returns a pack URI for the source icon, or a dummy transparent URI when no icon exists.
    /// </summary>
    public Uri SourceIconUri => SourceIconPath != null
        ? new Uri($"ms-appx:///{SourceIconPath}")
        : new Uri("ms-appx:///Assets/icons/steam.ico");

    public bool HasSourceIcon => SourceIconPath != null;

    public Visibility SourceIconImageVisibility =>
        SourceIconPath != null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SourceIconTextVisibility =>
        SourceIconPath == null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visible when the game is flagged as 32-bit (shows badge next to source/engine).</summary>
    public Visibility Is32BitBadgeVisibility => Is32Bit ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visible on 32-bit UE cards — shows WIP placeholder instead of install button.</summary>
    public Visibility Is32BitUeWipVisibility =>
        (Is32Bit && IsGenericMod && EngineHint.Contains("Unreal") && !EngineHint.Contains("Legacy"))
            ? Visibility.Visible : Visibility.Collapsed;

    public string InstallPathDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(InstallPath)) return "";
            var parts = InstallPath.TrimEnd('\\', '/').Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 2 ? $"...\\{parts[^2]}\\{parts[^1]}" : InstallPath;
        }
    }

    public string InstalledFileLabel  => InstalledAddonFileName != null ? $"📦 {InstalledAddonFileName}" : "";
    public bool HasNotes              => !string.IsNullOrWhiteSpace(Notes);
    public bool IsUnityGeneric        => IsGenericMod && EngineHint.Contains("Unity");
    public bool HasDualBitMod         => Mod?.HasBothBitVersions == true;
    public bool HasExtraLinks         => NexusUrl != null || (DiscordUrl != null && EffectiveLumaMode) || IsExternalOnly;
    public bool HasNexusModsUrl       => !string.IsNullOrEmpty(NexusModsUrl);
    public bool HasPcgwUrl            => !string.IsNullOrEmpty(PcgwUrl);
    public bool HasNameUrl            => !string.IsNullOrEmpty(NameUrl);
    public string HideButtonLabel     => IsHidden ? "👁 Show" : "🚫 Hide";
    public string StarForeground       => IsFavourite ? "#FFD700" : "#282840";
    public Visibility IsFavouriteVisibility      => IsFavourite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsNotFavouriteVisibility   => IsFavourite ? Visibility.Collapsed : Visibility.Visible;

    // ── Visibility ────────────────────────────────────────────────────────────────

    public Visibility SourceBadgeVisibility      => string.IsNullOrEmpty(Source) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GenericBadgeVisibility     => IsGenericMod ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EngineBadgeVisibility      => !string.IsNullOrEmpty(EngineHint) ? Visibility.Visible : Visibility.Collapsed;
    public string GraphicsApiLabel               => GraphicsApiDetector.GetMultiLabel(DetectedApis, GraphicsApi);
    public bool HasGraphicsApiBadge              => GraphicsApi != GraphicsApiType.Unknown;
    public Visibility NotesButtonVisibility      => HasNotes ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProgressVisibility         => IsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MessageVisibility          => string.IsNullOrEmpty(ActionMessage) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ExternalBtnVisibility      => IsExternalOnly && !EffectiveLumaMode && CombinedRowVisibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExtraLinkVisibility        => HasExtraLinks ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InstalledFileLabelVisible  => !string.IsNullOrEmpty(InstalledAddonFileName) && !EffectiveLumaMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InstallOnlyBtnVisibility   => (!IsExternalOnly && Mod?.SnapshotUrl != null
                                                      && Status == GameStatus.Available
                                                      && Is32BitUeWipVisibility == Visibility.Collapsed
                                                      && !EffectiveLumaMode)
                                                      ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ReinstallRowVisibility     => (!IsExternalOnly && Mod?.SnapshotUrl != null
                                                      && (Status == GameStatus.Installed || Status == GameStatus.UpdateAvailable)
                                                      && !EffectiveLumaMode)
                                                      ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DualBitInstallVisibility   => Visibility.Collapsed;
    public Visibility UpdateBadgeVisibility      => ((Status == GameStatus.UpdateAvailable && !ExcludeFromUpdateAllRenoDx)
                                                      || (RsStatus == GameStatus.UpdateAvailable && !ExcludeFromUpdateAllReShade && !EffectiveLumaMode)
                                                      || (UlStatus == GameStatus.UpdateAvailable && !ExcludeFromUpdateAllUl)
                                                      || (DcStatus == GameStatus.UpdateAvailable && !ExcludeFromUpdateAllDc)
                                                      || (RefStatus == GameStatus.UpdateAvailable && !ExcludeFromUpdateAllRef))
                                                      ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsHiddenVisibility         => IsHidden ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsNotHiddenVisibility      => IsHidden ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NameLinkVisibility         => HasNameUrl ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoModVisibility            => Visibility.Collapsed;
    public Visibility SwitchToLumaVisibility     => (Mod == null && string.IsNullOrEmpty(InstalledAddonFileName)
                                                      && !EffectiveLumaMode
                                                      && LumaFeatureEnabled && IsLumaAvailable)
                                                      ? Visibility.Visible : Visibility.Collapsed;

    partial void OnVulkanRenderingPathChanged(string value)
    {
        OnPropertyChanged(nameof(RequiresVulkanInstall));
        OnPropertyChanged(nameof(IsVulkanOnly));
        OnPropertyChanged(nameof(CardRsStatusDot));
    }

    partial void OnIsHiddenChanged(bool value) => OnPropertyChanged(nameof(HideButtonLabel));
    partial void OnIsFavouriteChanged(bool value)
    {
        OnPropertyChanged(nameof(StarForeground));
        OnPropertyChanged(nameof(IsFavouriteVisibility));
        OnPropertyChanged(nameof(IsNotFavouriteVisibility));
    }
    partial void OnInstallPathChanged(string value) => OnPropertyChanged(nameof(InstallPathDisplay));
    partial void OnSourceChanged(string value) => OnPropertyChanged(nameof(SourceBadgeVisibility));

    // ── Mod author computed properties ────────────────────────────────────────────

    /// <summary>
    /// Returns the author(s) to display for this game card.
    /// - Generic UE (not UE-Extended): ShortFuse
    /// - UE-Extended (manifest or toggled): Marat only
    /// - Generic Unity: Voosh
    /// - Named mods: wiki Maintainer field, split on "&amp;"
    /// </summary>
    /// <summary>Display-name overrides for wiki maintainer handles.</summary>
    private static readonly Dictionary<string, string> AuthorDisplayNames =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["oopydoopy"] = "Jon",
    };

    /// <summary>Donation page URLs keyed by display name (after resolution).</summary>
    private static readonly Dictionary<string, string> AuthorDonationUrls =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["ShortFuse"] = "https://ko-fi.com/shortfuse",
        ["Jon"]       = "https://ko-fi.com/kickfister",
        ["Forge"]     = "https://ko-fi.com/forge87682",
        ["Voosh"]     = "https://ko-fi.com/notvoosh",
        ["Musa"]      = "https://ko-fi.com/musaqh",
        ["Pumbo"]     = "https://ko-fi.com/pumbo",
        ["Nukem"]     = "https://ko-fi.com/nukem9",
        ["Lilium"]    = "https://ko-fi.com/endlesslyflowering",
        ["Bit Viper"] = "https://ko-fi.com/bitviper",
    };

    /// <summary>Returns the donation URL for the given author display name, or null if none is known.</summary>
    public static string? GetAuthorDonationUrl(string displayName) =>
        AuthorDonationUrls.TryGetValue(displayName, out var url) ? url : null;

    /// <summary>
    /// Merges manifest-provided donation URLs and display-name overrides into the
    /// hardcoded dictionaries. Manifest entries take priority over hardcoded ones.
    /// </summary>
    public static void MergeManifestAuthorData(
        Dictionary<string, string>? donationUrls,
        Dictionary<string, string>? displayNames)
    {
        if (displayNames != null)
            foreach (var (key, value) in displayNames)
                AuthorDisplayNames[key] = value;

        if (donationUrls != null)
            foreach (var (key, value) in donationUrls)
                AuthorDonationUrls[key] = value;
    }

    /// <summary>Splits an author string on '&amp;' or ' and ' (case-insensitive), trims, and drops empties.</summary>
    private static IEnumerable<string> SplitAuthors(string raw) =>
        System.Text.RegularExpressions.Regex.Split(raw, @"\s+and\s+|&", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(a => a.Trim())
            .Where(a => a.Length > 0);

    /// <summary>Resolves a single author segment to its display name.
    /// Strips parenthesised aliases (e.g. "oopydoopy (Jon)") before lookup.</summary>
    private static string ResolveAuthorName(string raw)
    {
        // Strip trailing parenthesised alias: "oopydoopy (Jon)" → "oopydoopy"
        var parenIdx = raw.IndexOf('(');
        var key = (parenIdx > 0 ? raw[..parenIdx].Trim() : raw);
        return AuthorDisplayNames.TryGetValue(key, out var display) ? display : raw;
    }

    public string[] AuthorList
    {
        get
        {
            // Luma mode: show the Luma mod author instead of the RenoDX author
            if (EffectiveLumaMode && LumaMod != null && !string.IsNullOrWhiteSpace(LumaMod.Author))
                return SplitAuthors(LumaMod.Author).ToArray();

            // UE-Extended overrides everything — credit goes to Marat alone
            if (UseUeExtended || IsManifestUeExtended)
                return new[] { "Marat" };

            // Named mod with a maintainer from the wiki — resolve display names
            if (!string.IsNullOrWhiteSpace(Maintainer))
                return SplitAuthors(Maintainer).Select(ResolveAuthorName).ToArray();

            // Generic engine mods without a named maintainer
            if (IsGenericMod)
            {
                if (EngineHint?.Contains("Unreal", StringComparison.OrdinalIgnoreCase) == true)
                    return new[] { "ShortFuse" };
                if (EngineHint?.Contains("Unity", StringComparison.OrdinalIgnoreCase) == true)
                    return new[] { "Voosh" };
            }

            return Array.Empty<string>();
        }
    }

    /// <summary>True when at least one author name is present.</summary>
    public bool HasAuthors => AuthorList.Length > 0;
}
