// DetailPanelBuilder.Experimental.cs — Experimental section: new features that aren't fully tested yet (URTI first).
// Shown for every game in all layouts: Classic+ (collapsible, reorderable, before Management by default), Tabs (a tab
// between Neural Rendering and Management) and Spotlight (a Tune tile in the second row). Rows use the Extras row shape
// (6 columns), so Tabs / Spotlight arrange them with Extras' list cards (DetailLayoutHost.ArrangeExperimental).
// Items: URTI (Ray Tracing), CRCGI (Global Illumination), FrameWarp (Reprojection).
// The manifest can take an item out ("featureFlags": { "experimentalHidden": ["urti", "framewarp"] }); it stays where installed.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    internal const string ExperimentalSectionKey = "Experimental";

    public void BuildExperimentalSection(GameCardViewModel card)
    {
        _window.ViewModel.SetLastUiAction($"BuildExperimentalSection({card.GameName})");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var panel = _window.ExperimentalPanel;
        panel.Children.Clear();
        Layout.ShowSection(ExperimentalSectionKey);

        // ── Collapsible header (same as Extras) ───────────────────────────────
        bool collapsed = Layout.IsCollapsed(ExperimentalSectionKey);
        var arrow = new TextBlock
        {
            Text      = collapsed ? "▶" : "▼",
            FontSize  = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(0, 0, 6, 0),
        };
        var title = new TextBlock
        {
            Text       = "Experimental",
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        ToolTipService.SetToolTip(arrow, SectionHeaderTooltip);
        ToolTipService.SetToolTip(title, SectionHeaderTooltip);
        headerRow.Children.Add(MakeDragHandle(_window.ExperimentalContainer));
        headerRow.Children.Add(arrow);
        headerRow.Children.Add(title);
        panel.Children.Add(headerRow);
        Layout.RegisterHeader(ExperimentalSectionKey, headerRow);

        var body = new StackPanel { Spacing = 10, Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible };
        panel.Children.Add(body);

        headerRow.PointerEntered += (s, e) => title.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        headerRow.PointerExited  += (s, e) => title.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var handCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var cursorProp  = DetailPanelBuilder.CursorProp;
        headerRow.PointerEntered += (s, e) => cursorProp?.SetValue(headerRow, handCursor);
        headerRow.PointerExited  += (s, e) => cursorProp?.SetValue(headerRow, arrowCursor);

        // ── Collapsed summary (no folder scans beyond a File.Exists) ──────────
        var installPath = card.InstallPath ?? "";
        bool urtiInstalled = UrtiService.IsInstalledIn(installPath);
        var summaryEntries = new List<(string, string?)>();
        if (urtiInstalled) summaryEntries.Add(("URTI", "On"));
        bool crcgiInstalled = CrcgiService.IsInstalledIn(installPath);
        if (crcgiInstalled) summaryEntries.Add(("CRCGI", CrcgiService.InstalledVersion(installPath) is { } cv ? "v" + cv : "On"));
        bool frameWarpInstalled = FrameWarpService.IsInstalledIn(installPath);
        if (frameWarpInstalled) summaryEntries.Add(("FrameWarp", FrameWarpService.InstalledVersion(installPath) ?? "On"));
        var summary = MakeSectionSummaryInlines(summaryEntries, "Experimental features installed for this game.");
        if (summary != null)
        {
            summary.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            headerRow.Children.Add(summary);
        }

        headerRow.PointerPressed += (s, e) =>
        {
            bool nowCollapsed = body.Visibility == Visibility.Visible;
            body.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            arrow.Text = nowCollapsed ? "▶" : "▼";
            if (summary != null)
                summary.Visibility = nowCollapsed ? Visibility.Visible : Visibility.Collapsed;
            Layout.SetCollapsed(ExperimentalSectionKey, nowCollapsed);
        };

        // ── Notice (one line) ─────────────────────────────────────────────────
        var notice = new TextBlock
        {
            Text = "New and not fully tested. Expect rough edges, and remove an item if a game misbehaves.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            Margin = new Thickness(0, 0, 0, 2),
        };
        body.Children.Add(notice);

        // ── Items (hidden by the manifest unless installed here) ──────────────
        int items = 0;
        if (!FeatureFlags.IsExperimentalHidden(FeatureFlags.ExperimentalUrti) || urtiInstalled)
        {
            body.Children.Add(MakeExtrasSeparator("Ray Tracing", _window.ExperimentalContainer));
            BuildUrtiRow(card, body);
            items++;
        }
        if (!FeatureFlags.IsExperimentalHidden(FeatureFlags.ExperimentalCrcgi) || crcgiInstalled)
        {
            body.Children.Add(MakeExtrasSeparator("Global Illumination", _window.ExperimentalContainer));
            BuildCrcgiRow(card, body);
            items++;
        }
        if (!FeatureFlags.IsExperimentalHidden(FeatureFlags.ExperimentalFrameWarp) || frameWarpInstalled)
        {
            body.Children.Add(MakeExtrasSeparator("Reprojection", _window.ExperimentalContainer));
            BuildFrameWarpRow(card, body);
            items++;
        }
        if (items == 0)
            body.Children.Add(new TextBlock
            {
                Text = "Nothing to try here right now.",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            });

        // Tabs / Spotlight: the same rows as list cards under the notice
        if (Layout.IsTabs) Layout.ArrangeExperimental(body, notice);

        Layout.SummaryChanged();
        CrashReporter.Log($"[BuildExperimentalSection] {sw.ElapsedMilliseconds}ms '{card.GameName}' ({items} item(s))");
    }

    private bool _experimentalRebuildPending;

    /// <summary>Rebuilds the Experimental section for <paramref name="card"/> if it's still the one shown (once per burst).</summary>
    public void RequestExperimentalRebuild(GameCardViewModel card)
    {
        if (_experimentalRebuildPending) return;
        _experimentalRebuildPending = true;
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            _experimentalRebuildPending = false;
            if (_currentDetailCard != card) return;
            BuildExperimentalSection(card);
        });
    }
}
