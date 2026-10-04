// UpdateLogWindow.cs — "Component Updates" secondary window.
// Shows a dated, grouped list of every component/addon/shader-pack download that
// occurred since RHI was installed.  Opens from the "Updates" button in the bottom bar.
// UI is built entirely in code-behind (same pattern as SetupWindow).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using WinRT.Interop;

namespace RenoDXCommander;

/// <summary>
/// Imperative secondary window that displays the component update log.
/// Singleton — MainWindow holds the one live reference; creating a new one
/// when the old is still open brings it to the front.
/// </summary>
public sealed class UpdateLogWindow : Window
{
    private readonly IUpdateLogService _updateLogService;

    public UpdateLogWindow(IUpdateLogService updateLogService)
    {
        _updateLogService = updateLogService;

        Title = Loc.Get("Component Updates");

        // Size — 720 × 500 logical px, DPI-scaled
        var hwnd = WindowNative.GetWindowHandle(this);
        uint dpi = NativeInterop.GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)(720 * scale),
            (int)(500 * scale)));
        CenterOnPrimaryDisplay();

        // Dark title bar
        NativeInterop.EnableDarkTitleBar(hwnd);
        if (AppWindow.TitleBar is { } tb)
        {
            var res = Application.Current.Resources;
            tb.BackgroundColor = (Windows.UI.Color)res["TitleBarBackground"];
            tb.ForegroundColor = (Windows.UI.Color)res["TitleBarForeground"];
            tb.InactiveBackgroundColor = (Windows.UI.Color)res["TitleBarInactiveBackground"];
            tb.InactiveForegroundColor = (Windows.UI.Color)res["TitleBarInactiveForeground"];
            tb.ButtonBackgroundColor = (Windows.UI.Color)res["TitleBarButtonBackground"];
            tb.ButtonForegroundColor = (Windows.UI.Color)res["TitleBarButtonForeground"];
            tb.ButtonHoverBackgroundColor = (Windows.UI.Color)res["TitleBarButtonHoverBackground"];
            tb.ButtonHoverForegroundColor = (Windows.UI.Color)res["TitleBarButtonHoverForeground"];
            tb.ButtonPressedBackgroundColor = (Windows.UI.Color)res["TitleBarButtonPressedBackground"];
            tb.ButtonPressedForegroundColor = (Windows.UI.Color)res["TitleBarButtonPressedForeground"];
            tb.ButtonInactiveBackgroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveBackground"];
            tb.ButtonInactiveForegroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveForeground"];
        }

        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter ov)
        {
            ov.IsResizable = false;
            ov.IsMaximizable = false;
        }

        Content = BuildRoot();
    }

    // ── Layout ────────────────────────────────────────────────────────────

    private UIElement BuildRoot()
    {
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Scrollable entry list ──
        var listPanel = new StackPanel { Spacing = 0 };
        BuildEntryList(listPanel);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(20, 12, 20, 8),
            Content = listPanel,
        };
        Grid.SetRow(scroll, 0);
        outer.Children.Add(scroll);

        // ── Footer bar ──
        var footer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderSubtleBrush),
            Background = UIFactory.Brush(ResourceKeys.SurfaceStatusBarBrush),
            Padding = new Thickness(20, 8, 20, 8),
        };

        var footerRow = new Grid();
        footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var countText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextDisabledBrush),
        };
        var entries = _updateLogService.GetAll();
        countText.Localize("Text", entries.Count == 0
            ? Loc.Get("No updates recorded yet.")
            : Loc.Format($"{entries.Count} update{(entries.Count == 1 ? "" : "s")} recorded"));
        Grid.SetColumn(countText, 0);
        footerRow.Children.Add(countText);

        var clearBtn = new Button
        {
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            Background = UIFactory.Brush(ResourceKeys.SurfaceRaisedBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush)
        }.Localize("Content", Loc.Get("Clear History"));
        clearBtn.Click += (_, _) =>
        {
            _updateLogService.Clear();
            DispatcherQueue.TryEnqueue(() =>
            {
                listPanel.Children.Clear();
                BuildEntryList(listPanel);
                countText.Localize("Text", Loc.Get("No updates recorded yet."));
            });
        };
        Grid.SetColumn(clearBtn, 1);
        footerRow.Children.Add(clearBtn);

        var closeBtn = new Button
        {
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush)
        }.Localize("Content", Loc.Get("Close"));
        closeBtn.Click += (_, _) => Close();
        Grid.SetColumn(closeBtn, 2);
        footerRow.Children.Add(closeBtn);

        footer.Child = footerRow;
        Grid.SetRow(footer, 1);
        outer.Children.Add(footer);

        return outer;
    }

    private void BuildEntryList(StackPanel listPanel)
    {
        var entries = _updateLogService.GetAll(); // newest-first

        if (entries.Count == 0)
        {
            listPanel.Children.Add(new TextBlock
            {
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            }.Localize("Text", Loc.Get("No component updates have been recorded yet.\n\nUpdates are captured whenever RHI downloads a new version of ReShade, RenoDX addons, shader packs, OptiScaler, Display Commander, or other components.")));
            return;
        }

        // Group by local date (today, yesterday, or full date)
        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);

        string GroupLabel(DateTime dt)
        {
            var d = dt.ToLocalTime().Date;
            if (d == today) return "Today";
            if (d == yesterday) return "Yesterday";
            return d.ToString("d MMMM yyyy");
        }

        var groups = entries
            .GroupBy(e => e.Timestamp.ToLocalTime().Date)
            .OrderByDescending(g => g.Key);

        foreach (var group in groups)
        {
            // Date header
            listPanel.Children.Add(new TextBlock
            {
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                Margin = new Thickness(0, 16, 0, 4)
            }.Localize("Text", GroupLabel(group.Key.Add(TimeSpan.Zero))));

            listPanel.Children.Add(UIFactory.MakeSeparator());

            foreach (var entry in group.OrderByDescending(e => e.Timestamp))
            {
                listPanel.Children.Add(BuildEntryRow(entry));
            }
        }
    }

    private static UIElement BuildEntryRow(UpdateLogEntry entry)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        // Time | Name | Version | Category
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48, GridUnitType.Pixel) });  // time
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });   // name
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200, GridUnitType.Pixel) });  // version
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130, GridUnitType.Pixel) });  // category

        // Time
        var timeText = new TextBlock
        {
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextDisabledBrush),
            VerticalAlignment = VerticalAlignment.Center
        }.Localize("Text", entry.Timestamp.ToLocalTime().ToString("HH:mm"));
        Grid.SetColumn(timeText, 0);
        row.Children.Add(timeText);

        // Component name
        var nameText = new TextBlock
        {
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        }.Localize("Text", entry.ComponentName);
        Grid.SetColumn(nameText, 1);
        row.Children.Add(nameText);

        // Version change
        string versionStr = BuildVersionString(entry);
        var versionText = new TextBlock
        {
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        }.Localize("Text", versionStr);
        Grid.SetColumn(versionText, 2);
        row.Children.Add(versionText);

        // Category badge
        var (bgKey, fgKey, borderKey) = CategoryColors(entry.Category);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Background = UIFactory.Brush(bgKey),
            BorderBrush = UIFactory.Brush(borderKey),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                FontSize = 10,
                Foreground = UIFactory.Brush(fgKey)
            }.Localize("Text", entry.Category),
        };
        Grid.SetColumn(badge, 3);
        row.Children.Add(badge);

        return row;
    }

    private static string BuildVersionString(UpdateLogEntry entry)
    {
        // GhRelease packs now store tag_name directly (e.g. "v4.2", "2026.09.10").
        // DirectUrl packs store an ETag or Last-Modified header — unreadable, show "Updated".
        string FormatVersion(string? v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            // Strip surrounding quotes (ETag format)
            v = v.Trim('"');
            // Hash / ETag: 32+ hex chars — not user-friendly
            if (v.Length >= 32 && v.All(c => "0123456789abcdefABCDEF-".Contains(c)))
                return "Updated";
            // Last-Modified date string (HTTP date format: "Wed, 01 Jan 2025 00:00:00 GMT")
            if (v.Contains(',') && v.Contains(':'))
                return "Updated";
            // Legacy: old tokens were asset filenames — try to extract a version number
            var vMatch = System.Text.RegularExpressions.Regex.Match(v, @"v?(\d+\.\d+(?:\.\d+)*)");
            if (vMatch.Success)
                return vMatch.Value.StartsWith("v") ? vMatch.Value : "v" + vMatch.Groups[1].Value;
            return v;
        }

        var newVer = FormatVersion(entry.NewVersion);
        var oldVer = FormatVersion(entry.OldVersion);

        if (!string.IsNullOrEmpty(oldVer) && oldVer != "Updated")
            return $"{oldVer}  →  {newVer}";
        return string.IsNullOrEmpty(newVer) || newVer == "Updated"
            ? "Updated"
            : newVer;
    }

    // Category → (bg resource key, fg resource key, border resource key)
    private static (string bg, string fg, string border) CategoryColors(string category) => category switch
    {
        "ReShade" => (ResourceKeys.AccentTealBgBrush, ResourceKeys.AccentTealBrush, ResourceKeys.AccentTealBorderBrush),
        "Addon" => (ResourceKeys.AccentGreenBgBrush, ResourceKeys.AccentGreenBrush, ResourceKeys.AccentGreenBorderBrush),
        "Shader Pack" => (ResourceKeys.AccentPurpleBgBrush, ResourceKeys.AccentPurpleBrush, ResourceKeys.AccentPurpleBorderBrush),
        "RenoDX DLSS5" => (ResourceKeys.AccentBlueBgBrush, ResourceKeys.AccentBlueBrush, ResourceKeys.AccentBlueBorderBrush),
        "DLSS Tool" => (ResourceKeys.AccentBlueBgBrush, ResourceKeys.AccentBlueBrush, ResourceKeys.AccentBlueBorderBrush),
        "OptiScaler" => (ResourceKeys.AccentAmberBgBrush, ResourceKeys.AccentAmberBrush, ResourceKeys.BorderDefaultBrush),
        "OptiScaler Nightly" => (ResourceKeys.AccentAmberBgBrush, ResourceKeys.AccentAmberBrush, ResourceKeys.BorderDefaultBrush),
        "OptiPatcher" => (ResourceKeys.AccentAmberBgBrush, ResourceKeys.AccentAmberBrush, ResourceKeys.BorderDefaultBrush),
        "DLSS" => (ResourceKeys.AccentBlueBgBrush, ResourceKeys.AccentBlueBrush, ResourceKeys.AccentBlueBorderBrush),
        "Streamline" => (ResourceKeys.AccentBlueBgBrush, ResourceKeys.AccentBlueBrush, ResourceKeys.AccentBlueBorderBrush),
        "dgVoodoo2" => (ResourceKeys.AccentTealBgBrush, ResourceKeys.AccentTealBrush, ResourceKeys.AccentTealBorderBrush),
        _ => (ResourceKeys.SurfaceRaisedBrush, ResourceKeys.TextSecondaryBrush, ResourceKeys.BorderDefaultBrush),
    };

    // ── Helpers ───────────────────────────────────────────────────────────

    private void CenterOnPrimaryDisplay()
    {
        try
        {
            var area = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
            var winSize = AppWindow.Size;
            AppWindow.Move(new Windows.Graphics.PointInt32(
                area.X + (area.Width - winSize.Width) / 2,
                area.Y + (area.Height - winSize.Height) / 2));
        }
        catch { /* non-critical */ }
    }
}
