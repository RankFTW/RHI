using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Helper class responsible for building and showing the per-game overrides flyout.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public partial class OverridesFlyoutBuilder
{
    private readonly MainWindow _window;
    private readonly ICrashReporter _crashReporter;

    // Mutable captured name shared across partial methods — updated by rename/reset handlers.
    private string _capturedName = "";

    public OverridesFlyoutBuilder(MainWindow window, ICrashReporter crashReporter)
    {
        _window = window;
        _crashReporter = crashReporter;
    }

    /// <summary>
    /// Builds and shows the overrides flyout anchored to the given element.
    /// </summary>
    public void OpenOverridesFlyout(GameCardViewModel card, FrameworkElement anchor)
    {
        var ViewModel = _window.ViewModel;
        var gameName = card.GameName;
        bool isLumaMode = ViewModel.IsLumaEnabled(gameName);

        var panel = new StackPanel { Spacing = 8, Width = 700 };

        // ── Title + Reset Overrides link ──
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        titleRow.Children.Add(new TextBlock
        {
            Text = "Game Overrides",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        // Forward-declare controls that the reset handler needs
        TextBox gameNameBox = null!;
        TextBox wikiNameBox = null!;
        ToggleSwitch dllOverrideToggle = null!;
        ComboBox bitnessCombo = null!;
        ComboBox apiCombo = null!;
        TextBlock updateSummaryText = null!;

        // Mutable captured name so rename handler can update it for subsequent handlers
        _capturedName = gameName;
        var originalStoreName = ViewModel.GetOriginalStoreName(gameName);

        // ── Reset Overrides button (small link near title) ──
        var resetOverridesLink = new HyperlinkButton
        {
            Content = "Reset Overrides",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 2, 4, 2),
        };
        ToolTipService.SetToolTip(resetOverridesLink, "Reset all overrides for this game to defaults.");
        Grid.SetColumn(resetOverridesLink, 1);
        titleRow.Children.Add(resetOverridesLink);

        panel.Children.Add(titleRow);

        // ══════════════════════════════════════════════════════════════════════
        // Main two-column grid
        // ══════════════════════════════════════════════════════════════════════
        var mainGrid = new Grid { ColumnSpacing = 0, RowSpacing = 12 };
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // divider
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 5 content rows
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1 (separator)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 3 (separator)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 4

        // ── Vertical divider (spans all rows) ──
        var verticalDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(verticalDivider, 1);
        Grid.SetRow(verticalDivider, 0);
        Grid.SetRowSpan(verticalDivider, 5);
        mainGrid.Children.Add(verticalDivider);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 0 — Left: Game name + Wiki name + Reset + Wiki toggle
        //         Right: DLL override toggle + RS/DC/OS combos
        // ══════════════════════════════════════════════════════════════════════

        // ── Left column: Game name, Wiki name + Reset, Wiki toggle ──
        gameNameBox = new TextBox
        {
            Header = "Game name (editable)",
            Text = gameName,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x30)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE2, 0xE8, 0xFF)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x32, 0x40)),
            Padding = new Thickness(8, 4, 8, 4),
        };
        wikiNameBox = new TextBox
        {
            Header = "Wiki mod name",
            PlaceholderText = "Exact wiki name",
            Text = ViewModel.GetNameMapping(gameName) ?? "",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x30)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE2, 0xE8, 0xFF)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x32, 0x40)),
            Padding = new Thickness(8, 4, 8, 4),
        };

        var nameResetBtn = new Button
        {
            Content = "↩ Reset",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x30)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x6B, 0x7A, 0x8E)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x32, 0x40)),
        };
        ToolTipService.SetToolTip(nameResetBtn,
            "Reset game name back to auto-detected and clear wiki name mapping.");
        nameResetBtn.Click += (s, ev) =>
        {
            var resetName = (originalStoreName ?? gameName).Trim();
            gameNameBox.Text = resetName;
            wikiNameBox.Text = "";

            // Persist wiki mapping removal
            if (ViewModel.GetNameMapping(_capturedName) != null)
                ViewModel.RemoveNameMapping(_capturedName);

            // Persist rename back to original if name was changed
            if (!resetName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.RenameGame(_capturedName, resetName);
                _capturedName = resetName;
                _window.RequestReselect(resetName);
                card.NotifyAll();
            }
        };

        var leftCol0 = new StackPanel { Spacing = 6 };

        // Row 1: Game name + Wiki name side by side
        var nameRow = new Grid { ColumnSpacing = 8 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(gameNameBox, 0);
        Grid.SetColumn(wikiNameBox, 1);
        nameRow.Children.Add(gameNameBox);
        nameRow.Children.Add(wikiNameBox);
        leftCol0.Children.Add(nameRow);

        // Row 2: Reset button (half, blue accent) + Wiki lookup ComboBox (half)
        var resetWikiRow = new Grid { ColumnSpacing = 8 };
        resetWikiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        resetWikiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        nameResetBtn.Content = "Reset";
        nameResetBtn.FontSize = 12;
        nameResetBtn.Height = 32;
        nameResetBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        nameResetBtn.VerticalAlignment = VerticalAlignment.Stretch;
        nameResetBtn.Padding = new Thickness(10, 6, 10, 6);
        nameResetBtn.Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
        nameResetBtn.Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
        nameResetBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
        nameResetBtn.BorderThickness = new Thickness(1);
        nameResetBtn.CornerRadius = new CornerRadius(8);
        Grid.SetColumn(nameResetBtn, 0);
        resetWikiRow.Children.Add(nameResetBtn);

        var wikiExcludeItems = new[] { "Included", "Excluded" };
        var wikiExcludeCombo = new ComboBox
        {
            ItemsSource = wikiExcludeItems,
            SelectedItem = ViewModel.IsWikiExcluded(gameName) ? "Excluded" : "Included",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(wikiExcludeCombo,
            "Included = this game is looked up on the RenoDX and Luma wikis. Excluded = skip wiki lookups for this game.");
        wikiExcludeCombo.SelectionChanged += (s, ev) =>
        {
            var selected = wikiExcludeCombo.SelectedItem as string;
            bool shouldExclude = selected == "Excluded";
            if (shouldExclude != ViewModel.IsWikiExcluded(_capturedName))
                ViewModel.ToggleWikiExclusion(_capturedName);
        };
        Grid.SetColumn(wikiExcludeCombo, 1);
        resetWikiRow.Children.Add(wikiExcludeCombo);

        leftCol0.Children.Add(resetWikiRow);

        Grid.SetColumn(leftCol0, 0);
        Grid.SetRow(leftCol0, 0);
        mainGrid.Children.Add(leftCol0);

        // ── Right column: DLL naming overrides ──
        bool isDllOverride = ViewModel.HasDllOverride(gameName);
        var existingCfg = ViewModel.GetDllOverride(gameName);
        bool is32Bit = card.Is32Bit;

        dllOverrideToggle = new ToggleSwitch
        {
            Header = "DLL naming overrides",
            IsOn = isDllOverride,
            IsEnabled = true,
            OnContent = "Custom filenames enabled",
            OffContent = "Override DLL filenames",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
        };
        ToolTipService.SetToolTip(dllOverrideToggle,
            "Override the filenames ReShade is installed as. When enabled, existing RS files are renamed to the custom filenames.");
        var existingRsName = existingCfg?.ReShadeFileName ?? "";

        var rsNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = "Select ReShade DLL name",
            Header = (object?)null,
            FontSize = 11,
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

        // ── DC DLL naming override ──
        var existingDcName = existingCfg?.DcFileName ?? "";
        bool isDcDllOverrideOn = isDllOverride && !string.IsNullOrEmpty(existingDcName);

        var dcNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = "Select DC DLL name",
            FontSize = 11,
            IsEnabled = isDcDllOverrideOn,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DetailPanelBuilder.DcDllOverrideNames,
        };
        if (!string.IsNullOrEmpty(existingDcName))
        {
            if (DetailPanelBuilder.DcDllOverrideNames.Contains(existingDcName, StringComparer.OrdinalIgnoreCase))
                dcNameBox.SelectedItem = DetailPanelBuilder.DcDllOverrideNames.First(n => n.Equals(existingDcName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var capturedDc = existingDcName;
                dcNameBox.Loaded += (s, e) => dcNameBox.Text = capturedDc;
            }
        }

        // ── OptiScaler DLL naming override ──
        var existingOsName = existingCfg?.OsFileName ?? "";
        var availableOsNames = ViewModel.DllOverrideServiceInstance
            .GetAvailableOsDllNames(gameName, is32Bit);

        var osNameBox = new ComboBox
        {
            PlaceholderText = "Select OptiScaler DLL name",
            FontSize = 11,
            IsEnabled = isDllOverride,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = availableOsNames,
        };
        if (!string.IsNullOrEmpty(existingOsName))
        {
            if (availableOsNames.Contains(existingOsName, StringComparer.OrdinalIgnoreCase))
                osNameBox.SelectedItem = availableOsNames.First(n => n.Equals(existingOsName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var extendedOsNames = availableOsNames.Append(existingOsName).ToArray();
                osNameBox.ItemsSource = extendedOsNames;
                osNameBox.SelectedItem = existingOsName;
            }
        }

        // Track previous DC/OS selections for revert on foreign DLL conflict cancel
        string? _previousDcSelection = dcNameBox.SelectedItem as string;
        string? _previousOsSelection = osNameBox.SelectedItem as string;

        // ── Cross-exclusion: filter out the other component's current name ──
        bool _updatingDropdowns = false;

        void UpdateDcDropdownItems()
        {
            if (_updatingDropdowns) return;
            _updatingDropdowns = true;
            try
            {
                var rsCurrentName = dllOverrideToggle.IsOn
                    ? (rsNameBox.SelectedItem as string ?? rsNameBox.Text ?? "").Trim()
                    : Services.AuxInstallService.RsNormalName;
                var filtered = string.IsNullOrEmpty(rsCurrentName)
                    ? DetailPanelBuilder.DcDllOverrideNames
                    : DetailPanelBuilder.DcDllOverrideNames.Where(n => !n.Equals(rsCurrentName, StringComparison.OrdinalIgnoreCase)).ToArray();
                var currentDc = dcNameBox.SelectedItem as string;
                dcNameBox.ItemsSource = filtered;
                if (currentDc != null && filtered.Contains(currentDc, StringComparer.OrdinalIgnoreCase))
                    dcNameBox.SelectedItem = filtered.First(n => n.Equals(currentDc, StringComparison.OrdinalIgnoreCase));
            }
            finally { _updatingDropdowns = false; }
        }

        void UpdateRsDropdownItems()
        {
            if (_updatingDropdowns) return;
            _updatingDropdowns = true;
            try
            {
                var dcCurrentName = dllOverrideToggle.IsOn
                    ? (dcNameBox.SelectedItem as string ?? "").Trim()
                    : "";
                var filtered = string.IsNullOrEmpty(dcCurrentName)
                    ? DllOverrideConstants.CommonDllNames
                    : DllOverrideConstants.CommonDllNames.Where(n => !n.Equals(dcCurrentName, StringComparison.OrdinalIgnoreCase)).ToArray();
                var currentRs = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                rsNameBox.ItemsSource = filtered;
                if (!string.IsNullOrEmpty(currentRs) && filtered.Contains(currentRs, StringComparer.OrdinalIgnoreCase))
                    rsNameBox.SelectedItem = filtered.First(n => n.Equals(currentRs, StringComparison.OrdinalIgnoreCase));
            }
            finally { _updatingDropdowns = false; }
        }

        // Initial filter
        UpdateDcDropdownItems();
        UpdateRsDropdownItems();

        dllOverrideToggle.Toggled += (s, ev) =>
        {
            rsNameBox.IsEnabled = dllOverrideToggle.IsOn;
            dcNameBox.IsEnabled = dllOverrideToggle.IsOn;
            osNameBox.IsEnabled = dllOverrideToggle.IsOn;

            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;

            if (dllOverrideToggle.IsOn)
            {
                // Turning unified override ON
                var existingCfgNow = ViewModel.GetDllOverride(_capturedName);

                string rsName;
                string dcName;

                if (existingCfgNow != null
                    && (!string.IsNullOrEmpty(existingCfgNow.ReShadeFileName) || !string.IsNullOrEmpty(existingCfgNow.DcFileName)))
                {
                    // Prior config exists — restore saved filenames
                    rsName = existingCfgNow.ReShadeFileName ?? "";
                    dcName = existingCfgNow.DcFileName ?? "";

                    // Restore RS dropdown
                    if (!string.IsNullOrEmpty(rsName))
                    {
                        if (DllOverrideConstants.CommonDllNames.Contains(rsName, StringComparer.OrdinalIgnoreCase))
                        {
                            rsNameBox.SelectedItem = DllOverrideConstants.CommonDllNames
                                .First(n => n.Equals(rsName, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            var extended = DllOverrideConstants.CommonDllNames.Append(rsName).ToArray();
                            rsNameBox.ItemsSource = extended;
                            rsNameBox.SelectedItem = rsName;
                        }
                    }

                    // Restore DC dropdown
                    if (!string.IsNullOrEmpty(dcName))
                    {
                        if (DetailPanelBuilder.DcDllOverrideNames.Contains(dcName, StringComparer.OrdinalIgnoreCase))
                        {
                            dcNameBox.SelectedItem = DetailPanelBuilder.DcDllOverrideNames
                                .First(n => n.Equals(dcName, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            var extendedDc = DetailPanelBuilder.DcDllOverrideNames.Append(dcName).ToArray();
                            dcNameBox.ItemsSource = extendedDc;
                            dcNameBox.SelectedItem = dcName;
                        }
                    }
                }
                else
                {
                    // No prior config — auto-select safe defaults
                    rsName = targetCard.Is32Bit
                        ? Services.AuxInstallService.RsStaged32
                        : Services.AuxInstallService.RsStaged64;

                    if (DllOverrideConstants.CommonDllNames.Contains(rsName, StringComparer.OrdinalIgnoreCase))
                    {
                        rsNameBox.SelectedItem = DllOverrideConstants.CommonDllNames
                            .First(n => n.Equals(rsName, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        var extended = DllOverrideConstants.CommonDllNames.Append(rsName).ToArray();
                        rsNameBox.ItemsSource = extended;
                        rsNameBox.SelectedItem = rsName;
                    }

                    dcName = MainViewModel.GetDcFileName(targetCard.Is32Bit);

                    if (DetailPanelBuilder.DcDllOverrideNames.Contains(dcName, StringComparer.OrdinalIgnoreCase))
                    {
                        dcNameBox.SelectedItem = DetailPanelBuilder.DcDllOverrideNames
                            .First(n => n.Equals(dcName, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        var extendedDc = DetailPanelBuilder.DcDllOverrideNames.Append(dcName).ToArray();
                        dcNameBox.ItemsSource = extendedDc;
                        dcNameBox.SelectedItem = dcName;
                    }
                }

                ViewModel.EnableDllOverride(targetCard, rsName, dcName);
            }
            else
            {
                // Turning unified override OFF — delegate to service for both RS and DC revert
                var result = ViewModel.DisableDllOverride(targetCard);

                // Disable and clear both dropdowns
                rsNameBox.SelectedIndex = -1;
                if (rsNameBox.IsEditable) rsNameBox.Text = "";
                dcNameBox.SelectedIndex = -1;
                if (dcNameBox.IsEditable) dcNameBox.Text = "";

                // Set tooltips for partial revert failures
                if (!result.RsReverted)
                {
                    ToolTipService.SetToolTip(dllOverrideToggle,
                        "Could not revert ReShade to dxgi.dll — the filename is occupied by another file. ReShade was renamed to a fallback name instead.");
                }
                else if (!result.DcReverted)
                {
                    ToolTipService.SetToolTip(dllOverrideToggle,
                        "Could not revert Display Commander to its default name — the filename is occupied by another file. DC was kept under its current name.");
                }
                else
                {
                    // Both reverted successfully — reset tooltip to default
                    ToolTipService.SetToolTip(dllOverrideToggle,
                        "Override the filenames ReShade is installed as. When enabled, existing RS files are renamed to the custom filenames.");
                }
            }
        };

        // ── Auto-save: DC name box on dropdown selection (with foreign DLL check) ──
        dcNameBox.SelectionChanged += async (s, e) =>
        {
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var dcName = dcNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(dcName)) return;

            // Check for foreign DLL conflict before proceeding
            bool allowed = await ViewModel.DllOverrideServiceInstance
                .CheckDcForeignDllConflictAsync(targetCard, dcName);
            if (!allowed)
            {
                // Revert dropdown to previous selection
                if (_previousDcSelection != null)
                    dcNameBox.SelectedItem = DetailPanelBuilder.DcDllOverrideNames.FirstOrDefault(n =>
                        n.Equals(_previousDcSelection, StringComparison.OrdinalIgnoreCase));
                else
                    dcNameBox.SelectedIndex = -1;
                return;
            }

            _previousDcSelection = dcName;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            UpdateRsDropdownItems();
        };

        // ── Auto-save: DC name box on Enter (manual typed name) ──
        dcNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var dcName = (dcNameBox.SelectedItem as string ?? dcNameBox.Text)?.Trim();
            if (string.IsNullOrWhiteSpace(dcName)) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            UpdateRsDropdownItems();
        };

        // ── Auto-save: OS name box on dropdown selection ──
        osNameBox.SelectionChanged += (s, e) =>
        {
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var osName = osNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(osName)) return;

            _previousOsSelection = osName;
            ViewModel.DllOverrideServiceInstance.SetOsDllOverride(_capturedName, osName);

            // If OptiScaler is installed, rename the DLL in the game folder
            if (targetCard.IsOsInstalled && !string.IsNullOrEmpty(targetCard.OsInstalledFile)
                && !string.IsNullOrEmpty(targetCard.InstallPath))
            {
                var oldPath = System.IO.Path.Combine(targetCard.InstallPath, targetCard.OsInstalledFile);
                var newPath = System.IO.Path.Combine(targetCard.InstallPath, osName);
                try
                {
                    if (System.IO.File.Exists(oldPath)
                        && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);
                        System.IO.File.Move(oldPath, newPath);
                        targetCard.OsInstalledFile = osName;

                        // Update the tracking record
                        var osRecord = ViewModel.AuxInstallServiceInstance
                            .FindRecord(_capturedName, targetCard.InstallPath, "OptiScaler");
                        if (osRecord != null)
                        {
                            osRecord.InstalledAs = osName;
                            ViewModel.AuxInstallServiceInstance.SaveAuxRecord(osRecord);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[OverridesFlyoutBuilder] Failed to rename OS DLL for '{_capturedName}' — {ex.Message}");
                }
            }
        };

        // ── Auto-save: RS name box on Enter ──
        rsNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            if (string.IsNullOrEmpty(rsName)) return;
            var dcName = dllOverrideToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "").Trim() : "";

            if (ViewModel.HasDllOverride(_capturedName))
                ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            else
                ViewModel.EnableDllOverride(targetCard, rsName, dcName);
        };

        // ── Auto-save: RS name box on dropdown selection ──
        rsNameBox.SelectionChanged += (s, e) =>
        {
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsName = rsNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(rsName)) return;
            var dcName = dllOverrideToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "") : "";

            if (ViewModel.HasDllOverride(_capturedName))
                ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            else
                ViewModel.EnableDllOverride(targetCard, rsName, dcName);

            UpdateDcDropdownItems();
        };

        var rightCol0 = new StackPanel { Spacing = 4 };
        rightCol0.Children.Add(dllOverrideToggle);

        // 3 DLL name boxes side by side, hidden when toggle is off
        var dllBoxesGrid = new Grid { ColumnSpacing = 8, Visibility = isDllOverride ? Visibility.Visible : Visibility.Collapsed };
        dllBoxesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dllBoxesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dllBoxesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(rsNameBox, 0);
        Grid.SetColumn(dcNameBox, 1);
        Grid.SetColumn(osNameBox, 2);
        dllBoxesGrid.Children.Add(rsNameBox);
        dllBoxesGrid.Children.Add(dcNameBox);
        dllBoxesGrid.Children.Add(osNameBox);
        rightCol0.Children.Add(dllBoxesGrid);

        dllOverrideToggle.Toggled += (s, ev) =>
        {
            dllBoxesGrid.Visibility = dllOverrideToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        };

        Grid.SetColumn(rightCol0, 2);
        Grid.SetRow(rightCol0, 0);
        mainGrid.Children.Add(rightCol0);

        // ── Auto-save: Game name on Enter ──
        gameNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var det = gameNameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(det)) return;
            if (det.Equals(_capturedName, StringComparison.OrdinalIgnoreCase)) return;
            ViewModel.RenameGame(_capturedName, det);
            _window.RequestReselect(det);
            card.NotifyAll();
            _capturedName = det;
        };

        // ── Auto-save: Wiki name on Enter ──
        wikiNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var key = wikiNameBox.Text?.Trim();
            if (!string.IsNullOrEmpty(key))
            {
                var existing = ViewModel.GetNameMapping(_capturedName);
                if (!key.Equals(existing, StringComparison.OrdinalIgnoreCase))
                    ViewModel.AddNameMapping(_capturedName, key);
            }
            else
            {
                if (ViewModel.GetNameMapping(_capturedName) != null)
                    ViewModel.RemoveNameMapping(_capturedName);
            }
        };

        // ══════════════════════════════════════════════════════════════════════
        // ROW 1 — Horizontal separator
        // ══════════════════════════════════════════════════════════════════════
        var sep1 = UIFactory.MakeSeparator();
        Grid.SetColumn(sep1, 0);
        Grid.SetRow(sep1, 1);
        Grid.SetColumnSpan(sep1, 3);
        mainGrid.Children.Add(sep1);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 2 — Left: Bitness + Graphics API (side by side)
        //         Right: Update Inclusion button + summary
        // ══════════════════════════════════════════════════════════════════════

        // ── Bitness & API Override ──
        var bitnessItems = new[] { "Auto", "32-bit", "64-bit" };
        var currentBitnessOverride = ViewModel.GetBitnessOverride(gameName);
        var defaultBitnessSelection = currentBitnessOverride switch
        {
            "32" => "32-bit",
            "64" => "64-bit",
            _ => "Auto",
        };

        bitnessCombo = new ComboBox
        {
            ItemsSource = bitnessItems,
            SelectedItem = defaultBitnessSelection,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(bitnessCombo,
            "Override the auto-detected bitness for this game.");

        bitnessCombo.SelectionChanged += (s, e) =>
        {
            var selected = bitnessCombo.SelectedItem as string;
            string? overrideValue = selected switch
            {
                "32-bit" => "32",
                "64-bit" => "64",
                _ => null,
            };
            ViewModel.SetBitnessOverride(_capturedName, overrideValue);
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
            {
                var previousIs32Bit = targetCard.Is32Bit;

                // Compute the new effective bitness
                bool newIs32Bit;
                if (overrideValue == "32") newIs32Bit = true;
                else if (overrideValue == "64") newIs32Bit = false;
                else
                {
                    var detectedMachine = ViewModel.PeHeaderServiceInstance.DetectGameArchitecture(targetCard.InstallPath);
                    newIs32Bit = ViewModel.ResolveIs32Bit(_capturedName, detectedMachine);
                }

                // If bitness actually changed, uninstall all components BEFORE updating card.Is32Bit
                // (uninstall methods use card.Is32Bit to resolve filenames of deployed DLLs)
                if (previousIs32Bit != newIs32Bit && !targetCard.RequiresVulkanInstall)
                {
                    if (targetCard.IsRsInstalled)
                        ViewModel.UninstallReShade(targetCard);
                    if (targetCard.DcStatus == GameStatus.Installed)
                        ViewModel.UninstallDc(targetCard);
                    if (targetCard.InstalledRecord != null)
                        ViewModel.UninstallMod(targetCard);
                    if (targetCard.UlStatus == GameStatus.Installed)
                        ViewModel.UninstallUl(targetCard);
                    if (targetCard.OsStatus == GameStatus.Installed)
                        ViewModel.OptiScalerServiceInstance.Uninstall(targetCard);
                    if (targetCard.DxvkStatus == GameStatus.Installed)
                        ViewModel.UninstallDxvk(targetCard);
                    if (targetCard.RefStatus == GameStatus.Installed)
                        ViewModel.UninstallREFramework(targetCard);
                    if (targetCard.LumaStatus == GameStatus.Installed)
                        ViewModel.UninstallLuma(targetCard);
                }

                // NOW update card.Is32Bit to the new value
                targetCard.Is32Bit = newIs32Bit;
                targetCard.NotifyAll();
            }
        };

        var apiDropdownItems = new[] { "Auto", "DirectX8", "DirectX9", "DirectX10", "DX11/DX12", "Vulkan", "OpenGL" };
        var existingApiOverride = ViewModel.GetApiOverride(gameName);
        string defaultApiSelection = "Auto";
        if (existingApiOverride != null && existingApiOverride.Count > 0)
        {
            if (existingApiOverride.Contains("DirectX11", StringComparer.OrdinalIgnoreCase)
                || existingApiOverride.Contains("DirectX12", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "DX11/DX12";
            else if (existingApiOverride.Contains("Vulkan", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "Vulkan";
            else if (existingApiOverride.Contains("OpenGL", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "OpenGL";
            else if (existingApiOverride.Contains("DirectX10", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "DirectX10";
            else if (existingApiOverride.Contains("DirectX9", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "DirectX9";
            else if (existingApiOverride.Contains("DirectX8", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "DirectX8";
        }

        apiCombo = new ComboBox
        {
            ItemsSource = apiDropdownItems,
            SelectedItem = defaultApiSelection,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        apiCombo.SelectionChanged += (s, ev) =>
        {
            var selected = apiCombo.SelectedItem as string;
            List<string>? apiEnumNames = selected switch
            {
                "DirectX8" => new() { "DirectX8" },
                "DirectX9" => new() { "DirectX9" },
                "DirectX10" => new() { "DirectX10" },
                "DX11/DX12" => new() { "DirectX11", "DirectX12" },
                "Vulkan" => new() { "Vulkan" },
                "OpenGL" => new() { "OpenGL" },
                _ => null,
            };
            ViewModel.SetApiOverride(_capturedName, apiEnumNames);
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
            {
                if (apiEnumNames != null)
                {
                    var newApis = new HashSet<GraphicsApiType>();
                    foreach (var name in apiEnumNames)
                        if (Enum.TryParse<GraphicsApiType>(name, out var apiType)) newApis.Add(apiType);
                    targetCard.DetectedApis = newApis;
                }
                else
                {
                    targetCard.DetectedApis = ViewModel._DetectAllApisForCard(targetCard.InstallPath, _capturedName);
                }
                targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
                targetCard.GraphicsApi = ViewModel.DetectGraphicsApi(targetCard.InstallPath, EngineType.Unknown, _capturedName);
                targetCard.NotifyAll();
            }
        };

        var bitnessApiRow = new Grid { ColumnSpacing = 8 };
        bitnessApiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bitnessApiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bitnessApiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bitnessPanel = new StackPanel { Spacing = 4 };
        bitnessPanel.Children.Add(new TextBlock { Text = "Bitness", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });
        bitnessPanel.Children.Add(bitnessCombo);
        Grid.SetColumn(bitnessPanel, 0);

        var apiPanel = new StackPanel { Spacing = 4 };
        apiPanel.Children.Add(new TextBlock { Text = "Graphics API", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });
        apiPanel.Children.Add(apiCombo);
        Grid.SetColumn(apiPanel, 1);



        // Continuation: build Rows 4-6 (Shaders, Addons, DXVK, Launch), reset handler, and show flyout
        BuildShadersAddonsDxvkAndShowFlyout(
            card, anchor, mainGrid, panel, verticalDivider,
            resetOverridesLink, gameNameBox, wikiNameBox, dllOverrideToggle,
            bitnessCombo, apiCombo, wikiExcludeCombo,
            updateSummaryText, originalStoreName, gameName, isLumaMode,
            bitnessApiRow, bitnessPanel, apiPanel);
    }
}
