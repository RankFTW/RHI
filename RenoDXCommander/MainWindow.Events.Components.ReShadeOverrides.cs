// MainWindow.Events.Components.ReShadeOverrides.cs — the ReShade ⚙'s Setup and Shaders and addons sections (and Updates).
// The same per-game settings as Game Overrides (Graphics API, ReShade Channel, Shaders, Addons, ReShade DLL name, Select
// ReShade Preset), kept in both places so people find them either way. Both call the shared methods in
// DetailPanelBuilder.Overrides.RsShared.cs (Graphics API: DetailPanelBuilder.Overrides.ApiShared.cs).

using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>
    /// Adds the Setup and Shaders and addons sections. Picks that open a dialog (pickers, the Vulkan confirmation) can't open over the
    /// cog when it is itself a dialog (Classic layout), so they go through <paramref name="closeThenRun"/>: the cog closes,
    /// the action runs, and the cog opens again. In Tabs / Spotlight the cog isn't a dialog and they run in place.
    /// Returns the ReShade DLL name dropdown (null when not shown) so "Load ReShade as ASI" can grey it out.
    /// </summary>
    private ComboBox? AddRsGameOverridesSection(CogDialog cog, GameCardViewModel card, Action<Func<Task>> closeThenRun,
        Action<ComboBox?> addLoadingRows)
    {
        var dp = _detailPanelBuilder;
        if (dp == null) return null;
        var gameName = card.GameName;

        // ── Setup: which ReShade, for which API, under which name ──
        cog.AddSection("Setup");
        cog.AddNote("The same settings as in this game's Game Overrides section. A change in one place shows in the other.");

        ComboBox? addonCombo = null;

        AddSharedOverrideCombo(cog, "Graphics API",
            "The graphics API RHI treats this game as using. Auto uses the detected one. Switching between DirectX and Vulkan "
            + "removes the ReShade install of the other kind (install ReShade again afterwards). Reset Overrides goes back to Auto.",
            () => (DetailPanelBuilder.ApiItems, dp.GetApiDisplay(card)),
            _ => false,
            sel =>
            {
                bool removed = dp.ApplyApiOverride(card, gameName, sel, rebuildNow: false);
                cog.SetSuccess(removed
                    ? $"Graphics API set to {sel}. ReShade was removed because the game moved between DirectX and Vulkan: install it again."
                    : $"Graphics API set to {sel}.");
                return Task.FromResult(true);
            },
            reopenItem: null, closeThenRun: closeThenRun);

        AddSharedOverrideCombo(cog, "ReShade channel",
            "The ReShade build for this game. Vulkan games share one ReShade, so a change applies to every Vulkan game.",
            () => { var (items, sel) = dp.GetRsChannelChoices(card); return (items, sel); },
            sel => DetailPanelBuilder.RsChannelPickNeedsDialog(card, sel),
            async sel =>
            {
                bool ok = await dp.ApplyRsChannelAsync(card, gameName, sel);
                if (addonCombo != null) addonCombo.IsEnabled = !card.UseNormalReShade;
                return ok;
            },
            reopenItem: "Custom", closeThenRun: closeThenRun);

        // Vulkan games load ReShade as a layer, not a DLL in the game folder, so there's no name to change
        ComboBox? dllCombo = null;
        if (!card.RequiresVulkanInstall)
        {
            dllCombo = AddSharedOverrideCombo(cog, "ReShade DLL name",
                "Renames ReShade's DLL in this game's folder. -------- uses the default name. Names used by Display Commander or OptiScaler aren't listed. Greyed out while Load ReShade as ASI is on.",
                () => { var (items, sel) = dp.GetRsDllNameChoices(card); return (items, sel); },
                _ => false,
                sel =>
                {
                    bool ok = dp.ApplyRsDllName(card, gameName, sel);
                    if (ok)
                    {
                        _detailPanelBuilder?.UpdateDetailComponentRows(card);
                        dp.QueueOverridesRebuild(gameName);
                    }
                    else cog.SetError("That name is used by OptiScaler in this game.");
                    return Task.FromResult(ok);
                },
                reopenItem: null, closeThenRun: closeThenRun,
                enabled: () => !ViewModel.GetRsAsiMode(gameName, card.Source ?? ""));
        }
        addLoadingRows(dllCombo); // Load ReShade as ASI, right under the DLL name it replaces

        // ── Shaders and addons: what ReShade loads ──
        cog.AddSection("Shaders and addons");
        var shaderCombo = AddSharedOverrideCombo(cog, "Shaders",
            "Global uses the shaders chosen in Settings, Custom your own shader folders, Select picks packs for this game, Off installs none.",
            () => (DetailPanelBuilder.ShaderModeItems, dp.GetShaderModeDisplay(card)),
            sel => sel == "Select",
            async sel =>
            {
                bool ok = await dp.ApplyShaderModeAsync(card, gameName, sel);
                if (ok) dp.QueueOverridesRebuild(gameName);
                return ok;
            },
            reopenItem: "Select", closeThenRun: closeThenRun);

        addonCombo = AddSharedOverrideCombo(cog, "Addons",
            "Global uses the addons chosen in Settings, Select picks addons for this game, Off installs none. Needs the addon build of ReShade (any channel except No Addons).",
            () => (DetailPanelBuilder.AddonModeItems, dp.GetAddonModeDisplay(card)),
            sel => sel == "Select",
            async sel =>
            {
                bool ok = await dp.ApplyAddonModeAsync(card, gameName, sel);
                if (ok) dp.QueueOverridesRebuild(gameName);
                return ok;
            },
            reopenItem: "Select", closeThenRun: closeThenRun,
            enabled: () => !card.UseNormalReShade);

        var presetBtn = cog.MakeButton("Select ReShade Preset", CogButtonKind.Secondary, async () =>
        {
            if (!cog.IsEmbedded) { closeThenRun(() => dp.PickReShadePresetAsync(card, gameName)); return; }
            if (await dp.PickReShadePresetAsync(card, gameName))
            {
                shaderCombo.SelectedItem = dp.GetShaderModeDisplay(card);
                cog.SetSuccess("Presets and their shaders installed.");
            }
        }, "Pick .ini preset files to copy to this game's folder. Place presets in the reshade-presets folder.");
        cog.AddButtons(presetBtn);

        return dllCombo;
    }

    /// <summary>
    /// The ReShade ⚙'s Updates section: whether Update All updates ReShade in this game (the same choice as the ReShade
    /// tick in Game Overrides' Update inclusion; Vulkan games share it), and whether RHI keeps its reshade.ini updated.
    /// </summary>
    private void AddRsUpdatesSection(CogDialog cog, GameCardViewModel card)
    {
        var gameName = card.GameName;
        var store = card.Source ?? "";
        AddUpdateAllSection(cog, card, UpdateAllComponent.ReShade);

        cog.AddCombo("Keep ReShade.ini updated",
            "No stops RHI from changing this game's reshade.ini when ReShade is installed or updated, or when you use the Settings buttons that write to all games.",
            new[] { "Yes", "No" },
            ViewModel.GetKeepRsIniUpdated(gameName, store) ? 0 : 1,
            i =>
            {
                ViewModel.SetKeepRsIniUpdated(gameName, i == 0, store);
                cog.SetSuccess(i == 0
                    ? "RHI will keep this game's reshade.ini updated."
                    : "RHI will leave this game's reshade.ini alone.");
            });
    }

    /// <summary>
    /// A cog dropdown whose change runs <paramref name="apply"/> (may open dialogs, may be cancelled). Afterwards the
    /// dropdown re-reads the saved value, so a cancelled pick goes back by itself. Picking <paramref name="reopenItem"/>
    /// while it's already the saved value runs it again (reopens its picker), like the Game Overrides dropdowns.
    /// </summary>
    private static ComboBox AddSharedOverrideCombo(CogDialog cog, string label, string help,
        Func<(IList<string> Items, string Selected)> read,
        Func<string, bool> needsDialog,
        Func<string, Task<bool>> apply,
        string? reopenItem,
        Action<Func<Task>> closeThenRun,
        Func<bool>? enabled = null)
    {
        var combo = MakeSharedOverrideCombo(cog, label, read, needsDialog, apply, reopenItem, closeThenRun, enabled);
        cog.AddRow(label, combo, help);
        return combo;
    }

    /// <summary>The dropdown of <see cref="AddSharedOverrideCombo"/>, not added anywhere (for compact grids).</summary>
    private static ComboBox MakeSharedOverrideCombo(CogDialog cog, string label,
        Func<(IList<string> Items, string Selected)> read,
        Func<string, bool> needsDialog,
        Func<string, Task<bool>> apply,
        string? reopenItem,
        Action<Func<Task>> closeThenRun,
        Func<bool>? enabled = null)
    {
        var (items, selected) = read();
        var combo = CogDialog.MakeCombo(items, items.IndexOf(selected), enabled?.Invoke() ?? true);
        bool quiet = false, busy = false;

        void Reload()
        {
            var (newItems, newSel) = read();
            quiet = true;
            combo.ItemsSource = newItems;
            combo.SelectedIndex = newItems.IndexOf(newSel);
            quiet = false;
            combo.IsEnabled = enabled?.Invoke() ?? true;
        }

        async Task PickAsync(string picked)
        {
            busy = true;
            if (!cog.IsEmbedded && needsDialog(picked))
            {
                closeThenRun(() => apply(picked)); // the cog closes; it opens again once this has run
                return;
            }
            combo.IsEnabled = false;
            try
            {
                cog.ClearStatus();
                if (await apply(picked))
                    cog.SetSuccess($"{label}: {(read().Selected)}.");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MainWindow.RsCogButton_Click] '{label}' change failed — {ex}");
                cog.SetError($"Couldn't change {label}: {ex.Message}");
            }
            finally
            {
                Reload();
                busy = false;
            }
        }

        combo.SelectionChanged += async (_, _) =>
        {
            if (quiet || busy || combo.SelectedItem is not string picked) return;
            await PickAsync(picked);
        };
        combo.DropDownClosed += async (_, _) =>
        {
            if (quiet || busy || reopenItem == null) return;
            if (combo.SelectedItem as string == reopenItem && read().Selected == reopenItem)
                await PickAsync(reopenItem);
        };

        return combo;
    }
}
