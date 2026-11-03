// DetailLayoutHost.Overview.cs -- the Tabs layout's Overview tab (mockup A): a card per component and
// "At a glance" tiles that open the other tabs.
// The cards hold no install rules of their own. They copy state from the (hidden) Components rows
// after DetailPanelBuilder.UpdateDetailComponentRows fills them: the row's status text and colour
// pick the pill, the row's button text and colour pick the main button, and the row's Info / ⚙ / ✕
// decide which icon buttons show. Their buttons call the rows' own click handlers with the row's
// button as sender. A change to a row's rules therefore shows up in the cards without touching this file.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Controls;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    private const double Gap = 12;

    private StackPanel? _overview;
    private AutoFitGrid? _cardsPanel;
    private AutoFitGrid? _glancePanel;
    private TextBlock? _noModNote;
    private StackPanel? _overviewGlance; // hidden when Overview opens in the Spotlight panel
    private readonly List<OverviewCard> _cards = new();

    /// <summary>Adds the Overview panel after the tab strip (Tabs) or removes it (other layouts).</summary>
    private void EnsureOverview(bool wanted)
    {
        var panel = SectionsPanel;
        if (!wanted)
        {
            if (_overview != null && panel.Children.Contains(_overview))
                panel.Children.Remove(_overview);
            return;
        }

        if (_overview == null) BuildOverview();
        int want = FirstSectionIndex + 1; // right after the tab strip
        int idx = panel.Children.IndexOf(_overview);
        if (idx == want) return;
        if (idx >= 0) panel.Children.RemoveAt(idx);
        panel.Children.Insert(Math.Min(want, panel.Children.Count), _overview!);
    }

    private void BuildOverview()
    {
        _overview = new StackPanel { Spacing = 20 };
        // Mockup: repeat(auto-fit, minmax(260px / 220px, 1fr)), 12 px gaps
        _cardsPanel = new AutoFitGrid { MinColumnWidth = 260, ColumnSpacing = Gap, RowSpacing = Gap };
        _glancePanel = new AutoFitGrid { MinColumnWidth = 220, ColumnSpacing = Gap, RowSpacing = Gap };
        _noModNote = new TextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextSecondary),
            Visibility = Visibility.Collapsed,
        };

        // One card per row, except ReLimiter and Display Commander, which share the frame limiter card
        var rows = _window.ComponentRows;
        var ul = rows.First(r => r.Key == "Ul");
        var dc = rows.First(r => r.Key == "Dc");
        foreach (var row in rows)
        {
            if (row.Key == "Dc") continue;
            var card = row.Key == "Ul"
                ? new OverviewCard(this, new[] { ul, dc })
                : new OverviewCard(this, new[] { row });
            _cards.Add(card);
            _cardsPanel.Children.Add(card.Root);
        }

        var components = new StackPanel { Spacing = 12 };
        components.Children.Add(TabsTheme.SectionHeading("Components"));
        components.Children.Add(_cardsPanel);
        components.Children.Add(_noModNote);

        var glance = new StackPanel { Spacing = 12 };
        glance.Children.Add(TabsTheme.SectionHeading("At a glance"));
        glance.Children.Add(_glancePanel);

        _overview.Children.Add(components);
        _overview.Children.Add(glance);
        _overviewGlance = glance;

    }

    /// <summary>Copies the Components rows into the Overview cards. Cheap; called after every row update.</summary>
    internal void RefreshOverviewCards()
    {
        if (IsSpotlight) RefreshSpotStatus();
        if (_overview == null || !IsTabs) return;
        foreach (var card in _cards) card.Update();

        // "No RenoDX mod available" note, copied from the Components section
        if (_noModNote != null)
        {
            var noMod = _window.NoModMessage;
            var text = noMod.Visibility == Visibility.Visible ? FirstText(noMod) : null;
            _noModNote.Text = text ?? "";
            _noModNote.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Copies only install progress and messages (they change many times a second).</summary>
    internal void RefreshOverviewProgress()
    {
        if (_overview == null || !IsTabs) return;
        foreach (var card in _cards) card.UpdateProgress();
    }

    /// <summary>Refreshes cards and tiles. Called when Overview is shown.</summary>
    internal void RefreshOverview()
    {
        if (_overview == null || !IsTabs) return;
        RefreshOverviewCards();
        RefreshGlance();
    }

    /// <summary>A section's collapsed-header summary changed (some are filled in later, in the background).</summary>
    internal void SummaryChanged()
    {
        if (IsSpotlight) { RefreshTuneTiles(); return; }
        if (_overview?.Visibility == Visibility.Visible) RefreshGlance();
    }

    // ── At a glance ───────────────────────────────────────────────────────────

    private static string GlanceFallback(string key) => key switch
    {
        "GameOverrides"       => "Name, DLL names, launch options, shaders and addons",
        "NeuralRendering"     => "Not set up",
        "NvidiaProfileDlss"   => "No DLSS or Streamline files found",
        "NvidiaProfileDriver" => "NVIDIA driver settings for this game",
        "Management"          => "Install folder, reset and report",
        "Extras"              => "OptiScaler, frame generation unlocks, DXVK and more",
        _                     => "",
    };

    private void RefreshGlance()
    {
        if (_glancePanel == null) return;
        _glancePanel.Children.Clear();
        foreach (var key in TabKeys)
        {
            if (key == OverviewKey) continue;
            _glancePanel.Children.Add(MakeGlanceTile(key));
        }
    }

    internal Border MakeGlanceTile(string key)
    {
        var summary = new TextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = TabsTheme.B(TabsTheme.TextSecondary),
        };
        if (!CopySummary(key, summary)) summary.Text = GlanceFallback(key);

        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock
        {
            Text = GetTitle(key),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary),
        });
        body.Children.Add(summary);

        var tile = new Border
        {
            Background = TabsTheme.B(TabsTheme.Card),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14),
            Child = body,
        };
        TabsTheme.SetHandTip(tile, $"Open {GetTitle(key)}");
        TabsTheme.UseHandCursor(tile);
        tile.PointerEntered += (s, e) => tile.Background = TabsTheme.B(TabsTheme.RowHover);
        tile.PointerExited += (s, e) => tile.Background = TabsTheme.B(TabsTheme.Card);
        tile.Tapped += (s, e) => SelectTab(key);
        return tile;
    }

    /// <summary>
    /// Copies the summary from the section's header row (the TextBlocks after the ≡ handle, arrow and
    /// title, as shown when the section is collapsed in Classic). Values keep their colours; labels use
    /// the tile's text colour. False when the section has no summary yet.
    /// </summary>
    private bool CopySummary(string key, TextBlock target)
    {
        if (!_headers.TryGetValue(key, out var header) || header is not Panel row) return false;
        bool any = false;
        for (int i = 3; i < row.Children.Count; i++)
        {
            if (row.Children[i] is not TextBlock src) continue;
            if (any) target.Inlines.Add(new Run { Text = " · " });
            if (src.Inlines.Count == 0)
                target.Inlines.Add(new Run { Text = src.Text });
            else
                foreach (var inline in src.Inlines)
                {
                    if (inline is Run r)
                    {
                        // Keep each item ("RR 310.9.16 · F · 75%") on one line: only the separators
                        // between items ("  ·  ") may break; spaces inside an item become non-breaking
                        bool separator = r.Text.Trim() == "·";
                        var copy = new Run { Text = separator ? r.Text : r.Text.Replace(' ', '\u00A0') };
                        // Coloured values go green like the mockup; plain labels stay secondary
                        if (r.Foreground is SolidColorBrush fg && TabsTheme.ToneOf(fg) != TabsTheme.Tone.Grey
                            && !string.IsNullOrWhiteSpace(r.Text) && r.Text.Trim() != "·")
                            copy.Foreground = TabsTheme.ToneOf(fg) is TabsTheme.Tone.Green or TabsTheme.Tone.Blue
                                ? TabsTheme.B(TabsTheme.Value)
                                : TabsTheme.B(TabsTheme.PillFor(TabsTheme.ToneOf(fg)).Fg);
                        target.Inlines.Add(copy);
                    }
                    else if (inline is LineBreak)
                        target.Inlines.Add(new Run { Text = " " });
                }
            any = true;
        }
        if (any && target.Inlines.FirstOrDefault() is Run first)
            first.Text = first.Text.TrimStart(' ', '·', '—', '-', '|').TrimStart();
        return any && target.Inlines.OfType<Run>().Any(r => !string.IsNullOrWhiteSpace(r.Text));
    }

    // ── Button labels ─────────────────────────────────────────────────────────

    private static readonly string[] ActionVerbs = { "Install", "Reinstall", "Update", "Download", "Uninstall", "Repair" };

    /// <summary>
    /// The mockup's short button labels: "↺  Reinstall ReShade" becomes "Reinstall". Labels that
    /// don't start with an action verb (warnings such as "RE Framework required") are kept whole.
    /// </summary>
    internal static string ShortAction(string text)
    {
        int i = 0;
        while (i < text.Length && !char.IsLetterOrDigit(text[i])) i++;
        var t = i < text.Length ? text[i..].Trim() : text.Trim();
        var first = t.Split(' ', 2)[0];
        return ActionVerbs.Any(v => string.Equals(v, first, StringComparison.OrdinalIgnoreCase)) ? first : t;
    }

    // ── Card descriptions ─────────────────────────────────────────────────────

    /// <summary>The one-line description under a card's name (the mockup's "Stable channel · ..." line).</summary>
    private string? DescribeRow(string rowKey)
    {
        var vm = _window.ViewModel;
        var card = vm.SelectedGame;
        if (card == null) return null;
        var store = card.Source ?? "";
        switch (rowKey)
        {
            case "Rs":
            {
                if (card.UseNormalReShade) return "No Addons channel · addons off";
                var channel = vm.GetReShadeChannelOverride(card.GameName, store) ?? Settings.ReShadeChannel;
                if (string.IsNullOrWhiteSpace(channel)) channel = "Stable";
                var shaders = vm.GetPerGameShaderMode(card.GameName, store);
                var addons = vm.GetPerGameAddonMode(card.GameName, store);
                var line = string.Equals(shaders, addons, StringComparison.OrdinalIgnoreCase)
                    ? $"{channel} channel · {shaders.ToLowerInvariant()} shaders and addons"
                    : $"{channel} channel · {shaders.ToLowerInvariant()} shaders · {addons.ToLowerInvariant()} addons";
                return card.RequiresVulkanInstall ? line + " · Vulkan layer" : line;
            }
            case "Rdx":
                return !string.IsNullOrEmpty(card.InstalledAddonFileName)
                    ? card.InstalledAddonFileName
                    : "HDR mod for this game";
            case "Luma":
            {
                // The mod's Name is just the game's name, so describe it instead
                var luma = card.LumaMod;
                var line = luma?.IsGenericLuma == true ? "Generic Luma for DX11 Unreal Engine games"
                    : luma?.Author is { Length: > 0 } a ? $"HDR mod for this game by {a}" : "HDR mod for this game";
                if (luma?.RequiresDgVoodoo == true) line += " · installs dgVoodoo2";
                // Latest GitHub build, when it's newer than what's installed (Nexus-only mods have no build number)
                if (luma?.DownloadUrl != null && LatestLumaBuild() is > 0 and var latest
                    && latest > (card.LumaRecord?.InstalledBuildNumber ?? 0))
                    line = $"Build {latest} available · " + line;
                return line;
            }
            case "Ref":    return "Needed for ReShade in RE Engine games";
            case "DofFix": return "Fixes depth of field with RenoDX in Unreal Engine 5 games";
            default:       return null;
        }
    }

    private bool _lumaBuildRequested;

    /// <summary>
    /// The latest Luma build RHI knows of. The first time it isn't known (no Luma installed, so the
    /// update check never asked), fetch it once in the background and refresh the cards.
    /// </summary>
    private int LatestLumaBuild()
    {
        var luma = App.Services.GetService(typeof(Services.ILumaService)) as Services.ILumaService;
        if (luma == null) return 0;
        if (luma.LastKnownBuildNumber > 0 || _lumaBuildRequested) return luma.LastKnownBuildNumber;
        _lumaBuildRequested = true;
        _ = Task.Run(async () =>
        {
            try
            {
                if (await luma.GetLatestBuildNumberAsync().ConfigureAwait(false) > 0)
                    _window.DispatcherQueue.TryEnqueue(RefreshOverviewCards);
            }
            catch (Exception ex) { Services.CrashReporter.Log($"[DetailLayoutHost.LatestLumaBuild] Failed — {ex.Message}"); }
        });
        return 0;
    }

    // Frame limiter card: the one shown when neither is installed (this session only)
    private string _limiterChoice = "Ul";

    // ── Copy helpers ──────────────────────────────────────────────────────────

    private const string InfoArrow = "◄";

    /// <summary>The visible text of a button's content (a string, or the label TextBlock of WithInfoArrow's Grid).</summary>
    internal static string ContentText(object? content) => content switch
    {
        null => "",
        string s => s,
        TextBlock tb => tb.Text,
        Panel p => p.Children.OfType<TextBlock>().Select(t => t.Text).FirstOrDefault(t => t != InfoArrow) ?? "",
        ContentControl cc => ContentText(cc.Content),
        _ => content.ToString() ?? "",
    };

    /// <summary>True when WithInfoArrow added its "this game has notes" arrow.</summary>
    internal static bool HasInfoArrow(object? content) =>
        content is Panel p && p.Children.OfType<TextBlock>().Any(t => t.Text == InfoArrow);

    /// <summary>A copy of an element's tooltip when it's plain text (tooltip elements can't be shared).</summary>
    internal static string? TooltipText(DependencyObject d) => ToolTipService.GetToolTip(d) switch
    {
        string s => s,
        TextBlock tb => tb.Text,
        ToolTip { Content: string s2 } => s2,
        ToolTip { Content: TextBlock tb2 } => tb2.Text,
        _ => null,
    };

    private static string? FirstText(DependencyObject root)
    {
        if (root is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
        if (root is Border { Child: { } child }) return FirstText(child);
        if (root is Panel panel)
            foreach (var c in panel.Children)
                if (FirstText(c) is { } t) return t;
        return null;
    }

    // ── One component card ────────────────────────────────────────────────────

    /// <summary>
    /// A mockup A component card: caption, name, status pill, description (or the frame limiter switch),
    /// main button, Info / ⚙ / ✕ and install progress. Built once; Update() copies the row's state.
    /// The frame limiter card covers two rows (ReLimiter, Display Commander) with a switch between them.
    /// </summary>
    private sealed class OverviewCard
    {
        private readonly DetailLayoutHost _host;
        private readonly ComponentRowRef[] _rows;
        private readonly TextBlock _caption;
        private readonly TextBlock _name;
        private readonly Border _pill;
        private readonly TextBlock _pillText;
        private readonly TextBlock _desc;
        private readonly Border _switchHost;
        private readonly Grid _buttons;
        private Button _install;
        private TabsTheme.Look _installLook;
        private readonly Button _info;
        private readonly Button _cog;
        private readonly Button _delete;
        private readonly ProgressBar _progress;
        private readonly TextBlock _message;
        private ComponentRowRef? _shown;
        private string _switchState = "";

        internal Border Root { get; }

        internal OverviewCard(DetailLayoutHost host, ComponentRowRef[] rows)
        {
            _host = host;
            _rows = rows;

            _caption = new TextBlock
            {
                FontSize = 12,
                Foreground = TabsTheme.B(TabsTheme.TextMuted),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _name = new TextBlock
            {
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = TabsTheme.B(TabsTheme.TextPrimary),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var titles = new StackPanel { Spacing = 4 };
            titles.Children.Add(_caption);
            titles.Children.Add(_name);

            _pill = TabsTheme.MakePill(out _pillText);

            var top = new Grid { ColumnSpacing = 8 };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(titles);
            Grid.SetColumn(_pill, 1);
            top.Children.Add(_pill);

            _desc = new TextBlock
            {
                FontSize = 13,
                LineHeight = 19.5,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = TabsTheme.B(TabsTheme.TextSecondary),
            };
            _switchHost = new Border { Visibility = Visibility.Collapsed };

            _installLook = TabsTheme.BlueButton;
            _install = MakeInstallButton(_installLook);
            _info = TabsTheme.MakeIconButton(TabsTheme.GlyphInfo, TabsTheme.IconButton);
            _cog = TabsTheme.MakeIconButton(TabsTheme.GlyphSettings, TabsTheme.IconButton);
            _delete = TabsTheme.MakeIconButton(TabsTheme.GlyphClose, TabsTheme.DangerButton, 12);
            _info.Click   += (s, e) => { if (_shown is { } r) r.OnInfo(r.Info, e); };
            _cog.Click    += (s, e) => { if (_shown is { } r) r.OnCog(r.Cog, e); };
            _delete.Click += (s, e) => { if (_shown is { } r) r.OnDelete(r.Delete, e); };

            _buttons = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Bottom };
            _buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _buttons.Children.Add(_install);
            Grid.SetColumn(_info, 1);   _buttons.Children.Add(_info);
            Grid.SetColumn(_cog, 2);    _buttons.Children.Add(_cog);
            Grid.SetColumn(_delete, 3); _buttons.Children.Add(_delete);

            _progress = new ProgressBar
            {
                Height = 3,
                Maximum = 100,
                CornerRadius = new CornerRadius(2),
                Foreground = TabsTheme.B(TabsTheme.Accent),
                Visibility = Visibility.Collapsed,
            };
            _message = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };

            // Rows: top, description / switch, (filler), buttons, progress, message.
            // The filler row pushes the buttons to the bottom like the mockup's margin-top: auto.
            var body = new Grid { RowSpacing = 12 };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.Children.Add(top);
            var middle = new StackPanel();
            middle.Children.Add(_desc);
            middle.Children.Add(_switchHost);
            Grid.SetRow(middle, 1); body.Children.Add(middle);
            Grid.SetRow(_buttons, 3); body.Children.Add(_buttons);
            var progressRow = new StackPanel { Spacing = 6 };
            progressRow.Children.Add(_progress);
            progressRow.Children.Add(_message);
            Grid.SetRow(progressRow, 4); body.Children.Add(progressRow);

            Root = TabsTheme.MakeCard(body);
            Root.MinHeight = 172;
            Root.Visibility = Visibility.Collapsed;
        }

        private Button MakeInstallButton(TabsTheme.Look look)
        {
            var btn = new Button { HorizontalAlignment = HorizontalAlignment.Stretch };
            TabsTheme.Apply(btn, look);
            btn.Click += (s, e) => { if (_shown is { } r) r.OnInstall(r.Install, e); };
            return btn;
        }

        /// <summary>The row this card shows now (the frame limiter card picks one of its two rows).</summary>
        private ComponentRowRef? PickRow(out List<ComponentRowRef> visible)
        {
            visible = _rows.Where(r => r.Row.Visibility == Visibility.Visible).ToList();
            if (visible.Count == 0) return null;
            if (visible.Count == 1) return visible[0];
            // Frame limiter: the installed one wins (only one can be installed), else the user's pick
            var installed = visible.FirstOrDefault(r => r.Delete.IsHitTestVisible && r.Delete.Opacity > 0.05);
            if (installed != null) return installed;
            return visible.FirstOrDefault(r => r.Key == _host._limiterChoice) ?? visible[0];
        }

        internal void Update()
        {
            var r = PickRow(out var visible);
            _shown = r;
            Root.Visibility = r != null ? Visibility.Visible : Visibility.Collapsed;
            if (r == null) return;

            _caption.Text = r.Caption;
            _name.Text = string.IsNullOrWhiteSpace(r.Label?.Text) ? r.FallbackName : r.Label!.Text;

            // Pill: the row's status text, coloured by the row's status colour
            var status = (r.Status.Text ?? "").Replace("\r", "").Replace('\n', ' ').Trim();
            var tone = TabsTheme.ToneOf(r.Status.Foreground);
            _pillText.Text = status;
            TabsTheme.ApplyPill(_pill, _pillText, TabsTheme.PillFor(tone));
            _pill.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
            ToolTipService.SetToolTip(_pill, TooltipText(r.Status));

            // An available update outlines the whole card in purple (mockup)
            Root.BorderBrush = TabsTheme.B(tone == TabsTheme.Tone.Purple ? "#4A3A7A" : TabsTheme.CardBorder);

            // Description, or the ReLimiter / Display Commander switch
            bool showSwitch = visible.Count > 1;
            UpdateSwitch(showSwitch ? visible : null, r);
            var desc = showSwitch ? null : _host.DescribeRow(r.Key);
            _desc.Text = desc ?? "";
            _desc.Visibility = string.IsNullOrEmpty(desc) ? Visibility.Collapsed : Visibility.Visible;

            // Main button: same text as the row's, coloured by the row's button text colour
            var src = r.Install;
            var look = TabsTheme.ButtonFor(TabsTheme.ToneOf(src.Foreground));
            if (look != _installLook)
            {
                // Hover colours are read when a button loads, so a new look needs a new button
                _buttons.Children.Remove(_install);
                _installLook = look;
                _install = MakeInstallButton(look);
                _buttons.Children.Insert(0, _install);
            }
            _install.Content = ShortAction(ContentText(src.Content));
            _install.IsEnabled = src.IsEnabled;
            _install.Opacity = src.Opacity;
            _install.IsHitTestVisible = src.IsHitTestVisible;
            ToolTipService.SetToolTip(_install, TooltipText(src));

            // Info: highlighted when the game has notes (the row shows a ◄ arrow for that)
            CopyIcon(r.Info, _info);
            bool notes = HasInfoArrow(src.Content);
            _info.BorderBrush = TabsTheme.B(notes ? TabsTheme.Accent : "#263042");
            ToolTipService.SetToolTip(_info, notes ? "This game has notes. Click to read them." : (TooltipText(r.Info) ?? "Info"));
            CopyIcon(r.Cog, _cog);
            ToolTipService.SetToolTip(_cog, TooltipText(r.Cog) ?? $"{_name.Text} settings");

            // ✕ is hidden with Opacity 0 in the row (keeps its column); hide it properly here
            bool showDelete = r.Delete.Visibility == Visibility.Visible && r.Delete.IsHitTestVisible && r.Delete.Opacity > 0.05;
            _delete.Visibility = showDelete ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(_delete, TooltipText(r.Delete) ?? $"Remove {_name.Text}");

            UpdateProgress();
        }

        private void UpdateSwitch(List<ComponentRowRef>? choices, ComponentRowRef shown)
        {
            if (choices == null)
            {
                _switchHost.Visibility = Visibility.Collapsed;
                _switchState = "";
                return;
            }
            var state = string.Join("|", choices.Select(c => c.Key)) + ">" + shown.Key;
            _switchHost.Visibility = Visibility.Visible;
            if (state == _switchState) return; // rebuild only when it changes (keeps hover state)
            _switchState = state;
            var labels = choices.Select(c => string.IsNullOrWhiteSpace(c.Label?.Text) ? c.FallbackName : c.Label!.Text).ToList();
            int sel = choices.IndexOf(shown);
            _switchHost.Child = TabsTheme.MakeSegmented(labels, sel, i =>
            {
                _host._limiterChoice = choices[i].Key;
                Update();
            });
        }

        private static void CopyIcon(Button src, Button dst)
        {
            bool shown = src.Visibility == Visibility.Visible && src.Opacity > 0.05;
            dst.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown) return;
            dst.IsEnabled = src.IsEnabled && src.IsHitTestVisible;
            dst.Opacity = src.IsEnabled ? 1.0 : 0.4;
        }

        internal void UpdateProgress()
        {
            var r = _shown;
            if (r == null || r.Row.Visibility != Visibility.Visible) return;
            _progress.Visibility = r.Progress.Visibility;
            _progress.IsIndeterminate = r.Progress.IsIndeterminate;
            _progress.Value = r.Progress.Value;
            _message.Visibility = string.IsNullOrWhiteSpace(r.Message.Text) ? Visibility.Collapsed : r.Message.Visibility;
            _message.Text = r.Message.Text;
            _message.Foreground = r.Message.Foreground;
        }
    }
}
