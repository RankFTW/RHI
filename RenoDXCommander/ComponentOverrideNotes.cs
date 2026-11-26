// ComponentOverrideNotes.cs — a component's per-game overrides as short notes ("d3d12.dll", "not in Update All"),
// shown where the component is (Game Overrides was removed in 2.9.6.3; user decision Nov 2026): Tabs / Spotlight Overview
// cards (appended to the card line), Spotlight's What's installed tiles (after the version), the Classic+ Components
// header summary (in brackets after the version), and OptiScaler's Extras row and summary. One source, so every view
// says the same. The game-wide ones (Graphics API override, launch arguments) are header tags (UpdateGraphicsApiBadges).

using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

internal static class ComponentOverrideNotes
{
    /// <summary>
    /// The overrides set for one component in this game. <paramref name="key"/>: the component row key ("Rs", "Rdx",
    /// "Luma", "Ul", "Dc") or "Os". <paramref name="rsModes"/>: include ReShade's channel / shaders / addons (left out
    /// where the card line already says them).
    /// </summary>
    internal static List<string> For(MainViewModel vm, GameCardViewModel card, string key, bool rsModes = true)
    {
        var notes = new List<string>();
        var name = card.GameName;
        var store = card.Source ?? "";
        var dll = vm.HasDllOverride(name) ? vm.GetDllOverride(name) : null;
        static bool Set(string? file) => !string.IsNullOrEmpty(file) && file != "--------";

        switch (key)
        {
            case "Rs":
                if (rsModes)
                {
                    if (vm.GetReShadeChannelOverride(name, store) is { Length: > 0 } channel) notes.Add($"{channel} channel");
                    var shaders = vm.GetPerGameShaderMode(name, store);
                    if (!string.IsNullOrEmpty(shaders) && shaders != "Global") notes.Add($"{shaders} shaders");
                    var addons = vm.GetPerGameAddonMode(name, store);
                    if (!string.IsNullOrEmpty(addons) && addons != "Global") notes.Add($"{addons} addons");
                }
                if (Set(dll?.ReShadeFileName)) notes.Add(dll!.ReShadeFileName!);
                if (vm.IsUpdateAllExcludedReShade(name, store)) notes.Add("not in Update All");
                break;
            case "Rdx":
                if (vm.IsUpdateAllExcludedRenoDx(name, store)) notes.Add("not in Update All");
                break;
            case "Ul":
                if (vm.IsUpdateAllExcludedUl(name, store)) notes.Add("not in Update All");
                break;
            case "Dc":
                if (Set(dll?.DcFileName)) notes.Add(dll!.DcFileName!);
                if (vm.IsUpdateAllExcludedDc(name, store)) notes.Add("not in Update All");
                break;
            case "Os":
                if (Set(dll?.OsFileName)) notes.Add(dll!.OsFileName!);
                if (vm.IsUpdateAllExcludedOs(name, store)) notes.Add("not in Update All");
                break;
        }
        return notes;
    }

    /// <summary>The notes joined with " · ", or null when there are none.</summary>
    internal static string? Line(MainViewModel vm, GameCardViewModel card, string key, bool rsModes = true)
    {
        var notes = For(vm, card, key, rsModes);
        return notes.Count > 0 ? string.Join(" · ", notes) : null;
    }
}
