// MainWindow.Skeleton.cs — Skeleton loading screen creation, animation, and cleanup.

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using RenoDXCommander.Models;
using Windows.UI;

namespace RenoDXCommander;

/// <summary>
/// Represents the computed visibility state for skeleton-related panels.
/// </summary>
internal record VisibilityState(
    Visibility LoadingPanel,
    Visibility GameViewPanel,
    Visibility SkeletonRowPanel,
    Visibility SkeletonDetailPanel,
    Visibility AboutPanel);

/// <summary>Describes the structural specification of a single skeleton row element.</summary>
internal record SkeletonRowSpec(
    double CornerRadius,
    double PaddingLeft, double PaddingTop, double PaddingRight, double PaddingBottom,
    double GridColumnSpacing,
    int ChildCount,
    double IconWidth, double IconHeight,
    double NameHeight,
    double BadgeWidth, double BadgeHeight);

/// <summary>Describes the structural specification of the skeleton detail panel.</summary>
internal record SkeletonDetailPanelSpec(
    double HeaderHeight,
    int BadgeCount,
    double TableCornerRadius,
    int TableRowCount,
    int TableColumnCount,
    double PaddingLeft, double PaddingTop, double PaddingRight, double PaddingBottom,
    double Spacing);

/// <summary>Describes the configuration of a single shimmer animation.</summary>
internal record ShimmerAnimationSpec(
    bool AutoReverse,
    bool RepeatForever,
    double DurationSeconds);

public sealed partial class MainWindow
{
    /// <summary>Number of skeleton rows to display in the sidebar list area.</summary>
    internal const int SkeletonRowCount = 12;

    /// <summary>Skeleton base color (dark tone, resting state).</summary>
    internal const byte SkeletonBaseR = 0x1A, SkeletonBaseG = 0x20, SkeletonBaseB = 0x28;

    /// <summary>Skeleton highlight color (light tone, shimmer peak).</summary>
    internal const byte SkeletonHighlightR = 0x25, SkeletonHighlightG = 0x2D, SkeletonHighlightB = 0x38;

    private Storyboard? _shimmerStoryboard;

    /// <summary>
    /// Returns the structural specification of a skeleton row without creating WinUI objects.
    /// </summary>
    internal static SkeletonRowSpec GetSkeletonRowSpec() => new(
        CornerRadius: 6,
        PaddingLeft: 8, PaddingTop: 6, PaddingRight: 8, PaddingBottom: 6,
        GridColumnSpacing: 6,
        ChildCount: 3,
        IconWidth: 14, IconHeight: 14,
        NameHeight: 14,
        BadgeWidth: 8, BadgeHeight: 8);

    /// <summary>
    /// Returns the structural specification of the skeleton detail panel without creating WinUI objects.
    /// </summary>
    internal static SkeletonDetailPanelSpec GetSkeletonDetailPanelSpec() => new(
        HeaderHeight: 22,
        BadgeCount: 6,
        TableCornerRadius: 12,
        TableRowCount: 4,
        TableColumnCount: 5,
        PaddingLeft: 24, PaddingTop: 18, PaddingRight: 24, PaddingBottom: 24,
        Spacing: 16);

    /// <summary>
    /// Returns the shimmer animation spec for a given target count without creating WinUI objects.
    /// </summary>
    internal static List<ShimmerAnimationSpec> GetShimmerAnimationSpecs(int targetCount)
    {
        var specs = new List<ShimmerAnimationSpec>(targetCount);
        for (int i = 0; i < targetCount; i++)
        {
            specs.Add(new ShimmerAnimationSpec(
                AutoReverse: true,
                RepeatForever: true,
                DurationSeconds: 1.5));
        }
        return specs;
    }

