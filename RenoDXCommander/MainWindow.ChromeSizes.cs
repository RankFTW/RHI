// MainWindow.ChromeSizes.cs -- sizes for the top bar (every layout) and the game list (Classic+ and Tabs),
// on top of the window-wide interface size:
//   - top bar: drag its bottom edge to scale it (Settings.TopBarScale, %), also Settings › General › Top bar size
//   - game list: drag its right edge to scale it (Settings.GameListScale, %; the width follows, 300 x size),
//     also Settings › General › Game list size
// Double-click an edge to reset it. Spotlight has its own strip (covers / banner), so the game list
// size and grip don't apply there.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    internal const int ChromeScaleMin = 50, ChromeScaleMax = 200;
    private const double GameListDefaultWidth = 300, GameListMinWidth = 150, GameListMaxWidth = 900;
    /// <summary>The game page keeps at least this much room when the list is dragged wide.</summary>
    private const double GamePageMinWidth = 360;

    private ScaleBox? _topBarScaleBox;
    private ScaleBox? _listScaleBox;
    private ResizeGrip? _topBarGrip;
    private ResizeGrip? _listWidthGrip;
    private Thickness _topBarBasePad = new(16, 10, 16, 10);

    /// <summary>Wraps the top bar and game list in scale boxes and adds their edge grips (once, from BuildTabsChrome).</summary>
    private void SetUpChromeSizing()
    {
        try
        {
            if (_xamlTopBar != null && _topBarScaleBox == null)
            {
                var inner = _xamlTopBar.Child;
                _xamlTopBar.Child = null;
                _topBarScaleBox = new ScaleBox { Child = inner };
                _xamlTopBar.Child = _topBarScaleBox;

                if (_xamlTopBar.Parent is Grid root)
                {
                    _topBarGrip = new ResizeGrip(8)
                    {
                        Reference = () => LayoutRoot,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Margin = new Thickness(0, 0, 0, -4), // straddles the edge
                    };
                    Grid.SetRow(_topBarGrip, Grid.GetRow(_xamlTopBar));
                    ToolTipService.SetToolTip(_topBarGrip, "Drag to resize the top bar. Double-click to reset.");
                    HookTopBarGrip(_topBarGrip);
                    root.Children.Add(_topBarGrip);
                }
            }

            if (_sidebar != null && _listScaleBox == null)
            {
                var inner = _sidebar.Child;
                _sidebar.Child = null;
                _listScaleBox = new ScaleBox { Child = inner };
                _sidebar.Child = _listScaleBox;

                _listWidthGrip = new ResizeGrip(8, horizontal: true)
                {
                    Reference = () => LayoutRoot,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, -4, 0), // straddles the edge
                };
                Grid.SetColumn(_listWidthGrip, 0);
                Grid.SetRowSpan(_listWidthGrip, 2); // Spotlight adds a second row
                ToolTipService.SetToolTip(_listWidthGrip, "Drag to resize the game list. Double-click to reset.");
                HookListGrip(_listWidthGrip);
                GameViewPanel.Children.Add(_listWidthGrip);
                GameViewPanel.SizeChanged += (s, e) =>
                {
                    if (e.NewSize.Width != e.PreviousSize.Width) ApplyGameListWidth();
                };
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.SetUpChromeSizing] {ex.Message}");
        }
    }

    private void HookTopBarGrip(ResizeGrip grip)
    {
        double startHeight = 0, startScale = 1;
        grip.DragStarted += () =>
        {
            startHeight = _xamlTopBar?.ActualHeight ?? 0;
            startScale = ViewModel.Settings.TopBarScale / 100.0;
        };
        grip.DragDelta += dy =>
        {
            if (startHeight <= 0) return;
            int percent = (int)Math.Round(startScale * (startHeight + dy) / startHeight * 100);
            percent = Math.Clamp(percent, ChromeScaleMin, ChromeScaleMax);
            if (percent == ViewModel.Settings.TopBarScale) return;
            ViewModel.Settings.TopBarScale = percent;
            ApplyTopBarScale();
        };
        grip.DragCompleted += () =>
        {
            ViewModel.SaveSettingsPublic();
            _settingsHandler.SelectChromeSizes();
            CrashReporter.Log($"[MainWindow.ChromeSizes] Top bar size {ViewModel.Settings.TopBarScale}%");
        };
        grip.Reset += () =>
        {
            ViewModel.Settings.TopBarScale = 100;
            ApplyTopBarScale();
            ViewModel.SaveSettingsPublic();
            _settingsHandler.SelectChromeSizes();
            CrashReporter.Log("[MainWindow.ChromeSizes] Top bar size reset");
        };
    }

    private void HookListGrip(ResizeGrip grip)
    {
        // Like the top bar: dragging the edge scales the whole list (its width follows)
        double startWidth = 0, startScale = 1;
        grip.DragStarted += () =>
        {
            startWidth = GameViewPanel.ColumnDefinitions[0].ActualWidth;
            startScale = ViewModel.Settings.GameListScale / 100.0;
        };
        grip.DragDelta += dx =>
        {
            if (startWidth <= 0) return;
            int percent = (int)Math.Round(startScale * (startWidth + dx) / startWidth * 100);
            percent = Math.Clamp(percent, ChromeScaleMin, ChromeScaleMax);
            if (percent == ViewModel.Settings.GameListScale) return;
            ViewModel.Settings.GameListScale = percent;
            ApplyGameListScale();
        };
        grip.DragCompleted += () =>
        {
            ViewModel.SaveSettingsPublic();
            _settingsHandler.SelectChromeSizes();
            CrashReporter.Log($"[MainWindow.ChromeSizes] Game list size {ViewModel.Settings.GameListScale}%");
        };
        grip.Reset += () =>
        {
            ViewModel.Settings.GameListScale = 100;
            ApplyGameListScale();
            ViewModel.SaveSettingsPublic();
            _settingsHandler.SelectChromeSizes();
            CrashReporter.Log("[MainWindow.ChromeSizes] Game list size reset");
        };
    }

    private double MaxGameListWidth()
    {
        double panel = GameViewPanel.ActualWidth;
        return panel > 0 ? Math.Clamp(panel - GamePageMinWidth, GameListMinWidth, GameListMaxWidth) : GameListMaxWidth;
    }

    /// <summary>Applies the top bar size, game list size and width (layout changes and settings changes).</summary>
    internal void ApplyChromeSizes()
    {
        ApplyTopBarScale();
        ApplyGameListScale();
    }

    internal void ApplyTopBarScale()
    {
        if (_topBarScaleBox == null || _xamlTopBar == null) return;
        double s = Math.Clamp(ViewModel.Settings.TopBarScale, ChromeScaleMin, ChromeScaleMax) / 100.0;
        _topBarScaleBox.Factor = s;
        var p = _topBarBasePad;
        _xamlTopBar.Padding = new Thickness(p.Left * s, p.Top * s, p.Right * s, p.Bottom * s);
    }

    /// <summary>Sets the top bar's padding at 100% (the layouts restyle it); the top bar size scales it.</summary>
    private void SetTopBarPadding(Thickness pad)
    {
        _topBarBasePad = pad;
        if (_topBarScaleBox != null) ApplyTopBarScale();
        else if (_xamlTopBar != null) _xamlTopBar.Padding = pad;
    }

    internal void ApplyGameListScale()
    {
        if (_listScaleBox == null) return;
        bool spotlight = _detailPanelBuilder?.Layout.IsSpotlight == true;
        _listScaleBox.Factor = GameListFactor(spotlight);
        ApplyGameListWidth();
    }

    private double GameListFactor(bool spotlight) =>
        spotlight ? 1.0 : Math.Clamp(ViewModel.Settings.GameListScale, ChromeScaleMin, ChromeScaleMax) / 100.0;

    private void ApplyGameListWidth()
    {
        try
        {
            bool spotlight = _detailPanelBuilder?.Layout.IsSpotlight == true;
            if (_listWidthGrip != null) _listWidthGrip.Visibility = spotlight ? Visibility.Collapsed : Visibility.Visible;
            if (GameViewPanel.ColumnDefinitions.Count == 0) return;
            double width = Math.Clamp(GameListDefaultWidth * GameListFactor(spotlight), GameListMinWidth, MaxGameListWidth());
            var col = GameViewPanel.ColumnDefinitions[0];
            if (Math.Abs(col.Width.Value - width) >= 0.5 || !col.Width.IsAbsolute)
                col.Width = new GridLength(width);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ApplyGameListWidth] {ex.Message}");
        }
    }
}
