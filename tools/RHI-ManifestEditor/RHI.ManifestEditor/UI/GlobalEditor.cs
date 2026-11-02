using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RHI.ManifestEditor.Json;

namespace RHI.ManifestEditor.UI;

public sealed record GlobalSection(string Id, string Title, string Description);

/// <summary>Settings that aren't per game: packs, presets, authors, flags, lists.</summary>
public sealed class GlobalEditor
{
    public static readonly GlobalSection[] Sections =
    {
        new("flags", "Feature flags", "Turn app features on or off for every user."),
        new("authors", "Authors and donations", "Donate dialog: author links and what they're known for."),
        new("dgvoodoo", "dgVoodoo2 versions", "Versions offered for Luma's dgVoodoo2 install."),
        new("legacyrs", "Legacy ReShade versions", "Older ReShade versions offered in the Legacy picker."),
        new("shaderPacks", "Shader packs", "Add, change or hide shader packs."),
        new("addonPacks", "Addon packs", "Add, change or hide Addon Manager entries."),
        new("dlssPresets", "DLSS presets", "Preset choices added to the DLSS dropdowns."),
        new("dlssPresetsDev", "DLSS presets (dev builds)", "Preset choices shown only in dev builds."),
        new("prefixes", "Hide by name prefix", "Any game whose name starts with one of these is hidden."),
        new("profileExe", "NVIDIA profile exe exclusions", "Exe names never used to match NVIDIA profiles."),
        new("components", "Component download links", "Override where components are downloaded from."),
        new("misc", "Other settings", "Single values: links, PCGamingWiki options, manifest version."),
        new("raw", "Raw JSON", "Edit any field directly, e.g. emulatorGames or renodxExtraSettings."),
    };

    private readonly IEditorHost _host;
    private JsonDoc Doc => _host.Doc;

    public GlobalEditor(IEditorHost host) => _host = host;