    /// <summary>
    /// Pure function that computes the cleanup result: whether panels should be cleared and collapsed.
    /// Returns (shouldClearChildren, shouldCollapse, shouldNullStoryboard).
    /// </summary>
    internal static (bool ClearChildren, bool Collapse, bool NullStoryboard) ComputeCleanupResult(
        int rowChildCount, int detailChildCount, bool hasStoryboard)
    {
        // Cleanup always clears, collapses, and nulls — idempotent
        return (ClearChildren: true, Collapse: true, NullStoryboard: hasStoryboard);
    }

    /// <summary>
    /// Pure function that computes the visibility state for skeleton-related panels
    /// based on the current loading state, initialization state, and active page.
    /// </summary>
    internal static VisibilityState ComputeVisibilityState(bool isLoading, bool hasInitialized, AppPage currentPage)
    {
        // About page: only AboutPanel visible, everything else collapsed
        if (currentPage == AppPage.About)
        {
            return new VisibilityState(
                LoadingPanel: Visibility.Collapsed,
                GameViewPanel: Visibility.Collapsed,
                SkeletonRowPanel: Visibility.Collapsed,
                SkeletonDetailPanel: Visibility.Collapsed,
                AboutPanel: Visibility.Visible);
        }

        // Settings page: everything collapsed (settings panel shown instead)
        if (currentPage == AppPage.Settings)
        {
            return new VisibilityState(
                LoadingPanel: Visibility.Collapsed,
                GameViewPanel: Visibility.Collapsed,
                SkeletonRowPanel: Visibility.Collapsed,
                SkeletonDetailPanel: Visibility.Collapsed,
                AboutPanel: Visibility.Collapsed);
        }

        // Initial loading: show skeletons
        if (isLoading && !hasInitialized)
        {
            return new VisibilityState(
                LoadingPanel: Visibility.Collapsed,
                GameViewPanel: Visibility.Visible,
                SkeletonRowPanel: Visibility.Visible,
                SkeletonDetailPanel: Visibility.Visible,
                AboutPanel: Visibility.Collapsed);
        }

        // Not loading, or silent refresh (hasInitialized=true): skeletons hidden
        return new VisibilityState(
            LoadingPanel: Visibility.Collapsed,
            GameViewPanel: Visibility.Visible,
            SkeletonRowPanel: Visibility.Collapsed,
            SkeletonDetailPanel: Visibility.Collapsed,
            AboutPanel: Visibility.Collapsed);
    }

    /// <summary>Number of skeleton cards to display in the grid skeleton (3×3 grid).</summary>
    internal const int SkeletonGridCardCount = 9;

