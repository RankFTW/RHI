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
    [ObservableProperty] private string? _rdxInstalledVersion;
    [ObservableProperty] private bool _isHidden = false;
    [ObservableProperty] private bool _isFavourite = false;

    [ObservableProperty] private bool _isExternalOnly;
    [ObservableProperty] private bool _isGenericMod;
    [ObservableProperty] private string _externalUrl   = "";
    [ObservableProperty] private string _externalLabel = "";
    [ObservableProperty] private string? _nexusUrl;
    [ObservableProperty] private string? _nexusModsUrl;
    [ObservableProperty] private string? _pcgwUrl;
    [ObservableProperty] private string? _uwFixUrl;
    public string? UwFixSource { get; set; }
    [ObservableProperty] private string? _ultraPlusUrl;
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
    [ObservableProperty] private bool       _useNormalReShade;
    public AuxInstalledRecord? RsRecord { get; set; }

    // ── Vulkan / dual-API state ──────────────────────────────────────────────────
    [ObservableProperty] private string _vulkanRenderingPath = "DirectX";

    // Plain properties — not mutated after card creation, no need to observe
    public HashSet<GraphicsApiType> DetectedApis { get; set; } = new();
    public bool IsDualApiGame { get; set; }

    // Computed Vulkan properties
    public bool IsVulkanOnly => GraphicsApi == GraphicsApiType.Vulkan && !IsDualApiGame;
    public bool RequiresVulkanInstall => IsVulkanOnly
        || (IsDualApiGame && VulkanRenderingPath == "Vulkan")
        || (DxvkEnabled && GraphicsApi is not GraphicsApiType.DirectX8 and not GraphicsApiType.DirectX9);
    public bool ShowRenderingPathToggle => IsDualApiGame;

    public string EngineHint    { get; set; } = "";
    public GraphicsApiType GraphicsApi { get; set; } = GraphicsApiType.Unknown;
    public string? NameUrl      { get; set; }
    public bool ExcludeFromUpdateAllReShade { get; set; }
    public bool ExcludeFromUpdateAllRenoDx  { get; set; }
    public bool ExcludeFromUpdateAllUl      { get; set; }
    public bool ExcludeFromUpdateAllDc      { get; set; }
    /// <summary>Per-game shader selection override: null = follow global selection.</summary>
    public string? ShaderModeOverride { get; set; }

    // ── 32-bit mode ───────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _is32Bit = false;

    // ── ReLimiter state ──────────────────────────────────────────────────────
    [ObservableProperty] private GameStatus _ulStatus  = GameStatus.NotInstalled;
    [ObservableProperty] private bool       _ulIsInstalling;
    [ObservableProperty] private double     _ulProgress;
    [ObservableProperty] private string     _ulActionMessage = "";
    [ObservableProperty] private string?    _ulInstalledFile;
    [ObservableProperty] private string?    _ulInstalledVersion;

    // ── Display Commander state ──────────────────────────────────────────────
    [ObservableProperty] private GameStatus _dcStatus  = GameStatus.NotInstalled;
    [ObservableProperty] private bool       _dcIsInstalling;
    [ObservableProperty] private double     _dcProgress;
    [ObservableProperty] private string     _dcActionMessage = "";
    [ObservableProperty] private string?    _dcInstalledFile;
    [ObservableProperty] private string?    _dcInstalledVersion;

    // ── RE Framework state ──────────────────────────────────────────────────────
    [ObservableProperty] private GameStatus _refStatus  = GameStatus.NotInstalled;
    [ObservableProperty] private bool       _refIsInstalling;
    [ObservableProperty] private double     _refProgress;
    [ObservableProperty] private string     _refActionMessage = "";
    [ObservableProperty] private string?    _refInstalledVersion;

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
    /// Per-property fade tokens — ensures only the latest message for each property is cleared.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _fadeTokens = new();

    /// <summary>
    /// Sets an action message property and automatically clears it after a delay.
    /// Each property tracks its own token so multiple messages across different properties fade independently.
    /// </summary>
    public void FadeMessage(Action<string> setter, string message, int delayMs = 4000, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(setter))] string? key = null)
    {
        var tokenKey = key ?? "default";
        var token = _fadeTokens.AddOrUpdate(tokenKey, 1, (_, old) => old + 1);
        setter(message);
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            if (_fadeTokens.TryGetValue(tokenKey, out var current) && current == token)
                setter("");
        });
    }

    /// <summary>
    /// Notifies all computed properties that depend on WikiStatus when it changes.
    /// </summary>
    partial void OnWikiStatusChanged(string value)
    {
        OnPropertyChanged(nameof(WikiStatusLabel));
        OnPropertyChanged(nameof(WikiStatusBadgeBackground));
        OnPropertyChanged(nameof(WikiStatusBadgeBorderBrush));
        OnPropertyChanged(nameof(WikiStatusBadgeForeground));
        OnPropertyChanged(nameof(WikiStatusIcon));
        OnPropertyChanged(nameof(WikiStatusIconVisible));
    }

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

        // ── ReLimiter: UlStatus dependents ───────────────────────────
        NotifyOnce(nameof(UlStatusDot));
        NotifyOnce(nameof(UlActionLabel));
        NotifyOnce(nameof(UlDeleteVisibility));
        NotifyOnce(nameof(UlStatusText));
        NotifyOnce(nameof(UlStatusColor));
        NotifyOnce(nameof(UlShortAction));
        NotifyOnce(nameof(IsUlInstalled));
        NotifyOnce(nameof(UlInstallEnabled));
        NotifyOnce(nameof(UlRowVisibility));
        NotifyOnce(nameof(CardUlStatusDot));
        NotifyOnce(nameof(CardUlInstallEnabled));

        // ── ReLimiter: UlIsInstalling dependents ─────────────────────
        NotifyOnce(nameof(UlProgressVisibility));
        NotifyOnce(nameof(IsUlNotInstalling));

        // ── Display Commander: DcStatus dependents ───────────────────
        NotifyOnce(nameof(DcStatusDot));
        NotifyOnce(nameof(DcActionLabel));
        NotifyOnce(nameof(DcBtnBackground));
        NotifyOnce(nameof(DcBtnForeground));
        NotifyOnce(nameof(DcBtnBorderBrush));
        NotifyOnce(nameof(DcDeleteVisibility));
        NotifyOnce(nameof(DcStatusText));
        NotifyOnce(nameof(DcStatusColor));
        NotifyOnce(nameof(DcShortAction));
        NotifyOnce(nameof(IsDcInstalled));
        NotifyOnce(nameof(DcInstallEnabled));
        NotifyOnce(nameof(DcRowVisibility));
        NotifyOnce(nameof(CardDcStatusDot));
        NotifyOnce(nameof(CardDcInstallEnabled));

        // ── Display Commander: DcIsInstalling dependents ─────────────
        NotifyOnce(nameof(DcProgressVisibility));
        NotifyOnce(nameof(IsDcNotInstalling));

        // ── OptiScaler: OsStatus dependents ──────────────────────────
        NotifyOnce(nameof(OsStatusDot));
        NotifyOnce(nameof(OsActionLabel));
        NotifyOnce(nameof(OsBtnBackground));
        NotifyOnce(nameof(OsBtnForeground));
        NotifyOnce(nameof(OsBtnBorderBrush));
        NotifyOnce(nameof(OsDeleteVisibility));
        NotifyOnce(nameof(OsStatusText));
        NotifyOnce(nameof(OsStatusColor));
        NotifyOnce(nameof(OsShortAction));
        NotifyOnce(nameof(IsOsInstalled));
        NotifyOnce(nameof(OsInstallEnabled));
        NotifyOnce(nameof(OsRowVisibility));
        NotifyOnce(nameof(CardOsStatusDot));
        NotifyOnce(nameof(CardOsInstallEnabled));

        // ── OptiScaler: OsIsInstalling dependents ────────────────────
        NotifyOnce(nameof(OsProgressVisibility));
        NotifyOnce(nameof(OsMessageVisibility));
        NotifyOnce(nameof(IsOsNotInstalling));
        NotifyOnce(nameof(OsIniExists));

        // ── DXVK: DxvkStatus dependents ─────────────────────────────
        NotifyOnce(nameof(DxvkStatusDot));
        NotifyOnce(nameof(DxvkActionLabel));
        NotifyOnce(nameof(DxvkBtnBackground));
        NotifyOnce(nameof(DxvkBtnForeground));
        NotifyOnce(nameof(DxvkBtnBorderBrush));
        NotifyOnce(nameof(DxvkDeleteVisibility));
        NotifyOnce(nameof(DxvkStatusText));
        NotifyOnce(nameof(DxvkStatusColor));
        NotifyOnce(nameof(DxvkShortAction));
        NotifyOnce(nameof(IsDxvkInstalled));
        NotifyOnce(nameof(DxvkInstallEnabled));
        NotifyOnce(nameof(CardDxvkStatusDot));
        NotifyOnce(nameof(CardDxvkInstallEnabled));

        // ── DXVK: DxvkIsInstalling dependents ───────────────────────
        NotifyOnce(nameof(DxvkProgressVisibility));
        NotifyOnce(nameof(IsDxvkNotInstalling));

        // ── DXVK: DxvkEnabled dependents ────────────────────────────
        NotifyOnce(nameof(DxvkRowVisibility));

        // ── DXVK: Toggle eligibility dependents ─────────────────────
        NotifyOnce(nameof(IsDxvkToggleVisible));
        NotifyOnce(nameof(IsDxvkToggleEnabled));
        NotifyOnce(nameof(DxvkToggleTooltip));

        // ── Normal ReShade: UseNormalReShade dependents ───────────────
        NotifyOnce(nameof(AddonsDisabled));
        NotifyOnce(nameof(AddonStrikethrough));

        // ── RE Framework: RefStatus dependents ───────────────────────
        NotifyOnce(nameof(RefActionLabel));
        NotifyOnce(nameof(RefDeleteVisibility));
        NotifyOnce(nameof(RefStatusText));
        NotifyOnce(nameof(RefStatusColor));
        NotifyOnce(nameof(RefShortAction));
        NotifyOnce(nameof(IsRefInstalled));
        NotifyOnce(nameof(RefRowVisibility));
        NotifyOnce(nameof(CardRefStatusDot));
        NotifyOnce(nameof(CardRefInstallEnabled));

        // ── RE Framework: RefIsInstalling dependents ─────────────────
        NotifyOnce(nameof(RefProgressVisibility));
        NotifyOnce(nameof(IsRefNotInstalling));

        // ── Vulkan / dual-API computed properties ────────────────────────
        NotifyOnce(nameof(IsVulkanOnly));
        NotifyOnce(nameof(RequiresVulkanInstall));
        NotifyOnce(nameof(ShowRenderingPathToggle));

        // ── Graphics API badge computed properties ───────────────────────
        NotifyOnce(nameof(HasGraphicsApiBadge));
        NotifyOnce(nameof(GraphicsApiLabel));

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

        // ── Nexus Mods / PCGW link computed properties ───────────────────────
        NotifyOnce(nameof(HasNexusModsUrl));
        NotifyOnce(nameof(HasPcgwUrl));
    }
}
