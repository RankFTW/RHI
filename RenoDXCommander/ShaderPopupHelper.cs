using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

/// <summary>
/// Builds and shows the shader selection ContentDialog.
/// Supports both global (Deploy/Cancel) and per-game (Confirm/Cancel) contexts.
/// </summary>
public static class ShaderPopupHelper
{
    public enum PopupContext { Global, PerGame }

    /// <summary>
    /// Shows the shader selection popup.
    /// Returns the list of selected pack IDs, or null if cancelled.
    /// Persists per-file exclusions as a side effect on confirm.
    /// </summary>
    public static async Task<List<string>?> ShowAsync(
        XamlRoot xamlRoot,
        IShaderPackService shaderPackService,
        List<string>? currentSelection,
        PopupContext context)
    {
        var packs = shaderPackService.AvailablePacks;
        var primaryButtonText = context == PopupContext.Global ? "Deploy" : "Confirm";

        // Handle empty packs state
        if (packs.Count == 0)
        {
            var emptyDlg = new ContentDialog
            {
                Title             = "Select Shader Packs",
                Content           = new TextBlock
                {
                    Text       = "No shader packs available.",
                    FontSize   = 13,
                    Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                },
                PrimaryButtonText      = primaryButtonText,
                IsPrimaryButtonEnabled = false,
                CloseButtonText        = "Cancel",
                XamlRoot               = xamlRoot,
                Background             = Brush(ResourceKeys.SurfaceOverlayBrush),
                RequestedTheme         = ElementTheme.Dark,
                MinWidth               = 750,
            };

            await DialogService.ShowSafeAsync(emptyDlg);
            return null;
        }

        var selected = new HashSet<string>(currentSelection ?? [], StringComparer.OrdinalIgnoreCase);

        // Pre-compute pack cache state and exclusions using async methods to avoid
        // blocking on _settingsLock while a shader pack download is in progress.
        var packCacheState = new Dictionary<string, (bool IsCached, HashSet<string> Exclusions)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (id, _, _) in shaderPackService.AvailablePacks)
        {
            var cached = await shaderPackService.IsPackCachedAsync(id);
            var excl   = cached
                ? await shaderPackService.GetExcludedFilesAsync(id)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            packCacheState[id] = (cached, excl);
        }

