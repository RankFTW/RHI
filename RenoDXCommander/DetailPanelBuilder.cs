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
    // Cached once — typeof(UIElement).GetProperty is reflective and called on every card
    // selection and on every mouse hover over section headers. Storing it as a static field
    // eliminates repeated MemberInfo lookups across the lifetime of the process.
    internal static readonly System.Reflection.PropertyInfo? CursorProp =
        typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private readonly MainWindow _window;
    private readonly DispatcherQueue _dispatcherQueue;
    private GameCardViewModel? _currentDetailCard;

    // Services injected directly — no longer accessed via ViewModel forwarding properties
    private readonly IGameNameService _gameNameService;
    private readonly IPeHeaderService _peHeaderService;
    private readonly DlssPresetService _dlssPresetService;
    private readonly IDlssStreamlineService _dlssStreamlineService;
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
        Layout = new DetailLayoutHost(window);
        _gameNameService = gameNameService;
        _peHeaderService = peHeaderService;
        _dlssPresetService = dlssPresetService;
        _dlssStreamlineService = dlssStreamlineService;
        _dllOverrideService = dllOverrideService;
        _auxInstallService = auxInstallService;
        _shaderPackService = shaderPackService;
        _optiScalerWikiService = optiScalerWikiService;
        _hdrDatabaseService = hdrDatabaseService;
        _optiScalerService = optiScalerService;

        // Set hand cursor on link buttons so they feel like clickable links
        var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var cursorProp = DetailPanelBuilder.CursorProp;
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
        var __sw = System.Diagnostics.Stopwatch.StartNew();
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
        UpdateStoreBadgeLink(card);

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
        ToolTipService.SetToolTip(_window.Detail32BitBadge, "32-bit game. Some components aren't available for 32-bit games.");
        ToolTipService.SetToolTip(_window.Detail64BitBadge, "64-bit game.");

        // Wiki status badge — hidden from main UI, shown inside Info button dialog instead
        _window.DetailWikiBadge.Visibility = Visibility.Collapsed;

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
                    var cursorProp = DetailPanelBuilder.CursorProp;
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

        // Install path + installed file. Steam reports some libraries with a lowercase drive letter
        // ("c:\steam\..."); show it capitalised like every other path (display only).
        var shownPath = card.InstallPath ?? "";
        if (shownPath.Length >= 2 && shownPath[1] == ':' && char.IsLower(shownPath[0]))
            shownPath = char.ToUpperInvariant(shownPath[0]) + shownPath[1..];
        _window.DetailInstallPath.Text = shownPath;

        // Determine label: RenoDX addon filename + Luma mod name when both are present
        string? fileLabel = null;
        bool lumaInstalled = card.LumaStatus is GameStatus.Installed or GameStatus.UpdateAvailable;
        string? lumaLabel = null;
        if (lumaInstalled && card.LumaMod != null)
        {
            // Prefer the actual installed addon filename (e.g. "Luma-Prey.addon") like RenoDX does
            var addonFile = card.LumaRecord?.InstalledFiles
                .Select(f => Path.GetFileName(f))
                .FirstOrDefault(f => f.EndsWith(".addon", StringComparison.OrdinalIgnoreCase)
                                  || f.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
                                  || f.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase));
            lumaLabel = addonFile ?? (card.LumaMod.IsGenericLuma ? "Luma" : card.LumaMod.Name);
        }

        if (!string.IsNullOrEmpty(card.InstalledAddonFileName) && lumaLabel != null)
            fileLabel = $"{card.InstalledAddonFileName}  ·  {lumaLabel}";
        else if (!string.IsNullOrEmpty(card.InstalledAddonFileName))
            fileLabel = card.InstalledAddonFileName;
        else if (lumaLabel != null)
            fileLabel = lumaLabel;

        if (!string.IsNullOrEmpty(fileLabel))
        {
            _window.DetailInstalledFile.Text = fileLabel;
            ToolTipService.SetToolTip(_window.DetailInstalledFileBadge,
                fileLabel.Contains('·') ? "Installed HDR mod files" : "Installed HDR mod file");
            _window.DetailInstalledFileBadge.Visibility = Visibility.Visible;
            _window.DetailSepModPlatform.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailInstalledFileBadge.Visibility = Visibility.Collapsed;
            _window.DetailSepModPlatform.Visibility = Visibility.Collapsed;
        }

        // Mod status icon — green ✓ for Done, 🔨 for WIP.
        // Priority: DB unreal entry → DB named mod (by detected name) → card.WikiStatus (already
        // resolved from the correct mod regardless of name mapping).
        // WikiStatus is "✅"/"🚧" for named mods, "—"/"?"/"💬" for others — only show icon for ✅/🚧.
        string? modStatusText = null;
        var dbEntry = _window.ViewModel.GetDbUnrealEntry(card.GameName);
        if (dbEntry != null)
        {
            modStatusText = dbEntry.Status; // "Done" or "WIP"
        }
        else
        {
            var dbUnityEntry = _window.ViewModel.GetDbUnityEntry(card.GameName);
            if (dbUnityEntry != null)
            {
                modStatusText = dbUnityEntry.Status; // "Done" or "WIP"
            }
            else
            {
                var dbMod = _window.ViewModel.GetDbNamedMod(card.GameName);
                if (dbMod != null)
                {
                    // MapStatus converts "Done"→"✅" and "WIP"→"🚧", so normalise back to text
                    modStatusText = dbMod.Status == "🚧" ? "WIP" : dbMod.Status == "✅" ? "Done" : null;
                }
                else if (!card.IsGenericMod)
                {
                    // Fallback: WikiStatus is already correctly resolved for this card (handles
                    // games detected by folder name like "AFOP" whose DB entry uses the full title)
                    modStatusText = card.WikiStatus == "🚧" ? "WIP"
                                  : card.WikiStatus == "✅" ? "Done"
                                  : null;
                }
            }
        }

        if (string.Equals(modStatusText, "Done", StringComparison.OrdinalIgnoreCase))
        {
            _window.DetailModStatusIcon.Text = "✓";
            _window.DetailModStatusIcon.Foreground = UIFactory.Brush(ResourceKeys.AccentGreenBrush);
            _window.DetailModStatusIcon.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(_window.DetailModStatusIcon, "HDR mod complete");
        }
        else if (string.Equals(modStatusText, "WIP", StringComparison.OrdinalIgnoreCase))
        {
            _window.DetailModStatusIcon.Text = "🔨";
            _window.DetailModStatusIcon.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
            _window.DetailModStatusIcon.Visibility = Visibility.Visible;
            ToolTipService.SetToolTip(_window.DetailModStatusIcon, "HDR mod in progress");
        }
        else
        {
            _window.DetailModStatusIcon.Visibility = Visibility.Collapsed;
        }

        // Utility buttons — set Tag for event handlers
        _window.DetailFavBtn.Tag = card;
        ApplyFavouriteButtonStyle(_window.DetailFavBtn, _window.DetailFavIcon, card.IsFavourite);

        _window.DetailHideBtn.Tag = card;
        _window.DetailHideIcon.Text = card.IsHidden ? "Show" : "Hide";
        ToolTipService.SetToolTip(_window.DetailHideBtn, card.IsHidden
            ? "Show this game in the game list again."
            : "Hide this game from the sidebar (find it again in the Hidden filter)");
        _window.DetailHideBtn.Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush);

        // Folder management buttons
        _window.DetailFolderBtn.Tag = card;
        _window.DetailLaunchBtn.Tag = card; // Launch starts the game shown in the panel

        // AppData button — visible only for UE games with a resolvable AppData/Documents config folder
        // GameConfigRootPath is pre-computed on a background thread (BuildCards/CacheLoad) so
        // no filesystem I/O happens here on the UI thread.
        _window.DetailAppDataBtn.Tag = card;
        _window.DetailAppDataBtn.Visibility = card.GameConfigRootPath != null ? Visibility.Visible : Visibility.Collapsed;

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
        ToolTipService.SetToolTip(_window.DetailHdrToggleBtn, HdrToggleTooltip(hdrActive, hdrOverride != null));

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
            ToolTipService.SetToolTip(_window.DetailResToggleBtn, ResToggleTooltip(resActive, resOverride != null));
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
            bool collapsed  = Layout.IsCollapsed(sectionKey);

            _window.DetailComponentsArrow.Text = collapsed ? "▶" : "▼";
            _window.DetailComponentsBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            _window.DetailComponentsTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
            ToolTipService.SetToolTip(_window.DetailComponentsArrow, SectionHeaderTooltip);
            ToolTipService.SetToolTip(_window.DetailComponentsTitle, SectionHeaderTooltip);

            // Re-subscribe each time panel is populated (card changes)
            _window.DetailComponentsHeader.PointerPressed -= ComponentsHeader_PointerPressed;
            _window.DetailComponentsHeader.PointerPressed += ComponentsHeader_PointerPressed;
            _window.DetailComponentsHeader.PointerEntered -= ComponentsHeader_PointerEntered;
            _window.DetailComponentsHeader.PointerEntered += ComponentsHeader_PointerEntered;
            _window.DetailComponentsHeader.PointerExited  -= ComponentsHeader_PointerExited;
            _window.DetailComponentsHeader.PointerExited  += ComponentsHeader_PointerExited;
            if (_window.DetailComponentsHeader.Parent is FrameworkElement componentsHeaderGrid)
                Layout.RegisterHeader(sectionKey, componentsHeaderGrid);

            // Replace drag handle as first child.
            // The XAML starts with [0]=Arrow, [1]=Title.
            // After insert: [0]=DragHandle, [1]=Arrow, [2]=Title.
            // We must remove any previous drag handle first so we don't accumulate.
            if (_window.DetailComponentsHeader.Children.Count > 0
                && _window.DetailComponentsHeader.Children[0] is TextBlock tb
                && tb.Tag is string t && t == "DragHandle")
                _window.DetailComponentsHeader.Children.RemoveAt(0);
            var dragHandle = MakeDragHandle(_window.DetailComponentSection);
            dragHandle.Tag = "DragHandle";
            _window.DetailComponentsHeader.Children.Insert(0, dragHandle);
            // Now: [0]=DragHandle [1]=Arrow [2]=Title — Count=3

            // The header StackPanel now spans cols 0-3 (Grid.ColumnSpan=4 in XAML) — full width, no clipping.
            // Append the summary directly to the header StackPanel at index 3 (after drag handle, arrow, title).
            // Remove any stale summary from a previous card first.
            while (_window.DetailComponentsHeader.Children.Count > 3)
                _window.DetailComponentsHeader.Children.RemoveAt(3);

            // Always hide the old DetailLumaInfoText slot (no longer used for summary)
            _window.DetailLumaInfoText.Visibility = Visibility.Collapsed;
            _window.DetailLumaInfoText.Inlines.Clear();

            _componentsSummaryCard = card;
            _componentsSummary = BuildComponentsSummary(card);
            if (_componentsSummary != null)
            {
                _componentsSummary.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
                _window.DetailComponentsHeader.Children.Add(_componentsSummary);
            }
        }

        // Populate component rows
        UpdateDetailComponentRows(card);
        Layout.RefreshHeader(); // Tabs: mockup header copies the header filled above
        __sw.Stop();
        if (__sw.ElapsedMilliseconds > 30)
            CrashReporter.Log($"[PopulateDetailPanel] SLOW: '{card.GameName}' took {__sw.ElapsedMilliseconds}ms");
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
            var apiName = api switch
            {
                GraphicsApiType.DirectX8  => "DirectX 8",
                GraphicsApiType.DirectX9  => "DirectX 9",
                GraphicsApiType.DirectX10 => "DirectX 10",
                GraphicsApiType.DirectX11 => "DirectX 11",
                GraphicsApiType.DirectX12 => "DirectX 12",
                GraphicsApiType.Vulkan    => "Vulkan",
                GraphicsApiType.OpenGL    => "OpenGL",
                _                         => label,
            };
            ToolTipService.SetToolTip(badge, $"Detected graphics API: {apiName}");
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

    /// <summary>Decides where the sections go, their order and collapse state.</summary>
    internal DetailLayoutHost Layout { get; }

    /// <summary>Puts the sections in the saved order. Call after all sections are populated+visible.</summary>
    internal void ApplySectionOrder() => Layout.ApplySectionOrder();

    // Drag state
    private Border?  _dragBorder;
    private TextBlock? _dragHandle; // reset to grey on drag end (PointerExited is skipped mid-drag)
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
    /// <summary>
    /// Calculates a fixed column width for use in star-column grids inside the detail panel.
    /// Star columns inside StackPanel/ScrollViewer with HorizontalScrollBarVisibility=Disabled
    /// cause WinUI to enter an infinite layout loop. Use this instead of GridLength.Star.
    /// </summary>
    /// <param name="numCols">Number of equal columns.</param>
    /// <param name="spacing">ColumnSpacing value on the grid.</param>
    /// <param name="overhead">Any additional fixed-width columns (sum of their widths + spacings).</param>
    internal double PanelColW(int numCols, double spacing = 8, double overhead = 0, double containerWidth = 0)
    {
        double w = containerWidth > 0 ? containerWidth : _window.DetailPanel.ActualWidth;
        if (w <= 0) w = 750;
        const double SectionPadding = 28;
        var available = w - SectionPadding - overhead - (numCols - 1) * spacing;
        return Math.Max(80, available / numCols);
    }

    /// <summary>
    /// Favourite button look. Favourited: amber "Favourited" text with the same quiet border as
    /// the buttons beside it (a full amber outline stood out too much). Not favourited: plain.
    /// </summary>
    internal static void ApplyFavouriteButtonStyle(Button btn, TextBlock label, bool isFavourite)
    {
        label.Text = isFavourite ? "Favourited" : "Favourite";
        label.Foreground = UIFactory.Brush(isFavourite ? ResourceKeys.AccentAmberBrush : ResourceKeys.ChipTextBrush);
        btn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderSubtleBrush);
        ToolTipService.SetToolTip(btn, isFavourite
            ? "Favourite. Click to remove it from Favourites."
            : "Add this game to Favourites (find it with the Favourites filter).");
    }

    /// <summary>HDR button tooltip that states the current setting for this game.</summary>
    internal static string HdrToggleTooltip(bool active, bool isOverride)
    {
        var source = isOverride ? "set for this game" : "following Settings › Display & HDR";
        return active
            ? $"Windows HDR: On ({source}).\nWhen you launch the game from RHI, Windows HDR is turned on, then back off when the game closes.\nClick to turn off for this game."
            : $"Windows HDR: Off ({source}).\nClick to turn Windows HDR on when you launch this game from RHI (and back off when it closes).";
    }

    /// <summary>RES button tooltip that states the current setting for this game.</summary>
    internal static string ResToggleTooltip(bool active, bool isOverride)
    {
        var source = isOverride ? "set for this game" : "following Settings › Display & HDR";
        return active
            ? $"Change resolution: On ({source}).\nWhen you launch the game from RHI, the display switches to your target resolution, then back when the game closes.\nClick to turn off for this game."
            : $"Change resolution: Off ({source}).\nClick to switch to your target resolution when you launch this game from RHI (and back when it closes).";
    }

    /// <summary>Tooltip for the arrow + title of every collapsible detail section header.</summary>
    internal const string SectionHeaderTooltip = "Click to collapse or expand";

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
        ToolTipService.SetToolTip(handle, "Drag to reorder sections");

        var handCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var cursorProp  = DetailPanelBuilder.CursorProp;

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
            _dragHandle      = handle;
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
        if (_dragHandle != null) _dragHandle.Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush);
        _dragHandle = null;
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

    private void SaveSectionOrder() => Layout.SaveSectionOrder();

    // ── Components section header event handlers (wired per-card in PopulateDetailPanel) ──

    // ── Components section summary state ─────────────────────────────────────
    private TextBlock? _componentsSummary;
    private GameCardViewModel? _componentsSummaryCard;

    private void ComponentsHeader_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        const string sectionKey = "Components";
        bool collapsed = _window.DetailComponentsBody.Visibility == Visibility.Visible;
        _window.DetailComponentsBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        _window.DetailComponentsArrow.Text      = collapsed ? "▶" : "▼";


        // Rebuild summary on collapse, hide on expand
        if (collapsed)
        {
            while (_window.DetailComponentsHeader.Children.Count > 3)
                _window.DetailComponentsHeader.Children.RemoveAt(3);

            _componentsSummary = _componentsSummaryCard != null
                ? BuildComponentsSummary(_componentsSummaryCard)
                : null;
            if (_componentsSummary != null)
                _window.DetailComponentsHeader.Children.Add(_componentsSummary);
        }
        else
        {
            if (_componentsSummary != null)
                _componentsSummary.Visibility = Visibility.Collapsed;
        }

        Layout.SetCollapsed(sectionKey, collapsed);
    }

    private void ComponentsHeader_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _window.DetailComponentsTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        DetailPanelBuilder.CursorProp
            ?.SetValue(_window.DetailComponentsHeader, handCursor);
    }

    private void ComponentsHeader_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _window.DetailComponentsTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        DetailPanelBuilder.CursorProp
            ?.SetValue(_window.DetailComponentsHeader, arrowCursor);
    }

    // ── Section-collapse helper ──────────────────────────────────────────────

    // Cancels in-flight NVAPI background scans when the user navigates to a new game.
    // Every call to BuildOverridesPanel cancels the previous token and issues a new one.
    // Tasks waiting on _panelScanSemaphore check the token and bail immediately,
    // freeing their thread pool thread instead of sitting blocked.
    private CancellationTokenSource _panelScanCts = new();
    internal void StopBackgroundWork() => _panelScanCts.Cancel();

    // Limits concurrent background scans to prevent thread pool saturation
    // when rapidly clicking through games. Capacity of 1 ensures at most one
    // NVAPI/file-scan is running at a time — all others are cancelled immediately
    // when BuildOverridesPanel fires for a new game via _panelScanCts.
    private static readonly SemaphoreSlim _panelScanSemaphore = new SemaphoreSlim(1, 1);

    // ── Collapsed section summary helpers ────────────────────────────────────

    /// <summary>
    /// Creates a TextBlock with inline white + green runs for a collapsed section summary.
    /// Format: "Name1 version1  ·  Name2 version2"
    /// Each entry is (labelText, versionText?) — label in TextSecondaryBrush, version in #5ECB7D green.
    /// Returns null when the list is empty (nothing installed/active → don't show anything).
    /// </summary>
    internal static TextBlock? MakeSectionSummaryInlines(
        IEnumerable<(string Label, string? Version)> items, string? tooltip = null)
    {
        var entries = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Label))
            .ToList();
        if (entries.Count == 0) return null;

        var tb = new TextBlock
        {
            FontSize  = 11,
            Margin    = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };

        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0)
            {
                tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text       = "  ·  ",
                    Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                });
            }

            tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text       = entries[i].Label,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            });

            if (!string.IsNullOrWhiteSpace(entries[i].Version))
            {
                tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text       = " " + entries[i].Version,
                    Foreground = UIFactory.GetBrush("#5ECB7D"),
                });
            }
        }

        if (!string.IsNullOrEmpty(tooltip))
            ToolTipService.SetToolTip(tb, tooltip);

        return tb;
    }

    /// <summary>
    /// Builds the summary for the Components section from the current card state.
    /// Returns null when nothing is installed.
    /// </summary>
    internal static TextBlock? BuildComponentsSummary(GameCardViewModel card)
    {
        var entries = new List<(string Label, string? Version)>();

        if (card.IsRefInstalled)
            entries.Add(("RE Framework", card.RefInstalledVersion));
        if (card.IsRsInstalled && card.LumaStatus is not GameStatus.Installed and not GameStatus.UpdateAvailable)
            entries.Add(("ReShade", card.RsInstalledVersion));
        if (card.IsRdxInstalled)
            entries.Add(("RenoDX", card.RdxInstalledVersion));
        if (card.IsUlInstalled)
            entries.Add(("ReLimiter", card.UlInstalledVersion));
        if (card.IsDcInstalled)
            entries.Add(("DC", card.DcInstalledVersion));
        if (card.IsLumaInstalled)
            entries.Add(("Luma", "On"));

        return MakeSectionSummaryInlines(entries, "Installed components and versions.");
    }

    /// <summary>
    /// Builds the summary for the Game Overrides section.
    /// Shows active non-default per-game overrides: RS channel, API, launch args.
    /// Returns null when everything is at default.
    /// </summary>
    internal TextBlock? BuildGameOverridesSummary(GameCardViewModel card)
    {
        var entries = new List<(string Label, string? Version)>();

        var channel = _window.ViewModel.GetReShadeChannelOverride(card.GameName, card.Source ?? "");
        if (!string.IsNullOrEmpty(channel))
            entries.Add(("RS Channel:", channel));

        var apis = _window.ViewModel.GetApiOverride(card.GameName, card.Source ?? "");
        if (apis is { Count: > 0 })
            entries.Add(("API:", string.Join("+", apis)));

        // Shaders / Addons — show when set to non-default
        var shaderMode = _window.ViewModel.GetPerGameShaderMode(card.GameName, card.Source ?? "");
        if (!string.IsNullOrEmpty(shaderMode) && shaderMode != "Global")
            entries.Add(("Shaders:", shaderMode));

        var addonMode = _window.ViewModel.GetPerGameAddonMode(card.GameName, card.Source ?? "");
        if (!string.IsNullOrEmpty(addonMode) && addonMode != "Global")
            entries.Add(("Addons:", addonMode));

        // Update inclusion — show only when any component is excluded
        var excluded = new List<string>();
        if (_window.ViewModel.IsUpdateAllExcludedReShade(card.GameName, card.Source ?? "")) excluded.Add("RS");
        if (_window.ViewModel.IsUpdateAllExcludedRenoDx(card.GameName, card.Source ?? "")) excluded.Add("RDX");
        if (_window.ViewModel.IsUpdateAllExcludedUl(card.GameName, card.Source ?? ""))     excluded.Add("RL");
        if (_window.ViewModel.IsUpdateAllExcludedDc(card.GameName, card.Source ?? ""))     excluded.Add("DC");
        if (_window.ViewModel.IsUpdateAllExcludedOs(card.GameName, card.Source ?? ""))     excluded.Add("OS");
        if (excluded.Count > 0)
            entries.Add(("Excluded:", string.Join(" ", excluded)));

        var launchArgs = _gameNameService.LaunchArgsOverrides.TryGetValue(card.GameName, out var la)
            ? la : null;
        if (!string.IsNullOrWhiteSpace(launchArgs))
            entries.Add(("Args:", launchArgs));

        // DLL naming overrides — show each custom filename that's set
        if (_window.ViewModel.HasDllOverride(card.GameName))
        {
            var cfg = _window.ViewModel.GetDllOverride(card.GameName);
            if (cfg != null)
            {
                if (!string.IsNullOrEmpty(cfg.ReShadeFileName) && cfg.ReShadeFileName != "--------")
                    entries.Add(("RS DLL:", cfg.ReShadeFileName));
                if (!string.IsNullOrEmpty(cfg.DcFileName) && cfg.DcFileName != "--------")
                    entries.Add(("DC DLL:", cfg.DcFileName));
                if (!string.IsNullOrEmpty(cfg.OsFileName) && cfg.OsFileName != "--------")
                    entries.Add(("OS DLL:", cfg.OsFileName));
            }
        }

        return MakeSectionSummaryInlines(entries,
            "Game overrides that differ from the defaults.\n"
            + "RS = ReShade, RDX = RenoDX, RL = ReLimiter, DC = Display Commander, OS = OptiScaler.\n"
            + "API = graphics API override. Args = launch arguments. DLL = custom DLL file name.\n"
            + "Excluded = components Update All skips for this game.");
    }
}
