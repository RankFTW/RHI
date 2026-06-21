// DetailPanelBuilder.Overrides.cs — Overrides panel construction and all inline UI logic for per-game overrides.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    internal static readonly string[] DcDllOverrideNames =
    [
        "dxgi.dll", "d3d9.dll", "d3d11.dll", "d3d12.dll", "ddraw.dll",
        "hid.dll", "version.dll", "opengl32.dll", "dbghelp.dll",
        "vulkan-1.dll", "winmm.dll",
    ];
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
            IsEnabled = !isLumaMode,
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
        bitnessPanel.Children.Add(apiCombo);

        // ── ReShade Channel Override ──
        bitnessPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var channelLabel = new TextBlock
        {
            Text = "RS Channel",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        ToolTipService.SetToolTip(channelLabel,
            "Override the global ReShade build channel for this game.\nVulkan games: changing this affects ALL Vulkan games.");

        var channelItems = new[] { "Global", "Stable", "Nightly", "Custom", "No Addons", "Legacy..." };
        // For Vulkan games, show the effective Vulkan-wide override (any Vulkan game's override applies to all)
        var currentChannelOverride = _window.ViewModel.GetReShadeChannelOverride(gameName);
        if (currentChannelOverride == null && card.RequiresVulkanInstall)
        {
            currentChannelOverride = _window.ViewModel.AllCards
                .Where(c => c.RequiresVulkanInstall && c.GameName != gameName)
                .Select(c => _window.ViewModel.GetReShadeChannelOverride(c.GameName))
                .FirstOrDefault(ch => ch != null);
        }

        // If a legacy version is active, add it to the dropdown items
        var channelItemsList = new List<string>(channelItems);
        string defaultChannelSelection;
        if (card.UseNormalReShade)
        {
            defaultChannelSelection = "No Addons";
        }
        else if (string.Equals(currentChannelOverride, "Custom", StringComparison.OrdinalIgnoreCase))
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
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(channelCombo,
            "Override the global ReShade build channel for this game.\nGlobal = use Settings default. Vulkan games: changing this affects ALL Vulkan games.");

        bool channelComboInitializing = true;

        channelCombo.SelectionChanged += async (s, ev) =>
        {
            var selected = channelCombo.SelectedItem as string;
            if (channelComboInitializing) return;
            if (string.IsNullOrEmpty(selected)) return;
            CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' selection changed to: '{selected}'");

            // ── "Legacy..." opens the version picker dialog ──
            if (selected == "Legacy...")
            {
                var legacyVersions = _window.ViewModel.Manifest?.LegacyReShadeAvailable;
                if (legacyVersions == null || legacyVersions.Count == 0)
                {
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }

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

                if (!AuxInstallService.IsLegacyVersionCached(pickedVersion))
                {
                    var success = await AuxInstallService.DownloadLegacyReShadeAsync(pickedVersion, _window.ViewModel.HttpClient);
                    if (!success)
                    {
                        channelCombo.SelectedItem = defaultChannelSelection;
                        return;
                    }
                }

                _window.ViewModel.SetReShadeChannelOverride(capturedName, pickedVersion);

                // Clear No Addons mode if active (legacy is an addon build)
                var targetCardLegacy = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCardLegacy != null && targetCardLegacy.UseNormalReShade)
                    _window.ViewModel.SetUseNormalReShade(targetCardLegacy, false);

                // Update dropdown: remove old legacy version, add new one
                var oldLegacy = channelItemsList.FirstOrDefault(v => MainViewModel.IsLegacyVersion(v) && v != "Legacy...");
                if (oldLegacy != null) channelItemsList.Remove(oldLegacy);
                if (!channelItemsList.Contains(pickedVersion))
                    channelItemsList.Insert(3, pickedVersion);
                channelCombo.ItemsSource = channelItemsList;
                channelCombo.SelectedItem = pickedVersion;
                defaultChannelSelection = pickedVersion;

                var targetCard2 = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard2 != null)
                    await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCard2);

                _window.ViewModel.NotifyUpdateButtonChanged();

                // Rebuild overrides panel to reflect the new legacy version in the dropdown
                var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard != null)
                    BuildOverridesPanel(refreshCard);
                return;
            }

            // ── If a legacy version is already selected and user picks it again, do nothing ──
            if (selected != "Global" && selected != "Stable" && selected != "Nightly"
                && selected != "No Addons" && selected != "Legacy..." && selected != "Custom"
                && MainViewModel.IsLegacyVersion(selected))
            {
                return;
            }

            // ── "Custom" — use user-supplied ReShade DLLs ──
            if (selected == "Custom")
            {
                CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → Custom ReShade");

                if (!AuxInstallService.IsCustomReShadeAvailable())
                {
                    // No custom DLLs found — warn user and revert
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

                var targetCardCustom = _window.ViewModel.AllCards.FirstOrDefault(c =>
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
                    foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
                    {
                        _window.ViewModel.SetReShadeChannelOverride(vCard.GameName, "Custom");
                        if (vCard.UseNormalReShade)
                            _window.ViewModel.SetUseNormalReShade(vCard, false);
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
                        CrashReporter.Log($"[DetailPanelBuilder] Failed to update Vulkan layer with custom ReShade — {ex.Message}");
                    }
                }
                else
                {
                    // Non-Vulkan: per-game only
                    _window.ViewModel.SetReShadeChannelOverride(capturedName, "Custom");
                    if (targetCardCustom != null && targetCardCustom.UseNormalReShade)
                        _window.ViewModel.SetUseNormalReShade(targetCardCustom, false);

                    // Reinstall with custom DLLs
                    if (targetCardCustom != null)
                        await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCardCustom);
                }

                defaultChannelSelection = "Custom";
                _window.ViewModel.NotifyUpdateButtonChanged();

                // Rebuild overrides panel to reflect update inclusion changes
                var refreshCardCustom = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCardCustom != null)
                    BuildOverridesPanel(refreshCardCustom);
                return;
            }

            // ── "No Addons" — switch to normal (non-addon) ReShade ──
            if (selected == "No Addons")
            {
                CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → No Addons mode");
                var targetCardNoAddon = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCardNoAddon != null && !targetCardNoAddon.UseNormalReShade)
                {
                    _window.ViewModel.SetUseNormalReShade(targetCardNoAddon, true);
                    // Clear any channel override since we're switching to normal
                    _window.ViewModel.SetReShadeChannelOverride(capturedName, null);
                    // Auto-install the normal (no-addon) version
                    if (targetCardNoAddon.RsStatus == GameStatus.NotInstalled || targetCardNoAddon.IsRsInstalled)
                        await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCardNoAddon);
                }
                defaultChannelSelection = "No Addons";
                // Rebuild to grey out addon controls
                var refreshCardNoAddon = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCardNoAddon != null)
                    BuildOverridesPanel(refreshCardNoAddon);
                return;
            }

            // ── Switching away from "No Addons" — re-enable addon ReShade ──
            {
                var targetCardReEnable = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCardReEnable != null && targetCardReEnable.UseNormalReShade)
                {
                    CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → leaving No Addons mode, re-enabling addon support");
                    _window.ViewModel.SetUseNormalReShade(targetCardReEnable, false);
                }
            }

            string? channelValue = selected switch
            {
                "Stable" => "Stable",
                "Nightly" => "Nightly",
                _ => null,
            };
            CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → channelValue={channelValue ?? "Global (null)"}");

            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
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
                foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
                {
                    _window.ViewModel.SetReShadeChannelOverride(vCard.GameName, channelValue);
                    vCard.NotifyAll();
                }

                // Determine the effective channel for the Vulkan layer
                var effectiveChannel = channelValue ?? _window.ViewModel.Settings.ReShadeChannel;

                // Update the Vulkan layer DLLs
                try
                {
                    var layerDir = VulkanLayerService.LayerDirectory;
                    var stagedPath64 = AuxInstallService.GetStagedPathForChannel(effectiveChannel, false);
                    var stagedPath32 = AuxInstallService.GetStagedPathForChannel(effectiveChannel, true);
                    var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);
                    var layer32 = Path.Combine(layerDir, "ReShade32.dll");

                    if (File.Exists(stagedPath64) && new FileInfo(stagedPath64).Length > AuxInstallService.MinReShadeSize && File.Exists(layer64))
                        AuxInstallService.CopyFileWithElevation(stagedPath64, layer64);
                    if (File.Exists(stagedPath32) && new FileInfo(stagedPath32).Length > AuxInstallService.MinReShadeSize && File.Exists(layer32))
                        AuxInstallService.CopyFileWithElevation(stagedPath32, layer32);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DetailPanelBuilder] Failed to update Vulkan layer — {ex.Message}");
                }

                // Mark all Vulkan games as installed (layer updated in-place)
                foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall && c.IsRsInstalled))
                {
                    vCard.RsStatus = GameStatus.Installed;
                    vCard.NotifyAll();
                }

                // Rebuild overrides panel to reflect update inclusion changes
                var refreshCardVulkan = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCardVulkan != null)
                    BuildOverridesPanel(refreshCardVulkan);
            }
            else
            {
                // ── Non-Vulkan game: per-game only ──
                CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{capturedName}' → setting per-game override to: {channelValue ?? "(null/Global)"}");
                _window.ViewModel.SetReShadeChannelOverride(capturedName, channelValue);

                // Auto-reinstall ReShade with the new channel if it's currently installed
                if (targetCard != null && targetCard.IsRsInstalled)
                {
                    await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCard);
                }

                // Rebuild overrides panel to reflect update inclusion changes
                var refreshCard2 = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard2 != null)
                    BuildOverridesPanel(refreshCard2);
            }

            _window.ViewModel.NotifyUpdateButtonChanged();
        };

        Grid.SetRow(channelLabel, 0); Grid.SetColumn(channelLabel, 2);
        Grid.SetRow(channelCombo, 1); Grid.SetColumn(channelCombo, 2);
        bitnessPanel.Children.Add(channelLabel);
        bitnessPanel.Children.Add(channelCombo);
        channelComboInitializing = false;

        // Disable RS Channel when Luma mode is active (Luma bundles its own ReShade)
        if (card.IsLumaMode)
            channelCombo.IsEnabled = false;

        // ── Global update inclusion (compact: button + summary) ──────────────────
        var capturedCard = card;

        var (updateInclusionBtn, updateSummaryText) = UpdateInclusionHelper.CreateUpdateInclusionControls(
            _window.ViewModel, capturedName, card.IsREEngineGame, _window.Content.XamlRoot,
            onSaved: () =>
            {
                // Rebuild the detail panel so component rows reflect the new exclusion state
                // (e.g. ReShade row un-greys when REF is excluded)
                var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard != null)
                {
                    _window.PopulateDetailPanel(refreshCard);
                    BuildOverridesPanel(refreshCard);
                }
            },
            isDxvkEnabled: card.DxvkEnabled);

        var toggleRow = new StackPanel { Spacing = 0 };
        ToolTipService.SetToolTip(updateInclusionBtn, "Choose which components are included in Update All for this game.");
        toggleRow.Children.Add(updateInclusionBtn);
        toggleRow.Children.Add(updateSummaryText);

        // ── Global update inclusion section (Middle Row right column) ────────────
        var globalUpdateColumn = new StackPanel { Spacing = 0 };
        globalUpdateColumn.Children.Add(new TextBlock
        {
            Text = "Global update inclusion",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        });
        globalUpdateColumn.Children.Add(toggleRow);

        // ── Middle Row vertical divider ──────────────────────────────────────────
        var middleRowDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };

        // ── Middle Row Grid (3 columns: Star | Auto | Star) — Bitness/API + Global update ──
        var middleRowGrid = new Grid { ColumnSpacing = 0 };
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(bitnessPanel, 0);
        Grid.SetColumn(middleRowDivider, 1);
        Grid.SetColumn(globalUpdateColumn, 2);

        middleRowGrid.Children.Add(bitnessPanel);
        middleRowGrid.Children.Add(middleRowDivider);
        middleRowGrid.Children.Add(globalUpdateColumn);

        _window.OverridesPanel.Children.Add(middleRowGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Combined "Shaders and Addons" Row (3 columns: Star | Auto | Star) ──
        var shadersAddonsRowGrid = new Grid { ColumnSpacing = 0 };
        shadersAddonsRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shadersAddonsRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shadersAddonsRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── Left column: "Shaders and Addons" ──
        var shadersAddonsLeftColumn = new StackPanel { Spacing = 6 };
        shadersAddonsLeftColumn.Children.Add(new TextBlock
        {
            Text = "Shaders and Addons",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 4),
        });

        // Shader + Addon ComboBoxes side by side in a 2-column grid
        var shaderAddonGrid = new Grid { ColumnSpacing = 12 };
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

        // ── Per-game Addon mode ComboBox ─────────────────────────────────────
        string currentAddonMode = _window.ViewModel.GetPerGameAddonMode(gameName);
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
            CrashReporter.Log($"[DetailPanelBuilder.AddonMode] '{capturedName}' selection changed to: '{selected}'");

            if (selected == "Select")
            {
                List<string>? current = _window.ViewModel.GameNameServiceInstance.PerGameAddonSelection.TryGetValue(gameName, out var existingAddons)
                    ? existingAddons
                    : null;

                IAddonPackService? addonPackService = null;
                var addonSvcProp = _window.ViewModel.GetType().GetProperty("AddonPackServiceInstance");
                if (addonSvcProp != null)
                    addonPackService = addonSvcProp.GetValue(_window.ViewModel) as IAddonPackService;

                if (addonPackService == null)
                {
                    var infoDlg = new ContentDialog
                    {
                        Title = "Select Addons",
                        Content = new TextBlock
                        {
                            Text = "Addon service is not yet wired. Complete Task 9.1 to enable addon selection.",
                            FontSize = 13,
                            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                        },
                        CloseButtonText = "OK",
                        XamlRoot = _window.Content.XamlRoot,
                        Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                        RequestedTheme = ElementTheme.Dark,
                    };
                    await DialogService.ShowSafeAsync(infoDlg);
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
                    _window.ViewModel.GameNameServiceInstance.PerGameAddonSelection[gameName] = result;
                    _window.ViewModel.SetPerGameAddonMode(capturedName, "Select");
                    _window.ViewModel.DeployAddonsForCard(capturedName);
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
            {
                _window.ViewModel.SetPerGameAddonMode(capturedName, "Off");
                _window.ViewModel.DeployAddonsForCard(capturedName);
            }
            else // "Global"
            {
                _window.ViewModel.SetPerGameAddonMode(capturedName, "Global");
                _window.ViewModel.DeployAddonsForCard(capturedName);
            }
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

        shadersAddonsLeftColumn.Children.Add(shaderAddonGrid);

        // "Select ReShade Preset" button
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
            Margin = new Thickness(0, 8, 0, 0),
        };
        ToolTipService.SetToolTip(presetBtn,
            "Pick .ini preset files to copy to this game's folder. Place presets in the reshade-presets folder.");
        presetBtn.Click += async (s, ev) =>
        {
            var selected = await PresetPopupHelper.ShowAsync(_window.Content.XamlRoot);
            if (selected != null && selected.Count > 0)
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && !string.IsNullOrEmpty(targetCard.InstallPath))
                {
                    int count = PresetPopupHelper.DeployPresets(selected, targetCard.InstallPath);
                    CrashReporter.Log($"[DetailPanelBuilder] Deployed {count} preset(s) to '{capturedName}'");

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
                            await _window.ViewModel.ApplyPresetShadersAsync(capturedName, presetPaths);

                            // Rebuild overrides panel so the shader combo reflects the new "Select" mode
                            var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                            if (refreshCard != null)
                                BuildOverridesPanel(refreshCard);
                        }
                    }
                }
            }
        };
        shadersAddonsLeftColumn.Children.Add(presetBtn);

        Grid.SetColumn(shadersAddonsLeftColumn, 0);
        shadersAddonsRowGrid.Children.Add(shadersAddonsLeftColumn);

        // Vertical divider
        var shadersAddonsDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(shadersAddonsDivider, 1);
        shadersAddonsRowGrid.Children.Add(shadersAddonsDivider);

        // ── Right column: "Launch executable" (grid-aligned with left column) ──
        var shadersAddonsRightColumn = new Grid { RowSpacing = 6 };
        shadersAddonsRightColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: label + spacer (matches left title + sub-labels)
        shadersAddonsRightColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: exe path + args side by side
        shadersAddonsRightColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: buttons (aligns with preset btn)

        // Label + spacer to match left column's "Shaders and Addons" title + sub-label row
        var launchExeHeaderPanel = new StackPanel { Spacing = 4 };
        launchExeHeaderPanel.Children.Add(new TextBlock
        {
            Text = "Launch executable",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        // Invisible spacer matching the "Shaders" / "Addons" sub-label height
        launchExeHeaderPanel.Children.Add(new TextBlock
        {
            Text = " ",
            FontSize = 11,
        });
        Grid.SetRow(launchExeHeaderPanel, 0);
        shadersAddonsRightColumn.Children.Add(launchExeHeaderPanel);

        var currentLaunchExe = _window.ViewModel.GameNameServiceInstance.LaunchExeOverrides
            .TryGetValue(capturedName, out var savedExe) ? savedExe : "";
        var launchExeBox = new TextBox
        {
            Text = currentLaunchExe,
            PlaceholderText = "Auto-detect (or paste path)",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(launchExeBox,
            "Override the executable used when launching this game. Leave blank for auto-detection (largest exe in install folder).");
        launchExeBox.LostFocus += (s, ev) =>
        {
            var newPath = launchExeBox.Text.Trim();
            if (string.IsNullOrEmpty(newPath))
                _window.ViewModel.GameNameServiceInstance.LaunchExeOverrides.Remove(capturedName);
            else
                _window.ViewModel.GameNameServiceInstance.LaunchExeOverrides[capturedName] = newPath;
            _window.ViewModel.SaveSettingsPublic();
        };

        var currentLaunchArgs = _window.ViewModel.GameNameServiceInstance.LaunchArgsOverrides
            .TryGetValue(capturedName, out var savedArgs) ? savedArgs : "";
        var launchArgsBox = new TextBox
        {
            Text = currentLaunchArgs,
            PlaceholderText = "Launch arguments",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var argsTooltip = "Command-line arguments passed to the game on launch. Saves on focus lost.";
        if (card.Source.Equals("Epic", StringComparison.OrdinalIgnoreCase))
            argsTooltip += "\n\nNote: Setting arguments disables Epic protocol launch. EOS-protected games may fail to launch with arguments.";
        ToolTipService.SetToolTip(launchArgsBox, argsTooltip);
        launchArgsBox.LostFocus += (s, ev) =>
        {
            var newArgs = launchArgsBox.Text.Trim();
            if (string.IsNullOrEmpty(newArgs))
                _window.ViewModel.GameNameServiceInstance.LaunchArgsOverrides.Remove(capturedName);
            else
                _window.ViewModel.GameNameServiceInstance.LaunchArgsOverrides[capturedName] = newArgs;
            _window.ViewModel.SaveSettingsPublic();
        };

        var launchBoxRow = new Grid { ColumnSpacing = 8 };
        launchBoxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        launchBoxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(launchExeBox, 0);
        Grid.SetColumn(launchArgsBox, 1);
        launchBoxRow.Children.Add(launchExeBox);
        launchBoxRow.Children.Add(launchArgsBox);

        Grid.SetRow(launchBoxRow, 1);
        shadersAddonsRightColumn.Children.Add(launchBoxRow);

        var launchBtnRow = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
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
                _window.ViewModel.GameNameServiceInstance.LaunchExeOverrides[capturedName] = filePath;
                _window.ViewModel.SaveSettingsPublic();
            }
        };
        Grid.SetColumn(browseLaunchBtn, 0);
        ToolTipService.SetToolTip(browseLaunchBtn, "Browse for a game executable to use as the launch target.");
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
            _window.ViewModel.GameNameServiceInstance.LaunchExeOverrides.Remove(capturedName);
            _window.ViewModel.SaveSettingsPublic();
        };
        Grid.SetColumn(resetLaunchBtn, 1);
        ToolTipService.SetToolTip(resetLaunchBtn, "Clear the launch executable override and revert to auto-detection.");
        launchBtnRow.Children.Add(resetLaunchBtn);
        Grid.SetRow(launchBtnRow, 2);
        shadersAddonsRightColumn.Children.Add(launchBtnRow);

        Grid.SetColumn(shadersAddonsRightColumn, 2);
        shadersAddonsRowGrid.Children.Add(shadersAddonsRightColumn);

        _window.OverridesPanel.Children.Add(shadersAddonsRowGrid);

        // Forward-declare DXVK toggle so the reset handler can reference it
        ToggleSwitch dxvkToggle = null!;

        var resetOverridesBtn = new Button
        {
            Content = "Reset Overrides",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        resetOverridesBtn.Click += (s, ev) =>
        {
            // Reset all controls to defaults
            detectedBox.Text = originalStoreName ?? gameName;
            wikiBox.Text = "";
            shaderComboInitializing = true;
            shaderModeCombo.SelectedItem = "Global";
            shaderComboInitializing = false;
            addonComboInitializing = true;
            addonModeCombo.SelectedItem = "Global";
            addonComboInitializing = false;
            if (renderPathCombo != null) renderPathCombo.SelectedItem = "DirectX";
            dllOverrideToggle.IsOn = false;
            // Reset update inclusion to all-included
            if (_window.ViewModel.IsUpdateAllExcludedReShade(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedUl(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionUl(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedDc(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDc(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedOs(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionOs(capturedName);
            UpdateInclusionHelper.RefreshSummary(updateSummaryText, _window.ViewModel, capturedName, card.IsREEngineGame, card.DxvkEnabled);
            wikiExcludeCombo.SelectedItem = "Included";

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

            // Shader mode → Global
            if (_window.ViewModel.GetPerGameShaderMode(capturedName) != "Global")
            {
                _window.ViewModel.SetPerGameShaderMode(capturedName, "Global");
                _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(capturedName);
                _window.ViewModel.DeployShadersForCard(capturedName);
            }

            // Addon mode → Global
            if (_window.ViewModel.GetPerGameAddonMode(capturedName) != "Global")
            {
                _window.ViewModel.SetPerGameAddonMode(capturedName, "Global");
                _window.ViewModel.DeployAddonsForCard(capturedName);
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
            if (_window.ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedUl(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionUl(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedDc(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDc(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedOs(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionOs(capturedName);

            // Disable wiki exclusion
            if (_window.ViewModel.IsWikiExcluded(capturedName))
                _window.ViewModel.ToggleWikiExclusion(capturedName);

            // Reset Normal ReShade (via channel combo)
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && targetCard.UseNormalReShade)
                    _window.ViewModel.SetUseNormalReShade(targetCard, false);
            }

            // Reset DXVK toggles
            if (dxvkToggle != null)
            {
                dxvkToggle.IsOn = false;
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && targetCard.DxvkEnabled)
                    _ = _window.ViewModel.HandleDxvkToggleAsync(targetCard, false, _window.Content.XamlRoot);
            }

            // Reset DXVK update exclusion via the shared Update Inclusion system
            if (_window.ViewModel.IsUpdateAllExcludedDxvk(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDxvk(capturedName);

            // Reset bitness override to Auto
            bitnessCombo.SelectedItem = "Auto";
            _window.ViewModel.SetBitnessOverride(capturedName, null);

            // Reset API overrides
            apiCombo.SelectedItem = "Auto";
            _window.ViewModel.SetApiOverride(capturedName, null);

            // Reset ReShade channel override
            channelCombo.SelectedItem = "Global";
            _window.ViewModel.SetReShadeChannelOverride(capturedName, null);

            // Reset launch exe override
            _window.ViewModel.GameNameServiceInstance.LaunchExeOverrides.Remove(capturedName);
            _window.ViewModel.GameNameServiceInstance.LaunchArgsOverrides.Remove(capturedName);
            _window.ViewModel.SaveSettingsPublic();
            launchExeBox.Text = "";
            launchArgsBox.Text = "";

            // Revert card properties to auto-detected values
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                {
                    // Re-resolve bitness from PE header auto-detection
                    var detectedMachine = _window.ViewModel.PeHeaderServiceInstance.DetectGameArchitecture(targetCard.InstallPath);
                    targetCard.Is32Bit = _window.ViewModel.ResolveIs32Bit(capturedName, detectedMachine);

                    // Re-detect APIs from scanning (overrides are now cleared)
                    targetCard.DetectedApis = _window.ViewModel._DetectAllApisForCard(targetCard.InstallPath, capturedName);
                    targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
                    targetCard.GraphicsApi = _window.ViewModel.DetectGraphicsApi(
                        targetCard.InstallPath, EngineType.Unknown, capturedName);

                    // Bitness changed — no need to update placeholder

                    targetCard.NotifyAll();
                }
            }

            // Reset DLSS presets to Default
            {
                var presetSvc = _window.ViewModel.DlssPresetServiceInstance;
                if (presetSvc.IsSupported)
                {
                    presetSvc.SetSrPreset(capturedName, card.InstallPath, 0);
                    presetSvc.SetRrPreset(capturedName, card.InstallPath, 0);
                    presetSvc.SetFgPreset(capturedName, card.InstallPath, 0);
                }
            }

            CrashReporter.Log($"[DetailPanelBuilder.BuildOverridesPanel] Overrides reset for: {capturedName}");

            // Only reselect if the game name actually changed
            if (nameChanged)
                _window.RequestReselect(capturedName);
        };
        // resetOverridesBtn is hidden — triggered via Management panel automation peer
        resetOverridesBtn.Visibility = Visibility.Collapsed;
        _window.OverridesPanel.Children.Add(resetOverridesBtn);

        // ══════════════════════════════════════════════════════════════════════
        // Nvidia Profile Overrides — separate section below Overrides
        // DLSS / Streamline / ReBAR + future additions
        // ══════════════════════════════════════════════════════════════════════
        _window.NvidiaProfilePanel.Children.Clear();
        var nvidiaHeaderText = "Nvidia Profile Overrides";
        var driverVer = _window.ViewModel.DlssPresetServiceInstance.DriverVersionString;
        if (!string.IsNullOrEmpty(driverVer))
            nvidiaHeaderText += $" — Driver {driverVer}";
        _window.NvidiaProfilePanel.Children.Add(new TextBlock
        {
            Text = nvidiaHeaderText,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        if (card.HasAnyDlssStreamline)
        {
            var dlssService = _window.ViewModel.DlssStreamlineServiceInstance;
            var presetService = _window.ViewModel.DlssPresetServiceInstance;
            bool hasDlss = card.HasDlss;
            bool hasDlssd = card.HasDlssd;
            bool hasDlssg = card.HasDlssg;
            bool hasStreamline = card.HasStreamline;

            var dlssRowGrid = new Grid { ColumnSpacing = 12 };
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dlssRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // SR column
            // Disable for DLSS 1.x (not compatible with 2.x+ versions in manifest)
            bool srEnabled = hasDlss && !(card.DlssInstalledVersion?.StartsWith("1.") == true);
            bool srDriverOverride = presetService.IsSupported && presetService.IsSrDriverOverrideActive(card.GameName, card.InstallPath ?? "");
            var srCol = BuildDlssColumn("DLSS", srEnabled, dlssService.DlssVersions,
                card.DlssInstalledVersion, DlssPresetService.SrPresets,
                presetService.IsSupported && srEnabled ? presetService.GetSrPreset(card.GameName, card.InstallPath) : 0u,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.DlssPath == null) return;
                    if (version == "Default") dlssService.Restore(tc.DlssDetection.DlssPath);
                    else if (version == "Custom") await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssPath);
                    else await dlssService.SwapDlssAsync(tc.DlssDetection.DlssPath, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                (preset) => { presetService.SetSrPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                currentRenderScale: presetService.IsSupported && srEnabled ? presetService.GetSrRenderScale(card.GameName, card.InstallPath) : 0u,
                onRenderScaleSelected: (pct) => { presetService.SetSrRenderScale(card.GameName, card.InstallPath, pct); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                originalVersion: card.DlssDetection?.OriginalDlssVersion,
                driverOverrideActive: srDriverOverride);
            Grid.SetColumn(srCol, 0);
            dlssRowGrid.Children.Add(srCol);

            dlssRowGrid.Children.Add(MakeDlssDivider(1));

            // RR column
            bool rrDriverOverride = presetService.IsSupported && presetService.IsRrDriverOverrideActive(card.GameName, card.InstallPath ?? "");
            var rrCol = BuildDlssColumn("Ray Reconstruction", hasDlssd, dlssService.DlssdVersions,
                card.DlssdInstalledVersion, DlssPresetService.RrPresets,
                presetService.IsSupported && hasDlssd ? presetService.GetRrPreset(card.GameName, card.InstallPath) : 0u,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.DlssdPath == null) return;
                    if (version == "Default") dlssService.Restore(tc.DlssDetection.DlssdPath);
                    else if (version == "Custom") await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssdPath);
                    else await dlssService.SwapDlssdAsync(tc.DlssDetection.DlssdPath, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                (preset) => { presetService.SetRrPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                currentRenderScale: presetService.IsSupported && hasDlssd ? presetService.GetRrRenderScale(card.GameName, card.InstallPath) : 0u,
                onRenderScaleSelected: (pct) => { presetService.SetRrRenderScale(card.GameName, card.InstallPath, pct); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                originalVersion: card.DlssDetection?.OriginalDlssdVersion,
                driverOverrideActive: rrDriverOverride);
            Grid.SetColumn(rrCol, 2);
            dlssRowGrid.Children.Add(rrCol);

            dlssRowGrid.Children.Add(MakeDlssDivider(3));

            // FG column — no v1.x guard (FG can be updated from v1.0.0 to newer versions)
            bool fgEnabled = hasDlssg;
            bool fgDriverOverride = presetService.IsSupported && presetService.IsFgDriverOverrideActive(card.GameName, card.InstallPath ?? "");
            var fgCol = BuildDlssColumn("Frame Generation", fgEnabled, dlssService.DlssgVersions,
                card.DlssgInstalledVersion, DlssPresetService.FgPresets,
                presetService.IsSupported && fgEnabled ? presetService.GetFgPreset(card.GameName, card.InstallPath) : 0u,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.DlssgPath == null) return;
                    if (version == "Default") dlssService.Restore(tc.DlssDetection.DlssgPath);
                    else if (version == "Custom") await dlssService.SwapDlssCustomAsync(tc.DlssDetection.DlssgPath);
                    else await dlssService.SwapDlssgAsync(tc.DlssDetection.DlssgPath, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                (preset) => { presetService.SetFgPreset(card.GameName, card.InstallPath, preset); _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card)); },
                originalVersion: card.DlssDetection?.OriginalDlssgVersion,
                driverOverrideActive: fgDriverOverride);

            // Add Multi Frame Generation button to FG column
            fgCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            var mfgBtn = new Button
            {
                Content = "Multi Frame Gen",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = fgEnabled && presetService.IsSupported,
                Opacity = (fgEnabled && presetService.IsSupported) ? 1.0 : 0.4,
            };
            ToolTipService.SetToolTip(mfgBtn, "Configure NVIDIA Multi Frame Generation: mode, frame count multiplier, and dynamic target frame rate. Requires 50 Series GPU.");
            mfgBtn.Click += async (s, ev) =>
            {
                var xamlRoot = (s as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
                await MfgDialog.ShowAsync(
                    presetService,
                    _window.ViewModel.Settings,
                    capturedName,
                    card.InstallPath ?? "",
                    xamlRoot,
                    () => _window.ViewModel.SaveSettingsPublic());
                // Rebuild panel so Restore All button reflects MFG changes
                var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard != null)
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(refreshCard));
            };
            fgCol.Children.Add(mfgBtn);

            Grid.SetColumn(fgCol, 4);
            dlssRowGrid.Children.Add(fgCol);

            dlssRowGrid.Children.Add(MakeDlssDivider(5));

            // SL column (no preset)
            // Disable for Streamline v1.x (not compatible with v2.x+ versions in manifest)
            bool slEnabled = hasStreamline && !(card.StreamlineInstalledVersion?.StartsWith("1.") == true);
            // Check if custom Streamline marker exists — override version to "Custom"
            var slInstalledVersion = (hasStreamline && !string.IsNullOrEmpty(card.DlssDetection?.StreamlineFolder)
                && DlssStreamlineService.IsCustomStreamlineActive(card.DlssDetection.StreamlineFolder))
                ? "Custom"
                : card.StreamlineInstalledVersion;
            var slCol = BuildDlssColumn("Streamline", slEnabled, dlssService.StreamlineVersions,
                slInstalledVersion, null, 0,
                async (version) =>
                {
                    var tc = _window.ViewModel.AllCards.FirstOrDefault(c => c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (tc?.DlssDetection?.StreamlineFolder == null) return;
                    if (version == "Default") dlssService.RestoreStreamline(tc.DlssDetection.StreamlineFolder);
                    else if (version == "Custom") await dlssService.SwapStreamlineCustomAsync(tc.DlssDetection.StreamlineFolder);
                    else await dlssService.SwapStreamlineAsync(tc.DlssDetection.StreamlineFolder, version);
                    tc.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(tc));
                },
                null,
                originalVersion: card.DlssDetection?.OriginalStreamlineVersion);

            // Add Restore All button into the SL column (fills the preset slot)
            // Enabled when any backup exists OR any preset is non-default
            bool hasNonDefaultPreset = (presetService.IsSupported && hasDlss && presetService.GetSrPreset(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlssd && presetService.GetRrPreset(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlssg && presetService.GetFgPreset(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlss && presetService.GetSrRenderScale(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlssd && presetService.GetRrRenderScale(card.GameName, card.InstallPath) != 0)
                || (presetService.IsSupported && hasDlssg && presetService.GetMfgMode(card.GameName, card.InstallPath) != 0);
            bool restoreEnabled = card.HasAnyDlssBackup || hasNonDefaultPreset;
            var dlssRestoreBtn = new Button
            {
                Content = "Restore DLSS/SL",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = restoreEnabled ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = restoreEnabled ? UIFactory.Brush(ResourceKeys.AccentBlueBrush) : UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = restoreEnabled ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = restoreEnabled,
            };
            dlssRestoreBtn.Click += (s, ev) =>
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard?.DlssDetection != null)
                {
                    dlssService.RestoreAll(targetCard.DlssDetection);
                    presetService.SetSrPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetRrPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetFgPreset(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetSrRenderScale(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetRrRenderScale(targetCard.GameName, targetCard.InstallPath, 0);
                    // Reset MFG settings
                    presetService.SetMfgMode(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetMfgGenerationFactor(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetMfgDynamicMaxCount(targetCard.GameName, targetCard.InstallPath, 0);
                    presetService.SetMfgDynamicTargetFps(targetCard.GameName, targetCard.InstallPath, 0);
                    targetCard.RefreshDlssVersions(dlssService);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(targetCard));
                }
            };
            // Add spacer label to align buttons with the Preset/RenderScale rows in other columns
            slCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });

            // Quick Apply button (created below, added here after creation)
            // Spacer + Restore All (added after Quick Apply is created)
            var hasDefaults = !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssVersion)
                || !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssdVersion)
                || !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultDlssgVersion)
                || !string.IsNullOrEmpty(_window.ViewModel.Settings.DefaultStreamlineVersion)
                || _window.ViewModel.Settings.DefaultSrPreset != 0
                || _window.ViewModel.Settings.DefaultRrPreset != 0
                || _window.ViewModel.Settings.DefaultFgPreset != 0
                || _window.ViewModel.Settings.DefaultSrRenderScale != 0
                || _window.ViewModel.Settings.DefaultRrRenderScale != 0;

            var applyBtn = new Button
            {
                Content = "Quick Apply",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = hasDefaults ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = hasDefaults ? UIFactory.Brush(ResourceKeys.AccentBlueBrush) : UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = hasDefaults ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = hasDefaults && card.HasAnyDlssStreamline,
            };
            ToolTipService.SetToolTip(applyBtn, "Apply your configured DLSS/Streamline default versions, presets, and render scales to this game. Downloads versions on-demand if not cached.");
            applyBtn.Click += async (s, ev) =>
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard?.DlssDetection == null) return;

                var settings = _window.ViewModel.Settings;
                var svc = _window.ViewModel.DlssStreamlineServiceInstance;
                var pSvc = _window.ViewModel.DlssPresetServiceInstance;

                // Check driver override state — skip DLL swaps for overridden components
                bool srOverride = pSvc.IsSupported && pSvc.IsSrDriverOverrideActive(targetCard.GameName, targetCard.InstallPath ?? "");
                bool rrOverride = pSvc.IsSupported && pSvc.IsRrDriverOverrideActive(targetCard.GameName, targetCard.InstallPath ?? "");
                bool fgOverride = pSvc.IsSupported && pSvc.IsFgDriverOverrideActive(targetCard.GameName, targetCard.InstallPath ?? "");

                if (!string.IsNullOrEmpty(settings.DefaultDlssVersion) && targetCard.HasDlss && targetCard.DlssDetection.DlssPath != null
                    && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true) && !srOverride)
                    await svc.SwapDlssAsync(targetCard.DlssDetection.DlssPath, settings.DefaultDlssVersion);
                if (!string.IsNullOrEmpty(settings.DefaultDlssdVersion) && targetCard.HasDlssd && targetCard.DlssDetection.DlssdPath != null
                    && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true) && !rrOverride)
                    await svc.SwapDlssdAsync(targetCard.DlssDetection.DlssdPath, settings.DefaultDlssdVersion);
                if (!string.IsNullOrEmpty(settings.DefaultDlssgVersion) && targetCard.HasDlssg && targetCard.DlssDetection.DlssgPath != null
                    && !fgOverride)
                    await svc.SwapDlssgAsync(targetCard.DlssDetection.DlssgPath, settings.DefaultDlssgVersion);
                if (!string.IsNullOrEmpty(settings.DefaultStreamlineVersion) && targetCard.HasStreamline && targetCard.DlssDetection.StreamlineFolder != null
                    && !(targetCard.StreamlineInstalledVersion?.StartsWith("1.") == true))
                    await svc.SwapStreamlineAsync(targetCard.DlssDetection.StreamlineFolder, settings.DefaultStreamlineVersion);

                if (settings.DefaultSrPreset != 0 && targetCard.HasDlss && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true))
                {
                    var curSrPreset = pSvc.GetSrPreset(targetCard.GameName, targetCard.InstallPath);
                    if (curSrPreset != 0x00FFFFFF && curSrPreset != 0x00FFFFFE)
                        pSvc.SetSrPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultSrPreset);
                }
                if (settings.DefaultRrPreset != 0 && targetCard.HasDlssd && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true))
                {
                    var curRrPreset = pSvc.GetRrPreset(targetCard.GameName, targetCard.InstallPath);
                    if (curRrPreset != 0x00FFFFFF && curRrPreset != 0x00FFFFFE)
                        pSvc.SetRrPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultRrPreset);
                }
                if (settings.DefaultFgPreset != 0 && targetCard.HasDlssg && !(targetCard.DlssgInstalledVersion?.StartsWith("1.") == true))
                {
                    var curFgPreset = pSvc.GetFgPreset(targetCard.GameName, targetCard.InstallPath);
                    if (curFgPreset != 0x00FFFFFF && curFgPreset != 0x00FFFFFE)
                        pSvc.SetFgPreset(targetCard.GameName, targetCard.InstallPath, settings.DefaultFgPreset);
                }

                if (settings.DefaultSrRenderScale != 0 && targetCard.HasDlss && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetSrRenderScale(targetCard.GameName, targetCard.InstallPath, settings.DefaultSrRenderScale);
                if (settings.DefaultRrRenderScale != 0 && targetCard.HasDlssd && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true))
                    pSvc.SetRrRenderScale(targetCard.GameName, targetCard.InstallPath, settings.DefaultRrRenderScale);

                targetCard.RefreshDlssVersions(svc);
                _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(targetCard));
            };

            // Add buttons to SL column: Quick Apply first, then spacer, then Restore All at bottom
            slCol.Children.Add(applyBtn);
            slCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            slCol.Children.Add(dlssRestoreBtn);

            // Override column opacity so buttons aren't dimmed by the SL column's 0.4 opacity.
            // Manually dim the SL label and version combo if Streamline isn't present.
            if ((hasDefaults || restoreEnabled) && !slEnabled)
            {
                slCol.Opacity = 1.0;
                // Dim the SL-specific children (label, version sub-label, combo, etc.) but not buttons
                foreach (var child in slCol.Children.OfType<UIElement>())
                {
                    if (child != applyBtn && child != dlssRestoreBtn)
                        child.Opacity = 0.4;
                }
            }
            ToolTipService.SetToolTip(dlssRestoreBtn, "Restore all DLSS and Streamline DLLs to their original game versions and reset presets to Default.");

            Grid.SetColumn(slCol, 6);
            dlssRowGrid.Children.Add(slCol);

            _window.NvidiaProfilePanel.Children.Add(dlssRowGrid);
        }

        // ══════════════════════════════════════════════════════════════════════
        // Nvidia Profile Settings — VSync, Latency, Smooth Motion, Power/CPU, ReBAR
        // ══════════════════════════════════════════════════════════════════════
        var nvidiaPresetService = _window.ViewModel.DlssPresetServiceInstance;
        if (nvidiaPresetService.IsSupported)
        {
            bool isAdmin = VulkanLayerService.IsRunningAsAdmin();

            _window.NvidiaProfilePanel.Children.Add(UIFactory.MakeSeparator());

            var nvidiaGrid = new Grid { ColumnSpacing = 12, Opacity = isAdmin ? 1.0 : 0.4, IsHitTestVisible = isAdmin };
            // 4 columns with dividers between: col0 | div1 | col2 | div3 | col4 | div5 | col6
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var installPathSafe = card.InstallPath ?? "";

            // ── Column 0: VSync ──
            var vsyncCol = new StackPanel { Spacing = 4 };
            var vsyncLabel = new TextBlock { Text = "VSync", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(vsyncLabel, "Vertical Sync settings — controls how the driver synchronizes frame rendering with your display's refresh rate.");
            vsyncCol.Children.Add(vsyncLabel);

            // VSync Mode
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.VSyncModeOptions;
                uint current = nvidiaPresetService.GetVSyncMode(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo, "VSync Mode — App Controlled: let the game decide. Force Off: disables VSync entirely. Force On: locks to refresh rate. Fast Sync: renders freely, displays latest complete frame.");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetVSyncMode(card.GameName, installPathSafe, options[i].Value);
                };
                vsyncCol.Children.Add(combo);
                init = false;
            }

            // VSync Tear Control
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Tear Control", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.VSyncTearControlOptions;
                uint current = nvidiaPresetService.GetVSyncTearControl(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo, "VSync Tear Control — Standard: normal VSync behavior. Adaptive: VSync on when FPS ≥ refresh rate, off when below (reduces stuttering at low FPS).");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetVSyncTearControl(card.GameName, installPathSafe, options[i].Value);
                };
                vsyncCol.Children.Add(combo);
                init = false;
            }

            // Low Latency Mode (in VSync column)
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Low Latency", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.LowLatencyModeOptions;
                uint current = nvidiaPresetService.GetLowLatencyMode(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo2 = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo2, "Low Latency Mode — Off: game controls frame queue. On: limits pre-rendered frames to 1 (lower latency). Ultra: just-in-time frame submission (lowest latency, may reduce FPS slightly).");
                var init2 = true;
                combo2.SelectionChanged += (s, ev) =>
                {
                    if (init2) return;
                    int i = combo2.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetLowLatencyMode(card.GameName, installPathSafe, options[i].Value);
                };
                vsyncCol.Children.Add(combo2);
                init2 = false;
            }

            Grid.SetColumn(vsyncCol, 0);
            nvidiaGrid.Children.Add(vsyncCol);
            nvidiaGrid.Children.Add(MakeDlssDivider(1));

            // ── Column 4: Smooth Motion ──
            var smoothCol = new StackPanel { Spacing = 4 };
            var smoothLabel = new TextBlock { Text = "Smooth Motion", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(smoothLabel, "NVIDIA Smooth Motion — driver-level frame generation. Adds interpolated frames for smoother visuals. RTX 40 Series+ required.");
            smoothCol.Children.Add(smoothLabel);

            // Enable
            bool smoothMotionEnabled;
            {
                smoothCol.Children.Add(new TextBlock { Text = "Enable", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.SmoothMotionEnableOptions;
                uint current = nvidiaPresetService.GetSmoothMotionEnable(card.GameName, installPathSafe);
                smoothMotionEnabled = current != 0;
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo, "Smooth Motion Enable — Off: disabled. On: enables driver-level frame generation (RTX 40 Series+ only).");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetSmoothMotionEnable(card.GameName, installPathSafe, options[i].Value);
                    // Cascade: set APIs to All when enabling, None when disabling
                    bool enabling = options[i].Value != 0;
                    nvidiaPresetService.SetSmoothMotionApis(card.GameName, installPathSafe, enabling ? 0x00000007u : 0x00000000u);
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card));
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            // APIs
            {
                smoothCol.Children.Add(new TextBlock { Text = "Allowed APIs", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.SmoothMotionApisOptions;
                uint current = nvidiaPresetService.GetSmoothMotionApis(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    IsEnabled = smoothMotionEnabled,
                    Opacity = smoothMotionEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(combo, "Smooth Motion APIs — which graphics APIs Smooth Motion is allowed to hook. None = disabled for all APIs.");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetSmoothMotionApis(card.GameName, installPathSafe, options[i].Value);
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            // Flip Pacing (combined — sets both Fullscreen and Windowed together)
            {
                smoothCol.Children.Add(new TextBlock { Text = "Flip Pacing", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.SmoothMotionFlipPacingFsOptions;
                uint current = nvidiaPresetService.GetSmoothMotionFlipPacingFs(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    IsEnabled = smoothMotionEnabled,
                    Opacity = smoothMotionEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(combo, "Flip Pacing — Off: prioritize lower latency. On: prioritize smoother frame pacing. Sets both fullscreen and windowed modes together.");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetSmoothMotionFlipPacingFs(card.GameName, installPathSafe, options[i].Value);
                    // Also set windowed pacing to the same value (use 0x00000001 for "On" instead of 0xFFFFFFFF)
                    uint winValue = options[i].Value == 0xFFFFFFFF ? 0x00000001 : options[i].Value;
                    nvidiaPresetService.SetSmoothMotionFlipPacingWin(card.GameName, installPathSafe, winValue);
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            Grid.SetColumn(smoothCol, 4);
            nvidiaGrid.Children.Add(smoothCol);
            nvidiaGrid.Children.Add(MakeDlssDivider(5));

            // ── Column 6: Power / CPU ──
            var powerCol = new StackPanel { Spacing = 4 };
            var powerLabel = new TextBlock { Text = "Power", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(powerLabel, "GPU power management. Maximum Performance prevents clock throttling. Optimal is NVIDIA's recommended default.");
            powerCol.Children.Add(powerLabel);

            // Power Management Mode
            {
                powerCol.Children.Add(new TextBlock { Text = "Power Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.PowerManagementOptions;
                uint current = nvidiaPresetService.GetPowerManagementMode(card.GameName, installPathSafe);
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(combo, "Power Management — Adaptive: GPU clocks down at idle. Maximum: locks GPU to highest clocks. Optimal: balanced (NVIDIA recommended).");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    nvidiaPresetService.SetPowerManagementMode(card.GameName, installPathSafe, options[i].Value);
                };
                powerCol.Children.Add(combo);
                init = false;
            }

            // Restore Profile Defaults button (aligned with Flip Pacing combo in Smooth Motion column)
            powerCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            var restoreProfileBtn = new Button
            {
                Content = "Restore Defaults",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = nvidiaPresetService.IsSupported,
            };
            ToolTipService.SetToolTip(restoreProfileBtn,
                "Restore this game's NVIDIA driver profile to factory defaults. Removes all custom settings (presets, render scale, MFG, driver overrides). This action is irreversible.");
            restoreProfileBtn.Click += async (s, ev) =>
            {
                var xamlRoot = (s as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
                var warningDialog = new ContentDialog
                {
                    Title = "Restore driver settings?",
                    Content = new TextBlock
                    {
                        Text = $"This will restore driver settings for {capturedName} back to the factory default. This action is irreversible.",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                    },
                    PrimaryButtonText = "Restore",
                    CloseButtonText = "Cancel",
                    XamlRoot = xamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };

                var result = await DialogService.ShowSafeAsync(warningDialog);
                if (result != ContentDialogResult.Primary) return;

                var success = nvidiaPresetService.RestoreProfileDefaults(capturedName, installPathSafe);
                if (success)
                {
                    var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (refreshCard != null)
                    {
                        // Also restore DLSS/Streamline DLLs to originals
                        if (refreshCard.DlssDetection != null)
                        {
                            var dlssSvc = _window.ViewModel.DlssStreamlineServiceInstance;
                            dlssSvc.RestoreAll(refreshCard.DlssDetection);
                            refreshCard.RefreshDlssVersions(dlssSvc);
                        }
                        _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(refreshCard));
                    }
                }
            };
            powerCol.Children.Add(restoreProfileBtn);

            Grid.SetColumn(powerCol, 6);
            nvidiaGrid.Children.Add(powerCol);

            // ── Column 8: ReBAR ──
            var rebarCol = new StackPanel { Spacing = 4 };
            var rebarLabel = new TextBlock { Text = "ReBAR", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(rebarLabel, "Resizable BAR — allows the CPU to access full GPU VRAM at once. Can improve performance by 5-10% in some titles. RTX 30+ and BIOS support required.");
            rebarCol.Children.Add(rebarLabel);

            bool rebarEnabled = nvidiaPresetService.GetReBarEnabled(card.GameName, installPathSafe);
            ulong rebarSizeLimit = nvidiaPresetService.GetReBarSizeLimit(card.GameName, installPathSafe);
            var globalReBarState = nvidiaPresetService.GetGlobalReBarEnabled();

            // Enable — with Global (On/Off) option when global is set
            {
                rebarCol.Children.Add(new TextBlock { Text = "Enable", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var enableItems = new List<string>();
                if (globalReBarState.HasValue)
                    enableItems.Add($"Global ({(globalReBarState.Value ? "On" : "Off")})");
                enableItems.Add("Off");
                enableItems.Add("On");

                // Determine selected index
                int enableIdx;
                if (globalReBarState.HasValue)
                {
                    // If per-game matches global, show "Global" selected; otherwise show the per-game value
                    bool perGameMatchesGlobal = rebarEnabled == globalReBarState.Value;
                    enableIdx = perGameMatchesGlobal ? 0 : (rebarEnabled ? 2 : 1); // Global=0, Off=1, On=2
                }
                else
                {
                    enableIdx = rebarEnabled ? 1 : 0; // Off=0, On=1
                }

                var rebarEnableCombo = new ComboBox
                {
                    ItemsSource = enableItems,
                    SelectedIndex = enableIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(rebarEnableCombo, globalReBarState.HasValue
                    ? "Global = inherit from global setting. On/Off = per-game override."
                    : "Off = ReBAR disabled. On = Force-enable ReBAR for this game.");
                var rebarComboInit = true;
                rebarEnableCombo.SelectionChanged += (s, ev) =>
                {
                    if (rebarComboInit) return;
                    var selected = rebarEnableCombo.SelectedItem as string;
                    if (selected != null && selected.StartsWith("Global"))
                    {
                        // Remove per-game override — inherit from global
                        // Delete the per-game setting by setting it to match global
                        bool globalVal = globalReBarState ?? false;
                        nvidiaPresetService.SetReBarEnabled(card.GameName, installPathSafe, globalVal, 2);
                    }
                    else
                    {
                        bool enabling = selected == "On";
                        nvidiaPresetService.SetReBarEnabled(card.GameName, installPathSafe, enabling, 2);
                    }
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card));
                };
                rebarCol.Children.Add(rebarEnableCombo);
                rebarComboInit = false;
            }

            // Mode
            {
                rebarCol.Children.Add(new TextBlock { Text = "Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                uint rebarMode = nvidiaPresetService.GetReBarMode(card.GameName, installPathSafe);
                var modeItems = new List<string>();
                if (globalReBarState.HasValue)
                    modeItems.Add("Global (Standard)");
                modeItems.AddRange(DlssPresetService.ReBarModes.Select(m => m.Name));

                int modeIdx;
                if (globalReBarState.HasValue)
                {
                    // Mode 0 = Standard = matches global default
                    modeIdx = rebarMode == 0 ? 0 : (Array.FindIndex(DlssPresetService.ReBarModes, m => m.Value == rebarMode) + 1);
                    if (modeIdx < 0) modeIdx = 0;
                }
                else
                {
                    modeIdx = Array.FindIndex(DlssPresetService.ReBarModes, m => m.Value == rebarMode);
                    if (modeIdx < 0) modeIdx = 0;
                }

                var rebarModeCombo = new ComboBox
                {
                    ItemsSource = modeItems,
                    SelectedIndex = modeIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    IsEnabled = rebarEnabled || (globalReBarState == true),
                    Opacity = (rebarEnabled || globalReBarState == true) ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(rebarModeCombo, "Standard = conservative. Optimized = aggressive driver scheduling (used by NVIDIA-whitelisted titles).");
                var modeComboInit = true;
                rebarModeCombo.SelectionChanged += (s, ev) =>
                {
                    if (modeComboInit) return;
                    int idx = rebarModeCombo.SelectedIndex;
                    if (idx < 0) return;
                    if (globalReBarState.HasValue)
                    {
                        if (idx == 0) return; // "Global (Standard)" — no override
                        uint newMode = DlssPresetService.ReBarModes[idx - 1].Value;
                        nvidiaPresetService.SetReBarMode(card.GameName, installPathSafe, newMode);
                    }
                    else
                    {
                        uint newMode = DlssPresetService.ReBarModes[idx].Value;
                        nvidiaPresetService.SetReBarMode(card.GameName, installPathSafe, newMode);
                    }
                };
                rebarCol.Children.Add(rebarModeCombo);
                modeComboInit = false;
            }

            // Size Limit — with Global option when global is set
            {
                rebarCol.Children.Add(new TextBlock { Text = "Size Limit", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });

                var sizeItems = new List<string>();
                var sizeValues = new List<ulong>();
                ulong globalSize = nvidiaPresetService.GetGlobalReBarSizeLimit();
                if (globalSize != 0)
                {
                    // Find the name for the global size
                    var globalSizeName = DlssPresetService.ReBarSizeLimits
                        .FirstOrDefault(sl => sl.Value == globalSize).Name ?? $"{globalSize / (1024*1024*1024)}GB";
                    sizeItems.Add($"Global ({globalSizeName})");
                    sizeValues.Add(0); // sentinel for "use global"
                }
                foreach (var sl in DlssPresetService.ReBarSizeLimits)
                {
                    sizeItems.Add(sl.Name);
                    sizeValues.Add(sl.Value);
                }

                int sizeIdx;
                if (globalSize != 0 && (rebarSizeLimit == 0 || rebarSizeLimit == globalSize))
                    sizeIdx = 0; // Global
                else
                {
                    var matchIdx = sizeValues.IndexOf(rebarSizeLimit);
                    sizeIdx = matchIdx >= 0 ? matchIdx : (globalSize != 0 ? 0 : 1); // Default: Global or 1GB
                }

                var rebarSizeCombo = new ComboBox
                {
                    ItemsSource = sizeItems,
                    SelectedIndex = sizeIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    IsEnabled = rebarEnabled || (globalReBarState == true),
                    Opacity = (rebarEnabled || globalReBarState == true) ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(rebarSizeCombo, "1GB is optimal for most games. Decrease to 512MB if experiencing ReBAR-related stutters.");
                var sizeComboInit = true;
                rebarSizeCombo.SelectionChanged += (s, ev) =>
                {
                    if (sizeComboInit) return;
                    int idx = rebarSizeCombo.SelectedIndex;
                    if (idx < 0) return;
                    ulong newSize = sizeValues[idx];
                    if (newSize == 0) return; // "Global" selected — no per-game override needed
                    nvidiaPresetService.SetReBarSizeLimit(card.GameName, installPathSafe, newSize);
                };
                rebarCol.Children.Add(rebarSizeCombo);
                sizeComboInit = false;
            }

            Grid.SetColumn(rebarCol, 2);
            nvidiaGrid.Children.Add(rebarCol);
            nvidiaGrid.Children.Add(MakeDlssDivider(3));

            _window.NvidiaProfilePanel.Children.Add(nvidiaGrid);
        }

        // Admin notice at the bottom of the Nvidia Profile section
        bool isElevated = VulkanLayerService.IsRunningAsAdmin();
        _window.NvidiaProfilePanel.Children.Add(new TextBlock
        {
            Text = isElevated
                ? "✓ Running as admin — all driver profile settings are writable."
                : "⚠ Admin rights required to write driver profile settings. Enable Admin Mode in Settings or restart as admin.",
            FontSize = 10,
            Foreground = UIFactory.Brush(isElevated ? ResourceKeys.TextTertiaryBrush : ResourceKeys.AccentAmberDimBrush),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });

        // ══════════════════════════════════════════════════════════════════════
        // DXVK section — separator + DXVK ComboBox (left), right reserved
        // ══════════════════════════════════════════════════════════════════════
        if (card.IsDxvkToggleVisible)
        {
            _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

            var dxvkRowGrid = new Grid { ColumnSpacing = 0 };
            dxvkRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dxvkRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            dxvkRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left column — DXVK ComboBox (Off / Global / Development / Stable / Lilium HDR)
            var dxvkModeItems = new[] { "Off", "Global", "Development", "Stable", "Lilium HDR" };
            string defaultDxvkSelection;
            if (!card.DxvkEnabled)
            {
                defaultDxvkSelection = "Off";
            }
            else
            {
                var currentDxvkOverride = _window.ViewModel.GetDxvkVariantOverride(gameName);
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
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard == null) return;

                if (selected == "Off")
                {
                    if (targetCard.DxvkEnabled)
                    {
                        await _window.ViewModel.HandleDxvkToggleAsync(targetCard, false, _window.Content.XamlRoot);
                        _window.ViewModel.SetDxvkVariantOverride(capturedName, null);
                        _window.PopulateDetailPanel(targetCard);
                        BuildOverridesPanel(targetCard);
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
                    _window.ViewModel.SetDxvkVariantOverride(capturedName, variantValue);

                    if (!targetCard.DxvkEnabled)
                    {
                        var resolvedVariant = _window.ViewModel.ResolveDxvkVariant(capturedName);
                        var savedVariant = _window.ViewModel.DxvkServiceInstance.SelectedVariant;
                        _window.ViewModel.DxvkServiceInstance.SelectedVariant = resolvedVariant;
                        await _window.ViewModel.HandleDxvkToggleAsync(targetCard, true, _window.Content.XamlRoot);
                        _window.ViewModel.DxvkServiceInstance.SelectedVariant = savedVariant;
                        if (!targetCard.DxvkEnabled) dxvkModeCombo.SelectedItem = "Off";
                        _window.PopulateDetailPanel(targetCard);
                        BuildOverridesPanel(targetCard);
                    }
                    else
                    {
                        var resolvedVariant = _window.ViewModel.ResolveDxvkVariant(capturedName);
                        var savedVariant = _window.ViewModel.DxvkServiceInstance.SelectedVariant;
                        _window.ViewModel.DxvkServiceInstance.SelectedVariant = resolvedVariant;
                        await _window.ViewModel.DxvkServiceInstance.EnsureStagingAsync();
                        if (_window.ViewModel.DxvkServiceInstance.IsStagingReady)
                            await _window.ViewModel.InstallDxvkAsync(targetCard, _window.Content.XamlRoot);
                        _window.ViewModel.DxvkServiceInstance.SelectedVariant = savedVariant;
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
            dxvkRowGrid.Children.Add(dxvkColumn);

            var dxvkDivider = new Border
            {
                Width = 1,
                Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(12, 0, 12, 0),
            };
            Grid.SetColumn(dxvkDivider, 1);
            dxvkRowGrid.Children.Add(dxvkDivider);

            _window.OverridesPanel.Children.Add(dxvkRowGrid);
        }
        // ── Management section (single row: 4 buttons side by side with separators) ──
        _window.ManagementPanel.Children.Clear();

        var mgmtRow = new Grid { ColumnSpacing = 0 };
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mgmtRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var changeFolderBtn = new Button
        {
            Content = "Change install folder",
            FontSize = 11,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = card,
        };
        changeFolderBtn.Click += (s, ev) => _window.BrowseFolder_Click(s, ev);
        ToolTipService.SetToolTip(changeFolderBtn, "Change the install folder for this game. Use when auto-detection picked the wrong directory.");
        Grid.SetColumn(changeFolderBtn, 0);
        mgmtRow.Children.Add(changeFolderBtn);

        var sep1 = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(sep1, 1);
        mgmtRow.Children.Add(sep1);

        var removeGameBtn = new Button
        {
            Content = "Reset / Remove game",
            FontSize = 11,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = card,
        };
        removeGameBtn.Click += (s, ev) => _window.RemoveManualGame_Click(s, ev);
        ToolTipService.SetToolTip(removeGameBtn, "Reset the install folder to auto-detected, or remove a manually added game entirely.");
        Grid.SetColumn(removeGameBtn, 2);
        mgmtRow.Children.Add(removeGameBtn);

        var sep2 = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(sep2, 3);
        mgmtRow.Children.Add(sep2);

        var mgmtResetOverridesBtn = new Button
        {
            Content = "Reset Overrides",
            FontSize = 11,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        mgmtResetOverridesBtn.Click += (s, ev) =>
        {
            var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(resetOverridesBtn)
                as Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer;
            peer?.Invoke();
        };
        Grid.SetColumn(mgmtResetOverridesBtn, 4);
        ToolTipService.SetToolTip(mgmtResetOverridesBtn, "Reset all per-game overrides back to defaults (DLL names, channels, shaders, addons, DXVK, launch settings, update inclusion).");
        mgmtRow.Children.Add(mgmtResetOverridesBtn);

        var sep3 = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(8, 4, 8, 4) };
        Grid.SetColumn(sep3, 5);
        mgmtRow.Children.Add(sep3);

        var reportBtn = new Button
        {
            Content = "Copy Report",
            FontSize = 11,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        reportBtn.Click += async (s, ev) =>
        {
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
                await GameReportEncoder.ShowAndCopyAsync(_window.Content.XamlRoot, targetCard, _window.ViewModel);
        };
        Grid.SetColumn(reportBtn, 6);
        ToolTipService.SetToolTip(reportBtn, "Copy a diagnostic report for this game to the clipboard. Useful for Discord or GitHub support.");
        mgmtRow.Children.Add(reportBtn);

        _window.ManagementPanel.Children.Add(mgmtRow);
    }

    /// <summary>
    /// Builds a single DLSS/Streamline column with label, version ComboBox, optional preset ComboBox, and optional render scale ComboBox.
    /// </summary>
    private StackPanel BuildDlssColumn(string label, bool isPresent,
        IReadOnlyList<string> availableVersions, string? installedVersion,
        (string Name, uint Value)[]? presets, uint currentPreset,
        Func<string, Task> onVersionSelected, Action<uint>? onPresetSelected,
        uint currentRenderScale = 0, Action<uint>? onRenderScaleSelected = null,
        string? originalVersion = null, bool driverOverrideActive = false)
    {
        var col = new StackPanel { Spacing = 4, Opacity = isPresent ? 1.0 : 0.4 };

        col.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        // Version ComboBox
        var versionLabel = new TextBlock { Text = "Version", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) };
        if (driverOverrideActive)
            ToolTipService.SetToolTip(versionLabel, "Driver override is active — the NVIDIA driver is injecting its own DLL. Disable 'Latest DLL' in NVIDIA App or Profile Inspector to manage versions manually.");
        col.Children.Add(versionLabel);

        // Build items list with (Default) marker on the game's original/default version
        var items = new List<string>();

        if (!isPresent && installedVersion == null)
        {
            // Game truly doesn't have this component — show "None"
            items.Add("None");
        }
        else
        {
            string? formattedOriginal = originalVersion != null
                ? DlssStreamlineService.FormatVersion(originalVersion)
                : (installedVersion != null ? installedVersion : null);
            bool defaultInList = false;

            foreach (var ver in availableVersions)
            {
                if (formattedOriginal != null && (ver.Equals(formattedOriginal, StringComparison.OrdinalIgnoreCase)
                    || ver.StartsWith(formattedOriginal, StringComparison.OrdinalIgnoreCase)
                    || formattedOriginal.StartsWith(ver, StringComparison.OrdinalIgnoreCase)))
                {
                    items.Add($"{ver} (Default)");
                    defaultInList = true;
                }
                else
                    items.Add(ver);
            }
            items.Add("Custom");

            // If original version isn't in the managed list, insert it at top with (Default)
            if (!defaultInList && formattedOriginal != null)
                items.Insert(0, $"{formattedOriginal} (Default)");
        }

        // Find selected index based on installed version
        int selectedIndex = 0;
        if (installedVersion != null && isPresent)
        {
            if (installedVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = items.Count - 1;
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var itemBase = items[i].Replace(" (Default)", "");
                    if (installedVersion.Equals(itemBase, StringComparison.OrdinalIgnoreCase)
                        || itemBase.StartsWith(installedVersion, StringComparison.OrdinalIgnoreCase)
                        || installedVersion.StartsWith(itemBase, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }
        }

        var versionCombo = new ComboBox
        {
            ItemsSource = driverOverrideActive ? new List<string> { "Driver Override Active" } : items,
            SelectedIndex = driverOverrideActive ? 0 : selectedIndex,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = isPresent && !driverOverrideActive,
            Opacity = driverOverrideActive ? 0.4 : 1.0,
        };
        if (driverOverrideActive)
            ToolTipService.SetToolTip(versionCombo, "Driver override is active — disable it in NVIDIA App or Profile Inspector to manage this DLL in RHI.");

        // When driver override is active, tooltip is already on the combo — no extra text needed
        col.Children.Add(versionCombo);

        bool versionInit = true;
        versionCombo.SelectionChanged += async (s, ev) =>
        {
            if (versionInit) return;
            var selected = versionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            // If it's the (Default) item, treat as "Default" (restore original)
            if (selected.EndsWith(" (Default)"))
                await onVersionSelected("Default");
            else
                await onVersionSelected(selected);
        };
        versionInit = false;

        // Preset ComboBox (only for SR, RR, FG)
        if (presets != null && isPresent)
        {
            col.Children.Add(new TextBlock { Text = "Preset", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });

            // Detect "Use recommended preset" driver override (NVIDIA App sets this)
            bool presetDriverOverride = currentPreset == 0x00FFFFFF || currentPreset == 0x00FFFFFE;

            if (presetDriverOverride)
            {
                // Set tooltip on the Preset label since disabled combos can't show tooltips
                var presetLabel = (TextBlock)col.Children[col.Children.Count - 1];
                ToolTipService.SetToolTip(presetLabel, "Driver override is active — NVIDIA App has 'Use recommended preset' enabled. Disable it in NVIDIA App → Game Settings or Profile Inspector.");
            }

            var presetItems = presetDriverOverride
                ? new List<string> { "Driver Override Active" }
                : presets.Select(p => p.Name).ToList();
            int presetIdx = 0;
            if (!presetDriverOverride)
            {
                for (int i = 0; i < presets.Length; i++)
                {
                    if (presets[i].Value == currentPreset) { presetIdx = i; break; }
                }
            }

            var presetCombo = new ComboBox
            {
                ItemsSource = presetItems,
                SelectedIndex = presetIdx,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = isPresent && !presetDriverOverride,
                Opacity = presetDriverOverride ? 0.4 : 1.0,
            };

            if (presetDriverOverride)
            {
                ToolTipService.SetToolTip(presetCombo, "The NVIDIA driver is overriding presets with 'Use recommended preset'. Disable this in NVIDIA App → Game Settings or NVIDIA Profile Inspector to use manual presets.");
            }
            else
            {
            // Add tooltip explaining presets
            string presetTooltip = label switch
            {
                "DLSS" => "Override the DLSS upscaling model. J/K use the 1st-gen transformer (DLSS 4.0). L/M use the 2nd-gen transformer (DLSS 4.5) with better temporal stability and less shimmer. M is tuned for Performance mode. Default lets the driver choose automatically.",
                "Ray Reconstruction" => "Override the Ray Reconstruction denoising model. Higher presets are newer model iterations. Results are game-dependent. Default lets the driver choose automatically.",
                "Frame Generation" => "Override the Frame Generation interpolation model. Higher presets are newer model iterations. Default lets the driver choose automatically.",
                _ => ""
            };
            if (!string.IsNullOrEmpty(presetTooltip))
                ToolTipService.SetToolTip(presetCombo, presetTooltip);
            }

            bool presetInit = true;
            presetCombo.SelectionChanged += (s, ev) =>
            {
                if (presetInit) return;
                var idx = presetCombo.SelectedIndex;
                if (idx >= 0 && idx < presets.Length)
                    onPresetSelected?.Invoke(presets[idx].Value);
            };
            presetInit = false;
            col.Children.Add(presetCombo);
        }

        // Render Scale ComboBox (only for SR and RR)
        if (onRenderScaleSelected != null && isPresent)
        {
            col.Children.Add(new TextBlock { Text = "Render Scale", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
            var rsOptions = DlssPresetService.RenderScaleOptions;
            var rsItems = rsOptions.Select(o => o.Name).ToList();

            // Determine current selection
            int rsIdx = 0; // Off
            if (currentRenderScale > 0)
            {
                // Check if it matches a named option
                int namedIdx = Array.FindIndex(rsOptions, o => o.Value == currentRenderScale);
                if (namedIdx >= 0)
                    rsIdx = namedIdx;
                else
                    rsIdx = rsItems.Count - 1; // Custom
            }

            // If Custom is selected, show the percentage in the item text
            if (rsIdx == rsItems.Count - 1 && currentRenderScale > 0)
                rsItems[^1] = $"Custom ({currentRenderScale}%)";

            var rsCombo = new ComboBox
            {
                ItemsSource = rsItems,
                SelectedIndex = rsIdx,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = isPresent,
            };
            ToolTipService.SetToolTip(rsCombo,
                "Override the DLSS render resolution scale. Off = game controls the scale.\nNamed presets set a fixed percentage. Custom lets you enter any value from 33-100%.");

            bool rsInit = true;
            rsCombo.SelectionChanged += (s, ev) =>
            {
                if (rsInit) return;
                var idx = rsCombo.SelectedIndex;
                if (idx < 0 || idx >= rsOptions.Length) return;

                if (rsOptions[idx].Name == "Custom")
                {
                    // Show a TextBox inline — replace the combo temporarily
                    var parent = rsCombo.Parent as StackPanel;
                    if (parent == null) return;
                    var comboIdx = parent.Children.IndexOf(rsCombo);
                    var inputBox = new TextBox
                    {
                        PlaceholderText = "33-100",
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        MaxLength = 3,
                    };
                    inputBox.KeyDown += (ks, ke) =>
                    {
                        if (ke.Key == Windows.System.VirtualKey.Enter)
                        {
                            if (uint.TryParse(inputBox.Text, out var val) && val >= 33 && val <= 100)
                            {
                                onRenderScaleSelected(val);
                            }
                            else
                            {
                                // Invalid — revert to Off
                                onRenderScaleSelected(0);
                            }
                        }
                        else if (ke.Key == Windows.System.VirtualKey.Escape)
                        {
                            // Cancel — revert
                            onRenderScaleSelected(currentRenderScale);
                        }
                    };
                    inputBox.LostFocus += (ls, le) =>
                    {
                        if (uint.TryParse(inputBox.Text, out var val) && val >= 33 && val <= 100)
                            onRenderScaleSelected(val);
                        else
                            onRenderScaleSelected(currentRenderScale); // revert
                    };
                    parent.Children[comboIdx] = inputBox;
                    inputBox.Focus(FocusState.Programmatic);
                }
                else
                {
                    onRenderScaleSelected(rsOptions[idx].Value);
                }
            };
            rsInit = false;
            col.Children.Add(rsCombo);
        }

        return col;
    }

    private static Border MakeDlssDivider(int column)
    {
        var divider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 0),
        };
        Grid.SetColumn(divider, column);
        return divider;
    }
}

