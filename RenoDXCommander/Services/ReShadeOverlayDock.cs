// ReShadeOverlayDock.cs — which side of the screen ReShade's menu is docked on (Settings › ReShade ›
// ReShade menu position). Pure text edits of reshade.ini, unit tested (ReShadeOverlayDockTests).
//
// ReShade keeps its menu layout in reshade.ini [OVERLAY] as two keys written by Dear ImGui's settings
// handlers (ReShade source, runtime_gui.cpp load_config_gui / save_config_gui). Each key is a list of
// lines joined with ',' where a real comma is written ",,":
//   Docking=[Docking][Data],DockSpace   ID=0xB0DF600F Pos=8,,8 Size=3424,,1424 Split=X,
//             DockNode  ID=0x00000001 Parent=0xB0DF600F SizeRef=1203,,1440 Selected=0x495DD1F4,
//             DockNode  ID=0x00000002 Parent=0xB0DF600F SizeRef=2235,,1440 CentralNode=1
//   Window=[Window][###home],Pos=8,,8,Size=1203,,1424,Collapsed=0,DockId=0x00000001,,0,[Window][###addons],...
// The dock space is split left / right: the first child is the left side. One child holds the menu's
// windows (Home, Add-ons, Settings... as tabs) and the other is the "central node", the empty part the
// game shows through. Menu on the left = windows node first (ReShade's default); on the right = central
// node first (seen in a real file after dragging the menu across).
//
// ReShade only builds its default (left) layout when the file has no dock space yet, and docks the
// windows in the same step. So for a file with no layout, "Right" writes the layout and the windows'
// dock ids itself, the way ReShade's own default does.
namespace RenoDXCommander.Services;

public static class ReShadeOverlayDock
{
    public const string Default = "Default"; // leave every game's layout alone
    public const string Left = "Left";
    public const string Right = "Right";

    public static string Normalize(string? side) =>
        string.Equals(side, Left, StringComparison.OrdinalIgnoreCase) ? Left
        : string.Equals(side, Right, StringComparison.OrdinalIgnoreCase) ? Right
        : Default;

    // ReShade's dock space id is ImGui's hash of "ViewportDockspace" inside its "Viewport" window: the
    // same in every game (checked across ~60 real reshade.ini files).
    private const string RootId = "0xB0DF600F";
    private const string WindowsNode = "0x00000001";
    private const string CentralNode = "0x00000002";

    // A root dock space needs Pos and Size or ImGui ignores the line; ReShade resizes it to the
    // screen every frame and the two halves keep their proportion (35% menu, as ReShade's default).
    private static readonly string[] FreshRightLayout =
    [
        "[Docking][Data]",
        $"DockSpace   ID={RootId} Pos=8,,8 Size=1904,,1064 Split=X",
        $"  DockNode  ID={CentralNode} Parent={RootId} SizeRef=1237,,1064 CentralNode=1",
        $"  DockNode  ID={WindowsNode} Parent={RootId} SizeRef=667,,1064",
    ];

    /// <summary>ReShade's own windows, in its tab order.</summary>
    private static readonly string[] MenuWindows = ["###home", "###addons", "###settings", "###statistics", "###log", "###about"];

    /// <summary>
    /// Windows of add-ons RHI installs (their titles as ReShade saves them). ReShade's default layout
    /// docks every loaded add-on's window with the menu; a layout RHI writes has to name them, or they
    /// would open as loose windows. Names that never appear in a game are simply unused.
    /// </summary>
    private static readonly string[] AddonWindows =
    [
        "RenoDX", "ReLimiter", "DC", "RenoDX DLSS", "RenoDX-DLSSNR", "RenoDX Upgrade", "RenoDX DevKit",
        "RenoDX Pipeline Insert", "MFG Unlock", "HSF", "GITC Uplift", "Luma", "DLSS 5 Neural Rendering",
        "DLSS 5 Bridge", "CRCGI Injector", "XPAR", "Discord HDR Fix",
    ];

