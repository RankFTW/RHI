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
public class OverridesFlyoutBuilder
{
    private readonly MainWindow _window;
    private readonly ICrashReporter _crashReporter;

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
        var capturedName = gameName;
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
            if (ViewModel.GetNameMapping(capturedName) != null)
                ViewModel.RemoveNameMapping(capturedName);

            // Persist rename back to original if name was changed
            if (!resetName.Equals(capturedName, StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.RenameGame(capturedName, resetName);
                capturedName = resetName;
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
            if (shouldExclude != ViewModel.IsWikiExcluded(capturedName))
                ViewModel.ToggleWikiExclusion(capturedName);
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
            IsEnabled = !isLumaMode,
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
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;

            if (dllOverrideToggle.IsOn)
            {
                // Turning unified override ON
                var existingCfgNow = ViewModel.GetDllOverride(capturedName);

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
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
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
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
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
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var osName = osNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(osName)) return;

            _previousOsSelection = osName;
            ViewModel.DllOverrideServiceInstance.SetOsDllOverride(capturedName, osName);

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
                            .FindRecord(capturedName, targetCard.InstallPath, "OptiScaler");
                        if (osRecord != null)
                        {
                            osRecord.InstalledAs = osName;
                            ViewModel.AuxInstallServiceInstance.SaveAuxRecord(osRecord);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[OverridesFlyoutBuilder] Failed to rename OS DLL for '{capturedName}' — {ex.Message}");
                }
            }
        };

