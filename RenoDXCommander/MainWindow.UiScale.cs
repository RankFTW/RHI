// MainWindow.UiScale.cs — Settings › General › Interface size: scales RHI's window content.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    // The window's root Grid sits in a Canvas once a size other than 100% is used: the Canvas
    // doesn't clip, so the root can be laid out at window size ÷ scale and then scaled to fit.
    private Canvas? _uiScaleHost;
    private FrameworkElement? _uiScaledRoot;
    private double _uiScale = 1.0;

    /// <summary>Interface sizes offered in Settings, in percent.</summary>
    internal static readonly int[] UiScaleOptions = { 50, 60, 67, 75, 80, 90, 100, 110, 125, 150 };

    /// <summary>
    /// Sizes the whole window content to <paramref name="percent"/> of normal (on top of Windows
    /// scaling). Pop-ups that WinUI draws in their own layer (dialogs, dropdown lists, tooltips)
    /// keep the Windows size.
    /// </summary>
    internal void ApplyUiScale(int percent)
    {
        try
        {
            double scale = Math.Clamp(percent, 50, 200) / 100.0;
            if (_uiScaleHost == null)
            {
                if (Math.Abs(scale - 1.0) < 0.001) return; // 100%: leave the window as built
                if (Content is not FrameworkElement root) return;
                var host = new Canvas();
                if (Application.Current.Resources.TryGetValue(ResourceKeys.SurfaceBaseBrush, out var bg) && bg is Brush b)
                    host.Background = b;
                Content = host;
                host.Children.Add(root);
                host.SizeChanged += (s, e) => SizeScaledRoot();
                _uiScaleHost = host;
                _uiScaledRoot = root;
            }

            _uiScale = scale;
            _uiScaledRoot!.RenderTransform = Math.Abs(scale - 1.0) < 0.001
                ? null
                : new ScaleTransform { ScaleX = scale, ScaleY = scale };
            SizeScaledRoot();
            _crashReporter.Log($"[MainWindow.ApplyUiScale] Interface size {percent}%");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.ApplyUiScale] {ex.Message}");
        }
    }

    private void SizeScaledRoot()
    {
        if (_uiScaleHost == null || _uiScaledRoot == null) return;
        double w = _uiScaleHost.ActualWidth, h = _uiScaleHost.ActualHeight;
        if (w <= 0 || h <= 0) return;
        _uiScaledRoot.Width = w / _uiScale;
        _uiScaledRoot.Height = h / _uiScale;
    }
}
