// DisplayModeEditor.cs -- the editor for one Display mode value (a game's value for a mode, or a mode's default):
// a dropdown for choices, a box + Set for numbers and text, a key capture box for hotkeys. The first choice / an
// empty value means "not set" (use the mode default, or nothing for mode defaults). Used by the per-game Display
// mode dialog (MainWindow.DisplayModes.cs) and the Settings card (SettingsHandler.DisplayModes.cs).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

internal static class DisplayModeEditor
{
    /// <param name="item">The item being edited.</param>
    /// <param name="value">The value set here, or null.</param>
    /// <param name="unsetLabel">What "not set" means, e.g. "Mode default (700 nits)" or "Not set".</param>
    /// <param name="set">Saves a value (null = not set). Returns an error to show, or null.</param>
    /// <param name="report">Shows a result line (text, isError).</param>
    public static FrameworkElement Build(DisplayModeItem item, string? value, string unsetLabel,
        Func<string?, Task<string?>> set, Action<string, bool> report)
    {
        string? current = value;

        async Task Save(string? v)
        {
            try
            {
                var error = await set(v);
                if (error != null) { report(error, true); return; }
                current = v;
                report(v == null ? $"{item.Label}: {unsetLabel}." : $"{item.Label}: {item.Format(v)}.", false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                CrashReporter.Log($"[DisplayModeEditor] {item.Key} save failed — {ex.Message}");
                report($"Couldn't save {item.Label}: {ex.Message}", true);
            }
        }

        switch (item.Kind)
        {
            case DisplayModeValueKind.Choice:
            {
                var choices = item.Choices?.Invoke() ?? Array.Empty<(string Label, string Value)>();
                var labels = new List<string> { unsetLabel };
                var values = new List<string?> { null };
                foreach (var (label, v) in choices) { labels.Add(label); values.Add(v); }
                int idx = 0;
                if (value != null)
                {
                    idx = values.FindIndex(v => v != null && v.Equals(value, StringComparison.OrdinalIgnoreCase));
                    if (idx < 0) { labels.Add(value); values.Add(value); idx = labels.Count - 1; }
                }
                var combo = CogDialog.MakeCombo(labels, idx);
                int last = idx;
                combo.SelectionChanged += async (_, _) =>
                {
                    if (combo.SelectedIndex < 0 || combo.SelectedIndex == last) return;
                    last = combo.SelectedIndex;
                    await Save(values[combo.SelectedIndex]);
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(combo, item.Label);
                return combo;
            }

            case DisplayModeValueKind.ReShadeKey:
            case DisplayModeValueKind.ReLimiterKey:
            {
                bool reshade = item.Kind == DisplayModeValueKind.ReShadeKey;
                string Shown() => current == null ? "" : item.Format(current);
                var box = CogDialog.MakeTextBox(Shown(), unsetLabel + " - click, then press a key");
                box.IsReadOnly = true;
                box.GotFocus += (_, _) => box.PlaceholderText = "Press a key...";
                box.LostFocus += (_, _) => { box.PlaceholderText = unsetLabel + " - click, then press a key"; box.Text = Shown(); };
                box.KeyDown += async (_, ev) =>
                {
                    var key = ev.Key;
                    int vk = (int)key;
                    if (vk is 0 or 16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165) return;
                    if (key is Windows.System.VirtualKey.Tab or Windows.System.VirtualKey.Escape or Windows.System.VirtualKey.Enter) return;
                    if (!reshade && key == Windows.System.VirtualKey.Back) return; // not a usable ReLimiter key
                    bool Down(Windows.System.VirtualKey k) => Microsoft.UI.Input.InputKeyboardSource
                        .GetKeyStateForCurrentThread(k).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                    bool shift = Down(Windows.System.VirtualKey.Shift), ctrl = Down(Windows.System.VirtualKey.Control), alt = Down(Windows.System.VirtualKey.Menu);
                    ev.Handled = true;
                    var v = reshade
                        ? (item.Key == DisplayModeItemKeys.RsScreenshotKey
                            ? HotkeyManager.BuildScreenshotHotkeyString(vk, shift, ctrl, alt)
                            : HotkeyManager.BuildHotkeyString(vk, shift, ctrl, alt))
                        : HotkeyManager.BuildUlHotkeyString(vk, shift, ctrl, alt);
                    await Save(v);
                    box.Text = Shown();
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, item.Label);
                var clear = CogDialog.StyledButton("Clear", CogButtonKind.Secondary, compact: true);
                ToolTipService.SetToolTip(clear, unsetLabel);
                clear.Click += async (_, _) => { await Save(null); box.Text = Shown(); };
                return CogDialog.Inline(box, clear);
            }

            default:
            {
                string placeholder = item.Kind switch
                {
                    DisplayModeValueKind.Number or DisplayModeValueKind.Decimal =>
                        unsetLabel + (item.AllowZero ? $" ({item.Min:0}-{item.Max:0}, 0 = off)" : $" ({item.Min:0}-{item.Max:0})"),
                    _ => unsetLabel,
                };
                var box = CogDialog.MakeTextBox(current ?? "", placeholder);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, item.Label);
                var setBtn = CogDialog.StyledButton("Set", CogButtonKind.Secondary, compact: true);
                var clear = CogDialog.StyledButton("Clear", CogButtonKind.Secondary, compact: true);
                ToolTipService.SetToolTip(clear, unsetLabel);

                async Task SetFromBox()
                {
                    var text = box.Text;
                    if (item.Kind == DisplayModeValueKind.Text)
                    {
                        // Launch arguments may be empty on purpose (no arguments in this mode)
                        if (text.Trim().Length == 0 && item.Key != DisplayModeItemKeys.LaunchArgs) { report($"Enter a value for {item.Label}, or press Clear.", true); return; }
                        await Save(text.Trim());
                        return;
                    }
                    var norm = item.Normalize(text);
                    if (norm == null)
                    {
                        report(item.AllowZero
                            ? $"Enter a number from {item.Min:0} to {item.Max:0} (0 = off)."
                            : $"Enter a number from {item.Min:0} to {item.Max:0}.", true);
                        return;
                    }
                    box.Text = norm;
                    await Save(norm);
                }
                setBtn.Click += async (_, _) => await SetFromBox();
                box.KeyDown += async (_, ev) =>
                {
                    if (ev.Key != Windows.System.VirtualKey.Enter) return;
                    ev.Handled = true;
                    await SetFromBox();
                };
                clear.Click += async (_, _) => { box.Text = ""; await Save(null); };
                return CogDialog.Inline(box, setBtn, clear);
            }
        }
    }
}