    public UIElement Build(string id)
    {
        var s = Sections.First(x => x.Id == id);
        var page = new StackPanel { Spacing = 14, Padding = new Thickness(28, 20, 28, 40), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(Ui.T(s.Title, 26, bold: true));
        page.Children.Add(Ui.T(s.Description, 13, fg: Ui.Secondary));
        UIElement body = id switch
        {
            "flags" => Flags(),
            "authors" => Authors(),
            "dgvoodoo" => KeyValueEditor(new[] { "dgVoodooVersions" }, "Version", "Download link", JsonDoc.Placement.Top,
                "Newest version goes first: the first one is the default for new installs and Luma. Copy the link from the GitHub release (the tag format varies, so a guessed link can 404)."),
            "legacyrs" => StringListEditor(new[] { "legacyReShadeAvailable" }, "Version, e.g. 6.4.1", null),
            "shaderPacks" => ObjectDictEditor("shaderPacks", ShaderPackFields, "Pack ID",
                "Each pack has an ID. An ID that matches a built-in pack changes it; tick Hidden to remove a pack."),
            "addonPacks" => ObjectDictEditor("addonPacks", AddonPackFields, "Section ID (from Addons.ini)",
                "Entries are keyed by the addon's section ID in ReShade's Addons.ini. Matching IDs change the built-in entry; new IDs add an addon."),
            "dlssPresets" => PresetsEditor("dlssPresets"),
            "dlssPresetsDev" => PresetsEditor("dlssPresetsDev"),
            "prefixes" => StringListEditor(new[] { "blacklistPrefixes" }, "Prefix, e.g. Battle.net.", "Not case-sensitive."),
            "profileExe" => StringListEditor(new[] { "profileExeExclusions" }, "Exe name, e.g. launcher.exe", "Added to RHI's built-in list."),
            "components" => KeyValueEditor(new[] { "componentUrls" }, "Component", "Link", JsonDoc.Placement.Auto,
                "Known components: ueExtended (UE-Extended addon) and ueDofFix (DOF Fix addon)."),
            "misc" => Misc(),
            "raw" => RawEditor(),
            _ => Ui.T("?"),
        };
        page.Children.Add(body);
        return page;
    }

    // ── Sections ─────────────────────────────────────────────────────────────

    private static readonly (string Key, string Label, string Help)[] KnownFlags =
    {
        ("dlssNr", "DLSS Neural Rendering", "Shows the Neural Rendering section and DLSS NR downloads."),
        ("nexusMods", "Nexus Mods", "Nexus Mods account and downloads."),
        ("resolutionControl", "Resolution control", "Per-game resolution and DLDSR settings."),
    };

    private UIElement Flags()
    {
        var panel = new StackPanel { Spacing = 18 };
        var flags = KnownFlags.ToList();
        if (Doc.Get("featureFlags") is { IsObject: true } obj)
            foreach (var m in obj.Members!.Where(m => flags.All(f => f.Key != m.Key)))
                flags.Add((m.Key, m.Key, ""));
        foreach (var (key, label, help) in flags)
        {
            var cur = Doc.Get("featureFlags", key)?.AsBool;
            var ts = new ToggleSwitch { IsOn = cur == true, OnContent = "On", OffContent = cur == null ? "Off (not in the file)" : "Off" };
            ts.Toggled += (_, _) =>
            {
                bool on = ts.IsOn;
                _host.Apply(() => Doc.Set(new[] { "featureFlags" }, key, JsonOut.Bool(on)));
            };
            panel.Children.Add(Ui.Row(label, $"{help} ({key})".Trim(), ts));
        }
        return Ui.Card(panel);
    }

    private UIElement Authors()
    {
        var names = new List<string>();
        foreach (var f in new[] { "donationUrls", "authorRoles" })
            if (Doc.Get(f) is { IsObject: true } o)
                foreach (var m in o.Members!) if (!names.Contains(m.Key)) names.Add(m.Key);

        var rows = new StackPanel { Spacing = 8 };
        Grid MakeGrid()
        {
            var g = new Grid { ColumnSpacing = 8 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            return g;
        }
        var header = MakeGrid();
        header.Children.Add(Ui.Help("Author"));
        var h2 = Ui.Help("Donation link (Ko-fi etc.)"); Grid.SetColumn(h2, 1); header.Children.Add(h2);
        var h3 = Ui.Help("Known for"); Grid.SetColumn(h3, 2); header.Children.Add(h3);
        rows.Children.Add(header);

        void AddRow(string name)
        {
            var g = MakeGrid();
            var nameBox = Ui.Box(name, "Display name");
            var url = Ui.Box(Doc.GetString("donationUrls", name) ?? "", "https://ko-fi.com/...");
            var role = Ui.Box(Doc.GetString("authorRoles", name) ?? "", "e.g. RenoDX mods");
            string currentName = name;
            nameBox.LostFocus += (_, _) =>
            {
                var n = nameBox.Text.Trim();
                if (n.Length == 0 || n == currentName) { nameBox.Text = currentName; return; }
                var old = currentName;
                currentName = n;
                _host.Apply(() => Doc.Batch(() =>
                {
                    if (old.Length > 0)
                    {
                        Doc.RenameKey(new[] { "donationUrls" }, old, n);
                        Doc.RenameKey(new[] { "authorRoles" }, old, n);
                    }
                    else
                    {
                        // New row: write whatever is already typed
                        if (url.Text.Trim().Length > 0) Doc.Set(new[] { "donationUrls" }, n, JsonOut.Str(url.Text.Trim()));
                        if (role.Text.Trim().Length > 0) Doc.Set(new[] { "authorRoles" }, n, JsonOut.Str(role.Text.Trim()));
                    }
                }));
            };
            void Bind(TextBox box, string field) => box.TextChanged += (_, _) =>
            {
                var v = box.Text.Trim();
                if (currentName.Length == 0) return; // written once the name is filled in
                var who = currentName;
                _host.Defer($"{field}|{who}", () =>
                {
                    if (v.Length == 0) Doc.Remove(new[] { field }, who);
                    else Doc.Set(new[] { field }, who, JsonOut.Str(v));
                });
            };
            Bind(url, "donationUrls");
            Bind(role, "authorRoles");
            var rm = Ui.RemoveBtn(async () =>
            {
                if (currentName.Length > 0 && !await _host.Confirm("Remove author", $"Remove {currentName}'s donation link and description?", "Remove", danger: true)) return;
                var who = currentName;
                if (who.Length > 0)
                    _host.Apply(() => Doc.Batch(() => { Doc.Remove(new[] { "donationUrls" }, who); Doc.Remove(new[] { "authorRoles" }, who); }));
                rows.Children.Remove(g);
            });
            g.Children.Add(nameBox);
            Grid.SetColumn(url, 1); g.Children.Add(url);
            Grid.SetColumn(role, 2); g.Children.Add(role);
            Grid.SetColumn(rm, 3); g.Children.Add(rm);
            rows.Children.Add(g);
        }
        foreach (var n in names) AddRow(n);
        var add = Ui.Btn("Add author", () => AddRow(""));
        add.HorizontalAlignment = HorizontalAlignment.Left;

        var displayNames = KeyValueEditor(new[] { "authorDisplayNames" }, "Wiki handle", "Shown as", JsonDoc.Placement.Auto,
            "Shows a wiki maintainer's handle under a different name.");
        return Ui.V(14, Ui.Card(Ui.V(10, rows, add)), Ui.T("Display names", 16, bold: true), displayNames);
    }

    private UIElement Misc()
    {
        var panel = new StackPanel { Spacing = 18 };
        panel.Children.Add(Ui.Row("RTX HDR guide link", "Link in the RenoDX info window. Empty uses RHI's built-in link.", TopString("rtxHdrInfoUrl", "https://...")));

        var appId = Doc.Get("pcgwUseAppId")?.AsBool;
        var ts = new ToggleSwitch { IsOn = appId == true, OnContent = "Yes", OffContent = "No" };
        ts.Toggled += (_, _) => { bool on = ts.IsOn; _host.Apply(() => Doc.Set(Array.Empty<string>(), "pcgwUseAppId", JsonOut.Bool(on))); };
        panel.Children.Add(Ui.Row("PCGamingWiki: look up by Steam AppID", "Find PCGamingWiki pages by AppID instead of by name.", ts));

        panel.Children.Add(Ui.Row("PCGamingWiki link cache version", "Raise this number to make every user re-fetch PCGamingWiki links.", TopNumber("pcgwUrlCacheVersion")));
        panel.Children.Add(Ui.Row("Manifest version", "Logged by the app. Rarely changed.", TopNumber("version")));
        return Ui.Card(panel);
    }

    private TextBox TopString(string key, string placeholder)
    {
        var box = Ui.Box(Doc.GetString(key) ?? "", placeholder);
        box.TextChanged += (_, _) =>
        {
            var v = box.Text.Trim();
            _host.Defer(key, () =>
            {
                if (v.Length == 0) Doc.Remove(Array.Empty<string>(), key);
                else Doc.Set(Array.Empty<string>(), key, JsonOut.Str(v));
            });
        };
        return box;
    }

    private UIElement TopNumber(string key)
    {
        var cur = Doc.Get(key);
        var box = Ui.Box(cur != null ? Doc.Raw(cur) : "", "Number");
        box.MaxWidth = 200;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.TextChanged += (_, _) =>
        {
            if (!long.TryParse(box.Text.Trim(), out var n)) return;
            _host.Defer(key, () => Doc.Set(Array.Empty<string>(), key, JsonOut.Num(n)));
        };
        return box;
    }

    // ── Generic editors ──────────────────────────────────────────────────────

    /// <summary>Two-column editor for a { key: "value" } object.</summary>
    private UIElement KeyValueEditor(string[] objPath, string keyLabel, string valueLabel, JsonDoc.Placement placement, string? help)
    {
        var rows = new StackPanel { Spacing = 8 };
        Grid MakeGrid()
        {
            var g = new Grid { ColumnSpacing = 8 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.5, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            return g;
        }
        var header = MakeGrid();
        header.Children.Add(Ui.Help(keyLabel));
        var hv = Ui.Help(valueLabel); Grid.SetColumn(hv, 1); header.Children.Add(hv);
        rows.Children.Add(header);

        void AddRow(string key, JNode? value, bool atTop)
        {
            var g = MakeGrid();
            var kb = Ui.Box(key, keyLabel);
            string currentKey = key;
            UIElement valueEl;
            TextBox? vb = null;
            if (value != null && !value.IsString)
            {
                valueEl = Ui.T(Doc.Raw(value), 12, fg: Ui.Secondary);
                ToolTipService.SetToolTip(valueEl, "Not text. Edit under Raw JSON.");
            }
            else
            {
                vb = Ui.Box(value?.Str ?? "", valueLabel);
                valueEl = vb;
                vb.TextChanged += (_, _) =>
                {
                    if (currentKey.Length == 0) return;
                    var k = currentKey; var v = vb.Text.Trim();
                    _host.Defer($"{string.Join("/", objPath)}|{k}", () => Doc.Set(objPath, k, JsonOut.Str(v), placement));
                };
            }
            kb.LostFocus += (_, _) =>
            {
                var n = kb.Text.Trim();
                if (n == currentKey) return;
                if (n.Length == 0) { kb.Text = currentKey; return; }
                var old = currentKey;
                currentKey = n;
                _host.Apply(() =>
                {
                    if (old.Length > 0) Doc.RenameKey(objPath, old, n);
                    else Doc.Set(objPath, n, JsonOut.Str(vb?.Text.Trim() ?? ""), placement);
                });
            };
            var rm = Ui.RemoveBtn(() =>
            {
                var k = currentKey;
                if (k.Length > 0) _host.Apply(() => Doc.Remove(objPath, k));
                rows.Children.Remove(g);
            });
            g.Children.Add(kb);
            Grid.SetColumn((FrameworkElement)valueEl, 1); g.Children.Add(valueEl);
            Grid.SetColumn(rm, 2); g.Children.Add(rm);
            if (atTop) rows.Children.Insert(1, g); else rows.Children.Add(g);
            if (key.Length == 0) kb.Focus(FocusState.Programmatic);
        }
        if (Doc.Get(objPath) is { IsObject: true } obj)
            foreach (var m in obj.Members!) AddRow(m.Key, m.Value, false);
        var add = Ui.Btn(placement == JsonDoc.Placement.Top ? "Add (at the top)" : "Add", () => AddRow("", null, placement == JsonDoc.Placement.Top));
        add.HorizontalAlignment = HorizontalAlignment.Left;
        var panel = Ui.V(10);
        if (help != null) panel.Children.Add(Ui.Help(help));
        panel.Children.Add(rows);
        panel.Children.Add(Ui.Help("Type the name first, then the value. Changes are saved to the file when you press Save."));
        panel.Children.Add(add);
        return Ui.Card(panel);
    }

    /// <summary>Editor for an array of strings.</summary>
    private UIElement StringListEditor(string[] arrPath, string placeholder, string? help)
    {
        var list = new StackPanel { Spacing = 2 };
        void Refresh()
        {
            list.Children.Clear();
            var items = Doc.Get(arrPath)?.Items?.Where(i => i.IsString).Select(i => i.Str!).ToList() ?? new();
            if (items.Count == 0) list.Children.Add(Ui.Help("The list is empty."));
            foreach (var item in items)
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var t = Ui.T(item, 14);
                t.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(t);
                var v = item;
                var rm = Ui.RemoveBtn(() => { _host.Apply(() => Doc.RemoveFromArray(arrPath, v)); Refresh(); });
                Grid.SetColumn(rm, 1);
                g.Children.Add(rm);
                list.Children.Add(g);
            }
        }
        Refresh();
        var box = Ui.Box("", placeholder);
        void Add()
        {
            var v = box.Text.Trim();
            if (v.Length == 0) return;
            _host.Apply(() => Doc.AddToArray(arrPath, v));
            box.Text = "";
            Refresh();
        }
        box.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Add(); e.Handled = true; } };
        var addRow = new Grid { ColumnSpacing = 8 };
        addRow.ColumnDefinitions.Add(new ColumnDefinition());
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.Children.Add(box);
        var addBtn = Ui.Btn("Add", Add);
        Grid.SetColumn(addBtn, 1);
        addRow.Children.Add(addBtn);
        var panel = Ui.V(10);
        if (help != null) panel.Children.Add(Ui.Help(help));
        panel.Children.Add(addRow);
        panel.Children.Add(list);
        return Ui.Card(panel);
    }