    /// <summary>
    /// Called once during window initialization to populate skeleton placeholders
    /// and start the shimmer animation. Shows the appropriate content skeleton
    /// based on the persisted view layout.
    /// </summary>
    internal void InitializeSkeletons()
    {
        // Resolve skeleton brush from theme resources, with hardcoded fallback
        SolidColorBrush fillBrush;
        try
        {
            fillBrush = Application.Current.Resources[ResourceKeys.SkeletonBaseBrush] as SolidColorBrush
                        ?? new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x28));
        }
        catch
        {
            fillBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x28));
        }

        // Create skeleton rows (sidebar — shared across all views) and collect shimmer targets
        var shimmerTargets = new List<Border>();

        for (int i = 0; i < SkeletonRowCount; i++)
        {
            var row = CreateSkeletonRow(fillBrush);
            SkeletonRowPanel.Children.Add(row);
            shimmerTargets.Add(row);
        }

        // Show the appropriate content skeleton based on the persisted view layout
        var layout = ViewModel.CurrentViewLayout;

        if (layout == ViewLayout.Grid)
        {
            // Grid view: show card grid skeleton, hide detail skeleton
            SkeletonDetailScrollViewer.Visibility = Visibility.Collapsed;
            SkeletonGridScrollViewer.Visibility = Visibility.Visible;

            SolidColorBrush tableBgBrush;
            try
            {
                tableBgBrush = Application.Current.Resources[ResourceKeys.SurfaceComponentTableBrush] as SolidColorBrush
                               ?? fillBrush;
            }
            catch { tableBgBrush = fillBrush; }

            SolidColorBrush borderBrush;
            try
            {
                borderBrush = Application.Current.Resources[ResourceKeys.BorderDefaultBrush] as SolidColorBrush
                              ?? fillBrush;
            }
            catch { borderBrush = fillBrush; }

            for (int i = 0; i < SkeletonGridCardCount; i++)
            {
                var card = CreateSkeletonCard(fillBrush, tableBgBrush, borderBrush);
                SkeletonGridPanel.Children.Add(card);
                shimmerTargets.Add(card);
            }
        }
        else
        {
            // Detail and Compact views: show detail skeleton, hide grid skeleton
            SkeletonDetailScrollViewer.Visibility = Visibility.Visible;
            SkeletonGridScrollViewer.Visibility = Visibility.Collapsed;

            PopulateSkeletonDetailPanel(fillBrush);

            // In Compact mode, hide the overrides and management skeleton sections
            // since they're on separate pages (only the game card page is visible)
            if (layout == ViewLayout.Compact)
            {
                var children = SkeletonDetailPanel.Children;
                // The last two children are the Overrides border and Management border
                if (children.Count >= 2)
                {
                    children[children.Count - 1].Visibility = Visibility.Collapsed; // Management
                    children[children.Count - 2].Visibility = Visibility.Collapsed; // Overrides
                }
            }

            // Collect detail panel borders for shimmer
            foreach (var child in SkeletonDetailPanel.Children)
            {
                if (child is Border b)
                    shimmerTargets.Add(b);
            }
        }

        // Resolve shimmer colors
        Color fromColor, toColor;
        try
        {
            fromColor = (Color)Application.Current.Resources[ResourceKeys.SkeletonBase];
            toColor = (Color)Application.Current.Resources[ResourceKeys.SkeletonHighlight];
        }
        catch
        {
            fromColor = ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x28);
            toColor = ColorHelper.FromArgb(0xFF, 0x25, 0x2D, 0x38);
        }

        // Build and start shimmer storyboard
        _shimmerStoryboard = CreateShimmerStoryboard(shimmerTargets, fromColor, toColor);
        try
        {
            _shimmerStoryboard.Begin();
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.InitializeSkeletons] Storyboard.Begin failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the shimmer animation, clears skeleton children, and collapses
    /// all skeleton panels. Called on the first IsLoading → false transition.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    internal void RemoveSkeletons()
    {
        RemoveSkeletons(SkeletonRowPanel, SkeletonDetailPanel, ref _shimmerStoryboard);
        // Also collapse the ScrollViewer wrappers around the content skeletons
        SkeletonDetailScrollViewer.Visibility = Visibility.Collapsed;
        SkeletonGridPanel.Children.Clear();
        SkeletonGridScrollViewer.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Static overload for skeleton cleanup. Testable without a MainWindow instance.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    internal static void RemoveSkeletons(StackPanel rowPanel, StackPanel detailPanel, ref Storyboard? storyboard)
    {
        // Stop storyboard (catch failures)
        if (storyboard != null)
        {
            try { storyboard.Stop(); }
            catch { /* swallow — cleanup must proceed */ }
            storyboard = null;
        }

        // Clear children and collapse panels
        rowPanel.Children.Clear();
        detailPanel.Children.Clear();
        rowPanel.Visibility = Visibility.Collapsed;
        detailPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Creates a single skeleton row Border matching the game card DataTemplate layout:
    /// 14×14 icon rect + flexible-width name rect + 8px badge circle.
    /// The outer border is transparent (matching real cards), only inner elements are filled.
    /// Real cards have BorderThickness=1 and Padding=8,6 — skeleton uses matching dimensions.
    /// </summary>
    internal static Border CreateSkeletonRow(SolidColorBrush fillBrush)
    {
        // Icon placeholder: 14×14 rounded rect (matches real source icon)
        var iconBorder = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(3),
            Background = fillBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(iconBorder, 0);

        // Name placeholder: flexible-width Border (matches FontSize=12 text height)
        var nameBorder = new Border
        {
            Height = 14,
            CornerRadius = new CornerRadius(4),
            Background = fillBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameBorder, 1);

        // Badge placeholder: 8×8 Ellipse (matches update badge dot)
        var badgeEllipse = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = fillBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 2, 0),
        };
        Grid.SetColumn(badgeEllipse, 2);

        // Inner grid with 3 columns: Auto | * | Auto
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBorder);
        grid.Children.Add(nameBorder);
        grid.Children.Add(badgeEllipse);

        // Outer border — matches real card: BorderThickness=1, Padding=8,6, CornerRadius=6
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(1),
            BorderBrush = fillBrush,
            Child = grid,
        };
    }

    /// <summary>
    /// Creates a single skeleton card matching the real card layout from CardBuilder.
    /// Width matches the CardItemWidth resource (280px in code, but the outer border
    /// in the real builder uses 280). Includes header row, API badge, status dots,
    /// action button, and settings gear placeholders.
    /// </summary>
    internal static Border CreateSkeletonCard(SolidColorBrush fillBrush, SolidColorBrush tableBgBrush, SolidColorBrush borderBrush)
    {
        var root = new StackPanel { Spacing = 8 };

        // ── Header row: source icon + game name + favourite star + more button ──
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // icon
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // fav
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // more

        // Source icon placeholder (16×16 matches real card)
        var iconPlaceholder = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(3),
            Background = fillBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        Grid.SetColumn(iconPlaceholder, 0);
        header.Children.Add(iconPlaceholder);

        // Game name placeholder (flexible width, matches FontSize=13 SemiBold)
        var namePlaceholder = new Border
        {
            Height = 14,
            CornerRadius = new CornerRadius(4),
            Background = fillBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(namePlaceholder, 1);
        header.Children.Add(namePlaceholder);

        // Favourite star placeholder
        var favPlaceholder = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = fillBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        Grid.SetColumn(favPlaceholder, 2);
        header.Children.Add(favPlaceholder);

        // More button placeholder
        var morePlaceholder = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = fillBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
        };
        Grid.SetColumn(morePlaceholder, 3);
        header.Children.Add(morePlaceholder);

        root.Children.Add(header);

        // ── API badge placeholder (e.g. "DX11/12") ──
        var apiBadge = new Border
        {
            Width = 55,
            Height = 22,
            CornerRadius = new CornerRadius(5),
            Background = fillBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        root.Children.Add(apiBadge);

        // ── Status dots row: RDX, RS, UL ──
        var dotsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        string[] dotLabels = ["RDX", "RS", "UL"];
        foreach (var label in dotLabels)
        {
            var dotPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            dotPanel.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = fillBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            dotPanel.Children.Add(new Border
            {
                Width = label.Length * 7,
                Height = 10,
                CornerRadius = new CornerRadius(3),
                Background = fillBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            dotsRow.Children.Add(dotPanel);
        }
        root.Children.Add(dotsRow);

        // ── Action button placeholder (full-width, matches install button) ──
        var actionBtn = new Border
        {
            Height = 30,
            CornerRadius = new CornerRadius(6),
            Background = fillBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.Children.Add(actionBtn);

        // ── Bottom row: info area (left) + settings gear (right) ──
        var bottomRow = new Grid();
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Info button placeholder (left)
        var infoPlaceholder = new Border
        {
            Width = 40,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            Background = fillBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetColumn(infoPlaceholder, 0);
        bottomRow.Children.Add(infoPlaceholder);

        // Settings gear placeholder (right)
        var gearPlaceholder = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            Background = fillBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(gearPlaceholder, 1);
        bottomRow.Children.Add(gearPlaceholder);

        root.Children.Add(bottomRow);

        // ── Outer card border — matches real card: Width=280, CornerRadius=10, Padding=14,12 ──
        return new Border
        {
            Width = 280,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Background = tableBgBrush,
            BorderBrush = borderBrush,
            Padding = new Thickness(14, 12, 14, 12),
            Child = root,
        };
    }

    /// <summary>
    /// Populates the detail skeleton panel with header, badge row, and
    /// component table placeholders. Instance method delegates to the static overload.
    /// </summary>
    internal void PopulateSkeletonDetailPanel(SolidColorBrush fillBrush)
    {
        SolidColorBrush tableBgBrush;
        try
        {
            tableBgBrush = Application.Current.Resources[ResourceKeys.SurfaceComponentTableBrush] as SolidColorBrush
                           ?? fillBrush;
        }
        catch { tableBgBrush = fillBrush; }

        PopulateSkeletonDetailPanel(SkeletonDetailPanel, fillBrush, tableBgBrush);
    }

    /// <summary>
    /// Static overload that populates a given StackPanel with skeleton detail placeholders.
    /// Testable without a MainWindow instance.
    /// </summary>
    internal static void PopulateSkeletonDetailPanel(StackPanel panel, SolidColorBrush fillBrush, SolidColorBrush? tableBgBrush = null)
    {
        tableBgBrush ??= fillBrush;

        // ── Header: game name + path + utility buttons (matches real DetailPanel header) ──
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerStack = new StackPanel { Spacing = 6 };

        // Game name placeholder (wide, tall — matches FontSize=20 Bold)
        headerStack.Children.Add(new Border
        {
            Width = 280,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            Background = fillBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        // Install path placeholder (narrower, shorter — matches FontSize=11 Consolas)
        headerStack.Children.Add(new Border
        {
            Width = 360,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            Background = fillBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        Grid.SetColumn(headerStack, 0);
        headerGrid.Children.Add(headerStack);

        // Utility buttons placeholder (Hide, Browse, ⭐ — right-aligned)
        var utilBtnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Top,
        };
        utilBtnRow.Children.Add(new Border { Width = 48, Height = 28, CornerRadius = new CornerRadius(6), Background = fillBrush });
        utilBtnRow.Children.Add(new Border { Width = 56, Height = 28, CornerRadius = new CornerRadius(6), Background = fillBrush });
        utilBtnRow.Children.Add(new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Background = fillBrush });
        Grid.SetColumn(utilBtnRow, 1);
        headerGrid.Children.Add(utilBtnRow);

        panel.Children.Add(headerGrid);

        // ── Badge row: 6 badges with border (matches real badge styling) ──
        var badgeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        // Varying widths to look natural — real badges have different text lengths
        double[] badgeWidths = [100, 60, 55, 55, 45, 60];
        foreach (var w in badgeWidths)
        {
            badgeRow.Children.Add(new Border
            {
                Width = w,
                Height = 24,
                CornerRadius = new CornerRadius(5),
                Background = fillBrush,
                BorderBrush = fillBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
            });
        }
        panel.Children.Add(badgeRow);

        // ── Component table (matches real DetailComponentSection) ──
        var tableContent = new StackPanel { Spacing = 10 };

        // "Components" header text (real TextBlock, not skeleton — matches real layout)
        tableContent.Children.Add(new TextBlock
        {
            Text = "Components",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE8, 0xEC, 0xF2)),
            Margin = new Thickness(0, 0, 0, 4),
        });

        // 2 component rows for ReShade + RenoDX
        for (int i = 0; i < 2; i++)
        {
            var rowGrid = new Grid { ColumnSpacing = 8 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var namePlaceholder = new Border
            {
                Width = 70 + (i * 10),
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = fillBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(namePlaceholder, 0);
            rowGrid.Children.Add(namePlaceholder);

            var versionPlaceholder = new Border
            {
                Width = 50,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = fillBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(versionPlaceholder, 1);
            rowGrid.Children.Add(versionPlaceholder);

            var actionBtn = new Border { Height = 32, CornerRadius = new CornerRadius(8), Background = fillBrush };
            Grid.SetColumn(actionBtn, 2);
            rowGrid.Children.Add(actionBtn);

            var iconBtn = new Border { Width = 36, Height = 32, CornerRadius = new CornerRadius(8), Background = fillBrush };
            Grid.SetColumn(iconBtn, 3);
            rowGrid.Children.Add(iconBtn);

            var deleteBtn = new Border { Width = 36, Height = 32, CornerRadius = new CornerRadius(8), Background = fillBrush };
            Grid.SetColumn(deleteBtn, 4);
            rowGrid.Children.Add(deleteBtn);

            tableContent.Children.Add(rowGrid);
        }

        // "── Frame limiters — Choose one ──" separator
        tableContent.Children.Add(new TextBlock
        {
            Text = "——  Frame limiters — Choose one  ——",
            FontSize = 10,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x5A, 0x68, 0x80)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2),
        });

        // 2 component rows for ReLimiter + DC
        for (int i = 0; i < 2; i++)
        {
            var rowGrid = new Grid { ColumnSpacing = 8 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var namePlaceholder = new Border
            {
                Width = i == 0 ? 75 : 120,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = fillBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(namePlaceholder, 0);
            rowGrid.Children.Add(namePlaceholder);

            var versionPlaceholder = new Border
            {
                Width = 50,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = fillBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(versionPlaceholder, 1);
            rowGrid.Children.Add(versionPlaceholder);

            var actionBtn = new Border { Height = 32, CornerRadius = new CornerRadius(8), Background = fillBrush };
            Grid.SetColumn(actionBtn, 2);
            rowGrid.Children.Add(actionBtn);

            var iconBtn = new Border { Width = 36, Height = 32, CornerRadius = new CornerRadius(8), Background = fillBrush };
            Grid.SetColumn(iconBtn, 3);
            rowGrid.Children.Add(iconBtn);

            var deleteBtn = new Border { Width = 36, Height = 32, CornerRadius = new CornerRadius(8), Background = fillBrush };
            Grid.SetColumn(deleteBtn, 4);
            rowGrid.Children.Add(deleteBtn);

            tableContent.Children.Add(rowGrid);
        }

        // "── Optional ──" separator (matches real detail panel separator above OptiScaler)
        tableContent.Children.Add(new TextBlock
        {
            Text = "── Optional ──",
            FontSize = 10,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x5A, 0x68, 0x80)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2),
        });

        // OptiScaler placeholder row (4th component row, same 5-column layout)
        {
            var osRowGrid = new Grid { ColumnSpacing = 8 };
            osRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            osRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            osRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            osRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            osRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            var osNamePlaceholder = new Border
            {
                Width = 85,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = fillBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(osNamePlaceholder, 0);
            osRowGrid.Children.Add(osNamePlaceholder);

            var osVersionPlaceholder = new Border
            {
                Width = 50,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = fillBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(osVersionPlaceholder, 1);
            osRowGrid.Children.Add(osVersionPlaceholder);

            var osActionBtn = new Border
            {
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = fillBrush,
            };
            Grid.SetColumn(osActionBtn, 2);
            osRowGrid.Children.Add(osActionBtn);

            var osIconBtn = new Border
            {
                Width = 36,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = fillBrush,
            };
            Grid.SetColumn(osIconBtn, 3);
            osRowGrid.Children.Add(osIconBtn);

            var osDeleteBtn = new Border
            {
                Width = 36,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = fillBrush,
            };
            Grid.SetColumn(osDeleteBtn, 4);
            osRowGrid.Children.Add(osDeleteBtn);

            tableContent.Children.Add(osRowGrid);
        }

        var tableBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = tableBgBrush,
            Padding = new Thickness(16, 14, 16, 14),
            Child = tableContent,
        };
        panel.Children.Add(tableBorder);

        // ── Overrides section placeholder (matches real Overrides panel) ──
        var overridesContent = new StackPanel { Spacing = 14 };

        // "Overrides" header text
        overridesContent.Children.Add(new TextBlock
        {
            Text = "Overrides",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE8, 0xEC, 0xF2)),
        });

        // Row 1: Game name (editable) + DLL naming override — two-column
        var row1 = new Grid { ColumnSpacing = 16 };
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // Left: label + text input placeholder
        var r1Left = new StackPanel { Spacing = 6 };
        r1Left.Children.Add(new Border { Width = 120, Height = 12, CornerRadius = new CornerRadius(3), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        r1Left.Children.Add(new Border { Height = 32, CornerRadius = new CornerRadius(6), Background = fillBrush });
        Grid.SetColumn(r1Left, 0);
        // Right: label + toggle/dropdown placeholder
        var r1Right = new StackPanel { Spacing = 6 };
        r1Right.Children.Add(new Border { Width = 140, Height = 12, CornerRadius = new CornerRadius(3), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        r1Right.Children.Add(new Border { Height = 32, CornerRadius = new CornerRadius(6), Background = fillBrush });
        Grid.SetColumn(r1Right, 1);
        row1.Children.Add(r1Left);
        row1.Children.Add(r1Right);
        overridesContent.Children.Add(row1);

        // Row 2: Wiki mod name + toggle — two-column
        var row2 = new Grid { ColumnSpacing = 16 };
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var r2Left = new StackPanel { Spacing = 6 };
        r2Left.Children.Add(new Border { Width = 100, Height = 12, CornerRadius = new CornerRadius(3), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        r2Left.Children.Add(new Border { Height = 32, CornerRadius = new CornerRadius(6), Background = fillBrush });
        r2Left.Children.Add(new Border { Width = 160, Height = 24, CornerRadius = new CornerRadius(4), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        Grid.SetColumn(r2Left, 0);
        row2.Children.Add(r2Left);
        overridesContent.Children.Add(row2);

        // Row 3: Shaders + Global update inclusion — two-column
        var row3 = new Grid { ColumnSpacing = 16 };
        row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var r3Left = new StackPanel { Spacing = 6 };
        r3Left.Children.Add(new Border { Width = 60, Height = 12, CornerRadius = new CornerRadius(3), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        r3Left.Children.Add(new Border { Height = 28, CornerRadius = new CornerRadius(6), Background = fillBrush });
        r3Left.Children.Add(new Border { Height = 32, CornerRadius = new CornerRadius(6), Background = fillBrush });
        Grid.SetColumn(r3Left, 0);
        var r3Right = new StackPanel { Spacing = 6 };
        r3Right.Children.Add(new Border { Width = 140, Height = 12, CornerRadius = new CornerRadius(3), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        // Update Inclusion button placeholder
        r3Right.Children.Add(new Border { Height = 32, CornerRadius = new CornerRadius(8), Background = fillBrush });
        // Summary text placeholder
        r3Right.Children.Add(new Border { Width = 200, Height = 12, CornerRadius = new CornerRadius(3), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        Grid.SetColumn(r3Right, 1);
        row3.Children.Add(r3Left);
        row3.Children.Add(r3Right);
        overridesContent.Children.Add(row3);

        // Row 4: Bitness + Graphics API dropdowns — two-column
        var row4 = new Grid { ColumnSpacing = 16 };
        row4.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row4.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var r4Left = new StackPanel { Spacing = 6 };
        r4Left.Children.Add(new Border { Width = 50, Height = 12, CornerRadius = new CornerRadius(3), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        r4Left.Children.Add(new Border { Height = 32, CornerRadius = new CornerRadius(6), Background = fillBrush });
        Grid.SetColumn(r4Left, 0);
        var r4Right = new StackPanel { Spacing = 6 };
        r4Right.Children.Add(new Border { Width = 80, Height = 12, CornerRadius = new CornerRadius(3), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        r4Right.Children.Add(new Border { Height = 32, CornerRadius = new CornerRadius(6), Background = fillBrush });
        Grid.SetColumn(r4Right, 1);
        row4.Children.Add(r4Left);
        row4.Children.Add(r4Right);
        overridesContent.Children.Add(row4);

        // Row 5: Select ReShade Preset button + Normal ReShade toggle
        var row5 = new Grid { ColumnSpacing = 16 };
        row5.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row5.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var presetBtn = new Border { Height = 32, CornerRadius = new CornerRadius(8), Background = fillBrush };
        Grid.SetColumn(presetBtn, 0);
        var normalRsPlaceholder = new StackPanel { Spacing = 6 };
        normalRsPlaceholder.Children.Add(new Border { Width = 180, Height = 12, CornerRadius = new CornerRadius(3), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        normalRsPlaceholder.Children.Add(new Border { Width = 50, Height = 20, CornerRadius = new CornerRadius(10), Background = fillBrush, HorizontalAlignment = HorizontalAlignment.Left });
        Grid.SetColumn(normalRsPlaceholder, 1);
        row5.Children.Add(presetBtn);
        row5.Children.Add(normalRsPlaceholder);
        overridesContent.Children.Add(row5);

        // Row 6: Action buttons (Change folder, Reset, Reset Overrides, Copy Report)
        for (int b = 0; b < 4; b++)
        {
            overridesContent.Children.Add(new Border
            {
                Height = 32,
                CornerRadius = new CornerRadius(6),
                Background = fillBrush,
            });
        }

        var overridesBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = tableBgBrush,
            Padding = new Thickness(16, 14, 16, 14),
            Child = overridesContent,
        };
        panel.Children.Add(overridesBorder);

        // ── Manage section placeholder (matches real Manage panel with two buttons) ──
        var manageContent = new StackPanel { Spacing = 12 };

        // "Manage" header text
        manageContent.Children.Add(new TextBlock
        {
            Text = "Manage",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE8, 0xEC, 0xF2)),
        });

        // Two buttons side by side: "Change install folder" + "Reset folder / Remove game"
        var manageBtnRow = new Grid { ColumnSpacing = 16 };
        manageBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        manageBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var manageBtn1 = new Border { Height = 32, CornerRadius = new CornerRadius(6), Background = fillBrush };
        Grid.SetColumn(manageBtn1, 0);
        var manageBtn2 = new Border { Height = 32, CornerRadius = new CornerRadius(6), Background = fillBrush };
        Grid.SetColumn(manageBtn2, 1);
        manageBtnRow.Children.Add(manageBtn1);
        manageBtnRow.Children.Add(manageBtn2);
        manageContent.Children.Add(manageBtnRow);

        var manageBorder = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = tableBgBrush,
            Padding = new Thickness(16, 14, 16, 14),
            Child = manageContent,
        };
        panel.Children.Add(manageBorder);
    }

    /// <summary>
    /// Creates the Storyboard with ColorAnimation instances targeting each
    /// skeleton Border's Background.Color property.
    /// </summary>
    internal static Storyboard CreateShimmerStoryboard(List<Border> targets, Color fromColor, Color toColor)
    {
        var storyboard = new Storyboard();

        foreach (var border in targets)
        {
            // Ensure each border has its own SolidColorBrush instance for independent animation
            border.Background = new SolidColorBrush(fromColor);

            var animation = new ColorAnimation
            {
                From = fromColor,
                To = toColor,
                Duration = new Duration(TimeSpan.FromSeconds(1.5)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };

            Storyboard.SetTarget(animation, border);
            Storyboard.SetTargetProperty(animation, "(Border.Background).(SolidColorBrush.Color)");

            storyboard.Children.Add(animation);
        }

        return storyboard;
    }
}
