namespace RenoDXCommander;

/// <summary>
/// String constants for all theme resource keys defined in DarkTheme.xaml.
/// Eliminates magic strings when looking up brushes and colours from the resource dictionary.
/// </summary>
/// <remarks>
/// Constants are organised into three categories:
/// <list type="bullet">
///   <item><description>Color keys — raw colour resources (e.g. "SurfaceBase").</description></item>
///   <item><description>Brush keys — <c>SolidColorBrush</c> resources (e.g. "SurfaceBaseBrush").</description></item>
///   <item><description>Style / layout keys — styles and layout constants (e.g. "CardRadius").</description></item>
/// </list>
/// </remarks>
public static class ResourceKeys
{
    // ── Color keys: Base surfaces ───────────────────────────────────────

    /// <summary>Resource key for the base application surface colour.</summary>
    public const string SurfaceBase = "SurfaceBase";
    /// <summary>Resource key for the raised surface colour (cards, panels).</summary>
    public const string SurfaceRaised = "SurfaceRaised";
    /// <summary>Resource key for the overlay surface colour (dialogs, popups).</summary>
    public const string SurfaceOverlay = "SurfaceOverlay";
    /// <summary>Resource key for the header surface colour.</summary>
    public const string SurfaceHeader = "SurfaceHeader";
    /// <summary>Resource key for the status bar surface colour.</summary>
    public const string SurfaceStatusBar = "SurfaceStatusBar";
    /// <summary>Resource key for the sidebar surface colour.</summary>
    public const string SurfaceSidebar = "SurfaceSidebar";
    /// <summary>Resource key for the toolbar surface colour.</summary>
    public const string SurfaceToolbar = "SurfaceToolbar";
    /// <summary>Resource key for the input field surface colour.</summary>
    public const string SurfaceInput = "SurfaceInput";
    /// <summary>Resource key for the component table surface colour.</summary>
    public const string SurfaceComponentTable = "SurfaceComponentTable";

    // ── Color keys: Borders ─────────────────────────────────────────────

    /// <summary>Resource key for the subtle border colour.</summary>
    public const string BorderSubtle = "BorderSubtle";
    /// <summary>Resource key for the default border colour.</summary>
    public const string BorderDefault = "BorderDefault";
    /// <summary>Resource key for the strong border colour.</summary>
    public const string BorderStrong = "BorderStrong";

    // ── Color keys: Text ────────────────────────────────────────────────

    /// <summary>Resource key for the primary text colour.</summary>
    public const string TextPrimary = "TextPrimary";
    /// <summary>Resource key for the secondary text colour.</summary>
    public const string TextSecondary = "TextSecondary";
    /// <summary>Resource key for the tertiary text colour.</summary>
    public const string TextTertiary = "TextTertiary";
    /// <summary>Resource key for the disabled text colour.</summary>
    public const string TextDisabled = "TextDisabled";

    // ── Color keys: Accent Teal ─────────────────────────────────────────

    /// <summary>Resource key for the teal accent colour.</summary>
    public const string AccentTeal = "AccentTeal";
    /// <summary>Resource key for the dimmed teal accent colour.</summary>
    public const string AccentTealDim = "AccentTealDim";
    /// <summary>Resource key for the teal accent background colour.</summary>
    public const string AccentTealBg = "AccentTealBg";
    /// <summary>Resource key for the teal accent border colour.</summary>
    public const string AccentTealBorder = "AccentTealBorder";

    // ── Color keys: Accent Green ────────────────────────────────────────

    /// <summary>Resource key for the green accent colour.</summary>
    public const string AccentGreen = "AccentGreen";
    /// <summary>Resource key for the dimmed green accent colour.</summary>
    public const string AccentGreenDim = "AccentGreenDim";
    /// <summary>Resource key for the green accent background colour.</summary>
    public const string AccentGreenBg = "AccentGreenBg";
    /// <summary>Resource key for the green accent border colour.</summary>
    public const string AccentGreenBorder = "AccentGreenBorder";

    // ── Color keys: Accent Purple ───────────────────────────────────────

    /// <summary>Resource key for the purple accent colour.</summary>
    public const string AccentPurple = "AccentPurple";
    /// <summary>Resource key for the dimmed purple accent colour.</summary>
    public const string AccentPurpleDim = "AccentPurpleDim";
    /// <summary>Resource key for the purple accent background colour.</summary>
    public const string AccentPurpleBg = "AccentPurpleBg";
    /// <summary>Resource key for the purple accent border colour.</summary>
    public const string AccentPurpleBorder = "AccentPurpleBorder";