        // Build the include map once — used for dependency auto-select
        Dictionary<string, HashSet<string>> includeMap;
        try { includeMap = shaderPackService.BuildIncludeMap(); }
        catch { includeMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); }

        // Build a fallback ownership map for uncached packs — filename → packId.
        // Uses pre-computed packCacheState to avoid hitting the lock again.
        var uncachedPackOwnership = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (packId, _, _) in packs)
        {
            if (packCacheState.TryGetValue(packId, out var pcs) && pcs.IsCached) continue;
            foreach (var file in shaderPackService.GetPackShaderFiles(new[] { packId }))
                uncachedPackOwnership.TryAdd(file, packId);
        }

        var panel = new StackPanel { Spacing = 4 };

        // checkBoxes holds pack-level check boxes for dependency wiring
        var checkBoxes = new List<(string Id, CheckBox Box)>();

        // Per-pack file sub-panels (expand/collapse) and their expand buttons
        var fileSubPanels  = new Dictionary<string, StackPanel>(StringComparer.OrdinalIgnoreCase);
        var expandButtons  = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        // Per-pack file checkboxes: packId → list of (filename, checkbox)
        var fileCheckBoxes = new Dictionary<string, List<(string File, CheckBox Box)>>(StringComparer.OrdinalIgnoreCase);

        // Guard flag used by profile-loading to suppress checkbox event re-entrancy
        bool profileLoading = false;

        // ── Expand/Collapse All + Deselect All buttons ────────────────────────
        bool allExpanded = false;
        var expandAllBtn = new Button
        {
            Content             = "Expand All",
            FontSize            = 12,
            Padding             = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var deselectAllBtn = new Button
        {
            Content             = "Deselect All",
            FontSize            = 12,
            Padding             = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        expandAllBtn.Click += (s, ev) =>
        {
            allExpanded = !allExpanded;
            foreach (var (pid, sp) in fileSubPanels)
            {
                if (sp.Children.Count == 0) continue;
                sp.Visibility = allExpanded ? Visibility.Visible : Visibility.Collapsed;
                if (expandButtons.TryGetValue(pid, out var eb))
                    eb.Content = allExpanded ? "▼" : "▶";
            }
            expandAllBtn.Content = allExpanded ? "Collapse All" : "Expand All";
        };
        deselectAllBtn.Click += (s, ev) =>
        {
            profileLoading = true;
            try
            {
                foreach (var (_, box) in checkBoxes)
                    box.IsChecked = false;
                foreach (var (_, fcList) in fileCheckBoxes)
                    foreach (var (_, fcb) in fcList)
                        fcb.IsChecked = false;
                foreach (var (pid, sp) in fileSubPanels)
                {
                    sp.Visibility = Visibility.Collapsed;
                    if (expandButtons.TryGetValue(pid, out var eb))
                        eb.Content = "▶";
                }
                allExpanded = false;
                expandAllBtn.Content = "Expand All";
            }
            finally { profileLoading = false; }
        };
        var topButtonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing     = 6,
            Margin      = new Thickness(0, 0, 0, 8),
        };
        topButtonRow.Children.Add(expandAllBtn);
        topButtonRow.Children.Add(deselectAllBtn);

        // ── Search box (right of Deselect All) ───────────────────────────────
        var searchBox = new TextBox
        {
            PlaceholderText = "Search packs or shaders...",
            FontSize        = 12,
            MinWidth        = 220,
            Background      = Brush(ResourceKeys.SurfaceInputBrush),
            Foreground      = Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush     = Brush(ResourceKeys.BorderSubtleBrush),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(10, 5, 10, 5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Force built-in WinUI 3 clear (✕) button to always show
        searchBox.Loaded += (_, _) => VisualStateManager.GoToState(searchBox, "ButtonVisible", false);
        topButtonRow.Children.Add(searchBox);

        panel.Children.Add(topButtonRow);

        // ── Search filter action — wired after rows are built ─────────────────
        // packRows tracks (id, displayName, packCb, fileSubPanel) for filtering
        var packRows = new List<(string Id, string DisplayName, CheckBox PackCb, StackPanel FileSub)>();
        // categoryHeaders tracks (category, header TextBlock, list of pack ids in that category)
        var categoryHeaders = new List<(string Category, TextBlock Header, List<string> PackIds)>();

        // ── Group packs by category ───────────────────────────────────────────
        var groups = packs
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var headerText = group.Key switch
            {
                ShaderPackService.PackCategory.Essential   => "Essential",
                ShaderPackService.PackCategory.Recommended => "Recommended",
                _                                          => "Extra",
            };

            var headerTextBlock = new TextBlock
            {
                Text       = headerText,
                FontSize   = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                Margin     = new Thickness(0, checkBoxes.Count > 0 ? 10 : 4, 0, 4),
            };
            panel.Children.Add(headerTextBlock);
            var categoryPackIds = new List<string>();
            categoryHeaders.Add((headerText, headerTextBlock, categoryPackIds));

            foreach (var (id, displayName, _) in group)
            {
                var capturedId  = id;
                var description = shaderPackService.GetPackDescription(id);
                packCacheState.TryGetValue(id, out var packCs);
                var isCached          = packCs.IsCached;
                var initialExclusions = isCached
                    ? packCs.Exclusions
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // ── Build per-file sub-panel ──────────────────────────────────
                var fileSubPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Margin      = new Thickness(24, 0, 0, 0),
                    Visibility  = Visibility.Collapsed,
                };

                var fileCbList = new List<(string File, CheckBox Box)>();

                if (isCached)
                {
                    var shaderFiles    = shaderPackService.GetPackShaderFiles(new[] { id });
                    var packIsSelected = selected.Contains(id);
                    foreach (var fileName in shaderFiles)
                    {
                        var fileCb = new CheckBox
                        {
                            Content   = new TextBlock
                            {
                                Text       = fileName,
                                FontSize   = 12,
                                Foreground = Brush(ResourceKeys.TextPrimaryBrush),
                            },
                            Margin    = new Thickness(0, 1, 0, 1),
                            IsChecked = packIsSelected && !initialExclusions.Contains(fileName),
                        };
                        fileCbList.Add((fileName, fileCb));
                        fileSubPanel.Children.Add(fileCb);
                    }
                }

                fileSubPanels[id]  = fileSubPanel;
                fileCheckBoxes[id] = fileCbList;

                bool? packInitialState;
                if (!selected.Contains(id))
                    packInitialState = false;
                else if (isCached && initialExclusions.Count > 0)
                    packInitialState = null;
                else
                    packInitialState = true;

                var packCb = new CheckBox
                {
                    IsThreeState = isCached && fileCbList.Count > 0,
                    IsChecked    = packInitialState,
                    Margin       = new Thickness(0, 2, 0, 2),
                };

                var innerPanel = new StackPanel { Spacing = 0, MaxWidth = 490 };
                var nameRow    = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                nameRow.Children.Add(new TextBlock
                {
                    Text              = displayName,
                    FontSize          = 13,
                    Foreground        = Brush(ResourceKeys.TextPrimaryBrush),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                if (isCached)
                {
                    nameRow.Children.Add(new TextBlock
                    {
                        Text              = "✓",
                        FontSize          = 13,
                        Foreground        = Brush(ResourceKeys.AccentGreenBrush),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }

                if (isCached && fileCbList.Count > 0)
                {
                    var expandBtn = new Button
                    {
                        Content           = "▶",
                        FontSize          = 10,
                        Padding           = new Thickness(4, 0, 4, 0),
                        Margin            = new Thickness(4, 0, 0, 0),
                        Visibility        = Visibility.Visible,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    expandButtons[id] = expandBtn;
                    nameRow.Children.Add(expandBtn);

                    expandBtn.Click += (s, ev) =>
                    {
                        var isExpanded = fileSubPanels[capturedId].Visibility == Visibility.Visible;
                        fileSubPanels[capturedId].Visibility = isExpanded
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                        expandBtn.Content = isExpanded ? "▶" : "▼";
                    };
                }

                innerPanel.Children.Add(nameRow);

                if (!string.IsNullOrEmpty(description))
                {
                    innerPanel.Children.Add(new TextBlock
                    {
                        Text         = description,
                        FontSize     = 11,
                        Opacity      = 0.6,
                        Foreground   = Brush(ResourceKeys.TextPrimaryBrush),
                        TextWrapping = TextWrapping.Wrap,
                        Width        = 340,
                    });
                }

                packCb.Content = innerPanel;

                // ── Wire pack-level checkbox handlers ─────────────────────────
                bool packCbInitializing = false;

                packCb.Checked += (s, ev) =>
                {
                    if (packCbInitializing || profileLoading) return;

                    var required = shaderPackService.GetRequiredPacks(capturedId);
                    foreach (var reqId in required)
                    {
                        var depBox = checkBoxes.FirstOrDefault(c =>
                            c.Id.Equals(reqId, StringComparison.OrdinalIgnoreCase)).Box;
                        if (depBox != null && depBox.IsChecked != true)
                            depBox.IsChecked = true;
                    }

                    if (fileCheckBoxes.TryGetValue(capturedId, out var fcList))
                    {
                        foreach (var (_, fcb) in fcList)
                            fcb.IsChecked = true;
                    }
                };

                packCb.Unchecked += (s, ev) =>
                {
                    if (packCbInitializing || profileLoading) return;
                    if (fileCheckBoxes.TryGetValue(capturedId, out var fcList))
                    {
                        foreach (var (_, fcb) in fcList)
                            fcb.IsChecked = false;
                    }
                    if (expandButtons.TryGetValue(capturedId, out var eb))
                    {
                        if (fileSubPanels.TryGetValue(capturedId, out var sp))
                        {
                            sp.Visibility = Visibility.Collapsed;
                            eb.Content    = "▶";
                        }
                    }
                };

                checkBoxes.Add((id, packCb));
                packRows.Add((id, displayName, packCb, fileSubPanel));
                categoryPackIds.Add(id);
                panel.Children.Add(packCb);

                if (fileCbList.Count > 0)
                    panel.Children.Add(fileSubPanel);

                // ── Wire file checkbox Checked/Unchecked → update pack tri-state
                if (fileCbList.Count > 0)
                {
                    var capturedPackCb     = packCb;
                    var capturedFileCbList = fileCbList;
                    var capturedIncMap     = includeMap;

                    foreach (var (fileName, fileCb) in fileCbList)
                    {
                        var capturedFileName = fileName;

                        fileCb.Checked += (s, ev) =>
                        {
                            if (profileLoading) return;
                            packCbInitializing = true;
                            UpdatePackTriState(capturedPackCb, capturedFileCbList);
                            packCbInitializing = false;

                            AutoSelectDependencies(capturedFileName, capturedId,
                                capturedIncMap, checkBoxes, fileCheckBoxes,
                                uncachedPackOwnership, shaderPackService);
                        };

                        fileCb.Unchecked += (s, ev) =>
                        {
                            if (profileLoading) return;
                            packCbInitializing = true;
                            UpdatePackTriState(capturedPackCb, capturedFileCbList);
                            packCbInitializing = false;
                        };
                    }
                }
            }
        }

        var packScrollViewer = new ScrollViewer
        {
            Content                     = panel,
            MaxHeight                   = 700,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding                     = new Thickness(0, 0, 8, 0),
        };

        // ── Wire search box filter ────────────────────────────────────────────
        searchBox.TextChanged += (s, ev) =>
        {
            VisualStateManager.GoToState(searchBox, "ButtonVisible", true);
            var query = searchBox.Text.Trim();
            bool hasQuery = !string.IsNullOrEmpty(query);

            foreach (var (id, displayName, packCb, fileSub) in packRows)
            {
                if (!hasQuery)
                {
                    packCb.Visibility  = Visibility.Visible;
                    // Restore all file row visibilities
                    if (fileCheckBoxes.TryGetValue(id, out var allFc))
                        foreach (var (_, fileCb) in allFc)
                            fileCb.Visibility = Visibility.Visible;
                    continue;
                }

                // Match pack name
                bool nameMatch = displayName.Contains(query, StringComparison.OrdinalIgnoreCase);

                // Match any individual shader file name
                bool fileMatch = false;
                if (!nameMatch && fileCheckBoxes.TryGetValue(id, out var fcList))
                {
                    fileMatch = fcList.Any(fc => fc.File.Contains(query, StringComparison.OrdinalIgnoreCase));
                    // Hide individual file rows that don't match the query
                    if (fileMatch)
                    {
                        foreach (var (file, fileCb) in fcList)
                            fileCb.Visibility = file.Contains(query, StringComparison.OrdinalIgnoreCase)
                                ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                else if (nameMatch && fileCheckBoxes.TryGetValue(id, out var allFcList))
                {
                    // Pack name matched — show all file rows
                    foreach (var (_, fileCb) in allFcList)
                        fileCb.Visibility = Visibility.Visible;
                }

                bool match = nameMatch || fileMatch;
                packCb.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                // Collapse sub-panel for non-matching packs
                if (!match && fileSubPanels.TryGetValue(id, out var hideSp))
                    hideSp.Visibility = Visibility.Collapsed;
                // If filtering by file, expand the sub-panel so matching files are visible
                if (fileMatch && !nameMatch && fileSubPanels.TryGetValue(id, out var sp))
                {
                    sp.Visibility = Visibility.Visible;
                    if (expandButtons.TryGetValue(id, out var eb))
                        eb.Content = "▼";
                }
            }

            // Show/hide category headers based on whether any pack in that category is visible
            foreach (var (_, header, packIds) in categoryHeaders)
            {
                bool anyVisible = packIds.Any(pid =>
                {
                    var row = packRows.FirstOrDefault(r => r.Id.Equals(pid, StringComparison.OrdinalIgnoreCase));
                    return row.PackCb?.Visibility == Visibility.Visible;
                });
                header.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
            }
        };

        // Enter key unfocuses the search box without moving cursor elsewhere
        searchBox.KeyDown += (s, ev) =>
        {
            if (ev.Key == Windows.System.VirtualKey.Enter)
            {
                ev.Handled = true;
                // Move focus to the scroll viewer to deselect search box without side effects
                packScrollViewer.Focus(FocusState.Programmatic);
            }
        };

        // ── Profile state ─────────────────────────────────────────────────────
        var profiles        = ShaderProfileService.Load();
        int activeProfileIdx = -1;

        // ── Profile panel (right column, 200px) ──────────────────────────────
        var profilePanel = new StackPanel
        {
            Width   = 200,
            Spacing = 4,
            Margin  = new Thickness(8, 0, 0, 0),
        };

        // Header
        profilePanel.Children.Add(new TextBlock
        {
            Text       = "Profiles",
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(ResourceKeys.TextPrimaryBrush),
            Margin     = new Thickness(0, 0, 0, 4),
        });

        // Profile list scroll area
        var profileListPanel = new StackPanel { Spacing = 2 };
        var profileListScroll = new ScrollViewer
        {
            Content                     = profileListPanel,
            MaxHeight                   = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        profilePanel.Children.Add(profileListScroll);

        // Inline rename TextBox for new profile (hidden until "New" is clicked)
        var newProfileBox = new TextBox
        {
            PlaceholderText = "Profile name",
            FontSize        = 12,
            Margin          = new Thickness(0, 2, 0, 2),
            Visibility      = Visibility.Collapsed,
        };

        // Status label for export confirmation
        var exportStatusLabel = new TextBlock
        {
            Text       = "",
            FontSize   = 11,
            Foreground = Brush(ResourceKeys.AccentGreenBrush),
            Visibility = Visibility.Collapsed,
            Margin     = new Thickness(0, 2, 0, 0),
        };

        // ── Helper: collect current selection from checkboxes ─────────────────
        List<string> CollectCurrentPackIds()
        {
            var result = new List<string>();
            foreach (var (id, box) in checkBoxes)
                if (box.IsChecked != false)
                    result.Add(id);
            return result;
        }

        Dictionary<string, List<string>> CollectCurrentExclusions()
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, box) in checkBoxes)
            {
                if (box.IsChecked == false) continue;
                if (!fileCheckBoxes.TryGetValue(id, out var fcList) || fcList.Count == 0) continue;
                var excl = fcList
                    .Where(fc => fc.Box.IsChecked != true)
                    .Select(fc => fc.File)
                    .ToList();
                if (excl.Count > 0)
                    result[id] = excl;
            }
            return result;
        }

        // ── Helper: apply a profile's selection to the left panel ─────────────
        void ApplyProfileToPanel(ShaderProfile profile)
        {
            profileLoading = true;
            try
            {
                var profileSelected = new HashSet<string>(
                    profile.SelectedPacks, StringComparer.OrdinalIgnoreCase);

                foreach (var (id, box) in checkBoxes)
                {
                    bool isSelected = profileSelected.Contains(id);

                    // Determine file-level state
                    if (!isSelected)
                    {
                        box.IsChecked = false;
                        if (fileCheckBoxes.TryGetValue(id, out var fcList2))
                            foreach (var (_, fcb) in fcList2)
                                fcb.IsChecked = false;
                        // Collapse sub-panel
                        if (fileSubPanels.TryGetValue(id, out var sp2))
                            sp2.Visibility = Visibility.Collapsed;
                        if (expandButtons.TryGetValue(id, out var eb2))
                            eb2.Content = "▶";
                    }
                    else
                    {
                        // Apply file exclusions
                        HashSet<string>? excl = null;
                        if (profile.FileExclusions.TryGetValue(id, out var exclList))
                            excl = new HashSet<string>(exclList, StringComparer.OrdinalIgnoreCase);

                        if (fileCheckBoxes.TryGetValue(id, out var fcList))
                        {
                            foreach (var (fileName, fcb) in fcList)
                                fcb.IsChecked = excl == null || !excl.Contains(fileName);

                            // Set tri-state based on file states
                            bool hasFiles = fcList.Count > 0;
                            if (hasFiles)
                            {
                                int checkedCount = fcList.Count(fc => fc.Box.IsChecked == true);
                                if (checkedCount == fcList.Count)
                                    box.IsChecked = true;
                                else if (checkedCount == 0)
                                    box.IsChecked = false;
                                else
                                    box.IsChecked = null;
                            }
                            else
                            {
                                box.IsChecked = true;
                            }
                        }
                        else
                        {
                            box.IsChecked = true;
                        }
                    }
                }
            }
            finally
            {
                profileLoading = false;
            }
        }

        // ── Helper: rebuild the profile list rows ─────────────────────────────
        Action? rebuildProfileList = null;

        rebuildProfileList = () =>
        {
            profileListPanel.Children.Clear();

            for (int i = 0; i < profiles.Count; i++)
            {
                var capturedIdx = i;
                var prof        = profiles[i];
                bool isActive   = capturedIdx == activeProfileIdx;

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // rowPanel kept for backwards compat with rename swap logic — hosted inside col 0
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal };
                Grid.SetColumn(rowPanel, 0);
                rowGrid.Children.Add(rowPanel);

                var rowBorder = new Border
                {
                    Background      = isActive ? Brush(ResourceKeys.AccentTealBgBrush) : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderBrush     = isActive ? Brush(ResourceKeys.AccentTealBorderBrush) : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(isActive ? 1 : 0),
                    CornerRadius    = new CornerRadius(4),
                    Margin          = new Thickness(0, 1, 0, 1),
                    Child           = rowGrid,
                };

                // Name button — loads the profile
                var nameBtn = new Button
                {
                    Content             = prof.Name,
                    FontSize            = 12,
                    Padding             = new Thickness(6, 3, 6, 3),
                    Background          = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness     = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    MinWidth            = 0,
                };
                int nameBtnWidth = context == PopupContext.Global ? 120 : 170;

                nameBtn.Click += (s, ev) =>
                {
                    activeProfileIdx = capturedIdx;
                    ApplyProfileToPanel(profiles[capturedIdx]);
                    rebuildProfileList!();
                    CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Loaded profile '{profiles[capturedIdx].Name}'");
                };

                rowPanel.Children.Add(nameBtn);

                // Pencil rename button — Global context only
                if (context == PopupContext.Global)
                {
                    var editBtn = new Button
                    {
                        Content           = "✎",
                        FontSize          = 11,
                        Padding           = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Opacity           = 0.6,
                    };
                    editBtn.Click += (s, ev) =>
                    {
                        // Replace name button with inline TextBox in the row
                        var renameBox = new TextBox
                        {
                            Text      = profiles[capturedIdx].Name,
                            FontSize  = 12,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Padding   = new Thickness(4, 2, 4, 2),
                            VerticalAlignment = VerticalAlignment.Center,
                        };

                        // Swap name button out, TextBox in
                        int nameBtnIndex = rowPanel.Children.IndexOf(nameBtn);
                        rowPanel.Children.RemoveAt(nameBtnIndex);
                        rowPanel.Children.Insert(nameBtnIndex, renameBox);
                        editBtn.IsEnabled = false;

                        void CommitRename()
                        {
                            var newName = renameBox.Text.Trim();
                            if (!string.IsNullOrEmpty(newName))
                            {
                                profiles[capturedIdx].Name = newName;
                                ShaderProfileService.Save(profiles);
                                CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Renamed profile to '{newName}'");
                            }
                            rebuildProfileList!();
                        }

                        renameBox.KeyDown += (rs, re) =>
                        {
                            if (re.Key == Windows.System.VirtualKey.Enter) CommitRename();
                            else if (re.Key == Windows.System.VirtualKey.Escape) rebuildProfileList!();
                        };
                        renameBox.LostFocus += (rs, re) => CommitRename();

                        renameBox.Focus(FocusState.Programmatic);
                        renameBox.SelectAll();
                    };
                    // Pencil and X go in grid cols 1 and 2 — pins them to the right edge
                    Grid.SetColumn(editBtn, 1);
                    editBtn.VerticalAlignment = VerticalAlignment.Center;
                    rowGrid.Children.Add(editBtn);

                    // Delete button
                    var delBtn = new Button
                    {
                        Content           = "X",
                        FontSize          = 10,
                        Padding           = new Thickness(4, 2, 4, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    delBtn.Click += (s, ev) =>
                    {
                        CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Deleted profile '{profiles[capturedIdx].Name}'");
                        profiles.RemoveAt(capturedIdx);
                        if (activeProfileIdx == capturedIdx)
                            activeProfileIdx = -1;
                        else if (activeProfileIdx > capturedIdx)
                            activeProfileIdx--;
                        ShaderProfileService.Save(profiles);
                        rebuildProfileList!();
                    };
                    Grid.SetColumn(delBtn, 2);
                    rowGrid.Children.Add(delBtn);
                }

                profileListPanel.Children.Add(rowBorder);
            }
        };

        rebuildProfileList();

        // ── Buttons (Global context only) ─────────────────────────────────────
        if (context == PopupContext.Global)
        {
            // Save button
            var saveBtn = new Button
            {
                Content  = "Save",
                FontSize = 12,
                Padding  = new Thickness(8, 4, 8, 4),
                Margin   = new Thickness(0, 6, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            saveBtn.Click += (s, ev) =>
            {
                var packIds  = CollectCurrentPackIds();
                var excls    = CollectCurrentExclusions();

                if (activeProfileIdx >= 0 && activeProfileIdx < profiles.Count)
                {
                    // Overwrite active profile
                    profiles[activeProfileIdx].SelectedPacks  = packIds;
                    profiles[activeProfileIdx].FileExclusions = excls;
                    CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Updated profile '{profiles[activeProfileIdx].Name}'");
                }
                else
                {
                    // Create new "Profile N"
                    int n = profiles.Count + 1;
                    while (profiles.Any(p => p.Name.Equals($"Profile {n}", StringComparison.OrdinalIgnoreCase)))
                        n++;
                    var newProf = new ShaderProfile
                    {
                        Name           = $"Profile {n}",
                        SelectedPacks  = packIds,
                        FileExclusions = excls,
                    };
                    profiles.Add(newProf);
                    activeProfileIdx = profiles.Count - 1;
                    CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Created profile '{newProf.Name}'");
                }

                ShaderProfileService.Save(profiles);
                rebuildProfileList!();
            };
            profilePanel.Children.Add(saveBtn);
            ToolTipService.SetToolTip(saveBtn, "Save the current shader selection into the highlighted profile. If no profile is selected, a new one is created automatically.");
            var newBtn = new Button
            {
                Content  = "New",
                FontSize = 12,
                Padding  = new Thickness(8, 4, 8, 4),
                Margin   = new Thickness(0, 2, 0, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            newBtn.Click += (s, ev) =>
            {
                // Show the inline rename box pre-filled
                int n = profiles.Count + 1;
                while (profiles.Any(p => p.Name.Equals($"Profile {n}", StringComparison.OrdinalIgnoreCase)))
                    n++;
                newProfileBox.Text       = $"Profile {n}";
                newProfileBox.Visibility = Visibility.Visible;
                newProfileBox.Focus(FocusState.Programmatic);
                newProfileBox.SelectAll();
            };
            profilePanel.Children.Add(newBtn);
            ToolTipService.SetToolTip(newBtn, "Create a new profile from the current shader selection. You'll be prompted to enter a name.");
            profilePanel.Children.Add(newProfileBox);

            // Confirm new profile on Enter or focus lost
            void ConfirmNewProfile()
            {
                if (newProfileBox.Visibility != Visibility.Visible) return;
                newProfileBox.Visibility = Visibility.Collapsed;

                var name = newProfileBox.Text.Trim();
                if (string.IsNullOrEmpty(name)) return;

                var packIds  = CollectCurrentPackIds();
                var excls    = CollectCurrentExclusions();

                var newProf = new ShaderProfile
                {
                    Name           = name,
                    SelectedPacks  = packIds,
                    FileExclusions = excls,
                };
                profiles.Add(newProf);
                activeProfileIdx = profiles.Count - 1;
                ShaderProfileService.Save(profiles);
                rebuildProfileList!();
                CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Created new profile '{name}'");
            }

            newProfileBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == Windows.System.VirtualKey.Enter)
                    ConfirmNewProfile();
            };
            newProfileBox.LostFocus += (s, ev) => ConfirmNewProfile();

            // Separator
            profilePanel.Children.Add(new Border
            {
                Height          = 1,
                Margin          = new Thickness(0, 4, 0, 4),
                Background      = Brush(ResourceKeys.SurfaceOverlayBrush),
                Opacity         = 0.4,
            });

            // Export button
            var exportBtn = new Button
            {
                Content  = "Export",
                FontSize = 12,
                Padding  = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            exportBtn.Click += async (s, ev) =>
            {
                try
                {
                    var packIds = CollectCurrentPackIds();
                    // Build exclusions as HashSet<string> keyed by packId
                    var exclDict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (id, box) in checkBoxes)
                    {
                        if (box.IsChecked == false) continue;
                        if (!fileCheckBoxes.TryGetValue(id, out var fcList) || fcList.Count == 0) continue;
                        var excl = fcList
                            .Where(fc => fc.Box.IsChecked != true)
                            .Select(fc => fc.File)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (excl.Count > 0)
                            exclDict[id] = excl;
                    }

                    var zipPath = ShaderProfileService.BuildExportZip(packIds, exclDict, shaderPackService,
                        activeProfileIdx >= 0 && activeProfileIdx < profiles.Count
                            ? profiles[activeProfileIdx]
                            : new ShaderProfile { Name = "Exported Profile", SelectedPacks = packIds, FileExclusions = exclDict.ToDictionary(k => k.Key, k => k.Value.ToList()) });
                    var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(zipPath);
                    var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dp.SetStorageItems(new[] { storageFile });
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

                    CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Exported shaders zip to clipboard: {zipPath}");

                    exportStatusLabel.Text       = "Copied to clipboard";
                    exportStatusLabel.Visibility = Visibility.Visible;

                    // Clear after 3 seconds
                    var timer = new Microsoft.UI.Xaml.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(3),
                    };
                    timer.Tick += (t, _) =>
                    {
                        timer.Stop();
                        exportStatusLabel.Text       = "";
                        exportStatusLabel.Visibility = Visibility.Collapsed;
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Export failed: {ex.Message}");
                }
            };
            profilePanel.Children.Add(exportBtn);
            ToolTipService.SetToolTip(exportBtn, "Zip the currently selected shader files and copy the archive to your clipboard. Paste directly into Discord to share.");
            profilePanel.Children.Add(exportStatusLabel);

            // Import button
            var importBtn = new Button
            {
                Content  = "Import",
                FontSize = 12,
                Padding  = new Thickness(8, 4, 8, 4),
                Margin   = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTipService.SetToolTip(importBtn, "Import a shader profile from a .zip archive exported by RHI.");

            var importStatusLabel = new TextBlock
            {
                Text       = "",
                FontSize   = 11,
                Foreground = Brush(ResourceKeys.AccentGreenBrush),
                Visibility = Visibility.Collapsed,
                Margin     = new Thickness(0, 2, 0, 0),
            };

            importBtn.Click += async (s, ev) =>
            {
                try
                {
                    // Win32 file picker — filter to zip files
                    // Get hwnd via WinRT interop from the XamlRoot
                    var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(
                        xamlRoot.ContentIslandEnvironment.AppWindowId);
                    string? zipPath = await Task.Run(() =>
                    {
                        var ofn = new NativeInterop.OpenFileName();
                        ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
                        ofn.hwndOwner  = hwnd;
                        ofn.filter     = "ZIP Archives (*.zip)\0*.zip\0All Files (*.*)\0*.*\0";
                        ofn.file       = new string(new char[260]);
                        ofn.maxFile    = ofn.file.Length;
                        ofn.title      = "Import Shader Profile";
                        ofn.flags      = 0x00080000 | 0x00001000; // OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST
                        return NativeInterop.GetOpenFileName(ref ofn) ? ofn.file.TrimEnd('\0') : null;
                    });
                    if (string.IsNullOrEmpty(zipPath)) return;

                    importBtn.IsEnabled = false;
                    importStatusLabel.Text       = "Importing...";
                    importStatusLabel.Foreground = Brush(ResourceKeys.AccentGreenBrush);
                    importStatusLabel.Visibility = Visibility.Visible;

                    var result = await Task.Run(() => ShaderProfileService.ImportFromZip(zipPath, shaderPackService));

                    if (result == null)
                    {
                        importStatusLabel.Text       = "Invalid archive — not an RHI shader profile.";
                        importStatusLabel.Foreground = Brush(ResourceKeys.AccentRedBrush);
                        importStatusLabel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        var (importedProfile, extractedPackIds) = result.Value;

                        // Deduplicate name
                        var importName = importedProfile.Name;
                        int suffix = 1;
                        while (profiles.Any(p => p.Name.Equals(importName, StringComparison.OrdinalIgnoreCase)))
                            importName = $"{importedProfile.Name} ({suffix++})";
                        importedProfile.Name = importName;

                        profiles.Add(importedProfile);
                        activeProfileIdx = profiles.Count - 1;
                        ShaderProfileService.Save(profiles);
                        rebuildProfileList!();
                        ApplyProfileToPanel(importedProfile);

                        var msg = extractedPackIds.Count > 0
                            ? $"Imported — {extractedPackIds.Count} pack(s) extracted from archive."
                            : "Imported.";
                        importStatusLabel.Text       = msg;
                        importStatusLabel.Foreground = Brush(ResourceKeys.AccentGreenBrush);
                        importStatusLabel.Visibility = Visibility.Visible;
                        CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Imported profile '{importedProfile.Name}', extracted packs: [{string.Join(", ", extractedPackIds)}]");
                    }

                    // Clear status after 4 seconds
                    var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        importStatusLabel.Visibility = Visibility.Collapsed;
                        importBtn.IsEnabled = true;
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Import failed: {ex.Message}");
                    importBtn.IsEnabled = true;
                }
            };

            profilePanel.Children.Add(importBtn);
            profilePanel.Children.Add(importStatusLabel);
        }

        // ── Two-column layout grid ─────────────────────────────────────────────
        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        // Vertical separator
        var separator = new Border
        {
            Width      = 1,
            Margin     = new Thickness(8, 0, 0, 0),
            Background = Brush(ResourceKeys.SurfaceOverlayBrush),
            Opacity    = 0.3,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        Grid.SetColumn(packScrollViewer, 0);
        Grid.SetColumn(separator, 1);
        Grid.SetColumn(profilePanel, 2);

        contentGrid.Children.Add(packScrollViewer);
        contentGrid.Children.Add(separator);
        contentGrid.Children.Add(profilePanel);

        var dlg = new ContentDialog
        {
            Title             = "Select Shader Packs",
            Content           = contentGrid,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText   = "Cancel",
            XamlRoot          = xamlRoot,
            Background        = Brush(ResourceKeys.SurfaceOverlayBrush),
            RequestedTheme    = ElementTheme.Dark,
            MinWidth          = 920,
        };
        dlg.Resources["ContentDialogMaxWidth"] = 980.0;

        var dialogResult = await DialogService.ShowSafeAsync(dlg);
        if (dialogResult != ContentDialogResult.Primary)
            return null;

        // ── Build confirmed selection and persist per-file exclusions ─────────
        // SetExcludedFiles calls _settingsLock.Wait() synchronously — offload to background
        // thread so the UI thread isn't blocked while the lock may be held by a download.
        var confirmed = new List<string>();
        var exclusionsToWrite = new List<(string Id, IEnumerable<string> Excluded)>();

        foreach (var (id, box) in checkBoxes)
        {
            if (box.IsChecked == false) continue;
            confirmed.Add(id);

            if (!fileCheckBoxes.TryGetValue(id, out var fcList) || fcList.Count == 0)
            {
                exclusionsToWrite.Add((id, Array.Empty<string>()));
                continue;
            }

            var excludedFiles = fcList
                .Where(fc => fc.Box.IsChecked != true)
                .Select(fc => fc.File)
                .ToList();

            exclusionsToWrite.Add((id, excludedFiles));
            CrashReporter.Log($"[ShaderPopupHelper.ShowAsync] Pack '{id}': {excludedFiles.Count} file(s) excluded");
        }

        // Write exclusions off the UI thread
        var capturedExclusions = exclusionsToWrite;
        _ = Task.Run(() =>
        {
            foreach (var (id, excluded) in capturedExclusions)
                shaderPackService.SetExcludedFiles(id, excluded);
        });

        return confirmed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void UpdatePackTriState(CheckBox packCb, List<(string File, CheckBox Box)> fileCbList)
    {
        if (fileCbList.Count == 0) return;
        int checkedCount = fileCbList.Count(fc => fc.Box.IsChecked == true);
        if (checkedCount == fileCbList.Count)
            packCb.IsChecked = true;
        else if (checkedCount == 0)
            packCb.IsChecked = false;
        else
            packCb.IsChecked = null;
    }

    private static void AutoSelectDependencies(
        string checkedFile,
        string ownerPackId,
        Dictionary<string, HashSet<string>> includeMap,
        List<(string Id, CheckBox Box)> checkBoxes,
        Dictionary<string, List<(string File, CheckBox Box)>> fileCheckBoxes,
        Dictionary<string, string> uncachedPackOwnership,
        IShaderPackService shaderPackService)
    {
        if (!includeMap.TryGetValue(checkedFile, out var deps)) return;

        foreach (var dep in deps)
        {
            string? depPackId = null;
            CheckBox? depFileCb = null;

            foreach (var (packId, fcList) in fileCheckBoxes)
            {
                var match = fcList.FirstOrDefault(fc =>
                    fc.File.Equals(dep, StringComparison.OrdinalIgnoreCase));
                if (match.Box != null)
                {
                    depPackId  = packId;
                    depFileCb  = match.Box;
                    break;
                }
            }

            if (depPackId == null)
                uncachedPackOwnership.TryGetValue(dep, out depPackId);

            if (depPackId == null) continue;

            var packBox = checkBoxes.FirstOrDefault(c =>
                c.Id.Equals(depPackId, StringComparison.OrdinalIgnoreCase)).Box;
            if (packBox != null && packBox.IsChecked != true)
                packBox.IsChecked = true;

            if (depFileCb != null && depFileCb.IsChecked != true)
                depFileCb.IsChecked = true;
        }
    }

    /// <summary>
    /// Pure logic: computes the checkbox model for the popup.
    /// Returns one entry per available pack with its pre-checked state.
    /// </summary>
    internal static List<(string Id, bool IsChecked)> ComputeCheckboxModel(
        IReadOnlyList<(string Id, string DisplayName, ShaderPackService.PackCategory Category)> availablePacks,
        List<string>? currentSelection)
    {
        var sel   = new HashSet<string>(currentSelection ?? [], StringComparer.OrdinalIgnoreCase);
        var model = new List<(string Id, bool IsChecked)>(availablePacks.Count);
        foreach (var (id, _, _) in availablePacks)
            model.Add((id, sel.Contains(id)));
        return model;
    }

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];
}
