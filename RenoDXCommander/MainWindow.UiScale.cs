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

    /// <summary>The window's root Grid (inside the interface scaling): drag distances measured against it are in layout units.</summary>
    internal UIElement? LayoutRoot => _uiScaledRoot ?? Content;

    /// <summary>
    /// Interface sizes offered in Settings and stepped through by Ctrl + mouse wheel, in percent:
    /// every 5% from 50 to 200 (the range <see cref="ApplyUiScale"/> accepts).
    /// </summary>
    internal static readonly int[] UiScaleOptions = Enumerable.Range(0, 31).Select(i => 50 + i * 5).ToArray();

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

    /// <summary>
    /// Ctrl + mouse wheel steps the interface size through <see cref="UiScaleOptions"/>, like browser zoom.
    /// Hooked on the original root (it stays in the tree when the scale host wraps it), with handled
    /// events included so scroll areas under the pointer don't swallow it.
    /// </summary>
    private void HookUiScaleWheel()
    {
        if (Content is not UIElement root) return;
        root.AddHandler(UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(UiScaleWheel_Changed), handledEventsToo: true);
    }

    private void UiScaleWheel_Changed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (!e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control)) return;
            int delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
            if (delta == 0) return;
            e.Handled = true;

            // The next size up or down from the current one (which may be off the list, e.g. an old 67%)
            int current = ViewModel.Settings.UiScale;
            int percent = delta > 0
                ? UiScaleOptions.FirstOrDefault(p => p > current, UiScaleOptions[^1])
                : UiScaleOptions.LastOrDefault(p => p < current, UiScaleOptions[0]);
            if (percent == current) return;

            ViewModel.Settings.UiScale = percent;
            ViewModel.SaveSettingsPublic(); // debounced, so a fast scroll writes once
            ApplyUiScale(percent);
            _settingsHandler.SelectUiScale();
            ShowStatusBarMessage($"Interface size {percent}%", TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.UiScaleWheel_Changed] {ex.Message}");
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
