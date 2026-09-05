// DetailPanelBuilder.NeuralRendering.cs — Self-contained Neural Rendering section.
// Shown between Game Overrides and NVIDIA Profile Overrides.
// Handles DLSS5 Tool, DLSS5 Tool + DX11 Bridge, DLSS Tool (ShortFuse), and DLSS5 Feeder.
// All files are deployed automatically — no addon picker required.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    // ── Section IDs for the Bridge and Feeder addons (from manifest addonPacks) ──
    private const string BridgePackageName  = "DLSS5 DX11 Bridge";
    private const string FeederPackageName  = "DLSS5 Feeder";
    private const string BridgeDeployFile   = "dlss5-bridge.addon64";
    private const string FeederDeployFile64 = "dlss5-feed.addon64";
    private const string FeederDeployFile32 = "dlss5-feed.addon32";

    // ── Method constants ──────────────────────────────────────────────────────
    private const string NrMethodDlss5Tool        = "DLSS5Tool";
    private const string NrMethodDlss5ToolBridge  = "DLSS5ToolBridge";
    private const string NrMethodShortFuse         = "ShortFuse";
    private const string NrMethodFeeder            = "Feeder";

    public void BuildNeuralRenderingSection(GameCardViewModel card)
    {
        _window.NeuralRenderingPanel.Children.Clear();

        if (string.IsNullOrEmpty(card.InstallPath)) return;

        var installPath = card.InstallPath;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";

        var rdx5Svc     = App.Services.GetRequiredService<Renodx5AddonService>();
        var addonSvc    = _window.ViewModel.AddonPackServiceInstance;
        var dlssSvc     = _dlssStreamlineService;

        // ── Detect current install state (off the UI thread — all File.Exists calls) ──
        _ = Task.Run(async () =>
        {
            await _panelScanSemaphore.WaitAsync();
            try
            {
            bool dlss5Installed  = rdx5Svc.IsInstalledIn(installPath);
            bool sfInstalled     = rdx5Svc.IsSfInstalledIn(installPath);
            bool nrDllPresent    = File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll"));
            bool nrDllOwnedByRhi = File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll.original"));
            string? nrDllVersion = null;
            if (nrDllPresent)
                nrDllVersion = DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlssnr.dll")));
            bool bridgePresent = File.Exists(Path.Combine(installPath, BridgeDeployFile));
            bool feederPresent = File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64));

            _window.DispatcherQueue?.TryEnqueue(() =>
                BuildNeuralRenderingSectionWithData(card, dlss5Installed, sfInstalled,
                    nrDllPresent, nrDllOwnedByRhi, nrDllVersion, bridgePresent, feederPresent));
            }
            finally { _panelScanSemaphore.Release(); }
        });
    }

    private void BuildNeuralRenderingSectionWithData(
        GameCardViewModel card,
        bool dlss5Installed, bool sfInstalled,
        bool nrDllPresent, bool nrDllOwnedByRhi, string? nrDllVersion,
        bool bridgePresent, bool feederPresent)
    {
        // Guard: if the user navigated away before the background scan finished, bail out
        if (_window.ViewModel.SelectedGame != card) return;

        // Re-clear the panel in case another card was selected while we were scanning
        _window.NeuralRenderingPanel.Children.Clear();

        var installPath = card.InstallPath!;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";

        var rdx5Svc     = App.Services.GetRequiredService<Renodx5AddonService>();
        var addonSvc    = _window.ViewModel.AddonPackServiceInstance;
        var dlssSvc     = _dlssStreamlineService;
        bool hasDlss  = card.HasAnyDlssStreamline;
        bool isDx12   = card.GraphicsApi == GraphicsApiType.DirectX12;
        bool isDx11   = card.GraphicsApi == GraphicsApiType.DirectX11;
        bool isVulkan = card.GraphicsApi == GraphicsApiType.Vulkan;
        bool isDx9    = card.GraphicsApi == GraphicsApiType.DirectX9;
        bool is32Bit  = card.Is32Bit;

        // ── Infer current method from installed state (migration) ─────────────
        string? storedMethod = _window.ViewModel.GetNrMethodOverride(gameName, store);
        if (storedMethod == null)
        {
            // Infer from what's on disk
            if (sfInstalled)
                storedMethod = NrMethodShortFuse;
            else if (dlss5Installed && bridgePresent)
                storedMethod = NrMethodDlss5ToolBridge;
            else if (dlss5Installed)
                storedMethod = NrMethodDlss5Tool;
            else if (feederPresent)
                storedMethod = NrMethodFeeder;
        }

        // ── Auto-select best method if nothing stored/inferred ────────────────
        string effectiveMethod = storedMethod ?? (
            is32Bit              ? NrMethodFeeder :
            !hasDlss             ? NrMethodFeeder :
            (isDx11 || isVulkan) ? NrMethodDlss5ToolBridge :
                                   NrMethodShortFuse);

        // ── Build method combo items (show all, disable inapplicable) ─────────
        var methodItems = new[]
        {
            new { Name = "DLSS5 Tool",                Key = NrMethodDlss5Tool,       Enabled = hasDlss && !is32Bit },
            new { Name = "DLSS5 Tool + DX11 Bridge",  Key = NrMethodDlss5ToolBridge, Enabled = hasDlss && (isDx11 || isVulkan) && !is32Bit },
            new { Name = "DLSS Tool (ShortFuse)",      Key = NrMethodShortFuse,       Enabled = !is32Bit },
            new { Name = "DLSS5 Feeder",               Key = NrMethodFeeder,          Enabled = true },
        };

        // ── Header ────────────────────────────────────────────────────────────
        const string nrSectionKey = "NeuralRendering";
        var nrSettings   = _window.ViewModel.Settings;
        bool nrCollapsed = nrSettings.CollapsedDetailSections.Contains(nrSectionKey);

        var nrArrow = new TextBlock
        {
            Text      = nrCollapsed ? "▶" : "▼",
            FontSize  = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(0, 0, 6, 0),
        };
        var nrTitle = new TextBlock
        {
            Text       = "Neural Rendering",
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var nrHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        nrHeaderRow.Children.Add(MakeDragHandle(_window.NeuralRenderingContainer));
        nrHeaderRow.Children.Add(nrArrow);
        nrHeaderRow.Children.Add(nrTitle);
        _window.NeuralRenderingPanel.Children.Add(nrHeaderRow);

        // Body wrapper — all section content goes into this
        var nrBody = new StackPanel { Spacing = 6, Visibility = nrCollapsed ? Visibility.Collapsed : Visibility.Visible };
        _window.NeuralRenderingPanel.Children.Add(nrBody);

        nrHeaderRow.PointerEntered += (s, e) => nrTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        nrHeaderRow.PointerExited  += (s, e) => nrTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var nrHandCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var nrArrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var nrCursorProp  = typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        nrHeaderRow.PointerEntered += (s, e) => nrCursorProp?.SetValue(nrHeaderRow, nrHandCursor);
        nrHeaderRow.PointerExited  += (s, e) => nrCursorProp?.SetValue(nrHeaderRow, nrArrowCursor);
        nrHeaderRow.PointerPressed += (s, e) =>
        {
            bool nowCollapsed = nrBody.Visibility == Visibility.Visible;
            nrBody.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            nrArrow.Text = nowCollapsed ? "▶" : "▼";
            if (nowCollapsed) nrSettings.CollapsedDetailSections.Add(nrSectionKey);
            else              nrSettings.CollapsedDetailSections.Remove(nrSectionKey);
            _window.ViewModel.SaveSettingsPublic();
        };

        // ── Row 1: Method combo + NR version combo ────────────────────────────
        var row1 = new Grid { ColumnSpacing = 8 };
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Method combo
        var methodStack = new StackPanel { Spacing = 2 };
        methodStack.Children.Add(new TextBlock { Text = "Method", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush) });

        var methodCombo = new ComboBox
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
        foreach (var item in methodItems)
        {
            var cbi = new ComboBoxItem { Content = item.Name, IsEnabled = item.Enabled };
            if (!item.Enabled) cbi.Opacity = 0.4;
            methodCombo.Items.Add(cbi);
            if (item.Key == effectiveMethod)
                methodCombo.SelectedItem = cbi;
        }
        if (methodCombo.SelectedIndex < 0) methodCombo.SelectedIndex = 0;
        ToolTipService.SetToolTip(methodCombo,
            "DLSS5 Tool: for DX12 native-DLSS games.\n" +
            "DLSS5 Tool + DX11 Bridge: for DX11/Vulkan native-DLSS games.\n" +
            "DLSS Tool (ShortFuse): alternative full-stack install for native-DLSS games.\n" +
            "DLSS5 Feeder: for games with no native DLSS (DX11, DX12, Vulkan, 32-bit).");
        methodStack.Children.Add(methodCombo);
        Grid.SetColumn(methodStack, 0);
        row1.Children.Add(methodStack);

        // NR version combo (only shown for DLSS5 Tool / Bridge methods)
        var nrVersionStack = new StackPanel { Spacing = 2 };
        nrVersionStack.Children.Add(new TextBlock { Text = "NR DLL Version", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush) });

        var nrVersionCombo = new ComboBox
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(6),
        };
        var nrVersions = dlssSvc.DlssnrVersions.ToList();
        nrVersions.Insert(0, "Latest");
        foreach (var v in nrVersions)
            nrVersionCombo.Items.Add(v);
        nrVersionCombo.SelectedIndex = 0;
        ToolTipService.SetToolTip(nrVersionCombo, "NR DLL version to deploy. 'Latest' always uses the newest available.");
        nrVersionStack.Children.Add(nrVersionCombo);
        Grid.SetColumn(nrVersionStack, 1);
        row1.Children.Add(nrVersionStack);

        nrBody.Children.Add(row1);

        // ── Status line ───────────────────────────────────────────────────────
        var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        nrBody.Children.Add(statusPanel);

        void RefreshStatus()
        {
            // Gather all file I/O on a background thread, then update UI
            _ = Task.Run(async () =>
            {
                await _panelScanSemaphore.WaitAsync();
                try
                {
                bool d5i    = rdx5Svc.IsInstalledIn(installPath);
                bool sfi    = rdx5Svc.IsSfInstalledIn(installPath);
                bool nri    = File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll"));
                bool bri    = File.Exists(Path.Combine(installPath, BridgeDeployFile));
                bool fei    = File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64));
                bool rsi    = card.IsRsInstalled;
                bool dlssi  = File.Exists(Path.Combine(installPath, "nvngx_dlss.dll"));
                bool dlssdi = File.Exists(Path.Combine(installPath, "nvngx_dlssd.dll"));
                bool dlssgi = File.Exists(Path.Combine(installPath, "nvngx_dlssg.dll"));
                string? nrv    = nri    ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlssnr.dll"))) : null;
                string? dlssv  = dlssi  ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlss.dll")))   : null;
                string? dlssdv = dlssdi ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlssd.dll")))  : null;
                string? dlssgv = dlssgi ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(Path.Combine(installPath, "nvngx_dlssg.dll")))  : null;

                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    if (_window.ViewModel.SelectedGame != card) return;
                    RefreshStatusWithData(d5i, sfi, nri, bri, fei, rsi, dlssi, dlssdi, dlssgi, nrv, dlssv, dlssdv, dlssgv);
                });
                }
                finally { _panelScanSemaphore.Release(); }
            });
        }

        void RefreshStatusWithData(
            bool d5i, bool sfi, bool nri, bool bri, bool fei, bool rsi,
            bool dlssi, bool dlssdi, bool dlssgi,
            string? nrv, string? dlssv, string? dlssdv, string? dlssgv)
        {
            statusPanel.Children.Clear();

            void Tag(string text, bool ok)
            {
                statusPanel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    Foreground = UIFactory.Brush(ok ? ResourceKeys.AccentGreenBrush : ResourceKeys.TextTertiaryBrush),
                });
            }

            var selectedKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string
                           ?? methodItems.ElementAtOrDefault(methodCombo.SelectedIndex)?.Key
                           ?? effectiveMethod;

            Tag(rsi ? "✓ ReShade" : "✗ ReShade", rsi);

            switch (selectedKey)
            {
                case NrMethodDlss5Tool:
                    Tag(d5i ? "✓ DLSS5 Tool" : "✗ DLSS5 Tool", d5i);
                {
                    var det = card.DlssDetection;
                    var srPath2 = det?.DlssPath  ?? Path.Combine(installPath, "nvngx_dlss.dll");
                    var rrPath2 = det?.DlssdPath ?? Path.Combine(installPath, "nvngx_dlssd.dll");
                    var fgPath2 = det?.DlssgPath ?? Path.Combine(installPath, "nvngx_dlssg.dll");
                    var nrPath3 = det?.DlssnrPath ?? Path.Combine(installPath, "nvngx_dlssnr.dll");
                    bool srOk2 = File.Exists(srPath2); bool rrOk2 = File.Exists(rrPath2);
                    bool fgOk2 = File.Exists(fgPath2); bool nrOk3 = File.Exists(nrPath3);
                    if (card.HasDlss)
                    {
                        Tag(srOk2 ? $"✓ DLSS SR {DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(srPath2))}" : "✗ DLSS SR", srOk2);
                        Tag(rrOk2 ? $"✓ DLSS RR {DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(rrPath2))}" : "✗ DLSS RR", rrOk2);
                        Tag(fgOk2 ? $"✓ DLSS FG {DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(fgPath2))}" : "✗ DLSS FG", fgOk2);
                    }
                    Tag(nrOk3 ? $"✓ NR DLL {DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(nrPath3))}" : "✗ NR DLL", nrOk3);
                }
                    break;
                case NrMethodDlss5ToolBridge:
                    Tag(d5i ? "✓ DLSS5 Tool" : "✗ DLSS5 Tool", d5i);
                    Tag(bri ? "✓ DX11 Bridge" : "✗ DX11 Bridge", bri);
                {
                    var det = card.DlssDetection;
                    var srPath3 = det?.DlssPath  ?? Path.Combine(installPath, "nvngx_dlss.dll");
                    var rrPath3 = det?.DlssdPath ?? Path.Combine(installPath, "nvngx_dlssd.dll");
                    var fgPath3 = det?.DlssgPath ?? Path.Combine(installPath, "nvngx_dlssg.dll");
                    var nrPath4 = det?.DlssnrPath ?? Path.Combine(installPath, "nvngx_dlssnr.dll");
                    bool srOk3 = File.Exists(srPath3); bool rrOk3 = File.Exists(rrPath3);
                    bool fgOk3 = File.Exists(fgPath3); bool nrOk4 = File.Exists(nrPath4);
                    if (card.HasDlss)
                    {
                        Tag(srOk3 ? $"✓ DLSS SR {DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(srPath3))}" : "✗ DLSS SR", srOk3);
                        Tag(rrOk3 ? $"✓ DLSS RR {DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(rrPath3))}" : "✗ DLSS RR", rrOk3);
                        Tag(fgOk3 ? $"✓ DLSS FG {DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(fgPath3))}" : "✗ DLSS FG", fgOk3);
                    }
                    Tag(nrOk4 ? $"✓ NR DLL {DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(nrPath4))}" : "✗ NR DLL", nrOk4);
                }
                    break;
                case NrMethodShortFuse:
                    Tag(sfi    ? "✓ DLSS Tool (ShortFuse)"  : "✗ DLSS Tool (ShortFuse)", sfi);
                {
                    // Use detected DLL paths — ShortFuse deploys to wherever DLSS lives (deep plugin folders on UE games)
                    var det    = card.DlssDetection;
                    var srPath = det?.DlssPath  ?? Path.Combine(installPath, "nvngx_dlss.dll");
                    var rrPath = det?.DlssdPath ?? Path.Combine(installPath, "nvngx_dlssd.dll");
                    var fgPath = det?.DlssgPath ?? Path.Combine(installPath, "nvngx_dlssg.dll");
                    var nrSfPath = det?.DlssnrPath ?? Path.Combine(installPath, "nvngx_dlssnr.dll");
                    bool srOk = File.Exists(srPath);
                    bool rrOk = File.Exists(rrPath);
                    bool fgOk = File.Exists(fgPath);
                    bool nrSfOk = File.Exists(nrSfPath);
                    string? srv   = srOk   ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(srPath))   : null;
                    string? rrvSf = rrOk   ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(rrPath))   : null;
                    string? fgvSf = fgOk   ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(fgPath))   : null;
                    string? nrvSf = nrSfOk ? DlssStreamlineService.FormatVersion(dlssSvc.GetFileVersion(nrSfPath)) : null;
                    Tag(srOk   ? $"✓ DLSS SR {srv}"   : "✗ DLSS SR",  srOk);
                    Tag(rrOk   ? $"✓ DLSS RR {rrvSf}" : "✗ DLSS RR", rrOk);
                    Tag(fgOk   ? $"✓ DLSS FG {fgvSf}" : "✗ DLSS FG", fgOk);
                    Tag(nrSfOk ? $"✓ NR DLL {nrvSf}"  : "✗ NR DLL",  nrSfOk);

                    // ASI Loader status
                    var ualName = _window.ViewModel.GetUalInstalledAs(gameName, store);
                    bool ualOk  = !string.IsNullOrEmpty(ualName)
                               && File.Exists(Path.Combine(installPath, ualName));
                    Tag(ualOk ? $"✓ ASI Loader ({ualName})" : "✗ ASI Loader", ualOk);
                }
                    break;
                case NrMethodFeeder:
                    Tag(fei   ? "✓ Feeder Addon"            : "✗ Feeder Addon",  fei);
                    Tag(d5i   ? "✓ DLSS5 Tool"              : "✗ DLSS5 Tool",    d5i);
                    Tag(dlssi ? $"✓ DLSS SR {dlssv}"        : "✗ DLSS SR",       dlssi);
                    Tag(nri   ? $"✓ NR DLL {nrv}"           : "✗ NR DLL",        nri);
                    var shadersDir = Path.Combine(installPath, ShaderPackService.GameReShadeShaders, "Shaders");
                    bool feedFxPresent = Directory.Exists(shadersDir) &&
                        Directory.GetFiles(shadersDir, "DLSS5_Feed.fx", SearchOption.AllDirectories).Length > 0;
                    bool lumeniteFxPresent = Directory.Exists(shadersDir) &&
                        Directory.GetFiles(shadersDir, "lumenite_Kernel.fx", SearchOption.AllDirectories).Length > 0;
                    Tag(feedFxPresent    ? "✓ Feed.fx"    : "✗ Feed.fx",    feedFxPresent);
                    Tag(lumeniteFxPresent ? "✓ LumeniteFX" : "✗ LumeniteFX", lumeniteFxPresent);
                    break;
                default:
                    Tag("Not installed", false);
                    break;
            }
        }

        RefreshStatus();

        // ── Description panel (switches per method) ──────────────────────────
        var descBorder = new Border
        {
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 4, 0, 0),
        };
        var descStack = new StackPanel { Spacing = 4 };
        var descText = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        var descLink = new HyperlinkButton
        {
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            Padding = new Thickness(0),
        };
        descStack.Children.Add(descText);
        descStack.Children.Add(descLink);
        descBorder.Child = descStack;
        nrBody.Children.Add(descBorder);

        void UpdateDescription(string methodKey)
        {
            switch (methodKey)
            {
                case NrMethodDlss5Tool:
                    descText.Text = hasDlss
                        ? "For DX12 games with native DLSS. Deploys the DLSS5 Tool ReShade addon and nvngx_dlssnr.dll. Recommended for most DX12 titles."
                        : "For DX12 games with native DLSS. This game has no detected DLSS — consider DLSS Tool (ShortFuse) instead.";
                    descLink.Content = "DLSS5 Tool info →";
                    descLink.NavigateUri = new Uri("https://discord.com/channels/1408098019194310818/1543802634991968366");
                    break;
                case NrMethodDlss5ToolBridge:
                    descText.Text = "For DX11 and Vulkan games that already have native DLSS. The bridge mirrors the game's DLSS onto a private DX12 session so the NR addon can hook it. Also works for DX11 with no native DLSS via optical flow (lower quality).";
                    descLink.Content = "DX11 Bridge info →";
                    descLink.NavigateUri = new Uri("https://github.com/NIGos/dlss5-bridge");
                    break;
                case NrMethodShortFuse:
                    descText.Text = "For any 64-bit game with native DLSS. Deploys the full DLSS SR/RR/FG/NR stack and Streamline alongside the ReShade addon. Supports DX12, DX11, DX9, and Vulkan. Alternative to DLSS5 Tool for non-DLSS games.";
                    descLink.Content = "ShortFuse info →";
                    descLink.NavigateUri = new Uri("https://discord.com/channels/1408098019194310818/1543975158937821315");
                    break;
                case NrMethodFeeder:
                    descText.Text = is32Bit
                        ? "For 32-bit games. Feeds a synthetic DLSS contract from ReShade depth and motion vectors. Deploys the Feeder addon, DLSS5 Tool (neural consumer), NR DLL, DLSS SR DLL, and the required shaders (DLSS5_Feed.fx + LumeniteFX)."
                        : "For games with no native DLSS (DX11, DX12, Vulkan, OpenGL). Feeds a synthetic DLSS contract from ReShade depth and motion vectors. Deploys the Feeder addon, DLSS5 Tool (neural consumer), NR DLL, DLSS SR DLL, and required shaders.";
                    descLink.Content = "Feeder setup guide →";
                    descLink.NavigateUri = new Uri("https://github.com/jlrouzies-fr/DLSS5-Feeder");
                    break;
            }
        }

        UpdateDescription(effectiveMethod);

        // ── Row 2: Install / Remove buttons ──────────────────────────────────
        var btnRow = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // cog (ShortFuse only)

        var installBtn = new Button
        {
            FontSize = 12,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
        };

        var removeBtn = new Button
        {
            Content = "Remove",
            FontSize = 12,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentRedBrush),
        };

        // ShortFuse-only cog button
        var sfCogBtn = new Button
        {
            Width = 34, Height = 34,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            Content = new TextBlock { Text = "⚙", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
            Visibility = effectiveMethod == NrMethodShortFuse ? Visibility.Visible : Visibility.Collapsed,
        };
        ToolTipService.SetToolTip(sfCogBtn, "ShortFuse settings — auto-configure ReShade for FrameGen");
        sfCogBtn.Click += async (s, e) =>
        {
            bool currentEnabled = _window.ViewModel.GetSfAutoConfigEnabled(gameName, store);
            bool newEnabled = currentEnabled;

            var toggleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var togLabel = new TextBlock
            {
                Text = "Auto-configure ReShade for FrameGen",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var tog = new ToggleSwitch
            {
                IsOn = currentEnabled,
                OnContent = "On",
                OffContent = "Off",
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 0,
            };
            toggleRow.Children.Add(togLabel);
            toggleRow.Children.Add(tog);

            var desc = new TextBlock
            {
                Text = "When On, installing ShortFuse will automatically:\n" +
                       "• Rename ReShade to Reshade64.asi\n" +
                       "• Install ASI Loader (winmm → version → dinput8)\n" +
                       "• Write HookStreamline=1 and HookDirectX=1 to reshade.ini\n\n" +
                       "These steps are needed for FrameGen to work correctly after ReShade.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            };

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(toggleRow);
            content.Children.Add(desc);

            tog.Toggled += (ts, te) => newEnabled = tog.IsOn;

            var dlg = new ContentDialog
            {
                Title = "ShortFuse Settings",
                Content = content,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = _window.Content.XamlRoot,
            };
            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary && newEnabled != currentEnabled)
                _window.ViewModel.SetSfAutoConfigEnabled(gameName, newEnabled, store);
        };

        void UpdateInstallBtnAppearance()
        {
            var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string
                      ?? methodItems.ElementAtOrDefault(methodCombo.SelectedIndex)?.Key
                      ?? effectiveMethod;
            bool anyInstalled = selKey switch
            {
                NrMethodDlss5Tool       => rdx5Svc.IsInstalledIn(installPath) || File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll.original")),
                NrMethodDlss5ToolBridge => rdx5Svc.IsInstalledIn(installPath) || File.Exists(Path.Combine(installPath, BridgeDeployFile)),
                NrMethodShortFuse       => rdx5Svc.IsSfInstalledIn(installPath),
                NrMethodFeeder          => File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64)),
                _                       => false,
            };

            bool isFeeder = selKey == NrMethodFeeder;

            // Install button appearance
            if (isFeeder)
            {
                installBtn.Content = "Install Feeder Addon";
                installBtn.Background  = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                installBtn.Foreground  = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
                installBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
            }
            else if (anyInstalled)
            {
                installBtn.Content = "Reinstall";
                installBtn.Background  = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
                installBtn.Foreground  = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
                installBtn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush);
            }
            else
            {
                installBtn.Content = "Install Neural Rendering";
                installBtn.Background  = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                installBtn.Foreground  = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
                installBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
            }

            // NR version combo only relevant for DLSS5 Tool / Bridge (not ShortFuse/Feeder)
            bool nrVersionRelevant = selKey == NrMethodDlss5Tool || selKey == NrMethodDlss5ToolBridge;
            nrVersionStack.Opacity   = nrVersionRelevant ? 1.0 : 0.4;
            nrVersionCombo.IsEnabled = nrVersionRelevant;

            // Remove button visibility
            removeBtn.Visibility = anyInstalled ? Visibility.Visible : Visibility.Collapsed;

            // ShortFuse cog — only visible when ShortFuse is selected
            sfCogBtn.Visibility = selKey == NrMethodShortFuse ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateInstallBtnAppearance();

        // Tag each combo item with its key for easy lookup
        for (int i = 0; i < methodCombo.Items.Count; i++)
        {
            if (methodCombo.Items[i] is ComboBoxItem cbi)
                cbi.Tag = methodItems[i].Key;
        }

        // Method combo change handler
        bool methodComboInit = true;
        methodCombo.SelectionChanged += (s, ev) =>
        {
            if (methodComboInit) return;
            var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? effectiveMethod;
            _window.ViewModel.SetNrMethodOverride(gameName, selKey, store);
            UpdateInstallBtnAppearance();
            UpdateDescription(selKey);
            RefreshStatus();
        };
        methodComboInit = false;

        // ── Install button click ──────────────────────────────────────────────
        installBtn.Click += async (s, ev) =>
        {
            installBtn.IsEnabled = false;
            removeBtn.IsEnabled  = false;
            installBtn.Content   = "Installing...";

            var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? effectiveMethod;

            try
            {
                // Ensure ReShade is installed first — all NR methods require it
                if (!card.IsRsInstalled)
                {
                    _window.DispatcherQueue?.TryEnqueue(() => installBtn.Content = "Installing ReShade...");
                    await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(card).ConfigureAwait(false);
                    // Wait for card to reflect installed state
                    await Task.Delay(500).ConfigureAwait(false);
                }

                switch (selKey)
                {
                    case NrMethodDlss5Tool:
                        await InstallDlss5ToolAsync(card, installBtn, nrVersionCombo, rdx5Svc, dlssSvc, addonSvc);
                        break;

                    case NrMethodDlss5ToolBridge:
                        await InstallDlss5ToolAsync(card, installBtn, nrVersionCombo, rdx5Svc, dlssSvc, addonSvc);
                        await InstallBridgeAddonAsync(card, installBtn, addonSvc);
                        break;

                    case NrMethodShortFuse:
                        await InstallShortFuseAsync(card, installBtn, rdx5Svc, dlssSvc);
                        break;

                    case NrMethodFeeder:
                        await InstallFeederAddonAsync(card, installBtn, addonSvc);
                        break;
                }

                // Persist chosen method
                _window.ViewModel.SetNrMethodOverride(gameName, selKey, store);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[NeuralRendering.Install] Failed for '{gameName}' — {ex.Message}");
                _window.DispatcherQueue?.TryEnqueue(() => installBtn.Content = "Install failed");
            }
            finally
            {
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    installBtn.IsEnabled = true;
                    removeBtn.IsEnabled  = true;
                    UpdateInstallBtnAppearance();
                    RefreshStatus();
                    // Rebuild the full overrides panel so shader mode combo + NR section both refresh
                    var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(store) || c.Source == store));
                    if (targetCard != null)
                        BuildOverridesPanel(targetCard);
                });
            }
        };

        // ── Remove button click ───────────────────────────────────────────────
        removeBtn.Click += async (s, ev) =>
        {
            removeBtn.IsEnabled  = false;
            installBtn.IsEnabled = false;

            var selKey = (methodCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? effectiveMethod;

            try
            {
                await Task.Run(() =>
                {
                    switch (selKey)
                    {
                        case NrMethodDlss5Tool:
                            rdx5Svc.Uninstall(installPath);
                            RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                            break;

                        case NrMethodDlss5ToolBridge:
                            rdx5Svc.Uninstall(installPath);
                            RemoveAddonFile(installPath, BridgeDeployFile, "NeuralRendering.Remove.Bridge");
                            RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                            break;

                        case NrMethodShortFuse:
                        {
                            var det = _dlssStreamlineService.Detect(installPath);
                            rdx5Svc.UninstallSf(installPath, det.HasAny ? det : null);

                            // Revert ShortFuse auto-config (rename Reshade64.asi back, remove UAL)
                            _window.ViewModel.RevertSfAutoConfig(card);
                            break;
                        }

                        case NrMethodFeeder:
                        {
                            var file = card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64;
                            RemoveAddonFile(installPath, file, "NeuralRendering.Remove.Feeder");

                            // Remove DLSS5 Tool neural consumer
                            rdx5Svc.Uninstall(installPath);

                            // Remove nvngx_dlss.dll if we placed it (sentinel)
                            var dlssDest = Path.Combine(installPath, "nvngx_dlss.dll");
                            var dlssSentinel = dlssDest + ".original";
                            if (File.Exists(dlssSentinel))
                            {
                                var info = new FileInfo(dlssSentinel);
                                if (info.Length == 0) { try { File.Delete(dlssDest); File.Delete(dlssSentinel); } catch { } }
                                else { try { File.Copy(dlssSentinel, dlssDest, overwrite: true); File.Delete(dlssSentinel); } catch { } }
                            }

                            // Remove NR dll
                            rdx5Svc.RemoveNrDll(installPath);

                            // Remove only DLSS5Feeder + LumeniteFX shader files — never wipe the whole folder
                            try
                            {
                                var gameKey = Models.GameKey.FromCard(gameName, store).ToKey();
                                var shadersDir  = Path.Combine(installPath, ShaderPackService.GameReShadeShaders, "Shaders");
                                var texturesDir = Path.Combine(installPath, ShaderPackService.GameReShadeShaders, "Textures");
                                var packsToRemove = new[] { "DLSS5Feeder", "LumeniteFX" };

                                // Delete specific pack files from the game's shader folder
                                foreach (var packId in packsToRemove)
                                {
                                    var files = _shaderPackService.GetPackShaderFiles(new[] { packId });
                                    foreach (var f in files)
                                    {
                                        var fxPath = Path.Combine(shadersDir, f);
                                        try { if (File.Exists(fxPath)) File.Delete(fxPath); } catch { }
                                    }
                                }
                                // Also remove lumenite texture
                                if (Directory.Exists(texturesDir))
                                {
                                    foreach (var f in Directory.GetFiles(texturesDir, "lumenite_*"))
                                        try { File.Delete(f); } catch { }
                                }
                                // Also remove DLSS5Feeder subfolder entirely
                                try { if (Directory.Exists(Path.Combine(shadersDir, "DLSS5Feeder"))) Directory.Delete(Path.Combine(shadersDir, "DLSS5Feeder"), true); } catch { }
                                try { if (Directory.Exists(Path.Combine(shadersDir, "LumeniteFX"))) Directory.Delete(Path.Combine(shadersDir, "LumeniteFX"), true); } catch { }
                                // Remove only the two specific shader files we deployed
                                try { if (File.Exists(Path.Combine(shadersDir, "DLSS5_Feed.fx"))) File.Delete(Path.Combine(shadersDir, "DLSS5_Feed.fx")); } catch { }
                                try { if (File.Exists(Path.Combine(shadersDir, "lumenite_Kernel.fx"))) File.Delete(Path.Combine(shadersDir, "lumenite_Kernel.fx")); } catch { }
                                // Also remove include folder if it only had our headers
                                try { if (Directory.Exists(Path.Combine(shadersDir, "include"))) Directory.Delete(Path.Combine(shadersDir, "include"), true); } catch { }

                                // Update persisted selection — remove our packs, keep others
                                var current = _gameNameService.PerGameShaderSelection.TryGetValue(gameKey, out var sel)
                                    ? sel.ToList() : new List<string>();
                                var remaining = current
                                    .Where(p => !p.Equals("DLSS5Feeder", StringComparison.OrdinalIgnoreCase)
                                             && !p.Equals("LumeniteFX", StringComparison.OrdinalIgnoreCase))
                                    .ToList();
                                if (remaining.Count > 0)
                                    _gameNameService.PerGameShaderSelection[gameKey] = remaining;
                                else
                                {
                                    _gameNameService.PerGameShaderSelection.Remove(gameKey);
                                    _window.DispatcherQueue?.TryEnqueue(() =>
                                    {
                                        _window.ViewModel.SetPerGameShaderMode(gameName, "Global", store);
                                        card.ShaderModeOverride = null;
                                    });
                                }
                                _window.DispatcherQueue?.TryEnqueue(() =>
                                {
                                    _window.ViewModel.SaveSettingsPublic();
                                    // Re-deploy global shaders now that mode is back to Global
                                    _window.ViewModel.DeployShadersForCard(gameName);
                                });
                            }
                            catch { }
                            break;
                        }
                    }

                    _window.ViewModel.SetNrMethodOverride(gameName, null, store);
                });
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[NeuralRendering.Remove] Failed for '{gameName}' — {ex.Message}");
            }
            finally
            {
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    removeBtn.IsEnabled  = true;
                    installBtn.IsEnabled = true;
                    UpdateInstallBtnAppearance();
                    RefreshStatus();
                    // Rebuild full overrides panel so DLSS versions + shader mode reflect new state
                    var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(store) || c.Source == store));
                    if (targetCard != null)
                    {
                        // Re-detect DLSS so Nvidia Profile section shows up-to-date versions
                        var freshDetection = _dlssStreamlineService.Detect(targetCard.InstallPath ?? "");
                        targetCard.ApplyDlssDetection(freshDetection);
                        targetCard.RefreshDlssVersions(_dlssStreamlineService);
                        BuildOverridesPanel(targetCard);
                    }
                });
            }
        };

        Grid.SetColumn(installBtn, 0);
        Grid.SetColumn(removeBtn,  1);
        Grid.SetColumn(sfCogBtn,   2);
        btnRow.Children.Add(installBtn);
        btnRow.Children.Add(removeBtn);
        btnRow.Children.Add(sfCogBtn);
        nrBody.Children.Add(btnRow);

        // ── How to use links ──────────────────────────────────────────────────
        var linksRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        HyperlinkButton MakeLink(string text, string url) => new HyperlinkButton
        {
            Content = text,
            NavigateUri = new Uri(url),
            FontSize = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            Padding = new Thickness(0),
        };
        linksRow.Children.Add(MakeLink("DLSS5 Tool →",  "https://discord.com/channels/1408098019194310818/1543802634991968366"));
        linksRow.Children.Add(MakeLink("DX11 Bridge →", "https://github.com/NIGos/dlss5-bridge"));
        linksRow.Children.Add(MakeLink("ShortFuse →",   "https://discord.com/channels/1408098019194310818/1543975158937821315"));
        linksRow.Children.Add(MakeLink("Feeder →",      "https://github.com/jlrouzies-fr/DLSS5-Feeder"));
        nrBody.Children.Add(linksRow);
    }

    // ── Install helpers ───────────────────────────────────────────────────────

    private async Task InstallDlss5ToolAsync(
        GameCardViewModel card,
        Button statusBtn,
        ComboBox nrVersionCombo,
        Renodx5AddonService rdx5Svc,
        IDlssStreamlineService dlssSvc,
        IAddonPackService addonSvc)
    {
        var installPath = card.InstallPath!;
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Staging DLSS5 Tool...");
        await rdx5Svc.EnsureStagingAsync().ConfigureAwait(false);

        if (!rdx5Svc.IsStagingReady)
            throw new InvalidOperationException("DLSS5 Tool staging not ready");

        // Deploy the addon
        await Task.Run(() =>
        {
            var deployDir = ModInstallService.GetAddonDeployPath(installPath);
            Directory.CreateDirectory(deployDir);
            File.Copy(rdx5Svc.StagedFilePath, Path.Combine(deployDir, "renodx-dlss5.addon64"), overwrite: true);
            CrashReporter.Log($"[NeuralRendering] Deployed renodx-dlss5.addon64 to '{deployDir}'");
            // Note: intentionally NOT calling TrackAddonDeployment — NR-managed files are not
            // tracked by AddonPackService to prevent the stale-cleanup pass from removing them.
        }).ConfigureAwait(false);

        // Upgrade all DLSS DLLs to latest (SR/RR/FG + NR)
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Upgrading DLSS DLLs...");
        await UpgradeDlssDllsAsync(card, dlssSvc, nrVersionCombo).ConfigureAwait(false);
    }

    /// <summary>
    /// Deploys the newest DLSS SR/RR/FG/NR DLLs to the game using detected paths + sentinel pattern.
    /// Used by DLSS5 Tool and DLSS5 Tool + Bridge installs.
    /// </summary>
    private async Task UpgradeDlssDllsAsync(GameCardViewModel card, IDlssStreamlineService dlssSvc, ComboBox? nrVersionCombo = null)
    {
        var installPath = card.InstallPath!;
        var detection   = card.DlssDetection;

        // Fetch newest cached DLLs
        var cachedSr = await dlssSvc.EnsureNewestDlssCachedAsync().ConfigureAwait(false);
        var cachedRr = await dlssSvc.EnsureNewestDlssdCachedAsync().ConfigureAwait(false);
        var cachedFg = await dlssSvc.EnsureNewestDlssgCachedAsync().ConfigureAwait(false);

        // NR DLL — use selected version or newest
        string? nrSelectedVersion = null;
        if (nrVersionCombo != null)
        {
            nrSelectedVersion = await DispatchAsync<string?>(_window.DispatcherQueue!,
                () => nrVersionCombo.SelectedItem as string).ConfigureAwait(false);
        }
        string? cachedNr;
        if (string.IsNullOrEmpty(nrSelectedVersion) || nrSelectedVersion == "Latest")
            cachedNr = await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
        else
        {
            var nrDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "DLSS-NR", nrSelectedVersion);
            cachedNr = Path.Combine(nrDir, "nvngx_dlssnr.dll");
            if (!File.Exists(cachedNr))
                cachedNr = await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
        }

        await Task.Run(() =>
        {
            // SR
            if (cachedSr != null)
            {
                var dest = detection?.DlssPath ?? Path.Combine(installPath, "nvngx_dlss.dll");
                DeployWithSentinel(cachedSr, dest, "NeuralRendering.UpgradeSR");
            }
            // RR
            if (cachedRr != null)
            {
                var dest = detection?.DlssdPath ?? Path.Combine(installPath, "nvngx_dlssd.dll");
                DeployWithSentinel(cachedRr, dest, "NeuralRendering.UpgradeRR");
            }
            // FG
            if (cachedFg != null)
            {
                var dest = detection?.DlssgPath ?? Path.Combine(installPath, "nvngx_dlssg.dll");
                DeployWithSentinel(cachedFg, dest, "NeuralRendering.UpgradeFG");
            }
            // NR
            if (cachedNr != null)
            {
                var dest = detection?.DlssnrPath ?? Path.Combine(installPath, "nvngx_dlssnr.dll");
                DeployNrDllSentinel(installPath, cachedNr); // uses the sentinel helper
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Deploys src → dest with sentinel backup. If dest exists, backs up the original. If dest doesn't exist, writes a 0-byte sentinel so uninstall knows to delete it entirely.</summary>
    private static void DeployWithSentinel(string src, string dest, string logCtx)
    {
        try
        {
            var sentinel = dest + ".original";
            if (File.Exists(dest))
            {
                if (!File.Exists(sentinel))
                    File.Copy(dest, sentinel); // backup game original
            }
            else
            {
                if (!File.Exists(sentinel))
                    File.WriteAllBytes(sentinel, Array.Empty<byte>()); // 0-byte sentinel — RHI placed this
            }
            File.Copy(src, dest, overwrite: true);
            CrashReporter.Log($"[{logCtx}] Deployed to '{dest}'");
        }
        catch (Exception ex) { CrashReporter.Log($"[{logCtx}] Failed '{dest}' — {ex.Message}"); }
    }

    /// <summary>
    /// Restores or deletes DLSS SR/RR/FG/NR DLLs deployed by UpgradeDlssDllsAsync,
    /// using the sentinel pattern: 0-byte sentinel = delete entirely, non-zero = restore original.
    /// </summary>
    private static void RestoreWithSentinel(string dest, string logCtx)
    {
        try
        {
            var sentinel = dest + ".original";
            if (!File.Exists(sentinel)) return; // not placed by RHI — leave untouched
            var info = new FileInfo(sentinel);
            if (info.Length == 0)
            {
                // RHI placed this from scratch — delete both
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                try { File.Delete(sentinel); } catch { }
                CrashReporter.Log($"[{logCtx}] Deleted '{dest}' (RHI-placed)");
            }
            else
            {
                // Restore game original
                File.Copy(sentinel, dest, overwrite: true);
                File.Delete(sentinel);
                CrashReporter.Log($"[{logCtx}] Restored '{dest}' from backup");
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[{logCtx}] Restore failed '{dest}' — {ex.Message}"); }
    }

    /// <summary>Restores all DLSS DLLs (SR/RR/FG/NR) deployed by UpgradeDlssDllsAsync using their sentinels.</summary>
    private static void RestoreDlssDllsWithSentinel(GameCardViewModel card, IDlssStreamlineService dlssSvc)
    {
        var installPath = card.InstallPath!;
        var det = card.DlssDetection;
        RestoreWithSentinel(det?.DlssPath   ?? Path.Combine(installPath, "nvngx_dlss.dll"),   "NeuralRendering.RestoreSR");
        RestoreWithSentinel(det?.DlssdPath  ?? Path.Combine(installPath, "nvngx_dlssd.dll"),  "NeuralRendering.RestoreRR");
        RestoreWithSentinel(det?.DlssgPath  ?? Path.Combine(installPath, "nvngx_dlssg.dll"),  "NeuralRendering.RestoreFG");
        RestoreWithSentinel(det?.DlssnrPath ?? Path.Combine(installPath, "nvngx_dlssnr.dll"), "NeuralRendering.RestoreNR");
    }

    private async Task InstallBridgeAddonAsync(
        GameCardViewModel card,
        Button statusBtn,
        IAddonPackService addonSvc)
    {
        var installPath = card.InstallPath!;
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Downloading DX11 Bridge...");

        // Ensure staged — always re-download Bridge to get latest version
        // (stale cached file may be from the old dead URL dlss5-dx11-bridge.addon64)
        var entry = addonSvc.AvailablePacks.FirstOrDefault(p =>
            p.PackageName.Equals(BridgePackageName, StringComparison.OrdinalIgnoreCase));
        if (entry != null)
        {
            addonSvc.RemoveAddon(BridgePackageName); // clear stale cached version
            await addonSvc.DownloadAddonAsync(entry).ConfigureAwait(false);
        }

        // Deploy — Bridge goes in the game root (next to ReShade / exe), not reshade-addons
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying DX11 Bridge...");
        await Task.Run(() =>
        {
            // Find staged file
            string? stagedPath = FindStagedAddon(BridgePackageName, ".addon64");
            if (stagedPath == null || !File.Exists(stagedPath))
            {
                CrashReporter.Log($"[NeuralRendering] Bridge staging file not found");
                return;
            }
            var dest = Path.Combine(installPath, BridgeDeployFile);
            File.Copy(stagedPath, dest, overwrite: true);
            CrashReporter.Log($"[NeuralRendering] Deployed {BridgeDeployFile} to '{installPath}'");
        }).ConfigureAwait(false);
    }

    private async Task InstallShortFuseAsync(
        GameCardViewModel card,
        Button statusBtn,
        Renodx5AddonService rdx5Svc,
        IDlssStreamlineService dlssSvc)
    {
        var installPath = card.InstallPath!;
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Staging ShortFuse...");
        await rdx5Svc.EnsureSfStagingAsync().ConfigureAwait(false);

        if (!rdx5Svc.IsSfStagingReady)
            throw new InvalidOperationException("ShortFuse staging not ready");

        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Installing DLSS stack...");
        var detection = dlssSvc.Detect(installPath);
        await rdx5Svc.InstallSfAsync(installPath, detection.HasAny ? detection : null).ConfigureAwait(false);

        // Update DLSS detection cache
        var newDetection = dlssSvc.Detect(installPath);
        if (newDetection.HasAny)
        {
            dlssSvc.RecordDlssFound(card.GameName);
            dlssSvc.RecordTrustedPath(card.GameName, newDetection);
        }
        _window.DispatcherQueue?.TryEnqueue(() =>
        {
            card.DlssDetection = newDetection;
            card.ApplyDlssDetection(newDetection);
            card.RefreshDlssVersions(dlssSvc);
        });

        // Apply auto-config (rename ReShade, install UAL, write reshade.ini [INSTALL] keys)
        if (_window.ViewModel.GetSfAutoConfigEnabled(card.GameName, card.Source ?? ""))
        {
            _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Configuring ReShade...");
            await _window.ViewModel.ApplySfAutoConfigAsync(card).ConfigureAwait(false);
        }
    }

    private async Task ApplySfAutoConfigAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        var installPath = card.InstallPath;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";

        // ── Step 1: Rename ReShade DLL to Reshade64.asi ───────────────────────
        const string asiName = "Reshade64.asi";
        var rsRecord = card.RsRecord;
        if (rsRecord != null && !string.IsNullOrEmpty(rsRecord.InstalledAs)
            && !rsRecord.InstalledAs.Equals(asiName, StringComparison.OrdinalIgnoreCase))
        {
            var currentPath = Path.Combine(installPath, rsRecord.InstalledAs);
            var asiPath     = Path.Combine(installPath, asiName);
            try
            {
                if (File.Exists(currentPath))
                {
                    if (File.Exists(asiPath)) File.Delete(asiPath);
                    File.Move(currentPath, asiPath);
                    rsRecord.InstalledAs = asiName;
                    card.RsRecord.InstalledAs = asiName;
                    _auxInstallService.SaveAuxRecord(rsRecord);
                    CrashReporter.Log($"[SfAutoConfig] Renamed ReShade to '{asiName}' for '{gameName}'");
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[SfAutoConfig] ReShade rename failed — {ex.Message}"); }
        }

        // ── Step 2: Auto-install ASI Loader (winmm → version → dinput8) ───────
        var ualSvc = App.Services.GetRequiredService<UltimateAsiLoaderService>();
        bool ualAlreadyInstalled = !string.IsNullOrEmpty(
            _window.ViewModel.GetUalInstalledAs(gameName, store));

        if (!ualAlreadyInstalled)
        {
            // Pick the first available name from the preference order
            string[] preferenceOrder = { "winmm.dll", "version.dll", "dinput8.dll" };
            string? chosenName = null;
            foreach (var candidate in preferenceOrder)
            {
                var candidatePath = Path.Combine(installPath, candidate);
                // Skip if already occupied by a non-RHI file
                bool takenByOther = File.Exists(candidatePath)
                    && !string.Equals(rsRecord?.InstalledAs, candidate, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(card.OsInstalledFile, candidate, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(card.DcInstalledFile, candidate, StringComparison.OrdinalIgnoreCase);
                if (!takenByOther) { chosenName = candidate; break; }
            }

            if (chosenName != null)
            {
                try
                {
                    var (success, hookedOriginal) = await ualSvc.InstallAsync(card, chosenName).ConfigureAwait(false);
                    if (success)
                    {
                        _window.ViewModel.SetUalInstalledAs(gameName, chosenName, store);
                        CrashReporter.Log($"[SfAutoConfig] Installed UAL as '{chosenName}' for '{gameName}'" +
                            (hookedOriginal != null ? $" (chained '{hookedOriginal}')" : ""));
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[SfAutoConfig] UAL install failed — {ex.Message}"); }
            }
            else
            {
                CrashReporter.Log($"[SfAutoConfig] No suitable UAL name available for '{gameName}' — all candidates taken");
            }
        }
        else
        {
            CrashReporter.Log($"[SfAutoConfig] UAL already installed for '{gameName}' — skipping");
        }

        // ── Step 3: Write [INSTALL] HookStreamline=1 + HookDirectX=1 ─────────
        if (_window.ViewModel.GetKeepRsIniUpdated(gameName, store))
        {
            var iniPath = Path.Combine(installPath, "reshade.ini");
            if (File.Exists(iniPath))
            {
                try
                {
                    var ini = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                    if (!ini.ContainsKey("INSTALL"))
                        ini["INSTALL"] = new AuxInstallService.OrderedDict();
                    ini["INSTALL"]["HookStreamline"] = "1";
                    ini["INSTALL"]["HookDirectX"]    = "1";
                    AuxInstallService.WriteIni(iniPath, ini);
                    CrashReporter.Log($"[SfAutoConfig] Wrote [INSTALL] keys to reshade.ini for '{gameName}'");
                }
                catch (Exception ex) { CrashReporter.Log($"[SfAutoConfig] reshade.ini write failed — {ex.Message}"); }
            }
        }

        await Task.CompletedTask;
    }

    private async Task InstallFeederAddonAsync(
        GameCardViewModel card,
        Button statusBtn,
        IAddonPackService addonSvc)
    {
        var installPath = card.InstallPath!;
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Downloading Feeder...");

        var entry = addonSvc.AvailablePacks.FirstOrDefault(p =>
            p.PackageName.Equals(FeederPackageName, StringComparison.OrdinalIgnoreCase));
        if (entry != null && !addonSvc.IsDownloaded(FeederPackageName))
            await addonSvc.DownloadAddonAsync(entry).ConfigureAwait(false);

        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying Feeder...");
        await Task.Run(() =>
        {
            var bitnessExt = card.Is32Bit ? ".addon32" : ".addon64";
            string? stagedPath = FindStagedAddon(FeederPackageName, bitnessExt);
            if (stagedPath == null || !File.Exists(stagedPath))
            {
                CrashReporter.Log($"[NeuralRendering] Feeder staging file not found");
                return;
            }
            var destName = card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64;
            var dest = Path.Combine(installPath, destName);
            File.Copy(stagedPath, dest, overwrite: true);
            CrashReporter.Log($"[NeuralRendering] Deployed {destName} to '{installPath}'");
        }).ConfigureAwait(false);

        // Also deploy NR dll alongside the feeder
        var rdx5Svc = App.Services.GetRequiredService<Renodx5AddonService>();
        await rdx5Svc.DeployNrDllIfAbsentAsync(installPath).ConfigureAwait(false);

        // Deploy DLSS5 Tool as neural consumer (Feeder needs renodx-dlss5.addon64 alongside it)
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying DLSS5 Tool...");
        await rdx5Svc.EnsureStagingAsync().ConfigureAwait(false);
        if (rdx5Svc.IsStagingReady)
        {
            await Task.Run(() =>
            {
                var deployDir = ModInstallService.GetAddonDeployPath(installPath);
                Directory.CreateDirectory(deployDir);
                File.Copy(rdx5Svc.StagedFilePath, Path.Combine(deployDir, "renodx-dlss5.addon64"), overwrite: true);
                // Note: intentionally NOT calling TrackAddonDeployment — NR-managed files are not
                // tracked by AddonPackService to prevent the stale-cleanup pass from removing them.
                CrashReporter.Log($"[NeuralRendering] Deployed renodx-dlss5.addon64 (Feeder consumer) to '{deployDir}'");
            }).ConfigureAwait(false);
        }

        // Deploy newest nvngx_dlss.dll — required by Feeder beside the game exe (install root, not detected plugin path)
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying DLSS SR...");
        var cachedDlss = await _dlssStreamlineService.EnsureNewestDlssCachedAsync().ConfigureAwait(false);
        if (cachedDlss != null && new FileInfo(cachedDlss).Length > 0)
        {
            await Task.Run(() =>
            {
                // Always deploy to install root — Feeder looks for nvngx_dlss.dll beside itself, not in deep plugin folders
                var dlssDest     = Path.Combine(installPath, "nvngx_dlss.dll");
                var dlssSentinel = dlssDest + ".original";
                if (File.Exists(dlssDest) && !File.Exists(dlssSentinel))
                    File.Copy(dlssDest, dlssSentinel); // backup game original if present
                else if (!File.Exists(dlssDest))
                    File.WriteAllBytes(dlssSentinel, Array.Empty<byte>()); // sentinel — game had none
                File.Copy(cachedDlss, dlssDest, overwrite: true);
                CrashReporter.Log($"[NeuralRendering] Deployed newest nvngx_dlss.dll to '{installPath}' (Feeder root)");
            }).ConfigureAwait(false);
        }

        // Deploy DLSS5_Feed.fx + lumenite_Kernel.fx
        // Only DLSS5Feeder is tracked in PerGameShaderSelection — lumenite_Kernel.fx is deployed
        // directly (not via pack system) to avoid syncing the full LumeniteFX pack on refresh.
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying shaders...");
        try
        {
            await _shaderPackService.EnsurePacksAsync(new[] { "DLSS5Feeder", "LumeniteFX" }).ConfigureAwait(false);

            // Build exclusion sets — deploy only lumenite_Kernel.fx from LumeniteFX, only DLSS5_Feed.fx from DLSS5Feeder
            var lumeniteExclude = _shaderPackService.GetPackShaderFiles(new[] { "LumeniteFX" })
                .Where(f => !f.Equals("lumenite_Kernel.fx", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var feederExclude = _shaderPackService.GetPackShaderFiles(new[] { "DLSS5Feeder" })
                .Where(f => !f.Equals("DLSS5_Feed.fx", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var exclusions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["LumeniteFX"]  = lumeniteExclude,
                ["DLSS5Feeder"] = feederExclude,
            };

            await Task.Run(() =>
            {
                _shaderPackService.DeployToGameFolder(installPath, new[] { "LumeniteFX", "DLSS5Feeder" }, exclusions);
                CrashReporter.Log($"[NeuralRendering] Deployed lumenite_Kernel.fx + DLSS5_Feed.fx to '{installPath}'");

                // Write DLSS5_MV_PROVIDER=3 to reshade.ini [GENERAL] PreprocessorDefinitions
                var iniPath = Path.Combine(installPath, "reshade.ini");
                if (File.Exists(iniPath))
                {
                    try
                    {
                        var ini = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                        if (!ini.TryGetValue("GENERAL", out var general))
                        {
                            general = new AuxInstallService.OrderedDict();
                            ini["GENERAL"] = general;
                        }
                        if (general.TryGetValue("PreprocessorDefinitions", out var existing) && !string.IsNullOrEmpty(existing))
                        {
                            if (!existing.Contains("DLSS5_MV_PROVIDER", StringComparison.OrdinalIgnoreCase))
                                general["PreprocessorDefinitions"] = existing.TrimEnd(',') + ",DLSS5_MV_PROVIDER=3";
                        }
                        else
                        {
                            general["PreprocessorDefinitions"] = "DLSS5_MV_PROVIDER=3";
                        }
                        AuxInstallService.WriteIni(iniPath, ini);
                        CrashReporter.Log($"[NeuralRendering] Set DLSS5_MV_PROVIDER=3 in reshade.ini for '{installPath}'");

                        // Write ReShadePreset.ini with both techniques enabled, full TechniqueSorting order
                        const string lumeniteTech = "Lumenite_Kernel@lumenite_Kernel.fx";
                        const string feederTech   = "DLSS5_Feed@DLSS5_Feed.fx";
                        var presetPath = Path.Combine(installPath, "ReShadePreset.ini");
                        if (!File.Exists(presetPath))
                        {
                            var presetContent =
                                "Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "TechniqueSorting=DLSS5_Feed_Debug@DLSS5_Feed.fx,lilium__rcas_hdr@lilium__rcas_hdr.fx,lilium__make_overlay_bg_redraw@lilium__hdr_and_sdr_analysis.fx,lilium__hdr_and_sdr_analysis@lilium__hdr_and_sdr_analysis.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "\r\n" +
                                "[DLSS5_Feed.fx]\r\n" +
                                "DEBUG_VIEW=0\r\n" +
                                "DEPTH_TOLERANCE=0.100000\r\n" +
                                "GEOM_AGREE_PX=-1.500000\r\n" +
                                "GEOM_DYNAMIC_MARGIN=0.250000\r\n" +
                                "GEOM_ENABLE=3\r\n" +
                                "GEOM_MASK_REJECTED=0.350000\r\n" +
                                "GEOM_OUTLIER_PX=4.000000\r\n" +
                                "GEOM_PARALLAX=1.020000\r\n" +
                                "LUMA_TOLERANCE=0.280000\r\n" +
                                "MASK_STRENGTH=1.000000\r\n" +
                                "MV_CONSISTENCY=1.400000\r\n" +
                                "MV_LOWRES_FILTER=0\r\n" +
                                "MV_PROVIDER_INFO=0\r\n" +
                                "MV_SCALE=1.000000\r\n" +
                                "MV_SIGN=1.000000,1.000000\r\n" +
                                "MV_VALIDATE=1\r\n" +
                                "STATIC_BIAS=0.150000\r\n" +
                                "STATIC_MIN_CONTRAST=0.012000\r\n" +
                                "VALIDATE_DEPTH=1\r\n" +
                                "VALIDATE_LUMA=0\r\n" +
                                "VALIDATE_MV=1\r\n" +
                                "VALIDATE_STATIC=1\r\n" +
                                "\r\n" +
                                "[GENERAL]\r\n" +
                                "Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "TechniqueSorting=DLSS5_Feed_Debug@DLSS5_Feed.fx,lilium__rcas_hdr@lilium__rcas_hdr.fx,lilium__make_overlay_bg_redraw@lilium__hdr_and_sdr_analysis.fx,lilium__hdr_and_sdr_analysis@lilium__hdr_and_sdr_analysis.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n";
                            File.WriteAllText(presetPath, presetContent);
                            // Point reshade.ini at this preset
                            var rIni = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                            if (!rIni.TryGetValue("GENERAL", out var rg))
                            { rg = new AuxInstallService.OrderedDict(); rIni["GENERAL"] = rg; }
                            rg["PresetPath"] = ".\\ReShadePreset.ini";
                            AuxInstallService.WriteIni(iniPath, rIni);
                            CrashReporter.Log($"[NeuralRendering] Created ReShadePreset.ini for '{installPath}'");
                        }
                        else
                        {
                            // Overwrite existing preset — always use the canonical layout
                            // (Techniques/TechniqueSorting as top-level lines then [GENERAL] section)
                            var presetContent =
                                "Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "TechniqueSorting=DLSS5_Feed_Debug@DLSS5_Feed.fx,lilium__rcas_hdr@lilium__rcas_hdr.fx,lilium__make_overlay_bg_redraw@lilium__hdr_and_sdr_analysis.fx,lilium__hdr_and_sdr_analysis@lilium__hdr_and_sdr_analysis.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "\r\n" +
                                "[DLSS5_Feed.fx]\r\n" +
                                "DEBUG_VIEW=0\r\n" +
                                "DEPTH_TOLERANCE=0.100000\r\n" +
                                "GEOM_AGREE_PX=-1.500000\r\n" +
                                "GEOM_DYNAMIC_MARGIN=0.250000\r\n" +
                                "GEOM_ENABLE=3\r\n" +
                                "GEOM_MASK_REJECTED=0.350000\r\n" +
                                "GEOM_OUTLIER_PX=4.000000\r\n" +
                                "GEOM_PARALLAX=1.020000\r\n" +
                                "LUMA_TOLERANCE=0.280000\r\n" +
                                "MASK_STRENGTH=1.000000\r\n" +
                                "MV_CONSISTENCY=1.400000\r\n" +
                                "MV_LOWRES_FILTER=0\r\n" +
                                "MV_PROVIDER_INFO=0\r\n" +
                                "MV_SCALE=1.000000\r\n" +
                                "MV_SIGN=1.000000,1.000000\r\n" +
                                "MV_VALIDATE=1\r\n" +
                                "STATIC_BIAS=0.150000\r\n" +
                                "STATIC_MIN_CONTRAST=0.012000\r\n" +
                                "VALIDATE_DEPTH=1\r\n" +
                                "VALIDATE_LUMA=0\r\n" +
                                "VALIDATE_MV=1\r\n" +
                                "VALIDATE_STATIC=1\r\n" +
                                "\r\n" +
                                "[GENERAL]\r\n" +
                                "Techniques=Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n" +
                                "TechniqueSorting=DLSS5_Feed_Debug@DLSS5_Feed.fx,lilium__rcas_hdr@lilium__rcas_hdr.fx,lilium__make_overlay_bg_redraw@lilium__hdr_and_sdr_analysis.fx,lilium__hdr_and_sdr_analysis@lilium__hdr_and_sdr_analysis.fx,Lumenite_Kernel@lumenite_Kernel.fx,DLSS5_Feed@DLSS5_Feed.fx\r\n";
                            File.WriteAllText(presetPath, presetContent);
                            CrashReporter.Log($"[NeuralRendering] Overwrote ReShadePreset.ini for '{installPath}'");
                        }
                    }
                    catch (Exception iniEx) { CrashReporter.Log($"[NeuralRendering] reshade.ini/preset update failed — {iniEx.Message}"); }
                }
            }).ConfigureAwait(false);

            // Add to PerGameShaderSelection so SyncGameFolder keeps them deployed

            // Add DLSS5Feeder and LumeniteFX to PerGameShaderSelection, with global pack exclusions
            // so SyncGameFolder only deploys lumenite_Kernel.fx and DLSS5_Feed.fx on refresh.
            // SetExcludedFiles is global per-pack — safe here because LumeniteFX and DLSS5Feeder
            // are only used by Feeder games and have no other use in the standard shader picker.
            _window.DispatcherQueue?.TryEnqueue(() =>
            {
                // Persist pack-level exclusions so SyncGameFolder uses them on every refresh
                var lumeniteAllFiles = _shaderPackService.GetPackShaderFiles(new[] { "LumeniteFX" })
                    .Where(f => !f.Equals("lumenite_Kernel.fx", StringComparison.OrdinalIgnoreCase));
                _shaderPackService.SetExcludedFiles("LumeniteFX", lumeniteAllFiles);

                var feederAllFiles = _shaderPackService.GetPackShaderFiles(new[] { "DLSS5Feeder" })
                    .Where(f => !f.Equals("DLSS5_Feed.fx", StringComparison.OrdinalIgnoreCase));
                _shaderPackService.SetExcludedFiles("DLSS5Feeder", feederAllFiles);

                var gameKey = Models.GameKey.From(card.GameName, card.Source ?? "").ToKey();
                var current = _gameNameService.PerGameShaderSelection.TryGetValue(gameKey, out var sel)
                    ? sel.ToList() : new List<string>();
                if (!current.Contains("DLSS5Feeder", StringComparer.OrdinalIgnoreCase))
                    current.Add("DLSS5Feeder");
                if (!current.Contains("LumeniteFX", StringComparer.OrdinalIgnoreCase))
                    current.Add("LumeniteFX");
                _gameNameService.PerGameShaderSelection[gameKey] = current;
                _window.ViewModel.SetPerGameShaderMode(card.GameName, "Select", card.Source ?? "");
                card.ShaderModeOverride = "Select";
                _window.ViewModel.SaveSettingsPublic();
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NeuralRendering] Shader deploy failed — {ex.Message}");
        }

        // Rebuild panel one final time now that shaders are deployed — status will show ✓ Feed.fx / ✓ LumeniteFX
        _window.DispatcherQueue?.TryEnqueue(() =>
        {
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(card.GameName, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(card.Source) || c.Source == card.Source));
            if (targetCard != null)
                BuildOverridesPanel(targetCard);
        });
    }

    // ── Utility helpers ───────────────────────────────────────────────────────

    private static void DeployNrDllSentinel(string installPath, string cachedNrPath)
    {
        var dest     = Path.Combine(installPath, "nvngx_dlssnr.dll");
        var sentinel = dest + ".original";
        if (File.Exists(sentinel)) return;          // already placed by RHI
        if (File.Exists(dest))     return;          // game-original — don't touch
        File.Copy(cachedNrPath, dest, overwrite: false);
        File.WriteAllBytes(sentinel, Array.Empty<byte>());
        CrashReporter.Log($"[NeuralRendering] Deployed nvngx_dlssnr.dll to '{installPath}' (sentinel written)");
    }

    private static string? FindStagedAddon(string packageName, string extension)
    {
        var stagingDir = AddonPackService.GetStagingDir();
        // Try sanitized package name first (AddonPackService.SanitizeFileName pattern)
        var safeName = new string(packageName.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        var candidate = Path.Combine(stagingDir, safeName + extension);
        if (File.Exists(candidate)) return candidate;
        // Also check versions.json OriginalName entries via directory scan
        foreach (var f in Directory.EnumerateFiles(stagingDir, $"*{extension}"))
        {
            var fn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            if (packageName.ToLowerInvariant().Contains(fn) || fn.Contains("bridge") || fn.Contains("feed"))
                return f;
        }
        return null;
    }

    private static void RemoveAddonFile(string installPath, string fileName, string logCtx)
    {
        var path = Path.Combine(installPath, fileName);
        try
        {
            if (File.Exists(path)) { File.Delete(path); CrashReporter.Log($"[{logCtx}] Deleted '{path}'"); }
        }
        catch (Exception ex) { CrashReporter.Log($"[{logCtx}] Delete failed '{path}' — {ex.Message}"); }
    }

    private static Task<T> DispatchAsync<T>(Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        dispatcher.TryEnqueue(() =>
        {
            try   { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }
}
