// MainWindow.ExperimentalSection.cs — the Experimental section's container (new and not fully tested features, such
// as URTI). Built in C# rather than MainWindow.xaml (the publish pipeline's duplicate MainWindow.g.i.cs problem): the
// same frame as ExtrasContainer, added to DetailPanel just before ManagementContainer on first use. The layout host
// (DetailLayoutHost.GetContainer "Experimental") orders, shows and hides it like the XAML sections.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private Border? _experimentalContainer;
    private StackPanel? _experimentalPanel;

    /// <summary>The Experimental section's frame (created and placed in DetailPanel on first use).</summary>
    internal Border ExperimentalContainer
    {
        get
        {
            EnsureExperimentalSection();
            return _experimentalContainer!;
        }
    }

    /// <summary>The Experimental section's content panel (header row, then the body).</summary>
    internal StackPanel ExperimentalPanel
    {
        get
        {
            EnsureExperimentalSection();
            return _experimentalPanel!;
        }
    }

    private void EnsureExperimentalSection()
    {
        if (_experimentalContainer != null) return;
        _experimentalPanel = new StackPanel { Spacing = 6 };
        _experimentalContainer = new Border
        {
            Background      = UIFactory.Brush(ResourceKeys.SurfaceComponentTableBrush),
            BorderBrush     = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(12),
            Padding         = new Thickness(14, 12, 14, 12),
            Visibility      = Visibility.Collapsed,
            Child           = _experimentalPanel,
        };
        int at = DetailPanel.Children.IndexOf(ManagementContainer);
        if (at < 0) DetailPanel.Children.Add(_experimentalContainer);
        else DetailPanel.Children.Insert(at, _experimentalContainer);
    }
}
