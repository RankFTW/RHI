// DetailLayoutHost.Controller.cs -- what controller support (MainWindow.Controller.cs) needs from the
// Spotlight page: the slide-in panel, the Launch button, the game menu and the tiles to return to.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    private Button? _hLaunchBtn;
    private Button? _manageBtn;

    internal bool IsPanelOpen => _panelOverlay?.Visibility == Visibility.Visible;
    internal string? PanelKey => _panelKey;
    internal FrameworkElement? PanelSheet => _panelSheet;
    internal bool IsCogInPanel => _panelCogView?.Visibility == Visibility.Visible;
    internal Button? LaunchButton => _hLaunchBtn;

    /// <summary>The first control in the panel: the cog's settings when one is open, else the section.</summary>
    internal DependencyObject? FirstInPanel()
    {
        if (!IsPanelOpen) return null;
        DependencyObject? root = IsCogInPanel ? _panelCogContent : _panelHost;
        return root == null ? null : FocusManager.FindFirstFocusableElement(root);
    }

    /// <summary>Y: the Launch button, as a click would.</summary>
    internal void PadLaunch()
    {
        if (_hLaunchBtn is { IsEnabled: true, IsLoaded: true } b)
            (FrameworkElementAutomationPeer.CreatePeerForElement(b) as ButtonAutomationPeer)?.Invoke();
    }

    /// <summary>
    /// LB / RB in the panel: the previous / next section, in the page's order (Components, then the
    /// Tune tiles), wrapping round. A cog open in the panel is closed first.
    /// </summary>
    internal bool StepPanel(int delta)
    {
        if (!IsPanelOpen) return false;
        var keys = new List<string> { OverviewKey };
        if (_tuneGrid != null)
            foreach (var child in _tuneGrid.Children)
                if (child is FrameworkElement { Visibility: Visibility.Visible, Tag: string key }) keys.Add(key);
        if (keys.Count < 2) return false;
        int index = Math.Max(0, keys.IndexOf(_panelKey ?? OverviewKey));
        var next = keys[((index + delta) % keys.Count + keys.Count) % keys.Count];
        if (IsCogInPanel) CloseCog(ContentDialogResult.None);
        OpenPanel(next);
        return true;
    }

    /// <summary>X: the game menu (the same one as right-clicking the banner).</summary>
    internal void PadGameMenu()
    {
        if (_hero == null || _window.ViewModel.SelectedGame is not { } game) return;
        _hero.Tag = game;
        _window.CardMoreMenu_Click(_hero, new RoutedEventArgs());
    }

    /// <summary>The page tile that opens <paramref name="key"/>: where B goes back to after closing the panel.</summary>
    internal Control? SpotTileFor(string? key)
    {
        if (key == AchievementsKey && _achStrip is { Visibility: Visibility.Visible } strip && _achSlot?.Visibility == Visibility.Visible)
            return strip;
        if (key == OverviewKey)
        {
            var status = _statusTiles.FirstOrDefault(t => t.Root.Visibility == Visibility.Visible)?.Root;
            return status ?? _manageBtn;
        }
        if (_tuneGrid != null)
            foreach (var child in _tuneGrid.Children)
                if (child is Control c && Equals(c.Tag, key)) return c;
        return _hLaunchBtn;
    }
}
