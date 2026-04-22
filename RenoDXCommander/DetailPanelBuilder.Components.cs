// DetailPanelBuilder.Components.cs — Component row updates, property-changed handling, and message color logic.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private readonly AddonInfoResolver _addonInfoResolver = new();

    /// <summary>
    /// Returns true when the Info button for this addon has real per-game content
    /// (manifest or wiki), meaning the arrow indicator should be shown on the install button.
    /// </summary>
    private bool HasRealInfoContent(GameCardViewModel card, AddonType addonType)
    {
        var manifest = _window.ViewModel.Manifest;
        var osWikiData = _window.ViewModel.OptiScalerWikiServiceInstance.CachedData;
        var hdrDatabase = _window.ViewModel.HdrDatabaseServiceInstance.CachedData;
        var sourceType = _addonInfoResolver.GetSourceType(card, addonType, manifest, osWikiData, hdrDatabase);
        return sourceType is InfoSourceType.Manifest or InfoSourceType.Wiki;
    }

    /// <summary>
    /// Prepends a left-aligned arrow indicator to the install button when the
    /// Info button has real per-game content, drawing attention to it.
    /// The arrow sits on the left edge while the label text stays centered.
    /// Skipped when the button shows an update (purple state).
    /// </summary>
    private static object WithInfoArrow(string label, bool hasInfo, bool isUpdate, Button? btn = null)
    {
        if (!hasInfo || isUpdate)
        {
            // Reset to default centered alignment when no arrow
            if (btn != null) btn.HorizontalContentAlignment = HorizontalAlignment.Center;
            return label;
        }

        // Stretch content so the Grid fills the full button width
        if (btn != null) btn.HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var grid = new Grid();

        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        grid.Children.Add(new TextBlock
        {
            Text = "◄",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return grid;
    }

    /// <summary>
    /// Applies Info button styling (highlighted vs muted) based on the resolved source type.
    /// Also sets the Tag, tooltip, and click handler.
    /// </summary>
    private void ApplyInfoButtonStyle(Button infoBtn, GameCardViewModel card, AddonType addonType)
    {
        infoBtn.Tag = card;
        var manifest = _window.ViewModel.Manifest;
        var osWikiData = _window.ViewModel.OptiScalerWikiServiceInstance.CachedData;
        var hdrDatabase = _window.ViewModel.HdrDatabaseServiceInstance.CachedData;
        var sourceType = _addonInfoResolver.GetSourceType(card, addonType, manifest, osWikiData, hdrDatabase);
        var tooltip = _addonInfoResolver.GetTooltip(card, addonType, manifest, osWikiData, hdrDatabase);
        ToolTipService.SetToolTip(infoBtn, tooltip);

        if (sourceType is InfoSourceType.None)
        {
            // Greyed-out state: no content available at all
            infoBtn.Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
            infoBtn.Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
            infoBtn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush);
            infoBtn.Opacity = 0.3;
            infoBtn.IsHitTestVisible = false;
        }
        else if (sourceType is InfoSourceType.Manifest or InfoSourceType.Wiki)
        {
            // Highlighted style for manifest/wiki content
            infoBtn.Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
            infoBtn.Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
            infoBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
            infoBtn.Opacity = 1.0;
            infoBtn.IsHitTestVisible = true;
        }
        else
        {
            // Default muted style for fallback content
            infoBtn.Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
            infoBtn.Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
            infoBtn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush);
            infoBtn.Opacity = 1.0;
            infoBtn.IsHitTestVisible = true;
        }
        infoBtn.BorderThickness = new Thickness(1);
    }
    public void UpdateDetailComponentRows(GameCardViewModel card)
    {
        bool isLumaMode = card.LumaFeatureEnabled && card.IsLumaMode;

        // RE Framework row — visible only for RE Engine games when not in Luma mode
        _window.DetailRefRow.Visibility = card.RefRowVisibility;
        if (card.RefRowVisibility == Visibility.Visible)
        {
            _window.DetailRefStatus.Text = card.RefStatusText;
            _window.DetailRefStatus.Foreground = UIFactory.GetBrush(card.RefStatusColor);
            _window.DetailRefStatus.TextDecorations = card.IsRefInstalled
                ? Windows.UI.Text.TextDecorations.Underline
                : Windows.UI.Text.TextDecorations.None;
            _window.DetailRefInstallBtn.Tag = card;
            _window.DetailRefInstallBtn.Content = WithInfoArrow(card.RefActionLabel, HasRealInfoContent(card, AddonType.REFramework), card.RefStatus == GameStatus.UpdateAvailable, _window.DetailRefInstallBtn);
            _window.DetailRefInstallBtn.IsEnabled = card.IsRefNotInstalling;
            _window.DetailRefInstallBtn.Background = UIFactory.GetBrush(card.RefBtnBackground);
            _window.DetailRefInstallBtn.Foreground = UIFactory.GetBrush(card.RefBtnForeground);
            _window.DetailRefInstallBtn.BorderBrush = UIFactory.GetBrush(card.RefBtnBorderBrush);
            _window.DetailRefInstallBtn.BorderThickness = new Thickness(1);
            _window.DetailRefDeleteBtn.Tag = card;
            var refShow = card.RefDeleteVisibility == Visibility.Visible;
            _window.DetailRefDeleteBtn.Opacity = refShow ? 1 : 0;
            _window.DetailRefDeleteBtn.IsHitTestVisible = refShow;
            ApplyInfoButtonStyle(_window.DetailRefInfoBtn, card, AddonType.REFramework);
        }

        // ReShade row
        _window.DetailRsRow.Visibility = isLumaMode ? Visibility.Collapsed : Visibility.Visible;
        if (!isLumaMode)
        {
            if (card.RequiresVulkanInstall)
            {
                // Vulkan layer install path — RS is "installed" when reshade.ini exists
                // in the game folder (the Vulkan layer needs it to function for this game).
                bool rsIniExists = File.Exists(Path.Combine(card.InstallPath, "reshade.ini"));
                if (rsIniExists)
                {
                    var vulkanVersion = AuxInstallService.ReadInstalledVersion(
                        VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                    _window.DetailRsStatus.Text = (vulkanVersion ?? "Installed") + "\n(Vulkan)";
                    _window.DetailRsStatus.Foreground = UIFactory.GetBrush("#5ECB7D");
                    _window.DetailRsStatus.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                }
                else
                {
                    _window.DetailRsStatus.Text = "Ready";
                    _window.DetailRsStatus.Foreground = UIFactory.GetBrush("#A0AABB");
                    _window.DetailRsStatus.TextDecorations = Windows.UI.Text.TextDecorations.None;
                }
                _window.DetailRsInstallBtn.Tag = card;
                _window.DetailRsInstallBtn.Content = WithInfoArrow(card.RsActionLabel, HasRealInfoContent(card, AddonType.ReShade), card.RsStatus == GameStatus.UpdateAvailable, _window.DetailRsInstallBtn);
                _window.DetailRsInstallBtn.IsEnabled = card.IsRsNotInstalling;
                _window.DetailRsInstallBtn.Background = UIFactory.GetBrush(card.RsBtnBackground);
                _window.DetailRsInstallBtn.Foreground = UIFactory.GetBrush(card.RsBtnForeground);
                _window.DetailRsInstallBtn.BorderBrush = UIFactory.GetBrush(card.RsBtnBorderBrush);
                _window.DetailRsInstallBtn.BorderThickness = new Thickness(1);
                _window.DetailRsIniBtn.Tag = card;
                _window.DetailRsIniBtn.IsEnabled = card.RsIniExists;
                _window.DetailRsIniBtn.Opacity = card.RsIniExists ? 1 : 0.3;
                _window.DetailRsDeleteBtn.Tag = card;
                _window.DetailRsDeleteBtn.Opacity = rsIniExists ? 1 : 0;
                _window.DetailRsDeleteBtn.IsHitTestVisible = rsIniExists;
            }
            else
            {
                // Standard DX ReShade install path
                _window.DetailRsStatus.Text = card.RsStatusText;
                _window.DetailRsStatus.Foreground = UIFactory.GetBrush(card.RsStatusColor);
                _window.DetailRsStatus.TextDecorations = card.IsRsInstalled
                    ? Windows.UI.Text.TextDecorations.Underline
                    : Windows.UI.Text.TextDecorations.None;
                _window.DetailRsInstallBtn.Tag = card;
                _window.DetailRsInstallBtn.Content = WithInfoArrow(card.RsActionLabel, HasRealInfoContent(card, AddonType.ReShade), card.RsStatus == GameStatus.UpdateAvailable, _window.DetailRsInstallBtn);
                _window.DetailRsInstallBtn.IsEnabled = card.IsRsNotInstalling;
                _window.DetailRsInstallBtn.Background = UIFactory.GetBrush(card.RsBtnBackground);
                _window.DetailRsInstallBtn.Foreground = UIFactory.GetBrush(card.RsBtnForeground);
                _window.DetailRsInstallBtn.BorderBrush = UIFactory.GetBrush(card.RsBtnBorderBrush);
                _window.DetailRsInstallBtn.BorderThickness = new Thickness(1);
                _window.DetailRsIniBtn.Tag = card;
                _window.DetailRsIniBtn.IsEnabled = card.RsIniExists;
                _window.DetailRsIniBtn.Opacity = card.RsIniExists ? 1 : 0.3;
                _window.DetailRsDeleteBtn.Tag = card;
                var rsShow = card.RsDeleteVisibility == Visibility.Visible;
                _window.DetailRsDeleteBtn.Opacity = rsShow ? 1 : 0;
                _window.DetailRsDeleteBtn.IsHitTestVisible = rsShow;
            }
            ApplyInfoButtonStyle(_window.DetailRsInfoBtn, card, AddonType.ReShade);

            // Grey out ReShade row when RE Engine game needs REFramework first
            bool rsNeedsRef = card.IsREEngineGame && !card.IsRefInstalled;
            if (rsNeedsRef)
            {
                _window.DetailRsInstallBtn.IsEnabled = false;
                _window.DetailRsInstallBtn.Opacity = 0.35;
                _window.DetailRsInstallBtn.IsHitTestVisible = false;
            }
            else
            {
                _window.DetailRsInstallBtn.Opacity = 1.0;
                _window.DetailRsInstallBtn.IsHitTestVisible = true;
            }
        }

        // ReLimiter row — hidden when in Luma mode
        _window.DetailUlRow.Visibility = card.UlRowVisibility;
        bool ulGreyed = card.UseNormalReShade || card.IsDcInstalled || card.Is32Bit || !card.IsRsInstalled;
        _window.DetailUlRow.Opacity = 1.0;
        _window.DetailUlRow.IsHitTestVisible = true;
        if (card.UlRowVisibility == Visibility.Visible)
        {
            // Strikethrough the label and status when the other limiter (DC) is installed, game is 32-bit, or normal ReShade is active
            var ulStrike = (card.IsDcInstalled || card.Is32Bit || card.UseNormalReShade)
                ? Windows.UI.Text.TextDecorations.Strikethrough
                : Windows.UI.Text.TextDecorations.None;
            _window.DetailUlLabel.TextDecorations = ulStrike;
            _window.DetailUlLabel.Opacity = ulGreyed ? 0.35 : 1.0;

            _window.DetailUlStatus.Text = card.UlStatusText;
            _window.DetailUlStatus.Foreground = UIFactory.GetBrush(card.UlStatusColor);
            _window.DetailUlStatus.TextDecorations = card.IsUlInstalled
                ? Windows.UI.Text.TextDecorations.Underline
                : (card.IsDcInstalled || card.Is32Bit || card.UseNormalReShade) ? Windows.UI.Text.TextDecorations.Strikethrough
                : Windows.UI.Text.TextDecorations.None;
            _window.DetailUlStatus.Opacity = ulGreyed ? 0.35 : 1.0;
            _window.DetailUlInstallBtn.Tag = card;
            var ulLabel = WithInfoArrow(card.UlActionLabel, HasRealInfoContent(card, AddonType.ReLimiter), card.UlStatus == GameStatus.UpdateAvailable, _window.DetailUlInstallBtn);
            _window.DetailUlInstallBtn.Content = (card.IsDcInstalled || card.Is32Bit || card.UseNormalReShade)
                ? (object)new TextBlock { Text = card.UlActionLabel, TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough }
                : ulLabel;
            _window.DetailUlInstallBtn.IsEnabled = card.UlInstallEnabled;
            _window.DetailUlInstallBtn.Background = UIFactory.GetBrush(card.UlBtnBackground);
            _window.DetailUlInstallBtn.Foreground = UIFactory.GetBrush(card.UlBtnForeground);
            _window.DetailUlInstallBtn.BorderBrush = UIFactory.GetBrush(card.UlBtnBorderBrush);
            _window.DetailUlInstallBtn.BorderThickness = new Thickness(1);
            _window.DetailUlInstallBtn.Opacity = ulGreyed ? 0.35 : 1.0;
            _window.DetailUlInstallBtn.IsHitTestVisible = !card.UseNormalReShade;
            _window.DetailUlIniBtn.Tag = card;
            _window.DetailUlIniBtn.IsEnabled = card.UlIniExists;
            _window.DetailUlIniBtn.Opacity = ulGreyed ? 0.35 : (card.UlIniExists ? 1 : 0.3);
            _window.DetailUlIniBtn.IsHitTestVisible = !card.UseNormalReShade;
            _window.DetailUlDeleteBtn.Tag = card;
            var ulShow = card.UlDeleteVisibility == Visibility.Visible;
            _window.DetailUlDeleteBtn.Opacity = ulGreyed ? 0 : (ulShow ? 1 : 0);
            _window.DetailUlDeleteBtn.IsHitTestVisible = ulShow && !ulGreyed;
            ApplyInfoButtonStyle(_window.DetailUlInfoBtn, card, AddonType.ReLimiter);
        }

        // Display Commander row — always visible (available in Luma mode)
        _window.DetailDcRow.Visibility = card.DcRowVisibility;
        bool dcGreyed = card.UseNormalReShade || card.IsUlInstalled || !card.IsRsInstalled;
        _window.DetailDcRow.Opacity = 1.0;
        _window.DetailDcRow.IsHitTestVisible = true;
        if (card.DcRowVisibility == Visibility.Visible)
        {
            // Strikethrough the label and status when the other limiter (UL) is installed or normal ReShade is active
            var dcStrike = (card.IsUlInstalled || card.UseNormalReShade)
                ? Windows.UI.Text.TextDecorations.Strikethrough
                : Windows.UI.Text.TextDecorations.None;
            _window.DetailDcLabel.TextDecorations = dcStrike;
            _window.DetailDcLabel.Opacity = dcGreyed ? 0.35 : 1.0;

            _window.DetailDcStatus.Text = card.DcStatusText;
            _window.DetailDcStatus.Foreground = UIFactory.GetBrush(card.DcStatusColor);
            _window.DetailDcStatus.TextDecorations = card.IsDcInstalled
                ? Windows.UI.Text.TextDecorations.Underline
                : (card.IsUlInstalled || card.UseNormalReShade) ? Windows.UI.Text.TextDecorations.Strikethrough
                : Windows.UI.Text.TextDecorations.None;
            _window.DetailDcStatus.Opacity = dcGreyed ? 0.35 : 1.0;
            _window.DetailDcInstallBtn.Tag = card;
            var dcLabel = WithInfoArrow(card.DcActionLabel, HasRealInfoContent(card, AddonType.DisplayCommander), card.DcStatus == GameStatus.UpdateAvailable, _window.DetailDcInstallBtn);
            _window.DetailDcInstallBtn.Content = (card.IsUlInstalled || card.UseNormalReShade)
                ? (object)new TextBlock { Text = card.DcActionLabel, TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough }
                : dcLabel;
            _window.DetailDcInstallBtn.IsEnabled = card.DcInstallEnabled;
            _window.DetailDcInstallBtn.Background = UIFactory.GetBrush(card.DcBtnBackground);
            _window.DetailDcInstallBtn.Foreground = UIFactory.GetBrush(card.DcBtnForeground);
            _window.DetailDcInstallBtn.BorderBrush = UIFactory.GetBrush(card.DcBtnBorderBrush);
            _window.DetailDcInstallBtn.BorderThickness = new Thickness(1);
            _window.DetailDcInstallBtn.Opacity = dcGreyed ? 0.35 : 1.0;
            _window.DetailDcInstallBtn.IsHitTestVisible = !card.UseNormalReShade;
            _window.DetailDcIniBtn.Tag = card;
            _window.DetailDcIniBtn.IsEnabled = card.DcIniExists;
            _window.DetailDcIniBtn.Opacity = dcGreyed ? 0.35 : (card.DcIniExists ? 1 : 0.3);
            _window.DetailDcIniBtn.IsHitTestVisible = !card.UseNormalReShade;
            _window.DetailDcDeleteBtn.Tag = card;
            var dcShow = card.DcDeleteVisibility == Visibility.Visible;
            _window.DetailDcDeleteBtn.Opacity = dcGreyed ? 0 : (dcShow ? 1 : 0);
            _window.DetailDcDeleteBtn.IsHitTestVisible = dcShow && !dcGreyed;
            ApplyInfoButtonStyle(_window.DetailDcInfoBtn, card, AddonType.DisplayCommander);
        }

        // OptiScaler row — always visible, greyed out for 32-bit games
        _window.DetailOsRow.Visibility = card.OsRowVisibility;
        _window.DetailOptionalSeparator.Visibility = card.OsRowVisibility;
        bool osGreyed = card.Is32Bit;
        _window.DetailOsRow.Opacity = 1.0;
        _window.DetailOsRow.IsHitTestVisible = true;
        if (osGreyed)
        {
            _window.DetailOsLabel.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            _window.DetailOsLabel.Opacity = 0.35;
            _window.DetailOsStatus.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            _window.DetailOsStatus.Opacity = 0.35;
        }
        else
        {
            _window.DetailOsLabel.TextDecorations = Windows.UI.Text.TextDecorations.None;
            _window.DetailOsLabel.Opacity = 1.0;
            _window.DetailOsStatus.Opacity = 1.0;
        }
        if (card.OsRowVisibility == Visibility.Visible)
        {
            _window.DetailOsStatus.Text = card.OsStatusText;
            _window.DetailOsStatus.Foreground = UIFactory.GetBrush(card.OsStatusColor);
            if (!osGreyed)
            {
                _window.DetailOsStatus.TextDecorations = card.IsOsInstalled
                    ? Windows.UI.Text.TextDecorations.Underline
                    : Windows.UI.Text.TextDecorations.None;
            }
            _window.DetailOsInstallBtn.Tag = card;
            _window.DetailOsInstallBtn.Content = WithInfoArrow(card.OsActionLabel, HasRealInfoContent(card, AddonType.OptiScaler), card.OsStatus == GameStatus.UpdateAvailable, _window.DetailOsInstallBtn);
            _window.DetailOsInstallBtn.IsEnabled = card.OsInstallEnabled;
            _window.DetailOsInstallBtn.Background = UIFactory.GetBrush(card.OsBtnBackground);
            _window.DetailOsInstallBtn.Foreground = UIFactory.GetBrush(card.OsBtnForeground);
            _window.DetailOsInstallBtn.BorderBrush = UIFactory.GetBrush(card.OsBtnBorderBrush);
            _window.DetailOsInstallBtn.BorderThickness = new Thickness(1);
            _window.DetailOsInstallBtn.Opacity = osGreyed ? 0.35 : 1.0;
            _window.DetailOsInstallBtn.IsHitTestVisible = !osGreyed;
            _window.DetailOsIniBtn.Tag = card;
            _window.DetailOsIniBtn.IsEnabled = card.OsIniExists;
            _window.DetailOsIniBtn.Opacity = osGreyed ? 0.35 : (card.OsIniExists ? 1 : 0.3);
            _window.DetailOsIniBtn.IsHitTestVisible = !osGreyed;
            _window.DetailOsDeleteBtn.Tag = card;
            var osShow = card.OsDeleteVisibility == Visibility.Visible;
            _window.DetailOsDeleteBtn.Opacity = osGreyed ? 0 : (osShow ? 1 : 0);
            _window.DetailOsDeleteBtn.IsHitTestVisible = osShow && !osGreyed;
            ApplyInfoButtonStyle(_window.DetailOsInfoBtn, card, AddonType.OptiScaler);
        }

        // RenoDX row (also used for external-only / Discord link)
        bool showRdx = !isLumaMode;
        _window.DetailRdxRow.Visibility = showRdx ? Visibility.Visible : Visibility.Collapsed;
        bool rdxGreyed = card.UseNormalReShade || !card.IsRsInstalled;
        _window.DetailRdxRow.Opacity = 1.0;
        _window.DetailRdxRow.IsHitTestVisible = true;
        if (showRdx)
        {
            _window.DetailRdxInstallBtn.Tag = card;
            if (card.IsExternalOnly)
            {
                // Strikethrough for external-only when normal ReShade is active
                var extStrike = card.UseNormalReShade
                    ? Windows.UI.Text.TextDecorations.Strikethrough
                    : Windows.UI.Text.TextDecorations.None;
                _window.DetailRdxLabel.TextDecorations = extStrike;
                _window.DetailRdxLabel.Opacity = rdxGreyed ? 0.35 : 1.0;

                _window.DetailRdxStatus.Text = card.IsRdxInstalled ? (card.RdxInstalledVersion ?? "Installed") : "";
                _window.DetailRdxStatus.Foreground = UIFactory.GetBrush("#5ECB7D");
                _window.DetailRdxStatus.TextDecorations = card.UseNormalReShade
                    ? Windows.UI.Text.TextDecorations.Strikethrough
                    : card.IsRdxInstalled
                        ? Windows.UI.Text.TextDecorations.Underline
                        : Windows.UI.Text.TextDecorations.None;
                _window.DetailRdxStatus.Opacity = rdxGreyed ? 0.35 : 1.0;
                var extLabel = WithInfoArrow(card.ExternalDisplayLabel, HasRealInfoContent(card, AddonType.RenoDX), card.Status == GameStatus.UpdateAvailable, _window.DetailRdxInstallBtn);
                _window.DetailRdxInstallBtn.Content = card.UseNormalReShade
                    ? (object)new TextBlock { Text = card.ExternalDisplayLabel, TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough }
                    : extLabel;
                _window.DetailRdxInstallBtn.IsEnabled = card.UseNormalReShade ? false : true;
                _window.DetailRdxInstallBtn.Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                _window.DetailRdxInstallBtn.Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
                _window.DetailRdxInstallBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
                _window.DetailRdxInstallBtn.BorderThickness = new Thickness(1);
                _window.DetailRdxInstallBtn.Opacity = rdxGreyed ? 0.35 : 1.0;
                _window.DetailRdxInstallBtn.IsHitTestVisible = !card.UseNormalReShade;
                _window.DetailRdxDeleteBtn.Tag = card;
                var extInstalled = card.IsRdxInstalled;
                _window.DetailRdxDeleteBtn.Opacity = rdxGreyed ? 0 : (extInstalled ? 1 : 0);
                _window.DetailRdxDeleteBtn.IsHitTestVisible = extInstalled && !rdxGreyed;
            }
            else
            {
                // Strikethrough RenoDX label and status when normal ReShade is active
                var rdxStrike = card.UseNormalReShade
                    ? Windows.UI.Text.TextDecorations.Strikethrough
                    : Windows.UI.Text.TextDecorations.None;
                _window.DetailRdxLabel.TextDecorations = rdxStrike;
                _window.DetailRdxLabel.Opacity = rdxGreyed ? 0.35 : 1.0;

                _window.DetailRdxStatus.Text = card.RdxStatusText;
                _window.DetailRdxStatus.Foreground = UIFactory.GetBrush(card.RdxStatusColor);
                _window.DetailRdxStatus.TextDecorations = card.UseNormalReShade
                    ? Windows.UI.Text.TextDecorations.Strikethrough
                    : card.IsRdxInstalled
                        ? Windows.UI.Text.TextDecorations.Underline
                        : Windows.UI.Text.TextDecorations.None;
                _window.DetailRdxStatus.Opacity = rdxGreyed ? 0.35 : 1.0;
                var rdxLabel = WithInfoArrow(card.InstallActionLabel, HasRealInfoContent(card, AddonType.RenoDX), card.Status == GameStatus.UpdateAvailable, _window.DetailRdxInstallBtn);
                _window.DetailRdxInstallBtn.Content = card.UseNormalReShade
                    ? (object)new TextBlock { Text = card.InstallActionLabel, TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough }
                    : rdxLabel;
                _window.DetailRdxInstallBtn.IsEnabled = card.UseNormalReShade ? false : card.CanInstall;
                _window.DetailRdxInstallBtn.Background = UIFactory.GetBrush(card.InstallBtnBackground);
                _window.DetailRdxInstallBtn.Foreground = UIFactory.GetBrush(card.InstallBtnForeground);
                _window.DetailRdxInstallBtn.BorderBrush = UIFactory.GetBrush(card.InstallBtnBorderBrush);
                _window.DetailRdxInstallBtn.BorderThickness = new Thickness(1);
                _window.DetailRdxInstallBtn.Opacity = rdxGreyed ? 0.35 : 1.0;
                _window.DetailRdxInstallBtn.IsHitTestVisible = !card.UseNormalReShade && card.IsRsInstalled;
                _window.DetailRdxDeleteBtn.Tag = card;
                var rdxShow = card.ReinstallRowVisibility == Visibility.Visible;
                _window.DetailRdxDeleteBtn.Opacity = rdxGreyed ? 0 : (rdxShow ? 1 : 0);
                _window.DetailRdxDeleteBtn.IsHitTestVisible = rdxShow && !rdxGreyed;
            }
            ApplyInfoButtonStyle(_window.DetailRdxInfoBtn, card, AddonType.RenoDX);
        }

        // Luma row
        if (isLumaMode)
        {
            _window.DetailLumaRow.Visibility = Visibility.Visible;
            _window.DetailLumaStatus.Text = card.LumaStatusText;
            _window.DetailLumaStatus.Foreground = UIFactory.GetBrush(card.LumaStatusColor);
            _window.DetailLumaInstallBtn.Tag = card;
            _window.DetailLumaInstallBtn.Content = WithInfoArrow(card.LumaActionLabel, HasRealInfoContent(card, AddonType.Luma), card.LumaStatus == GameStatus.UpdateAvailable, _window.DetailLumaInstallBtn);
            _window.DetailLumaInstallBtn.IsEnabled = card.IsLumaNotInstalling;
            _window.DetailLumaInstallBtn.Background = UIFactory.GetBrush(card.LumaBtnBackground);
            _window.DetailLumaInstallBtn.Foreground = UIFactory.GetBrush(card.LumaBtnForeground);
            _window.DetailLumaInstallBtn.BorderBrush = UIFactory.GetBrush(card.LumaBtnBorderBrush);
            _window.DetailLumaInstallBtn.BorderThickness = new Thickness(1);
            _window.DetailLumaIniBtn.Tag = card;
            bool lumaIniExists = !string.IsNullOrEmpty(card.InstallPath) && File.Exists(Path.Combine(card.InstallPath, "reshade.ini"));
            _window.DetailLumaIniBtn.IsEnabled = lumaIniExists;
            _window.DetailLumaIniBtn.Opacity = lumaIniExists ? 1 : 0.3;
            _window.DetailLumaDeleteBtn.Tag = card;
            var lumaShow = card.LumaReinstallVisibility == Visibility.Visible;
            _window.DetailLumaDeleteBtn.Opacity = lumaShow ? 1 : 0;
            _window.DetailLumaDeleteBtn.IsHitTestVisible = lumaShow;
            ApplyInfoButtonStyle(_window.DetailLumaInfoBtn, card, AddonType.Luma);
        }
        else _window.DetailLumaRow.Visibility = Visibility.Collapsed;

        // UE-Extended flyout (inline in RenoDX row, column 3)
        if (card.UeExtendedToggleVisibility == Visibility.Visible && !isLumaMode)
        {
            _window.DetailUeExtendedBtn.Opacity = 1;
            _window.DetailUeExtendedBtn.IsHitTestVisible = true;
            _window.DetailUeExtendedBtn.Tag = card;
            ToolTipService.SetToolTip(_window.DetailUeExtendedBtn,
                card.UseUeExtended ? "Disable UE Extended" : "Enable UE Extended");
            // Visual indicator: green when enabled, default when off
            if (card.UseUeExtended)
            {
                _window.DetailUeExtendedBtn.Background = UIFactory.Brush(ResourceKeys.AccentGreenBgBrush);
                _window.DetailUeExtendedBtn.Foreground = UIFactory.Brush(ResourceKeys.AccentGreenBrush);
                _window.DetailUeExtendedBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentGreenBorderBrush);
            }
            else
            {
                _window.DetailUeExtendedBtn.Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
                _window.DetailUeExtendedBtn.Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
                _window.DetailUeExtendedBtn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush);
            }
        }
        else
        {
            _window.DetailUeExtendedBtn.Opacity = 0;
            _window.DetailUeExtendedBtn.IsHitTestVisible = false;
        }

        // No mod message
        _window.DetailNoModMsg.Visibility = card.NoModVisibility;

        // Progress bars
        _window.DetailRefProgress.Visibility = card.RefRowVisibility == Visibility.Visible ? card.RefProgressVisibility : Visibility.Collapsed;
        _window.DetailRefProgress.Value = card.RefProgress;
        _window.DetailRefMessage.Visibility = card.RefRowVisibility == Visibility.Visible ? card.RefMessageVisibility : Visibility.Collapsed;
        _window.DetailRefMessage.Text = card.RefActionMessage;
        _window.DetailRefMessage.Foreground = UIFactory.GetBrush(GetMessageColor(card.RefActionMessage));
        _window.DetailRsProgress.Visibility = card.RsProgressVisibility;
        _window.DetailRsProgress.Value = card.RsProgress;
        _window.DetailRsMessage.Visibility = card.RsMessageVisibility;
        _window.DetailRsMessage.Text = card.RsActionMessage;
        _window.DetailRsMessage.Foreground = UIFactory.GetBrush(GetMessageColor(card.RsActionMessage));
        _window.DetailUlProgress.Visibility = card.UlRowVisibility == Visibility.Visible ? card.UlProgressVisibility : Visibility.Collapsed;
        _window.DetailUlProgress.Value = card.UlProgress;
        _window.DetailUlMessage.Visibility = card.UlRowVisibility == Visibility.Visible ? card.UlMessageVisibility : Visibility.Collapsed;
        _window.DetailUlMessage.Text = card.UlActionMessage;
        _window.DetailUlMessage.Foreground = UIFactory.GetBrush(GetMessageColor(card.UlActionMessage));
        _window.DetailDcProgress.Visibility = card.DcRowVisibility == Visibility.Visible ? card.DcProgressVisibility : Visibility.Collapsed;
        _window.DetailDcProgress.Value = card.DcProgress;
        _window.DetailDcMessage.Visibility = card.DcRowVisibility == Visibility.Visible ? card.DcMessageVisibility : Visibility.Collapsed;
        _window.DetailDcMessage.Text = card.DcActionMessage;
        _window.DetailDcMessage.Foreground = UIFactory.GetBrush(GetMessageColor(card.DcActionMessage));
        _window.DetailOsProgress.Visibility = card.OsRowVisibility == Visibility.Visible ? card.OsProgressVisibility : Visibility.Collapsed;
        _window.DetailOsProgress.Value = card.OsProgress;
        _window.DetailOsMessage.Visibility = card.OsRowVisibility == Visibility.Visible ? card.OsMessageVisibility : Visibility.Collapsed;
        _window.DetailOsMessage.Text = card.OsActionMessage;
        _window.DetailOsMessage.Foreground = UIFactory.GetBrush(GetMessageColor(card.OsActionMessage));
        _window.DetailRdxProgress.Visibility = card.ProgressVisibility;
        _window.DetailRdxProgress.Value = card.InstallProgress;
        _window.DetailRdxMessage.Visibility = card.MessageVisibility;
        _window.DetailRdxMessage.Text = card.ActionMessage;
        _window.DetailRdxMessage.Foreground = UIFactory.GetBrush(GetMessageColor(card.ActionMessage));
        _window.DetailLumaProgress.Visibility = card.LumaProgressVisibility;
        _window.DetailLumaProgress.Value = card.LumaProgress;
        _window.DetailLumaMessage.Visibility = card.LumaMessageVisibility;
        _window.DetailLumaMessage.Text = card.LumaActionMessage;
        _window.DetailLumaMessage.Foreground = UIFactory.GetBrush(GetMessageColor(card.LumaActionMessage));
    }

    public void DetailCard_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_currentDetailCard == null) return;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_currentDetailCard == null) return;
            UpdateDetailComponentRows(_currentDetailCard);

            // Refresh 32-bit / 64-bit badge when bitness changes
            if (e.PropertyName is "Is32Bit" or "Is32BitBadgeVisibility")
            {
                _window.Detail32BitBadge.Visibility = _currentDetailCard.Is32Bit
                    ? Visibility.Visible : Visibility.Collapsed;
                _window.Detail64BitBadge.Visibility = !_currentDetailCard.Is32Bit
                    ? Visibility.Visible : Visibility.Collapsed;
            }

            // Refresh Graphics API badge when API changes
            if (e.PropertyName is "HasGraphicsApiBadge" or "GraphicsApiLabel")
            {
                if (_currentDetailCard.HasGraphicsApiBadge)
                {
                    _window.DetailGraphicsApiText.Text = _currentDetailCard.GraphicsApiLabel;
                    _window.DetailGraphicsApiBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    _window.DetailGraphicsApiBadge.Visibility = Visibility.Collapsed;
                }
            }

            // Refresh addon file badge when install state changes
            if (e.PropertyName is "InstalledAddonFileName" or "Status" or "ActionMessage")
            {
                if (!string.IsNullOrEmpty(_currentDetailCard.InstalledAddonFileName))
                {
                    _window.DetailInstalledFile.Text = $"{_currentDetailCard.InstalledAddonFileName}";
                    _window.DetailInstalledFileBadge.Visibility = Visibility.Visible;
                    _window.DetailSepModPlatform.Visibility = Visibility.Visible;
                }
                else
                {
                    _window.DetailInstalledFileBadge.Visibility = Visibility.Collapsed;
                    _window.DetailSepModPlatform.Visibility = Visibility.Collapsed;
                }
            }

            // Refresh Luma mode buttons when luma state changes
            if (e.PropertyName is "IsLumaMode" or "LumaStatus" or "LumaBadgeVisibility" or "LumaBadgeLabel")
            {
                _window.DetailLumaToggle.IsChecked = _currentDetailCard.IsLumaMode;
                _window.UpdateLumaToggleStyle(_currentDetailCard.IsLumaMode);
            }

            // Refresh PCGW / Nexus Mods link visibility when URLs change
            if (e.PropertyName is "PcgwUrl" or "HasPcgwUrl")
            {
                _window.DetailPcgwBtn.Visibility = _currentDetailCard.HasPcgwUrl
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            if (e.PropertyName is "NexusModsUrl" or "HasNexusModsUrl")
            {
                _window.DetailNexusModsBtn.Visibility = _currentDetailCard.HasNexusModsUrl
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        });
    }

    /// <summary>Returns a color hex string based on message content: green for installs, red for removals, blue default.</summary>
    private static string GetMessageColor(string message) =>
        message.Contains('✅') ? "#5ECB7D"
        : message.Contains('✖') ? "#E06060"
        : "#7AACDD";
}
