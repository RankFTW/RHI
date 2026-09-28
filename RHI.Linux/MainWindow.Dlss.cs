using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux;

// Neural Rendering (DLSS 5) and Nvidia Profile Overrides sections, laid out like the Windows
// detail panel. Windows driver-profile values are applied through dxvk-nvapi launch options.
public sealed partial class MainWindow
{
    private readonly Dictionary<string, NrState> _nrStates = [];
    private readonly Dictionary<string, int> _nrGenerations = [];
    private readonly HashSet<string> _nrScanning = [];
    private static readonly IBrush Blue = Brush("#7AACDD"), Red = Brush("#E07070"), Overlay = Brush("#1E242C");
    private static readonly string? DriverVersion = ReadDriverVersion();

    private static string? ReadDriverVersion()
    {
        try { return File.Exists("/sys/module/nvidia/version") ? File.ReadAllText("/sys/module/nvidia/version").Trim() : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void InvalidateDlss(Game game)
    {
        _nrStates.Remove(game.Id);
        _nrGenerations[game.Id] = _nrGenerations.GetValueOrDefault(game.Id) + 1;
    }

    // DLSS detection walks the whole game folder, so it runs off the UI thread and is cached.
    private NrState? DlssState(Game game)
    {
        if (_nrStates.TryGetValue(game.Id, out var cached)) return cached;
        var generation = _nrGenerations.GetValueOrDefault(game.Id);
        if (!_nrScanning.Add(game.Id + "#" + generation)) return null;
        _ = Task.Run(() => NeuralRenderingSetup.Read(game)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _nrScanning.Remove(game.Id + "#" + generation);
            if (t.IsFaulted) { CrashReporter.Log(t.Exception!.ToString()); return; }
            if (_nrGenerations.GetValueOrDefault(game.Id) != generation || _nrStates.ContainsKey(game.Id)) return;
            _nrStates[game.Id] = t.Result;
            if (Selected?.Id == game.Id) ShowGame();
        }));
        return null;
    }

    private LaunchExtras Extras(Game game) => GameLaunch.Extras(game, _settings.For(game));

    private async Task RefreshDlssCatalogs()
    {
        try { await _dlss.Refresh(); } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException) { CrashReporter.Log("DLSS list: " + ex.Message); }
        try { await _releases.Refresh(); } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) { CrashReporter.Log("NR releases: " + ex.Message); }
        if (_games.Any(g => g.IsREEngine))
            try { await _ref.Refresh(); } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException) { CrashReporter.Log("RE Framework releases: " + ex.Message); }
        if (_states.Values.Any(s => s.Components.ContainsKey(OptiScaler.Component)))
            try { await _os.Refresh(); } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException) { CrashReporter.Log("OptiScaler releases: " + ex.Message); }
    }

    // ── Shared building blocks ───────────────────────────────────────────────
    private Border Section(string key, string title, string? summary, Control body, string? name = null)
    {
        var collapsed = _settings.CollapsedSections.Contains(key);
        var arrow = Label(collapsed ? "▶" : "▼", 10, Muted); arrow.Margin = new Thickness(0, 0, 6, 0);
        var heading = Label(title, 13, null, true); heading.TextWrapping = TextWrapping.NoWrap;
        var hint = Label(summary ?? "", 11, Muted); hint.Margin = new Thickness(12, 0, 0, 0); hint.TextTrimming = TextTrimming.CharacterEllipsis; hint.TextWrapping = TextWrapping.NoWrap;
        hint.IsVisible = collapsed && !string.IsNullOrEmpty(summary);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Children = { arrow, heading, hint }, Name = name == null ? null : name + "Header" };
        body.IsVisible = !collapsed; body.Margin = new Thickness(0, 10, 0, 0);
        header.PointerEntered += (_, _) => heading.Foreground = Teal;
        header.PointerExited += (_, _) => heading.Foreground = Brush("#E8ECF2");
        header.PointerPressed += (_, _) =>
        {
            var now = body.IsVisible; body.IsVisible = !now; arrow.Text = now ? "▶" : "▼"; hint.IsVisible = now && !string.IsNullOrEmpty(summary);
            if (now) _settings.CollapsedSections.Add(key); else _settings.CollapsedSections.Remove(key);
            _settings.Save();
        };
        var card = Card(new StackPanel { Children = { header, body } }); card.Name = name; return card;
    }
    private static StackPanel Field(string label, Control control, string? tip = null)
    {
        var field = new StackPanel { Spacing = 3, Children = { Label(label, 10, Muted), control } };
        if (tip != null) ToolTip.SetTip(field, tip);
        return field;
    }
    private static ComboBox Combo(IEnumerable<object> items, int selected, string? name = null)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = selected, FontSize = 11, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Name = name };
        return combo;
    }
    private static Button Link(string text, string url, IBrush color, double size = 10)
    {
        var b = new Button { Content = text, Foreground = color, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0, FontSize = size };
        b.Classes.Add("link"); b.Click += (_, _) => Proton.Open(url); ToolTip.SetTip(b, url); return b;
    }
    private static string LatestLabel(string? version) => string.IsNullOrEmpty(version) ? "Latest" : $"Latest ({version})";
    private static int IndexOf(IReadOnlyList<string> items, string? value) => value == null ? 0 : Math.Max(0, items.ToList().FindIndex(i => i.Equals(value, StringComparison.OrdinalIgnoreCase)));
    private static string? Pick(ComboBox combo) => combo.SelectedItem as string is { } s && !s.StartsWith("Latest") ? s : null;
}
