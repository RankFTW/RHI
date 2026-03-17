using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Helper class responsible for detail panel population and overrides panel construction.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public class DetailPanelBuilder
{
    private readonly MainWindow _window;
    private readonly DispatcherQueue _dispatcherQueue;
    private GameCardViewModel? _currentDetailCard;

    public DetailPanelBuilder(MainWindow window)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;
    }

    /// <summary>Gets the currently displayed detail card (if any).</summary>
    public GameCardViewModel? CurrentDetailCard => _currentDetailCard;

    public void PopulateDetailPanel(GameCardViewModel card)
    {
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
        }
        else _window.DetailEngineBadge.Visibility = Visibility.Collapsed;

        // Graphics API badge
        if (card.HasGraphicsApiBadge)
        {
            _window.DetailGraphicsApiText.Text = card.GraphicsApiLabel;
            _window.DetailGraphicsApiBadge.Visibility = Visibility.Visible;
        }
        else _window.DetailGraphicsApiBadge.Visibility = Visibility.Collapsed;

        // Generic badge
        if (card.IsGenericMod)
        {
            _window.DetailGenericText.Text = card.GenericModLabel;
            _window.DetailGenericBadge.Visibility = Visibility.Visible;
        }
        else _window.DetailGenericBadge.Visibility = Visibility.Collapsed;

        // 32-bit badge
        _window.Detail32BitBadge.Visibility = card.Is32Bit ? Visibility.Visible : Visibility.Collapsed;

        // Wiki status badge
        var hasWikiLabel = !string.IsNullOrEmpty(card.WikiStatusLabel);
        _window.DetailWikiText.Text = card.WikiStatusLabel;
        _window.DetailWikiText.Foreground = UIFactory.GetBrush(card.WikiStatusBadgeForeground);
        _window.DetailWikiBadge.Background = UIFactory.GetBrush(card.WikiStatusBadgeBackground);
        _window.DetailWikiBadge.BorderBrush = UIFactory.GetBrush(card.WikiStatusBadgeBorderBrush);
        _window.DetailWikiBadge.BorderThickness = new Thickness(1);
        _window.DetailWikiBadge.Visibility = hasWikiLabel ? Visibility.Visible : Visibility.Collapsed;
        _window.DetailSepPlatformStatus.Visibility = hasWikiLabel ? Visibility.Visible : Visibility.Collapsed;

        // Author badges
        _window.DetailAuthorBadgePanel.Children.Clear();
        if (card.HasAuthors)
        {
            foreach (var author in card.AuthorList)
            {
                var textBlock = new TextBlock
                {
                    Text = author,
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
        _window.DetailFavIcon.Text = card.IsFavourite ? "⭐" : "☆";
        _window.DetailFavIcon.Foreground = new SolidColorBrush(card.IsFavourite
            ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
            : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.TextDisabledBrush]).Color);

        _window.DetailDiscussionBtn.Tag = card;
        _window.DetailDiscussionBtn.Visibility = card.NameLinkVisibility;

        _window.DetailNotesBtn.Tag = card;
        _window.DetailNotesBtn.Visibility = card.NotesButtonVisibility;

        _window.DetailHideBtn.Tag = card;
        _window.DetailHideIcon.Text = card.IsHidden ? "Show" : "Hide";
        _window.DetailHideBtn.Foreground = card.IsHidden
            ? UIFactory.Brush(ResourceKeys.TextTertiaryBrush)
            : UIFactory.Brush(ResourceKeys.TextDisabledBrush);

        // Folder management buttons
        _window.DetailFolderBtn.Tag = card;
        _window.DetailChangeFolderBtn.Tag = card;
        _window.DetailRemoveGameBtn.Tag = card;

        // Luma badge toggle
        if (card.LumaBadgeVisibility == Visibility.Visible)
        {
            _window.DetailLumaBadgeContainer.Visibility = Visibility.Visible;
            _window.DetailLumaToggle.IsChecked = card.IsLumaMode;
            _window.UpdateLumaToggleStyle(card.IsLumaMode);
        }
        else
        {
            _window.DetailLumaBadgeContainer.Visibility = Visibility.Collapsed;
        }

        // Populate component rows
        UpdateDetailComponentRows(card);
    }

    public void UpdateDetailComponentRows(GameCardViewModel card)
    {
        bool isLumaMode = card.LumaFeatureEnabled && card.IsLumaMode;

        // ReShade row
        _window.DetailRsRow.Visibility = isLumaMode ? Visibility.Collapsed : Visibility.Visible;
        if (!isLumaMode)
        {
            if (card.RequiresVulkanInstall)
            {
                // Vulkan layer install path — RS is "installed" when reshade.ini exists
                // in the game folder (the Vulkan layer needs it to function for this game).
                bool vulkanLayerInstalled = VulkanLayerService.IsLayerInstalled();
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
                _window.DetailRsInstallBtn.Content = vulkanLayerInstalled ? "Reinstall Vulkan Layer" : "Install Vulkan Layer";
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

        // DC row
        _window.DetailDcRow.Visibility = isLumaMode ? Visibility.Collapsed : Visibility.Visible;
        if (!isLumaMode)
        {
            _window.DetailDcStatus.Text = card.DcStatusText;
            _window.DetailDcStatus.Foreground = UIFactory.GetBrush(card.DcStatusColor);
            _window.DetailDcInstallBtn.Tag = card;
            _window.DetailDcInstallBtn.Content = card.DcActionLabel;
            _window.DetailDcInstallBtn.IsEnabled = card.IsDcNotInstalling;
            _window.DetailDcInstallBtn.Background = UIFactory.GetBrush(card.DcBtnBackground);
            _window.DetailDcInstallBtn.Foreground = UIFactory.GetBrush(card.DcBtnForeground);
            _window.DetailDcInstallBtn.BorderBrush = UIFactory.GetBrush(card.DcBtnBorderBrush);
            _window.DetailDcInstallBtn.BorderThickness = new Thickness(1);
            _window.DetailDcIniBtn.Tag = card;
            _window.DetailDcIniBtn.IsEnabled = card.DcIniExists;
            _window.DetailDcIniBtn.Opacity = card.DcIniExists ? 1 : 0.3;
            _window.DetailDcDeleteBtn.Tag = card;
            var dcShow = card.DcDeleteVisibility == Visibility.Visible;
            _window.DetailDcDeleteBtn.Opacity = dcShow ? 1 : 0;
            _window.DetailDcDeleteBtn.IsHitTestVisible = dcShow;
        }

        // RenoDX row (also used for external-only / Discord link)
        bool showRdx = !isLumaMode;
        _window.DetailRdxRow.Visibility = showRdx ? Visibility.Visible : Visibility.Collapsed;
        if (showRdx)
        {
            _window.DetailRdxInstallBtn.Tag = card;
            if (card.IsExternalOnly)
            {
                _window.DetailRdxStatus.Text = "";
                _window.DetailRdxInstallBtn.Content = card.ExternalLabel;
                _window.DetailRdxInstallBtn.IsEnabled = true;
                _window.DetailRdxInstallBtn.Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                _window.DetailRdxInstallBtn.Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
                _window.DetailRdxInstallBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
                _window.DetailRdxInstallBtn.BorderThickness = new Thickness(1);
                _window.DetailRdxDeleteBtn.Opacity = 0;
                _window.DetailRdxDeleteBtn.IsHitTestVisible = false;
            }
            else
            {
                _window.DetailRdxStatus.Text = card.RdxStatusText;
                _window.DetailRdxStatus.Foreground = UIFactory.GetBrush(card.RdxStatusColor);
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
        _window.DetailRsProgress.Visibility = card.RsProgressVisibility;
        _window.DetailRsProgress.Value = card.RsProgress;
        _window.DetailRsMessage.Visibility = card.RsMessageVisibility;
        _window.DetailRsMessage.Text = card.RsActionMessage;
        _window.DetailDcProgress.Visibility = card.DcProgressVisibility;
        _window.DetailDcProgress.Value = card.DcProgress;
        _window.DetailDcMessage.Visibility = card.DcMessageVisibility;
        _window.DetailDcMessage.Text = card.DcActionMessage;
        _window.DetailRdxProgress.Visibility = card.ProgressVisibility;
        _window.DetailRdxProgress.Value = card.InstallProgress;
        _window.DetailRdxMessage.Visibility = card.MessageVisibility;
        _window.DetailRdxMessage.Text = card.ActionMessage;
        _window.DetailLumaProgress.Visibility = card.LumaProgressVisibility;
        _window.DetailLumaProgress.Value = card.LumaProgress;
        _window.DetailLumaMessage.Visibility = card.LumaMessageVisibility;
        _window.DetailLumaMessage.Text = card.LumaActionMessage;
    }

    public void DetailCard_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_currentDetailCard == null) return;
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_currentDetailCard == null) return;
            UpdateDetailComponentRows(_currentDetailCard);

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

    public void BuildOverridesPanel(GameCardViewModel card)
    {
        _window.OverridesPanel.Children.Clear();

        var gameName = card.GameName;
        bool isLumaMode = _window.ViewModel.IsLumaEnabled(gameName);

        // ── Title ────────────────────────────────────────────────────────────────
        _window.OverridesPanel.Children.Add(new TextBlock
        {
            Text = "Overrides",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        // ── Game name + Wiki name ────────────────────────────────────────────────
        var detectedBox = new TextBox
        {
            Header = "Game name (editable)",
            Text = gameName,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var wikiBox = new TextBox
        {
            Header = "Wiki mod name",
            PlaceholderText = "Exact wiki name",
            Text = _window.ViewModel.GetNameMapping(gameName) ?? "",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var originalStoreName = _window.ViewModel.GetOriginalStoreName(gameName);
        var resetBtn = new Button
        {
            Content = "↩ Reset",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(10, 6, 10, 6),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
        };
        ToolTipService.SetToolTip(resetBtn,
            "Reset game name back to auto-detected and clear wiki name mapping.");
        resetBtn.Click += (s, ev) =>
        {
            detectedBox.Text = originalStoreName ?? gameName;
            wikiBox.Text = "";
        };

        var nameGrid = new Grid { ColumnSpacing = 8 };
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(detectedBox, 0);
        Grid.SetColumn(wikiBox, 1);
        Grid.SetColumn(resetBtn, 2);
        nameGrid.Children.Add(detectedBox);
        nameGrid.Children.Add(wikiBox);
        nameGrid.Children.Add(resetBtn);
        _window.OverridesPanel.Children.Add(nameGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Per-game DC Mode + Shader mode (side by side) ─────────────────────────
        int? currentDcMode = _window.ViewModel.GetPerGameDcModeOverride(gameName);
        var globalDcLabel = _window.ViewModel.DcModeLevel switch { 1 => "DC Mode 1", 2 => "DC Mode 2", _ => "Off" };
        var dcModeOptions = new[] { $"Global ({globalDcLabel})", "Exclude (Off)", "DC Mode 1", "DC Mode 2" };
        var dcModeCombo = new ComboBox
        {
            ItemsSource = dcModeOptions,
            // Vulkan games default to DC mode off when no explicit override is set
            SelectedIndex = (card.RequiresVulkanInstall && currentDcMode == null) ? 1
                : currentDcMode switch { null => 0, 0 => 1, 1 => 2, 2 => 3, _ => 0 },
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "DC Mode",
        };
        ToolTipService.SetToolTip(dcModeCombo,
            "Global = use the Settings DC Mode. Exclude (Off) = always use normal naming. " +
            "DC Mode 1 = force dxgi.dll proxy. DC Mode 2 = force winmm.dll proxy.");

        string currentShaderMode = _window.ViewModel.GetPerGameShaderMode(gameName);
        var globalShaderLabel = _window.ViewModel.ShaderDeployMode.ToString();
        var shaderModeValues = new[] { "Global", "Off", "Minimum", "All", "User", "Select" };
        var shaderModeDisplay = new[] { $"Global ({globalShaderLabel})", "Off", "Minimum", "All", "User", "Select" };
        int shaderSelectedIdx = Array.IndexOf(shaderModeValues, currentShaderMode);
        if (shaderSelectedIdx < 0) shaderSelectedIdx = 0;
        var shaderModeCombo = new ComboBox
        {
            ItemsSource = shaderModeDisplay,
            SelectedIndex = shaderSelectedIdx,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "Shader Mode",
        };
        ToolTipService.SetToolTip(shaderModeCombo,
            "Global = follow the Settings toggle. Off = no shaders. Minimum = Lilium only. All = all packs. User = custom folder only. Select = pick specific packs.\n" +
            "Note: Per-game shader mode only applies when ReShade is used standalone (DC Mode OFF). " +
            "When DC Mode is ON, all DC-mode games share the DC global shader folder.");

        // Track previous shader mode index so we can revert on picker cancel
        int previousShaderIdx = shaderSelectedIdx;
        shaderModeCombo.SelectionChanged += async (s, ev) =>
        {
            var idx = shaderModeCombo.SelectedIndex;
            if (idx >= 0 && idx < shaderModeValues.Length && shaderModeValues[idx] == "Select")
            {
                // Trigger the per-game shader selection picker
                if (_window.ViewModel.ShowPerGameShaderSelectionPicker != null)
                {
                    // Pre-populate with existing per-game selection, or fall back to global
                    List<string>? current = _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection.TryGetValue(gameName, out var existing)
                        ? existing
                        : _window.ViewModel.Settings.SelectedShaderPacks;
                    var result = await _window.ViewModel.ShowPerGameShaderSelectionPicker(gameName, current);
                    if (result != null)
                    {
                        // Store the per-game selection
                        _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection[gameName] = result;
                        previousShaderIdx = idx;
                    }
                    else
                    {
                        // User cancelled — revert ComboBox to previous value
                        shaderModeCombo.SelectedIndex = previousShaderIdx;
                    }
                }
            }
            else
            {
                previousShaderIdx = idx;
            }
        };

        var modeGrid = new Grid { ColumnSpacing = 8 };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dcModeCombo, 0);
        Grid.SetColumn(shaderModeCombo, 1);
        modeGrid.Children.Add(dcModeCombo);
        modeGrid.Children.Add(shaderModeCombo);
        _window.OverridesPanel.Children.Add(modeGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Rendering Path (dual-API games only) ─────────────────────────────────
        ComboBox? renderPathCombo = null;
        if (card.IsDualApiGame)
        {
            var renderPathItems = new[] { "DirectX", "Vulkan" };
            renderPathCombo = new ComboBox
            {
                Header = "Rendering Path",
                ItemsSource = renderPathItems,
                SelectedItem = card.VulkanRenderingPath,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTipService.SetToolTip(renderPathCombo,
                "Choose which rendering path ReShade targets. " +
                "DirectX uses per-game DLL injection. Vulkan uses a global implicit layer.");

            // When switching to Vulkan, force DC mode combo to "Exclude (Off)" visually
            renderPathCombo.SelectionChanged += (s, e) =>
            {
                var selected = renderPathCombo.SelectedItem as string;
                if (selected == "Vulkan" && !card.PerGameDcMode.HasValue)
                    dcModeCombo.SelectedIndex = 1; // "Exclude (Off)"
            };

            _window.OverridesPanel.Children.Add(renderPathCombo);
            _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());
        }

        // ── DLL naming override (grouped in a border) ────────────────────────────
        bool isDllOverride = _window.ViewModel.HasDllOverride(gameName);
        var existingCfg = _window.ViewModel.GetDllOverride(gameName);
        bool is32Bit = card.Is32Bit;
        var defaultRsName = is32Bit ? "ReShade32.dll" : "ReShade64.dll";
        var defaultDcName = is32Bit ? "zzz_display_commander.addon32" : "zzz_display_commander.addon64";

        var dllOverrideToggle = new ToggleSwitch
        {
            Header = "DLL naming override",
            IsOn = isDllOverride,
            IsEnabled = !isLumaMode,
            OnContent = "Custom filenames enabled",
            OffContent = "Using default filenames",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(dllOverrideToggle,
            "Override the filenames ReShade and Display Commander are installed as. " +
            "When enabled, existing RS/DC files are renamed to the custom filenames. " +
            "The game is automatically excluded from DC Mode, Update All, and global shaders.");
        var existingRsName = existingCfg?.ReShadeFileName ?? "";
        var existingDcName = existingCfg?.DcFileName ?? "";
        var rsNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = defaultRsName,
            Header = "ReShade filename",
            FontSize = 12,
            IsEnabled = isDllOverride,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DllOverrideConstants.CommonDllNames,
        };
        if (!string.IsNullOrEmpty(existingRsName))
        {
            if (DllOverrideConstants.CommonDllNames.Contains(existingRsName, StringComparer.OrdinalIgnoreCase))
                rsNameBox.SelectedItem = DllOverrideConstants.CommonDllNames.First(n => n.Equals(existingRsName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var capturedRs = existingRsName;
                rsNameBox.Loaded += (s, e) => rsNameBox.Text = capturedRs;
            }
        }
        var dcNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = defaultDcName,
            Header = "DC filename",
            FontSize = 12,
            IsEnabled = isDllOverride,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DllOverrideConstants.CommonDllNames,
        };
        if (!string.IsNullOrEmpty(existingDcName))
        {
            if (DllOverrideConstants.CommonDllNames.Contains(existingDcName, StringComparer.OrdinalIgnoreCase))
                dcNameBox.SelectedItem = DllOverrideConstants.CommonDllNames.First(n => n.Equals(existingDcName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var capturedDc = existingDcName;
                dcNameBox.Loaded += (s, e) => dcNameBox.Text = capturedDc;
            }
        }
        dllOverrideToggle.Toggled += (s, ev) => { rsNameBox.IsEnabled = dllOverrideToggle.IsOn; dcNameBox.IsEnabled = dllOverrideToggle.IsOn; };

        var dllNameGrid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        dllNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dllNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(rsNameBox, 0);
        Grid.SetColumn(dcNameBox, 1);
        dllNameGrid.Children.Add(rsNameBox);
        dllNameGrid.Children.Add(dcNameBox);

        var dllGroupPanel = new StackPanel { Spacing = 4 };
        dllGroupPanel.Children.Add(dllOverrideToggle);
        dllGroupPanel.Children.Add(dllNameGrid);
        var dllGroupBorder = new Border
        {
            Child = dllGroupPanel,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderSubtleBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 12),
        };
        _window.OverridesPanel.Children.Add(dllGroupBorder);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Global update inclusion (3 ToggleSwitches) ───────────────────────────
        _window.OverridesPanel.Children.Add(new TextBlock
        {
            Text = "Global update inclusion",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        });

        var rsToggle = new ToggleSwitch
        {
            Header = "ReShade",
            IsOn = !_window.ViewModel.IsUpdateAllExcludedReShade(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var dcToggle = new ToggleSwitch
        {
            Header = "DC",
            IsOn = !_window.ViewModel.IsUpdateAllExcludedDc(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var rdxToggle = new ToggleSwitch
        {
            Header = "RenoDX",
            IsOn = !_window.ViewModel.IsUpdateAllExcludedRenoDx(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };

        var rsBorder = new Border
        {
            Child = rsToggle,
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var dcBorder = new Border
        {
            Child = dcToggle,
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var rdxBorder = new Border
        {
            Child = rdxToggle,
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };

        var toggleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };
        toggleRow.Children.Add(rsBorder);
        toggleRow.Children.Add(dcBorder);
        toggleRow.Children.Add(rdxBorder);
        _window.OverridesPanel.Children.Add(toggleRow);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Wiki exclusion ────────────────────────────────────────────────────────
        var wikiExcludeToggle = new ToggleSwitch
        {
            Header = "Wiki exclusion",
            IsOn = _window.ViewModel.IsWikiExcluded(gameName),
            OnContent = "Excluded from wiki lookups",
            OffContent = "Included in wiki lookups",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(wikiExcludeToggle,
            "When enabled, this game will not be looked up on the RenoDX wiki. " +
            "Useful for games that share a name with an unrelated wiki entry.");
        _window.OverridesPanel.Children.Add(wikiExcludeToggle);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Button row (Reset + Save) ───────────────────────────────────────────
        var resetOverridesBtn = new Button
        {
            Content = "Reset Overrides",
            FontSize = 12,
            Padding = new Thickness(16, 8, 16, 8),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        resetOverridesBtn.Click += (s, ev) =>
        {
            detectedBox.Text = originalStoreName ?? gameName;
            wikiBox.Text = "";
            dcModeCombo.SelectedIndex = 0;
            shaderModeCombo.SelectedIndex = 0;
            if (renderPathCombo != null) renderPathCombo.SelectedItem = "DirectX";
            dllOverrideToggle.IsOn = false;
            rsToggle.IsOn = true;
            dcToggle.IsOn = true;
            rdxToggle.IsOn = true;
            wikiExcludeToggle.IsOn = false;
        };

        var saveBtn = new Button
        {
            Content = "Save Overrides",
            FontSize = 12,
            Padding = new Thickness(16, 8, 16, 8),
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };

        var hintText = new TextBlock
        {
            Text = "You must press Save for changes to apply.",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(resetOverridesBtn, 0);
        Grid.SetColumn(hintText, 1);
        Grid.SetColumn(saveBtn, 2);
        buttonRow.Children.Add(resetOverridesBtn);
        buttonRow.Children.Add(hintText);
        buttonRow.Children.Add(saveBtn);
        var capturedName = gameName;
        saveBtn.Click += (s, ev) =>
        {
            var det = detectedBox.Text?.Trim();

            // Handle game rename
            if (!string.IsNullOrEmpty(capturedName) && !string.IsNullOrEmpty(det)
                && !det.Equals(capturedName, StringComparison.OrdinalIgnoreCase))
            {
                _window.ViewModel.RenameGame(capturedName, det);
            }

            // DC Mode override
            int? newDcMode = dcModeCombo.SelectedIndex switch { 1 => 0, 2 => 1, 3 => 2, _ => null };
            if (!string.IsNullOrEmpty(det) && newDcMode != _window.ViewModel.GetPerGameDcModeOverride(det))
            {
                _window.ViewModel.SetPerGameDcModeOverride(det, newDcMode);
                _window.ViewModel.ApplyDcModeSwitchForCard(det);
            }

            // Per-component Update All
            bool nowRsExcluded = !rsToggle.IsOn;
            if (!string.IsNullOrEmpty(det) && nowRsExcluded != _window.ViewModel.IsUpdateAllExcludedReShade(det))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(det);

            bool nowDcExcluded = !dcToggle.IsOn;
            if (!string.IsNullOrEmpty(det) && nowDcExcluded != _window.ViewModel.IsUpdateAllExcludedDc(det))
                _window.ViewModel.ToggleUpdateAllExclusionDc(det);

            bool nowRdxExcluded = !rdxToggle.IsOn;
            if (!string.IsNullOrEmpty(det) && nowRdxExcluded != _window.ViewModel.IsUpdateAllExcludedRenoDx(det))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(det);

            // Shader mode
            var shaderModeIdx = shaderModeCombo.SelectedIndex;
            var newShaderMode = shaderModeIdx >= 0 && shaderModeIdx < shaderModeValues.Length
                ? shaderModeValues[shaderModeIdx] : "Global";
            if (!string.IsNullOrEmpty(det) && newShaderMode != _window.ViewModel.GetPerGameShaderMode(det))
            {
                _window.ViewModel.SetPerGameShaderMode(det, newShaderMode);
                _window.ViewModel.DeployShadersForCard(det);
            }

            // Rendering path (dual-API games)
            if (renderPathCombo != null && !string.IsNullOrEmpty(det))
            {
                var newRenderPath = renderPathCombo.SelectedItem as string ?? "DirectX";
                var oldRenderPath = _window.ViewModel.GetVulkanRenderingPath(det);
                if (newRenderPath != oldRenderPath)
                {
                    // Switching from DirectX → Vulkan: clean up DX install artifacts
                    if (newRenderPath == "Vulkan" && !string.IsNullOrEmpty(card.InstallPath))
                    {
                        // Uninstall DX ReShade DLL if installed
                        if (card.RsRecord != null)
                            _window.ViewModel.UninstallReShadeCommand.Execute(card);
                        // Uninstall DC if installed
                        if (card.DcRecord != null)
                            _window.ViewModel.UninstallDcCommand.Execute(card);
                        // Remove reshade.ini (DX version)
                        var iniPath = Path.Combine(card.InstallPath, "reshade.ini");
                        if (File.Exists(iniPath))
                            try { File.Delete(iniPath); } catch { }
                        // Remove managed shaders and restore originals
                        _window.ViewModel.ShaderPackServiceInstance.RemoveFromGameFolder(card.InstallPath);
                        _window.ViewModel.ShaderPackServiceInstance.RestoreOriginalIfPresent(card.InstallPath);
                    }

                    _window.ViewModel.SetVulkanRenderingPath(det, newRenderPath);
                }
            }

            // Wiki exclusion
            if (!string.IsNullOrEmpty(det) && wikiExcludeToggle.IsOn != _window.ViewModel.IsWikiExcluded(det))
                _window.ViewModel.ToggleWikiExclusion(det);

            // DLL naming override
            bool nowDllOverride = dllOverrideToggle.IsOn;
            bool wasDllOverride = !string.IsNullOrEmpty(det) && _window.ViewModel.HasDllOverride(det);
            if (!string.IsNullOrEmpty(det))
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(det, StringComparison.OrdinalIgnoreCase));

                if (nowDllOverride && !wasDllOverride && targetCard != null)
                {
                    var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                    var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
                    var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
                    var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
                    _window.ViewModel.EnableDllOverride(targetCard, rsName, dcName);
                }
                else if (nowDllOverride && wasDllOverride && targetCard != null)
                {
                    var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                    var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
                    var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
                    var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
                    _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
                }
                else if (!nowDllOverride && wasDllOverride && targetCard != null)
                {
                    _window.ViewModel.DisableDllOverride(targetCard);
                }
            }

            // Name mapping
            var key = wikiBox.Text?.Trim();
            if (!string.IsNullOrEmpty(det) && !string.IsNullOrEmpty(key))
                _window.ViewModel.AddNameMapping(det, key);
            else if (!string.IsNullOrEmpty(det) && string.IsNullOrEmpty(key))
                _window.ViewModel.RemoveNameMapping(det);

            CrashReporter.Log($"[DetailPanelBuilder.BuildOverridesPanel] Compact overrides saved for: {det}");

            // Re-select the game card after the filter refresh so the user
            // doesn't lose their selection when saving overrides.
            _window.RequestReselect(det);
        };
        _window.OverridesPanel.Children.Add(buttonRow);
    }


}
