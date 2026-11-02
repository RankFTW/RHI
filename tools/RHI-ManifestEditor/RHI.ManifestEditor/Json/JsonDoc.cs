using System.Text.Json.Nodes;

namespace RHI.ManifestEditor.Json;

/// <summary>
/// The manifest as text plus a position-aware parse tree. Every edit splices only the
/// characters it changes, so saving never reformats, re-sorts or re-escapes the rest of
/// the file (the old editor re-serialised everything). Each edit is re-parsed straight
/// away, so a bad edit throws instead of producing broken JSON.
/// </summary>
public sealed class JsonDoc
{
    public string Text { get; private set; }
    public JNode Root { get; private set; }
    public string NL { get; }
    public string SavedText { get; private set; }
    public bool IsDirty => Text != SavedText;

    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private int _batch;

    /// <summary>Raised after every change (a batch raises it once).</summary>
    public event Action? Changed;

    public JsonDoc(string text)
    {
        Root = JsonParser.Parse(text);
        Text = text;
        SavedText = text;
        NL = text.Contains("\r\n") ? "\r\n" : "\n";
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void MarkSaved() => SavedText = Text;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Text);
        SetTextInternal(_undo.Pop());
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Text);
        SetTextInternal(_redo.Pop());
        Changed?.Invoke();
    }

    /// <summary>Replaces the whole text (raw edit, discard changes). Undoable.</summary>
    public void ReplaceAll(string text)
    {
        if (text == Text) return;
        var root = JsonParser.Parse(text);
        _undo.Push(Text);
        _redo.Clear();
        Text = text;
        Root = root;
        Changed?.Invoke();
    }

    /// <summary>Reload from disk: replaces text and forgets undo history.</summary>
    public void Reset(string text)
    {
        Root = JsonParser.Parse(text);
        Text = text;
        SavedText = text;
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    private void SetTextInternal(string t)
    {
        Root = JsonParser.Parse(t);
        Text = t;
    }

    /// <summary>Runs several edits as one undo step. Rolls back if any of them fails.</summary>
    public void Batch(Action edits)
    {
        var before = Text;
        _batch++;
        try { edits(); }
        catch
        {
            _batch--;
            SetTextInternal(before);
            throw;
        }
        _batch--;
        if (_batch == 0 && Text != before)
        {
            _undo.Push(before);
            _redo.Clear();
            Changed?.Invoke();
        }
    }

    private void Splice(int start, int length, string insert)
    {
        var t = string.Concat(Text.AsSpan(0, start), insert, Text.AsSpan(start + length));
        var root = JsonParser.Parse(t); // throws on a bad edit, leaving the document unchanged
        if (_batch == 0) { _undo.Push(Text); _redo.Clear(); }
        Text = t;
        Root = root;
        if (_batch == 0) Changed?.Invoke();
    }

    // ── Reading ──────────────────────────────────────────────────────────────

    public JNode? Get(params string[] path)
    {
        JNode? n = Root;
        foreach (var k in path)
        {
            if (n == null || !n.IsObject) return null;
            n = n.Get(k);
        }
        return n;
    }

    public string? GetString(params string[] path) => Get(path) is { IsString: true } n ? n.Str : null;

    public string Raw(JNode n) => Text[n.Start..n.End];

    // ── Layout helpers ───────────────────────────────────────────────────────

    private int LineStart(int pos)
    {
        int i = pos > 0 ? Text.LastIndexOf('\n', pos - 1) : -1;
        return i + 1;
    }

    private int Indent(int pos)
    {
        int s = LineStart(pos), i = s;
        while (i < Text.Length && Text[i] == ' ') i++;
        return i - s;
    }

    private static int Count(JNode c) => c.IsObject ? c.Members!.Count : c.Items!.Count;
    private static int ChildStart(JNode c, int i) => c.IsObject ? c.Members![i].KeyStart : c.Items![i].Start;
    private static int ChildEnd(JNode c, int i) => c.IsObject ? c.Members![i].Value.End : c.Items![i].End;

    private bool IsMultiline(JNode c)
    {
        if (Count(c) == 0) return true;
        return Text.AsSpan(c.Start, ChildStart(c, 0) - c.Start).Contains('\n');
    }

    private int ChildIndent(JNode c)
    {
        if (Count(c) > 0 && IsMultiline(c)) return Indent(ChildStart(c, 0));
        return Indent(c.Start) + 2;
    }

    private void InsertChild(JNode c, Func<int, string> childText, int? before)
    {
        int n = Count(c);
        if (n == 0)
        {
            int ci = Indent(c.Start) + 2;
            string open = c.IsObject ? "{" : "[", close = c.IsObject ? "}" : "]";
            Splice(c.Start, c.End - c.Start,
                open + NL + new string(' ', ci) + childText(ci) + NL + new string(' ', Indent(c.Start)) + close);
            return;
        }
        bool ml = IsMultiline(c);
        int ind = ChildIndent(c);
        string sep = ml ? NL + new string(' ', ind) : " ";
        if (before is int b && b < n)
            Splice(ChildStart(c, b), 0, childText(ind) + "," + sep);
        else
            Splice(ChildEnd(c, n - 1), 0, "," + sep + childText(ind));
    }

    private void RemoveChild(JNode c, int i)
    {
        int n = Count(c);
        if (n == 1) Splice(c.Start + 1, c.End - 1 - (c.Start + 1), "");
        else if (i < n - 1) Splice(ChildStart(c, i), ChildStart(c, i + 1) - ChildStart(c, i), "");
        else Splice(ChildEnd(c, i - 1), ChildEnd(c, i) - ChildEnd(c, i - 1), "");
    }

    /// <summary>Index to insert at so a list that is already alphabetical stays alphabetical; null = append.</summary>
    private static int? SortedIndex(IList<string> existing, string key)
    {
        if (existing.Count < 3) return null;
        var cmp = StringComparer.OrdinalIgnoreCase;
        for (int i = 1; i < existing.Count; i++)
            if (cmp.Compare(existing[i - 1], existing[i]) > 0) return null;
        for (int i = 0; i < existing.Count; i++)
            if (cmp.Compare(key, existing[i]) < 0) return i;
        return null;
    }

    // ── Object edits ─────────────────────────────────────────────────────────

    public enum Placement { Auto, Top, End }

    /// <summary>Sets obj[key] = value. Creates missing parent objects. Updates in place if the key exists.</summary>
    public void Set(string[] objPath, string key, JsonNode? value, Placement placement = Placement.Auto)
        => SetRaw(objPath, key, ind => JsonOut.Write(value, ind, NL), placement);

    public void SetRaw(string[] objPath, string key, Func<int, string> valueText, Placement placement = Placement.Auto)
    {
        Batch(() =>
        {
            EnsureObject(objPath);
            var obj = Get(objPath)!;
            int idx = obj.IndexOf(key);
            if (idx >= 0)
            {
                var m = obj.Members![idx];
                var newText = valueText(Indent(m.KeyStart));
                if (Text.AsSpan(m.Value.Start, m.Value.End - m.Value.Start).SequenceEqual(newText)) return;
                Splice(m.Value.Start, m.Value.End - m.Value.Start, newText);
                return;
            }
            int? before = placement switch
            {
                Placement.Top => 0,
                Placement.End => null,
                _ => SortedIndex(obj.Members!.Select(m => m.Key).ToList(), key),
            };
            InsertChild(obj, ind => JsonOut.Quote(key) + ": " + valueText(ind), before);
        });
    }

    /// <summary>Makes sure every object along the path exists (creates empty ones at the end of their parent).</summary>
    public void EnsureObject(string[] path)
    {
        for (int i = 1; i <= path.Length; i++)
        {
            var node = Get(path[..i]);
            if (node == null)
            {
                var parent = Get(path[..(i - 1)])!;
                // New top-level fields go at the end; entries inside a field keep alphabetical order if it has one
                int? before = i == 1 ? null : SortedIndex(parent.Members!.Select(m => m.Key).ToList(), path[i - 1]);
                InsertChild(parent, ind => JsonOut.Quote(path[i - 1]) + ": {}", before);
            }
            else if (!node.IsObject)
                throw new InvalidOperationException($"\"{string.Join(" > ", path[..i])}\" isn't an object in the file.");
        }
    }

    /// <summary>Removes obj[key] (every copy, if duplicated). No-op if missing.</summary>
    public void Remove(string[] objPath, string key)
    {
        Batch(() =>
        {
            while (Get(objPath) is { IsObject: true } obj && obj.IndexOf(key) is var i && i >= 0)
                RemoveChild(obj, i);
        });
    }

    /// <summary>Removes obj[key], then removes obj itself from its parent if that left it empty.</summary>
    public void RemoveAndPrune(string[] objPath, string key)
    {
        Batch(() =>
        {
            Remove(objPath, key);
            if (objPath.Length > 1 && Get(objPath) is { IsObject: true } o && o.Members!.Count == 0)
                Remove(objPath[..^1], objPath[^1]);
        });
    }

    /// <summary>
    /// Renames obj[oldKey] to newKey in place (same position, value text untouched).
    /// If newKey already exists, its old value is replaced by the renamed entry.
    /// </summary>
    public void RenameKey(string[] objPath, string oldKey, string newKey)
    {
        if (oldKey == newKey) return;
        Batch(() =>
        {
            if (Get(objPath) is not { IsObject: true } obj || obj.IndexOf(oldKey) < 0) return;
            if (obj.IndexOf(newKey) >= 0) Remove(objPath, newKey);
            var m = Get(objPath)!.Members![Get(objPath)!.IndexOf(oldKey)];
            Splice(m.KeyStart, m.KeyEnd - m.KeyStart, JsonOut.Quote(newKey));
        });
    }

    // ── String-array edits ───────────────────────────────────────────────────

    public bool ArrayContains(string[] arrPath, string value)
        => Get(arrPath) is { IsArray: true } a && a.Items!.Any(i => i.IsString && i.Str == value);

    /// <summary>Adds a string to an array (creating the array if needed). Keeps alphabetical arrays alphabetical.</summary>
    public void AddToArray(string[] arrPath, string value)
    {
        Batch(() =>
        {
            var arr = Get(arrPath);
            if (arr == null)
            {
                Set(arrPath[..^1], arrPath[^1], new JsonArray(JsonOut.Str(value)), Placement.End);
                return;
            }
            if (!arr.IsArray) throw new InvalidOperationException($"\"{string.Join(" > ", arrPath)}\" isn't a list in the file.");
            if (arr.Items!.Any(i => i.IsString && i.Str == value)) return;
            var names = arr.Items!.Where(i => i.IsString).Select(i => i.Str!).ToList();
            int? before = names.Count == arr.Items!.Count ? SortedIndex(names, value) : null;
            InsertChild(arr, _ => JsonOut.Quote(value), before);
        });
    }

    public void RemoveFromArray(string[] arrPath, string value)
    {
        Batch(() =>
        {
            while (Get(arrPath) is { IsArray: true } arr
                   && arr.Items!.FindIndex(i => i.IsString && i.Str == value) is var i && i >= 0)
                RemoveChild(arr, i);
        });
    }

    public void RenameInArray(string[] arrPath, string oldValue, string newValue)
    {
        Batch(() =>
        {
            if (Get(arrPath) is not { IsArray: true } arr) return;
            var item = arr.Items!.FirstOrDefault(i => i.IsString && i.Str == oldValue);
            if (item == null) return;
            if (arr.Items!.Any(i => i.IsString && i.Str == newValue)) { RemoveFromArray(arrPath, oldValue); return; }
            Splice(item.Start, item.End - item.Start, JsonOut.Quote(newValue));
        });
    }

    /// <summary>Replaces a value's text with raw JSON typed by the user (validated by the re-parse).</summary>
    public void ReplaceRaw(string[] path, string rawJson)
    {
        var n = Get(path) ?? throw new InvalidOperationException("Entry not found.");
        JsonParser.Parse(rawJson); // validate on its own first for a clearer error
        Splice(n.Start, n.End - n.Start, rawJson.Trim());
    }
}
