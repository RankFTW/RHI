// SettingsHandler.RtxVideo.cs -- Settings › Display › "RTX Video" card (built in C#, XAML constraint): RTX Video
// Super Resolution and RTX Video HDR, the NVIDIA App's System › Video settings for video in browsers and players.
// Inserted after the DLDSR card the first time Settings opens; re-read from the driver each time Settings opens.
// Driver calls: Services/RtxVideoService.cs (background thread). Changes apply at once (sliders 400 ms after the
// last move).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private StackPanel? _rvBody;
    private bool _rvCardFailed;
    private int _rvLoadId;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _rvSliderTimer;
    private Func<Task>? _rvPendingSlider;

    private static readonly string[] RvSrModes = { "Off", "Auto", "Very low", "Low", "Medium", "High" };
    private static readonly string[] RvPriorities = { "Low", "Medium", "High" };

    private void EnsureRtxVideoCard()
    {
        if (_rvBody != null) { _ = LoadRtxVideoCardAsync(); return; }
        if (_rvCardFailed) return;
        try
        {
            StackPanel? section = null;
            foreach (var child in _window.SettingsCardsPanel.Children)
                if (child is StackPanel sp && sp.Tag is string t && t == "Section:Display") { section = sp; break; }
            if (section == null)
            {
                _rvCardFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureRtxVideoCard] Display section not found — card not added");
                return;
            }

            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var outer = new StackPanel { Spacing = 10 };
            card.Child = outer;
            var head = new StackPanel { Spacing = 3 };
            head.Children.Add(new TextBlock { Text = "RTX Video", Style = FindStyle("SettingsCardTitleStyle") });
            head.Children.Add(new TextBlock
            {
                Text = "RTX Video Super Resolution and RTX Video HDR for videos in browsers and video players, not games. "
                     + "These are the NVIDIA App's System › Video settings; changes apply straight away.",
                Style = FindStyle("SettingsCardDescStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            outer.Children.Add(head);
            _rvBody = new StackPanel { Spacing = 10 };
            outer.Children.Add(_rvBody);

            int idx = section.Children.IndexOf(_window.DldsrControlCard);
            if (idx >= 0) section.Children.Insert(idx + 1, card);
            else section.Children.Add(card);
            _ = LoadRtxVideoCardAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            _rvCardFailed = true;
            _rvBody = null;
            CrashReporter.Log($"[SettingsHandler.EnsureRtxVideoCard] Failed — {ex.Message}");
        }
    }

    private async Task LoadRtxVideoCardAsync()
    {
        if (_rvBody == null) return;
        int id = ++_rvLoadId;
        if (_rvBody.Children.Count == 0) SetRtxVideoMessage("Reading the driver's settings...");
        RtxVideoState? state = null;
        bool hasApp = false;
        try
        {
            (state, hasApp) = await Task.Run(() => (RtxVideoService.Read(), RtxVideoService.FindNvCplDll() != null));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.SEHException)
        {
            CrashReporter.Log($"[SettingsHandler.LoadRtxVideoCardAsync] Read failed — {ex.Message}");
        }
        if (id != _rvLoadId || _rvBody == null) return;
        if (state == null || (!state.SrSupported && !state.HdrSupported))
        {
            SetRtxVideoMessage(hasApp
                ? "Not available: needs an NVIDIA RTX graphics card with a recent driver."
                : "Needs the NVIDIA App (and an NVIDIA RTX graphics card). RHI changes these settings through the NVIDIA App's files.");
            return;
        }
        BuildRtxVideoBody(state);
    }

    private void SetRtxVideoMessage(string text)
    {
        if (_rvBody == null) return;
        _rvBody.Children.Clear();
        _rvBody.Children.Add(new TextBlock { Text = text, Style = FindStyle("SettingsNoteStyle"), TextWrapping = TextWrapping.Wrap });
    }

    private void BuildRtxVideoBody(RtxVideoState s)
    {
        if (_rvBody == null) return;
        _rvBody.Children.Clear();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Spacing = 9, Margin = new Thickness(0, 0, 20, 0) };
        var divider = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush) };
        var right = new StackPanel { Spacing = 9, Margin = new Thickness(20, 0, 0, 0) };
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(divider);
        grid.Children.Add(right);
        _rvBody.Children.Add(grid);

        // ── Super Resolution ──
        left.Children.Add(new TextBlock { Text = "Super Resolution", Style = FindStyle("SettingsHeadStyle") });
        if (!s.SrSupported)
            left.Children.Add(new TextBlock { Text = "Not supported on this graphics card.", Style = FindStyle("SettingsNoteStyle"), TextWrapping = TextWrapping.Wrap });
        else
        {
            // Combo order: Off, Auto, Very low .. High  ↔  driver 0, 5, 1 .. 4
            int srIndex = s.SrMode switch { 5 => 1, >= 1 and <= 4 => s.SrMode + 1, _ => 0 };
            var srCombo = RvCombo(RvSrModes, srIndex, "Super Resolution",
                "Uses AI to upscale video and clean up compression artefacts in supported browsers and players. "
                + "Auto picks the quality level from the GPU priority below; the others are fixed quality levels (Very low uses the least GPU).");
            var prioCombo = RvCombo(RvPriorities, s.SrGpuPriority, "GPU priority",
                "Auto mode only: how much GPU time Super Resolution may use. High allows higher quality levels.");
            prioCombo.IsEnabled = srIndex == 1;
            srCombo.SelectionChanged += (_, _) =>
            {
                int i = srCombo.SelectedIndex;
                if (i < 0) return;
                int mode = i switch { 0 => 0, 1 => 5, _ => i - 1 };
                prioCombo.IsEnabled = i == 1;
                _ = RtxVideoWriteAsync(() => RtxVideoService.SetSuperResolution(mode), reload: true);
            };
            prioCombo.SelectionChanged += (_, _) =>
            {
                int i = prioCombo.SelectedIndex;
                if (i >= 0) _ = RtxVideoWriteAsync(() => RtxVideoService.SetGpuPriority(i), reload: false);
            };
            left.Children.Add(SettingRow("Super Resolution", srCombo));
            left.Children.Add(SettingRow("GPU priority", prioCombo));
            left.Children.Add(SettingRow("Status indicator", RvIndicatorCombo(s.SrIndicator, RtxVideoService.FlagSrIndicator,
                "Shows a small RTX VSR label on the video while Super Resolution is working.")));
            left.Children.Add(new TextBlock
            {
                Text = s.SrStatus is >= 1 and <= 4 ? $"Status: active (quality level {s.SrStatus})" : "Status: inactive (no video using it right now)",
                Style = FindStyle("SettingsHelpStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        // ── HDR ──
        right.Children.Add(new TextBlock { Text = "HDR", Style = FindStyle("SettingsHeadStyle") });
        if (!s.HdrSupported)
        {
            right.Children.Add(new TextBlock { Text = "Not supported on this graphics card or display.", Style = FindStyle("SettingsNoteStyle"), TextWrapping = TextWrapping.Wrap });
            return;
        }
        var hdrCombo = RvCombo(new[] { "Off", "On" }, s.HdrOn ? 1 : 0, "Video HDR",
            "Uses AI to turn SDR video into HDR in supported browsers and players. Needs Windows HDR turned on.");
        right.Children.Add(SettingRow("HDR", hdrCombo));

        int peakMin = s.HdrPeakMin > 0 ? s.HdrPeakMin : 400;
        int peakMax = s.HdrPeakMax >= peakMin ? s.HdrPeakMax : Math.Max(peakMin, 2000);
        var peak = RvSlider(peakMin, peakMax, Math.Clamp(s.HdrPeak, peakMin, peakMax), "",
            "Brightest highlight in nits. Set it to your display's peak brightness.");
        var mid = RvSlider(10, 100, Math.Clamp(s.HdrMiddleGrey, 10, 100), "",
            "Brightness of mid-tones in nits. Higher makes the whole picture brighter.");
        var contrast = RvSlider(-100, 100, Math.Clamp(s.HdrContrast, -100, 100), "%", "Contrast of the HDR picture.");
        var saturation = RvSlider(-100, 100, Math.Clamp(s.HdrSaturation, -100, 100), "%", "Colour saturation of the HDR picture.");
        right.Children.Add(SettingRow("Peak brightness (nits)", peak.Row));
        right.Children.Add(SettingRow("Middle grey (nits)", mid.Row));
        right.Children.Add(SettingRow("Contrast", contrast.Row));
        right.Children.Add(SettingRow("Saturation", saturation.Row));
        right.Children.Add(SettingRow("Status indicator", RvIndicatorCombo(s.HdrIndicator, RtxVideoService.FlagHdrIndicator,
            "Shows a small RTX HDR label on the video while Video HDR is working.")));
        right.Children.Add(new TextBlock
        {
            Text = "Needs Windows HDR on (Win + Alt + B). The sliders can be set while HDR is off.",
            Style = FindStyle("SettingsHelpStyle"),
            TextWrapping = TextWrapping.Wrap,
        });

        void SlidersChanged()
        {
            int p = (int)peak.Slider.Value, m = (int)mid.Slider.Value, c = (int)contrast.Slider.Value, sa = (int)saturation.Slider.Value;
            QueueRtxVideoSliderWrite(() => RtxVideoWriteAsync(() => RtxVideoService.SetHdrValues(p, m, c, sa), reload: false));
        }
        foreach (var sl in new[] { peak.Slider, mid.Slider, contrast.Slider, saturation.Slider })
            sl.ValueChanged += (_, _) => SlidersChanged();
        hdrCombo.SelectionChanged += (_, _) =>
        {
            int i = hdrCombo.SelectedIndex;
            if (i >= 0) _ = RtxVideoWriteAsync(() => RtxVideoService.SetHdrEnabled(i == 1), reload: false);
        };
    }

    private ComboBox RvCombo(string[] items, int selected, string name, string tooltip)
    {
        var combo = new ComboBox { Style = FindStyle("SettingsComboStyle") };
        foreach (var it in items) combo.Items.Add(new ComboBoxItem { Content = it });
        combo.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1); // before any handler is attached
        ToolTipService.SetToolTip(combo, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(combo, name);
        return combo;
    }

    private ComboBox RvIndicatorCombo(bool on, int flag, string tooltip)
    {
        var combo = RvCombo(new[] { "Off", "On" }, on ? 1 : 0, "Status indicator", tooltip);
        combo.SelectionChanged += (_, _) =>
        {
            int i = combo.SelectedIndex;
            if (i >= 0) _ = RtxVideoWriteAsync(() => RtxVideoService.SetIndicator(flag, i == 1), reload: false);
        };
        return combo;
    }

    private (Grid Row, Slider Slider) RvSlider(int min, int max, int value, string suffix, string tooltip)
    {
        var slider = new Slider { Minimum = min, Maximum = max, StepFrequency = 1, Value = value, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock
        {
            Text = $"{value}{suffix}",
            Style = FindStyle("SettingsLabelStyle"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 44,
            TextAlignment = TextAlignment.Right,
        };
        slider.ValueChanged += (_, e) => text.Text = $"{(int)e.NewValue}{suffix}";
        ToolTipService.SetToolTip(slider, tooltip);
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(slider);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return (row, slider);
    }

    /// <summary>Slider writes wait until the slider has been still for 400 ms.</summary>
    private void QueueRtxVideoSliderWrite(Func<Task> write)
    {
        _rvPendingSlider = write;
        if (_rvSliderTimer == null)
        {
            _rvSliderTimer = _window.DispatcherQueue.CreateTimer();
            _rvSliderTimer.Interval = TimeSpan.FromMilliseconds(400);
            _rvSliderTimer.IsRepeating = false;
            _rvSliderTimer.Tick += (_, _) =>
            {
                var w = _rvPendingSlider;
                _rvPendingSlider = null;
                if (w != null) _ = w();
            };
        }
        _rvSliderTimer.Stop();
        _rvSliderTimer.Start();
    }

    private async Task RtxVideoWriteAsync(Func<bool> write, bool reload)
    {
        bool ok;
        try { ok = await Task.Run(write); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.SEHException)
        {
            CrashReporter.Log($"[SettingsHandler.RtxVideoWriteAsync] Failed — {ex.Message}");
            ok = false;
        }
        if (!ok)
        {
            _window.ShowStatusBarMessage("RTX Video: the driver didn't accept the change. See the log for details.", TimeSpan.FromSeconds(8));
            reload = true; // show what the driver really has
        }
        if (reload) await LoadRtxVideoCardAsync();
    }
}