    /// <summary>
    /// Returns <paramref name="text"/> with ReShade's menu docked on <paramref name="side"/>, or unchanged when
    /// there is nothing to do: side is Default; the layout is already that way; the file has no layout and the
    /// side is Left (ReShade's default); or the layout isn't the plain left / right split (the menu was undocked
    /// or arranged another way by the user, which is left as it is).
    /// </summary>
    public static string SetSideInText(string text, string? side)
    {
        side = Normalize(side);
        if (side == Default || string.IsNullOrEmpty(text)) return text;

        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

        int section = lines.FindIndex(l => l.Trim().Equals("[OVERLAY]", StringComparison.OrdinalIgnoreCase));
        int dockLine = FindKey(lines, section, "Docking");
        var entries = dockLine >= 0 ? SplitList(ValueOf(lines[dockLine])) : new List<string>();
        bool hasNodes = entries.Any(e => IsNode(e));

        if (!hasNodes)
        {
            // No layout yet (new file, or ReShade's menu was never opened in this game)
            if (side == Left) return text;
            if (section < 0)
            {
                // Keep the file's shape: a new section at the end
                if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                if (lines.Count > 0 && lines[^1].Length > 0) lines.Add("");
                lines.Add("[OVERLAY]");
                section = lines.Count - 1;
                lines.Add("");
            }
            var docking = "Docking=" + string.Join(",", FreshRightLayout);
            if (dockLine >= 0) lines[dockLine] = docking;
            else lines.Insert(section + 1, docking);

            int windowLine = FindKey(lines, section, "Window");
            var windows = windowLine >= 0 ? SplitList(ValueOf(lines[windowLine])) : new List<string>();
            AddMissingWindows(windows);
            var windowText = "Window=" + string.Join(",", windows);
            if (windowLine >= 0) lines[windowLine] = windowText;
            else lines.Insert(FindKey(lines, section, "Docking") + 1, windowText);
            return string.Join(nl, lines);
        }

        // An existing layout: only the plain two-way split is turned round
        int root = entries.FindIndex(e => e.TrimStart().StartsWith("DockSpace", StringComparison.Ordinal)
                                       && HasToken(e, "Split=X"));
        if (root < 0) return text;
        var rootId = TokenValue(entries[root], "ID=");
        if (rootId == null) return text;
        int rootIndent = Indent(entries[root]);

        // The root's subtree = the following, more indented entries
        int end = root + 1;
        while (end < entries.Count && Indent(entries[end]) > rootIndent) end++;

        // Its two halves: each starts at an entry whose parent is the root and runs to the next one
        var starts = new List<int>();
        for (int i = root + 1; i < end; i++)
            if (string.Equals(TokenValue(entries[i], "Parent="), rootId, StringComparison.OrdinalIgnoreCase))
                starts.Add(i);
        if (starts.Count != 2 || starts[0] != root + 1) return text;

        var first = entries.GetRange(starts[0], starts[1] - starts[0]);
        var second = entries.GetRange(starts[1], end - starts[1]);
        bool firstIsCentral = first.Any(e => HasToken(e, "CentralNode=1"));
        bool secondIsCentral = second.Any(e => HasToken(e, "CentralNode=1"));
        if (firstIsCentral == secondIsCentral) return text; // not a menu half + a game half

        bool menuIsLeft = secondIsCentral;
        if ((side == Left) == menuIsLeft) return text; // already there

        var rebuilt = entries.GetRange(0, root + 1);
        rebuilt.AddRange(second);
        rebuilt.AddRange(first);
        rebuilt.AddRange(entries.GetRange(end, entries.Count - end));
        lines[dockLine] = "Docking=" + string.Join(",", rebuilt);
        return string.Join(nl, lines);
    }

    private static void AddMissingWindows(List<string> windows)
    {
        bool Has(string name) => windows.Any(w => w.Equals($"[Window][{name}]", StringComparison.Ordinal));
        int order = 0;
        foreach (var name in MenuWindows.Concat(AddonWindows))
        {
            if (!Has(name))
            {
                windows.Add($"[Window][{name}]");
                windows.Add("Collapsed=0");
                windows.Add($"DockId={WindowsNode},,{order}");
            }
            order++;
        }
        // The shader editor goes in the other half, as in ReShade's default
        if (!Has("###editor"))
        {
            windows.Add("[Window][###editor]");
            windows.Add("Collapsed=0");
            windows.Add($"DockId={CentralNode}");
        }
    }

    // ── ini helpers ──────────────────────────────────────────────────────────

    private static int FindKey(List<string> lines, int section, string key)
    {
        if (section < 0) return -1;
        for (int i = section + 1; i < lines.Count && !lines[i].TrimStart().StartsWith('['); i++)
        {
            var t = lines[i];
            int eq = t.IndexOf('=');
            if (eq > 0 && t[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static string ValueOf(string line) => line[(line.IndexOf('=') + 1)..];

    /// <summary>Splits a ReShade list value on single commas; ",," is a real comma and stays in the item as written.</summary>
    internal static List<string> SplitList(string value)
    {
        var items = new List<string>();
        var current = new System.Text.StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == ',')
            {
                if (i + 1 < value.Length && value[i + 1] == ',') { current.Append(",,"); i++; }
                else { items.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(value[i]);
        }
        items.Add(current.ToString());
        return items;
    }

    private static bool IsNode(string entry)
    {
        var t = entry.TrimStart();
        return t.StartsWith("DockSpace", StringComparison.Ordinal) || t.StartsWith("DockNode", StringComparison.Ordinal);
    }

    private static int Indent(string entry) => entry.Length - entry.TrimStart().Length;

    private static bool HasToken(string entry, string token) =>
        entry.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(t => t.Equals(token, StringComparison.Ordinal));

    private static string? TokenValue(string entry, string prefix) =>
        entry.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(t => t.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
}
