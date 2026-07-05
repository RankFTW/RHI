// DetailPanelBuilder.Overrides.cs — Main overrides panel (DLL names, Bitness, API).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private sealed class OverridesPanelCtx
    {
        public required GameCardViewModel Card;
        public required string GameName;
        public string CapturedName = null!;
        public required bool IsLumaMode;
        public required Grid BitnessPanel;
        public required ComboBox BitnessCombo;
        public required ComboBox ApiCombo;
        public required TextBox DetectedBox;
        public required TextBox WikiBox;
        public required ComboBox WikiExcludeCombo;
        public required ToggleSwitch DllOverrideToggle;
        public required string? OriginalStoreName;
        public ComboBox? RenderPathCombo;
        public ComboBox ChannelCombo = null!;
        public ComboBox ShaderModeCombo = null!;
        public bool ShaderComboInitializing;
        public TextBlock UpdateSummaryText = null!;
        public bool ChannelComboInitializing;
        public ToggleSwitch DxvkToggle = null!;
        public Button ResetOverridesBtn = null!;
    }

    public void BuildOverridesPanel(GameCardViewModel card)
    {
        _window.OverridesPanel.Children.Clear();

        var gameName = card.GameName;
        bool isLumaMode = _window.ViewModel.IsLumaEnabled(gameName);

        // ── Title ────────────────────────────────────────────────────────────────
        _window.OverridesPanel.Children.Add(new TextBlock
        {
            Text = "Game Overrides",
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
        ToolTipService.SetToolTip(detectedBox,
            "The display name for this game. Edit and press Enter to rename. Reset reverts to the auto-detected store name.");
        var wikiBox = new TextBox
        {
            Header = "Wiki mod name",
            PlaceholderText = "Exact wiki name",
            Text = _window.ViewModel.GetUserNameMapping(gameName) ?? "",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(wikiBox,
            "Override the name used to look up this game on the RenoDX/Luma wiki. Leave blank to use the game name. Press Enter to save.");
        var originalStoreName = _window.ViewModel.GetOriginalStoreName(gameName);

        // Mutable captured name so rename handler can update it for subsequent handlers
        var capturedName = gameName;

        var resetBtn = new Button
        {
            Content = "Reset",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(10, 6, 10, 6),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
        };
        ToolTipService.SetToolTip(resetBtn, "Reset game name back to auto-detected and clear wiki name mapping.");
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

        // ── DLL naming override (placed in Top Row right column) ───────────
        bool isDllOverride = _window.ViewModel.HasDllOverride(gameName);
        var existingCfg = _window.ViewModel.GetDllOverride(gameName);
        bool is32Bit = card.Is32Bit;
        var defaultRsName = is32Bit ? "ReShade32.dll" : "ReShade64.dll";

        var dllOverrideToggle = new ToggleSwitch
        {
            Header = "DLL naming overrides",
            IsOn = isDllOverride,
            IsEnabled = true,
            OnContent = "Custom filenames enabled",
            OffContent = "Override DLL filenames",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(dllOverrideToggle,
            "Override the filenames ReShade is installed as. When enabled, existing RS files are renamed to the custom filenames.");
        var existingRsName = existingCfg?.ReShadeFileName ?? "";

        var rsNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = "Select ReShade DLL name",
            Header = (object?)null,
            FontSize = 12,
            IsEnabled = isDllOverride && !card.IsOsInstalled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DllOverrideConstants.CommonDllNames,
        };
        if (card.IsOsInstalled)
        {
            ToolTipService.SetToolTip(rsNameBox,
                "ReShade DLL name is controlled by OptiScaler. Uninstall OptiScaler to change the ReShade DLL name.");
        }
        if (!string.IsNullOrEmpty(existingRsName))
        {
            if (DllOverrideConstants.CommonDllNames.Contains(existingRsName, StringComparer.OrdinalIgnoreCase))
            {
                rsNameBox.SelectedItem = DllOverrideConstants.CommonDllNames.First(n => n.Equals(existingRsName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Add the custom name as a temporary item so SelectedItem works reliably.
                // The Loaded event approach is unreliable in WinUI 3 — the deferred Text
                // assignment can be overwritten by the ComboBox's internal state reset.
                var extendedRsNames = DllOverrideConstants.CommonDllNames.Append(existingRsName).ToArray();
                rsNameBox.ItemsSource = extendedRsNames;
                rsNameBox.SelectedItem = existingRsName;
            }
        }
        // ── DC DLL naming override ─────────────────────────────────────────
        var existingDcName = existingCfg?.DcFileName ?? "";
        bool isDcDllOverrideOn = isDllOverride && !string.IsNullOrEmpty(existingDcName);

        var dcNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = "Select DC DLL name",
            FontSize = 12,
            IsEnabled = isDcDllOverrideOn,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DcDllOverrideNames,
        };
        if (!string.IsNullOrEmpty(existingDcName))
        {
            if (DcDllOverrideNames.Contains(existingDcName, StringComparer.OrdinalIgnoreCase))
            {
                dcNameBox.SelectedItem = DcDllOverrideNames.First(n => n.Equals(existingDcName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Add the custom name as a temporary item so SelectedItem works reliably.
                // The Loaded event approach is unreliable in WinUI 3 — the deferred Text
                // assignment can be overwritten by the ComboBox's internal state reset.
                var extendedDcNames = DcDllOverrideNames.Append(existingDcName).ToArray();
                dcNameBox.ItemsSource = extendedDcNames;
                dcNameBox.SelectedItem = existingDcName;
            }
        }

        // Track previous DC selection for revert on foreign DLL conflict cancel
        string? _previousDcSelection = dcNameBox.SelectedItem as string;

        // ── OptiScaler DLL naming override ─────────────────────────────────────
        var existingOsName = existingCfg?.OsFileName ?? "";
        var availableOsNames = _window.ViewModel.DllOverrideServiceInstance
            .GetAvailableOsDllNames(gameName, is32Bit);

        var osNameBox = new ComboBox
        {
            PlaceholderText = "Select OptiScaler DLL name",
            FontSize = 12,
            IsEnabled = isDllOverride,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = availableOsNames,
        };
        if (!string.IsNullOrEmpty(existingOsName))
        {
            if (availableOsNames.Contains(existingOsName, StringComparer.OrdinalIgnoreCase))
            {
                osNameBox.SelectedItem = availableOsNames.First(n => n.Equals(existingOsName, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Add the custom name as a temporary item so SelectedItem works reliably.
                var extendedOsNames = availableOsNames.Append(existingOsName).ToArray();
                osNameBox.ItemsSource = extendedOsNames;
                osNameBox.SelectedItem = existingOsName;
            }
        }

        // Track previous OS selection for revert
        string? _previousOsSelection = osNameBox.SelectedItem as string;

        // ── Auto-save: OS name box on dropdown selection ──────────────────────
        osNameBox.SelectionChanged += (s, e) =>
        {
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var osName = osNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(osName)) return;

            _previousOsSelection = osName;
            _window.ViewModel.DllOverrideServiceInstance.SetOsDllOverride(capturedName, osName);

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
                        var osRecord = _window.ViewModel.AuxInstallServiceInstance
                            .FindRecord(capturedName, targetCard.InstallPath, "OptiScaler");
                        if (osRecord != null)
                        {
                            osRecord.InstalledAs = osName;
                            _window.ViewModel.AuxInstallServiceInstance.SaveAuxRecord(osRecord);
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DetailPanelBuilder.BuildOverridesPanel] Failed to rename OS DLL for '{capturedName}' — {ex.Message}");
                }
            }
        };

        // ── Cross-exclusion: filter out the other component's current name ───────
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
                    ? DcDllOverrideNames
                    : DcDllOverrideNames.Where(n => !n.Equals(rsCurrentName, StringComparison.OrdinalIgnoreCase)).ToArray();
                var currentDc = dcNameBox.SelectedItem as string;
                // Preserve custom DC name that isn't in the base list
                if (currentDc != null && !filtered.Contains(currentDc, StringComparer.OrdinalIgnoreCase))
                    filtered = filtered.Append(currentDc).ToArray();
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
                // Preserve custom RS name that isn't in the base list
                if (!string.IsNullOrEmpty(currentRs) && !filtered.Contains(currentRs, StringComparer.OrdinalIgnoreCase))
                    filtered = filtered.Append(currentRs).ToArray();
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
            rsNameBox.IsEnabled = dllOverrideToggle.IsOn && !card.IsOsInstalled;
            dcNameBox.IsEnabled = dllOverrideToggle.IsOn;
            osNameBox.IsEnabled = dllOverrideToggle.IsOn;

            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;

            if (dllOverrideToggle.IsOn)
            {
                // Turning unified override ON
                var existingCfgNow = _window.ViewModel.GetDllOverride(capturedName);

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
                        if (DcDllOverrideNames.Contains(dcName, StringComparer.OrdinalIgnoreCase))
                        {
                            dcNameBox.SelectedItem = DcDllOverrideNames
                                .First(n => n.Equals(dcName, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            var extendedDc = DcDllOverrideNames.Append(dcName).ToArray();
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

                    if (DcDllOverrideNames.Contains(dcName, StringComparer.OrdinalIgnoreCase))
                    {
                        dcNameBox.SelectedItem = DcDllOverrideNames
                            .First(n => n.Equals(dcName, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        var extendedDc = DcDllOverrideNames.Append(dcName).ToArray();
                        dcNameBox.ItemsSource = extendedDc;
                        dcNameBox.SelectedItem = dcName;
                    }
                }

                _window.ViewModel.EnableDllOverride(targetCard, rsName, dcName);
            }
            else
            {
                // Turning unified override OFF — delegate to service for both RS and DC revert
                var result = _window.ViewModel.DisableDllOverride(targetCard);

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
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var dcName = dcNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(dcName)) return;

            // Collision check: reject if selected DC name matches the current RS name (case-insensitive)
            // Only check against RsNormalName when RS override is OFF AND RS is actually installed
            string currentRsName;
            if (dllOverrideToggle.IsOn)
                currentRsName = (rsNameBox.SelectedItem as string ?? rsNameBox.Text ?? "").Trim();
            else if (targetCard.RsRecord != null || !string.IsNullOrEmpty(targetCard.RsInstalledFile))
                currentRsName = Services.AuxInstallService.RsNormalName;
            else
                currentRsName = "";
            if (!string.IsNullOrEmpty(currentRsName) && dcName.Equals(currentRsName, StringComparison.OrdinalIgnoreCase))
            {
                // Revert dropdown to previous selection
                if (_previousDcSelection != null)
                    dcNameBox.SelectedItem = DcDllOverrideNames.FirstOrDefault(n =>
                        n.Equals(_previousDcSelection, StringComparison.OrdinalIgnoreCase));
                else
                    dcNameBox.SelectedIndex = -1;
                return;
            }

            // Check for foreign DLL conflict before proceeding
            bool allowed = await _window.ViewModel.DllOverrideServiceInstance
                .CheckDcForeignDllConflictAsync(targetCard, dcName);
            if (!allowed)
            {
                // Revert dropdown to previous selection
                if (_previousDcSelection != null)
                    dcNameBox.SelectedItem = DcDllOverrideNames.FirstOrDefault(n =>
                        n.Equals(_previousDcSelection, StringComparison.OrdinalIgnoreCase));
                else
                    dcNameBox.SelectedIndex = -1;
                return;
            }

            _previousDcSelection = dcName;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            UpdateRsDropdownItems();
        };

        // ── Auto-save: DC name box on Enter (manual typed name) ──────────────────
        dcNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var dcName = (dcNameBox.SelectedItem as string ?? dcNameBox.Text)?.Trim();
            if (string.IsNullOrWhiteSpace(dcName)) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";

            // Collision check: reject if typed DC name matches the current RS name (case-insensitive)
            // Only check against RsNormalName when RS override is OFF AND RS is actually installed
            string currentRsName;
            if (dllOverrideToggle.IsOn)
                currentRsName = rsName;
            else if (targetCard.RsRecord != null || !string.IsNullOrEmpty(targetCard.RsInstalledFile))
                currentRsName = Services.AuxInstallService.RsNormalName;
            else
                currentRsName = "";
            if (!string.IsNullOrEmpty(currentRsName) && dcName.Equals(currentRsName, StringComparison.OrdinalIgnoreCase))
            {
                // Revert the text to the previous valid selection
                dcNameBox.Text = _previousDcSelection ?? "";
                return;
            }

            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            UpdateRsDropdownItems();
        };

        // ── Top Row Grid (3 columns: Star | Auto | Star) ─────────────────────
        var topRowGrid = new Grid { ColumnSpacing = 0 };
        topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left column: Game Name + Wiki Name side by side, then Reset + Wiki ComboBox below
        var topLeftColumn = new StackPanel { Spacing = 6 };

        // Row 1: Game name + Wiki name side by side
        var nameRow = new Grid { ColumnSpacing = 8 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(detectedBox, 0);
        Grid.SetColumn(wikiBox, 1);
        nameRow.Children.Add(detectedBox);
        nameRow.Children.Add(wikiBox);
        topLeftColumn.Children.Add(nameRow);

        // Row 2: Reset button (half) + Wiki lookup ComboBox (half)
        var resetWikiRow = new Grid { ColumnSpacing = 8 };
        resetWikiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        resetWikiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Restyle reset button to blue accent
        resetBtn.Content = "Reset";
        resetBtn.FontSize = 12;
        resetBtn.Height = 32;
        resetBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        resetBtn.VerticalAlignment = VerticalAlignment.Stretch;
        resetBtn.Padding = new Thickness(10, 6, 10, 6);
        resetBtn.Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
        resetBtn.Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
        resetBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
        resetBtn.BorderThickness = new Thickness(1);
        resetBtn.CornerRadius = new CornerRadius(8);
        Grid.SetColumn(resetBtn, 0);
        resetWikiRow.Children.Add(resetBtn);

        // Wiki lookup ComboBox (replaces ToggleSwitch)
        var wikiExcludeItems = new[] { "Included", "Excluded" };
        var wikiExcludeCombo = new ComboBox
        {
            ItemsSource = wikiExcludeItems,
            SelectedItem = _window.ViewModel.IsWikiExcluded(gameName) ? "Excluded" : "Included",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(wikiExcludeCombo,
            "Included = this game is looked up on the RenoDX and Luma wikis. Excluded = skip wiki lookups for this game.");
        wikiExcludeCombo.SelectionChanged += (s, ev) =>
        {
            var selected = wikiExcludeCombo.SelectedItem as string;
            bool shouldExclude = selected == "Excluded";
            if (shouldExclude != _window.ViewModel.IsWikiExcluded(capturedName))
                _window.ViewModel.ToggleWikiExclusion(capturedName);
        };
        Grid.SetColumn(wikiExcludeCombo, 1);
        resetWikiRow.Children.Add(wikiExcludeCombo);

        topLeftColumn.Children.Add(resetWikiRow);

        Grid.SetColumn(topLeftColumn, 0);
        topRowGrid.Children.Add(topLeftColumn);

        // Column 1: Vertical divider
        var topRowDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(topRowDivider, 1);
        topRowGrid.Children.Add(topRowDivider);

        // ── Rendering Path (dual-API games only) ─────────────────────────────────
        // Rendering Path ComboBox removed — API toggles make it redundant.
        ComboBox? renderPathCombo = null;

        // Column 2: DLL naming override
        var topRightColumn = new StackPanel { Spacing = 6 };
        topRightColumn.Children.Add(dllOverrideToggle);

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
        topRightColumn.Children.Add(dllBoxesGrid);

        // Show/hide DLL boxes when toggle changes
        dllOverrideToggle.Toggled += (s, ev) =>
        {
            dllBoxesGrid.Visibility = dllOverrideToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        };

        Grid.SetColumn(topRightColumn, 2);
        topRowGrid.Children.Add(topRightColumn);

        _window.OverridesPanel.Children.Add(topRowGrid);
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

        // ── Per-game Shader mode ComboBox ─────────────────────────────────────
        string currentShaderMode = _window.ViewModel.GetPerGameShaderMode(gameName);
        // Resolve effective display: if global UseCustomShaders is ON and mode is "Global", show "Custom"
        string effectiveShaderDisplay = currentShaderMode;
        if (currentShaderMode == "Global"
            && _window.ViewModel.Settings.UseCustomShaders
            && !_window.ViewModel.GameNameServiceInstance.PerGameShaderMode.ContainsKey(gameName))
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
            CrashReporter.Log($"[DetailPanelBuilder.ShaderMode] '{capturedName}' selection changed to: '{selected}'");

            if (selected == "Select")
            {
                // Open per-game shader picker
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
                    _window.ViewModel.SetPerGameShaderMode(capturedName, "Select");
                    _window.ViewModel.DeployShadersForCard(capturedName);
                }
                else
                {
                    // Cancelled — revert to previous
                    shaderComboInitializing = true;
                    shaderModeCombo.SelectedItem = effectiveShaderDisplay;
                    shaderComboInitializing = false;
                }
                return;
            }

            if (selected == "Off")
            {
                _window.ViewModel.SetPerGameShaderMode(capturedName, "Off");
                _window.ViewModel.DeployShadersForCard(capturedName);
            }
            else if (selected == "Custom")
            {
                _window.ViewModel.SetPerGameShaderMode(capturedName, "Custom");
                _window.ViewModel.DeployShadersForCard(capturedName);
            }
            else // "Global"
            {
                _window.ViewModel.SetPerGameShaderMode(capturedName, "Global");
                _window.ViewModel.DeployShadersForCard(capturedName);
            }
            effectiveShaderDisplay = selected;
        };
        shaderComboInitializing = false;

        // ── Auto-save: RS name box on Enter ──────────────────────────────────────
        rsNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            if (string.IsNullOrEmpty(rsName)) return;
            var dcName = dllOverrideToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "").Trim() : "";

            // Collision check: reject if typed RS name matches the current DC name (case-insensitive)
            if (dllOverrideToggle.IsOn && !string.IsNullOrEmpty(dcName) && rsName.Equals(dcName, StringComparison.OrdinalIgnoreCase))
            {
                // Revert the text to the previous valid RS selection
                var previousRs = _window.ViewModel.GetDllOverride(capturedName)?.ReShadeFileName;
                rsNameBox.Text = previousRs ?? "";
                return;
            }

            if (_window.ViewModel.HasDllOverride(capturedName))
                _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            else
                _window.ViewModel.EnableDllOverride(targetCard, rsName, dcName);
        };
        // ── Auto-save: RS name box on dropdown selection ─────────────────────────
        rsNameBox.SelectionChanged += (s, e) =>
        {
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsName = rsNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(rsName)) return;
            var dcName = dllOverrideToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "") : "";

            if (_window.ViewModel.HasDllOverride(capturedName))
                _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            else
                _window.ViewModel.EnableDllOverride(targetCard, rsName, dcName);

            UpdateDcDropdownItems();
        };

        // ── Bitness Override ComboBox (left column of Bitness & API Row) ─────────
        var bitnessLabel = new TextBlock
        {
            Text = "Bitness",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var bitnessItems = new[] { "Auto", "32-bit", "64-bit" };
        var currentBitnessOverride = _window.ViewModel.GetBitnessOverride(gameName);
        var defaultBitnessSelection = currentBitnessOverride switch
        {
            "32" => "32-bit",
            "64" => "64-bit",
            _ => "Auto",
        };

        var bitnessCombo = new ComboBox
        {
            ItemsSource = bitnessItems,
            SelectedItem = defaultBitnessSelection,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(bitnessCombo,
            "Override the auto-detected bitness for this game. Auto uses PE header detection. 32-bit or 64-bit forces the value.");

        bitnessCombo.SelectionChanged += (s, e) =>
        {
            var selected = bitnessCombo.SelectedItem as string;
            string? overrideValue = selected switch
            {
                "32-bit" => "32",
                "64-bit" => "64",
                _ => null,
            };

            _window.ViewModel.SetBitnessOverride(capturedName, overrideValue);

            // Update card.Is32Bit based on selection
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
            {
                var previousIs32Bit = targetCard.Is32Bit;

                // Compute the new effective bitness
                bool newIs32Bit;
                if (overrideValue == "32")
                    newIs32Bit = true;
                else if (overrideValue == "64")
                    newIs32Bit = false;
                else
                {
                    // "Auto" — re-resolve from auto-detection
                    var detectedMachine = _window.ViewModel.PeHeaderServiceInstance.DetectGameArchitecture(targetCard.InstallPath);
                    newIs32Bit = _window.ViewModel.ResolveIs32Bit(capturedName, detectedMachine);
                }

                // If bitness actually changed, uninstall all components BEFORE updating card.Is32Bit
                // (uninstall methods use card.Is32Bit to resolve filenames of deployed DLLs)
                if (previousIs32Bit != newIs32Bit && !targetCard.RequiresVulkanInstall)
                {
                    if (targetCard.IsRsInstalled)
                        _window.ViewModel.UninstallReShade(targetCard);
                    if (targetCard.DcStatus == GameStatus.Installed)
                        _window.ViewModel.UninstallDc(targetCard);
                    if (targetCard.InstalledRecord != null)
                        _window.ViewModel.UninstallMod(targetCard);
                    if (targetCard.UlStatus == GameStatus.Installed)
                        _window.ViewModel.UninstallUl(targetCard);
                    if (targetCard.OsStatus == GameStatus.Installed)
                        _window.ViewModel.OptiScalerServiceInstance.Uninstall(targetCard);
                    if (targetCard.DxvkStatus == GameStatus.Installed)
                        _window.ViewModel.UninstallDxvk(targetCard);
                    if (targetCard.RefStatus == GameStatus.Installed)
                        _window.ViewModel.UninstallREFramework(targetCard);
                    if (targetCard.LumaStatus == GameStatus.Installed)
                        _window.ViewModel.UninstallLuma(targetCard);
                }

                // NOW update card.Is32Bit to the new value
                targetCard.Is32Bit = newIs32Bit;

                // Update DLL naming section placeholder text to match new bitness
                rsNameBox.PlaceholderText = targetCard.Is32Bit ? "ReShade32.dll" : "ReShade64.dll";

                targetCard.NotifyAll();

                // Rebuild the detail panel so install buttons reflect the new bitness
                _window.RequestReselect(capturedName);
            }
        };

        var bitnessPanel = new Grid { ColumnSpacing = 12 };
        bitnessPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bitnessPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bitnessPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bitnessPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(bitnessLabel, 0); Grid.SetColumn(bitnessLabel, 0);
        Grid.SetRow(bitnessCombo, 1); Grid.SetColumn(bitnessCombo, 0);
        bitnessPanel.Children.Add(bitnessLabel);
        bitnessPanel.Children.Add(bitnessCombo);

        // ── API Override ComboBox (single selection, placed in left panel below bitness) ──────
        var apiLabel = new TextBlock
        {
            Text = "Graphics API",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        ToolTipService.SetToolTip(apiLabel,
            "Override the detected graphics API for this game.\n\n" +
            "Auto uses the auto-detected value from PE header scanning.\n" +
            "User overrides set here take precedence over manifest and auto-detected values.\n" +
            "Reset Overrides reverts to auto-detection.");

        var apiDropdownItems = new[] { "Auto", "DirectX8", "DirectX9", "DirectX10", "DX11/DX12", "Vulkan", "OpenGL" };
        var existingApiOverride = _window.ViewModel.GetApiOverride(gameName);

        // Determine current selection
        string defaultApiSelection = "Auto";
        if (existingApiOverride != null && existingApiOverride.Count > 0)
        {
            // Map stored override back to dropdown label
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

        var apiCombo = new ComboBox
        {
            ItemsSource = apiDropdownItems,
            SelectedItem = defaultApiSelection,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(apiCombo,
            "Override the detected graphics API for this game.\nAuto uses PE header scanning. Reset Overrides reverts to auto-detection.");

        apiCombo.SelectionChanged += (s, ev) =>
        {
            var selected = apiCombo.SelectedItem as string;

            // Map dropdown label to enum names for persistence
            List<string>? apiEnumNames = selected switch
            {
                "DirectX8" => new() { "DirectX8" },
                "DirectX9" => new() { "DirectX9" },
                "DirectX10" => new() { "DirectX10" },
                "DX11/DX12" => new() { "DirectX11", "DirectX12" },
                "Vulkan" => new() { "Vulkan" },
                "OpenGL" => new() { "OpenGL" },
                _ => null, // "Auto" clears the override
            };

            _window.ViewModel.SetApiOverride(capturedName, apiEnumNames);

            // Update card properties
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
            {
                if (apiEnumNames != null)
                {
                    var newApis = new HashSet<GraphicsApiType>();
                    foreach (var name in apiEnumNames)
                    {
                        if (Enum.TryParse<GraphicsApiType>(name, out var apiType))
                            newApis.Add(apiType);
                    }
                    targetCard.DetectedApis = newApis;
                }
                else
                {
                    // "Auto" — re-detect from scanning
                    targetCard.DetectedApis = _window.ViewModel._DetectAllApisForCard(targetCard.InstallPath, capturedName);
                }
                targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
                targetCard.GraphicsApi = _window.ViewModel.DetectGraphicsApi(
                    targetCard.InstallPath, EngineType.Unknown, capturedName);
                targetCard.NotifyAll();

                // Rebuild the detail panel so install buttons reflect the new API
                // (e.g., Vulkan games need the global layer install instead of per-game DLL)
                _window.RequestReselect(capturedName);
            }
        };

        // Add API dropdown to bitness panel (right column, side by side)
        Grid.SetRow(apiLabel, 0); Grid.SetColumn(apiLabel, 1);
        Grid.SetRow(apiCombo, 1); Grid.SetColumn(apiCombo, 1);
        bitnessPanel.Children.Add(apiLabel);

        var ctx = new OverridesPanelCtx
        {
            Card = card,
            GameName = gameName,
            CapturedName = capturedName,
            IsLumaMode = isLumaMode,
            BitnessPanel = bitnessPanel,
            BitnessCombo = bitnessCombo,
            ApiCombo = apiCombo,
            DetectedBox = detectedBox,
            WikiBox = wikiBox,
            WikiExcludeCombo = wikiExcludeCombo,
            DllOverrideToggle = dllOverrideToggle,
            OriginalStoreName = originalStoreName,
            RenderPathCombo = renderPathCombo,
            ShaderModeCombo = shaderModeCombo,
            ShaderComboInitializing = shaderComboInitializing,
        };

        BuildRsChannelSection(ctx);
        BuildShadersAddonsSection(ctx);

        // Sync mutable state back from context
        capturedName = ctx.CapturedName;

        BuildNvidiaProfileSection(card, capturedName);

        ctx.DxvkToggle = BuildDxvkAndManagementSection(card, capturedName, gameName, ctx.ResetOverridesBtn) ?? ctx.DxvkToggle;
    }
}
