// MainWindow.SpotlightCoverSize.cs -- Spotlight: drag the cover strip's edge (the side facing the game page)
// to make the covers bigger or smaller, live. Saved as Settings.SpotlightCoverScale; double-click resets.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private const double BaseTileWidth = 120, BaseCoverHeight = 180;
    private const double CoverScaleMin = 0.6, CoverScaleMax = 2.0;

    private ResizeGrip? _coverGrip;
    private int _coverDecodeWidth;

    private double CoverScale => Math.Clamp(ViewModel.Settings.SpotlightCoverScale, CoverScaleMin, CoverScaleMax);
    private double TileWidth => Math.Round(BaseTileWidth * CoverScale);
    private double CoverHeight => Math.Round(BaseCoverHeight * CoverScale);

    /// <summary>Covers are decoded at twice the tile width, in 80 px steps (re-decoded only when a drag ends).</summary>
    private int CoverDecodeWidth => (int)(Math.Ceiling(TileWidth * 2 / 80) * 80);

    private void EnsureCoverGrip(Grid parent)
    {
        if (_coverGrip != null) return;
        _coverGrip = new ResizeGrip { Reference = () => LayoutRoot, Visibility = Visibility.Collapsed };
        ToolTipService.SetToolTip(_coverGrip, "Drag to resize the covers. Double-click to reset.");
        double startHeight = 0;
        _coverGrip.DragStarted += () => startHeight = CoverHeight;
        _coverGrip.DragDelta += dy =>
        {
            // The grip sits on the side facing the page: dragging into the page makes the covers bigger
            double height = startHeight + (ViewModel.Settings.SpotlightShelfAtBottom ? -dy : dy);
            double scale = Math.Clamp(height / BaseCoverHeight, CoverScaleMin, CoverScaleMax);
            if (Math.Abs(scale - ViewModel.Settings.SpotlightCoverScale) < 0.005) return;
            ViewModel.Settings.SpotlightCoverScale = Math.Round(scale, 3);
            ApplyCoverSize(redecode: false);
        };
        _coverGrip.DragCompleted += () =>
        {
            ApplyCoverSize(redecode: true);
            ViewModel.SaveSettingsPublic();
            CrashReporter.Log($"[MainWindow.CoverSize] Cover size {ViewModel.Settings.SpotlightCoverScale:0.##}x");
        };
        _coverGrip.Reset += () =>
        {
            ViewModel.Settings.SpotlightCoverScale = 1.0;
            ApplyCoverSize(redecode: true);
            ViewModel.SaveSettingsPublic();
            CrashReporter.Log("[MainWindow.CoverSize] Cover size reset");
        };
        parent.Children.Add(_coverGrip);
    }

    /// <summary>Puts the grip on the strip's edge facing the page (top edge when the covers are at the bottom).</summary>
    private void PlaceCoverGrip(bool bottom)
    {
        if (_coverGrip == null || _shelf == null) return;
        Grid.SetRow(_coverGrip, Grid.GetRow(_shelf));
        _coverGrip.VerticalAlignment = bottom ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        // Half outside the strip, so it doesn't sit on the horizontal scroll bar
        _coverGrip.Margin = bottom ? new Thickness(0, -5, 0, 0) : new Thickness(0, 0, 0, -5);
    }

    /// <summary>Resizes the shown tiles and the strip; with <paramref name="redecode"/> reloads covers at the new size.</summary>
    private void ApplyCoverSize(bool redecode)
    {
        try
        {
            if (_shelf == null) return;
            _shelf.Height = CoverHeight + 58;
            foreach (var t in _shelfTiles)
            {
                t.Root.Width = TileWidth;
                t.Cover.Height = CoverHeight;
            }
            UpdateShelfCentering();
            if (redecode && CoverDecodeWidth != _coverDecodeWidth)
            {
                _coverCache.Clear();
                foreach (var t in _shelfTiles)
                    if (t.Card != null) { t.Image.Source = null; LoadCover(t, t.Card); }
            }
            if (redecode && ViewModel.SelectedGame != null) _shelf.ScrollIntoView(ViewModel.SelectedGame);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ApplyCoverSize] {ex.Message}");
        }
    }
}