    // ── Color keys: Accent Amber ────────────────────────────────────────

    /// <summary>Resource key for the amber accent colour.</summary>
    public const string AccentAmber = "AccentAmber";
    /// <summary>Resource key for the dimmed amber accent colour.</summary>
    public const string AccentAmberDim = "AccentAmberDim";
    /// <summary>Resource key for the amber accent background colour.</summary>
    public const string AccentAmberBg = "AccentAmberBg";

    // ── Color keys: Accent Red ──────────────────────────────────────────

    /// <summary>Resource key for the red accent colour.</summary>
    public const string AccentRed = "AccentRed";
    /// <summary>Resource key for the red accent background colour.</summary>
    public const string AccentRedBg = "AccentRedBg";

    // ── Color keys: Accent Blue ─────────────────────────────────────────

    /// <summary>Resource key for the blue accent colour.</summary>
    public const string AccentBlue = "AccentBlue";
    /// <summary>Resource key for the blue accent background colour.</summary>
    public const string AccentBlueBg = "AccentBlueBg";
    /// <summary>Resource key for the blue accent border colour.</summary>
    public const string AccentBlueBorder = "AccentBlueBorder";

    // ── Color keys: Filter chips ────────────────────────────────────────

    /// <summary>Resource key for the default filter chip colour.</summary>
    public const string ChipDefault = "ChipDefault";
    /// <summary>Resource key for the active filter chip colour.</summary>
    public const string ChipActive = "ChipActive";
    /// <summary>Resource key for the filter chip text colour.</summary>
    public const string ChipText = "ChipText";

    // ── Color keys: Sidebar ─────────────────────────────────────────────

    /// <summary>Resource key for the selected sidebar item colour.</summary>
    public const string SidebarItemSelected = "SidebarItemSelected";
    /// <summary>Resource key for the selected sidebar item border colour.</summary>
    public const string SidebarItemSelectedBorder = "SidebarItemSelectedBorder";
    /// <summary>Resource key for the selected sidebar item text colour.</summary>
    public const string SidebarItemSelectedText = "SidebarItemSelectedText";
    /// <summary>Resource key for the hovered sidebar item colour.</summary>
    public const string SidebarItemHover = "SidebarItemHover";
    /// <summary>Resource key for the hovered sidebar item border colour.</summary>
    public const string SidebarItemHoverBorder = "SidebarItemHoverBorder";
    /// <summary>Resource key for the sidebar item text colour.</summary>
    public const string SidebarItemText = "SidebarItemText";
    /// <summary>Resource key for the sidebar update badge colour.</summary>
    public const string SidebarUpdateBadge = "SidebarUpdateBadge";

    // ── Color keys: Inline description ──────────────────────────────────

    /// <summary>Resource key for the inline description text colour.</summary>
    public const string InlineDescriptionText = "InlineDescriptionText";

    // ── Color keys: Settings header ─────────────────────────────────────

    /// <summary>Resource key for the settings header text colour.</summary>
    public const string SettingsHeaderText = "SettingsHeaderText";

    // ── Color keys: Card grid ───────────────────────────────────────────

    /// <summary>Resource key for the card background colour.</summary>
    public const string CardBackground = "CardBackground";
    /// <summary>Resource key for the highlighted card background colour.</summary>
    public const string CardHighlightBackground = "CardHighlightBackground";
    /// <summary>Resource key for the card border colour.</summary>
    public const string CardBorder = "CardBorder";
    /// <summary>Resource key for the highlighted card border colour.</summary>
    public const string CardHighlightBorder = "CardHighlightBorder";

    // ── Brush keys: Surfaces ────────────────────────────────────────────