        // ── Auto-save: RS name box on Enter ──
        rsNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            if (string.IsNullOrEmpty(rsName)) return;
            var dcName = dllOverrideToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "").Trim() : "";

            if (ViewModel.HasDllOverride(capturedName))
                ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            else
                ViewModel.EnableDllOverride(targetCard, rsName, dcName);
        };

        // ── Auto-save: RS name box on dropdown selection ──
        rsNameBox.SelectionChanged += (s, e) =>
        {
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsName = rsNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(rsName)) return;
            var dcName = dllOverrideToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "") : "";

            if (ViewModel.HasDllOverride(capturedName))
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
            if (det.Equals(capturedName, StringComparison.OrdinalIgnoreCase)) return;
            ViewModel.RenameGame(capturedName, det);
            _window.RequestReselect(det);
            card.NotifyAll();
            capturedName = det;
        };

        // ── Auto-save: Wiki name on Enter ──
        wikiNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var key = wikiNameBox.Text?.Trim();
            if (!string.IsNullOrEmpty(key))
            {
                var existing = ViewModel.GetNameMapping(capturedName);
                if (!key.Equals(existing, StringComparison.OrdinalIgnoreCase))
                    ViewModel.AddNameMapping(capturedName, key);
            }
            else
            {
                if (ViewModel.GetNameMapping(capturedName) != null)
                    ViewModel.RemoveNameMapping(capturedName);
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
            ViewModel.SetBitnessOverride(capturedName, overrideValue);
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
            {
                if (overrideValue == "32") targetCard.Is32Bit = true;
                else if (overrideValue == "64") targetCard.Is32Bit = false;
                else
                {
                    var detectedMachine = ViewModel.PeHeaderServiceInstance.DetectGameArchitecture(targetCard.InstallPath);
                    targetCard.Is32Bit = ViewModel.ResolveIs32Bit(capturedName, detectedMachine);
                }
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
            ViewModel.SetApiOverride(capturedName, apiEnumNames);
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
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
                    targetCard.DetectedApis = ViewModel._DetectAllApisForCard(targetCard.InstallPath, capturedName);
                }
                targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
                targetCard.GraphicsApi = ViewModel.DetectGraphicsApi(targetCard.InstallPath, EngineType.Unknown, capturedName);
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

        // ── ReShade Channel Override ──
        var channelItems = new[] { "Global", "Stable", "Nightly", "Custom", "Legacy..." };
        // For Vulkan games, show the effective Vulkan-wide override (any Vulkan game's override applies to all)
        var currentChannelOverride = ViewModel.GetReShadeChannelOverride(capturedName);
        if (currentChannelOverride == null && card.RequiresVulkanInstall)
        {
            // Check if any other Vulkan game has an override — if so, this game effectively has the same
            currentChannelOverride = ViewModel.AllCards
                .Where(c => c.RequiresVulkanInstall && c.GameName != capturedName)
                .Select(c => ViewModel.GetReShadeChannelOverride(c.GameName))
                .FirstOrDefault(ch => ch != null);
        }

        // If a legacy version is active, add it to the dropdown items
        var channelItemsList = new List<string>(channelItems);
        string defaultChannelSelection;
        if (string.Equals(currentChannelOverride, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            defaultChannelSelection = "Custom";
        }
        else if (MainViewModel.IsLegacyVersion(currentChannelOverride))
        {
            channelItemsList.Insert(channelItemsList.IndexOf("Legacy..."), currentChannelOverride!);
            defaultChannelSelection = currentChannelOverride!;
        }
        else
        {
            defaultChannelSelection = currentChannelOverride switch
            {
                "Stable" => "Stable",
                "Nightly" => "Nightly",
                _ => "Global",
            };
        }

        var channelCombo = new ComboBox
        {
            ItemsSource = channelItemsList,
            SelectedItem = defaultChannelSelection,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(channelCombo,
            "Override the global ReShade build channel for this game.\nVulkan games: changing this affects ALL Vulkan games.");

        channelCombo.SelectionChanged += async (s, ev) =>
        {
            var selected = channelCombo.SelectedItem as string;

            // ── "Legacy..." opens the version picker dialog ──
            if (selected == "Legacy...")
            {
                var legacyVersions = ViewModel.Manifest?.LegacyReShadeAvailable;
                if (legacyVersions == null || legacyVersions.Count == 0)
                {
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }

                // Build radio button list
                var radioButtons = new RadioButtons { MaxColumns = 1 };
                foreach (var ver in legacyVersions)
                    radioButtons.Items.Add(ver);

                var pickerContent = new StackPanel { Spacing = 12 };
                pickerContent.Children.Add(new TextBlock
                {
                    Text = "⚠ Older ReShade versions may not support newer addons.\nThe game will be excluded from automatic ReShade updates.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                    FontSize = 12,
                });
                pickerContent.Children.Add(radioButtons);

                var pickerDialog = new ContentDialog
                {
                    Title = "Select Legacy ReShade Version",
                    Content = new ScrollViewer { Content = pickerContent, MaxHeight = 400 },
                    PrimaryButtonText = "Confirm",
                    CloseButtonText = "Cancel",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };

                var pickerResult = await DialogService.ShowSafeAsync(pickerDialog);
                if (pickerResult != ContentDialogResult.Primary || radioButtons.SelectedItem is not string pickedVersion)
                {
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }

                // Download the legacy version if not cached
                if (!AuxInstallService.IsLegacyVersionCached(pickedVersion))
                {
                    var success = await AuxInstallService.DownloadLegacyReShadeAsync(pickedVersion, ViewModel.HttpClient);
                    if (!success)
                    {
                        channelCombo.SelectedItem = defaultChannelSelection;
                        return;
                    }
                }

                // Set the override and reinstall
                ViewModel.SetReShadeChannelOverride(capturedName, pickedVersion);

                // Update dropdown: remove old legacy version, add new one
                var oldLegacy = channelItemsList.FirstOrDefault(v => MainViewModel.IsLegacyVersion(v) && v != "Legacy...");
                if (oldLegacy != null) channelItemsList.Remove(oldLegacy);
                if (!channelItemsList.Contains(pickedVersion))
                    channelItemsList.Insert(3, pickedVersion);
                channelCombo.ItemsSource = channelItemsList;
                channelCombo.SelectedItem = pickedVersion;
                defaultChannelSelection = pickedVersion;

                var targetCard2 = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard2 != null && targetCard2.IsRsInstalled)
                    await ViewModel.InstallReShadeCommand.ExecuteAsync(targetCard2);

                ViewModel.NotifyUpdateButtonChanged();
                return;
            }

            // ── If a legacy version is already selected and user picks it again, do nothing ──
            if (MainViewModel.IsLegacyVersion(selected) && selected != "Legacy..." && selected != "Custom")
            {
                return;
            }

            // ── "Custom" — use user-supplied ReShade DLLs ──
            if (selected == "Custom")
            {
                CrashReporter.Log($"[OverridesFlyoutBuilder.RSChannel] '{capturedName}' → Custom ReShade");

                if (!AuxInstallService.IsCustomReShadeAvailable())
                {
                    var customPath = DlssStreamlineService.RsCustomDir;
                    var linkBtn = new HyperlinkButton
                    {
                        Content = customPath,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 12,
                        Padding = new Thickness(0),
                    };
                    linkBtn.Click += (_, _) =>
                    {
                        System.IO.Directory.CreateDirectory(customPath);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(customPath) { UseShellExecute = true });
                    };
                    var warnContent = new StackPanel { Spacing = 8 };
                    warnContent.Children.Add(new TextBlock
                    {
                        Text = "No custom ReShade DLLs found.\n\nPlace your ReShade64.dll and/or ReShade32.dll in:",
                        TextWrapping = TextWrapping.Wrap,
                    });
                    warnContent.Children.Add(linkBtn);

                    var warnDialog = new ContentDialog
                    {
                        Title = "Custom ReShade Not Found",
                        Content = warnContent,
                        CloseButtonText = "OK",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    };
                    await DialogService.ShowSafeAsync(warnDialog);
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }

                var targetCardCustom = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));

                // Vulkan game: affects ALL Vulkan games
                if (targetCardCustom?.RequiresVulkanInstall == true)
                {
                    var vDialog = new ContentDialog
                    {
                        Title = "Vulkan ReShade Channel Override",
                        Content = "Vulkan games share a global ReShade layer.\n\n" +
                            "Changing the channel for this game will change it for ALL Vulkan games.",
                        PrimaryButtonText = "Apply to All Vulkan Games",
                        CloseButtonText = "Cancel",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    };
                    var vResult = await DialogService.ShowSafeAsync(vDialog);
                    if (vResult != ContentDialogResult.Primary)
                    {
                        channelCombo.SelectedItem = defaultChannelSelection;
                        return;
                    }

                    // Apply to ALL Vulkan games
                    foreach (var vCard in ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
                    {
                        ViewModel.SetReShadeChannelOverride(vCard.GameName, "Custom");
                        vCard.NotifyAll();
                    }

                    // Copy custom DLL to ProgramData Vulkan layer
                    try
                    {
                        var layerDir = VulkanLayerService.LayerDirectory;
                        var stagedPath64 = AuxInstallService.GetCustomReShadePathStatic(false);
                        var stagedPath32 = AuxInstallService.GetCustomReShadePathStatic(true);
                        var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);
                        var layer32 = Path.Combine(layerDir, "ReShade32.dll");

                        if (File.Exists(stagedPath64) && File.Exists(layer64))
                            AuxInstallService.CopyFileWithElevation(stagedPath64, layer64);
                        if (File.Exists(stagedPath32) && File.Exists(layer32))
                            AuxInstallService.CopyFileWithElevation(stagedPath32, layer32);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[OverridesFlyoutBuilder] Failed to update Vulkan layer with custom ReShade — {ex.Message}");
                    }
                }
                else
                {
                    // Non-Vulkan: per-game only
                    ViewModel.SetReShadeChannelOverride(capturedName, "Custom");
                    if (targetCardCustom != null && targetCardCustom.IsRsInstalled)
                        await ViewModel.InstallReShadeCommand.ExecuteAsync(targetCardCustom);
                }

                defaultChannelSelection = "Custom";
                ViewModel.NotifyUpdateButtonChanged();
                return;
            }

            string? channelValue = selected switch
            {
                "Stable" => "Stable",
                "Nightly" => "Nightly",
                _ => null,
            };

            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));

            // ── Vulkan game: affects ALL Vulkan games ──
            if (targetCard?.RequiresVulkanInstall == true)
            {
                if (channelValue != null)
                {
                    // Setting a specific override on a Vulkan game
                    var dialog = new ContentDialog
                    {
                        Title = "Vulkan ReShade Channel Override",
                        Content = "Vulkan games share a global ReShade layer.\n\n" +
                            "Changing the channel for this game will change it for ALL Vulkan games.",
                        PrimaryButtonText = "Apply to All Vulkan Games",
                        CloseButtonText = "Cancel",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    };
                    var result = await DialogService.ShowSafeAsync(dialog);
                    if (result != ContentDialogResult.Primary)
                    {
                        channelCombo.SelectedItem = defaultChannelSelection;
                        return;
                    }
                }

                // Apply override (or clear it) on ALL Vulkan games
                foreach (var vCard in ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
                {
                    ViewModel.SetReShadeChannelOverride(vCard.GameName, channelValue);
                    vCard.NotifyAll();
                }

                // Determine the effective channel for the Vulkan layer
                var effectiveChannel = channelValue ?? ViewModel.Settings.ReShadeChannel;

                // Update the Vulkan layer DLLs
                try
                {
                    var layerDir = VulkanLayerService.LayerDirectory;
                    var stagedPath64 = AuxInstallService.GetStagedPathForChannel(effectiveChannel, false);
                    var stagedPath32 = AuxInstallService.GetStagedPathForChannel(effectiveChannel, true);
                    var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);
                    var layer32 = Path.Combine(layerDir, "ReShade32.dll");

                    CrashReporter.Log($"[OverridesFlyoutBuilder] Vulkan channel override → {effectiveChannel}: staged64={stagedPath64} (exists={File.Exists(stagedPath64)}), layer64={layer64} (exists={File.Exists(layer64)})");

                    if (File.Exists(stagedPath64) && new FileInfo(stagedPath64).Length > AuxInstallService.MinReShadeSize && File.Exists(layer64))
                        AuxInstallService.CopyFileWithElevation(stagedPath64, layer64);
                    else
                        CrashReporter.Log($"[OverridesFlyoutBuilder] Skipped 64-bit copy: staged exists={File.Exists(stagedPath64)}, size={(File.Exists(stagedPath64) ? new FileInfo(stagedPath64).Length : 0)}, layer exists={File.Exists(layer64)}");

                    if (File.Exists(stagedPath32) && new FileInfo(stagedPath32).Length > AuxInstallService.MinReShadeSize && File.Exists(layer32))
                        AuxInstallService.CopyFileWithElevation(stagedPath32, layer32);

                    CrashReporter.Log($"[OverridesFlyoutBuilder] Updated Vulkan layer to {effectiveChannel}");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[OverridesFlyoutBuilder] Failed to update Vulkan layer — {ex.Message}");
                }

                // Mark all Vulkan games as installed (layer updated in-place)
                foreach (var vCard in ViewModel.AllCards.Where(c => c.RequiresVulkanInstall && c.IsRsInstalled))
                {
                    vCard.RsStatus = GameStatus.Installed;
                    vCard.NotifyAll();
                }
            }
            else
            {
                // ── Non-Vulkan game: per-game only ──
                ViewModel.SetReShadeChannelOverride(capturedName, channelValue);

                // Auto-reinstall ReShade with the new channel if it's currently installed
                if (targetCard != null && targetCard.IsRsInstalled)
                {
                    await ViewModel.InstallReShadeCommand.ExecuteAsync(targetCard);
                }
            }

            ViewModel.NotifyUpdateButtonChanged();
        };

        var channelPanel = new StackPanel { Spacing = 4 };
        channelPanel.Children.Add(new TextBlock { Text = "RS Channel", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });
        channelPanel.Children.Add(channelCombo);
        Grid.SetColumn(channelPanel, 2);

        bitnessApiRow.Children.Add(bitnessPanel);
        bitnessApiRow.Children.Add(apiPanel);
        bitnessApiRow.Children.Add(channelPanel);

        Grid.SetColumn(bitnessApiRow, 0);
        Grid.SetRow(bitnessApiRow, 2);
        mainGrid.Children.Add(bitnessApiRow);

        // ── Right column: Update Inclusion button + summary ──
        var (updateInclusionBtn, updateSummaryText_) = UpdateInclusionHelper.CreateUpdateInclusionControls(
            ViewModel, capturedName, card.IsREEngineGame, _window.Content.XamlRoot,
            onSaved: () =>
            {
                // Rebuild the detail panel if the same game is selected, so component
                // rows reflect the new exclusion state immediately
                if (ViewModel.SelectedGame is { } sel
                    && sel.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase))
                {
                    _window.PopulateDetailPanel(sel);
                    _window.BuildOverridesPanel(sel);
                }
            },
            isDxvkEnabled: card.DxvkEnabled);
        updateSummaryText = updateSummaryText_;

        var globalUpdateColumn = new StackPanel { Spacing = 4 };
        globalUpdateColumn.Children.Add(new TextBlock
        {
            Text = "Global update inclusion",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        globalUpdateColumn.Children.Add(updateInclusionBtn);
        globalUpdateColumn.Children.Add(updateSummaryText);

        Grid.SetColumn(globalUpdateColumn, 2);
        Grid.SetRow(globalUpdateColumn, 2);
        mainGrid.Children.Add(globalUpdateColumn);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 3 — Horizontal separator
        // ══════════════════════════════════════════════════════════════════════
        var sep2 = UIFactory.MakeSeparator();
        Grid.SetColumn(sep2, 0);
        Grid.SetRow(sep2, 3);
        Grid.SetColumnSpan(sep2, 3);
        mainGrid.Children.Add(sep2);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 4 — Left: Shaders (Global + Custom toggles + Select btn)
        //         Right: Addons (Global toggle + Select btn)
        // ══════════════════════════════════════════════════════════════════════

        // ── Shader Mode ComboBox ──
        string currentShaderMode = ViewModel.GetPerGameShaderMode(gameName);
        string effectiveShaderDisplay = currentShaderMode;
        if (currentShaderMode == "Global"
            && ViewModel.Settings.UseCustomShaders
            && !ViewModel.GameNameServiceInstance.PerGameShaderMode.ContainsKey(gameName))
            effectiveShaderDisplay = "Custom";

        var shaderModeItems = new[] { "Global", "Custom", "Select", "Off" };
        bool shaderComboInitializing = true;

        var shaderModeCombo = new ComboBox
        {
            ItemsSource = shaderModeItems,
            SelectedItem = effectiveShaderDisplay,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !card.UseNormalReShade,
        };
        ToolTipService.SetToolTip(shaderModeCombo,
            "Global = use global shader selection. Custom = use custom shader directories. Select = pick per-game packs. Off = no shaders.");

        shaderModeCombo.SelectionChanged += async (s, ev) =>
        {
            if (shaderComboInitializing) return;
            var selected = shaderModeCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            if (selected == "Select")
            {
                List<string>? current = ViewModel.GameNameServiceInstance.PerGameShaderSelection.TryGetValue(gameName, out var existing)
                    ? existing
                    : ViewModel.Settings.SelectedShaderPacks;
                var result = await ViewModel.ShowPerGameShaderSelectionPicker?.Invoke(gameName, current)!;
                if (result != null)
                {
                    ViewModel.GameNameServiceInstance.PerGameShaderSelection[gameName] = result;
                    ViewModel.SetPerGameShaderMode(capturedName, "Select");
                    ViewModel.DeployShadersForCard(capturedName);
                }
                else
                {
                    shaderComboInitializing = true;
                    shaderModeCombo.SelectedItem = effectiveShaderDisplay;
                    shaderComboInitializing = false;
                }
                return;
            }

            if (selected == "Off")
                ViewModel.SetPerGameShaderMode(capturedName, "Off");
            else if (selected == "Custom")
                ViewModel.SetPerGameShaderMode(capturedName, "Custom");
            else
                ViewModel.SetPerGameShaderMode(capturedName, "Global");
            ViewModel.DeployShadersForCard(capturedName);
            effectiveShaderDisplay = selected;
        };
        shaderComboInitializing = false;

        var shaderColumn = new StackPanel { Spacing = 6 };

        // Shader + Addon side by side
        var shaderAddonGrid = new Grid { ColumnSpacing = 8 };
        shaderAddonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shaderAddonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shaderAddonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shaderAddonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var shaderLabel = new TextBlock
        {
            Text = "Shaders",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        Grid.SetRow(shaderLabel, 0); Grid.SetColumn(shaderLabel, 0);
        shaderAddonGrid.Children.Add(shaderLabel);
        Grid.SetRow(shaderModeCombo, 1); Grid.SetColumn(shaderModeCombo, 0);
        shaderAddonGrid.Children.Add(shaderModeCombo);

        shaderColumn.Children.Add(shaderAddonGrid);

        // ── Per-game Addon mode ComboBox ──
        string currentAddonMode = ViewModel.GetPerGameAddonMode(gameName);
        var addonModeItems = new[] { "Global", "Select", "Off" };
        bool addonComboInitializing = true;

        var addonModeCombo = new ComboBox
        {
            ItemsSource = addonModeItems,
            SelectedItem = currentAddonMode == "Off" ? "Off" : (currentAddonMode == "Select" ? "Select" : "Global"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !card.UseNormalReShade,
        };
        ToolTipService.SetToolTip(addonModeCombo,
            "Global = use global addon set. Select = pick per-game addons. Off = no addons for this game.");

        addonModeCombo.SelectionChanged += async (s, ev) =>
        {
            if (addonComboInitializing) return;
            var selected = addonModeCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            if (selected == "Select")
            {
                List<string>? current = ViewModel.GameNameServiceInstance.PerGameAddonSelection.TryGetValue(gameName, out var existingAddons)
                    ? existingAddons
                    : null;

                IAddonPackService? addonPackService = ViewModel.AddonPackServiceInstance;
                if (addonPackService == null)
                {
                    addonComboInitializing = true;
                    addonModeCombo.SelectedItem = currentAddonMode == "Off" ? "Off" : "Global";
                    addonComboInitializing = false;
                    return;
                }

                var result = await AddonPopupHelper.ShowAsync(
                    _window.Content.XamlRoot,
                    addonPackService,
                    current,
                    AddonPopupHelper.PopupContext.PerGame);
                if (result != null)
                {
                    ViewModel.GameNameServiceInstance.PerGameAddonSelection[gameName] = result;
                    ViewModel.SetPerGameAddonMode(capturedName, "Select");
                    ViewModel.DeployAddonsForCard(capturedName);
                }
                else
                {
                    addonComboInitializing = true;
                    addonModeCombo.SelectedItem = currentAddonMode == "Off" ? "Off" : "Global";
                    addonComboInitializing = false;
                }
                return;
            }

            if (selected == "Off")
                ViewModel.SetPerGameAddonMode(capturedName, "Off");
            else
                ViewModel.SetPerGameAddonMode(capturedName, "Global");
            ViewModel.DeployAddonsForCard(capturedName);
        };
        addonComboInitializing = false;

        var addonLabel = new TextBlock
        {
            Text = "Addons",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        Grid.SetRow(addonLabel, 0); Grid.SetColumn(addonLabel, 1);
        shaderAddonGrid.Children.Add(addonLabel);
        Grid.SetRow(addonModeCombo, 1); Grid.SetColumn(addonModeCombo, 1);
        shaderAddonGrid.Children.Add(addonModeCombo);

        // Add "Shaders and Addons" section title
        shaderColumn.Children.Insert(0, new TextBlock
        {
            Text = "Shaders and Addons",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 4),
        });

        Grid.SetColumn(shaderColumn, 0);
        Grid.SetRow(shaderColumn, 4);
        mainGrid.Children.Add(shaderColumn);

        // Right column Row 4: Launch executable label + TextBox
        var launchExeColumn = new StackPanel { Spacing = 6 };
        launchExeColumn.Children.Add(new TextBlock
        {
            Text = "Launch executable",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 15),
        });

        var currentLaunchExe = ViewModel.GameNameServiceInstance.LaunchExeOverrides
            .TryGetValue(capturedName, out var savedExe) ? savedExe : "";
        var launchExeBox = new TextBox
        {
            Text = currentLaunchExe,
            PlaceholderText = "Auto-detect (or paste path)",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        launchExeBox.LostFocus += (s, ev) =>
        {
            var newPath = launchExeBox.Text.Trim();
            if (string.IsNullOrEmpty(newPath))
                ViewModel.GameNameServiceInstance.LaunchExeOverrides.Remove(capturedName);
            else
                ViewModel.GameNameServiceInstance.LaunchExeOverrides[capturedName] = newPath;
            ViewModel.SaveSettingsPublic();
        };
        launchExeColumn.Children.Add(launchExeBox);

        Grid.SetColumn(launchExeColumn, 2);
        Grid.SetRow(launchExeColumn, 4);
        mainGrid.Children.Add(launchExeColumn);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 5 — Left: Select ReShade Preset + Right: Browse/Reset (in same section as Row 4)
        // ══════════════════════════════════════════════════════════════════════

        var presetBtn = new Button
        {
            Content = "Select ReShade Preset",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(presetBtn,
            "Pick .ini preset files to copy to this game's folder. Place presets in the reshade-presets folder.");
        presetBtn.Click += async (s, ev) =>
        {
            var selected = await PresetPopupHelper.ShowAsync(_window.Content.XamlRoot);
            if (selected != null && selected.Count > 0)
            {
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && !string.IsNullOrEmpty(targetCard.InstallPath))
                {
                    int count = PresetPopupHelper.DeployPresets(selected, targetCard.InstallPath);
                    _crashReporter.Log($"[OverridesFlyoutBuilder] Deployed {count} preset(s) to '{capturedName}'");

                    if (count > 0)
                    {
                        var shaderDialog = new ContentDialog
                        {
                            Title = "🔧 Install Shaders?",
                            Content = "Also install the required shaders and textures?",
                            PrimaryButtonText = "Yes",
                            CloseButtonText = "No",
                            XamlRoot = _window.Content.XamlRoot,
                            RequestedTheme = ElementTheme.Dark,
                        };

                        var shaderResult = await DialogService.ShowSafeAsync(shaderDialog);
                        if (shaderResult == ContentDialogResult.Primary)
                        {
                            var presetPaths = selected.Select(f => Path.Combine(PresetPopupHelper.PresetsDir, f)).ToList();
                            await ViewModel.ApplyPresetShadersAsync(capturedName, presetPaths);

                            // Rebuild overrides panel so the shader toggle reflects the new "Select" mode
                            if (ViewModel.SelectedGame is { } selectedCard
                                && selectedCard.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase))
                            {
                                _window.BuildOverridesPanel(selectedCard);
                            }
                        }
                    }
                }
            }
        };

        presetBtn.Margin = new Thickness(0, 8, 0, 0);
        shaderColumn.Children.Add(presetBtn);
        var launchBtnRow = new Grid { ColumnSpacing = 8 };
        launchBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        launchBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var browseLaunchBtn = new Button
        {
            Content = "Browse",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        browseLaunchBtn.Click += async (s, ev) =>
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            string? filePath = await Task.Run(() =>
            {
                var ofn = new NativeInterop.OpenFileName();
                ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
                ofn.hwndOwner = hwnd;
                ofn.filter = "Executables (*.exe)\0*.exe\0All Files (*.*)\0*.*\0";
                ofn.file = new string(new char[260]);
                ofn.maxFile = ofn.file.Length;
                ofn.title = "Select Game Executable";
                ofn.initialDir = card.InstallPath;
                ofn.flags = 0x00080000 | 0x00001000;
                return NativeInterop.GetOpenFileName(ref ofn) ? ofn.file.TrimEnd('\0') : null;
            });
            if (!string.IsNullOrEmpty(filePath))
            {
                launchExeBox.Text = filePath;
                ViewModel.GameNameServiceInstance.LaunchExeOverrides[capturedName] = filePath;
                ViewModel.SaveSettingsPublic();
            }
        };
        Grid.SetColumn(browseLaunchBtn, 0);
        launchBtnRow.Children.Add(browseLaunchBtn);

        var resetLaunchBtn = new Button
        {
            Content = "Reset",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        resetLaunchBtn.Click += (s, ev) =>
        {
            launchExeBox.Text = "";
            ViewModel.GameNameServiceInstance.LaunchExeOverrides.Remove(capturedName);
            ViewModel.SaveSettingsPublic();
        };
        Grid.SetColumn(resetLaunchBtn, 1);
        launchBtnRow.Children.Add(resetLaunchBtn);

        launchBtnRow.Margin = new Thickness(0, 8, 0, 0);
        launchExeColumn.Children.Add(launchBtnRow);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 5 — Horizontal separator (before DXVK)
        // ══════════════════════════════════════════════════════════════════════
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 5 (separator)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 6 (DXVK)
        // Extend vertical divider to span the new rows
        Grid.SetRowSpan(verticalDivider, 7);

        var sep4 = UIFactory.MakeSeparator();
        Grid.SetColumn(sep4, 0);
        Grid.SetRow(sep4, 5);
        Grid.SetColumnSpan(sep4, 3);
        mainGrid.Children.Add(sep4);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 6 — Left: DXVK ComboBox (Off/Global/Development/Stable/Lilium HDR)
        // ══════════════════════════════════════════════════════════════════════

        // Forward-declare DXVK toggle for reset handler
        ToggleSwitch dxvkToggle = null!;

        if (card.IsDxvkToggleVisible)
        {
            var dxvkModeItems = new[] { "Off", "Global", "Development", "Stable", "Lilium HDR" };
            string defaultDxvkSelection;
            if (!card.DxvkEnabled)
            {
                defaultDxvkSelection = "Off";
            }
            else
            {
                var currentDxvkOverride = ViewModel.GetDxvkVariantOverride(capturedName);
                defaultDxvkSelection = currentDxvkOverride switch
                {
                    "Development" => "Development",
                    "Stable" => "Stable",
                    "LiliumHdr" => "Lilium HDR",
                    _ => "Global",
                };
            }

            var dxvkModeCombo = new ComboBox
            {
                ItemsSource = dxvkModeItems,
                SelectedItem = defaultDxvkSelection,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = card.IsDxvkToggleEnabled && card.DxvkInstallEnabled,
            };
            if (card.DxvkToggleTooltip != null)
                ToolTipService.SetToolTip(dxvkModeCombo, card.DxvkToggleTooltip);
            else
                ToolTipService.SetToolTip(dxvkModeCombo,
                    "Off = DXVK disabled. Global = use global variant setting.\nDevelopment/Stable/Lilium HDR = per-game variant override.\nDXVK translates DirectX to Vulkan — enables compute shaders.");

            dxvkToggle = new ToggleSwitch { IsOn = card.DxvkEnabled, Visibility = Visibility.Collapsed };

            dxvkModeCombo.SelectionChanged += async (s, ev) =>
            {
                var selected = dxvkModeCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(selected)) return;
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard == null) return;

                if (selected == "Off")
                {
                    if (targetCard.DxvkEnabled)
                    {
                        await ViewModel.HandleDxvkToggleAsync(targetCard, false, _window.Content.XamlRoot);
                        ViewModel.SetDxvkVariantOverride(capturedName, null);
                    }
                }
                else
                {
                    string? variantValue = selected switch
                    {
                        "Development" => "Development",
                        "Stable" => "Stable",
                        "Lilium HDR" => "LiliumHdr",
                        _ => null,
                    };
                    ViewModel.SetDxvkVariantOverride(capturedName, variantValue);

                    if (!targetCard.DxvkEnabled)
                    {
                        var resolvedVariant = ViewModel.ResolveDxvkVariant(capturedName);
                        var savedVariant = ViewModel.DxvkServiceInstance.SelectedVariant;
                        ViewModel.DxvkServiceInstance.SelectedVariant = resolvedVariant;
                        await ViewModel.HandleDxvkToggleAsync(targetCard, true, _window.Content.XamlRoot);
                        ViewModel.DxvkServiceInstance.SelectedVariant = savedVariant;
                        if (!targetCard.DxvkEnabled) dxvkModeCombo.SelectedItem = "Off";
                    }
                    else
                    {
                        var resolvedVariant = ViewModel.ResolveDxvkVariant(capturedName);
                        var savedVariant = ViewModel.DxvkServiceInstance.SelectedVariant;
                        ViewModel.DxvkServiceInstance.SelectedVariant = resolvedVariant;
                        await ViewModel.DxvkServiceInstance.EnsureStagingAsync();
                        if (ViewModel.DxvkServiceInstance.IsStagingReady)
                            await ViewModel.InstallDxvkAsync(targetCard, _window.Content.XamlRoot);
                        ViewModel.DxvkServiceInstance.SelectedVariant = savedVariant;
                    }
                }
            };

            var dxvkColumn = new StackPanel { Spacing = 6 };
            dxvkColumn.Children.Add(new TextBlock
            {
                Text = "DXVK",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                Margin = new Thickness(0, 0, 0, 4),
            });
            dxvkColumn.Children.Add(dxvkModeCombo);

            Grid.SetColumn(dxvkColumn, 0);
            Grid.SetRow(dxvkColumn, 6);
            mainGrid.Children.Add(dxvkColumn);
        }

        panel.Children.Add(mainGrid);

        // ══════════════════════════════════════════════════════════════════════
        // Reset Overrides handler (wired to the link near the title)
        // ══════════════════════════════════════════════════════════════════════
        resetOverridesLink.Click += (s, ev) =>
        {
            // Reset all controls to defaults
            gameNameBox.Text = originalStoreName ?? gameName;
            wikiNameBox.Text = "";
            shaderComboInitializing = true;
            shaderModeCombo.SelectedItem = "Global";
            shaderComboInitializing = false;
            addonComboInitializing = true;
            addonModeCombo.SelectedItem = "Global";
            addonComboInitializing = false;
            dllOverrideToggle.IsOn = false;
            wikiExcludeCombo.SelectedItem = "Included";

            // Persist all reset values immediately
            var resetName = (originalStoreName ?? gameName).Trim();
            bool nameChanged = !resetName.Equals(capturedName, StringComparison.OrdinalIgnoreCase);
            if (nameChanged && !string.IsNullOrWhiteSpace(resetName))
            {
                ViewModel.RenameGame(capturedName, resetName);
                capturedName = resetName;
            }

            // Remove wiki mapping
            if (ViewModel.GetNameMapping(capturedName) != null)
                ViewModel.RemoveNameMapping(capturedName);

            // Shader mode → Global
            if (ViewModel.GetPerGameShaderMode(capturedName) != "Global")
            {
                ViewModel.SetPerGameShaderMode(capturedName, "Global");
                ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(capturedName);
                ViewModel.DeployShadersForCard(capturedName);
            }

            // Addon mode → Global
            if (ViewModel.GetPerGameAddonMode(capturedName) != "Global")
            {
                ViewModel.SetPerGameAddonMode(capturedName, "Global");
                ViewModel.DeployAddonsForCard(capturedName);
            }

            // Disable DLL override
            if (ViewModel.HasDllOverride(capturedName))
            {
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                    ViewModel.DisableDllOverride(targetCard);
            }

            // Include all in Update All
            if (ViewModel.IsUpdateAllExcludedReShade(capturedName))
                ViewModel.ToggleUpdateAllExclusionReShade(capturedName);
            if (ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);
            if (ViewModel.IsUpdateAllExcludedUl(capturedName))
                ViewModel.ToggleUpdateAllExclusionUl(capturedName);
            if (ViewModel.IsUpdateAllExcludedDc(capturedName))
                ViewModel.ToggleUpdateAllExclusionDc(capturedName);
            if (ViewModel.IsUpdateAllExcludedOs(capturedName))
                ViewModel.ToggleUpdateAllExclusionOs(capturedName);

            // Refresh update summary
            UpdateInclusionHelper.RefreshSummary(updateSummaryText, ViewModel, capturedName, card.IsREEngineGame, card.DxvkEnabled);

            // Disable wiki exclusion
            if (ViewModel.IsWikiExcluded(capturedName))
                ViewModel.ToggleWikiExclusion(capturedName);

            // Reset Normal ReShade
            {
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && targetCard.UseNormalReShade)
                    ViewModel.SetUseNormalReShade(targetCard, false);
            }

            // Reset bitness and API overrides
            bitnessCombo.SelectedItem = "Auto";
            ViewModel.SetBitnessOverride(capturedName, null);
            apiCombo.SelectedItem = "Auto";
            ViewModel.SetApiOverride(capturedName, null);
            channelCombo.SelectedItem = "Global";
            ViewModel.SetReShadeChannelOverride(capturedName, null);
            if (card.IsDxvkToggleVisible)
            {
                ViewModel.SetDxvkVariantOverride(capturedName, null);
            }

            _crashReporter.Log($"[OverridesFlyoutBuilder.OpenOverridesFlyout] Overrides reset for: {capturedName}");

            // Only reselect/NotifyAll/RebuildCardGrid if game name actually changed
            if (nameChanged)
            {
                _window.RequestReselect(capturedName);
                card.NotifyAll();
                if (ViewModel.IsGridLayout)
                    _window.RebuildCardGrid();
            }
        };

        // Style the flyout presenter to allow scrolling and set max dimensions
        var flyoutStyle = new Style(typeof(FlyoutPresenter));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, 740));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MaxHeightProperty, 800));
        flyoutStyle.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto));

        var flyout = new Flyout
        {
            Content = panel,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = flyoutStyle,
        };

        flyout.ShowAt(anchor);
    }
}
