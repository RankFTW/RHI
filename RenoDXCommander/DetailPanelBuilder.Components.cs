// DetailPanelBuilder.Components.cs — Component row updates, property-changed handling, and message color logic.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
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
            _window.DetailRefInstallBtn.Content = card.RefActionLabel;
            _window.DetailRefInstallBtn.IsEnabled = card.IsRefNotInstalling;
            _window.DetailRefInstallBtn.Background = UIFactory.GetBrush(card.RefBtnBackground);
            _window.DetailRefInstallBtn.Foreground = UIFactory.GetBrush(card.RefBtnForeground);
            _window.DetailRefInstallBtn.BorderBrush = UIFactory.GetBrush(card.RefBtnBorderBrush);
            _window.DetailRefInstallBtn.BorderThickness = new Thickness(1);
            _window.DetailRefDeleteBtn.Tag = card;
            var refShow = card.RefDeleteVisibility == Visibility.Visible;
            _window.DetailRefDeleteBtn.Opacity = refShow ? 1 : 0;
            _window.DetailRefDeleteBtn.IsHitTestVisible = refShow;
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
                }
                else
                {
                    _window.DetailRsStatus.Text = "Not Installed";
                    _window.DetailRsStatus.Foreground = UIFactory.GetBrush("#A0AABB");
                }
                _window.DetailRsInstallBtn.Tag = card;
                _window.DetailRsInstallBtn.Content = card.RsActionLabel;
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
                _window.DetailRsInstallBtn.Content = card.RsActionLabel;
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
        }

        // ReLimiter row — hidden when in Luma mode
        _window.DetailUlRow.Visibility = card.UlRowVisibility;
        if (card.UlRowVisibility == Visibility.Visible)
        {
            _window.DetailUlStatus.Text = card.UlStatusText;
            _window.DetailUlStatus.Foreground = UIFactory.GetBrush(card.UlStatusColor);
            _window.DetailUlStatus.TextDecorations = card.IsUlInstalled
                ? Windows.UI.Text.TextDecorations.Underline
                : Windows.UI.Text.TextDecorations.None;
            _window.DetailUlInstallBtn.Tag = card;
            _window.DetailUlInstallBtn.Content = card.UlActionLabel;
            _window.DetailUlInstallBtn.IsEnabled = card.IsUlNotInstalling;
            _window.DetailUlInstallBtn.Background = UIFactory.GetBrush(card.UlBtnBackground);
            _window.DetailUlInstallBtn.Foreground = UIFactory.GetBrush(card.UlBtnForeground);
            _window.DetailUlInstallBtn.BorderBrush = UIFactory.GetBrush(card.UlBtnBorderBrush);
            _window.DetailUlInstallBtn.BorderThickness = new Thickness(1);
            _window.DetailUlIniBtn.Tag = card;
            _window.DetailUlIniBtn.IsEnabled = card.UlIniExists;
            _window.DetailUlIniBtn.Opacity = card.UlIniExists ? 1 : 0.3;
            _window.DetailUlDeleteBtn.Tag = card;
            var ulShow = card.UlDeleteVisibility == Visibility.Visible;
            _window.DetailUlDeleteBtn.Opacity = ulShow ? 1 : 0;
            _window.DetailUlDeleteBtn.IsHitTestVisible = ulShow;
        }

        // RenoDX row (also used for external-only / Discord link)
        bool showRdx = !isLumaMode;
        _window.DetailRdxRow.Visibility = showRdx ? Visibility.Visible : Visibility.Collapsed;
        if (showRdx)
        {
            _window.DetailRdxInstallBtn.Tag = card;
            if (card.IsExternalOnly)
            {
                _window.DetailRdxStatus.Text = card.IsRdxInstalled ? (card.RdxInstalledVersion ?? "Installed") : "";
                _window.DetailRdxStatus.Foreground = UIFactory.GetBrush("#5ECB7D");
                _window.DetailRdxStatus.TextDecorations = card.IsRdxInstalled
                    ? Windows.UI.Text.TextDecorations.Underline
                    : Windows.UI.Text.TextDecorations.None;
                _window.DetailRdxInstallBtn.Content = card.ExternalDisplayLabel;
                _window.DetailRdxInstallBtn.IsEnabled = true;
                _window.DetailRdxInstallBtn.Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                _window.DetailRdxInstallBtn.Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
                _window.DetailRdxInstallBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
                _window.DetailRdxInstallBtn.BorderThickness = new Thickness(1);
                _window.DetailRdxDeleteBtn.Tag = card;
                var extInstalled = card.IsRdxInstalled;
                _window.DetailRdxDeleteBtn.Opacity = extInstalled ? 1 : 0;
                _window.DetailRdxDeleteBtn.IsHitTestVisible = extInstalled;
            }
            else
            {
                _window.DetailRdxStatus.Text = card.RdxStatusText;
                _window.DetailRdxStatus.Foreground = UIFactory.GetBrush(card.RdxStatusColor);
                _window.DetailRdxStatus.TextDecorations = card.IsRdxInstalled
                    ? Windows.UI.Text.TextDecorations.Underline
                    : Windows.UI.Text.TextDecorations.None;
                _window.DetailRdxInstallBtn.Content = card.InstallActionLabel;
                _window.DetailRdxInstallBtn.IsEnabled = card.CanInstall;
                _window.DetailRdxInstallBtn.Background = UIFactory.GetBrush(card.InstallBtnBackground);
                _window.DetailRdxInstallBtn.Foreground = UIFactory.GetBrush(card.InstallBtnForeground);
                _window.DetailRdxInstallBtn.BorderBrush = UIFactory.GetBrush(card.InstallBtnBorderBrush);
                _window.DetailRdxInstallBtn.BorderThickness = new Thickness(1);
                _window.DetailRdxDeleteBtn.Tag = card;
                var rdxShow = card.ReinstallRowVisibility == Visibility.Visible;
                _window.DetailRdxDeleteBtn.Opacity = rdxShow ? 1 : 0;
                _window.DetailRdxDeleteBtn.IsHitTestVisible = rdxShow;
            }
        }

        // Luma row
        if (isLumaMode)
        {
            _window.DetailLumaRow.Visibility = Visibility.Visible;
            _window.DetailLumaStatus.Text = card.LumaStatusText;
            _window.DetailLumaStatus.Foreground = UIFactory.GetBrush(card.LumaStatusColor);
            _window.DetailLumaInstallBtn.Tag = card;
            _window.DetailLumaInstallBtn.Content = card.LumaActionLabel;
            _window.DetailLumaInstallBtn.IsEnabled = card.IsLumaNotInstalling;
            _window.DetailLumaInstallBtn.Background = UIFactory.GetBrush(card.LumaBtnBackground);
            _window.DetailLumaInstallBtn.Foreground = UIFactory.GetBrush(card.LumaBtnForeground);
            _window.DetailLumaInstallBtn.BorderBrush = UIFactory.GetBrush(card.LumaBtnBorderBrush);
            _window.DetailLumaInstallBtn.BorderThickness = new Thickness(1);
            _window.DetailLumaDeleteBtn.Tag = card;
            var lumaShow = card.LumaReinstallVisibility == Visibility.Visible;
            _window.DetailLumaDeleteBtn.Opacity = lumaShow ? 1 : 0;
            _window.DetailLumaDeleteBtn.IsHitTestVisible = lumaShow;
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

            // Refresh 32-bit badge when bitness changes
            if (e.PropertyName is "Is32Bit" or "Is32BitBadgeVisibility")
            {
                _window.Detail32BitBadge.Visibility = _currentDetailCard.Is32Bit
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
        });
    }

    /// <summary>Returns a color hex string based on message content: green for installs, red for removals, blue default.</summary>
    private static string GetMessageColor(string message) =>
        message.Contains('✅') ? "#5ECB7D"
        : message.Contains('✖') ? "#E06060"
        : "#7AACDD";
}