    /// <summary>Brush resource key for the base application surface.</summary>
    public const string SurfaceBaseBrush = "SurfaceBaseBrush";
    /// <summary>Brush resource key for the raised surface (cards, panels).</summary>
    public const string SurfaceRaisedBrush = "SurfaceRaisedBrush";
    /// <summary>Brush resource key for the overlay surface (dialogs, popups).</summary>
    public const string SurfaceOverlayBrush = "SurfaceOverlayBrush";
    /// <summary>Brush resource key for the header surface.</summary>
    public const string SurfaceHeaderBrush = "SurfaceHeaderBrush";
    /// <summary>Brush resource key for the status bar surface.</summary>
    public const string SurfaceStatusBarBrush = "SurfaceStatusBarBrush";
    /// <summary>Brush resource key for the sidebar surface.</summary>
    public const string SurfaceSidebarBrush = "SurfaceSidebarBrush";
    /// <summary>Brush resource key for the toolbar surface.</summary>
    public const string SurfaceToolbarBrush = "SurfaceToolbarBrush";
    /// <summary>Brush resource key for the input field surface.</summary>
    public const string SurfaceInputBrush = "SurfaceInputBrush";
    /// <summary>Brush resource key for the component table surface.</summary>
    public const string SurfaceComponentTableBrush = "SurfaceComponentTableBrush";

    // ── Brush keys: Borders ─────────────────────────────────────────────

    /// <summary>Brush resource key for the subtle border.</summary>
    public const string BorderSubtleBrush = "BorderSubtleBrush";
    /// <summary>Brush resource key for the default border.</summary>
    public const string BorderDefaultBrush = "BorderDefaultBrush";
    /// <summary>Brush resource key for the strong border.</summary>
    public const string BorderStrongBrush = "BorderStrongBrush";

    // ── Brush keys: Text ────────────────────────────────────────────────

    /// <summary>Brush resource key for primary text.</summary>
    public const string TextPrimaryBrush = "TextPrimaryBrush";
    /// <summary>Brush resource key for secondary text.</summary>
    public const string TextSecondaryBrush = "TextSecondaryBrush";
    /// <summary>Brush resource key for tertiary text.</summary>
    public const string TextTertiaryBrush = "TextTertiaryBrush";
    /// <summary>Brush resource key for disabled text.</summary>
    public const string TextDisabledBrush = "TextDisabledBrush";

    // ── Brush keys: Accent Teal ─────────────────────────────────────────

    /// <summary>Brush resource key for the teal accent.</summary>
    public const string AccentTealBrush = "AccentTealBrush";
    /// <summary>Brush resource key for the dimmed teal accent.</summary>
    public const string AccentTealDimBrush = "AccentTealDimBrush";
    /// <summary>Brush resource key for the teal accent background.</summary>
    public const string AccentTealBgBrush = "AccentTealBgBrush";
    /// <summary>Brush resource key for the teal accent border.</summary>
    public const string AccentTealBorderBrush = "AccentTealBorderBrush";

    // ── Brush keys: Accent Green ────────────────────────────────────────

    /// <summary>Brush resource key for the green accent.</summary>
    public const string AccentGreenBrush = "AccentGreenBrush";
    /// <summary>Brush resource key for the dimmed green accent.</summary>
    public const string AccentGreenDimBrush = "AccentGreenDimBrush";
    /// <summary>Brush resource key for the green accent background.</summary>
    public const string AccentGreenBgBrush = "AccentGreenBgBrush";
    /// <summary>Brush resource key for the green accent border.</summary>
    public const string AccentGreenBorderBrush = "AccentGreenBorderBrush";

    // ── Brush keys: Accent Purple ───────────────────────────────────────

    /// <summary>Brush resource key for the purple accent.</summary>
    public const string AccentPurpleBrush = "AccentPurpleBrush";
    /// <summary>Brush resource key for the dimmed purple accent.</summary>
    public const string AccentPurpleDimBrush = "AccentPurpleDimBrush";
    /// <summary>Brush resource key for the purple accent background.</summary>
    public const string AccentPurpleBgBrush = "AccentPurpleBgBrush";
    /// <summary>Brush resource key for the purple accent border.</summary>
    public const string AccentPurpleBorderBrush = "AccentPurpleBorderBrush";

    // ── Brush keys: Accent Amber ────────────────────────────────────────

    /// <summary>Brush resource key for the amber accent.</summary>
    public const string AccentAmberBrush = "AccentAmberBrush";
    /// <summary>Brush resource key for the dimmed amber accent.</summary>
    public const string AccentAmberDimBrush = "AccentAmberDimBrush";
    /// <summary>Brush resource key for the amber accent background.</summary>
    public const string AccentAmberBgBrush = "AccentAmberBgBrush";

    // ── Brush keys: Accent Red ──────────────────────────────────────────

    /// <summary>Brush resource key for the red accent.</summary>
    public const string AccentRedBrush = "AccentRedBrush";
    /// <summary>Brush resource key for the red accent background.</summary>
    public const string AccentRedBgBrush = "AccentRedBgBrush";