    private enum FKind { Text, Multiline, Bool, Choice, List }
    private sealed record FDef(string Key, string Label, FKind Kind, string Help = "", string[]? Options = null);

    private static readonly FDef[] ShaderPackFields =
    {
        new("displayName", "Name", FKind.Text, "Shown in the shader list."),
        new("description", "Description", FKind.Multiline),
        new("category", "Category", FKind.Choice, "Where it's listed.", new[] { "Recommended", "Extra" }),
        new("kind", "Download type", FKind.Choice, "GhRelease = latest GitHub release asset. DirectUrl = a fixed zip link.", new[] { "GhRelease", "DirectUrl" }),
        new("url", "Download link", FKind.Text, "Zip link (DirectUrl) or GitHub releases API link (GhRelease)."),
        new("assetExt", "Release file type", FKind.Text, "GhRelease only: file extension to pick, e.g. .zip"),
        new("isMinimum", "Part of the Minimum set", FKind.Bool),
        new("requires", "Needs these packs", FKind.List, "Pack IDs, separated by commas."),
        new("disabled", "Hidden", FKind.Bool, "Removes the pack from the list (also hides built-in packs)."),
    };

    private static readonly FDef[] AddonPackFields =
    {
        new("packageName", "Name", FKind.Text),
        new("description", "Description", FKind.Multiline),
        new("downloadUrl", "Download link", FKind.Text),
        new("downloadUrl32", "32-bit download link", FKind.Text, "Only if 32 and 64-bit are separate downloads."),
        new("downloadUrl64", "64-bit download link", FKind.Text),
        new("releaseApiUrl", "GitHub releases API link", FKind.Text, "To always get the latest release."),
        new("repositoryUrl", "Project page", FKind.Text),
        new("effectInstallPath", "Effect install path", FKind.Text),
        new("deployFileName", "Installed file name", FKind.Text),
        new("disabled", "Hidden", FKind.Bool, "Removes the addon from the Addon Manager."),
    };

