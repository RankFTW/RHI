// CompactViewBuilder.cs — Manages compact view by toggling visibility of sections
// within the existing DetailPanel. NO reparenting — compact mode uses the exact same
// XAML elements as detail mode, just showing/hiding sections based on page index.

using Microsoft.UI.Xaml;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Helper class responsible for compact view page display.
/// Instead of reparenting XAML elements, it toggles visibility of the existing
/// DetailPanel children to show only the relevant page content.
/// This guarantees visual parity with the Detail view.
/// </summary>
public class CompactViewBuilder
{
    private readonly MainWindow _window;

    public CompactViewBuilder(MainWindow window)
    {
        _window = window;
    }

    /// <summary>
    /// Enters compact mode: populates all panels using existing builders,
    /// then shows the appropriate page by toggling visibility.
    /// </summary>
    public void EnterCompactMode(GameCardViewModel card, int pageIndex)
    {
        try
        {
            // Populate all panels using the existing detail builders (same as Detail mode)
            _window.PopulateDetailPanel(card);
            _window.DetailPanel.Visibility = Visibility.Visible;
            _window.BuildOverridesPanel(card);
            _window.OverridesContainer.Visibility = Visibility.Visible;
            _window.ManagementContainer.Visibility = Visibility.Visible;

            // Add left/right padding to make room for nav arrows
            _window.DetailPanel.Padding = new Thickness(48, 18, 48, 24);

            // Show the correct page
            ShowPage(pageIndex);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CompactViewBuilder.EnterCompactMode] Error for '{card.GameName}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuilds the currently active page for the given card.
    /// Re-populates panels and shows the correct page.
    /// </summary>
    public void RebuildCurrentPage(GameCardViewModel card, int pageIndex)
    {
        try
        {
            // Populate all panels using existing builders
            _window.PopulateDetailPanel(card);
            _window.DetailPanel.Visibility = Visibility.Visible;
            _window.BuildOverridesPanel(card);
            _window.OverridesContainer.Visibility = Visibility.Visible;
            _window.ManagementContainer.Visibility = Visibility.Visible;

            // Show the correct page
            ShowPage(pageIndex);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CompactViewBuilder.RebuildCurrentPage] Error building page {pageIndex} for '{card.GameName}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Navigates to a specific page without re-populating the panels.
    /// Used when the user clicks the nav arrows (data hasn't changed, just the page).
    /// </summary>
    public void NavigateToPage(int pageIndex)
    {
        try
        {
            ShowPage(pageIndex);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CompactViewBuilder.NavigateToPage] Error navigating to page {pageIndex} — {ex.Message}");
        }
    }

    /// <summary>
    /// Leaves compact mode: restores all sections to visible and resets padding.
    /// </summary>
    public void LeaveCompactMode()
    {
        try
        {
            ShowAllSections();
            // Restore default detail panel padding
            _window.DetailPanel.Padding = new Thickness(24, 18, 24, 24);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CompactViewBuilder.LeaveCompactMode] Error restoring panels — {ex.Message}");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Shows the appropriate page by toggling visibility of DetailPanel's children.
    /// Page 0 (Game Card): Show game name, info card, component table; hide overrides + management
    /// Page 1 (Overrides): Show overrides container only
    /// Page 2 (Management): Show management container only
    /// </summary>
    private void ShowPage(int pageIndex)
    {
        var detailPanel = _window.DetailPanel;
        var overridesContainer = _window.OverridesContainer;
        var managementContainer = _window.ManagementContainer;

        foreach (var child in detailPanel.Children)
        {
            if (child is not UIElement element) continue;

            if (element == overridesContainer)
            {
                element.Visibility = pageIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (element == managementContainer)
            {
                element.Visibility = pageIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                // All other children (game name, info card, component table, etc.)
                // are visible on page 0 (Game Card) only
                element.Visibility = pageIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Makes all children of DetailPanel visible (used when leaving compact mode).
    /// </summary>
    private void ShowAllSections()
    {
        foreach (var child in _window.DetailPanel.Children)
        {
            if (child is UIElement element)
                element.Visibility = Visibility.Visible;
        }
    }
}
