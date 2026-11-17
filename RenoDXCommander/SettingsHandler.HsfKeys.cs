// SettingsHandler.HsfKeys.cs -- "HSF" card in Settings › Components, to the right of the GITC Uplift card: HSF's
// in-game keys (HSF.ini FrameGenKey / MfgKey / OsdKey). RHI writes the keys that are set into a game's HSF.ini when
// it installs HSF there (a new HSF.ini only, so keys set for one game in the HSF ⚙ stay); "Write keys to all HSF
// games" writes them to every game with HSF. None writes nothing (HSF's own default stays). Built in C# (XAML
// constraint), from EnsureUpliftKeySetting.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private int GetGlobalHsfKey(string iniKey) => iniKey switch
    {
        "FrameGenKey" => ViewModel.Settings.HsfFrameGenKey,
        "MfgKey"      => ViewModel.Settings.HsfMfgKey,
        "OsdKey"      => ViewModel.Settings.HsfOsdKey,
        _ => 0,
    };

    private void SetGlobalHsfKey(string iniKey, int vk)
    {
        switch (iniKey)
        {
            case "FrameGenKey": ViewModel.Settings.HsfFrameGenKey = vk; break;
            case "MfgKey":      ViewModel.Settings.HsfMfgKey = vk; break;
            case "OsdKey":      ViewModel.Settings.HsfOsdKey = vk; break;
        }
    }

    /// <summary>The HSF card: one key row per HSF hotkey, and the write-to-all button.</summary>
    private Border BuildHsfKeysCard()
    {
        var card = new Border { Style = FindStyle("SettingsCardStyle") };
        var body = new StackPanel { Spacing = 9 };
        card.Child = body;
        body.Children.Add(new TextBlock { Text = "HSF", Style = FindStyle("SettingsCardTitleStyle") });

        var boxes = new Dictionary<string, Grid>();
        foreach (var bind in HsfService.Keybinds)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
            var tip = bind.Help + " Click the box and press a key; Backspace or ↺ sets it back to None.\n\n"
                + "RHI writes it into a game's HSF.ini when it installs HSF there. None leaves HSF's own default."
                + (bind.IniKey == "MfgKey" ? "" : "\n\nNeeds an HSF version with KEYBINDS in its menu (newer than 0.9.8.6).");
            var label = new TextBlock { Text = bind.Label, Style = FindStyle("SettingsLabelStyle"), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(label, tip);
            row.Children.Add(label);

            var iniKey = bind.IniKey;
            var keyBox = SingleKeyBox.Create(GetGlobalHsfKey(iniKey),
                vk => { SaveGlobalHsfKey(iniKey, vk, boxes); return vk; },
                () => { SaveGlobalHsfKey(iniKey, 0, boxes); return 0; },
                tip, "None (leave HSF's own default)",
                FindStyle("SettingsTextBoxStyle"), FindStyle("SettingsIconButtonStyle"));
            Grid.SetColumn(keyBox, 1);
            row.Children.Add(keyBox);
            boxes[iniKey] = keyBox;
            body.Children.Add(row);
        }

        var write = MakeCustomFoldersButton("Write keys to all HSF games", primary: true,
            "Writes the keys that are set into the HSF.ini of every game with HSF. Keys set to None are left as each game has them. Skips hidden games.");
        write.HorizontalAlignment = HorizontalAlignment.Left;
        write.Click += async (s, e) =>
        {
            try { await WriteHsfKeysToAllAsync(); }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.WriteHsfKeysToAll] Unhandled error — {ex.Message}"); }
        };
        body.Children.Add(write);
        return card;
    }

    /// <summary>Saves one global HSF key; another global HSF key on the same key goes back to None (one key, one job).</summary>
    private void SaveGlobalHsfKey(string iniKey, int vk, Dictionary<string, Grid> boxes)
    {
        if (GetGlobalHsfKey(iniKey) == vk) return;
        SetGlobalHsfKey(iniKey, vk);
        if (vk > 0)
            foreach (var other in HsfService.Keybinds)
                if (other.IniKey != iniKey && GetGlobalHsfKey(other.IniKey) == vk)
                {
                    SetGlobalHsfKey(other.IniKey, 0);
                    if (boxes.TryGetValue(other.IniKey, out var g) && g.Children.OfType<TextBox>().FirstOrDefault() is { } otherBox)
                        otherBox.Text = SingleKeyBox.Text(0);
                }
        ViewModel.SaveSettingsPublic();
        CrashReporter.Log($"[SettingsHandler.HsfKeys] {iniKey} saved: {(vk == 0 ? "none" : vk.ToString())}");
    }

    /// <summary>Writes the global HSF keys into every game with HSF (not hidden).</summary>
    private async Task WriteHsfKeysToAllAsync()
    {
        var keys = ViewModel.GlobalHsfKeybinds();
        string message;
        if (keys.Values.All(v => v <= 0))
            message = "Pick a key first. None leaves each game's own HSF keys alone.";
        else
        {
            var svc = App.Services.GetRequiredService<HsfService>();
            var games = ViewModel.AllCards
                .Where(c => !c.IsHidden && !string.IsNullOrEmpty(c.InstallPath))
                .Select(c => c.InstallPath!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            int count = await Task.Run(() => games.Count(path =>
                File.Exists(HsfService.IniPath(path)) && svc.ApplyKeybinds(path, keys) > 0));
            CrashReporter.Log($"[SettingsHandler.WriteHsfKeysToAll] Keys written to {count} game(s)");
            message = $"HSF keys written to {count} game{(count == 1 ? "" : "s")}. Games that are running pick them up straight away.";
        }
        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "HSF",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        });
    }
}
