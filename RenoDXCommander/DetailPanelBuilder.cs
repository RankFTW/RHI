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
    private System.ComponentModel.PropertyChangedEventHandler? _dcModeLevelHandler;

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
            // Make version number a clickable link when DC is installed
            if (card.IsDcInstalled)
            {
                _window.DetailDcStatus.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                _window.DetailDcStatus.PointerPressed -= DcStatusLink_PointerPressed;
                _window.DetailDcStatus.PointerPressed += DcStatusLink_PointerPressed;
                ToolTipService.SetToolTip(_window.DetailDcStatus, "Version information");
            }
            else
            {
                _window.DetailDcStatus.TextDecorations = Windows.UI.Text.TextDecorations.None;
                _window.DetailDcStatus.PointerPressed -= DcStatusLink_PointerPressed;
                ToolTipService.SetToolTip(_window.DetailDcStatus, null);
            }
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
                _window.DetailRdxStatus.Text = card.IsRdxInstalled ? "Installed" : "";
                _window.DetailRdxStatus.Foreground = UIFactory.GetBrush("#5ECB7D");
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

    private static readonly Uri _dcCommitUri = new("https://github.com/pmnoxx/display-commander/commit/main");

    private void DcStatusLink_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(_dcCommitUri);
    }

    public void BuildOverridesPanel(GameCardViewModel card)
    {
        // Unsubscribe previous DcModeLevel handler to avoid leaked subscriptions
        if (_dcModeLevelHandler != null)
        {
            _window.ViewModel.PropertyChanged -= _dcModeLevelHandler;
            _dcModeLevelHandler = null;
        }

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

        // Mutable captured name so rename handler can update it for subsequent handlers
        var capturedName = gameName;

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
            var resetName = (originalStoreName ?? gameName).Trim();
            detectedBox.Text = resetName;
            wikiBox.Text = "";

            // Persist wiki mapping removal
            if (_window.ViewModel.GetNameMapping(capturedName) != null)
                _window.ViewModel.RemoveNameMapping(capturedName);

            // Persist rename back to original if name was changed
            if (!resetName.Equals(capturedName, StringComparison.OrdinalIgnoreCase))
            {
                _window.ViewModel.RenameGame(capturedName, resetName);
                capturedName = resetName;
                _window.RequestReselect(resetName);
            }
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

        // ── Auto-save: Game name on Enter ────────────────────────────────────────
        detectedBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var det = detectedBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(det)) return;
            if (det.Equals(capturedName, StringComparison.OrdinalIgnoreCase)) return;
            _window.ViewModel.RenameGame(capturedName, det);
            _window.RequestReselect(det);
            capturedName = det;
        };

        // ── Auto-save: Wiki name on Enter ────────────────────────────────────────
        wikiBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var key = wikiBox.Text?.Trim();
            if (!string.IsNullOrEmpty(key))
            {
                var existing = _window.ViewModel.GetNameMapping(capturedName);
                if (!key.Equals(existing, StringComparison.OrdinalIgnoreCase))
                    _window.ViewModel.AddNameMapping(capturedName, key);
            }
            else
            {
                if (_window.ViewModel.GetNameMapping(capturedName) != null)
                    _window.ViewModel.RemoveNameMapping(capturedName);
            }
        };

        // ── Per-game DC Mode + Shader mode (side by side) ─────────────────────────
        string? currentDcMode = _window.ViewModel.GetPerGameDcModeOverride(gameName);
        var globalDcLabel = _window.ViewModel.DcModeEnabled ? $"On — {_window.ViewModel.DcDllFileName}" : "Off";
        var dcModeOptions = new[] { $"Global ({globalDcLabel})", "Off", "Custom" };
        var dcModeCombo = new ComboBox
        {
            ItemsSource = dcModeOptions,
            // Vulkan games default to DC mode off when no explicit override is set
            SelectedIndex = (card.RequiresVulkanInstall && currentDcMode == null) ? 1
                : currentDcMode switch { "Off" => 1, "Custom" => 2, _ => 0 },
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "DC Mode",
        };
        ToolTipService.SetToolTip(dcModeCombo,
            "Global: Follow the global DC mode setting.\n" +
            "Off: Disable DC mode for this game.\n" +
            "Custom: Use a custom DLL filename for this game.");

        // Subscribe to DcModeEnabled/DcDllFileName changes to keep the "Global (...)" label current
        _dcModeLevelHandler = (sender, e) =>
        {
            if (e.PropertyName != "DcModeEnabled" && e.PropertyName != "DcDllFileName") return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                var updatedLabel = _window.ViewModel.DcModeEnabled ? $"On — {_window.ViewModel.DcDllFileName}" : "Off";
                var updatedOptions = new[] { $"Global ({updatedLabel})", "Off", "Custom" };
                var savedIndex = dcModeCombo.SelectedIndex;
                dcModeCombo.ItemsSource = updatedOptions;
                dcModeCombo.SelectedIndex = savedIndex;
            });
        };
        _window.ViewModel.PropertyChanged += _dcModeLevelHandler;

        // ── DC Mode Custom DLL filename selector ────────────────────────────────
        var dcCustomDllSelector = new ComboBox
        {
            IsEditable = true,
            ItemsSource = DllOverrideConstants.DcDllPickerNames,
            PlaceholderText = "Select or type DLL filename",
            Header = "DC Custom DLL filename",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = dcModeCombo.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed,
        };

        // Pre-populate with saved filename when opening panel for a game with DC Mode Custom
        if (currentDcMode == "Custom")
        {
            var savedDllName = _window.ViewModel.GetDcCustomDllFileName(gameName);
            if (!string.IsNullOrWhiteSpace(savedDllName))
            {
                if (DllOverrideConstants.DcDllPickerNames.Contains(savedDllName, StringComparer.OrdinalIgnoreCase))
                    dcCustomDllSelector.SelectedItem = DllOverrideConstants.DcDllPickerNames.First(n => n.Equals(savedDllName, StringComparison.OrdinalIgnoreCase));
                else
                {
                    var capturedDll = savedDllName;
                    dcCustomDllSelector.Loaded += (s, e) => dcCustomDllSelector.Text = capturedDll;
                }
            }
        }

        // Helper: rename the installed DC file to the chosen custom DLL filename
        void RenameDcToCustom(string dllFileName)
        {
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard?.DcRecord == null || string.IsNullOrEmpty(targetCard.InstallPath)) return;

            var oldName = targetCard.DcRecord.InstalledAs;
            if (string.IsNullOrEmpty(oldName)) return;
            if (oldName.Equals(dllFileName, StringComparison.OrdinalIgnoreCase)) return;

            var oldPath = Path.Combine(targetCard.InstallPath, oldName);
            var newPath = Path.Combine(targetCard.InstallPath, dllFileName);
            try
            {
                if (File.Exists(oldPath))
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(oldPath, newPath);
                    targetCard.DcRecord.InstalledAs = dllFileName;
                    targetCard.DcInstalledFile = dllFileName;
                    CrashReporter.Log($"[DetailPanelBuilder] DC custom rename {targetCard.GameName}: {oldName} → {dllFileName}");
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DetailPanelBuilder] DC custom rename failed for '{targetCard.GameName}' — {ex.Message}");
                targetCard.DcActionMessage = $"❌ Rename failed: {ex.Message}";
            }
        }

        // Track whether a selection change came from a real dropdown pick vs. text-match during typing
        bool dcCustomDllPickerIsTyping = false;
        bool dcCustomDllPickerJustCommitted = false;

        dcCustomDllSelector.GotFocus += (s, e) => { dcCustomDllPickerIsTyping = true; dcCustomDllPickerJustCommitted = false; };
        dcCustomDllSelector.LostFocus += (s, e) =>
        {
            dcCustomDllPickerIsTyping = false;
            if (dcCustomDllPickerJustCommitted) { dcCustomDllPickerJustCommitted = false; return; }
            // Commit on focus loss (e.g. user tabs away after typing a custom name)
            var typed = dcCustomDllSelector.Text?.Trim();
            var current = _window.ViewModel.GetDcCustomDllFileName(capturedName);
            if (!string.IsNullOrWhiteSpace(typed) && typed != current)
            {
                _window.ViewModel.SetDcCustomDllFileName(capturedName, typed);
                if (dcModeCombo.SelectedIndex == 2)
                    RenameDcToCustom(typed);
            }
        };

        // Auto-save: DC Custom DLL filename on dropdown selection (not during typing)
        dcCustomDllSelector.SelectionChanged += (s, e) =>
        {
            if (dcCustomDllPickerIsTyping) return; // ignore text-match events while user is typing
            var selected = dcCustomDllSelector.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(selected))
            {
                _window.ViewModel.SetDcCustomDllFileName(capturedName, selected);
                if (dcModeCombo.SelectedIndex == 2)
                    RenameDcToCustom(selected);
            }
        };

        // Auto-save: DC Custom DLL filename on Enter key (TextSubmitted fires when user presses Enter)
        dcCustomDllSelector.TextSubmitted += (sender, args) =>
        {
            var typed = args.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(typed))
            {
                dcCustomDllPickerIsTyping = true; // suppress SelectionChanged
                dcCustomDllPickerJustCommitted = true;
                _window.ViewModel.SetDcCustomDllFileName(capturedName, typed);
                if (dcModeCombo.SelectedIndex == 2)
                    RenameDcToCustom(typed);
                sender.SelectedItem = null; // clear stale selection so ComboBox doesn't revert
                dcCustomDllPickerIsTyping = false;
            }
            args.Handled = true; // prevent ComboBox from overriding SelectedItem
        };

        // ── Auto-save: DC Mode on selection change ───────────────────────────────
        dcModeCombo.SelectionChanged += (s, e) =>
        {
            string? newDcMode = dcModeCombo.SelectedIndex switch { 1 => "Off", 2 => "Custom", _ => (string?)null };
            var currentOverride = _window.ViewModel.GetPerGameDcModeOverride(capturedName);
            if (newDcMode != currentOverride)
            {
                var previousDcMode = currentOverride;
                _window.ViewModel.SetPerGameDcModeOverride(capturedName, newDcMode);
                _window.ViewModel.ApplyDcModeSwitchForCard(capturedName, previousDcMode);
            }

            // When switching to DC Mode Custom with a DLL filename already set, rename
            if (dcModeCombo.SelectedIndex == 2)
            {
                var dllName = dcCustomDllSelector.SelectedItem as string ?? dcCustomDllSelector.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(dllName))
                    RenameDcToCustom(dllName);
            }

            // Toggle DC Mode Custom DLL selector visibility
            dcCustomDllSelector.Visibility = dcModeCombo.SelectedIndex == 2
                ? Visibility.Visible
                : Visibility.Collapsed;
        };

        string currentShaderMode = _window.ViewModel.GetPerGameShaderMode(gameName);
        bool isGlobalShaders = currentShaderMode != "Select";
        var shaderToggle = new ToggleSwitch
        {
            Header = "Global Shaders",
            IsOn = isGlobalShaders,
            OnContent = "Using global shader selection",
            OffContent = "Using per-game shader selection",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(shaderToggle,
            "On = use the global shader selection from Settings. Off = pick specific shader packs for this game.");

        var selectShadersBtn = new Button
        {
            Content = "Select Shaders",
            FontSize = 12,
            Padding = new Thickness(12, 7, 12, 7),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !isGlobalShaders,
            Background = UIFactory.Brush(isGlobalShaders ? ResourceKeys.SurfaceOverlayBrush : ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(isGlobalShaders ? ResourceKeys.TextDisabledBrush : ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(isGlobalShaders ? ResourceKeys.BorderSubtleBrush : ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
        };
        ToolTipService.SetToolTip(selectShadersBtn, "Choose which shader packs to use for this game");

        shaderToggle.Toggled += (s, ev) =>
        {
            bool global = shaderToggle.IsOn;
            selectShadersBtn.IsEnabled = !global;
            selectShadersBtn.Background = UIFactory.Brush(global ? ResourceKeys.SurfaceOverlayBrush : ResourceKeys.AccentBlueBgBrush);
            selectShadersBtn.Foreground = UIFactory.Brush(global ? ResourceKeys.TextDisabledBrush : ResourceKeys.AccentBlueBrush);
            selectShadersBtn.BorderBrush = UIFactory.Brush(global ? ResourceKeys.BorderSubtleBrush : ResourceKeys.AccentBlueBorderBrush);

            // Auto-save: persist shader mode immediately
            var newMode = global ? "Global" : "Select";
            if (newMode != _window.ViewModel.GetPerGameShaderMode(capturedName))
            {
                _window.ViewModel.SetPerGameShaderMode(capturedName, newMode);
                if (newMode == "Global")
                    _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(capturedName);
                _window.ViewModel.DeployShadersForCard(capturedName);
            }
        };

        selectShadersBtn.Click += async (s, ev) =>
        {
            List<string>? current = _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection.TryGetValue(gameName, out var existing)
                ? existing
                : _window.ViewModel.Settings.SelectedShaderPacks;
            var result = await ShaderPopupHelper.ShowAsync(
                _window.Content.XamlRoot,
                _window.ViewModel.ShaderPackServiceInstance,
                current,
                ShaderPopupHelper.PopupContext.PerGame);
            if (result != null)
            {
                _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection[gameName] = result;
                _window.ViewModel.DeployShadersForCard(capturedName);
            }
        };

        // ── Two-column grid: DC Mode (left) | divider | Shaders (right) ────────
        var modeGrid = new Grid();
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Column 0: DC Mode combo + DC Mode Custom DLL selector
        var dcModeColumn = new StackPanel { Spacing = 8 };
        dcModeColumn.Children.Add(dcModeCombo);
        dcModeColumn.Children.Add(dcCustomDllSelector);
        Grid.SetColumn(dcModeColumn, 0);

        // Column 1: Vertical divider
        var modeDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(modeDivider, 1);

        // Column 2: Global Shaders toggle + Select Shaders button
        var shaderColumn = new StackPanel { Spacing = 8 };
        shaderColumn.Children.Add(shaderToggle);
        shaderColumn.Children.Add(selectShadersBtn);
        Grid.SetColumn(shaderColumn, 2);

        modeGrid.Children.Add(dcModeColumn);
        modeGrid.Children.Add(modeDivider);
        modeGrid.Children.Add(shaderColumn);
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
                if (selected == "Vulkan" && card.PerGameDcMode == null)
                    dcModeCombo.SelectedIndex = 1; // "Off"

                // Auto-save: persist rendering path immediately
                var newRenderPath = selected ?? "DirectX";
                var oldRenderPath = _window.ViewModel.GetVulkanRenderingPath(capturedName);
                if (newRenderPath != oldRenderPath)
                {
                    // Switching from DirectX → Vulkan: clean up DX install artifacts
                    if (newRenderPath == "Vulkan" && !string.IsNullOrEmpty(card.InstallPath))
                    {
                        if (card.RsRecord != null)
                            _window.ViewModel.UninstallReShadeCommand.Execute(card);
                        if (card.DcRecord != null)
                            _window.ViewModel.UninstallDcCommand.Execute(card);
                        var iniPath = Path.Combine(card.InstallPath, "reshade.ini");
                        if (File.Exists(iniPath))
                            try { File.Delete(iniPath); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder] Failed to delete reshade.ini at '{iniPath}' — {ex.Message}"); }
                        _window.ViewModel.ShaderPackServiceInstance.RemoveFromGameFolder(card.InstallPath);
                        _window.ViewModel.ShaderPackServiceInstance.RestoreOriginalIfPresent(card.InstallPath);
                    }
                    _window.ViewModel.SetVulkanRenderingPath(capturedName, newRenderPath);
                }
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
            "The game is automatically excluded from DC Mode and Update All.");
        var existingRsName = existingCfg?.ReShadeFileName ?? "";
        var existingDcName = existingCfg?.DcFileName ?? "";
        // Helper: rebuild one ComboBox's ItemsSource excluding the name chosen in the other box
        bool _updatingDllItems = false;
        void SyncDllNameItems(ComboBox box, ComboBox otherBox)
        {
            if (_updatingDllItems) return;
            _updatingDllItems = true;
            var otherSelected = (otherBox.SelectedItem as string ?? otherBox.Text ?? "").Trim();
            var filtered = string.IsNullOrWhiteSpace(otherSelected)
                ? DllOverrideConstants.CommonDllNames
                : DllOverrideConstants.CommonDllNames.Where(n => !n.Equals(otherSelected, StringComparison.OrdinalIgnoreCase)).ToArray();
            var currentSel = box.SelectedItem as string ?? box.Text;
            box.ItemsSource = filtered;
            // Restore selection/text
            if (!string.IsNullOrWhiteSpace(currentSel))
            {
                var match = filtered.FirstOrDefault(n => n.Equals(currentSel.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null) box.SelectedItem = match;
                else box.Text = currentSel;
            }
            _updatingDllItems = false;
        }

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
        // Initial cross-filter so each box hides the other's current selection
        SyncDllNameItems(dcNameBox, rsNameBox);
        SyncDllNameItems(rsNameBox, dcNameBox);
        dllOverrideToggle.Toggled += (s, ev) =>
        {
            rsNameBox.IsEnabled = dllOverrideToggle.IsOn;
            dcNameBox.IsEnabled = dllOverrideToggle.IsOn;

            // Auto-save: persist DLL override state immediately
            bool nowOn = dllOverrideToggle.IsOn;
            bool wasOn = _window.ViewModel.HasDllOverride(capturedName);
            if (nowOn == wasOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            if (nowOn)
            {
                var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
                var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
                var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
                _window.ViewModel.EnableDllOverride(targetCard, rsName, dcName);
            }
            else
            {
                _window.ViewModel.DisableDllOverride(targetCard);
            }
        };

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

        // ── Auto-save: RS/DC name boxes on Enter ─────────────────────────────────
        rsNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
            var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
            var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
            if (rsName.Equals(dcName, StringComparison.OrdinalIgnoreCase)) return;
            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            SyncDllNameItems(dcNameBox, rsNameBox);
        };
        dcNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
            var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
            var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
            if (rsName.Equals(dcName, StringComparison.OrdinalIgnoreCase)) return;
            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            SyncDllNameItems(rsNameBox, dcNameBox);
        };
        // ── Auto-save: RS/DC name boxes on dropdown selection ────────────────────
        rsNameBox.SelectionChanged += (s, e) =>
        {
            if (_updatingDllItems) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsName = rsNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(rsName)) return;
            var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
            var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
            if (rsName.Equals(dcName, StringComparison.OrdinalIgnoreCase)) return;
            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            SyncDllNameItems(dcNameBox, rsNameBox);
        };
        dcNameBox.SelectionChanged += (s, e) =>
        {
            if (_updatingDllItems) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
            var dcName = dcNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(dcName)) return;
            if (rsName.Equals(dcName, StringComparison.OrdinalIgnoreCase)) return;
            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            SyncDllNameItems(rsNameBox, dcNameBox);
        };

        // ── Global update inclusion + Wiki exclusion (inline row) ─────────────────
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

        // ── Auto-save: Update inclusion toggles ──────────────────────────────────
        rsToggle.Toggled += (s, ev) =>
        {
            if (!rsToggle.IsOn != _window.ViewModel.IsUpdateAllExcludedReShade(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(capturedName);
        };
        dcToggle.Toggled += (s, ev) =>
        {
            if (!dcToggle.IsOn != _window.ViewModel.IsUpdateAllExcludedDc(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDc(capturedName);
        };
        rdxToggle.Toggled += (s, ev) =>
        {
            if (!rdxToggle.IsOn != _window.ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);
        };

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

        // ── Auto-save: Wiki exclusion toggle ─────────────────────────────────────
        wikiExcludeToggle.Toggled += (s, ev) =>
        {
            if (wikiExcludeToggle.IsOn != _window.ViewModel.IsWikiExcluded(capturedName))
                _window.ViewModel.ToggleWikiExclusion(capturedName);
        };

        // Build inline row Grid: [Global update inclusion | divider | Wiki exclusion]
        var inlineRowGrid = new Grid();
        inlineRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inlineRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inlineRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Column 0: Global update inclusion
        var leftSection = new StackPanel { Spacing = 0 };
        leftSection.Children.Add(new TextBlock
        {
            Text = "Global update inclusion",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        });
        leftSection.Children.Add(toggleRow);
        Grid.SetColumn(leftSection, 0);

        // Column 1: Vertical divider
        var divider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(divider, 1);

        // Column 2: Wiki exclusion
        var rightSection = new StackPanel { Spacing = 0 };
        rightSection.Children.Add(wikiExcludeToggle);
        Grid.SetColumn(rightSection, 2);

        inlineRowGrid.Children.Add(leftSection);
        inlineRowGrid.Children.Add(divider);
        inlineRowGrid.Children.Add(rightSection);

        _window.OverridesPanel.Children.Add(inlineRowGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Button row (Reset only — auto-save replaces Save button) ──────────
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
            // Reset all controls to defaults
            detectedBox.Text = originalStoreName ?? gameName;
            wikiBox.Text = "";
            dcModeCombo.SelectedIndex = 0;
            shaderToggle.IsOn = true;
            if (renderPathCombo != null) renderPathCombo.SelectedItem = "DirectX";
            dllOverrideToggle.IsOn = false;
            rsToggle.IsOn = true;
            dcToggle.IsOn = true;
            rdxToggle.IsOn = true;
            wikiExcludeToggle.IsOn = false;

            // Persist all reset values immediately
            var resetName = (originalStoreName ?? gameName).Trim();
            bool nameChanged = !resetName.Equals(capturedName, StringComparison.OrdinalIgnoreCase);
            if (nameChanged && !string.IsNullOrWhiteSpace(resetName))
            {
                _window.ViewModel.RenameGame(capturedName, resetName);
                capturedName = resetName;
            }

            // Remove wiki mapping
            if (_window.ViewModel.GetNameMapping(capturedName) != null)
                _window.ViewModel.RemoveNameMapping(capturedName);

            // DC mode → Global (null)
            if (_window.ViewModel.GetPerGameDcModeOverride(capturedName) != null)
            {
                var prev = _window.ViewModel.GetPerGameDcModeOverride(capturedName);
                _window.ViewModel.SetPerGameDcModeOverride(capturedName, null);
                _window.ViewModel.ApplyDcModeSwitchForCard(capturedName, prev);
            }

            // Clear DC Mode Custom DLL filename
            _window.ViewModel.SetDcCustomDllFileName(capturedName, null);
            dcCustomDllSelector.SelectedItem = null;
            dcCustomDllSelector.Text = "";
            dcCustomDllSelector.Visibility = Visibility.Collapsed;

            // Shader mode → Global
            if (_window.ViewModel.GetPerGameShaderMode(capturedName) != "Global")
            {
                _window.ViewModel.SetPerGameShaderMode(capturedName, "Global");
                _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(capturedName);
                _window.ViewModel.DeployShadersForCard(capturedName);
            }

            // Disable DLL override
            if (_window.ViewModel.HasDllOverride(capturedName))
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                    _window.ViewModel.DisableDllOverride(targetCard);
            }

            // Include all in Update All
            if (_window.ViewModel.IsUpdateAllExcludedReShade(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedDc(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDc(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);

            // Disable wiki exclusion
            if (_window.ViewModel.IsWikiExcluded(capturedName))
                _window.ViewModel.ToggleWikiExclusion(capturedName);

            CrashReporter.Log($"[DetailPanelBuilder.BuildOverridesPanel] Overrides reset for: {capturedName}");

            // Only reselect if the game name actually changed
            if (nameChanged)
                _window.RequestReselect(capturedName);
        };

        _window.OverridesPanel.Children.Add(resetOverridesBtn);
    }


}
