using CommunityToolkit.Mvvm.ComponentModel;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

public partial class GameCardViewModel : ObservableObject
{
    // ── Core observable properties ────────────────────────────────────────────────
    [ObservableProperty] private string _gameName = "";
    [ObservableProperty] private string _maintainer = "";
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _installPath = "";
    [ObservableProperty] private string _wikiStatus = "✅";
    [ObservableProperty] private GameStatus _status = GameStatus.NotInstalled;
    [ObservableProperty] private bool _isInstalling = false;
    [ObservableProperty] private double _installProgress = 0;
    [ObservableProperty] private string _actionMessage = "";
    [ObservableProperty] private string? _installedAddonFileName;
    [ObservableProperty] private bool _isHidden = false;
    [ObservableProperty] private bool _isFavourite = false;

    [ObservableProperty] private bool _isExternalOnly;
    [ObservableProperty] private bool _isGenericMod;
    [ObservableProperty] private string _externalUrl   = "";
    [ObservableProperty] private string _externalLabel = "";
    [ObservableProperty] private string? _nexusUrl;
    [ObservableProperty] private string? _discordUrl;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private GameMod? _mod;
    [ObservableProperty] private bool _useUeExtended;

    // ── ReShade state ─────────────────────────────────────────────────────────────
    [ObservableProperty] private GameStatus _rsStatus  = GameStatus.NotInstalled;
    [ObservableProperty] private bool       _rsIsInstalling;
    [ObservableProperty] private double     _rsProgress;
    [ObservableProperty] private string     _rsActionMessage = "";
    [ObservableProperty] private string?    _rsInstalledFile;
    [ObservableProperty] private string?    _rsInstalledVersion;
    public AuxInstalledRecord? RsRecord { get; set; }

    // ── Vulkan / dual-API state ──────────────────────────────────────────────────
    [ObservableProperty] private string _vulkanRenderingPath = "DirectX";

    // Plain properties — not mutated after card creation, no need to observe
    public HashSet<GraphicsApiType> DetectedApis { get; set; } = new();
    public bool IsDualApiGame { get; set; }

    // Computed Vulkan properties
    public bool IsVulkanOnly => GraphicsApi == GraphicsApiType.Vulkan && !IsDualApiGame;
    public bool RequiresVulkanInstall => IsVulkanOnly || (IsDualApiGame && VulkanRenderingPath == "Vulkan");
    public bool ShowRenderingPathToggle => IsDualApiGame;

    public string EngineHint    { get; set; } = "";
    public GraphicsApiType GraphicsApi { get; set; } = GraphicsApiType.Unknown;
    public string? NameUrl      { get; set; }
    public bool ExcludeFromUpdateAllReShade { get; set; }
    public bool ExcludeFromUpdateAllRenoDx  { get; set; }
    /// <summary>Per-game shader selection override: null = follow global selection.</summary>
    public string? ShaderModeOverride { get; set; }

    // ── 32-bit mode ───────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _is32Bit = false;

    // ── Ultra Limiter state ──────────────────────────────────────────────────────
    [ObservableProperty] private GameStatus _ulStatus  = GameStatus.NotInstalled;
    [ObservableProperty] private bool       _ulIsInstalling;
    [ObservableProperty] private double     _ulProgress;
    [ObservableProperty] private string     _ulActionMessage = "";
    [ObservableProperty] private string?    _ulInstalledFile;

    // ── DLL Naming Override ─────────────────────────────────────────────────────
    [ObservableProperty] private bool _dllOverrideEnabled = false;

    // ── Luma Framework state ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLumaMode = false;
    [ObservableProperty] private bool _isLumaInstalling = false;
    [ObservableProperty] private double _lumaProgress = 0;
    [ObservableProperty] private string _lumaActionMessage = "";
    [ObservableProperty] private GameStatus _lumaStatus = GameStatus.NotInstalled;
    public LumaMod? LumaMod { get; set; }
    public LumaInstalledRecord? LumaRecord { get; set; }
    /// <summary>Global toggle — Luma UI is always enabled.</summary>
    [ObservableProperty] private bool _lumaFeatureEnabled = true;

    // ── Sidebar selection state ───────────────────────────────────────────────────
    [ObservableProperty] private bool _isSelected;

    // ── Card grid highlight state ─────────────────────────────────────────────────
    [ObservableProperty] private bool _cardHighlighted = false;

    // ── Component detail expand/collapse ──────────────────────────────────────────
    [ObservableProperty] private bool _componentExpanded;

    // ── Plain properties ──────────────────────────────────────────────────────────
    public string? NotesUrl     { get; set; }
    public string? NotesUrlLabel { get; set; }
    public string? LumaNotes     { get; set; }
    public string? LumaNotesUrl  { get; set; }
    public string? LumaNotesUrlLabel { get; set; }
    public bool IsManuallyAdded { get; set; }
    public DetectedGame? DetectedGame         { get; set; }
    public InstalledModRecord? InstalledRecord { get; set; }