    // ── Brush keys: Accent Blue ─────────────────────────────────────────

    /// <summary>Brush resource key for the blue accent.</summary>
    public const string AccentBlueBrush = "AccentBlueBrush";
    /// <summary>Brush resource key for the blue accent background.</summary>
    public const string AccentBlueBgBrush = "AccentBlueBgBrush";
    /// <summary>Brush resource key for the blue accent border.</summary>
    public const string AccentBlueBorderBrush = "AccentBlueBorderBrush";

    // ── Brush keys: Filter chips ────────────────────────────────────────

    /// <summary>Brush resource key for the default filter chip.</summary>
    public const string ChipDefaultBrush = "ChipDefaultBrush";
    /// <summary>Brush resource key for the active filter chip.</summary>
    public const string ChipActiveBrush = "ChipActiveBrush";
    /// <summary>Brush resource key for the filter chip text.</summary>
    public const string ChipTextBrush = "ChipTextBrush";

    // ── Brush keys: Sidebar ─────────────────────────────────────────────

    /// <summary>Brush resource key for the selected sidebar item.</summary>
    public const string SidebarItemSelectedBrush = "SidebarItemSelectedBrush";
    /// <summary>Brush resource key for the selected sidebar item border.</summary>
    public const string SidebarItemSelectedBorderBrush = "SidebarItemSelectedBorderBrush";
    /// <summary>Brush resource key for the selected sidebar item text.</summary>
    public const string SidebarItemSelectedTextBrush = "SidebarItemSelectedTextBrush";
    /// <summary>Brush resource key for the hovered sidebar item.</summary>
    public const string SidebarItemHoverBrush = "SidebarItemHoverBrush";
    /// <summary>Brush resource key for the hovered sidebar item border.</summary>
    public const string SidebarItemHoverBorderBrush = "SidebarItemHoverBorderBrush";
    /// <summary>Brush resource key for the sidebar item text.</summary>
    public const string SidebarItemTextBrush = "SidebarItemTextBrush";
    /// <summary>Brush resource key for the sidebar update badge.</summary>
    public const string SidebarUpdateBadgeBrush = "SidebarUpdateBadgeBrush";

    // ── Brush keys: Inline description ──────────────────────────────────

    /// <summary>Brush resource key for inline description text.</summary>
    public const string InlineDescriptionBrush = "InlineDescriptionBrush";

    // ── Brush keys: Settings header ─────────────────────────────────────

    /// <summary>Brush resource key for the settings header text.</summary>
    public const string SettingsHeaderBrush = "SettingsHeaderBrush";

    // ── Brush keys: Card grid ───────────────────────────────────────────

    /// <summary>Brush resource key for the card background.</summary>
    public const string CardBackgroundBrush = "CardBackgroundBrush";
    /// <summary>Brush resource key for the highlighted card background.</summary>
    public const string CardHighlightBackgroundBrush = "CardHighlightBackgroundBrush";
    /// <summary>Brush resource key for the card border.</summary>
    public const string CardBorderBrush = "CardBorderBrush";
    /// <summary>Brush resource key for the highlighted card border.</summary>
    public const string CardHighlightBorderBrush = "CardHighlightBorderBrush";

    // ── Style keys ──────────────────────────────────────────────────────

    /// <summary>Style resource key for inline description text blocks.</summary>
    public const string InlineDescriptionStyle = "InlineDescriptionStyle";
    /// <summary>Style resource key for settings section header text blocks.</summary>
    public const string SettingsSectionHeaderStyle = "SettingsSectionHeaderStyle";

    // ── Layout constant keys ────────────────────────────────────────────

    /// <summary>Layout resource key for the card item width.</summary>
    public const string CardItemWidth = "CardItemWidth";
    /// <summary>Layout resource key for the card corner radius.</summary>
    public const string CardRadius = "CardRadius";
    /// <summary>Layout resource key for the button corner radius.</summary>
    public const string ButtonRadius = "ButtonRadius";
    /// <summary>Layout resource key for the filter chip corner radius.</summary>
    public const string ChipRadius = "ChipRadius";
    /// <summary>Layout resource key for the badge corner radius.</summary>
    public const string BadgeRadius = "BadgeRadius";
    /// <summary>Layout resource key for the card internal padding.</summary>
    public const string CardPadding = "CardPadding";
    /// <summary>Layout resource key for the card external margin.</summary>
    public const string CardMargin = "CardMargin";
}