    /// <summary>List of entries on the left, a form for the selected entry on the right.</summary>
    private UIElement ObjectDictEditor(string field, FDef[] fields, string keyLabel, string help)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MinWidth = 240, MaxHeight = 640 };
        var form = new StackPanel { Spacing = 16 };
        string? selected = null;

        void Fill()
        {
            list.Items.Clear();
            if (Doc.Get(field) is { IsObject: true } obj)
                foreach (var m in obj.Members!)
                {
                    var hidden = m.Value.Get("disabled")?.AsBool == true;
                    var name = m.Value.Get(fields[0].Key)?.Str;
                    var item = new ListViewItem
                    {
                        Tag = m.Key,
                        Content = Ui.V(0, Ui.T(m.Key + (hidden ? " (hidden)" : ""), 14, wrap: false),
                            name != null ? Ui.T(name, 12, fg: Ui.Secondary, wrap: false) : null),
                    };
                    list.Items.Add(item);
                    if (m.Key == selected) list.SelectedItem = item;
                }
        }

        void ShowForm(string key)
        {
            form.Children.Clear();
            var path = new[] { field, key };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Ui.T(key, 18, bold: true));
            var actions = Ui.H(8,
                Ui.Btn("Rename...", async () =>
                {
                    var n = await _host.Prompt("Rename", keyLabel, key, "Rename");
                    if (string.IsNullOrWhiteSpace(n) || n.Trim() == key) return;
                    _host.Apply(() => Doc.RenameKey(new[] { field }, key, n.Trim()));
                    selected = n.Trim(); Fill(); ShowForm(selected);
                }),
                Ui.Btn("Delete...", async () =>
                {
                    if (!await _host.Confirm("Delete entry", $"Delete \"{key}\" from {field}? A built-in entry with this ID goes back to its default. To hide it instead, tick Hidden.", "Delete", danger: true)) return;
                    _host.Apply(() => Doc.Remove(new[] { field }, key));
                    selected = null; form.Children.Clear(); Fill();
                }));
            Grid.SetColumn(actions, 1);
            head.Children.Add(actions);
            form.Children.Add(head);

            var entry = Doc.Get(path);
            foreach (var fd in fields)
            {
                var cur = entry?.Get(fd.Key);
                UIElement ctl;
                switch (fd.Kind)
                {
                    case FKind.Bool:
                    {
                        var ts = new ToggleSwitch { IsOn = cur?.AsBool == true, OnContent = "Yes", OffContent = "No" };
                        ts.Toggled += (_, _) =>
                        {
                            bool on = ts.IsOn;
                            _host.Apply(() => { if (on) Doc.Set(path, fd.Key, JsonOut.Bool(true)); else Doc.Remove(path, fd.Key); });
                            if (fd.Key == "disabled") Fill();
                        };
                        ctl = ts;
                        break;
                    }
                    case FKind.Choice:
                    {
                        var cb = new ComboBox { MinWidth = 220 };
                        cb.Items.Add(NotSet);
                        foreach (var o in fd.Options!) cb.Items.Add(o);
                        if (cur?.Str is string s && !fd.Options!.Contains(s)) cb.Items.Add(s);
                        cb.SelectedItem = cur?.Str ?? NotSet;
                        cb.SelectionChanged += (_, _) =>
                        {
                            var v = cb.SelectedItem as string;
                            _host.Apply(() => { if (v == null || v == NotSet) Doc.Remove(path, fd.Key); else Doc.Set(path, fd.Key, JsonOut.Str(v)); });
                        };
                        ctl = cb;
                        break;
                    }
                    case FKind.List:
                    {
                        var text = cur is { IsArray: true } a ? string.Join(", ", a.Items!.Where(i => i.IsString).Select(i => i.Str)) : "";
                        var tb = Ui.Box(text, "comma separated");
                        tb.TextChanged += (_, _) =>
                        {
                            var parts = tb.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                            _host.Defer($"{field}|{key}|{fd.Key}", () =>
                            {
                                if (parts.Length == 0) Doc.Remove(path, fd.Key);
                                else Doc.Set(path, fd.Key, new JsonArray(parts.Select(p => (JsonNode?)JsonOut.Str(p)).ToArray()));
                            });
                        };
                        ctl = tb;
                        break;
                    }
                    default:
                    {
                        var tb = Ui.Box(cur is { IsString: true } ? Ui.ForTextBox(cur.Str!) : cur != null ? Doc.Raw(cur) : "", null, fd.Kind == FKind.Multiline);
                        tb.TextChanged += (_, _) =>
                        {
                            var v = Ui.NormalizeNewlines(tb.Text).Trim();
                            _host.Defer($"{field}|{key}|{fd.Key}", () =>
                            {
                                if (v.Length == 0) Doc.Remove(path, fd.Key);
                                else Doc.Set(path, fd.Key, JsonOut.Str(v));
                            });
                        };
                        ctl = tb;
                        break;
                    }
                }
                form.Children.Add(Ui.Row(fd.Label, fd.Help, ctl, cur != null));
            }
            var others = entry?.Members?.Where(m => fields.All(f => f.Key != m.Key)).Select(m => m.Key).ToList();
            if (others is { Count: > 0 })
                form.Children.Add(Ui.Help($"Other values in this entry (kept as they are; edit under Raw JSON): {string.Join(", ", others)}"));
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListViewItem { Tag: string k } && k != selected)
            {
                selected = k;
                ShowForm(k);
            }
        };

        var addBtn = Ui.Btn("Add...", async () =>
        {
            var n = await _host.Prompt("Add entry", keyLabel, "", "Add");
            if (string.IsNullOrWhiteSpace(n)) return;
            n = n.Trim();
            if (Doc.Get(field, n) == null) _host.Apply(() => Doc.Set(new[] { field }, n, new JsonObject(), JsonDoc.Placement.End));
            selected = n; Fill(); ShowForm(n);
        });

        Fill();
        var left = Ui.V(8, addBtn, list);
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(left);
        var formCard = Ui.Card(form);
        form.Children.Add(Ui.Help("Pick an entry on the left, or add one."));
        Grid.SetColumn(formCard, 1);
        grid.Children.Add(formCard);
        return Ui.V(10, Ui.Help(help), grid);
    }

    private const string NotSet = "(not set)";

    private UIElement PresetsEditor(string field)
    {
        var panel = Ui.V(14, Ui.Help("Each entry adds a preset to a dropdown: the name shown and the preset number the driver uses. Tick Remove to take a built-in preset with that name out of the list."));
        foreach (var (sub, label) in new[] { ("sr", "Super Resolution"), ("rr", "Ray Reconstruction"), ("fg", "Frame Generation"), ("nr", "Neural Rendering") })
        {
            var rows = new StackPanel { Spacing = 6 };
            var items = new List<(TextBox Name, TextBox Value, CheckBox Disabled)>();
            void Commit()
            {
                var arr = new JsonArray();
                foreach (var (n, v, d) in items)
                {
                    if (n.Text.Trim().Length == 0) continue;
                    var o = new JsonObject { ["name"] = JsonOut.Str(n.Text.Trim()), ["value"] = JsonOut.Num(long.TryParse(v.Text.Trim(), out var x) ? x : 0) };
                    if (d.IsChecked == true) o["disabled"] = JsonOut.Bool(true);
                    arr.Add(o);
                }
                _host.Defer($"{field}|{sub}", () =>
                {
                    if (arr.Count == 0) Doc.Remove(new[] { field }, sub);
                    else Doc.Set(new[] { field }, sub, arr.DeepClone());
                });
            }
            void AddRow(string name, string value, bool disabled)
            {
                var nb = Ui.Box(name, "Name shown, e.g. K - TF2");
                var vb = Ui.Box(value, "Number");
                vb.Width = 110;
                var db = new CheckBox { Content = "Remove", IsChecked = disabled, MinWidth = 0 };
                var g = new Grid { ColumnSpacing = 8 };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(nb);
                Grid.SetColumn(vb, 1); g.Children.Add(vb);
                Grid.SetColumn(db, 2); g.Children.Add(db);
                var item = (nb, vb, db);
                var rm = Ui.RemoveBtn(() => { items.Remove(item); rows.Children.Remove(g); Commit(); });
                Grid.SetColumn(rm, 3); g.Children.Add(rm);
                items.Add(item);
                rows.Children.Add(g);
                nb.TextChanged += (_, _) => Commit();
                vb.TextChanged += (_, _) => Commit();
                db.Click += (_, _) => Commit();
            }
            if (Doc.Get(field, sub) is { IsArray: true } arr)
                foreach (var it in arr.Items!.Where(i => i.IsObject))
                    AddRow(it.Get("name")?.Str ?? "", it.Get("value") is JNode v ? Doc.Raw(v) : "", it.Get("disabled")?.AsBool == true);
            var add = Ui.Btn("Add preset", () => AddRow("", "", false));
            add.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(Ui.Card(Ui.V(10, Ui.T(label, 16, bold: true), rows, add)));
        }
        return panel;
    }

    private UIElement RawEditor()
    {
        var combo = new ComboBox { MinWidth = 320 };
        foreach (var m in Doc.Root.Members!.Where(m => !m.Key.StartsWith('_')))
            combo.Items.Add(m.Key);
        var box = Ui.Mono("", 420);
        var status = Ui.T("", 13);
        var apply = Ui.Btn("Apply", () => { }, accent: true);
        apply.IsEnabled = false;
        string? field = null;
        combo.SelectionChanged += (_, _) =>
        {
            field = combo.SelectedItem as string;
            if (field == null) return;
            box.Text = Ui.ForTextBox(Doc.Get(field) is JNode n ? Doc.Raw(n) : "");
            status.Text = "";
            apply.IsEnabled = true;
        };
        apply.Click += (_, _) =>
        {
            if (field == null) return;
            var text = Ui.NormalizeNewlines(box.Text).Replace("\n", Doc.NL);
            try
            {
                JsonParser.Parse(text);
            }
            catch (JsonParseException ex)
            {
                var (line, col) = JsonParser.LineCol(text, ex.Position);
                status.Text = $"Not valid JSON: {ex.Message} (line {line}, column {col}). Nothing was changed.";
                status.Foreground = Ui.Critical;
                return;
            }
            var f = field;
            _host.Apply(() => Doc.ReplaceRaw(new[] { f }, text));
            status.Text = "Applied. Press Save to write the file.";
            status.Foreground = Ui.Success;
        };
        return Ui.Card(Ui.V(10,
            Ui.Help("Pick a field to see it exactly as it is in the file. Changes are checked before they're applied."),
            Ui.H(8, combo, apply), status, box));
    }
}