    /// <summary>Set by MainViewModel for games that default to UE-Extended + Native HDR label.</summary>
    public bool IsNativeHdrGame { get; set; }

    /// <summary>Set by MainViewModel for games in the manifest ueExtendedGames list (but NOT native HDR).</summary>
    public bool IsManifestUeExtended { get; set; }

    /// <summary>
    /// Refreshes all computed properties. Called by external code after bulk state changes.
    /// Uses a <see cref="HashSet{T}"/> guard to ensure each property is notified at most once,
    /// even though the underlying Notify*Dependents methods share overlapping property sets.
    /// </summary>
    public void NotifyAll()
    {
        var notified = new HashSet<string>(StringComparer.Ordinal);

        void NotifyOnce(string propertyName)
        {
            if (notified.Add(propertyName))
                OnPropertyChanged(propertyName);
        }

        // ── RenoDX: Status dependents ─────────────────────────────────────────
        NotifyOnce(nameof(InstallActionLabel));
        NotifyOnce(nameof(CanInstall));
        NotifyOnce(nameof(GenericModLabel));
        NotifyOnce(nameof(InstallBtnBackground));
        NotifyOnce(nameof(InstallBtnForeground));
        NotifyOnce(nameof(InstallBtnBorderBrush));
        NotifyOnce(nameof(InstallOnlyBtnVisibility));
        NotifyOnce(nameof(ReinstallRowVisibility));
        NotifyOnce(nameof(IsNotInstalling));
        NotifyOnce(nameof(IsRdxInstalled));
        NotifyOnce(nameof(RdxStatusText));
        NotifyOnce(nameof(RdxStatusColor));
        NotifyOnce(nameof(RdxShortAction));
        NotifyOnce(nameof(CardRdxStatusDot));
        NotifyOnce(nameof(CardRdxInstallEnabled));
        NotifyOnce(nameof(UpdateBadgeVisibility));
        NotifyOnce(nameof(CombinedStatusDot));
        NotifyOnce(nameof(CombinedActionLabel));
        NotifyOnce(nameof(CanCombinedInstall));
        NotifyOnce(nameof(CombinedBtnBackground));
        NotifyOnce(nameof(CombinedBtnForeground));
        NotifyOnce(nameof(CombinedBtnBorderBrush));
        NotifyOnce(nameof(CombinedRowVisibility));
        NotifyOnce(nameof(ComponentExpandVisibility));
        NotifyOnce(nameof(ChevronCornerRadius));
        NotifyOnce(nameof(ChevronBorderThickness));
        NotifyOnce(nameof(R7bInstallCornerRadius));
        NotifyOnce(nameof(R7bInstallBorderThickness));
        NotifyOnce(nameof(R7bInstallMargin));
        NotifyOnce(nameof(IsManaged));
        NotifyOnce(nameof(SidebarItemForeground));
        NotifyOnce(nameof(CardPrimaryActionLabel));
        NotifyOnce(nameof(CanCardInstall));
        NotifyOnce(nameof(ExternalBtnVisibility));
        NotifyOnce(nameof(NoModVisibility));
        NotifyOnce(nameof(SwitchToLumaVisibility));

        // ── RenoDX: IsInstalling dependents ───────────────────────────────────
        NotifyOnce(nameof(ProgressVisibility));

        // ── ReShade: RsStatus dependents ──────────────────────────────────────
        NotifyOnce(nameof(RsStatusDot));
        NotifyOnce(nameof(RsActionLabel));
        NotifyOnce(nameof(RsBtnBackground));
        NotifyOnce(nameof(RsBtnForeground));
        NotifyOnce(nameof(RsBtnBorderBrush));
        NotifyOnce(nameof(RsDeleteVisibility));
        NotifyOnce(nameof(RsInstalledVisible));
        NotifyOnce(nameof(RsStatusText));
        NotifyOnce(nameof(RsStatusColor));
        NotifyOnce(nameof(RsShortAction));
        NotifyOnce(nameof(RsInstallCornerRadius));
        NotifyOnce(nameof(RsInstallBorderThickness));
        NotifyOnce(nameof(RsInstallMargin));
        NotifyOnce(nameof(RsIniCornerRadius));
        NotifyOnce(nameof(RsIniBorderThickness));
        NotifyOnce(nameof(RsIniMargin));
        NotifyOnce(nameof(IsRsInstalled));
        NotifyOnce(nameof(CardRsStatusDot));
        NotifyOnce(nameof(CardRsInstallEnabled));

        // ── ReShade: RsIsInstalling dependents ────────────────────────────────
        NotifyOnce(nameof(RsProgressVisibility));
        NotifyOnce(nameof(IsRsNotInstalling));

        // ── Luma: LumaStatus dependents ───────────────────────────────────────
        NotifyOnce(nameof(LumaActionLabel));
        NotifyOnce(nameof(LumaInstallVisibility));
        NotifyOnce(nameof(LumaReinstallVisibility));
        NotifyOnce(nameof(LumaStatusText));
        NotifyOnce(nameof(LumaStatusColor));
        NotifyOnce(nameof(LumaShortAction));
        NotifyOnce(nameof(LumaBtnBackground));
        NotifyOnce(nameof(LumaBtnForeground));
        NotifyOnce(nameof(LumaBtnBorderBrush));
        NotifyOnce(nameof(IsLumaInstalled));
        NotifyOnce(nameof(CardLumaStatusDot));
        NotifyOnce(nameof(CardLumaInstallEnabled));

        // ── Luma: IsLumaInstalling dependents ─────────────────────────────────
        NotifyOnce(nameof(IsLumaNotInstalling));
        NotifyOnce(nameof(LumaProgressVisibility));

        // ── Luma: LumaFeatureEnabled dependents ───────────────────────────────
        NotifyOnce(nameof(LumaBadgeVisibility));
        NotifyOnce(nameof(CardLumaVisible));
        NotifyOnce(nameof(R7bLumaSwitchVisibility));
        NotifyOnce(nameof(R7bLumaSwitchCornerRadius));
        NotifyOnce(nameof(R7bLumaSwitchBorderThickness));
        NotifyOnce(nameof(R7bLumaSwitchMargin));
        NotifyOnce(nameof(RenoDxRowVisibility));
        NotifyOnce(nameof(ReShadeRowVisibility));
        NotifyOnce(nameof(InstalledFileLabelVisible));
        NotifyOnce(nameof(WikiStatusIcon));
        NotifyOnce(nameof(WikiStatusIconVisible));
        NotifyOnce(nameof(HasExtraLinks));
        NotifyOnce(nameof(ExtraLinkVisibility));

        // ── Luma: IsLumaMode dependents ───────────────────────────────────────
        NotifyOnce(nameof(LumaBadgeLabel));
        NotifyOnce(nameof(LumaBadgeBackground));
        NotifyOnce(nameof(LumaBadgeForeground));
        NotifyOnce(nameof(LumaBadgeBorderBrush));

        // ── Ultra Limiter: UlStatus dependents ───────────────────────────
        NotifyOnce(nameof(UlStatusDot));
        NotifyOnce(nameof(UlActionLabel));
        NotifyOnce(nameof(UlDeleteVisibility));
        NotifyOnce(nameof(UlStatusText));
        NotifyOnce(nameof(UlStatusColor));
        NotifyOnce(nameof(UlShortAction));
        NotifyOnce(nameof(IsUlInstalled));
        NotifyOnce(nameof(UlRowVisibility));
        NotifyOnce(nameof(CardUlStatusDot));
        NotifyOnce(nameof(CardUlInstallEnabled));

        // ── Ultra Limiter: UlIsInstalling dependents ─────────────────────
        NotifyOnce(nameof(UlProgressVisibility));
        NotifyOnce(nameof(IsUlNotInstalling));

        // ── Vulkan / dual-API computed properties ────────────────────────
        NotifyOnce(nameof(IsVulkanOnly));
        NotifyOnce(nameof(RequiresVulkanInstall));
        NotifyOnce(nameof(ShowRenderingPathToggle));

        // ── Properties not covered by targeted methods ────────────────────────
        NotifyOnce(nameof(DualBitInstallVisibility));
        NotifyOnce(nameof(InstalledFileLabel));
        NotifyOnce(nameof(InstallPathDisplay));
        NotifyOnce(nameof(HideButtonLabel));
        NotifyOnce(nameof(StarForeground));
        NotifyOnce(nameof(IsFavouriteVisibility));
        NotifyOnce(nameof(IsNotFavouriteVisibility));
        NotifyOnce(nameof(IsHiddenVisibility));
        NotifyOnce(nameof(IsNotHiddenVisibility));
        NotifyOnce(nameof(GenericBadgeVisibility));
        NotifyOnce(nameof(NotesButtonVisibility));
        NotifyOnce(nameof(HasNotes));
        NotifyOnce(nameof(HasDualBitMod));
        NotifyOnce(nameof(WikiStatusLabel));
        NotifyOnce(nameof(WikiStatusBadgeBackground));
        NotifyOnce(nameof(WikiStatusBadgeBorderBrush));
        NotifyOnce(nameof(WikiStatusBadgeForeground));
        NotifyOnce(nameof(Is32BitBadgeVisibility));
        NotifyOnce(nameof(Is32BitUeWipVisibility));
        NotifyOnce(nameof(UeExtendedLabel));
        NotifyOnce(nameof(UeExtendedBackground));
        NotifyOnce(nameof(UeExtendedForeground));
        NotifyOnce(nameof(UeExtendedBorderBrush));
        NotifyOnce(nameof(UeExtendedToggleVisibility));
        NotifyOnce(nameof(ComponentDetailVisibility));
        NotifyOnce(nameof(ExpandChevron));
        NotifyOnce(nameof(RsIniExists));
        NotifyOnce(nameof(IsLumaAvailable));
        NotifyOnce(nameof(HasInfoIndicator));
    }
}
