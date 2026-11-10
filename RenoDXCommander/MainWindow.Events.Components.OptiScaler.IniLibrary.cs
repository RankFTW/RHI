// MainWindow.Events.Components.OptiScaler.IniLibrary.cs — "Save ini..." and "Load saved ini..." in the
// OptiScaler cog's footer. Saved inis live in OsIniLibrary (any number, named by the user); loading one
// makes it the game's OptiScaler.ini and remembers it, so a reinstall or update puts it back.
// Both use flyouts: a second dialog can't open over the cog.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>Shown in the cog's status line after it reopens (e.g. after loading a saved ini).</summary>
    private string? _osCogNotice;

    private Button BuildOsSaveIniButton(OsCogContext ctx, string? iniPath, Action<string, bool> showStatus)
    {
        var card = ctx.Card;
        var btn = CogDialog.StyledButton("Save ini...", CogButtonKind.Secondary);
        ToolTipService.SetToolTip(btn,
            "Save this game's OptiScaler.ini under a name of your choice. Load it into any game with Load saved ini.");

        var nameBox = CogDialog.MakeTextBox("", "Name, for example F5 super ini");
        nameBox.MaxLength = OsIniLibrary.MaxNameLength;
        var note = MakeOsFooterNote("");
        var saveBtn = CogDialog.StyledButton("Save", CogButtonKind.Primary, compact: true);
        var panel = new StackPanel { Spacing = 8, Width = 300 };
        panel.Children.Add(new TextBlock
        {
            Text = "Save this game's OptiScaler.ini as:",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(note);
        panel.Children.Add(saveBtn);
        var flyout = new Flyout { Content = panel };
        btn.Flyout = flyout;

        void ShowNote(string text, bool error)
        {
            note.Text = text;
            note.Foreground = UIFactory.Brush(error ? ResourceKeys.AccentRedBrush : ResourceKeys.TextTertiaryBrush);
        }

        flyout.Opening += (_, _) =>
        {
            nameBox.Text = ViewModel.GetOsIniProfile(card.GameName, ctx.Store) ?? "";
            ShowNote("", false);
        };
        nameBox.TextChanged += (_, _) =>
        {
            var name = OsIniLibrary.CleanName(nameBox.Text);
            ShowNote(name != null && OsIniLibrary.Exists(name) ? $"Replaces your saved ini \"{name}\"." : "", false);
        };

        async Task SaveAsync()
        {
            try
            {
                var name = OsIniLibrary.CleanName(nameBox.Text);
                if (name == null) { ShowNote("Type a name first.", true); return; }
                if (iniPath == null || !File.Exists(iniPath)) { ShowNote("This game has no OptiScaler.ini to save.", true); return; }
                bool replaced = OsIniLibrary.Exists(name);
                await Task.Run(() => OsIniLibrary.Save(name, iniPath));
                // The game now matches the saved ini, so a reinstall puts it back
                ViewModel.SetOsIniProfile(card.GameName, name, ctx.Store);
                flyout.Hide();
                showStatus(replaced ? $"Saved ini \"{name}\" replaced." : $"Saved as \"{name}\". Load it into any game with Load saved ini.", true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[MainWindow.BuildOsSaveIniButton] Save failed — {ex.Message}");
                ShowNote($"Couldn't save: {ex.Message}", true);
            }
        }

        saveBtn.Click += async (_, _) => await SaveAsync();
        nameBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            await SaveAsync();
        };
        return btn;
    }

    private Button BuildOsLoadIniButton(OsCogContext ctx, Action<string, bool> showStatus)
    {
        var card = ctx.Card;
        var store = ctx.Store;
        var btn = CogDialog.StyledButton("Load saved ini...", CogButtonKind.Secondary);
        ToolTipService.SetToolTip(btn,
            "Use one of your saved inis as this game's OptiScaler.ini. RHI remembers it and puts it back after a reinstall or update.");

        var panel = new StackPanel { Spacing = 8, Width = 320 };
        var flyout = new Flyout { Content = panel };
        btn.Flyout = flyout;

        void Fill()
        {
            panel.Children.Clear();
            var names = OsIniLibrary.List();
            var note = MakeOsFooterNote("");

            void ShowNote(string text, bool error)
            {
                note.Text = text;
                note.Foreground = UIFactory.Brush(error ? ResourceKeys.AccentRedBrush : ResourceKeys.TextTertiaryBrush);
            }

            var addBtn = CogDialog.StyledButton("Add from file...", CogButtonKind.Secondary, compact: true);
            ToolTipService.SetToolTip(addBtn, "Add an OptiScaler.ini from anywhere on your PC (a build's own ini, one shared on Discord, ...) to your saved inis.");
            addBtn.Click += async (_, _) =>
            {
                try
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    var file = await Task.Run(() =>
                    {
                        var ofn = new NativeInterop.OpenFileName();
                        ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
                        ofn.hwndOwner = hwnd;
                        ofn.filter = "INI files (*.ini)\0*.ini\0All Files (*.*)\0*.*\0";
                        ofn.file = new string(new char[260]);
                        ofn.maxFile = ofn.file.Length;
                        ofn.title = "Add an OptiScaler.ini";
                        ofn.flags = 0x00080000 | 0x00001000; // OFN_EXPLORER | OFN_FILEMUSTEXIST
                        return NativeInterop.GetOpenFileName(ref ofn) ? ofn.file.TrimEnd('\0') : null;
                    });
                    if (string.IsNullOrEmpty(file)) return;
                    var name = OsIniLibrary.SuggestName(file);
                    await Task.Run(() => OsIniLibrary.Save(name, file));
                    Fill();
                    flyout.ShowAt(btn);
                    showStatus($"Added \"{name}\" to your saved inis.", true);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[MainWindow.BuildOsLoadIniButton] Add from file failed — {ex.Message}");
                    ShowNote($"Couldn't add the file: {ex.Message}", true);
                }
            };

            var folderBtn = CogDialog.StyledButton("Open folder", CogButtonKind.Secondary, compact: true);
            ToolTipService.SetToolTip(folderBtn, "Open the folder your saved inis are kept in. You can rename or copy them there too.");
            folderBtn.Click += async (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(OsIniLibrary.Dir);
                    await Windows.System.Launcher.LaunchFolderPathAsync(OsIniLibrary.Dir);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[MainWindow.BuildOsLoadIniButton] Open folder failed — {ex.Message}");
                    ShowNote($"Couldn't open the folder: {ex.Message}", true);
                }
            };

            if (names.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "No saved inis yet. Save one from a game with Save ini, or add one from a file.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                });
                var emptyRow = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
                emptyRow.Children.Add(addBtn);
                emptyRow.Children.Add(folderBtn);
                panel.Children.Add(emptyRow);
                panel.Children.Add(note);
                return;
            }

            var current = ViewModel.GetOsIniProfile(card.GameName, store);
            var combo = CogDialog.MakeCombo(names, Math.Max(0, names.FindIndex(n => n.Equals(current, StringComparison.OrdinalIgnoreCase))));
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;

            var useBtn = CogDialog.StyledButton("Use for this game", CogButtonKind.Primary, compact: true);
            useBtn.Click += async (_, _) =>
            {
                if (combo.SelectedItem is not string name || string.IsNullOrEmpty(card.InstallPath)) return;
                var gameDir = card.InstallPath;
                var err = await Task.Run(() => OsIniLibrary.Deploy(name, gameDir, ViewModel.Settings.OsHotkey));
                if (err != null) { ShowNote($"Couldn't load it: {err}", true); return; }

                ViewModel.SetOsIniProfile(card.GameName, name, store);
                // Edits applied to the old ini don't belong to this one
                ViewModel.SetOsIniEdits(card.GameName, null, store);
                SyncOsFgSettingsFromIni(card.GameName, store, gameDir);
                CrashReporter.Log($"[MainWindow.BuildOsLoadIniButton] '{card.GameName}' now uses saved ini '{name}'");
                flyout.Hide();
                _osCogNotice = $"Loaded \"{name}\" as this game's OptiScaler.ini. It's put back after a reinstall or update.";
                ReopenOsCog(ctx); // every setting above now reads the new ini
            };

            // Deleting asks for a second click
            var deleteBtn = CogDialog.StyledButton("Delete", CogButtonKind.Danger, compact: true);
            bool armed = false;
            deleteBtn.Click += async (_, _) =>
            {
                if (combo.SelectedItem is not string name) return;
                if (!armed)
                {
                    armed = true;
                    deleteBtn.Content = "Click again to delete";
                    ShowNote($"Delete \"{name}\" from your saved inis? Games already using it keep their OptiScaler.ini.", false);
                    return;
                }
                try
                {
                    await Task.Run(() => OsIniLibrary.Delete(name));
                    if (name.Equals(ViewModel.GetOsIniProfile(card.GameName, store), StringComparison.OrdinalIgnoreCase))
                        ViewModel.SetOsIniProfile(card.GameName, null, store);
                    showStatus($"Deleted saved ini \"{name}\".", true);
                    Fill();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[MainWindow.BuildOsLoadIniButton] Delete failed — {ex.Message}");
                    ShowNote($"Couldn't delete it: {ex.Message}", true);
                }
            };
            combo.SelectionChanged += (_, _) =>
            {
                armed = false;
                deleteBtn.Content = "Delete";
                ShowNote("", false);
            };

            panel.Children.Add(new TextBlock
            {
                Text = "Use a saved ini as this game's OptiScaler.ini. The current one is backed up first.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            });
            panel.Children.Add(combo);
            var row = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            row.Children.Add(useBtn);
            row.Children.Add(deleteBtn);
            row.Children.Add(addBtn);
            row.Children.Add(folderBtn);
            panel.Children.Add(row);
            panel.Children.Add(note);
        }

        flyout.Opening += (_, _) => Fill();
        return btn;
    }

    /// <summary>The Frame Generation dropdowns keep their own saved values; match them to the game's ini.</summary>
    private void SyncOsFgSettingsFromIni(string gameName, string store, string gameDir)
    {
        try
        {
            var settings = OsIniEditor.ParseFile(Path.Combine(gameDir, OptiScalerService.IniFileName));
            string? Val(string key) => settings.FirstOrDefault(s =>
                s.Section.Equals("FrameGen", StringComparison.OrdinalIgnoreCase) && s.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
            ViewModel.SetOsFgInput(gameName, string.IsNullOrEmpty(Val("FGInput")) ? "auto" : Val("FGInput")!, store);
            ViewModel.SetOsFgOutput(gameName, string.IsNullOrEmpty(Val("FGOutput")) ? "auto" : Val("FGOutput")!, store);
            var nvngx = Val("FGNvngxReplacement");
            ViewModel.SetOsFgNvngxReplacement(gameName,
                string.IsNullOrEmpty(nvngx) || nvngx.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "None" : nvngx, store);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[MainWindow.SyncOsFgSettingsFromIni] Failed — {ex.Message}");
        }
    }
}
