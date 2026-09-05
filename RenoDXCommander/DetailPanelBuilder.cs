// DetailPanelBuilder.cs — Core scaffolding: class declaration, constructor, current detail card state, and detail panel population.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System.IO;

namespace RenoDXCommander;

/// <summary>
/// Helper class responsible for detail panel population and overrides panel construction.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public partial class DetailPanelBuilder
{
    private readonly MainWindow _window;
    private readonly DispatcherQueue _dispatcherQueue;
    private GameCardViewModel? _currentDetailCard;

    // Services injected directly — no longer accessed via ViewModel forwarding properties
    private readonly IGameNameService _gameNameService;
    private readonly IPeHeaderService _peHeaderService;
    private readonly DlssPresetService _dlssPresetService;
    private readonly IDlssStreamlineService _dlssStreamlineService;
    private readonly IDxvkService _dxvkService;
    private readonly IDllOverrideService _dllOverrideService;
    private readonly IAuxInstallService _auxInstallService;
    private readonly IShaderPackService _shaderPackService;
    private readonly IOptiScalerWikiService _optiScalerWikiService;
    private readonly IHdrDatabaseService _hdrDatabaseService;
    private readonly IOptiScalerService _optiScalerService;

    public DetailPanelBuilder(
        MainWindow window,
        IGameNameService gameNameService,
        IPeHeaderService peHeaderService,
        DlssPresetService dlssPresetService,
        IDlssStreamlineService dlssStreamlineService,
        IDxvkService dxvkService,
        IDllOverrideService dllOverrideService,
        IAuxInstallService auxInstallService,
        IShaderPackService shaderPackService,
        IOptiScalerWikiService optiScalerWikiService,
        IHdrDatabaseService hdrDatabaseService,
        IOptiScalerService optiScalerService)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;
        _gameNameService = gameNameService;
        _peHeaderService = peHeaderService;
        _dlssPresetService = dlssPresetService;
        _dlssStreamlineService = dlssStreamlineService;
        _dxvkService = dxvkService;
        _dllOverrideService = dllOverrideService;
        _auxInstallService = auxInstallService;
        _shaderPackService = shaderPackService;
        _optiScalerWikiService = optiScalerWikiService;
        _hdrDatabaseService = hdrDatabaseService;
        _optiScalerService = optiScalerService;

        // Set hand cursor on link buttons so they feel like clickable links
        var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var cursorProp = typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        cursorProp?.SetValue(_window.DetailNexusModsBtn, handCursor);
        cursorProp?.SetValue(_window.DetailPcgwBtn, handCursor);
        cursorProp?.SetValue(_window.DetailUwFixBtn, handCursor);
        cursorProp?.SetValue(_window.DetailUltraPlusBtn, handCursor);
        cursorProp?.SetValue(_window.DetailFolderBtn, handCursor);
        cursorProp?.SetValue(_window.DetailAppDataBtn, handCursor);
    }

    /// <summary>Gets the currently displayed detail card (if any).</summary>
    public GameCardViewModel? CurrentDetailCard => _currentDetailCard;

    public void PopulateDetailPanel(GameCardViewModel card)
    {
        // Set DxvkVariantPending so the Install button row shows when a variant is selected but not installed
        card.DxvkVariantPending = !card.DxvkEnabled
            && _window.ViewModel.GetDxvkVariantOverride(card.GameName, card.Source) != null;

        // Unsubscribe from previous card
        if (_currentDetailCard != null)
            _currentDetailCard.PropertyChanged -= DetailCard_PropertyChanged;

        _currentDetailCard = card;
        card.PropertyChanged += DetailCard_PropertyChanged;

        // Header
        _window.DetailGameName.Text = card.GameName;

        // Source badge
        if (card.HasSourceIcon)
        {
            _window.DetailSourceIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(card.SourceIconUri);
            _window.DetailSourceIcon.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailSourceIcon.Visibility = Visibility.Collapsed;
        }
        _window.DetailSourceText.Text = card.Source;
        _window.DetailSourceBadge.Visibility = string.IsNullOrEmpty(card.Source) ? Visibility.Collapsed : Visibility.Visible;

        // Engine badge
        if (!string.IsNullOrEmpty(card.EngineHint))
        {
            _window.DetailEngineText.Text = card.EngineHint;
            // Set engine icon
            if (card.EngineHint.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0)
                _window.DetailEngineIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icons/unrealengine.ico"));
            else if (card.EngineHint.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0)
                _window.DetailEngineIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icons/unity.ico"));
            else
                _window.DetailEngineIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icons/engine.ico"));
            _window.DetailEngineBadge.Visibility = Visibility.Visible;

            // Clickable when version is vague (no specific number detected) and it's Unreal
            // NOT clickable when: specific version set (e.g. 5.3.2, 4.27.2), or Legacy engine
            bool hasSpecificVersion = System.Text.RegularExpressions.Regex.IsMatch(card.EngineHint, @"Unreal Engine \d");
            bool isVague = card.EngineHint == "Unreal Engine" || card.EngineHint == "Unreal Engine 5";
            bool isClickable = card.EngineHint.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0
                && !hasSpecificVersion
                && !card.EngineHint.Contains("Legacy", StringComparison.OrdinalIgnoreCase)
                || (isVague && _gameNameService.EngineVersionOverrides.ContainsKey(card.GameName));
            _window.DetailEngineText.TextDecorations = isClickable ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None;
            _window.DetailEngineBadge.Tag = isClickable ? card : null;
            if (isClickable)
                ToolTipService.SetToolTip(_window.DetailEngineBadge, "Click to cycle engine version (affects DOF Fix eligibility)");
            else
                ToolTipService.SetToolTip(_window.DetailEngineBadge, null);
        }
        else _window.DetailEngineBadge.Visibility = Visibility.Collapsed;

        // Graphics API badge(s) — one per detected API
        UpdateGraphicsApiBadges(_window, card);

        // Generic badge — hidden (redundant with engine badge + UE-Extended toggle)
        _window.DetailGenericBadge.Visibility = Visibility.Collapsed;

        // 32-bit / 64-bit badge
        _window.Detail32BitBadge.Visibility = card.Is32Bit ? Visibility.Visible : Visibility.Collapsed;
        _window.Detail64BitBadge.Visibility = !card.Is32Bit ? Visibility.Visible : Visibility.Collapsed;

        // Wiki status badge — hidden from main UI, shown inside Info button dialog instead
        _window.DetailWikiBadge.Visibility = Visibility.Collapsed;
        _window.DetailSepPlatformStatus.Visibility = Visibility.Collapsed;

        // Author badges
        _window.DetailAuthorBadgePanel.Children.Clear();
        if (card.HasAuthors)
        {
            foreach (var author in card.AuthorList)
            {
                var donationUrl = GameCardViewModel.GetAuthorDonationUrl(author);
                var textBlock = new TextBlock
                {
                    Text = author,
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush),
                    TextDecorations = donationUrl != null ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
                };
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = UIFactory.Brush(ResourceKeys.ChipDefaultBrush),
                    BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = textBlock,
                };
                if (donationUrl != null)
                {
                    badge.PointerPressed += async (s, e) =>
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(donationUrl));
                    var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
                    var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
                    var cursorProp = typeof(UIElement).GetProperty("ProtectedCursor",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    badge.PointerEntered += (s, e) => cursorProp?.SetValue(badge, handCursor);
                    badge.PointerExited += (s, e) => cursorProp?.SetValue(badge, arrowCursor);
                    ToolTipService.SetToolTip(badge, $"Mod author: {author} — click to open Ko-fi donation page");
                }
                else
                {
                    ToolTipService.SetToolTip(badge, $"Mod author: {author}");
                }
                _window.DetailAuthorBadgePanel.Children.Add(badge);
            }
            _window.DetailAuthorBadgePanel.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailAuthorBadgePanel.Visibility = Visibility.Collapsed;
        }

        // Install path + installed file
        _window.DetailInstallPath.Text = card.InstallPath;
        if (!string.IsNullOrEmpty(card.InstalledAddonFileName))
        {
            _window.DetailInstalledFile.Text = $"{card.InstalledAddonFileName}";
            _window.DetailInstalledFileBadge.Visibility = Visibility.Visible;
            _window.DetailSepModPlatform.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailInstalledFileBadge.Visibility = Visibility.Collapsed;
            _window.DetailSepModPlatform.Visibility = Visibility.Collapsed;
        }

        // Utility buttons — set Tag for event handlers
        _window.DetailFavBtn.Tag = card;
        _window.DetailFavIcon.Text = "Favourite";
        var favColor = card.IsFavourite
            ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
            : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.ChipTextBrush]).Color;
        _window.DetailFavIcon.Foreground = new SolidColorBrush(favColor);
        _window.DetailFavBtn.BorderBrush = card.IsFavourite
            ? new SolidColorBrush(((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color)
            : UIFactory.Brush(ResourceKeys.BorderSubtleBrush);

        _window.DetailHideBtn.Tag = card;
        _window.DetailHideIcon.Text = card.IsHidden ? "Show" : "Hide";
        _window.DetailHideBtn.Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush);

        // Folder management buttons
        _window.DetailFolderBtn.Tag = card;

        // AppData button — visible only for UE games with a resolvable AppData/Documents config folder
        _window.DetailAppDataBtn.Tag = card;
        var appDataPath = ResolveGameConfigRoot(card);
        _window.DetailAppDataBtn.Visibility = appDataPath != null ? Visibility.Visible : Visibility.Collapsed;

        // PCGW link button
        _window.DetailPcgwBtn.Tag = card;
        _window.DetailPcgwBtn.Visibility = card.HasPcgwUrl ? Visibility.Visible : Visibility.Collapsed;

        // HDR toggle button — show per-game state
        _window.DetailHdrToggleBtn.Tag = card;
        var hdrOverride = _gameNameService.HdrToggleOverrides
            .TryGetValue(card.GameName, out var hov) ? hov : null;
        bool hdrActive = hdrOverride != null
            ? string.Equals(hdrOverride, "On", StringComparison.OrdinalIgnoreCase)
            : _window.ViewModel.Settings.HdrAutoToggle;
        _window.DetailHdrToggleText.Text = "HDR";
        _window.DetailHdrToggleBtn.Background = hdrActive
            ? UIFactory.Brush(ResourceKeys.AccentPurpleBgBrush)
            : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
        _window.DetailHdrToggleBtn.BorderBrush = hdrActive
            ? UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush)
            : UIFactory.Brush(ResourceKeys.BorderSubtleBrush);
        _window.DetailHdrToggleText.Foreground = hdrActive
            ? UIFactory.Brush(ResourceKeys.AccentPurpleBrush)
            : UIFactory.Brush(ResourceKeys.ChipTextBrush);

        // RES toggle button — hidden unless feature enabled
        if (FeatureFlags.ResolutionControl)
        {
            _window.DetailResToggleBtn.Visibility = Visibility.Visible;
            _window.DetailResToggleBtn.Tag = card;
            var resOverride = _gameNameService.ResToggleOverrides
                .TryGetValue(card.GameName, out var rov) ? rov : null;
            bool resActive = resOverride != null
                ? string.Equals(resOverride, "On", StringComparison.OrdinalIgnoreCase)
                : _window.ViewModel.Settings.ResolutionAutoToggle;
            _window.DetailResToggleText.Text = "RES";
            _window.DetailResToggleBtn.Background = resActive
                ? UIFactory.Brush(ResourceKeys.AccentPurpleBgBrush)
                : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
            _window.DetailResToggleBtn.BorderBrush = resActive
                ? UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush)
                : UIFactory.Brush(ResourceKeys.BorderSubtleBrush);
            _window.DetailResToggleText.Foreground = resActive
                ? UIFactory.Brush(ResourceKeys.AccentPurpleBrush)
                : UIFactory.Brush(ResourceKeys.ChipTextBrush);
        }
        else
        {
            _window.DetailResToggleBtn.Visibility = Visibility.Collapsed;
        }

        // Nexus Mods link button
        _window.DetailNexusModsBtn.Tag = card;
        _window.DetailNexusModsBtn.Visibility = card.HasNexusModsUrl ? Visibility.Visible : Visibility.Collapsed;

        // UW Fix link button
        _window.DetailUwFixBtn.Tag = card;
        _window.DetailUwFixBtn.Visibility = card.HasUwFixUrl ? Visibility.Visible : Visibility.Collapsed;
        if (card.HasUwFixUrl)
        {
            var source = card.UwFixSource ?? "creator";
            ToolTipService.SetToolTip(_window.DetailUwFixBtn, $"Open {source}'s ultrawide fix page");
        }

        // Ultra+ link button
        _window.DetailUltraPlusBtn.Tag = card;
        _window.DetailUltraPlusBtn.Visibility = card.HasUltraPlusUrl ? Visibility.Visible : Visibility.Collapsed;

        // Luma toggle removed — both RenoDX and Luma rows always visible when Luma is available
        _window.DetailLumaToggle.Visibility = Visibility.Collapsed;
        _window.DetailLumaInfoText.Visibility = Visibility.Collapsed;

        // ── Wire Components section collapse + drag handle ────────────────────
        {
            const string sectionKey = "Components";
            var settings    = _window.ViewModel.Settings;
            bool collapsed  = settings.CollapsedDetailSections.Contains(sectionKey);

            _window.DetailComponentsArrow.Text = collapsed ? "▶" : "▼";
            _window.DetailComponentsBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            _window.DetailComponentsTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);

            // Re-subscribe each time panel is populated (card changes)
            _window.DetailComponentsHeader.PointerPressed -= ComponentsHeader_PointerPressed;
            _window.DetailComponentsHeader.PointerPressed += ComponentsHeader_PointerPressed;
            _window.DetailComponentsHeader.PointerEntered -= ComponentsHeader_PointerEntered;
            _window.DetailComponentsHeader.PointerEntered += ComponentsHeader_PointerEntered;
            _window.DetailComponentsHeader.PointerExited  -= ComponentsHeader_PointerExited;
            _window.DetailComponentsHeader.PointerExited  += ComponentsHeader_PointerExited;

            var handCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
            var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
            var cursorProp  = typeof(UIElement).GetProperty("ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            _window.DetailComponentsHeader.PointerEntered += (s, e) => cursorProp?.SetValue(_window.DetailComponentsHeader, handCursor);
            _window.DetailComponentsHeader.PointerExited  += (s, e) => cursorProp?.SetValue(_window.DetailComponentsHeader, arrowCursor);

            // Insert/replace drag handle as first child (tagged "DragHandle" for identification)
            var dragHandle = MakeDragHandle(_window.DetailComponentSection);
            dragHandle.Tag = "DragHandle";
            if (_window.DetailComponentsHeader.Children.Count > 0
                && _window.DetailComponentsHeader.Children[0] is TextBlock tb
                && tb.Tag is string t && t == "DragHandle")
                _window.DetailComponentsHeader.Children.RemoveAt(0);
            _window.DetailComponentsHeader.Children.Insert(0, dragHandle);
        }

        // Populate component rows
        UpdateDetailComponentRows(card);
    }

    /// <summary>
    /// Resolves the top-level game config directory (for the AppData button).
    /// Checks %LocalAppData%\{projectName}\ and Documents\My Games\{gameName}\.
    /// Returns the path if found, null otherwise.
    /// </summary>
    private static string? ResolveGameConfigRoot(GameCardViewModel card)
    {
        var projectName = card.EngineIniProjectOverride
            ?? AuxInstallService.ResolveUeProjectName(card.InstallPath ?? "");

        // If the override is a full path (or pipe-separated paths), resolve directly
        if (!string.IsNullOrEmpty(card.EngineIniProjectOverride)
            && (card.EngineIniProjectOverride.Contains('\\') || card.EngineIniProjectOverride.Contains('/')))
        {
            var candidates = card.EngineIniProjectOverride.Split('|');
            foreach (var candidate in candidates)
            {
                var expanded = Environment.ExpandEnvironmentVariables(candidate.Trim());
                if (Directory.Exists(expanded)) return expanded;
            }
            return null;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Check %LocalAppData%\{projectName}\
        if (!string.IsNullOrEmpty(projectName))
        {
            var dir = Path.Combine(localAppData, projectName);
            if (Directory.Exists(dir)) return dir;
        }

        // Check Documents\My Games\{gameName}\
        if (!string.IsNullOrEmpty(card.GameName))
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var myGamesDir = Path.Combine(docs, "My Games", card.GameName);
            if (Directory.Exists(myGamesDir)) return myGamesDir;

            // Try stripped name (® ™ ©)
            var stripped = card.GameName.Replace("®", "").Replace("™", "").Replace("©", "").Trim();
            if (stripped != card.GameName)
            {
                myGamesDir = Path.Combine(docs, "My Games", stripped);
                if (Directory.Exists(myGamesDir)) return myGamesDir;
            }
        }

        // Check in-game directory: {GameRoot}\{ProjectName}\Saved\
        if (!string.IsNullOrEmpty(card.InstallPath))
        {
            var normalized = card.InstallPath.Replace('/', '\\').TrimEnd('\\');
            var parts = normalized.Split('\\');
            for (int i = parts.Length - 1; i > 0; i--)
            {
                if (parts[i].Equals("Binaries", StringComparison.OrdinalIgnoreCase))
                {
                    // Project folder is immediately above Binaries
                    var projectDir = string.Join('\\', parts.Take(i));
                    var savedDir = Path.Combine(projectDir, "Saved");
                    if (Directory.Exists(savedDir)) return projectDir;

                    // Also check sibling folders in the game root
                    if (i - 1 > 0)
                    {
                        var gameRoot = string.Join('\\', parts.Take(i - 1));
                        try
                        {
                            foreach (var subDir in Directory.EnumerateDirectories(gameRoot))
                            {
                                var subSaved = Path.Combine(subDir, "Saved");
                                if (Directory.Exists(subSaved)) return subDir;
                            }
                        }
                        catch { }
                    }
                    break;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Clears the graphics API badge panel and adds one badge per detected API.
    /// APIs are shown in a consistent order: DX8 → DX9 → DX10 → DX11 → DX12 → Vulkan → OpenGL.
    /// </summary>
    internal static void UpdateGraphicsApiBadges(MainWindow window, GameCardViewModel card)
    {
        window.DetailGraphicsApiBadgePanel.Children.Clear();

        if (!card.HasGraphicsApiBadge)
        {
            window.DetailGraphicsApiBadgePanel.Visibility = Visibility.Collapsed;
            return;
        }

        var rawApis = card.DetectedApis.Count > 0
            ? card.DetectedApis
            : new HashSet<GraphicsApiType> { card.GraphicsApi };

        // Filter: if any modern API (DX11, DX12, VLK) is present, drop legacy (DX8, DX9, DX10).
        // If DX10 is the highest present, drop DX8/DX9. Priority: DX12 > DX11 > VLK > OGL > DX10 > DX9 > DX8.
        var hasModern = rawApis.Contains(GraphicsApiType.DirectX11)
                     || rawApis.Contains(GraphicsApiType.DirectX12)
                     || rawApis.Contains(GraphicsApiType.Vulkan);
        var hasDx10Plus = hasModern || rawApis.Contains(GraphicsApiType.DirectX10);

        var filtered = rawApis.Where(a => a switch
        {
            GraphicsApiType.DirectX8  => !hasDx10Plus,
            GraphicsApiType.DirectX9  => !hasDx10Plus,
            GraphicsApiType.DirectX10 => !hasModern,
            // OGL only shows alone — if any DX or Vulkan is present it's likely a launcher/helper exe
            GraphicsApiType.OpenGL    => rawApis.Count == 1,
            _                         => true,
        });

        // Display in consistent order: DX8→DX9→DX10→DX11→DX12→VLK→OGL
        var displayOrder = new[]
        {
            GraphicsApiType.DirectX8, GraphicsApiType.DirectX9, GraphicsApiType.DirectX10,
            GraphicsApiType.DirectX11, GraphicsApiType.DirectX12,
            GraphicsApiType.Vulkan, GraphicsApiType.OpenGL,
        };
        var apisToShow = displayOrder.Where(a => filtered.Contains(a)).ToList();

        foreach (var api in apisToShow)
        {
            var label = Services.GraphicsApiDetector.GetLabel(api);
            if (string.IsNullOrEmpty(label)) continue;

            var textBlock = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush),
            };
            var badge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Background = UIFactory.Brush(ResourceKeys.ChipDefaultBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = textBlock,
            };
            window.DetailGraphicsApiBadgePanel.Children.Add(badge);
        }

        window.DetailGraphicsApiBadgePanel.Visibility =
            window.DetailGraphicsApiBadgePanel.Children.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── OverridesHeaderRow event handler refs — stored so we can unsubscribe on rebuild ──
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _ovHeaderPressedHandler;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _ovHeaderEnteredHandler;
    private Microsoft.UI.Xaml.Input.PointerEventHandler? _ovHeaderExitedHandler;

    // ── Section order / drag-reorder ─────────────────────────────────────────

    /// <summary>Maps section key → its container Border in DetailPanel.</summary>
    private Border GetSectionContainer(string key) => key switch
    {
        "Components"      => _window.DetailComponentSection,
        "GameOverrides"   => _window.OverridesContainer,
        "NeuralRendering" => _window.NeuralRenderingContainer,
        "NvidiaProfile"   => _window.NvidiaProfileContainer,
        "Management"      => _window.ManagementContainer,
        "Extras"          => _window.ExtrasContainer,
        _                 => throw new ArgumentException($"Unknown section key: {key}"),
    };

    /// <summary>
    /// Reorders the 5 section Borders within DetailPanel.Children to match
    /// SettingsViewModel.DetailSectionOrder. Call after all sections are populated+visible.
    /// The 2 header Grids at [0] and [1] are left in place.
    /// </summary>
    internal void ApplySectionOrder()
    {
        var order = _window.ViewModel.Settings.DetailSectionOrder;
        var panel = _window.DetailPanel;

        var desired = order
            .Select(key => { try { return GetSectionContainer(key); } catch { return null; } })
            .Where(b => b != null).Cast<Border>().ToList();

        // Snapshot visibility
        var visibilities = new Dictionary<UIElement, Visibility>();
        for (int i = 2; i < panel.Children.Count; i++)
            if (panel.Children[i] is UIElement el) visibilities[el] = el.Visibility;

        int insertAt = 2;
        foreach (var border in desired)
        {
            var currentIndex = panel.Children.IndexOf(border);
            if (currentIndex < 0) continue;
            if (currentIndex == insertAt) { insertAt++; continue; }
            panel.Children.RemoveAt(currentIndex);
            panel.Children.Insert(insertAt, border);
            insertAt++;
        }

        // Restore visibility
        for (int i = 2; i < panel.Children.Count; i++)
            if (panel.Children[i] is UIElement el && visibilities.TryGetValue(el, out var vis))
                el.Visibility = vis;
    }

    // Drag state
    private Border?  _dragBorder;
    private uint     _dragPointerId;
    private double   _dragStartY;
    private int      _dragStartIdx;
    private int      _dragCurrentIdx;
    private double   _dragCardHeight;
    private bool     _dragging;

    private const double SectionCardHeight = 120.0;  // fallback only

    /// <summary>
    /// Creates a drag handle (≡) TextBlock for a section header.
    /// Live reorder: the section moves in real time as you drag.
    /// Index is computed from cumulative Y delta (no TransformToVisual queries mid-drag).
    /// Single Remove+Insert per threshold crossing keeps layout stable.
    /// Order is persisted on PointerReleased.
    /// </summary>
    internal TextBlock MakeDragHandle(Border container)
    {
        var handle = new TextBlock
        {
            Text              = "≡",
            FontSize          = 14,
            Foreground        = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0),
            ManipulationMode  = Microsoft.UI.Xaml.Input.ManipulationModes.None,
        };

        var handCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var cursorProp  = typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        handle.PointerEntered += (s, e) =>
        {
            if (!_dragging) handle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
            cursorProp?.SetValue(handle, handCursor);
        };
        handle.PointerExited += (s, e) =>
        {
            if (!_dragging) handle.Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush);
            cursorProp?.SetValue(handle, arrowCursor);
        };

        handle.PointerPressed += (s, e) =>
        {
            var panel = _window.DetailPanel;
            var idx   = panel.Children.IndexOf(container);
            if (idx < 2) return;

            _dragBorder      = container;
            _dragPointerId   = e.Pointer.PointerId;
            _dragStartY      = e.GetCurrentPoint(_window.DetailPanel).Position.Y;
            _dragStartIdx    = idx;
            _dragCurrentIdx  = idx;
            _dragging        = true;
            container.Opacity = 0.55;

            // Use actual average section height for threshold — measure the dragged section itself
            // as a proxy. Falls back to SectionCardHeight constant if layout hasn't run yet.
            _dragCardHeight = container.ActualHeight > 20 ? container.ActualHeight + 16 : SectionCardHeight;

            _window.DetailPanel.CapturePointer(e.Pointer);
            _window.DetailPanel.PointerMoved        += DetailPanel_PointerMoved;
            _window.DetailPanel.PointerReleased     += DetailPanel_PointerReleased;
            _window.DetailPanel.PointerCaptureLost  += DetailPanel_PointerCaptureLost;

            e.Handled = true;
        };

        // PointerMoved/Released/CaptureLost are handled on DetailPanel (see above)

        return handle;
    }

    private void DetailPanel_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_dragging || _dragBorder == null || e.Pointer.PointerId != _dragPointerId) return;

        var pt     = e.GetCurrentPoint(_window.DetailPanel).Position;
        var deltaY = pt.Y - _dragStartY;           // always relative to original press point
        var panel  = _window.DetailPanel;

        // Compute target index from total accumulated delta — each slot requires a full
        // SectionCardHeight of travel from the original start, so thresholds are evenly spaced
        // and don't compound with each move.
        int offset    = (int)(deltaY / _dragCardHeight);  // truncate, not round
        int targetIdx = Math.Clamp(_dragStartIdx + offset, 2, panel.Children.Count - 1);

        if (targetIdx != _dragCurrentIdx)
        {
            var currentPos = panel.Children.IndexOf(_dragBorder);
            if (currentPos >= 2)
            {
                var vis = new Dictionary<UIElement, Visibility>();
                for (int i = 2; i < panel.Children.Count; i++)
                    if (panel.Children[i] is UIElement el) vis[el] = el.Visibility;

                var captured = _dragBorder;
                int to = targetIdx;
                panel.Children.RemoveAt(currentPos);
                _window.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        // Clamp again at insert time — Children.Count changed by RemoveAt
                        int safeIdx = Math.Clamp(to, 2, Math.Max(2, panel.Children.Count));
                        panel.Children.Insert(safeIdx, captured);
                        for (int i = 2; i < panel.Children.Count; i++)
                            if (panel.Children[i] is UIElement el && vis.TryGetValue(el, out var v))
                                el.Visibility = v;
                    }
                    catch (Exception ex) { CrashReporter.Log($"[DragHandle] Insert failed: {ex.Message}"); }
                });

                _dragCurrentIdx = targetIdx;
                // _dragStartY intentionally NOT updated — delta is always from original press
            }
        }
        e.Handled = true;
    }

    private void DragEnd(bool save)
    {
        if (!_dragging) return;
        _dragging = false;
        var panel = _window.DetailPanel;
        panel.PointerMoved        -= DetailPanel_PointerMoved;
        panel.PointerReleased     -= DetailPanel_PointerReleased;
        panel.PointerCaptureLost  -= DetailPanel_PointerCaptureLost;
        if (_dragBorder != null) _dragBorder.Opacity = 1.0;
        _dragBorder = null;
        if (save) _window.DispatcherQueue.TryEnqueue(SaveSectionOrder);
    }

    private void DetailPanel_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _dragPointerId) return;
        _window.DetailPanel.ReleasePointerCapture(e.Pointer);
        DragEnd(save: true);
        e.Handled = true;
    }

    private void DetailPanel_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => DragEnd(save: true);

    private void SaveSectionOrder()
    {
        var panel    = _window.DetailPanel;
        var keyOrder = new List<string>();
        for (int i = 2; i < panel.Children.Count; i++)
        {
            var border = panel.Children[i] as Border;
            if      (border == _window.DetailComponentSection)     keyOrder.Add("Components");
            else if (border == _window.OverridesContainer)          keyOrder.Add("GameOverrides");
            else if (border == _window.NeuralRenderingContainer)    keyOrder.Add("NeuralRendering");
            else if (border == _window.NvidiaProfileContainer)      keyOrder.Add("NvidiaProfile");
            else if (border == _window.ManagementContainer)         keyOrder.Add("Management");
            else if (border == _window.ExtrasContainer)             keyOrder.Add("Extras");
        }
        if (keyOrder.Count > 0)
        {
            _window.ViewModel.Settings.DetailSectionOrder = keyOrder;
            _window.ViewModel.SaveSettingsPublic();
        }
    }

    // ── Components section header event handlers (wired per-card in PopulateDetailPanel) ──

    private void ComponentsHeader_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        const string sectionKey = "Components";
        var settings   = _window.ViewModel.Settings;
        bool collapsed = _window.DetailComponentsBody.Visibility == Visibility.Visible;
        _window.DetailComponentsBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        _window.DetailComponentsArrow.Text      = collapsed ? "▶" : "▼";

        if (collapsed) settings.CollapsedDetailSections.Add(sectionKey);
        else           settings.CollapsedDetailSections.Remove(sectionKey);

        _window.ViewModel.SaveSettingsPublic();
    }

    private void ComponentsHeader_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _window.DetailComponentsTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_window.DetailComponentsHeader, handCursor);
    }

    private void ComponentsHeader_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _window.DetailComponentsTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_window.DetailComponentsHeader, arrowCursor);
    }

    // ── Section-collapse helper ──────────────────────────────────────────────

    // Limits concurrent background scans to prevent thread pool saturation
    // when rapidly clicking through games.
    private static readonly SemaphoreSlim _panelScanSemaphore = new SemaphoreSlim(4, 4);

    /// <summary>
    /// Builds a collapsible section: a clickable header row (arrow + title) and a body
    /// StackPanel whose visibility is toggled on click.
    /// The collapsed state is persisted in SettingsViewModel.CollapsedDetailSections.
    /// Returns a StackPanel containing [header, body] that should be added to the parent panel.
    /// The caller should add all section body content into the returned <paramref name="body"/> panel.
    /// </summary>
    internal (StackPanel wrapper, StackPanel body) MakeSectionHeader(string title, string sectionKey)
    {
        var settings = _window.ViewModel.Settings;
        bool isCollapsed = settings.CollapsedDetailSections.Contains(sectionKey);

        var arrowText = new TextBlock
        {
            Text        = isCollapsed ? "▶" : "▼",
            FontSize    = 10,
            Foreground  = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin      = new Thickness(0, 0, 6, 0),
        };

        var titleText = new TextBlock
        {
            Text       = title,
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 0,
        };
        headerRow.Children.Add(arrowText);
        headerRow.Children.Add(titleText);

        // Make the header row behave like a button
        headerRow.PointerEntered += (s, e) => titleText.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        headerRow.PointerExited  += (s, e) => titleText.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);

        // Hand cursor on hover
        var handCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var cursorProp  = typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        headerRow.PointerEntered += (s, e) => cursorProp?.SetValue(headerRow, handCursor);
        headerRow.PointerExited  += (s, e) => cursorProp?.SetValue(headerRow, arrowCursor);

        var body = new StackPanel
        {
            Spacing    = 10,
            Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible,
        };

        // Toggle on click
        headerRow.PointerPressed += (s, e) =>
        {
            bool nowCollapsed = body.Visibility == Visibility.Visible;
            body.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            arrowText.Text  = nowCollapsed ? "▶" : "▼";

            if (nowCollapsed)
                settings.CollapsedDetailSections.Add(sectionKey);
            else
                settings.CollapsedDetailSections.Remove(sectionKey);

            _window.ViewModel.SaveSettingsPublic();
        };

        var wrapper = new StackPanel { Spacing = 8 };
        wrapper.Children.Add(headerRow);
        wrapper.Children.Add(body);
        return (wrapper, body);
    }
}
